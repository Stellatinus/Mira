import { useRef, useState } from 'react';
import type { SetStateAction } from 'react';
import { useDismiss } from './dismiss';
import { Chips, FacetSelect, Group } from './FilterParts';
import { useI18n } from './i18n';
import { ChevronDownIcon, FilterIcon } from './icons';
import { reasonLabel } from './labels';
import {
  activeItemFilterCount,
  COMMENTS_FILTERS,
  DEFAULT_ITEM_FILTERS,
  DRAFT_FILTERS,
  TYPE_FILTERS,
} from './itemview';
import type { ItemFilters } from './itemview';
import type { Facet } from './viewmodel';

interface Props {
  filters: ItemFilters;
  /** Come un setState: accetta anche una funzione, così due scelte vicine non si cancellano a vicenda. */
  onChange: (next: SetStateAction<ItemFilters>) => void;
  /** Le scelte di repository, proprietario e motivo presenti negli elementi della vista. */
  facets: { repos: Facet[]; owners: Facet[]; reasons: Facet[] };
}

/** Il pulsante «Filtra» per issue, PR e notifiche, con i gruppi di filtri combinabili tra loro. */
export function ItemFilterMenu({ filters, onChange, facets }: Props) {
  const i18n = useI18n();
  const { t } = i18n;
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  useDismiss(open, root, (viaEscape) => {
    setOpen(false);
    if (viaEscape) button.current?.focus();
  });

  const active = activeItemFilterCount(filters);
  const set = <K extends keyof ItemFilters>(key: K, value: ItemFilters[K]) =>
    onChange((current) => ({ ...current, [key]: value }));

  const optionsOf = <T extends string>(prefix: string, values: T[]) =>
    values.map((value) => ({ value, label: t(`${prefix}.${value}` as 'item.filter.type.all') }));

  return (
    <div className="dropdown" ref={root}>
      <button
        ref={button}
        className={active > 0 ? 'dd-btn highlighted' : 'dd-btn'}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={t('item.filter.label')}
        onClick={() => setOpen((current) => !current)}
      >
        <FilterIcon size={15} />
        <span className="dd-text">{active > 0 ? t('repo.filter.buttonActive', { count: active }) : t('repo.filter.button')}</span>
        <ChevronDownIcon size={14} />
      </button>

      {open && (
        <div className="popover" role="dialog" aria-label={t('item.filter.label')}>
          <Group label={t('item.filter.group.type')}>
            <Chips
              label={t('item.filter.group.type')}
              value={filters.type}
              options={optionsOf('item.filter.type', TYPE_FILTERS)}
              onChange={(value) => set('type', value)}
            />
          </Group>
          <Group label={t('item.filter.group.draft')}>
            <Chips
              label={t('item.filter.group.draft')}
              value={filters.draft}
              options={optionsOf('item.filter.draft', DRAFT_FILTERS)}
              onChange={(value) => set('draft', value)}
            />
          </Group>
          <Group label={t('item.filter.group.comments')}>
            <Chips
              label={t('item.filter.group.comments')}
              value={filters.comments}
              options={optionsOf('item.filter.comments', COMMENTS_FILTERS)}
              onChange={(value) => set('comments', value)}
            />
          </Group>
          {facets.reasons.length > 0 && (
            <Group label={t('item.filter.group.reason')} htmlFor="filter-reason">
              <FacetSelect
                id="filter-reason"
                value={filters.reason}
                facets={facets.reasons}
                labelOf={(facet) => reasonLabel(facet.value, i18n)}
                onChange={(value) => set('reason', value)}
              />
            </Group>
          )}
          <Group label={t('item.filter.group.repo')} htmlFor="filter-item-repo">
            <FacetSelect
              id="filter-item-repo"
              value={filters.repo}
              facets={facets.repos}
              labelOf={(facet) => facet.label}
              onChange={(value) => set('repo', value)}
            />
          </Group>
          <Group label={t('repo.filter.group.owner')} htmlFor="filter-item-owner">
            <FacetSelect
              id="filter-item-owner"
              value={filters.owner}
              facets={facets.owners}
              labelOf={(facet) => facet.label}
              onChange={(value) => set('owner', value)}
            />
          </Group>

          <div className="popover-foot">
            <button className="link-btn" disabled={active === 0} onClick={() => onChange(DEFAULT_ITEM_FILTERS)}>
              {t('repo.filter.reset')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
