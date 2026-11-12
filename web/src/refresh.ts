import { useCallback, useState } from 'react';

/**
 * Ogni quanti minuti Mira aggiorna da sola i dati (0 = mai, solo a mano). Non si scende sotto i
 * 5 minuti: la Search API di GitHub concede 30 richieste al minuto per utente e ogni aggiornamento
 * ne fa 5 per account.
 */
export const REFRESH_OPTIONS = [0, 5, 10, 15, 30, 60];
export const DEFAULT_REFRESH_MINUTES = 5;

const STORAGE_KEY = 'mira.refreshMinutes';

function readStored(): number {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved !== null && REFRESH_OPTIONS.includes(Number(saved)) ? Number(saved) : DEFAULT_REFRESH_MINUTES;
  } catch {
    return DEFAULT_REFRESH_MINUTES;
  }
}

export function useRefreshInterval(): [number, (minutes: number) => void] {
  const [minutes, setMinutes] = useState<number>(readStored);

  const choose = useCallback((next: number) => {
    setMinutes(next);
    try {
      localStorage.setItem(STORAGE_KEY, String(next));
    } catch {
      // storage non disponibile: la scelta vale solo per questa sessione
    }
  }, []);

  return [minutes, choose];
}
