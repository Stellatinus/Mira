import { useMemo, useState } from 'react';
import type { KeyboardEvent, MouseEvent } from 'react';
import { Avatar } from './Avatar';
import { ExternalIcon, ItemIcon, StarIcon } from './icons';
import { reasonLabel, timeAgo } from './format';
import { SECTIONS } from './sections';
import type { ViewConfig } from './sections';
import type { AccountStatus, Dashboard, DashboardItem, Section } from './types';

export type Mode = 'grid' | 'list' | 'compact';

/** Nella Home ogni sezione mostra poche card; nelle altre viste ce n'è molto più spazio. */
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
  onSelect: (id: string) => void;
  onManageAccounts: () => void;
}

interface Total {
  count: number;
  unknown: boolean;
}

export function ItemsView({ data, view, filter, query, mode, selectedId, onSelect, onManageAccounts }: Props) {
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
      for (const t of data.totals ?? []) {
        if (t.section !== key || (filter !== 'all' && t.account !== filter)) continue;
        found = true;
        if (t.total === null) unknown = true;
        else count += t.total;
      }
      result.set(key, !found || needle ? { count: shown, unknown: false } : { count: Math.max(count, shown), unknown });
    }
    return result;
  }, [data.totals, bySection, view.sections, filter, needle]);

  function totalLabel(key: Section): string {
    const t = totals.get(key);
    if (!t) return '0';
    return t.unknown ? `${t.count}+` : String(t.count);
  }

  if (data.accounts.length === 0) {
    return (
      <div className="empty">
        <h2>Nessun account collegato</h2>
        <p className="muted">Collega un account GitHub con un token per vedere qui tutto ciò che ti riguarda.</p>
        <button className="btn primary" onClick={onManageAccounts}>
          Collega un account
        </button>
      </div>
    );
  }

  const shownTotal = view.sections.reduce((sum, key) => sum + (bySection.get(key)?.length ?? 0), 0);
  const collapsedCount = view.key === 'home' ? COLLAPSED_HOME : COLLAPSED_OTHER;
  const showAccount = data.accounts.length > 1 && filter === 'all';

  return (
    <>
      {view.key === 'home' && (
        <div className="stats">
          {view.sections.map((key) => (
            <a key={key} className="stat" href={`#sec-${key}`}>
              <strong>{totalLabel(key)}</strong>
              <span>{SECTIONS.find((s) => s.key === key)?.title}</span>
            </a>
          ))}
        </div>
      )}

      {shownTotal === 0 && (
        <div className="empty">
          <h2>{needle ? 'Nessun risultato' : 'Tutto in ordine'}</h2>
          <p className="muted">
            {needle ? 'Nessun elemento corrisponde alla ricerca.' : 'Niente da mostrare in questa sezione.'}
          </p>
        </div>
      )}

      {view.sections.map((key) => {
        const config = SECTIONS.find((s) => s.key === key);
        const items = bySection.get(key) ?? [];
        if (!config || items.length === 0) return null;

        const isExpanded = expanded[key] === true;
        const visible = isExpanded ? items : items.slice(0, collapsedCount);
        const total = totals.get(key);
        const truncated = total !== undefined && (total.unknown || total.count > items.length);

        return (
          <section key={key} id={`sec-${key}`} className="group">
            <header className="group-head">
              <h2>{config.title}</h2>
              <span className="count" title={truncated ? 'Mostrati i più recenti' : undefined}>
                {truncated ? `${items.length} di ${totalLabel(key)}` : items.length}
              </span>
              <span className="hint muted">{config.hint}</span>
            </header>

            <div className={`cards ${mode}`}>
              {visible.map((item) => (
                <ItemCard
                  key={item.id}
                  item={item}
                  account={accountsByLogin.get(item.account)}
                  starred={item.type === 'repo' && starredKeys.has(`${item.account}|${item.repo}`)}
                  selected={item.id === selectedId}
                  showAccount={showAccount}
                  onSelect={onSelect}
                />
              ))}
            </div>

            {items.length > collapsedCount && (
              <button className="more" onClick={() => setExpanded({ ...expanded, [key]: !isExpanded })}>
                {isExpanded
                  ? 'Mostra meno'
                  : truncated
                    ? `Mostra i ${items.length} più recenti`
                    : `Mostra tutti (${items.length})`}
              </button>
            )}
            {truncated && isExpanded && (
              <p className="truncated muted">
                Qui ci sono i {items.length} aggiornati più di recente su {totalLabel(key)}.
              </p>
            )}
          </section>
        );
      })}

      {data.errors.length > 0 && (
        <details className="problems">
          <summary>{data.errors.length} richieste non sono andate a buon fine</summary>
          <ul>
            {data.errors.map((message) => (
              <li key={message}>{message}</li>
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
  selected: boolean;
  showAccount: boolean;
  onSelect: (id: string) => void;
}

function ItemCard({ item, account, starred, selected, showAccount, onSelect }: CardProps) {
  const when = item.reason ? `${reasonLabel(item.reason)} · ${timeAgo(item.updatedAt)}` : timeAgo(item.updatedAt);

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
          <span className="tag-text">{tag(item)}</span>
        )}
      </span>
      <a className="ci-open" href={item.url} target="_blank" rel="noreferrer" onClick={stop} aria-label="Apri su GitHub">
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

function tag(item: DashboardItem): string {
  switch (item.type) {
    case 'pr':
      return item.draft ? 'PR · bozza' : 'PR';
    case 'issue':
      return 'Issue';
    case 'release':
      return 'Release';
    case 'commit':
      return 'Commit';
    case 'discussion':
      return 'Discussione';
    default:
      return 'Notifica';
  }
}
