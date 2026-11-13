import { useRef } from 'react';
import type { KeyboardEvent } from 'react';

export interface RadioOption<T extends string> {
  value: T;
  label: string;
}

interface Props<T extends string> {
  options: RadioOption<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Nome del gruppo, oppure l'id dell'elemento che lo contiene (`labelledBy`). */
  label?: string;
  labelledBy?: string;
  className: string;
  /** Classe di ogni voce; quella scelta ha anche `active`. */
  itemClass: string;
}

/**
 * Scelta esclusiva a pulsanti affiancati, con il comportamento del gruppo di radio (WAI-ARIA APG):
 * Tab entra nel gruppo sulla voce scelta e ne esce alla successiva, le frecce spostano e scelgono
 * (con giro dall'ultima alla prima), Home e Fine vanno alla prima e all'ultima.
 */
export function RadioGroup<T extends string>({ options, value, onChange, label, labelledBy, className, itemClass }: Props<T>) {
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const selected = options.findIndex((option) => option.value === value);
  // Se nessuna voce è scelta, ci si entra comunque dalla prima.
  const entry = selected >= 0 ? selected : 0;

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const last = options.length - 1;
    let next: number;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        next = index === last ? 0 : index + 1;
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        next = index === 0 ? last : index - 1;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = last;
        break;
      default:
        return;
    }
    event.preventDefault();
    onChange(options[next].value);
    buttons.current[next]?.focus();
  }

  return (
    <div className={className} role="radiogroup" aria-label={label} aria-labelledby={labelledBy}>
      {options.map((option, index) => (
        <button
          key={option.value}
          ref={(node) => {
            buttons.current[index] = node;
          }}
          type="button"
          role="radio"
          aria-checked={option.value === value}
          tabIndex={index === entry ? 0 : -1}
          className={option.value === value ? `${itemClass} active` : itemClass}
          onClick={() => onChange(option.value)}
          onKeyDown={(event) => onKeyDown(event, index)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
