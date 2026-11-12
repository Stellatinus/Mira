import type { ReactNode } from 'react';
import { Avatar } from './Avatar';
import { AssignedIcon, DotsIcon, HomeIcon, PullRequestIcon, RepoIcon, SettingsIcon, StarIcon } from './icons';
import { VIEWS } from './sections';
import type { ViewKey } from './sections';
import type { AccountStatus } from './types';

const ICONS: Record<ViewKey, ReactNode> = {
  home: <HomeIcon size={20} />,
  repos: <RepoIcon size={20} />,
  assigned: <AssignedIcon size={20} />,
  starred: <StarIcon size={20} />,
  prs: <PullRequestIcon size={20} />,
  settings: <SettingsIcon size={20} />,
};

interface Props {
  view: ViewKey;
  onView: (view: ViewKey) => void;
  accounts: AccountStatus[];
  /** Account selezionato come filtro, oppure "all". */
  filter: string;
  onFilter: (login: string) => void;
  needsAttention: boolean;
}

export function Sidebar({ view, onView, accounts, filter, onFilter, needsAttention }: Props) {
  return (
    <aside className="sidebar">
      <img className="logo" src="/logo.png" alt="Mira" width={44} height={44} />

      <nav className="nav" aria-label="Sezioni">
        {VIEWS.map((v) => (
          <button
            key={v.key}
            className={view === v.key ? 'nav-item active' : 'nav-item'}
            onClick={() => onView(v.key)}
            aria-current={view === v.key ? 'page' : undefined}
          >
            <span className="nav-icon">
              {ICONS[v.key]}
              {v.key === 'settings' && needsAttention && <span className="attention" title="C'è qualcosa da sistemare" />}
            </span>
            <span className="nav-label">{v.label}</span>
          </button>
        ))}
      </nav>

      <div className="stack-box">
        <button className="stack-more" onClick={() => onView('settings')} title="Gestisci account" aria-label="Gestisci account">
          <DotsIcon />
        </button>
        <div className="stack" role="group" aria-label="Filtra per account">
          {accounts.map((account) => {
            const active = filter === account.login;
            return (
              <button
                key={account.login}
                className={active ? 'stack-avatar active' : 'stack-avatar'}
                title={active ? `${account.login} (clicca per mostrare tutti)` : `Mostra solo ${account.login}`}
                aria-pressed={active}
                onClick={() => onFilter(active ? 'all' : account.login)}
              >
                <Avatar account={account} size={36} />
              </button>
            );
          })}
        </div>
      </div>
    </aside>
  );
}
