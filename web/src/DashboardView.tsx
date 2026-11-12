import { useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import { accountAlerts } from './alerts';
import { reasonLabel, timeAgo } from './format';
import { ItemIcon } from './icons';
import type { AccountStatus, Dashboard, DashboardItem, Section } from './types';

interface SectionConfig {
  key: Section;
  title: string;
  hint: string;
  color: string;
}

const SECTIONS: SectionConfig[] = [
  { key: 'review', title: 'Review richieste a te', hint: 'PR che aspettano il tuo parere', color: 'var(--c-review)' },
  { key: 'assigned', title: 'Assegnate a te', hint: 'Issue e PR di cui sei assegnatario', color: 'var(--c-assigned)' },
  { key: 'notifications', title: 'Notifiche non lette', hint: 'Dalla casella di GitHub', color: 'var(--c-notif)' },
  { key: 'mentions', title: 'Menzioni', hint: 'Dove qualcuno ti ha taggato', color: 'var(--c-mention)' },
  { key: 'mine-prs', title: 'Le tue PR aperte', hint: 'In attesa di approvazione o merge', color: 'var(--c-prs)' },
  { key: 'mine-issues', title: 'Le tue issue aperte', hint: 'Quelle che hai aperto tu', color: 'var(--c-issues)' },
];

const COLLAPSED_COUNT = 8;
const FILTER_KEY = 'mira.accountFilter';

function readFilter(): string {
  try {
    return localStorage.getItem(FILTER_KEY) ?? 'all';
  } catch {
    return 'all';
  }
}

function writeFilter(value: string) {
  try {
    localStorage.setItem(FILTER_KEY, value);
  } catch {
    // storage non disponibile: il filtro vale solo per questa sessione
  }
}

interface Props {
  data: Dashboard;
  onManageAccounts: () => void;
}

export function DashboardView({ data, onManageAccounts }: Props) {
  const [storedFilter, setStoredFilter] = useState(readFilter);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const filter = data.accounts.some((a) => a.login === storedFilter) ? storedFilter : 'all';
  const accountsByLogin = useMemo(() => new Map(data.accounts.map((a) => [a.login, a])), [data.accounts]);
  const alerts = accountAlerts(data.accounts);

  const bySection = useMemo(() => {
    const groups = new Map<Section, DashboardItem[]>(SECTIONS.map((s) => [s.key, []]));
    for (const item of data.items) {
      if (filter === 'all' || item.account === filter) groups.get(item.section)?.push(item);
    }
    return groups;
  }, [data.items, filter]);

  // Totale reale per sezione (anche oltre gli elementi scaricati). "unknown" se per almeno
  // un account GitHub non ha indicato il totale: in quel caso si mostra "N+".
  const totals = useMemo(() => {
    const result = new Map<Section, { count: number; unknown: boolean }>();
    for (const section of SECTIONS) {
      const shown = bySection.get(section.key)?.length ?? 0;
      let count = 0;
      let unknown = false;
      let found = false;
      for (const t of data.totals ?? []) {
        if (t.section !== section.key || (filter !== 'all' && t.account !== filter)) continue;
        found = true;
        if (t.total === null) unknown = true;
        else count += t.total;
      }
      result.set(section.key, found ? { count: Math.max(count, shown), unknown } : { count: shown, unknown: false });
    }
    return result;
  }, [data.totals, bySection, filter]);

  function totalLabel(key: Section): string {
    const t = totals.get(key);
    if (!t) return '0';
    return t.unknown ? `${t.count}+` : String(t.count);
  }

  function chooseFilter(value: string) {
    setStoredFilter(value);
    writeFilter(value);
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

  const total = SECTIONS.reduce((sum, s) => sum + (bySection.get(s.key)?.length ?? 0), 0);

  return (
    <>
      {alerts.map((alert) => (
        <div key={alert.key} className={`alert ${alert.level}`} role="alert">
          <span>{alert.text}</span>
          <button className="link" onClick={onManageAccounts}>
            Gestisci account
          </button>
        </div>
      ))}

      {data.accounts.length > 1 && (
        <div className="filters" role="group" aria-label="Filtra per account">
          <button className={filter === 'all' ? 'chip active' : 'chip'} onClick={() => chooseFilter('all')}>
            Tutti
          </button>
          {data.accounts.map((account) => (
            <button
              key={account.login}
              className={filter === account.login ? 'chip active' : 'chip'}
              onClick={() => chooseFilter(account.login)}
            >
              <Avatar account={account} />
              {account.login}
            </button>
          ))}
        </div>
      )}

      <div className="stats">
        {SECTIONS.map((section) => (
          <a key={section.key} className="stat" href={`#sec-${section.key}`} style={{ '--dot': section.color } as CSSProperties}>
            <strong>{totalLabel(section.key)}</strong>
            <span>{section.title}</span>
          </a>
        ))}
      </div>

      {total === 0 && (
        <div className="empty">
          <h2>Tutto in ordine</h2>
          <p className="muted">Niente che richieda la tua attenzione in questo momento.</p>
        </div>
      )}

      {SECTIONS.map((section) => {
        const items = bySection.get(section.key) ?? [];
        if (items.length === 0) return null;

        const isExpanded = expanded[section.key] === true;
        const visible = isExpanded ? items : items.slice(0, COLLAPSED_COUNT);
        const sectionTotal = totals.get(section.key);
        const truncated = sectionTotal !== undefined && (sectionTotal.unknown || sectionTotal.count > items.length);

        return (
          <section key={section.key} id={`sec-${section.key}`} className="card" style={{ '--dot': section.color } as CSSProperties}>
            <header className="card-head">
              <span className="dot" />
              <h2>{section.title}</h2>
              <span className="count" title={truncated ? 'Mostrati i più recenti' : undefined}>
                {truncated ? `${items.length} di ${totalLabel(section.key)}` : items.length}
              </span>
              <span className="hint muted">{section.hint}</span>
            </header>
            <ul className="items">
              {visible.map((item) => (
                <ItemRow
                  key={item.id}
                  item={item}
                  account={accountsByLogin.get(item.account)}
                  showAccount={data.accounts.length > 1 && filter === 'all'}
                />
              ))}
            </ul>
            {items.length > COLLAPSED_COUNT && (
              <button className="more" onClick={() => setExpanded({ ...expanded, [section.key]: !isExpanded })}>
                {isExpanded ? 'Mostra meno' : truncated ? `Mostra i ${items.length} più recenti` : `Mostra tutti (${items.length})`}
              </button>
            )}
            {truncated && isExpanded && (
              <p className="truncated muted">
                Qui ci sono i {items.length} aggiornati più di recente su {totalLabel(section.key)}.
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

function Avatar({ account }: { account?: AccountStatus }) {
  if (!account?.avatarUrl) return <span className="avatar placeholder" />;
  return <img className="avatar" src={account.avatarUrl} alt="" width={16} height={16} />;
}

function ItemRow({ item, account, showAccount }: { item: DashboardItem; account?: AccountStatus; showAccount: boolean }) {
  return (
    <li className="item">
      <span className={`item-icon type-${item.type}`}>
        <ItemIcon type={item.type} />
      </span>
      <div className="item-main">
        <a className="item-title" href={item.url} target="_blank" rel="noreferrer">
          {item.title}
        </a>
        <div className="item-meta">
          <span className="repo">{item.repo}</span>
          {item.author && item.author !== item.account && <span>di {item.author}</span>}
          <span>{timeAgo(item.updatedAt)}</span>
        </div>
      </div>
      <div className="item-side">
        {item.draft && <span className="badge">Bozza</span>}
        {item.reason && <span className="badge">{reasonLabel(item.reason)}</span>}
        {item.comments > 0 && <span className="badge subtle">{item.comments} {item.comments === 1 ? 'commento' : 'commenti'}</span>}
        {showAccount && (
          <span className="account-chip">
            <Avatar account={account} />
            {item.account}
          </span>
        )}
      </div>
    </li>
  );
}
