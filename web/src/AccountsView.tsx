import { useState } from 'react';
import type { FormEvent } from 'react';
import { addAccount, removeAccount, renewAccount } from './api';
import { EXPIRY_WARNING_DAYS, warningText } from './alerts';
import { Avatar } from './Avatar';
import { failureText } from './errors';
import { daysUntil } from './format';
import { useI18n } from './i18n';
import type { I18n } from './i18n';
import { isLow, limitsText } from './limits';
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
  // Rinnovo del token: l'account aperto, il nuovo token incollato e l'eventuale errore.
  const [renewing, setRenewing] = useState<string | null>(null);
  const [renewToken, setRenewToken] = useState('');
  const [renewError, setRenewError] = useState<string | null>(null);

  // Mentre lavorano, i pulsanti usano aria-disabled (non disabled) per non far perdere il focus.
  const cannotSubmit = busy || token.trim() === '';

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (cannotSubmit) return;
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

  function openRenew(login: string) {
    setRenewing(renewing === login ? null : login);
    setRenewToken('');
    setRenewError(null);
  }

  async function renew(event: FormEvent, login: string) {
    event.preventDefault();
    if (busy || renewToken.trim() === '') return;
    setBusy(true);
    setRenewError(null);
    try {
      await renewAccount(login, renewToken);
      setRenewing(null);
      setRenewToken('');
      await onChanged();
    } catch (e) {
      setRenewError(failureText(e, i18n));
    } finally {
      setBusy(false);
    }
  }

  async function remove(login: string) {
    if (busy) return;
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
                  {account.rateLimits && account.rateLimits.length > 0 && (
                    <div className={account.rateLimits.some(isLow) ? 'item-meta warn-text' : 'item-meta'}>
                      <span>
                        {t('limits.label')}: {limitsText(account.rateLimits, i18n)}
                      </span>
                    </div>
                  )}
                  {account.warnings.map((warning) => (
                    <div key={warning} className="item-meta warn-text">
                      {warningText(warning, t)}
                    </div>
                  ))}
                  {renewing === account.login && (
                    <form className="renew-form" onSubmit={(e) => void renew(e, account.login)}>
                      <p className="small">
                        {t('accounts.renew.step1', { login: account.login })}{' '}
                        <a href={TOKEN_URL} target="_blank" rel="noreferrer">
                          {t('guide.step1.link')}
                          <span className="visually-hidden"> ({t('a11y.newTab')})</span>
                        </a>
                      </p>
                      <label htmlFor={`renew-${account.login}`} className="small">
                        {t('accounts.renew.step2')}
                      </label>
                      <div className="row">
                        <input
                          id={`renew-${account.login}`}
                          type="password"
                          autoComplete="off"
                          spellCheck={false}
                          placeholder="ghp_…"
                          value={renewToken}
                          onChange={(e) => setRenewToken(e.target.value)}
                          autoFocus
                        />
                        <button className="btn primary" type="submit" aria-disabled={busy || renewToken.trim() === ''}>
                          {busy ? t('accounts.add.busy') : t('accounts.renew.submit')}
                        </button>
                      </div>
                      {renewError && (
                        <p className="warn-text small" role="alert">
                          {renewError}
                        </p>
                      )}
                    </form>
                  )}
                </div>
                <div className="item-side">
                  <StatusPill state={account.state} t={t} />
                  <button
                    className={needsRenewal(account) ? 'btn primary' : 'btn'}
                    aria-expanded={renewing === account.login}
                    aria-label={t('accounts.renew.for', { login: account.login })}
                    onClick={() => openRenew(account.login)}
                  >
                    {t('accounts.renew')}
                  </button>
                  {confirming === account.login ? (
                    <>
                      <button
                        className="btn danger"
                        aria-disabled={busy}
                        aria-label={t('accounts.confirmFor', { login: account.login })}
                        onClick={() => void remove(account.login)}
                      >
                        {t('accounts.confirm')}
                      </button>
                      <button className="btn" onClick={() => setConfirming(null)}>
                        {t('accounts.cancel')}
                      </button>
                    </>
                  ) : (
                    <button
                      className="btn"
                      aria-label={t('accounts.removeAccount', { login: account.login })}
                      onClick={() => setConfirming(account.login)}
                    >
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
            <button className="btn primary" type="submit" aria-disabled={cannotSubmit}>
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
                  <span className="visually-hidden"> ({t('a11y.newTab')})</span>
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

/** Il token è scaduto, non vale più, o sta per scadere (negli stessi giorni dell'avviso): il rinnovo è il passo da fare. */
function needsRenewal(account: AccountStatus): boolean {
  if (account.state === 'invalid') return true;
  return account.tokenExpiresAt !== null && daysUntil(account.tokenExpiresAt) <= EXPIRY_WARNING_DAYS;
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
