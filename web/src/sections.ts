import type { Section } from './types';

export type ViewKey = 'home' | 'repos' | 'issues' | 'prs' | 'notifications' | 'releases' | 'settings';

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
  // Le release dei repository seguiti (preferiti e scelti a mano): come «Issue», si scaricano alla richiesta.
  { key: 'releases', tools: 'items', sections: ['releases'] },
  // Le impostazioni si aprono dal pulsante sopra gli avatar, non dall'elenco delle sezioni.
  { key: 'settings', sections: [] },
];

/** Le viste che compaiono nell'elenco della barra laterale. */
export const NAV_VIEWS = VIEWS.filter((v) => v.key !== 'settings');

/** Le viste i cui elementi si scaricano a parte, alla richiesta. */
export const isExtraView = (key: ViewKey): key is 'issues' | 'releases' => key === 'issues' || key === 'releases';

/** L'hash che identifica una vista nell'URL: `#/issues`; la Home è `#/`. */
export const hashFor = (key: ViewKey): string => (key === 'home' ? '#/' : `#/${key}`);

/**
 * La vista indicata dall'hash corrente, oppure null se l'hash non è una vista (per esempio `#sec-review`,
 * il link di un riquadro della Home a una sezione della pagina). Senza hash è la Home.
 */
export function viewFromHash(hash: string = window.location.hash): ViewKey | null {
  if (hash === '' || hash === '#' || hash === '#/') return 'home';
  const match = /^#\/([\w-]+)\/?$/.exec(hash);
  return VIEWS.find((v) => v.key === match?.[1])?.key ?? null;
}
