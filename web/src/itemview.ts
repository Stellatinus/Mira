import { ANY, ownerOf } from './repoview';
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

export interface ItemFilters {
  type: TypeFilter;
  /** Vale per le PR: «solo bozze» esclude le issue, «senza bozze» le tiene. */
  draft: DraftFilter;
  comments: CommentsFilter;
  /** Repository («owner/nome» in minuscolo), o ANY. */
  repo: string;
  /** Proprietario del repository (in minuscolo), o ANY. */
  owner: string;
  /** Motivo di una notifica (es. "mention"), o ANY. */
  reason: string;
}

export const DEFAULT_ITEM_FILTERS: ItemFilters = {
  type: 'all',
  draft: 'all',
  comments: 'all',
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

export function matchesItemFilters(item: DashboardItem, filters: ItemFilters): boolean {
  if (filters.type === 'pr' && item.type !== 'pr') return false;
  if (filters.type === 'issue' && item.type !== 'issue') return false;
  if (filters.draft === 'only' && !item.draft) return false;
  if (filters.draft === 'none' && item.draft) return false;
  if (filters.comments === 'with' && item.comments === 0) return false;
  if (filters.comments === 'without' && item.comments > 0) return false;
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
