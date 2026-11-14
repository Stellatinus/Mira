import type { I18n } from './i18n';
import type { AccountStatus, RateLimit } from './types';

/** Sotto questa soglia di ricerche rimaste si smette di aggiornare da soli, finché la finestra non si azzera. */
const SEARCH_RESERVE = 3;

/** Nome breve di una risorsa di GitHub, per chi legge. */
function resourceName(resource: string, t: I18n['t']): string {
  switch (resource) {
    case 'core':
      return t('limits.core');
    case 'search':
      return t('limits.search');
    case 'graphql':
      return t('limits.graphql');
    default:
      return resource;
  }
}

/** L'orario (e il giorno, se non è oggi) in cui una finestra si azzera. */
export function resetTime(iso: string, locale: string, now: number = Date.now()): string {
  const at = new Date(iso);
  const sameDay = at.toDateString() === new Date(now).toDateString();
  return new Intl.DateTimeFormat(locale, sameDay ? { timeStyle: 'short' } : { dateStyle: 'short', timeStyle: 'short' }).format(at);
}

/** «API 4990/5000 · ricerca 28/30»; una risorsa esaurita dice quando si azzera. */
export function limitsText(limits: RateLimit[], { t, language }: Pick<I18n, 't' | 'language'>): string {
  return limits
    .map((limit) => {
      const name = resourceName(limit.resource, t);
      return limit.remaining === 0
        ? t('limits.exhausted', { name, time: resetTime(limit.resetsAt, language.locale) })
        : t('limits.left', { name, remaining: limit.remaining, limit: limit.limit });
    })
    .join(' · ');
}

/** Una risorsa esaurita, o quasi, per cui conviene avvisare. */
export const isLow = (limit: RateLimit): boolean => limit.remaining === 0 || limit.remaining / limit.limit < 0.1;

/**
 * Vero se per qualche account le ricerche sono quasi finite e la finestra non si è ancora azzerata: un aggiornamento
 * automatico ora le esaurirebbe del tutto e farebbe fallire le sezioni. Quello fatto a mano resta sempre possibile.
 */
export function searchNearlyExhausted(accounts: AccountStatus[] | undefined, now: number = Date.now()): boolean {
  return (accounts ?? []).some((account) =>
    (account.rateLimits ?? []).some(
      (limit) => limit.resource === 'search' && limit.remaining <= SEARCH_RESERVE && Date.parse(limit.resetsAt) > now,
    ),
  );
}
