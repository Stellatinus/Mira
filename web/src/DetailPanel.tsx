import { Fragment, useEffect, useState } from 'react';
import { attentionLines, indicator, KINDS } from './attention';
import type { Attention } from './attention';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { CloseIcon, CopyIcon, DetailIcon, ExternalIcon, ItemIcon } from './icons';
import { reasonLabel, typeLabel } from './labels';
import { Tile } from './Tile';
import type { AccountStatus, DashboardItem } from './types';

interface Props {
  item: DashboardItem | null;
  account?: AccountStatus;
  starred: boolean;
  /** Cosa c'è di aperto nel repository selezionato (solo per gli elementi di tipo "repo"). */
  attention?: Attention;
  onClose: () => void;
}

export function DetailPanel({ item, account, starred, attention, onClose }: Props) {
  const i18n = useI18n();
  const { t, timeAgo, formatDate } = i18n;
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
                    {starred ? t('detail.starredByYou') : ''}
                  </dd>
                </>
              )}
              {item.type === 'repo' &&
                KINDS.filter((kind) => indicator(item, attention, kind).count > 0).map((kind) => (
                  <Fragment key={kind}>
                    <dt className="kind-label">
                      <span className={`kind-dot ${kind}`} aria-hidden />
                      {t(`attention.kind.${kind}` as 'attention.kind.pr')}
                    </dt>
                    <dd>
                      {attentionLines(item, attention, kind, t).map((line) => (
                        <div key={line}>{line}</div>
                      ))}
                    </dd>
                  </Fragment>
                ))}
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

            <h3 className="panel-label">{t('detail.seenBy')}</h3>
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
