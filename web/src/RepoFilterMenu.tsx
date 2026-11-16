import { useRef, useState } from 'react';
import type { SetStateAction } from 'react';
import { useDismiss } from './dismiss';
import { Chips, FacetChecklist, Group } from './FilterParts';
import { useI18n } from './i18n';
import { ChevronDownIcon, FilterIcon } from './icons';
import {
  ACTIVITY_CHOICES,
  ACTIVITY_CONDITIONS,
  ACTIVITY_MODES,
  activeFilterCount,
  activityCount,
  DEFAULT_REPO_FILTERS,
  NO_LANGUAGE,
  ORIGIN_FILTERS,
  STARRED_FILTERS,
  VISIBILITY_FILTERS,
} from './repoview';
import type { ActivityChoice, ActivityCondition, RepoFilters } from './repoview';
import type { Facet } from './viewmodel';

interface Props {
  filters: RepoFilters;
  /** Come un setState: accetta anche una funzione, così due scelte vicine non si cancellano a vicenda. */
  onChange: (next: SetStateAction<RepoFilters>) => void;
  /** Le scelte di linguaggio e proprietario presenti nei repository della vista. */
  facets: { languages: Facet[]; owners: Facet[] };
}

/** Il pulsante «Filtra» e il pannello con tutti i gruppi di filtri, combinabili tra loro. */
export function RepoFilterMenu({ filters, onChange, facets }: Props) {
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

  const setActivity = (condition: ActivityCondition, choice: ActivityChoice) =>
    onChange((current) => ({ ...current, activity: { ...current.activity, [condition]: choice } }));

  const optionsOf = <T extends string>(prefix: string, values: T[]) =>
    values.map((value) => ({ value, label: t(`${prefix}.${value}` as 'repo.filter.visibility.all') }));

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
            <div className="activity-rows">
              {ACTIVITY_CONDITIONS.map((condition) => (
                <div key={condition} className="activity-row">
                  <span className="activity-name">{t(`repo.filter.activity.${condition}` as 'repo.filter.activity.prs')}</span>
                  <Chips
                    label={t(`repo.filter.activity.${condition}` as 'repo.filter.activity.prs')}
                    value={filters.activity[condition]}
                    options={optionsOf('repo.filter.choice', ACTIVITY_CHOICES)}
                    onChange={(value) => setActivity(condition, value)}
                  />
                </div>
              ))}
            </div>
            {activityCount(filters.activity) > 1 && (
              <Chips
                label={t('repo.filter.mode.label')}
                value={filters.activityMode}
                options={optionsOf('repo.filter.mode', ACTIVITY_MODES)}
                onChange={(value) => set('activityMode', value)}
              />
            )}
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
        <Group label={t('repo.filter.group.starred')}>
          <Chips
            label={t('repo.filter.group.starred')}
            value={filters.starred}
            options={optionsOf('repo.filter.starred', STARRED_FILTERS)}
            onChange={(value) => set('starred', value)}
          />
        </Group>
          <Group label={t('repo.filter.group.language')}>
            <FacetChecklist
              label={t('repo.filter.group.language')}
              values={filters.language}
              facets={facets.languages}
              labelOf={(facet) => (facet.value === NO_LANGUAGE ? t('repo.filter.noLanguage') : facet.label)}
              onChange={(value) => set('language', value)}
            />
          </Group>
          <Group label={t('repo.filter.group.owner')}>
            <FacetChecklist
              label={t('repo.filter.group.owner')}
              values={filters.owner}
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
