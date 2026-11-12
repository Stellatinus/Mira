import { useMemo } from 'react';
import { repoKey } from './attention';
import type { Attention } from './attention';
import type { I18n } from './i18n';
import { compareRepos, isRepoSection, matchesRepoFilter } from './repoview';
import type { RepoFilter, RepoSort } from './repoview';
import type { ViewConfig } from './sections';
import type { Dashboard, DashboardItem, Section } from './types';

interface Total {
  count: number;
  unknown: boolean;
}

/** Gli elementi di ogni sezione della vista già filtrati e ordinati, e i loro conteggi. */
export interface ViewModel {
  /** La ricerca, già in minuscolo e senza spazi ai lati. */
  needle: string;
  bySection: Map<Section, DashboardItem[]>;
  /** Il filtro dei repository restringe l'elenco di questa sezione. */
  narrowed: (key: Section) => boolean;
  /** Vero se ci sono più elementi su GitHub di quelli scaricati. */
  truncated: (key: Section) => boolean;
  /** Il totale reale di GitHub, con «+» se non è noto per qualche account. */
  totalLabel: (key: Section) => string;
  /** Il numero di una sezione come lo si scrive: «65», «14 di 65» o «100 di 191». */
  countText: (key: Section) => string;
}

export const matchesQuery = (item: DashboardItem, needle: string): boolean =>
  [item.title, item.repo, item.author, item.description, item.language].some((field) =>
    field?.toLowerCase().includes(needle),
  );

interface Args {
  data: Dashboard | null;
  view: ViewConfig;
  /** Login dell'account scelto, oppure "all". */
  filter: string;
  query: string;
  attention: Map<string, Attention>;
  repoSort: RepoSort;
  repoFilter: RepoFilter;
  t: I18n['t'];
}

export function useViewModel({ data, view, filter, query, attention, repoSort, repoFilter, t }: Args): ViewModel {
  return useMemo(() => {
    const needle = query.trim().toLowerCase();
    const items = data?.items ?? [];
    // Il filtro dei repository restringe l'elenco: i totali di GitHub non valgono più.
    const narrowed = (key: Section) => isRepoSection(key) && repoFilter !== 'all';
    const attentionOf = (item: DashboardItem) => attention.get(repoKey(item.repo));

    const bySection = new Map<Section, DashboardItem[]>(view.sections.map((key) => [key, []]));
    // Quanti repository ci sono prima del filtro dei repository, per scrivere «14 di 65».
    const repoBase = new Map<Section, number>();

    for (const item of items) {
      if (filter !== 'all' && item.account !== filter) continue;
      if (needle && !matchesQuery(item, needle)) continue;
      if (isRepoSection(item.section)) {
        repoBase.set(item.section, (repoBase.get(item.section) ?? 0) + 1);
        if (!matchesRepoFilter(item, repoFilter, attentionOf(item))) continue;
      }
      bySection.get(item.section)?.push(item);
    }
    for (const [key, group] of bySection) {
      if (isRepoSection(key)) group.sort(compareRepos(repoSort, attentionOf));
    }

    // Totale reale per sezione (anche oltre gli elementi scaricati). Se per almeno un account
    // GitHub non lo indica, si mostra "N+". Con una ricerca o un filtro contano solo gli elementi trovati.
    const totals = new Map<Section, Total>();
    for (const key of view.sections) {
      const shown = bySection.get(key)?.length ?? 0;
      let count = 0;
      let unknown = false;
      let found = false;
      for (const total of data?.totals ?? []) {
        if (total.section !== key || (filter !== 'all' && total.account !== filter)) continue;
        found = true;
        if (total.total === null) unknown = true;
        else count += total.total;
      }
      const exact = !found || needle !== '' || narrowed(key);
      totals.set(key, exact ? { count: shown, unknown: false } : { count: Math.max(count, shown), unknown });
    }

    const shownOf = (key: Section) => bySection.get(key)?.length ?? 0;
    const truncated = (key: Section) => {
      const total = totals.get(key);
      return total !== undefined && (total.unknown || total.count > shownOf(key));
    };
    const totalLabel = (key: Section) => {
      const total = totals.get(key);
      if (!total) return '0';
      return total.unknown ? `${total.count}+` : String(total.count);
    };
    const countText = (key: Section) => {
      const shown = shownOf(key);
      if (narrowed(key)) return t('items.countOf', { shown, total: repoBase.get(key) ?? shown });
      if (truncated(key)) return t('items.countOf', { shown, total: totalLabel(key) });
      return String(shown);
    };

    return { needle, bySection, narrowed, truncated, totalLabel, countText };
  }, [data, view, filter, query, attention, repoSort, repoFilter, t]);
}
