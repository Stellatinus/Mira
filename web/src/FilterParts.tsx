import type { ReactNode } from 'react';
import { useI18n } from './i18n';
import { RadioGroup } from './RadioGroup';
import { ANY } from './repoview';
import type { Facet } from './viewmodel';

export function Group({ label, htmlFor, children }: { label: string; htmlFor?: string; children: ReactNode }) {
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
export function Chips<T extends string>({
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
  return <RadioGroup className="chip-row" itemClass="chip" label={label} value={value} options={options} onChange={onChange} />;
}

/** Un elenco a tendina di scelte con il numero di repository accanto; «Tutti» in cima. */
export function FacetSelect({
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
