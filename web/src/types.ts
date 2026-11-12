export type Section =
  | 'review'
  | 'assigned'
  | 'notifications'
  | 'mentions'
  | 'mine-prs'
  | 'mine-issues';

export type ItemType = 'issue' | 'pr' | 'release' | 'commit' | 'discussion' | 'other';

export interface AccountStatus {
  login: string;
  name: string | null;
  avatarUrl: string | null;
  state: 'ok' | 'invalid' | 'error';
  tokenExpiresAt: string | null;
  scopes: string[];
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
}

/** Totale reale di una sezione per un account; null quando GitHub non lo indica. */
export interface SectionTotal {
  account: string;
  section: Section;
  total: number | null;
}

export interface Dashboard {
  generatedAt: string;
  accounts: AccountStatus[];
  items: DashboardItem[];
  totals: SectionTotal[];
  errors: string[];
}
