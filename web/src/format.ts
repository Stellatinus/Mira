import type { DashboardItem } from './types';

const relative = new Intl.RelativeTimeFormat('it', { numeric: 'auto' });
const dateFormat = new Intl.DateTimeFormat('it-IT', { day: 'numeric', month: 'long', year: 'numeric' });

export function timeAgo(iso: string): string {
  const seconds = (new Date(iso).getTime() - Date.now()) / 1000;
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['day', 86400],
    ['hour', 3600],
    ['minute', 60],
  ];
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return relative.format(Math.round(seconds / size), unit);
  }
  return 'ora';
}

export function daysUntil(iso: string): number {
  return Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000);
}

export function formatDate(iso: string): string {
  return dateFormat.format(new Date(iso));
}

const reasons: Record<string, string> = {
  mention: 'Menzione',
  team_mention: 'Menzione del team',
  assign: 'Assegnata',
  review_requested: 'Review richiesta',
  author: 'Tua',
  comment: 'Nuovo commento',
  state_change: 'Cambio di stato',
  ci_activity: 'CI',
  subscribed: 'Seguita',
  manual: 'Seguita',
  security_alert: 'Sicurezza',
  approval_requested: 'Approvazione richiesta',
  invitation: 'Invito',
};

export function reasonLabel(reason: string): string {
  return reasons[reason] ?? reason.replaceAll('_', ' ');
}

/** Etichetta breve del tipo di elemento, per card e pannello dettaglio. */
export function typeLabel(item: Pick<DashboardItem, 'type' | 'draft' | 'private'>): string {
  switch (item.type) {
    case 'pr':
      return item.draft ? 'PR in bozza' : 'Pull request';
    case 'issue':
      return 'Issue';
    case 'repo':
      return item.private ? 'Repository privato' : 'Repository pubblico';
    case 'release':
      return 'Release';
    case 'commit':
      return 'Commit';
    case 'discussion':
      return 'Discussione';
    default:
      return 'Notifica';
  }
}
