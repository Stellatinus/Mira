import type { Dashboard, DashboardItem } from './types';

/** Gli elementi comparsi dall'ultimo aggiornamento che meritano un avviso del sistema. */
export interface NewItems {
  mentions: DashboardItem[];
  reviews: DashboardItem[];
}

/**
 * Cosa c'è di nuovo tra due aggiornamenti: le menzioni e le review richieste che prima non c'erano.
 * Un elemento è nuovo se il suo id non era nella stessa sezione dell'aggiornamento precedente. Al primo aggiornamento
 * (senza «prima») non c'è niente di nuovo: non si avvisa di tutto ciò che c'era già.
 * `isHidden` esclude ciò che l'utente ha già segnato come visto o rimandato.
 */
export function findNew(
  previous: Pick<Dashboard, 'items'> | null,
  next: Pick<Dashboard, 'items'>,
  isHidden: (item: DashboardItem) => boolean = () => false,
): NewItems {
  const found: NewItems = { mentions: [], reviews: [] };
  if (!previous) return found;

  const known = new Set(previous.items.filter((i) => i.section === 'mentions' || i.section === 'review').map((i) => `${i.section}|${i.id}`));
  const seenNow = new Set<string>();
  for (const item of next.items) {
    if (item.section !== 'mentions' && item.section !== 'review') continue;
    // Lo stesso elemento può arrivare da più account: se ne avvisa una volta sola.
    const key = `${item.section}|${item.url}`;
    if (seenNow.has(key)) continue;
    if (known.has(`${item.section}|${item.id}`) || isHidden(item)) continue;
    seenNow.add(key);
    (item.section === 'mentions' ? found.mentions : found.reviews).push(item);
  }
  return found;
}
