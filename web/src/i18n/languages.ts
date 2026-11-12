import { en } from './en';
import { it } from './it';
import type { Messages } from './it';

export interface Language {
  /** Codice breve salvato nelle preferenze (es. "it"). */
  code: string;
  /** Nome della lingua scritto nella lingua stessa: è quello che l'utente vede nell'elenco. */
  name: string;
  /** Codice BCP 47 usato per date e numeri (Intl). */
  locale: string;
  messages: Messages;
}

/**
 * Per aggiungere una lingua:
 *  1. copia `it.ts` in `xx.ts` e traduci i valori (le chiavi restano uguali);
 *  2. aggiungi una riga qui sotto.
 * Compare da sola nelle Impostazioni.
 */
export const LANGUAGES: Language[] = [
  { code: 'it', name: 'Italiano', locale: 'it-IT', messages: it },
  { code: 'en', name: 'English', locale: 'en-GB', messages: en },
];

export const DEFAULT_LANGUAGE = 'it';

export function findLanguage(code: string | null | undefined): Language | undefined {
  return LANGUAGES.find((l) => l.code === code);
}

/** Lingua del browser, se Mira la supporta (es. "en-US" → "en"). */
export function detectLanguage(preferred: readonly string[]): Language {
  for (const tag of preferred) {
    const found = findLanguage(tag.toLowerCase().split('-')[0]);
    if (found) return found;
  }
  return findLanguage(DEFAULT_LANGUAGE) ?? LANGUAGES[0];
}
