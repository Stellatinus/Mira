import { useCallback, useEffect, useState } from 'react';

export type ThemePreference = 'system' | 'dark' | 'light';
export const THEMES: ThemePreference[] = ['system', 'dark', 'light'];

const STORAGE_KEY = 'mira.theme';
const LIGHT_QUERY = '(prefers-color-scheme: light)';

function readStored(): ThemePreference {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'dark' || value === 'light' ? value : 'system';
  } catch {
    return 'system';
  }
}

/** Scrive il tema effettivo su <html data-theme>; i colori sono nei token CSS. */
export function applyTheme(preference: ThemePreference) {
  const light = preference === 'light' || (preference === 'system' && window.matchMedia(LIGHT_QUERY).matches);
  document.documentElement.dataset.theme = light ? 'light' : 'dark';
}

export function useTheme(): [ThemePreference, (next: ThemePreference) => void] {
  const [preference, setPreference] = useState<ThemePreference>(readStored);

  useEffect(() => {
    applyTheme(preference);
    if (preference !== 'system') return;
    // Con «Sistema» il tema segue il computer anche mentre Mira è aperta.
    const media = window.matchMedia(LIGHT_QUERY);
    const onChange = () => applyTheme('system');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [preference]);

  const choose = useCallback((next: ThemePreference) => {
    setPreference(next);
    try {
      if (next === 'system') localStorage.removeItem(STORAGE_KEY);
      else localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // storage non disponibile: la scelta vale solo per questa sessione
    }
  }, []);

  return [preference, choose];
}
