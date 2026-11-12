import { ApiError } from './api';
import type { I18n } from './i18n';

/** Testo, nella lingua scelta, di un errore lanciato da una chiamata al backend. */
export function failureText(error: unknown, { t, errorText }: Pick<I18n, 't' | 'errorText'>): string {
  if (error instanceof ApiError) return errorText(error.info);
  if (error instanceof TypeError) return errorText({ code: 'network', detail: error.message });
  return t('error.unknown');
}
