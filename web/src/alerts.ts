import type { I18n } from './i18n';
import type { AccountStatus } from './types';
import { daysUntil } from './format';

export interface Alert {
  key: string;
  level: 'danger' | 'warn';
  text: string;
}

export const EXPIRY_WARNING_DAYS = 7;

/** Testo di un avviso dell'account ("fine-grained", "missing-scope:repo"…) nella lingua scelta. */
export function warningText(code: string, t: I18n['t']): string {
  if (code === 'fine-grained') return t('warning.fine-grained');
  if (code.startsWith('missing-scope:')) return t('warning.missing-scope', { scope: code.slice('missing-scope:'.length) });
  return code;
}

/** Tutto ciò che, negli account collegati, richiede un intervento dell'utente. */
export function accountAlerts(accounts: AccountStatus[], t: I18n['t']): Alert[] {
  const alerts: Alert[] = [];

  for (const account of accounts) {
    const login = account.login;

    if (account.state === 'invalid') {
      alerts.push({ key: `${login}:invalid`, level: 'danger', text: t('alert.invalid', { login }) });
      continue;
    }

    if (account.state === 'error') {
      alerts.push({ key: `${login}:error`, level: 'warn', text: t('alert.unreachable', { login }) });
      continue;
    }

    if (account.tokenExpiresAt) {
      const days = daysUntil(account.tokenExpiresAt);
      if (days <= EXPIRY_WARNING_DAYS) {
        alerts.push({
          key: `${login}:expiry`,
          level: days <= 1 ? 'danger' : 'warn',
          text: days <= 0 ? t('alert.expiresToday', { login }) : t('alert.expiresIn', { login, count: days }),
        });
      }
    }

    for (const warning of account.warnings) {
      alerts.push({
        key: `${login}:${warning}`,
        level: 'warn',
        text: t('alert.warning', { login, warning: warningText(warning, t) }),
      });
    }
  }

  return alerts;
}
