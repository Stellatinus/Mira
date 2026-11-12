import type { AccountStatus, Dashboard, ErrorInfo } from './types';

/** Errore di una chiamata al backend: porta il codice, che l'interfaccia traduce. */
export class ApiError extends Error {
  readonly info: ErrorInfo;

  constructor(info: ErrorInfo) {
    super(info.code);
    this.info = info;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);

  if (!response.ok) {
    let info: ErrorInfo = { code: 'http', detail: String(response.status) };
    try {
      const body = (await response.json()) as { error?: ErrorInfo };
      if (body.error?.code) info = body.error;
    } catch {
      // risposta senza JSON: resta l'errore generico con il codice HTTP
    }
    throw new ApiError(info);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const getDashboard = (refresh = false) =>
  request<Dashboard>(`/api/dashboard${refresh ? '?refresh=true' : ''}`);

export const addAccount = (token: string) =>
  request<AccountStatus>('/api/accounts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  });

export const removeAccount = (login: string) =>
  request<void>(`/api/accounts/${encodeURIComponent(login)}`, { method: 'DELETE' });
