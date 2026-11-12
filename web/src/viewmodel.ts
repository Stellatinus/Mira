import { useMemo } from 'react';
import { repoKey } from './attention';
import type { Attention } from './attention';
import type { I18n } from './i18n';
import { activeItemFilterCount, compareItems, matchesItemFilters } from './itemview';
import type { ItemFilters, ItemSort } from './itemview';
import { activeFilterCount, compareRepos, isRepoSection, matchesRepoFilters, NO_LANGUAGE, ownerOf } from './repoview';
import type { RepoFilters, RepoSort } from './repoview';
import type { ViewConfig } from './sections';
import type { Dashboard, DashboardItem, Section } from './types';

interface Total {
  count: number;
  unknown: boolean;
}

/** Una scelta dei filtri a elenco (linguaggio o proprietario), con quanti repository la hanno. */
export interface Facet {
  value: string;
  label: string;
  count: number;
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
  /** Le scelte dei filtri a elenco, ricavate dai repository della vista (prima dei filtri dei repository). */
  facets: { languages: Facet[]; owners: Facet[] };
  /** Come `facets`, per i filtri degli elementi (repository, proprietario e motivo delle notifiche). */
  itemFacets: { repos: Facet[]; owners: Facet[]; reasons: Facet[] };
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
  repoFilters: RepoFilters;
  itemSort: ItemSort;
  itemFilters: ItemFilters;
  t: I18n['t'];
}

export function useViewModel({
  data,
  view,
  filter,
  query,
  attention,
  repoSort,
  repoFilters,
  itemSort,
  itemFilters,
  t,
}: Args): ViewModel {
  return useMemo(() => {
    const needle = query.trim().toLowerCase();
    const items = data?.items ?? [];
    // Il filtro dei repository restringe l'elenco: i totali di GitHub non valgono più.
    const repoFiltering = activeFilterCount(repoFilters) > 0;
    const itemFiltering = activeItemFilterCount(itemFilters) > 0;
    const narrowed = (key: Section) => (isRepoSection(key) ? repoFiltering : itemFiltering);
    const attentionOf = (item: DashboardItem) => attention.get(repoKey(item.repo));

    // Quali repository sono tra i preferiti di quale account (per il filtro «preferiti»).
    const starredKeys = new Set(items.filter((i) => i.section === 'starred').map((i) => `${i.account}|${i.repo}`));
    const languages = new Map<string, Facet>();
    const owners = new Map<string, Facet>();
    const itemRepos = new Map<string, Facet>();
    const itemOwners = new Map<string, Facet>();
    const itemReasons = new Map<string, Facet>();
    const count = (map: Map<string, Facet>, value: string, label: string) => {
      const found = map.get(value);
      if (found) found.count += 1;
      else map.set(value, { value, label, count: 1 });
    };

    const bySection = new Map<Section, DashboardItem[]>(view.sections.map((key) => [key, []]));
    // Quanti elementi ci sono prima dei filtri, per scrivere «14 di 65».
    const base = new Map<Section, number>();

    for (const item of items) {
      if (filter !== 'all' && item.account !== filter) continue;
      if (needle && !matchesQuery(item, needle)) continue;
      base.set(item.section, (base.get(item.section) ?? 0) + 1);
      if (isRepoSection(item.section)) {
        if (view.sections.includes(item.section)) {
          count(languages, item.language ?? NO_LANGUAGE, item.language ?? '');
          count(owners, ownerOf(item).toLowerCase(), ownerOf(item));
        }
        if (!matchesRepoFilters(item, repoFilters, attentionOf(item), starredKeys.has(`${item.account}|${item.repo}`))) continue;
      } else {
        if (view.sections.includes(item.section)) {
          count(itemRepos, item.repo.toLowerCase(), item.repo);
          count(itemOwners, ownerOf(item).toLowerCase(), ownerOf(item));
          if (item.reason) count(itemReasons, item.reason, item.reason);
        }
        if (!matchesItemFilters(item, itemFilters)) continue;
      }
      bySection.get(item.section)?.push(item);
    }
    for (const [key, group] of bySection) {
      group.sort(isRepoSection(key) ? compareRepos(repoSort, attentionOf) : compareItems(itemSort));
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
      if (narrowed(key)) return t('items.countOf', { shown, total: base.get(key) ?? shown });
      if (truncated(key)) return t('items.countOf', { shown, total: totalLabel(key) });
      return String(shown);
    };

    const byCount = (a: Facet, b: Facet) => b.count - a.count || a.label.localeCompare(b.label, undefined, { sensitivity: 'base' });
    const facets = {
      // «Nessun linguaggio» va in fondo all'elenco.
      languages: [...languages.values()].sort((a, b) => Number(a.value === NO_LANGUAGE) - Number(b.value === NO_LANGUAGE) || byCount(a, b)),
      owners: [...owners.values()].sort(byCount),
    };

    const itemFacets = {
      repos: [...itemRepos.values()].sort(byCount),
      owners: [...itemOwners.values()].sort(byCount),
      reasons: [...itemReasons.values()].sort(byCount),
    };

    return { needle, bySection, narrowed, truncated, totalLabel, countText, facets, itemFacets };
  }, [data, view, filter, query, attention, repoSort, repoFilters, itemSort, itemFilters, t]);
}
