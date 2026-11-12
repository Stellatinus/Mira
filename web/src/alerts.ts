import type { AccountStatus } from './types';
import { daysUntil } from './format';

export interface Alert {
  key: string;
  level: 'danger' | 'warn';
  text: string;
}

export const EXPIRY_WARNING_DAYS = 7;

/** Tutto ciò che, negli account collegati, richiede un intervento dell'utente. */
export function accountAlerts(accounts: AccountStatus[]): Alert[] {
  const alerts: Alert[] = [];

  for (const account of accounts) {
    if (account.state === 'invalid') {
      alerts.push({
        key: `${account.login}:invalid`,
        level: 'danger',
        text: `Il token di ${account.login} è scaduto o non è più valido. Genera un nuovo token e ricollega l'account.`,
      });
      continue;
    }

    if (account.state === 'error') {
      alerts.push({
        key: `${account.login}:error`,
        level: 'warn',
        text: `Non riesco a contattare GitHub per ${account.login}.`,
      });
      continue;
    }

    if (account.tokenExpiresAt) {
      const days = daysUntil(account.tokenExpiresAt);
      if (days <= EXPIRY_WARNING_DAYS) {
        alerts.push({
          key: `${account.login}:expiry`,
          level: days <= 1 ? 'danger' : 'warn',
          text:
            days <= 0
              ? `Il token di ${account.login} scade oggi.`
              : `Il token di ${account.login} scade tra ${days} ${days === 1 ? 'giorno' : 'giorni'}.`,
        });
      }
    }

    for (const warning of account.warnings) {
      alerts.push({ key: `${account.login}:${warning}`, level: 'warn', text: `${account.login}: ${warning}` });
    }
  }

  return alerts;
}
