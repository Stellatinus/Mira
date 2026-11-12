import { useMemo, useState } from 'react';
import type { KeyboardEvent, MouseEvent } from 'react';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { attentionLines, indicator, KINDS, repoKey } from './attention';
import type { Attention } from './attention';
import { ExternalIcon, ItemIcon, PullRequestIcon, StarIcon } from './icons';
import { reasonLabel, tagLabel } from './labels';
import type { ViewConfig } from './sections';
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
  query: string;
  mode: Mode;
  selectedId: string | null;
  /** Cosa c'è di aperto per ogni repository, ricavato dagli elementi già scaricati. */
  attention: Map<string, Attention>;
  onSelect: (id: string) => void;
  onManageAccounts: () => void;
}

interface Total {
  count: number;
  unknown: boolean;
}

const sectionTitle = (key: Section) => `section.${key}.title` as 'section.review.title';
const sectionHint = (key: Section) => `section.${key}.hint` as 'section.review.hint';

export function ItemsView({
  data,
  view,
  filter,
  query,
  mode,
  selectedId,
  attention,
  onSelect,
  onManageAccounts,
}: Props) {
  const i18n = useI18n();
  const { t, errorText } = i18n;
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const accountsByLogin = useMemo(() => new Map(data.accounts.map((a) => [a.login, a])), [data.accounts]);
  const starredKeys = useMemo(
    () => new Set(data.items.filter((i) => i.section === 'starred').map((i) => `${i.account}|${i.repo}`)),
    [data.items],
  );
  const needle = query.trim().toLowerCase();

  const bySection = useMemo(() => {
    const groups = new Map<Section, DashboardItem[]>(view.sections.map((key) => [key, []]));
    for (const item of data.items) {
      if (filter !== 'all' && item.account !== filter) continue;
      if (needle && !matches(item, needle)) continue;
      groups.get(item.section)?.push(item);
    }
    return groups;
  }, [data.items, view.sections, filter, needle]);

  // Totale reale per sezione (anche oltre gli elementi scaricati). Se per almeno un account
  // GitHub non lo indica, si mostra "N+". Con una ricerca attiva contano solo gli elementi trovati.
  const totals = useMemo(() => {
    const result = new Map<Section, Total>();
    for (const key of view.sections) {
      const shown = bySection.get(key)?.length ?? 0;
      let count = 0;
      let unknown = false;
      let found = false;
      for (const total of data.totals ?? []) {
        if (total.section !== key || (filter !== 'all' && total.account !== filter)) continue;
        found = true;
        if (total.total === null) unknown = true;
        else count += total.total;
      }
      result.set(key, !found || needle ? { count: shown, unknown: false } : { count: Math.max(count, shown), unknown });
    }
    return result;
  }, [data.totals, bySection, view.sections, filter, needle]);

  function totalLabel(key: Section): string {
    const total = totals.get(key);
    if (!total) return '0';
    return total.unknown ? `${total.count}+` : String(total.count);
  }

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
          <h2>{needle ? t('items.noResults.title') : t('items.empty.title')}</h2>
          <p className="muted">{needle ? t('items.noResults.text') : t('items.empty.text')}</p>
        </div>
      )}

      {view.sections.map((key) => {
        const items = bySection.get(key) ?? [];
        if (items.length === 0) return null;

        const isExpanded = expanded[key] === true;
        const visible = isExpanded ? items : items.slice(0, collapsedCount);
        const total = totals.get(key);
        const truncated = total !== undefined && (total.unknown || total.count > items.length);

        return (
          <section key={key} id={`sec-${key}`} className="group">
            <header className="group-head">
              <h2>{t(sectionTitle(key))}</h2>
              <span className="count" title={truncated ? t('items.mostRecent') : undefined}>
                {truncated ? t('items.countOf', { shown: items.length, total: totalLabel(key) }) : items.length}
              </span>
              <span className="hint muted">{t(sectionHint(key))}</span>
            </header>

            <div className={`cards ${mode}`}>
              {visible.map((item) => (
                <ItemCard
                  key={item.id}
                  item={item}
                  account={accountsByLogin.get(item.account)}
                  starred={item.type === 'repo' && starredKeys.has(`${item.account}|${item.repo}`)}
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

function matches(item: DashboardItem, needle: string): boolean {
  return [item.title, item.repo, item.author, item.description, item.language].some(
    (field) => field?.toLowerCase().includes(needle),
  );
}

interface CardProps {
  item: DashboardItem;
  account?: AccountStatus;
  starred: boolean;
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

function ItemCard({ item, account, starred, attention, selected, showAccount, onSelect }: CardProps) {
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
      <span className={starred ? 'ci-tag starred' : 'ci-tag'}>
        {item.type === 'repo' ? (
          <>
            <StarIcon size={15} filled={starred} />
            {item.stars}
          </>
        ) : (
          <span className="tag-text">{tagLabel(item, i18n)}</span>
        )}
      </span>
      {item.type === 'repo' && <AttentionBadges item={item} attention={attention} />}
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
      <div className="tile">
        <ItemIcon type={item.type} size={26} />
      </div>
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
