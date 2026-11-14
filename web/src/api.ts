import type { AccountStatus, Dashboard, ErrorInfo, ExtraKey, ExtraSnapshot, FollowList, SectionPage, SeenEntry, UpdateInfo } from './types';

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
  request<ExtraSnapshot>(`${view === 'releases' ? '/api/releases' : `/api/issues/${view}`}${refresh ? '?refresh=true' : ''}`);

export const getVersion = () => request<{ version: string }>('/api/version');

export const addAccount = (token: string) =>
  request<AccountStatus>('/api/accounts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  });

/** Sostituisce il token di un account già collegato; il nuovo deve essere dello stesso account. */
export const renewAccount = (login: string, token: string) =>
  request<AccountStatus>(`/api/accounts/${encodeURIComponent(login)}/renew`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  });

export const removeAccount = (login: string) =>
  request<void>(`/api/accounts/${encodeURIComponent(login)}`, { method: 'DELETE' });

/** Lo stato «visto» salvato dal server (non tocca GitHub). */
export const getSeen = () => request<SeenEntry[]>('/api/seen');

export const putSeen = (
  url: string,
  updatedAt: string,
  snoozeUntil: string | null,
  comments: number | null = null,
  hidden = true,
) =>
  request<void>('/api/seen', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ url, updatedAt, snoozeUntil, comments, hidden }),
  });

/** Il punto di partenza per contare le novità: il server aggiunge solo gli elementi che non hanno ancora una voce. */
export const postSeenBaseline = (items: { url: string; updatedAt: string; comments: number }[]) =>
  request<void>('/api/seen/baseline', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ items }),
  });

/** Senza indirizzo toglie tutte le voci. */
export const deleteSeen = (url?: string) =>
  request<void>(`/api/seen${url ? `?url=${encodeURIComponent(url)}` : ''}`, { method: 'DELETE' });

/** Le azioni sulle notifiche: il server le esegue su GitHub con il token dell'account a cui appartengono. */
const notificationUrl = (account: string, rest: string) => `/api/notifications/${encodeURIComponent(account)}/${rest}`;

export const markNotificationRead = (account: string, threadId: string) =>
  request<void>(notificationUrl(account, `threads/${encodeURIComponent(threadId)}/read`), { method: 'POST' });

export const unsubscribeNotification = (account: string, threadId: string) =>
  request<void>(notificationUrl(account, `threads/${encodeURIComponent(threadId)}/unsubscribe`), { method: 'POST' });

/** `repo` è «proprietario/nome». */
export const markRepositoryNotificationsRead = (account: string, repo: string) => {
  const [owner, name] = repo.split('/');
  return request<void>(notificationUrl(account, `repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}/read`), { method: 'POST' });
};

/** Una pagina in più di una sezione per un account (la dashboard ne scarica solo la prima); `page` parte da 2. */
export const getSectionPage = (account: string, section: string, page: number) =>
  request<SectionPage>(`/api/section?account=${encodeURIComponent(account)}&section=${encodeURIComponent(section)}&page=${page}`);

/** Esiste una release più nuova? Il server lo chiede a GitHub (senza token) e non scarica nulla. */
export const getUpdate = (refresh = false) => request<UpdateInfo>(`/api/update${refresh ? '?refresh=true' : ''}`);

/** I repository seguiti per le release oltre ai preferiti, e quelli tolti. */
export const getFollow = () => request<FollowList>('/api/follow');

export const putFollow = (repo: string, follow: boolean) =>
  request<void>('/api/follow', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ repo, follow }),
  });
