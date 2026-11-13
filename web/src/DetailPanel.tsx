import { useEffect, useState } from 'react';
import { useAnnounce } from './announce';
import { KINDS, kindSummary, summaryLines } from './attention';
import type { Attention, Kind } from './attention';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { CloseIcon, CopyIcon, DetailIcon, ExternalIcon, ItemIcon } from './icons';
import { reasonLabel, typeLabel } from './labels';
import { Tile } from './Tile';
import type { AccountStatus, DashboardItem } from './types';

interface Props {
  item: DashboardItem | null;
  /** Gli account che vedono l'elemento (più di uno per un repository). */
  accounts: Pick<AccountStatus, 'login' | 'avatarUrl'>[];
  /** Gli account che hanno messo la stella al repository. */
  starredBy: string[];
  /** Cosa c'è di aperto nel repository selezionato (solo per gli elementi di tipo "repo"). */
  attention?: Attention;
  onClose: () => void;
}

export function DetailPanel({ item, accounts, starredBy, attention, onClose }: Props) {
  const i18n = useI18n();
  const { t, timeAgo, formatDate } = i18n;
  const announce = useAnnounce();
  const [copied, setCopied] = useState(false);

  useEffect(() => setCopied(false), [item?.id]);

  async function copyLink() {
    if (!item) return;
    try {
      await navigator.clipboard.writeText(item.url);
      setCopied(true);
      announce(t('detail.copied'));
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      // appunti non disponibili: l'utente può comunque aprire il link
    }
  }

  return (
    <aside className="panel" aria-label={t('detail.title')}>
      <header className="panel-head">
        <DetailIcon />
        <span>{t('detail.title')}</span>
        <button className="icon-btn panel-close" onClick={onClose} aria-label={t('detail.close')}>
          <CloseIcon />
        </button>
      </header>
      <div className="rule" />

      {!item ? (
        <div className="panel-empty">
          <div className="tile big">
            <ItemIcon type="other" size={44} />
          </div>
          <p className="muted">{t('detail.empty')}</p>
        </div>
      ) : (
        <>
          <div className="panel-body">
            <Tile item={item} iconSize={52} big />
            <div className="rule" />

            <h2 className="panel-title">{item.title}</h2>
            <p className="panel-sub">
              <span className="mono">{item.repo}</span>
            </p>

            <h3 className="panel-label">{t('detail.description')}</h3>
            <p className={item.description ? 'panel-text' : 'panel-text muted'}>
              {item.description ?? t('detail.noDescription')}
            </p>
            <div className="rule" />

            <dl className="facts">
              <dt>{t('detail.type')}</dt>
              <dd>{typeLabel(item, i18n)}</dd>
              {item.reason && (
                <>
                  <dt>{t('detail.reason')}</dt>
                  <dd>{reasonLabel(item.reason, i18n)}</dd>
                </>
              )}
              {item.author && (
                <>
                  <dt>{t('detail.author')}</dt>
                  <dd>{item.author}</dd>
                </>
              )}
              {item.language && (
                <>
                  <dt>{t('detail.language')}</dt>
                  <dd>{item.language}</dd>
                </>
              )}
              {item.type === 'repo' && (
                <>
                  <dt>{t('detail.stars')}</dt>
                  <dd>
                    {item.stars}
                    {starredBy.length > 0 ? t('detail.starredBy', { accounts: starredBy.join(', ') }) : ''}
                  </dd>
                  <dt>{t('detail.fork')}</dt>
                  <dd>
                    {item.fork
                      ? item.forkOf
                        ? t('detail.forkOf', { parent: item.forkOf })
                        : t('detail.forkYes')
                      : t('detail.forkNo')}
                  </dd>
                </>
              )}
              {item.comments > 0 && (
                <>
                  <dt>{t('detail.comments')}</dt>
                  <dd>{item.comments}</dd>
                </>
              )}
              <dt>{t('detail.updated')}</dt>
              <dd title={formatDate(item.updatedAt)}>{timeAgo(item.updatedAt)}</dd>
            </dl>
            <div className="rule" />

            {item.type === 'repo' &&
              KINDS.map((kind) => <KindSection key={kind} kind={kind} item={item} attention={attention} />)}

            <h3 className="panel-label">{t('detail.seenBy')}</h3>
            <div className="shared-list">
              {accounts.map((account) => (
                <div key={account.login} className="shared">
                  <Avatar account={account} size={28} />
                  <span>{account.login}</span>
                </div>
              ))}
            </div>
          </div>

          <div className="rule" />
          <div className="panel-actions">
            <a className="action" href={item.url} target="_blank" rel="noreferrer">
              <span className="action-icon">
                <ExternalIcon />
              </span>
              {t('detail.open')}
            </a>
            <button className="action" onClick={() => void copyLink()}>
              <span className="action-icon">
                <CopyIcon />
              </span>
              {copied ? t('detail.copied') : t('detail.copyLink')}
            </button>
          </div>
        </>
      )}
    </aside>
  );
}

/**
 * Una sezione del pannello di un repository, per le PR o per le issue: quante sono aperte e, tra
 * parentesi, quante sono assegnate a te; per le issue anche quante non hanno assegnatario; poi il resto
 * che aspetta te. Non compare se non c'è nulla.
 */
function KindSection({ kind, item, attention }: { kind: Kind; item: DashboardItem; attention?: Attention }) {
  const { t } = useI18n();
  const lines = summaryLines(kindSummary(item, attention, kind, t), t);
  if (lines.length === 0) return null;

  return (
    <>
      <h3 className="panel-label kind-head">
        <span className={`kind-dot ${kind}`} aria-hidden />
        {t(`detail.section.${kind}` as 'detail.section.pr')}
      </h3>
      <ul className="kind-lines">
        {lines.map((line) => (
          <li key={line}>{line}</li>
        ))}
      </ul>
      <div className="rule" />
    </>
  );
}
