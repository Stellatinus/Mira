import { ciState } from './checks';
import { ANY, ownerOf } from './repoview';
import type { ViewKey } from './sections';
import type { DashboardItem } from './types';

/**
 * Filtri e ordinamento degli elementi (issue, PR, notifiche, menzioni). Funzionano come quelli dei
 * repository: ogni gruppo è a scelta singola e i gruppi si combinano tutti insieme.
 */
export type TypeFilter = 'all' | 'issue' | 'pr';
export const TYPE_FILTERS: TypeFilter[] = ['all', 'issue', 'pr'];

export type DraftFilter = 'all' | 'only' | 'none';
export const DRAFT_FILTERS: DraftFilter[] = ['all', 'only', 'none'];

export type CommentsFilter = 'all' | 'with' | 'without';
export const COMMENTS_FILTERS: CommentsFilter[] = ['all', 'with', 'without'];

/** Come una issue è assegnata, rispetto ai tuoi account: a te, ad altri, o a nessuno. */
export type AssignmentFilter = 'all' | 'mine' | 'others' | 'none';
export const ASSIGNMENT_FILTERS: AssignmentFilter[] = ['all', 'mine', 'others', 'none'];

/** Solo le PR con la CI rossa (fallita) o tutte. */
export type CiFilter = 'all' | 'failing';
export const CI_FILTERS: CiFilter[] = ['all', 'failing'];

export interface ItemFilters {
  /** «Assegnate a te», «ad altri» e «non assegnate» dividono le issue senza resto. */
  assignment: AssignmentFilter;
  /** Un assegnatario preciso (login in minuscolo), o ANY. */
  assignee: string;
  type: TypeFilter;
  /** Vale per le PR: «solo bozze» esclude le issue, «senza bozze» le tiene. */
  draft: DraftFilter;
  comments: CommentsFilter;
  /** «Solo CI rossa»: le PR con almeno un controllo fallito. */
  ci: CiFilter;
  /** Repository («owner/nome» in minuscolo), o ANY. */
  repo: string;
  /** Proprietario del repository (in minuscolo), o ANY. */
  owner: string;
  /** Motivo di una notifica (es. "mention"), o ANY. */
  reason: string;
}

export const DEFAULT_ITEM_FILTERS: ItemFilters = {
  assignment: 'all',
  assignee: ANY,
  type: 'all',
  draft: 'all',
  comments: 'all',
  ci: 'all',
  repo: ANY,
  owner: ANY,
  reason: ANY,
};

/** Quanti gruppi di filtri sono attivi (cioè diversi dal predefinito). */
export function activeItemFilterCount(filters: ItemFilters): number {
  return (Object.keys(DEFAULT_ITEM_FILTERS) as (keyof ItemFilters)[]).filter(
    (key) => filters[key] !== DEFAULT_ITEM_FILTERS[key],
  ).length;
}

/**
 * I filtri con cui si apre una vista: in «Issue» si parte da «Assegnate a te» (le issue su cui devi lavorare);
 * in tutte le altre da nessun filtro.
 */
export function defaultItemFilters(view: ViewKey): ItemFilters {
  return view === 'issues' ? { ...DEFAULT_ITEM_FILTERS, assignment: 'mine' } : DEFAULT_ITEM_FILTERS;
}

/** `mine`: i login (in minuscolo) degli account in vista, per capire cosa è assegnato «a te». */
export function matchesItemFilters(item: DashboardItem, filters: ItemFilters, mine: ReadonlySet<string>): boolean {
  const assignees = (item.assignees ?? []).map((a) => a.toLowerCase());
  if (filters.assignment === 'none' && assignees.length > 0) return false;
  if (filters.assignment === 'mine' && !assignees.some((a) => mine.has(a))) return false;
  if (filters.assignment === 'others' && !(assignees.length > 0 && !assignees.some((a) => mine.has(a)))) return false;
  if (filters.assignee !== ANY && !assignees.includes(filters.assignee)) return false;
  if (filters.type === 'pr' && item.type !== 'pr') return false;
  if (filters.type === 'issue' && item.type !== 'issue') return false;
  if (filters.draft === 'only' && !item.draft) return false;
  if (filters.draft === 'none' && item.draft) return false;
  if (filters.comments === 'with' && item.comments === 0) return false;
  if (filters.comments === 'without' && item.comments > 0) return false;
  if (filters.ci === 'failing' && ciState(item.checks) !== 'failure') return false;
  if (filters.repo !== ANY && item.repo.toLowerCase() !== filters.repo) return false;
  if (filters.owner !== ANY && ownerOf(item).toLowerCase() !== filters.owner) return false;
  if (filters.reason !== ANY && item.reason !== filters.reason) return false;
  return true;
}

export type ItemSort = 'updated' | 'oldest' | 'title' | 'repo' | 'comments';
export const ITEM_SORTS: ItemSort[] = ['updated', 'oldest', 'title', 'repo', 'comments'];
export const DEFAULT_ITEM_SORT: ItemSort = 'updated';

/** Confronto per ordinare gli elementi; a parità si tiene il più recente per primo. */
export function compareItems(sort: ItemSort): (a: DashboardItem, b: DashboardItem) => number {
  const recent = (a: DashboardItem, b: DashboardItem) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt);
  const text = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'base' });

  switch (sort) {
    case 'oldest':
      return (a, b) => -recent(a, b);
    case 'title':
      return (a, b) => text(a.title, b.title) || recent(a, b);
    case 'repo':
      return (a, b) => text(a.repo, b.repo) || recent(a, b);
    case 'comments':
      return (a, b) => b.comments - a.comments || recent(a, b);
    default:
      return recent;
  }
}
