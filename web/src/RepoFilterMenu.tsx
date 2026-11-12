import { useRef, useState } from 'react';
import type { ReactNode, SetStateAction } from 'react';
import { useDismiss } from './dismiss';
import { useI18n } from './i18n';
import { ChevronDownIcon, FilterIcon } from './icons';
import {
  ACTIVITY_FILTERS,
  activeFilterCount,
  ANY,
  DEFAULT_REPO_FILTERS,
  NO_LANGUAGE,
  ORIGIN_FILTERS,
  STARRED_FILTERS,
  VISIBILITY_FILTERS,
} from './repoview';
import type { RepoFilters } from './repoview';
import type { Facet } from './viewmodel';

interface Props {
  filters: RepoFilters;
  /** Come un setState: accetta anche una funzione, così due scelte vicine non si cancellano a vicenda. */
  onChange: (next: SetStateAction<RepoFilters>) => void;
  /** Le scelte di linguaggio e proprietario presenti nei repository della vista. */
  facets: { languages: Facet[]; owners: Facet[] };
  /** Il filtro «preferiti» serve solo dove ci sono anche repository non preferiti. */
  showStarred: boolean;
}

/** Il pulsante «Filtra» e il pannello con tutti i gruppi di filtri, combinabili tra loro. */
export function RepoFilterMenu({ filters, onChange, facets, showStarred }: Props) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  useDismiss(open, root, (viaEscape) => {
    setOpen(false);
    if (viaEscape) button.current?.focus();
  });

  const active = activeFilterCount(filters);
  const set = <K extends keyof RepoFilters>(key: K, value: RepoFilters[K]) =>
    onChange((current) => ({ ...current, [key]: value }));

  const optionsOf = <T extends string>(prefix: string, values: T[]) =>
    values.map((value) => ({ value, label: t(`${prefix}.${value}` as 'repo.filter.activity.any') }));

  return (
    <div className="dropdown" ref={root}>
      <button
        ref={button}
        className={active > 0 ? 'dd-btn highlighted' : 'dd-btn'}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={t('repo.filter.label')}
        onClick={() => setOpen((current) => !current)}
      >
        <FilterIcon size={15} />
        <span className="dd-text">{active > 0 ? t('repo.filter.buttonActive', { count: active }) : t('repo.filter.button')}</span>
        <ChevronDownIcon size={14} />
      </button>

      {open && (
        <div className="popover" role="dialog" aria-label={t('repo.filter.label')}>
          <Group label={t('repo.filter.group.activity')}>
            <Chips
              label={t('repo.filter.group.activity')}
              value={filters.activity}
              options={optionsOf('repo.filter.activity', ACTIVITY_FILTERS)}
              onChange={(value) => set('activity', value)}
            />
          </Group>
          <Group label={t('repo.filter.group.visibility')}>
            <Chips
              label={t('repo.filter.group.visibility')}
              value={filters.visibility}
              options={optionsOf('repo.filter.visibility', VISIBILITY_FILTERS)}
              onChange={(value) => set('visibility', value)}
            />
          </Group>
          <Group label={t('repo.filter.group.origin')}>
            <Chips
              label={t('repo.filter.group.origin')}
              value={filters.origin}
              options={optionsOf('repo.filter.origin', ORIGIN_FILTERS)}
              onChange={(value) => set('origin', value)}
            />
          </Group>
          {showStarred && (
            <Group label={t('repo.filter.group.starred')}>
              <Chips
                label={t('repo.filter.group.starred')}
                value={filters.starred}
                options={optionsOf('repo.filter.starred', STARRED_FILTERS)}
                onChange={(value) => set('starred', value)}
              />
            </Group>
          )}
          <Group label={t('repo.filter.group.language')} htmlFor="filter-language">
            <FacetSelect
              id="filter-language"
              value={filters.language}
              facets={facets.languages}
              labelOf={(facet) => (facet.value === NO_LANGUAGE ? t('repo.filter.noLanguage') : facet.label)}
              onChange={(value) => set('language', value)}
            />
          </Group>
          <Group label={t('repo.filter.group.owner')} htmlFor="filter-owner">
            <FacetSelect
              id="filter-owner"
              value={filters.owner}
              facets={facets.owners}
              labelOf={(facet) => facet.label}
              onChange={(value) => set('owner', value)}
            />
          </Group>

          <div className="popover-foot">
            <button className="link-btn" disabled={active === 0} onClick={() => onChange(DEFAULT_REPO_FILTERS)}>
              {t('repo.filter.reset')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function Group({ label, htmlFor, children }: { label: string; htmlFor?: string; children: ReactNode }) {
  return (
    <div className="filter-group">
      <label className="filter-label" htmlFor={htmlFor}>
        {label}
      </label>
      {children}
    </div>
  );
}

/** Una scelta esclusiva a pulsanti affiancati. */
function Chips<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onChange: (value: T) => void;
}) {
  return (
    <div className="chip-row" role="radiogroup" aria-label={label}>
      {options.map((option) => (
        <button
          key={option.value}
          role="radio"
          aria-checked={option.value === value}
          className={option.value === value ? 'chip active' : 'chip'}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

/** Un elenco a tendina di scelte con il numero di repository accanto; «Tutti» in cima. */
function FacetSelect({
  id,
  value,
  facets,
  labelOf,
  onChange,
}: {
  id: string;
  value: string;
  facets: Facet[];
  labelOf: (facet: Facet) => string;
  onChange: (value: string) => void;
}) {
  const { t } = useI18n();
  // Se la scelta attuale non c'è più nell'elenco (es. dopo aver cambiato account) resta comunque visibile.
  const shown =
    value === ANY || facets.some((facet) => facet.value === value)
      ? facets
      : [...facets, { value, label: value, count: 0 }];

  return (
    <select id={id} className="select full" value={value} onChange={(e) => onChange(e.target.value)}>
      <option value={ANY}>{t('repo.filter.any')}</option>
      {shown.map((facet) => (
        <option key={facet.value} value={facet.value}>
          {labelOf(facet)} · {facet.count}
        </option>
      ))}
    </select>
  );
}
