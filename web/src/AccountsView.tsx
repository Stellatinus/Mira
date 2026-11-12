import { useState } from 'react';
import type { FormEvent } from 'react';
import { addAccount, removeAccount } from './api';
import { warningText } from './alerts';
import { Avatar } from './Avatar';
import { failureText } from './errors';
import { daysUntil } from './format';
import { useI18n } from './i18n';
import type { I18n } from './i18n';
import type { AccountStatus } from './types';

const TOKEN_URL = 'https://github.com/settings/tokens/new?description=Mira&scopes=notifications,repo,read:org';

interface Props {
  accounts: AccountStatus[];
  onChanged: () => Promise<void>;
}

export function AccountsView({ accounts, onChanged }: Props) {
  const i18n = useI18n();
  const { t, tx } = i18n;
  const [token, setToken] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await addAccount(token);
      setToken('');
      await onChanged();
    } catch (e) {
      setError(failureText(e, i18n));
    } finally {
      setBusy(false);
    }
  }

  async function remove(login: string) {
    setBusy(true);
    setError(null);
    try {
      await removeAccount(login);
      setConfirming(null);
      await onChanged();
    } catch (e) {
      setError(failureText(e, i18n));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <section className="card">
        <header className="card-head">
          <h2>{t('accounts.title')}</h2>
          <span className="count">{accounts.length}</span>
        </header>
        {accounts.length === 0 ? (
          <p className="muted pad">{t('accounts.none')}</p>
        ) : (
          <ul className="items">
            {accounts.map((account) => (
              <li key={account.login} className="item account-row">
                <Avatar account={account} size={36} />
                <div className="item-main">
                  <strong>{account.login}</strong>
                  {account.name && <span className="muted"> · {account.name}</span>}
                  <div className="item-meta">
                    <span>{expiryText(account, i18n)}</span>
                  </div>
                  {account.warnings.map((warning) => (
                    <div key={warning} className="item-meta warn-text">
                      {warningText(warning, t)}
                    </div>
                  ))}
                </div>
                <div className="item-side">
                  <StatusPill state={account.state} t={t} />
                  {confirming === account.login ? (
                    <>
                      <button className="btn danger" disabled={busy} onClick={() => void remove(account.login)}>
                        {t('accounts.confirm')}
                      </button>
                      <button className="btn" onClick={() => setConfirming(null)}>
                        {t('accounts.cancel')}
                      </button>
                    </>
                  ) : (
                    <button className="btn" onClick={() => setConfirming(account.login)}>
                      {t('accounts.remove')}
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="card">
        <header className="card-head">
          <h2>{t('accounts.add.title')}</h2>
        </header>
        <form className="add-form" onSubmit={(e) => void submit(e)}>
          <label htmlFor="token">{t('accounts.add.label')}</label>
          <div className="row">
            <input
              id="token"
              type="password"
              autoComplete="off"
              spellCheck={false}
              placeholder="ghp_…"
              value={token}
              onChange={(e) => setToken(e.target.value)}
            />
            <button className="btn primary" type="submit" disabled={busy || token.trim() === ''}>
              {busy ? t('accounts.add.busy') : t('accounts.add.submit')}
            </button>
          </div>
          {error && <p className="warn-text">{error}</p>}
          <p className="muted small">{t('accounts.add.privacy')}</p>
        </form>
      </section>

      <section className="card">
        <header className="card-head">
          <h2>{t('guide.title')}</h2>
        </header>
        <ol className="guide">
          <li>
            {tx('guide.step1', {
              link: (
                <a href={TOKEN_URL} target="_blank" rel="noreferrer">
                  {t('guide.step1.link')}
                </a>
              ),
            })}
          </li>
          <li>
            {tx('guide.step2', {
              scopes: (
                <>
                  <code>notifications</code>, <code>repo</code>, <code>read:org</code>
                </>
              ),
            })}
          </li>
          <li>{t('guide.step3')}</li>
          <li>{t('guide.step4')}</li>
          <li>{t('guide.step5')}</li>
          <li>{t('guide.step6')}</li>
        </ol>
        <p className="muted small pad">{tx('guide.note', { classic: <strong>{t('guide.note.classic')}</strong> })}</p>
      </section>
    </>
  );
}

function StatusPill({ state, t }: { state: AccountStatus['state']; t: I18n['t'] }) {
  const label =
    state === 'ok' ? t('accounts.status.ok') : state === 'invalid' ? t('accounts.status.invalid') : t('accounts.status.error');
  return <span className={`pill ${state}`}>{label}</span>;
}

function expiryText(account: AccountStatus, { t, formatDate }: I18n): string {
  if (account.state === 'invalid') return t('accounts.expiry.invalid');
  if (account.state === 'error') return t('accounts.expiry.error');
  if (!account.tokenExpiresAt) return t('accounts.expiry.none');

  const days = daysUntil(account.tokenExpiresAt);
  const date = formatDate(account.tokenExpiresAt);
  if (days <= 0) return t('accounts.expiry.today', { date });
  return t('accounts.expiry.in', { count: days, date });
}
