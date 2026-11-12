import type { Section } from './types';

export type ViewKey = 'home' | 'repos' | 'assigned' | 'starred' | 'prs' | 'settings';

export interface ViewConfig {
  key: ViewKey;
  /** Sezioni di dati mostrate nella vista; etichetta e titolo stanno nelle traduzioni (`view.<chiave>.*`). */
  sections: Section[];
}

export const VIEWS: ViewConfig[] = [
  { key: 'home', sections: ['review', 'assigned', 'notifications', 'mentions', 'mine-prs', 'mine-issues'] },
  { key: 'repos', sections: ['repos'] },
  { key: 'assigned', sections: ['assigned'] },
  { key: 'starred', sections: ['starred'] },
  { key: 'prs', sections: ['review', 'mine-prs'] },
  // Le impostazioni si aprono dal pulsante sopra gli avatar, non dall'elenco delle sezioni.
  { key: 'settings', sections: [] },
];

/** Le viste che compaiono nell'elenco della barra laterale. */
export const NAV_VIEWS = VIEWS.filter((v) => v.key !== 'settings');
