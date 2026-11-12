import type { Section } from './types';

export type ViewKey = 'home' | 'repos' | 'issues' | 'unassigned' | 'assigned' | 'prs' | 'notifications' | 'settings';

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
  // «Issue» e «Non assegnate» scaricano tutte le pagine dei risultati alla richiesta (non fanno parte della dashboard).
  { key: 'issues', tools: 'items', sections: ['issues'] },
  { key: 'unassigned', tools: 'items', sections: ['unassigned'] },
  { key: 'assigned', tools: 'items', sections: ['assigned'] },
  { key: 'prs', tools: 'items', sections: ['review', 'mine-prs'] },
  { key: 'notifications', tools: 'items', sections: ['notifications', 'mentions'] },
  // Le impostazioni si aprono dal pulsante sopra gli avatar, non dall'elenco delle sezioni.
  { key: 'settings', sections: [] },
];

/** Le viste che compaiono nell'elenco della barra laterale. */
export const NAV_VIEWS = VIEWS.filter((v) => v.key !== 'settings');

/** Le viste i cui elementi si scaricano a parte, alla richiesta. */
export const isExtraView = (key: ViewKey): key is 'issues' | 'unassigned' => key === 'issues' || key === 'unassigned';
