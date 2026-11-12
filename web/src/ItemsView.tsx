import { useMemo, useState } from 'react';
import type { KeyboardEvent, MouseEvent } from 'react';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { attentionLines, indicator, KINDS, repoKey } from './attention';
import type { Attention } from './attention';
import { ExternalIcon, ItemIcon, PullRequestIcon } from './icons';
import { reasonLabel, tagLabel } from './labels';
import type { ViewConfig } from './sections';
import { Tile } from './Tile';
import type { ViewModel } from './viewmodel';
import type { AccountStatus, Dashboard, DashboardError, DashboardItem, Section } from './types';

export type Mode = 'grid' | 'list' | 'compact';

/** Nella Home ogni sezione mostra poche card; nelle altre viste c'è molto più spazio. */
const COLLAPSED_HOME = 6;
const COLLAPSED_OTHER = 24;

interface Props {
  data: Dashboard;
  view: ViewConfig;
  /** Login dell'account scelto, oppure "all". */
  filter: string;
  mode: Mode;
  selectedId: string | null;
  /** Cosa c'è di aperto per ogni repository, ricavato dagli elementi già scaricati. */
  attention: Map<string, Attention>;
  /** Gli elementi di ogni sezione, già filtrati e ordinati, con i loro conteggi. */
  model: ViewModel;
  onSelect: (id: string) => void;
  onManageAccounts: () => void;
}

const sectionTitle = (key: Section) => `section.${key}.title` as 'section.review.title';
const sectionHint = (key: Section) => `section.${key}.hint` as 'section.review.hint';

export function ItemsView({ data, view, filter, mode, selectedId, attention, model, onSelect, onManageAccounts }: Props) {
  const i18n = useI18n();
  const { t, errorText } = i18n;
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const accountsByLogin = useMemo(() => new Map(data.accounts.map((a) => [a.login, a])), [data.accounts]);
  const { bySection, needle, totalLabel } = model;

  if (data.accounts.length === 0) {
    return (
      <div className="empty">
        <h2>{t('items.noAccount.title')}</h2>
        <p className="muted">{t('items.noAccount.text')}</p>
        <button className="btn primary" onClick={onManageAccounts}>
          {t('items.noAccount.action')}
        </button>
      </div>
    );
  }

  const shownTotal = view.sections.reduce((sum, key) => sum + (bySection.get(key)?.length ?? 0), 0);
  const repoFiltering = view.sections.some(model.narrowed);
  // Una vista con una sola sezione ha già il titolo e il conteggio nella riga in alto: niente intestazione ripetuta.
  const showGroupHeader = view.sections.length > 1;
  const collapsedCount = view.key === 'home' ? COLLAPSED_HOME : COLLAPSED_OTHER;
  const showAccount = data.accounts.length > 1 && filter === 'all';

  // "account / sezione: errore" (senza sezione quando l'errore riguarda l'account intero).
  const problemLine = (problem: DashboardError) => {
    const where = problem.section === 'account' ? '' : ` / ${t(sectionTitle(problem.section as Section))}`;
    return `${problem.account}${where}: ${errorText(problem.error)}`;
  };

  return (
    <>
      {view.key === 'home' && (
        <div className="stats">
          {view.sections.map((key) => (
            <a key={key} className="stat" href={`#sec-${key}`}>
              <strong>{totalLabel(key)}</strong>
              <span>{t(sectionTitle(key))}</span>
            </a>
          ))}
        </div>
      )}

      {shownTotal === 0 && (
        <div className="empty">
          <h2>{needle || repoFiltering ? t('items.noResults.title') : t('items.empty.title')}</h2>
          <p className="muted">
            {needle
              ? t('items.noResults.text')
              : repoFiltering
                ? t('items.noResults.filterText')
                : t('items.empty.text')}
          </p>
        </div>
      )}

      {view.sections.map((key) => {
        const items = bySection.get(key) ?? [];
        if (items.length === 0) return null;

        const isExpanded = expanded[key] === true;
        const visible = isExpanded ? items : items.slice(0, collapsedCount);
        const truncated = model.truncated(key);

        return (
          <section key={key} id={`sec-${key}`} className="group">
            {showGroupHeader && (
              <header className="group-head">
                <h2>{t(sectionTitle(key))}</h2>
                <span className="count" title={truncated ? t('items.mostRecent') : undefined}>
                  {model.countText(key)}
                </span>
                <span className="hint muted">{t(sectionHint(key))}</span>
              </header>
            )}

            <div className={`cards ${mode}`}>
              {visible.map((item) => (
                <ItemCard
                  key={item.id}
                  item={item}
                  account={accountsByLogin.get(item.account)}
                  attention={item.type === 'repo' ? attention.get(repoKey(item.repo)) : undefined}
                  selected={item.id === selectedId}
                  showAccount={showAccount}
                  onSelect={onSelect}
                />
              ))}
            </div>

            {items.length > collapsedCount && (
              <button className="more" onClick={() => setExpanded({ ...expanded, [key]: !isExpanded })}>
                {isExpanded
                  ? t('items.showLess')
                  : truncated
                    ? t('items.showRecent', { count: items.length })
                    : t('items.showAll', { count: items.length })}
              </button>
            )}
            {truncated && isExpanded && (
              <p className="truncated muted">
                {t('items.truncatedNote', { shown: items.length, total: totalLabel(key) })}
              </p>
            )}
          </section>
        );
      })}

      {data.errors.length > 0 && (
        <details className="problems">
          <summary>{t('items.problems', { count: data.errors.length })}</summary>
          <ul>
            {data.errors.map((problem) => (
              <li key={`${problem.account}:${problem.section}`}>{problemLine(problem)}</li>
            ))}
          </ul>
        </details>
      )}
    </>
  );
}

interface CardProps {
  item: DashboardItem;
  account?: AccountStatus;
  attention?: Attention;
  selected: boolean;
  showAccount: boolean;
  onSelect: (id: string) => void;
}

/**
 * Indicatori sulle card dei repository, uno per le PR e uno per le issue, così un numero non può
 * passare per l'altro. Il numero è quante ce ne sono di aperte nel repository; il colore dice il tipo
 * (PR blu, issue arancio); il riempimento dice se tra queste qualcosa aspetta te (pieno) o no (solo contorno).
 */
function AttentionBadges({ item, attention }: { item: DashboardItem; attention?: Attention }) {
  const { t } = useI18n();

  const badges = KINDS.map((kind) => {
    const { count, urgent } = indicator(item, attention, kind);
    if (count === 0) return null;

    const text = `${t(`attention.kind.${kind}` as 'attention.kind.pr')}: ${attentionLines(item, attention, kind, t).join(' · ')}`;
    return (
      <span key={kind} className={urgent ? `ci-attn ${kind} urgent` : `ci-attn ${kind}`} title={text} aria-label={text} role="img">
        {kind === 'pr' ? <PullRequestIcon size={13} /> : <ItemIcon type="issue" size={13} />}
        {count}
      </span>
    );
  });

  if (badges.every((badge) => badge === null)) return null;
  return <span className="ci-attns">{badges}</span>;
}

function ItemCard({ item, account, attention, selected, showAccount, onSelect }: CardProps) {
  const i18n = useI18n();
  const { t, timeAgo } = i18n;
  const when = item.reason ? `${reasonLabel(item.reason, i18n)} · ${timeAgo(item.updatedAt)}` : timeAgo(item.updatedAt);

  function onKey(event: KeyboardEvent<HTMLElement>) {
    if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      onSelect(item.id);
    }
  }

  const stop = (event: MouseEvent) => event.stopPropagation();

  return (
    <article
      className={selected ? 'item-card selected' : 'item-card'}
      role="button"
      tabIndex={0}
      aria-pressed={selected}
      onClick={() => onSelect(item.id)}
      onKeyDown={onKey}
    >
      {item.type === 'repo' ? (
        // Le stelle non sono sulla card: stanno nel pannello dei dettagli.
        <AttentionBadges item={item} attention={attention} />
      ) : (
        <span className="ci-tag">
          <span className="tag-text">{tagLabel(item, i18n)}</span>
        </span>
      )}
      <a
        className="ci-open"
        href={item.url}
        target="_blank"
        rel="noreferrer"
        onClick={stop}
        aria-label={t('items.openOnGitHub')}
      >
        <ExternalIcon size={15} />
      </a>
      <Tile item={item} iconSize={26} />
      <h3 className="ci-title" title={item.title}>
        {item.title}
      </h3>
      <div className="rule" />
      <div className="ci-meta">
        <span className="k">{item.repo}</span>
        <span className="v">{when}</span>
      </div>
      {showAccount && (
        <span className="ci-acct" title={item.account}>
          <Avatar account={account ?? { login: item.account, avatarUrl: null }} size={22} />
        </span>
      )}
    </article>
  );
}
