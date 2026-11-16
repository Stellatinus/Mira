import { indicator } from './attention';
import type { Attention } from './attention';
import type { DashboardItem, Section } from './types';

/** Le sezioni fatte di repository, a cui si applicano ordinamento e filtri. */
export const REPO_SECTIONS: Section[] = ['repos', 'starred'];
export const isRepoSection = (section: Section) => REPO_SECTIONS.includes(section);

export type RepoSort = 'updated' | 'name' | 'stars' | 'prs' | 'issues' | 'attention';
export const REPO_SORTS: RepoSort[] = ['updated', 'name', 'stars', 'prs', 'issues', 'attention'];
export const DEFAULT_REPO_SORT: RepoSort = 'updated';

/** Le condizioni di «Attività»: cosa può esserci di aperto in un repository. */
export type ActivityCondition = 'prs' | 'issues' | 'unassigned' | 'needs';
export const ACTIVITY_CONDITIONS: ActivityCondition[] = ['prs', 'issues', 'unassigned', 'needs'];

/** Per ogni condizione: ignorata (`off`), il repository deve averla (`with`) o non averla (`without`). */
export type ActivityChoice = 'off' | 'with' | 'without';
export const ACTIVITY_CHOICES: ActivityChoice[] = ['off', 'with', 'without'];

/** Come si combinano le condizioni scelte: tutte insieme (E) o almeno una (O). */
export type ActivityMode = 'all' | 'any';
export const ACTIVITY_MODES: ActivityMode[] = ['all', 'any'];

export type VisibilityFilter = 'all' | 'public' | 'private';
export const VISIBILITY_FILTERS: VisibilityFilter[] = ['all', 'public', 'private'];

export type OriginFilter = 'all' | 'original' | 'fork';
export const ORIGIN_FILTERS: OriginFilter[] = ['all', 'original', 'fork'];

export type StarredFilter = 'all' | 'yes' | 'no';
export const STARRED_FILTERS: StarredFilter[] = ['all', 'yes', 'no'];

/** Il valore «nessun vincolo» dei filtri a elenco (linguaggio e proprietario). */
export const ANY = 'all';
/** Il valore del filtro linguaggio per i repository senza un linguaggio. */
export const NO_LANGUAGE = '(nessuno)';

/**
 * I filtri dei repository. Tra gruppi diversi vale sempre «E». Visibilità e provenienza sono a scelta
 * singola; linguaggio e proprietario ammettono più voci (valgono come «O»); le condizioni di
 * «Attività» si combinano con «Tutte» (E) o «Almeno una» (O). Vedi docs/REGOLE.md.
 */
export interface RepoFilters {
  activity: Record<ActivityCondition, ActivityChoice>;
  activityMode: ActivityMode;
  visibility: VisibilityFilter;
  origin: OriginFilter;
  starred: StarredFilter;
  /** Linguaggi principali scelti (anche NO_LANGUAGE); vuoto = nessun vincolo. */
  language: string[];
  /** Utenti o organizzazioni proprietari scelti (in minuscolo); vuoto = nessun vincolo. */
  owner: string[];
}

export const DEFAULT_ACTIVITY: Record<ActivityCondition, ActivityChoice> = {
  prs: 'off',
  issues: 'off',
  unassigned: 'off',
  needs: 'off',
};

export const DEFAULT_REPO_FILTERS: RepoFilters = {
  activity: DEFAULT_ACTIVITY,
  activityMode: 'all',
  visibility: 'all',
  origin: 'all',
  starred: 'all',
  language: [],
  owner: [],
};

/** Quante condizioni di «Attività» sono scelte. */
export const activityCount = (activity: RepoFilters['activity']) =>
  ACTIVITY_CONDITIONS.filter((c) => activity[c] !== 'off').length;

/** Quanti gruppi di filtri sono attivi (cioè diversi dal predefinito); «Attività» conta come un gruppo. */
export function activeFilterCount(filters: RepoFilters): number {
  return (
    (activityCount(filters.activity) > 0 ? 1 : 0) +
    (filters.visibility !== 'all' ? 1 : 0) +
    (filters.origin !== 'all' ? 1 : 0) +
    (filters.starred !== 'all' ? 1 : 0) +
    (filters.language.length > 0 ? 1 : 0) +
    (filters.owner.length > 0 ? 1 : 0)
  );
}

/** Il proprietario di un repository: la parte prima della barra di «owner/nome». */
export const ownerOf = (item: Pick<DashboardItem, 'repo'>) => item.repo.split('/')[0] ?? '';

/** Quanti elementi (PR e issue insieme) aspettano te in un repository. */
const needsOf = (attention?: Attention) => (attention?.pr.needs ?? 0) + (attention?.issue.needs ?? 0);

/** I numeri degli indicatori, gli stessi che si vedono sulla card: filtri e ordine ne seguono il valore. */
const prsOf = (item: DashboardItem, attention?: Attention) => indicator(item, attention, 'pr').count;
const issuesOf = (item: DashboardItem, attention?: Attention) => indicator(item, attention, 'issue').count;

function hasCondition(item: DashboardItem, condition: ActivityCondition, attention?: Attention): boolean {
  switch (condition) {
    case 'prs':
      return prsOf(item, attention) > 0;
    case 'issues':
      return issuesOf(item, attention) > 0;
    case 'unassigned':
      return item.unassignedIssues > 0;
    case 'needs':
      return needsOf(attention) > 0;
  }
}

/** Le condizioni scelte si combinano con E («Tutte») o O («Almeno una»); «senza» nega la singola condizione. */
function matchesActivity(
  item: DashboardItem,
  filters: Pick<RepoFilters, 'activity' | 'activityMode'>,
  attention?: Attention,
): boolean {
  const chosen = ACTIVITY_CONDITIONS.filter((c) => filters.activity[c] !== 'off');
  if (chosen.length === 0) return true;
  const results = chosen.map((c) => hasCondition(item, c, attention) === (filters.activity[c] === 'with'));
  return filters.activityMode === 'any' ? results.some(Boolean) : results.every(Boolean);
}

/** Vero se il repository passa tutti i filtri attivi. `starred` dice se è tra i preferiti dell'account. */
export function matchesRepoFilters(
  item: DashboardItem,
  filters: RepoFilters,
  attention: Attention | undefined,
  starred: boolean,
): boolean {
  if (!matchesActivity(item, filters, attention)) return false;
  if (filters.visibility === 'public' && item.private) return false;
  if (filters.visibility === 'private' && !item.private) return false;
  if (filters.origin === 'fork' && !item.fork) return false;
  if (filters.origin === 'original' && item.fork) return false;
  if (filters.starred === 'yes' && !starred) return false;
  if (filters.starred === 'no' && starred) return false;
  if (filters.language.length > 0 && !filters.language.includes(item.language ?? NO_LANGUAGE)) return false;
  if (filters.owner.length > 0 && !filters.owner.includes(ownerOf(item).toLowerCase())) return false;
  return true;
}

/** Confronto per ordinare i repository; a parità si tiene il più recente per primo. */
export function compareRepos(
  sort: RepoSort,
  attentionOf: (item: DashboardItem) => Attention | undefined,
): (a: DashboardItem, b: DashboardItem) => number {
  const recent = (a: DashboardItem, b: DashboardItem) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt);

  switch (sort) {
    case 'name':
      return (a, b) => a.title.localeCompare(b.title, undefined, { sensitivity: 'base' }) || recent(a, b);
    case 'stars':
      return (a, b) => b.stars - a.stars || recent(a, b);
    case 'prs':
      return (a, b) => prsOf(b, attentionOf(b)) - prsOf(a, attentionOf(a)) || recent(a, b);
    case 'issues':
      return (a, b) => issuesOf(b, attentionOf(b)) - issuesOf(a, attentionOf(a)) || recent(a, b);
    case 'attention':
      return (a, b) =>
        needsOf(attentionOf(b)) - needsOf(attentionOf(a)) ||
        prsOf(b, attentionOf(b)) + issuesOf(b, attentionOf(b)) - (prsOf(a, attentionOf(a)) + issuesOf(a, attentionOf(a))) ||
        recent(a, b);
    default:
      return recent;
  }
}
