export type Section =
  | 'review'
  | 'assigned'
  | 'notifications'
  | 'mentions'
  | 'mine-prs'
  | 'mine-issues'
  | 'repos'
  | 'starred'
  | 'issues';

export type ItemType = 'issue' | 'pr' | 'repo' | 'release' | 'commit' | 'discussion' | 'other';

/** Quante richieste restano a un account per una risorsa di GitHub (core, search, graphql…) e quando la finestra si azzera. */
export interface RateLimit {
  resource: string;
  limit: number;
  remaining: number;
  resetsAt: string;
}

export interface AccountStatus {
  login: string;
  name: string | null;
  avatarUrl: string | null;
  state: 'ok' | 'invalid' | 'error';
  tokenExpiresAt: string | null;
  scopes: string[];
  /** Codici: "fine-grained" oppure "missing-scope:<scope>"; l'interfaccia li traduce. */
  warnings: string[];
  rateLimits?: RateLimit[];
}

export interface DashboardItem {
  id: string;
  account: string;
  section: Section;
  type: ItemType;
  title: string;
  repo: string;
  url: string;
  updatedAt: string;
  author: string | null;
  reason: string | null;
  draft: boolean;
  comments: number;
  description: string | null;
  language: string | null;
  stars: number;
  private: boolean;
  /** Issue e PR aperte nel repository, contate a parte (solo per gli elementi di tipo "repo"). */
  openIssues: number;
  openPrs: number;
  /** Logo del repository (anteprima sociale personalizzata, altrimenti avatar del proprietario). */
  logoUrl: string | null;
  /** Se il repository è un fork, e di quale repository ("owner/nome"). */
  fork: boolean;
  forkOf: string | null;
  /** Issue aperte senza assegnatario, su tutto il repository. */
  unassignedIssues: number;
  /** I login degli assegnatari di una issue o PR. */
  assignees: string[] | null;
  /** Stato di review, solo per le PR dell'utente. */
  review?: ReviewInfo | null;
  /** Esito della CI, solo per le PR dell'utente che hanno controlli. */
  checks?: ChecksInfo | null;
}

/** Totale reale di una sezione per un account; null quando GitHub non lo indica. */
export interface SectionTotal {
  account: string;
  section: Section;
  total: number | null;
}

/** Errore con codice stabile in arrivo dal backend; il testo lo sceglie il frontend (vedi i18n). */
export interface ErrorInfo {
  code: string;
  detail?: string | null;
  cause?: ErrorInfo | null;
}

/** Errore avvenuto leggendo una sezione di un account (section può essere "account"). */
export interface DashboardError {
  account: string;
  section: string;
  error: ErrorInfo;
}

/** Il risultato del controllo degli aggiornamenti. */
export interface UpdateInfo {
  current: string;
  latest: string | null;
  url: string | null;
  publishedAt: string | null;
  available: boolean;
  checkedAt: string;
  error: ErrorInfo | null;
}

/** Una pagina in più di una sezione per un account («Carica altri»). */
export interface SectionPage {
  items: DashboardItem[];
  total: number | null;
  hasMore: boolean;
}

/** Una lista di elementi caricata a parte (viste «Issue» e «Non assegnate»). */
export interface ExtraSnapshot {
  generatedAt: string;
  items: DashboardItem[];
  errors: DashboardError[];
}

export type ExtraKey = 'issues';

/** Una review su una PR: chi, con che esito (APPROVED, CHANGES_REQUESTED, COMMENTED, DISMISSED, PENDING) e quando. */
export interface PrReview {
  login: string | null;
  state: string;
  at: string | null;
}

export interface PrComment {
  login: string | null;
  at: string;
}

/** A che punto è una PR dell'utente: esito di GitHub (se il repository chiede review), ultime review e ultimi commenti. */
export interface ReviewInfo {
  decision: string | null;
  reviews: PrReview[];
  comments: PrComment[];
  commentCount: number;
}

/** Un controllo che ha fallito: nome e, se c'è, l'indirizzo dei dettagli. */
export interface CheckRef {
  name: string;
  url: string | null;
}

/** Esito dei controlli (CI) sull'ultimo commit: stato di GitHub (SUCCESS, FAILURE, ERROR, PENDING, EXPECTED), totali e falliti. */
export interface ChecksInfo {
  state: string;
  total: number;
  pending: number;
  failed: CheckRef[];
}

/** Un elemento segnato come visto o rimandato (stato locale, vedi docs/REGOLE.md). */
export interface SeenEntry {
  url: string;
  /** L'ultimo aggiornamento che l'utente ha visto: se l'elemento cambia dopo, riappare. */
  updatedAt: string;
  /** Se rimandato, quando torna da vedere comunque. */
  snoozeUntil: string | null;
  savedAt: string;
  /** Quanti commenti aveva l'elemento l'ultima volta che l'hai visto: da qui si contano le novità. */
  comments: number | null;
  /** Falso per il solo punto di partenza delle novità: la voce non nasconde l'elemento. */
  hidden: boolean;
}

export interface Dashboard {
  generatedAt: string;
  accounts: AccountStatus[];
  items: DashboardItem[];
  totals: SectionTotal[];
  errors: DashboardError[];
}
