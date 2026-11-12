export type Section =
  | 'review'
  | 'assigned'
  | 'notifications'
  | 'mentions'
  | 'mine-prs'
  | 'mine-issues'
  | 'repos'
  | 'starred';

export type ItemType = 'issue' | 'pr' | 'repo' | 'release' | 'commit' | 'discussion' | 'other';

export interface AccountStatus {
  login: string;
  name: string | null;
  avatarUrl: string | null;
  state: 'ok' | 'invalid' | 'error';
  tokenExpiresAt: string | null;
  scopes: string[];
  /** Codici: "fine-grained" oppure "missing-scope:<scope>"; l'interfaccia li traduce. */
  warnings: string[];
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

export interface Dashboard {
  generatedAt: string;
  accounts: AccountStatus[];
  items: DashboardItem[];
  totals: SectionTotal[];
  errors: DashboardError[];
}
