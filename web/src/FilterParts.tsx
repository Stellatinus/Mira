import type { ReactNode } from 'react';
import { RadioGroup } from './RadioGroup';
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

/** Una scelta multipla: caselle da spuntare, con il numero di elementi accanto. Più voci valgono come «O». */
export function FacetChecklist({
  label,
  values,
  facets,
  labelOf,
  onChange,
}: {
  label: string;
  values: string[];
  facets: Facet[];
  labelOf: (facet: Facet) => string;
  onChange: (values: string[]) => void;
}) {
  // Se una scelta attuale non c'è più nell'elenco (es. dopo aver cambiato account) resta comunque visibile.
  const missing = values.filter((v) => !facets.some((f) => f.value === v)).map((v) => ({ value: v, label: v, count: 0 }));
  const toggle = (value: string) =>
    onChange(values.includes(value) ? values.filter((v) => v !== value) : [...values, value]);

  return (
    <div className="check-list" role="group" aria-label={label}>
      {[...facets, ...missing].map((facet) => (
        <label key={facet.value} className="check-row">
          <input type="checkbox" checked={values.includes(facet.value)} onChange={() => toggle(facet.value)} />
          <span className="check-text">{labelOf(facet)}</span>
          <span className="check-count">{facet.count}</span>
        </label>
      ))}
    </div>
  );
}
