import { indicator } from './attention';
import type { Attention } from './attention';
import type { DashboardItem, Section } from './types';

/** Le sezioni fatte di repository, a cui si applicano ordinamento e filtri. */
export const REPO_SECTIONS: Section[] = ['repos', 'starred'];
export const isRepoSection = (section: Section) => REPO_SECTIONS.includes(section);

export type RepoSort = 'updated' | 'name' | 'stars' | 'prs' | 'issues' | 'attention';
export const REPO_SORTS: RepoSort[] = ['updated', 'name', 'stars', 'prs', 'issues', 'attention'];
export const DEFAULT_REPO_SORT: RepoSort = 'updated';

export type RepoFilter = 'all' | 'prs' | 'issues' | 'needs';
export const REPO_FILTERS: RepoFilter[] = ['all', 'prs', 'issues', 'needs'];

/** Quanti elementi (PR e issue insieme) aspettano te in un repository. */
const needsOf = (attention?: Attention) => (attention?.pr.needs ?? 0) + (attention?.issue.needs ?? 0);

/** I numeri degli indicatori, gli stessi che si vedono sulla card: filtri e ordine ne seguono il valore. */
const prsOf = (item: DashboardItem, attention?: Attention) => indicator(item, attention, 'pr').count;
const issuesOf = (item: DashboardItem, attention?: Attention) => indicator(item, attention, 'issue').count;

export function matchesRepoFilter(item: DashboardItem, filter: RepoFilter, attention?: Attention): boolean {
  switch (filter) {
    case 'prs':
      return prsOf(item, attention) > 0;
    case 'issues':
      return issuesOf(item, attention) > 0;
    case 'needs':
      return needsOf(attention) > 0;
    default:
      return true;
  }
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
