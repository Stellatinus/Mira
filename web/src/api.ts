import type { AccountStatus, Dashboard, ErrorInfo, ExtraKey, ExtraSnapshot, SeenEntry } from './types';

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

/** Tutte le issue di una vista («issues»), scaricate alla richiesta. */
export const getIssues = (view: ExtraKey, refresh = false) =>
  request<ExtraSnapshot>(`/api/issues/${view}${refresh ? '?refresh=true' : ''}`);

export const getVersion = () => request<{ version: string }>('/api/version');

export const addAccount = (token: string) =>
  request<AccountStatus>('/api/accounts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  });

export const removeAccount = (login: string) =>
  request<void>(`/api/accounts/${encodeURIComponent(login)}`, { method: 'DELETE' });

/** Lo stato «visto» salvato dal server (non tocca GitHub). */
export const getSeen = () => request<SeenEntry[]>('/api/seen');

export const putSeen = (url: string, updatedAt: string, snoozeUntil: string | null) =>
  request<void>('/api/seen', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ url, updatedAt, snoozeUntil }),
  });

/** Senza indirizzo toglie tutte le voci. */
export const deleteSeen = (url?: string) =>
  request<void>(`/api/seen${url ? `?url=${encodeURIComponent(url)}` : ''}`, { method: 'DELETE' });
