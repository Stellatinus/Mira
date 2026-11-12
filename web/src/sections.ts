import type { Section } from './types';

export interface SectionConfig {
  key: Section;
  title: string;
  hint: string;
}

export const SECTIONS: SectionConfig[] = [
  { key: 'review', title: 'Review richieste a te', hint: 'PR che aspettano il tuo parere' },
  { key: 'assigned', title: 'Assegnate a te', hint: 'Issue e PR di cui sei assegnatario' },
  { key: 'notifications', title: 'Notifiche non lette', hint: 'Dalla casella di GitHub' },
  { key: 'mentions', title: 'Menzioni', hint: 'Dove qualcuno ti ha taggato' },
  { key: 'mine-prs', title: 'Le tue PR aperte', hint: 'In attesa di approvazione o merge' },
  { key: 'mine-issues', title: 'Le tue issue aperte', hint: 'Quelle che hai aperto tu' },
  { key: 'repos', title: 'Repository', hint: 'Aggiornati più di recente' },
  { key: 'starred', title: 'Repository preferiti', hint: 'Quelli a cui hai messo la stella' },
];

export type ViewKey = 'home' | 'repos' | 'assigned' | 'starred' | 'prs' | 'settings';

export interface ViewConfig {
  key: ViewKey;
  /** Etichetta breve sotto l'icona nella barra laterale. */
  label: string;
  title: string;
  sections: Section[];
}

export const VIEWS: ViewConfig[] = [
  {
    key: 'home',
    label: 'Home',
    title: 'Panoramica',
    sections: ['review', 'assigned', 'notifications', 'mentions', 'mine-prs', 'mine-issues'],
  },
  { key: 'repos', label: 'Repo', title: 'Repository', sections: ['repos'] },
  { key: 'assigned', label: 'Assegnate', title: 'Assegnate a te', sections: ['assigned'] },
  { key: 'starred', label: 'Preferiti', title: 'Repository preferiti', sections: ['starred'] },
  { key: 'prs', label: 'PR', title: 'Pull request', sections: ['review', 'mine-prs'] },
  { key: 'settings', label: 'Impostazioni', title: 'Impostazioni', sections: [] },
];
