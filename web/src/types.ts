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

/** Una lista di elementi caricata a parte (viste «Issue» e «Non assegnate»). */
export interface ExtraSnapshot {
  generatedAt: string;
  items: DashboardItem[];
  errors: DashboardError[];
}

export type ExtraKey = 'issues';

/** Un elemento segnato come visto o rimandato (stato locale, vedi docs/REGOLE.md). */
export interface SeenEntry {
  url: string;
  /** L'ultimo aggiornamento che l'utente ha visto: se l'elemento cambia dopo, riappare. */
  updatedAt: string;
  /** Se rimandato, quando torna da vedere comunque. */
  snoozeUntil: string | null;
  savedAt: string;
}

export interface Dashboard {
  generatedAt: string;
  accounts: AccountStatus[];
  items: DashboardItem[];
  totals: SectionTotal[];
  errors: DashboardError[];
}
