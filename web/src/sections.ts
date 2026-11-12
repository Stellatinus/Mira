import type { Section } from './types';

export type ViewKey = 'home' | 'repos' | 'issues' | 'prs' | 'notifications' | 'settings';

export interface ViewConfig {
  key: ViewKey;
  /** Quali strumenti (filtri e ordinamento) ha la vista: quelli dei repository o quelli degli elementi. */
  tools?: 'repos' | 'items';
  /** Sezioni di dati mostrate nella vista; etichetta e titolo stanno nelle traduzioni (`view.<chiave>.*`). */
  sections: Section[];
}

export const VIEWS: ViewConfig[] = [
  { key: 'home', sections: ['review', 'assigned', 'notifications', 'mentions', 'mine-prs', 'mine-issues'] },
  { key: 'repos', tools: 'repos', sections: ['repos'] },
  // «Issue» scarica tutte le issue alla richiesta (non fa parte della dashboard); assegnate a te, ad altri e non
  // assegnate sono filtri di questa vista.
  { key: 'issues', tools: 'items', sections: ['issues'] },
  { key: 'prs', tools: 'items', sections: ['review', 'mine-prs'] },
  { key: 'notifications', tools: 'items', sections: ['notifications', 'mentions'] },
  // Le impostazioni si aprono dal pulsante sopra gli avatar, non dall'elenco delle sezioni.
  { key: 'settings', sections: [] },
];

/** Le viste che compaiono nell'elenco della barra laterale. */
export const NAV_VIEWS = VIEWS.filter((v) => v.key !== 'settings');

/** Le viste i cui elementi si scaricano a parte, alla richiesta. */
export const isExtraView = (key: ViewKey): key is 'issues' => key === 'issues';
