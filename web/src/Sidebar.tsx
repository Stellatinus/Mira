import type { ReactNode } from 'react';
import logoUrl from './assets/logo.png';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { AssignedIcon, BellIcon, DotsIcon, HomeIcon, PullRequestIcon, RepoIcon, UsersIcon } from './icons';
import { NAV_VIEWS } from './sections';
import type { ViewKey } from './sections';
import type { AccountStatus } from './types';

const ICONS: Partial<Record<ViewKey, ReactNode>> = {
  home: <HomeIcon size={20} />,
  repos: <RepoIcon size={20} />,
  assigned: <AssignedIcon size={20} />,
  prs: <PullRequestIcon size={20} />,
  notifications: <BellIcon size={20} />,
};

interface Props {
  view: ViewKey;
  onView: (view: ViewKey) => void;
  accounts: AccountStatus[];
  /** Account selezionato come filtro, oppure "all". */
  filter: string;
  onFilter: (login: string) => void;
  needsAttention: boolean;
  /** Quante notifiche non lette ci sono (col filtro per account), per il numerino sulla voce Notifiche. */
  unreadNotifications: number;
}

export function Sidebar({ view, onView, accounts, filter, onFilter, needsAttention, unreadNotifications }: Props) {
  const { t } = useI18n();

  return (
    <aside className="sidebar">
      <img className="brand-logo" src={logoUrl} alt="Mira" width={56} height={56} />

      <nav className="nav" aria-label={t('nav.label')}>
        {NAV_VIEWS.map((v) => (
          <button
            key={v.key}
            className={view === v.key ? 'nav-item active' : 'nav-item'}
            onClick={() => onView(v.key)}
            aria-current={view === v.key ? 'page' : undefined}
          >
            <span className="nav-icon">
              {ICONS[v.key]}
              {v.key === 'notifications' && unreadNotifications > 0 && (
                <span className="nav-badge">{unreadNotifications > 99 ? '99+' : unreadNotifications}</span>
              )}
            </span>
            <span className="nav-label">{t(`view.${v.key}.label` as 'view.home.label')}</span>
          </button>
        ))}
      </nav>

      <div className="stack-box">
        <button
          className={view === 'settings' ? 'stack-more active' : 'stack-more'}
          onClick={() => onView('settings')}
          title={t('nav.settings')}
          aria-label={t('nav.settings')}
          aria-current={view === 'settings' ? 'page' : undefined}
        >
          <DotsIcon />
          {needsAttention && <span className="attention" title={t('nav.attention')} />}
        </button>
        {accounts.length > 1 && (
          <button
            className={filter === 'all' ? 'stack-all active' : 'stack-all'}
            title={t('nav.allAccounts')}
            aria-label={t('nav.allAccounts')}
            aria-pressed={filter === 'all'}
            onClick={() => onFilter('all')}
          >
            <UsersIcon size={18} />
          </button>
        )}
        <div className="stack" role="group" aria-label={t('nav.filterGroup')}>
          {accounts.map((account) => {
            const active = filter === account.login;
            return (
              <button
                key={account.login}
                className={active ? 'stack-avatar active' : 'stack-avatar'}
                title={
                  active
                    ? t('nav.accountActive', { login: account.login })
                    : t('nav.accountOnly', { login: account.login })
                }
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
