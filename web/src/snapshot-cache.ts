import type { Dashboard } from './types';

/**
 * L'ultima fotografia dei dati, salvata nel browser per mostrarla subito all'apertura mentre i dati
 * nuovi arrivano da GitHub. Non sono cookie (troppo piccoli, e viaggerebbero a ogni richiesta) ma lo
 * storage locale di questo browser. Contiene titoli di issue e PR (anche dei repository privati) e
 * mai i token; resta su questo computer.
 */
const KEY = 'mira.snapshot.v1';

/** Vero se il valore ha la forma di una fotografia dei dati (un salvataggio vecchio o rovinato si scarta). */
function isSnapshot(value: unknown): value is Dashboard {
  const v = value as Partial<Dashboard> | null;
  return (
    typeof v === 'object' &&
    v !== null &&
    typeof v.generatedAt === 'string' &&
    Array.isArray(v.accounts) &&
    Array.isArray(v.items) &&
    Array.isArray(v.totals) &&
    Array.isArray(v.errors)
  );
}

export function loadSnapshot(): Dashboard | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    return isSnapshot(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export function saveSnapshot(snapshot: Dashboard): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(snapshot));
  } catch {
    // spazio finito o storage non disponibile: si va avanti senza salvare
  }
}

export function clearSnapshot(): void {
  try {
    localStorage.removeItem(KEY);
  } catch {
    // niente da cancellare
  }
}

/** Quanto pesa il salvataggio, in kilobyte (0 se non c'è). */
export function snapshotSizeKb(): number {
  try {
    return Math.round((localStorage.getItem(KEY)?.length ?? 0) / 1024);
  } catch {
    return 0;
  }
}
