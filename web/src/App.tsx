import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { getDashboard } from './api';
import { accountAlerts } from './alerts';
import { timeAgo } from './format';
import { CompactIcon, GridIcon, ListIcon, RefreshIcon, SearchIcon } from './icons';
import { AccountsView } from './AccountsView';
import { DetailPanel } from './DetailPanel';
import { ItemsView } from './ItemsView';
import type { Mode } from './ItemsView';
import { Sidebar } from './Sidebar';
import { VIEWS } from './sections';
import type { ViewKey } from './sections';
import type { Dashboard } from './types';

const REFRESH_MS = 5 * 60 * 1000;
const FILTER_KEY = 'mira.accountFilter';
const MODE_KEY = 'mira.mode';

function readStored(key: string, fallback: string): string {
  try {
    return localStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}

function writeStored(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // storage non disponibile: la preferenza vale solo per questa sessione
  }
}

const MODES: { key: Mode; label: string; icon: ReactNode }[] = [
  { key: 'list', label: 'Elenco', icon: <ListIcon /> },
  { key: 'grid', label: 'Griglia', icon: <GridIcon /> },
  { key: 'compact', label: 'Compatta', icon: <CompactIcon /> },
];

export default function App() {
  const [data, setData] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [view, setView] = useState<ViewKey>('home');
  const [storedFilter, setStoredFilter] = useState(() => readStored(FILTER_KEY, 'all'));
  const [mode, setMode] = useState<Mode>(() => {
    const saved = readStored(MODE_KEY, 'grid');
    return saved === 'list' || saved === 'compact' ? saved : 'grid';
  });
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [, setTick] = useState(0);
  const firstLoad = useRef(true);

  const load = useCallback(async (force = false) => {
    setLoading(true);
    try {
      const next = await getDashboard(force);
      setData(next);
      setError(null);
      if (firstLoad.current) {
        firstLoad.current = false;
        // Nessun account ancora collegato: si parte dalle impostazioni per aggiungerne uno.
        if (next.accounts.length === 0) setView('settings');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Errore sconosciuto');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
    const refresh = window.setInterval(() => void load(), REFRESH_MS);
    const tick = window.setInterval(() => setTick((n) => n + 1), 30_000);
    const onVisible = () => {
      if (document.visibilityState === 'visible') void load();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      window.clearInterval(refresh);
      window.clearInterval(tick);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [load]);

  const filter = data?.accounts.some((a) => a.login === storedFilter) ? storedFilter : 'all';
  const alerts = useMemo(() => (data ? accountAlerts(data.accounts) : []), [data]);
  const config = VIEWS.find((v) => v.key === view) ?? VIEWS[0];
  const showItems = view !== 'settings';
  const selected = data?.items.find((i) => i.id === selectedId) ?? null;
  const selectedAccount = selected ? data?.accounts.find((a) => a.login === selected.account) : undefined;
  const selectedStarred =
    selected !== null &&
    selected.type === 'repo' &&
    (data?.items.some((i) => i.section === 'starred' && i.account === selected.account && i.repo === selected.repo) ?? false);

  function chooseFilter(login: string) {
    setStoredFilter(login);
    writeStored(FILTER_KEY, login);
  }

  function chooseMode(next: Mode) {
    setMode(next);
    writeStored(MODE_KEY, next);
  }

  function chooseView(next: ViewKey) {
    setView(next);
    setSelectedId(null);
  }

  const classes = ['shell'];
  if (!showItems) classes.push('no-panel');
  if (selected) classes.push('has-selection');

  return (
    <div className={classes.join(' ')}>
      <Sidebar
        view={view}
        onView={chooseView}
        accounts={data?.accounts ?? []}
        filter={filter}
        onFilter={chooseFilter}
        needsAttention={alerts.length > 0}
      />

      <main className="main">
        <header className="main-head">
          <h1>Mira</h1>
          <div className="head-actions">
            {data && <span className="updated">Aggiornato {timeAgo(data.generatedAt)}</span>}
            {showItems && (
              <label className="search">
                <SearchIcon />
                <input
                  type="search"
                  placeholder="Cerca…"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  aria-label="Cerca"
                />
              </label>
            )}
            <button
              className={loading ? 'icon-btn spinning' : 'icon-btn'}
              onClick={() => void load(true)}
              disabled={loading}
              title="Aggiorna"
              aria-label="Aggiorna"
            >
              <RefreshIcon />
            </button>
          </div>
        </header>
        <div className="rule" />

        <div className="scroll">
          <div className="toolbar">
            <h2>{config.title}</h2>
            {showItems && (
              <div className="modes" role="group" aria-label="Modalità di visualizzazione">
                {MODES.map((m) => (
                  <button
                    key={m.key}
                    className={mode === m.key ? 'mode active' : 'mode'}
                    onClick={() => chooseMode(m.key)}
                    title={m.label}
                    aria-label={m.label}
                    aria-pressed={mode === m.key}
                  >
                    {m.icon}
                  </button>
                ))}
              </div>
            )}
          </div>

          {error && (
            <div className="alert danger" role="alert">
              {error}. Controlla che il server di Mira sia avviato.
            </div>
          )}
          {alerts.map((alert) => (
            <div key={alert.key} className={`alert ${alert.level}`} role="alert">
              <span>{alert.text}</span>
              {view !== 'settings' && (
                <button className="link" onClick={() => chooseView('settings')}>
                  Gestisci account
                </button>
              )}
            </div>
          ))}

          {!data && !error && <p className="muted">Carico…</p>}
          {data && showItems && (
            <ItemsView
              data={data}
              view={config}
              filter={filter}
              query={query}
              mode={mode}
              selectedId={selected?.id ?? null}
              onSelect={setSelectedId}
              onManageAccounts={() => chooseView('settings')}
            />
          )}
          {data && view === 'settings' && <AccountsView accounts={data.accounts} onChanged={() => load(true)} />}
        </div>
      </main>

      {showItems && (
        <DetailPanel
          item={selected}
          account={selectedAccount}
          starred={selectedStarred}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
}
