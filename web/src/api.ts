import type { AccountStatus, Dashboard } from './types';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);

  if (!response.ok) {
    let message = `Errore ${response.status}`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body.error) message = body.error;
    } catch {
      // risposta senza JSON: resta il messaggio generico
    }
    throw new Error(message);
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
