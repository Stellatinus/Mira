import { useEffect, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { useAnnounce } from './announce';
import { KINDS, kindSummary, summaryLines } from './attention';
import type { Attention, Kind } from './attention';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { CheckIcon, ClockIcon, CloseIcon, CopyIcon, DetailIcon, ExternalIcon, EyeIcon, ItemIcon } from './icons';
import { reasonLabel, typeLabel } from './labels';
import { ciState } from './checks';
import { latestPerReviewer, newCommentsSince, reviewBadge } from './review';
import { canMarkSeen, isHidden } from './seen';
import { Tile } from './Tile';
import type { AccountStatus, ChecksInfo, DashboardItem, SeenEntry } from './types';

interface Props {
  item: DashboardItem | null;
  /** Gli account che vedono l'elemento (più di uno per un repository). */
  accounts: Pick<AccountStatus, 'login' | 'avatarUrl'>[];
  /** Gli account che hanno messo la stella al repository. */
  starredBy: string[];
  /** Cosa c'è di aperto nel repository selezionato (solo per gli elementi di tipo "repo"). */
  attention?: Attention;
  /** La voce «visto» di questo elemento, se c'è. */
  seen?: SeenEntry;
  /** Segna come visto, rimanda a domani o riporta da vedere. */
  onSeen: (action: 'seen' | 'snoozed' | 'unseen') => void;
  /** Per un repository: se se ne seguono le release (preferiti e aggiunti a mano, non tolti) e come cambiarlo. */
  followed: boolean;
  onFollow: (follow: boolean) => void;
  /** Azioni sulla notifica di GitHub: segna come letta, tutte quelle del repository, non seguire più. */
  onNotify: (action: 'read' | 'repoRead' | 'unsubscribe') => void;
  /** Cambia quando la scelta viene da tastiera: il focus passa al titolo del pannello. */
  focusSignal: number;
  onClose: () => void;
}

/** Sotto questa larghezza il pannello copre la pagina (vedi styles.css): si comporta da finestra. */
const NARROW = '(max-width: 1100px)';

function useNarrow(): boolean {
  const [narrow, setNarrow] = useState(() => window.matchMedia(NARROW).matches);
  useEffect(() => {
    const query = window.matchMedia(NARROW);
    const onChange = () => setNarrow(query.matches);
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);
  return narrow;
}

export function DetailPanel({ item, accounts, starredBy, attention, seen, onSeen, onNotify, followed, onFollow, focusSignal, onClose }: Props) {
  const i18n = useI18n();
  const { t, timeAgo, formatDate } = i18n;
  const announce = useAnnounce();
  // 'failed': gli appunti non sono disponibili; si mostra il link da copiare a mano.
  const [copied, setCopied] = useState<'idle' | 'copied' | 'failed'>('idle');
  // Il campo col link resta finché non si cambia elemento (il testo del pulsante torna normale dopo qualche secondo).
  const [manualOpen, setManualOpen] = useState(false);
  const manualLink = useRef<HTMLInputElement>(null);
  const narrow = useNarrow();
  const panel = useRef<HTMLElement>(null);
  const title = useRef<HTMLHeadingElement>(null);
  const handledSignal = useRef(focusSignal);

  useEffect(() => {
    setCopied('idle');
    setManualOpen(false);
  }, [item?.id]);

  // Il campo con il link compare già selezionato, pronto per Ctrl+C.
  useEffect(() => {
    if (manualOpen) manualLink.current?.select();
  }, [manualOpen]);

  // Scelta da tastiera, oppure pannello a tutta pagina: il focus va al titolo, così si legge subito.
  const itemId = item?.id;
  useEffect(() => {
    if (itemId === undefined) return;
    const requested = focusSignal !== handledSignal.current;
    handledSignal.current = focusSignal;
    if (requested || narrow) title.current?.focus();
  }, [itemId, focusSignal, narrow]);

  // A schermo stretto il pannello è una finestra: Tab non esce da lì finché è aperta.
  function trapFocus(event: KeyboardEvent<HTMLElement>) {
    if (!narrow || event.key !== 'Tab' || !panel.current) return;
    const focusable = [...panel.current.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')].filter(
      (node) => node.offsetParent !== null,
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;
    if (event.shiftKey && (active === first || active === title.current)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  async function copyLink() {
    if (!item) return;
    try {
      await navigator.clipboard.writeText(item.url);
      setCopied('copied');
      announce(t('detail.copied'));
      window.setTimeout(() => setCopied((current) => (current === 'copied' ? 'idle' : current)), 1500);
    } catch {
      // Appunti non disponibili (permesso negato, contesto non sicuro, finestra incorporata): si dice, e si offre il link.
      setCopied('failed');
      setManualOpen(true);
      announce(t('detail.copyFailed'));
      window.setTimeout(() => setCopied((current) => (current === 'failed' ? 'idle' : current)), 4000);
    }
  }

  // Senza selezione il pannello è solo un segnaposto: non è una regione per i lettori di schermo.
  if (!item) {
    return (
      <div className="panel" aria-hidden="true">
        <header className="panel-head">
          <DetailIcon />
          <span>{t('detail.title')}</span>
        </header>
        <div className="rule" />
        <div className="panel-empty">
          <div className="tile big">
            <ItemIcon type="other" size={44} />
          </div>
          <p className="muted">{t('detail.empty')}</p>
        </div>
      </div>
    );
  }

  return (
    <aside
      className="panel"
      ref={panel}
      aria-label={t('detail.title')}
      role={narrow ? 'dialog' : undefined}
      aria-modal={narrow ? true : undefined}
      onKeyDown={trapFocus}
    >
      <header className="panel-head">
        <DetailIcon />
        <span>{t('detail.title')}</span>
        <button className="icon-btn panel-close" onClick={onClose} aria-label={t('detail.close')}>
          <CloseIcon />
        </button>
      </header>
      <div className="rule" />

      {(
        <>
          <div className="panel-body">
            <Tile item={item} iconSize={52} big />
            <div className="rule" />

            <h2 className="panel-title" ref={title} tabIndex={-1}>
              {item.title}
            </h2>
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

            {item.review && <ReviewSection item={item} seen={seen} />}
            {item.checks && <ChecksSection checks={item.checks} />}

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
          {item.section === 'notifications' && (
            <div className="seen-box" role="group" aria-label={t('notify.group')}>
              <button className="btn" onClick={() => onNotify('read')}>
                <CheckIcon size={14} /> {t('notify.read')}
              </button>
              <button className="btn" onClick={() => onNotify('unsubscribe')}>
                <CloseIcon size={14} /> {t('notify.unsubscribe')}
              </button>
              <button className="btn" onClick={() => onNotify('repoRead')}>
                <CheckIcon size={14} /> {t('notify.readRepo', { repo: item.repo })}
              </button>
            </div>
          )}
          {item.type === 'repo' && (
            <div className="seen-box" role="group" aria-label={t('follow.group')}>
              <button className="btn" aria-pressed={followed} onClick={() => onFollow(!followed)}>
                <ItemIcon type="release" size={14} /> {followed ? t('follow.stop') : t('follow.start')}
              </button>
              <p className="muted small">{t('follow.hint')}</p>
            </div>
          )}
          {canMarkSeen(item) && (
            <div className="seen-box" role="group" aria-label={t('seen.group')}>
              {isHidden(seen, item, Date.now()) ? (
                <>
                  <p className="muted small">
                    {seen?.snoozeUntil
                      ? t('seen.status.snoozed', { when: new Date(seen.snoozeUntil).toLocaleString(i18n.language.locale, { weekday: 'long', hour: '2-digit', minute: '2-digit' }) })
                      : t('seen.status.seen')}
                  </p>
                  <button className="btn" onClick={() => onSeen('unseen')}>
                    <EyeIcon size={14} /> {t('seen.unmark')}
                  </button>
                </>
              ) : (
                <>
                  <button className="btn" onClick={() => onSeen('seen')}>
                    <CheckIcon size={14} /> {t('seen.mark')}
                  </button>
                  <button className="btn" onClick={() => onSeen('snoozed')}>
                    <ClockIcon size={14} /> {t('seen.snooze')}
                  </button>
                </>
              )}
            </div>
          )}
          <div className="panel-actions">
            <a className="action" href={item.url} target="_blank" rel="noreferrer">
              <span className="action-icon">
                <ExternalIcon />
              </span>
              {t('detail.open')}
              <span className="visually-hidden"> ({t('a11y.newTab')})</span>
            </a>
            <button className="action" onClick={() => void copyLink()}>
              <span className="action-icon">
                <CopyIcon />
              </span>
              {copied === 'copied' ? t('detail.copied') : copied === 'failed' ? t('detail.copyFailed') : t('detail.copyLink')}
            </button>
          </div>
          {manualOpen && (
            <div className="manual-link">
              <label htmlFor="manual-link" className="muted small">
                {t('detail.copyManual')}
              </label>
              <input id="manual-link" ref={manualLink} readOnly value={item.url} onFocus={(event) => event.currentTarget.select()} />
            </div>
          )}
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

/** Come sta andando una PR dell'utente: esito, review di ciascuno, ultimo commento e quanti ne sono arrivati di nuovi. */
function ReviewSection({ item, seen }: { item: DashboardItem; seen?: SeenEntry }) {
  const { t, timeAgo } = useI18n();
  const review = item.review!;
  const badge = reviewBadge(review);
  const reviews = latestPerReviewer(review.reviews).slice(0, 5);
  const lastComment = review.comments.length > 0 ? review.comments[review.comments.length - 1] : null;
  const fresh = newCommentsSince(item, seen);
  const who = (login: string | null) => login ?? t('review.someone');

  return (
    <>
      <h3 className="panel-label">{t('review.title')}</h3>
      {badge && <p className={`ci-review ${badge}`}>{t(`review.badge.${badge}` as 'review.badge.approved')}</p>}
      <ul className="kind-lines">
        {reviews.length === 0 && <li>{t('review.none')}</li>}
        {reviews.map((r) => (
          <li key={`${r.login}:${r.at}`}>
            {t('review.line', {
              who: who(r.login),
              what: t(`review.state.${r.state}` as 'review.state.APPROVED'),
              when: r.at ? timeAgo(r.at) : '',
            })}
          </li>
        ))}
        {lastComment && <li>{t('review.lastComment', { who: who(lastComment.login), when: timeAgo(lastComment.at) })}</li>}
        {review.commentCount > 0 && <li>{t('review.comments', { count: review.commentCount })}</li>}
        {fresh !== null && fresh > 0 && <li>{t('review.newComments', { count: fresh })}</li>}
      </ul>
      <div className="rule" />
    </>
  );
}

/** L'esito della CI sull'ultimo commit: stato, quanti controlli sono in corso e quali sono falliti (con il link ai dettagli). */
function ChecksSection({ checks }: { checks: ChecksInfo }) {
  const { t } = useI18n();
  const state = ciState(checks);

  return (
    <>
      <h3 className="panel-label">{t('checks.title')}</h3>
      {state && <p className={`ci-review ci-${state}`}>{t(`checks.badge.${state}` as 'checks.badge.success')}</p>}
      <ul className="kind-lines">
        <li>{t('checks.total', { count: checks.total })}</li>
        {checks.pending > 0 && <li>{t('checks.pending', { count: checks.pending })}</li>}
        {checks.failed.map((check) => (
          <li key={check.name}>
            {check.url ? (
              <a href={check.url} target="_blank" rel="noreferrer">
                {t('checks.failed', { name: check.name })}
                <span className="visually-hidden"> ({t('a11y.newTab')})</span>
              </a>
            ) : (
              t('checks.failed', { name: check.name })
            )}
          </li>
        ))}
      </ul>
      <div className="rule" />
    </>
  );
}
