import type { Section } from './types';

/** Le preferenze della Home: l'ordine delle sezioni, quali nascondere e quante card mostrare prima di «Mostra tutti». */
export interface HomePrefs {
  order: Section[];
  hidden: Section[];
  collapsed: number;
}

/** Quante card si possono scegliere di mostrare prima di «Mostra tutti». */
export const COLLAPSED_CHOICES = [3, 6, 9, 12, 24];
export const DEFAULT_COLLAPSED = 6;

export const defaultHomePrefs = (available: Section[]): HomePrefs => ({ order: [...available], hidden: [], collapsed: DEFAULT_COLLAPSED });

/**
 * Rende valide delle preferenze lette dal browser (che possono essere vecchie, rovinate o di una versione con altre
 * sezioni): l'ordine contiene solo sezioni conosciute, senza doppioni, e in fondo quelle nuove; non si nasconde tutto;
 * il numero di card è uno di quelli ammessi.
 */
export function normalizeHomePrefs(raw: unknown, available: Section[]): HomePrefs {
  const defaults = defaultHomePrefs(available);
  if (typeof raw !== 'object' || raw === null) return defaults;
  const value = raw as Partial<Record<keyof HomePrefs, unknown>>;

  const known = (list: unknown): Section[] =>
    Array.isArray(list) ? list.filter((s, i): s is Section => available.includes(s as Section) && list.indexOf(s) === i) : [];

  const order = [...known(value.order), ...available.filter((s) => !known(value.order).includes(s))];
  let hidden = known(value.hidden);
  // Una Home senza nessuna sezione non avrebbe senso: se è tutto nascosto si riparte dalle sezioni mostrate di default.
  if (order.every((s) => hidden.includes(s))) hidden = [];
  const collapsed = COLLAPSED_CHOICES.includes(value.collapsed as number) ? (value.collapsed as number) : DEFAULT_COLLAPSED;
  return { order, hidden, collapsed };
}

/** Le sezioni da mostrare nella Home, nell'ordine scelto. */
export const visibleSections = (prefs: HomePrefs): Section[] => prefs.order.filter((s) => !prefs.hidden.includes(s));

/** Sposta una sezione di un posto in su (-1) o in giù (+1); ai bordi non fa nulla. */
export function moveSection(prefs: HomePrefs, section: Section, delta: -1 | 1): HomePrefs {
  const from = prefs.order.indexOf(section);
  const to = from + delta;
  if (from < 0 || to < 0 || to >= prefs.order.length) return prefs;
  const order = [...prefs.order];
  [order[from], order[to]] = [order[to], order[from]];
  return { ...prefs, order };
}

/** Mostra o nasconde una sezione; l'ultima sezione mostrata non si può nascondere. */
export function toggleSection(prefs: HomePrefs, section: Section): HomePrefs {
  if (prefs.hidden.includes(section)) return { ...prefs, hidden: prefs.hidden.filter((s) => s !== section) };
  if (visibleSections(prefs).length <= 1) return prefs;
  return { ...prefs, hidden: [...prefs.hidden, section] };
}

/** Vero se le preferenze sono quelle predefinite (il «Ripristina» non serve). */
export function isDefaultHome(prefs: HomePrefs, available: Section[]): boolean {
  const defaults = defaultHomePrefs(available);
  return (
    prefs.collapsed === defaults.collapsed &&
    prefs.hidden.length === 0 &&
    prefs.order.every((s, i) => s === defaults.order[i])
  );
}
