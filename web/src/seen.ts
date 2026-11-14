import type { DashboardItem, SeenEntry } from './types';

/** Gli elementi segnati come visti o rimandati, per indirizzo dell'elemento. */
export type SeenMap = ReadonlyMap<string, SeenEntry>;

/**
 * Un elemento visto è nascosto finché non cambia: riappare da solo se è stato aggiornato dopo l'ultima volta che
 * l'utente l'ha visto. Uno rimandato è nascosto finché non passa il momento scelto; se cambia prima, riappare comunque.
 * Gli elementi senza voce non sono mai nascosti.
 */
export function isHidden(entry: SeenEntry | undefined, item: Pick<DashboardItem, 'updatedAt'>, now: number): boolean {
  // Una voce che è solo il punto di partenza per le novità non nasconde nulla.
  if (!entry || entry.hidden === false) return false;
  if (Date.parse(item.updatedAt) > Date.parse(entry.updatedAt)) return false;
  if (entry.snoozeUntil !== null && Date.parse(entry.snoozeUntil) <= now) return false;
  return true;
}

/** «Domani»: le 8 del mattino del giorno dopo, nell'ora del computer. */
export function tomorrowMorning(now: Date = new Date()): Date {
  const next = new Date(now);
  next.setDate(next.getDate() + 1);
  next.setHours(8, 0, 0, 0);
  return next;
}

/** Gli elementi per cui ha senso segnare «visto»: i repository non si «gestiscono». */
export const canMarkSeen = (item: Pick<DashboardItem, 'type'>): boolean => item.type !== 'repo';

/** L'identificativo che GitHub dà a una notifica: l'id dell'elemento è «account:notifications:<id>». */
export const threadIdOf = (item: Pick<DashboardItem, 'id'>): string => item.id.split(':').pop() ?? '';

/** Quanti commenti sono arrivati su una tua issue dall'ultima volta che l'hai vista (0 se non c'è un punto di partenza). */
export function newComments(entry: SeenEntry | undefined, item: Pick<DashboardItem, 'comments'>): number {
  if (!entry || entry.comments === null || entry.comments === undefined) return 0;
  return Math.max(0, item.comments - entry.comments);
}

/** Le novità si contano per le issue aperte dall'utente (la sezione «Le tue issue aperte»). */
export const tracksNews = (item: Pick<DashboardItem, 'section' | 'type'>): boolean =>
  item.section === 'mine-issues' && item.type === 'issue';
