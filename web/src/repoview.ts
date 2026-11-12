import { indicator } from './attention';
import type { Attention } from './attention';
import type { DashboardItem, Section } from './types';

/** Le sezioni fatte di repository, a cui si applicano ordinamento e filtri. */
export const REPO_SECTIONS: Section[] = ['repos', 'starred'];
export const isRepoSection = (section: Section) => REPO_SECTIONS.includes(section);

export type RepoSort = 'updated' | 'name' | 'stars' | 'prs' | 'issues' | 'attention';
export const REPO_SORTS: RepoSort[] = ['updated', 'name', 'stars', 'prs', 'issues', 'attention'];
export const DEFAULT_REPO_SORT: RepoSort = 'updated';

/** Cosa c'è di aperto in un repository. */
export type ActivityFilter = 'any' | 'prs' | 'issues' | 'open' | 'unassigned' | 'needs' | 'none';
export const ACTIVITY_FILTERS: ActivityFilter[] = ['any', 'prs', 'issues', 'open', 'unassigned', 'needs', 'none'];

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
 * I filtri dei repository. Ogni gruppo è a scelta singola e i gruppi si combinano tutti insieme
 * («pubblici» e «con issue non assegnate» e «linguaggio C#»).
 */
export interface RepoFilters {
  activity: ActivityFilter;
  visibility: VisibilityFilter;
  origin: OriginFilter;
  starred: StarredFilter;
  /** Linguaggio principale, o ANY, o NO_LANGUAGE. */
  language: string;
  /** Utente o organizzazione proprietari (in minuscolo), o ANY. */
  owner: string;
}

export const DEFAULT_REPO_FILTERS: RepoFilters = {
  activity: 'any',
  visibility: 'all',
  origin: 'all',
  starred: 'all',
  language: ANY,
  owner: ANY,
};

/** Quanti gruppi di filtri sono attivi (cioè diversi dal predefinito). */
export function activeFilterCount(filters: RepoFilters): number {
  return (Object.keys(DEFAULT_REPO_FILTERS) as (keyof RepoFilters)[]).filter(
    (key) => filters[key] !== DEFAULT_REPO_FILTERS[key],
  ).length;
}

/** Il proprietario di un repository: la parte prima della barra di «owner/nome». */
export const ownerOf = (item: Pick<DashboardItem, 'repo'>) => item.repo.split('/')[0] ?? '';

/** Quanti elementi (PR e issue insieme) aspettano te in un repository. */
const needsOf = (attention?: Attention) => (attention?.pr.needs ?? 0) + (attention?.issue.needs ?? 0);

/** I numeri degli indicatori, gli stessi che si vedono sulla card: filtri e ordine ne seguono il valore. */
const prsOf = (item: DashboardItem, attention?: Attention) => indicator(item, attention, 'pr').count;
const issuesOf = (item: DashboardItem, attention?: Attention) => indicator(item, attention, 'issue').count;

function matchesActivity(item: DashboardItem, activity: ActivityFilter, attention?: Attention): boolean {
  switch (activity) {
    case 'prs':
      return prsOf(item, attention) > 0;
    case 'issues':
      return issuesOf(item, attention) > 0;
    case 'open':
      return prsOf(item, attention) + issuesOf(item, attention) > 0;
    case 'unassigned':
      return item.unassignedIssues > 0;
    case 'needs':
      return needsOf(attention) > 0;
    case 'none':
      return prsOf(item, attention) + issuesOf(item, attention) === 0;
    default:
      return true;
  }
}

/** Vero se il repository passa tutti i filtri attivi. `starred` dice se è tra i preferiti dell'account. */
export function matchesRepoFilters(
  item: DashboardItem,
  filters: RepoFilters,
  attention: Attention | undefined,
  starred: boolean,
): boolean {
  if (!matchesActivity(item, filters.activity, attention)) return false;
  if (filters.visibility === 'public' && item.private) return false;
  if (filters.visibility === 'private' && !item.private) return false;
  if (filters.origin === 'fork' && !item.fork) return false;
  if (filters.origin === 'original' && item.fork) return false;
  if (filters.starred === 'yes' && !starred) return false;
  if (filters.starred === 'no' && starred) return false;
  if (filters.language !== ANY && (item.language ?? NO_LANGUAGE) !== filters.language) return false;
  if (filters.owner !== ANY && ownerOf(item).toLowerCase() !== filters.owner) return false;
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
