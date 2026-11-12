import { useEffect, useRef, useState } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
import { CheckIcon, ChevronDownIcon } from './icons';

export interface DropdownOption<T extends string> {
  value: T;
  label: string;
}

interface Props<T extends string> {
  /** Nome del menu per i lettori di schermo («Filtra i repository»). */
  label: string;
  icon: ReactNode;
  /** Testo del pulsante, di solito la scelta attuale. */
  text: string;
  value: T;
  options: DropdownOption<T>[];
  onChange: (value: T) => void;
  /** Evidenzia il pulsante (es. un filtro attivo). */
  highlighted?: boolean;
}

/** Un pulsante che apre un menu di scelte esclusive; si chiude con Esc, cliccando fuori o scegliendo. */
export function Dropdown<T extends string>({ label, icon, text, value, options, onChange, highlighted = false }: Props<T>) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointer = (event: MouseEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
        button.current?.focus();
      }
    };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    // Aprendo il menu il focus va sulla scelta attuale, così si naviga con le frecce.
    menu.current?.querySelector<HTMLElement>('[aria-checked="true"]')?.focus();
    return () => {
      document.removeEventListener('mousedown', onPointer);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  function onMenuKey(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    event.preventDefault();
    const items = [...(menu.current?.querySelectorAll<HTMLElement>('[role="menuitemradio"]') ?? [])];
    const index = items.indexOf(document.activeElement as HTMLElement);
    const next = event.key === 'ArrowDown' ? index + 1 : index - 1;
    items[(next + items.length) % items.length]?.focus();
  }

  return (
    <div className="dropdown" ref={root}>
      <button
        ref={button}
        className={highlighted ? 'dd-btn highlighted' : 'dd-btn'}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={label}
        onClick={() => setOpen((current) => !current)}
      >
        {icon}
        <span className="dd-text">{text}</span>
        <ChevronDownIcon size={14} />
      </button>
      {open && (
        <div className="dd-menu" role="menu" aria-label={label} ref={menu} onKeyDown={onMenuKey}>
          {options.map((option) => (
            <button
              key={option.value}
              role="menuitemradio"
              aria-checked={option.value === value}
              className={option.value === value ? 'dd-item active' : 'dd-item'}
              onClick={() => {
                onChange(option.value);
                setOpen(false);
                button.current?.focus();
              }}
            >
              <span className="dd-check">{option.value === value && <CheckIcon size={14} />}</span>
              {option.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
