import type { ChecksInfo } from './types';

export type CiState = 'success' | 'failure' | 'pending';

/** L'esito semplice della CI di una PR: riuscita, fallita o in corso. Senza controlli, null. */
export function ciState(checks: ChecksInfo | null | undefined): CiState | null {
  switch (checks?.state) {
    case 'SUCCESS':
      return 'success';
    case 'FAILURE':
    case 'ERROR':
      return 'failure';
    case 'PENDING':
    case 'EXPECTED':
      return 'pending';
    default:
      return null;
  }
}
