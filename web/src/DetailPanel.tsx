import { useEffect, useState } from 'react';
import { Avatar } from './Avatar';
import { CloseIcon, CopyIcon, DetailIcon, ExternalIcon, ItemIcon } from './icons';
import { formatDate, reasonLabel, timeAgo, typeLabel } from './format';
import type { AccountStatus, DashboardItem } from './types';

interface Props {
  item: DashboardItem | null;
  account?: AccountStatus;
  starred: boolean;
  onClose: () => void;
}

export function DetailPanel({ item, account, starred, onClose }: Props) {
  const [copied, setCopied] = useState(false);

  useEffect(() => setCopied(false), [item?.id]);

  async function copyLink() {
    if (!item) return;
    try {
      await navigator.clipboard.writeText(item.url);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      // appunti non disponibili: l'utente può comunque aprire il link
    }
  }

  return (
    <aside className="panel" aria-label="Dettagli">
      <header className="panel-head">
        <DetailIcon />
        <span>Dettagli</span>
        <button className="icon-btn panel-close" onClick={onClose} aria-label="Chiudi">
          <CloseIcon />
        </button>
      </header>
      <div className="rule" />

      {!item ? (
        <div className="panel-empty">
          <div className="tile big">
            <ItemIcon type="other" size={44} />
          </div>
          <p className="muted">Seleziona un elemento per vedere i dettagli.</p>
        </div>
      ) : (
        <>
          <div className="panel-body">
            <div className="tile big">
              <ItemIcon type={item.type} size={52} />
            </div>
            <div className="rule" />

            <h2 className="panel-title">{item.title}</h2>
            <p className="panel-sub">
              <span className="mono">{item.repo}</span>
            </p>

            <h3 className="panel-label">Descrizione</h3>
            <p className={item.description ? 'panel-text' : 'panel-text muted'}>
              {item.description ?? 'Nessuna descrizione.'}
            </p>
            <div className="rule" />

            <dl className="facts">
              <dt>Tipo</dt>
              <dd>{typeLabel(item)}</dd>
              {item.reason && (
                <>
                  <dt>Motivo</dt>
                  <dd>{reasonLabel(item.reason)}</dd>
                </>
              )}
              {item.author && (
                <>
                  <dt>Autore</dt>
                  <dd>{item.author}</dd>
                </>
              )}
              {item.language && (
                <>
                  <dt>Linguaggio</dt>
                  <dd>{item.language}</dd>
                </>
              )}
              {item.type === 'repo' && (
                <>
                  <dt>Stelle</dt>
                  <dd>
                    {item.stars}
                    {starred ? ' · l’hai messa tu' : ''}
                  </dd>
                </>
              )}
              {item.comments > 0 && (
                <>
                  <dt>Commenti</dt>
                  <dd>{item.comments}</dd>
                </>
              )}
              <dt>Aggiornato</dt>
              <dd title={formatDate(item.updatedAt)}>{timeAgo(item.updatedAt)}</dd>
            </dl>
            <div className="rule" />

            <h3 className="panel-label">Visto da</h3>
            <div className="shared">
              <Avatar account={account ?? { login: item.account, avatarUrl: null }} size={28} />
              <span>{item.account}</span>
            </div>
          </div>

          <div className="rule" />
          <div className="panel-actions">
            <a className="action" href={item.url} target="_blank" rel="noreferrer">
              <span className="action-icon">
                <ExternalIcon />
              </span>
              Apri
            </a>
            <button className="action" onClick={() => void copyLink()}>
              <span className="action-icon">
                <CopyIcon />
              </span>
              {copied ? 'Copiato' : 'Copia link'}
            </button>
          </div>
        </>
      )}
    </aside>
  );
}
