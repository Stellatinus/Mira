import { useState } from 'react';
import type { FormEvent } from 'react';
import { addAccount, removeAccount } from './api';
import { Avatar } from './Avatar';
import { daysUntil, formatDate } from './format';
import type { AccountStatus } from './types';

const TOKEN_URL = 'https://github.com/settings/tokens/new?description=Mira&scopes=notifications,repo,read:org';

interface Props {
  accounts: AccountStatus[];
  onChanged: () => Promise<void>;
}

export function AccountsView({ accounts, onChanged }: Props) {
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
      setError(e instanceof Error ? e.message : 'Errore sconosciuto');
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
      setError(e instanceof Error ? e.message : 'Errore sconosciuto');
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <section className="card">
        <header className="card-head">
          <h2>Account collegati</h2>
          <span className="count">{accounts.length}</span>
        </header>
        {accounts.length === 0 ? (
          <p className="muted pad">Nessun account ancora. Aggiungine uno qui sotto.</p>
        ) : (
          <ul className="items">
            {accounts.map((account) => (
              <li key={account.login} className="item account-row">
                <Avatar account={account} size={36} />
                <div className="item-main">
                  <strong>{account.login}</strong>
                  {account.name && <span className="muted"> · {account.name}</span>}
                  <div className="item-meta">
                    <span>{expiryText(account)}</span>
                  </div>
                  {account.warnings.map((warning) => (
                    <div key={warning} className="item-meta warn-text">
                      {warning}
                    </div>
                  ))}
                </div>
                <div className="item-side">
                  <StatusPill state={account.state} />
                  {confirming === account.login ? (
                    <>
                      <button className="btn danger" disabled={busy} onClick={() => void remove(account.login)}>
                        Conferma
                      </button>
                      <button className="btn" onClick={() => setConfirming(null)}>
                        Annulla
                      </button>
                    </>
                  ) : (
                    <button className="btn" onClick={() => setConfirming(account.login)}>
                      Rimuovi
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
          <h2>Aggiungi un account</h2>
        </header>
        <form className="add-form" onSubmit={(e) => void submit(e)}>
          <label htmlFor="token">Personal access token (classic)</label>
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
              {busy ? 'Verifico…' : 'Collega account'}
            </button>
          </div>
          {error && <p className="warn-text">{error}</p>}
          <p className="muted small">
            Il token resta sul tuo computer, cifrato, e non viene mai mostrato nel browser.
          </p>
        </form>
      </section>

      <section className="card">
        <header className="card-head">
          <h2>Come creare il token</h2>
        </header>
        <ol className="guide">
          <li>
            Accedi a GitHub con l'account che vuoi collegare, poi{' '}
            <a href={TOKEN_URL} target="_blank" rel="noreferrer">
              apri la pagina di creazione del token
            </a>{' '}
            (nome e permessi sono già compilati).
          </li>
          <li>
            Controlla che siano selezionati <code>notifications</code>, <code>repo</code> e <code>read:org</code>.
          </li>
          <li>
            Scegli una scadenza (consigliati 90 giorni): Mira ti avvisa quando il token sta per scadere.
          </li>
          <li>Genera il token, copialo — GitHub lo mostra una sola volta — e incollalo qui sopra.</li>
          <li>
            Se l'account appartiene a organizzazioni con SAML SSO, dopo la creazione usa «Configure SSO» accanto al token
            per autorizzarle.
          </li>
          <li>Per collegare un altro account, ripeti i passi accedendo prima con quell'account.</li>
        </ol>
        <p className="muted small pad">
          Servono token <strong>classic</strong>: i token fine-grained non danno accesso alle notifiche.
        </p>
      </section>
    </>
  );
}

function StatusPill({ state }: { state: AccountStatus['state'] }) {
  const label = state === 'ok' ? 'Attivo' : state === 'invalid' ? 'Scaduto o non valido' : 'Errore';
  return <span className={`pill ${state}`}>{label}</span>;
}

function expiryText(account: AccountStatus): string {
  if (account.state === 'invalid') return 'Token scaduto o revocato';
  if (account.state === 'error') return 'Stato non verificabile';
  if (!account.tokenExpiresAt) return 'Nessuna scadenza impostata';

  const days = daysUntil(account.tokenExpiresAt);
  const date = formatDate(account.tokenExpiresAt);
  if (days <= 0) return `Scade oggi (${date})`;
  return `Scade tra ${days} ${days === 1 ? 'giorno' : 'giorni'} (${date})`;
}
