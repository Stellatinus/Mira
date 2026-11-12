import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { getDashboard } from './api';
import { accountAlerts } from './alerts';
import { buildAttention, repoKey } from './attention';
import { failureText } from './errors';
import { useI18n } from './i18n';
import { CompactIcon, GridIcon, ListIcon, RefreshIcon, SearchIcon } from './icons';
import { DetailPanel } from './DetailPanel';
import { ItemsView } from './ItemsView';
import type { Mode } from './ItemsView';
import { SettingsView } from './SettingsView';
import { Sidebar } from './Sidebar';
import { VIEWS } from './sections';
import type { ViewKey } from './sections';
import { useTheme } from './theme';
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

const MODES: { key: Mode; icon: ReactNode }[] = [
  { key: 'list', icon: <ListIcon /> },
  { key: 'grid', icon: <GridIcon /> },
  { key: 'compact', icon: <CompactIcon /> },
];

export default function App() {
  const i18n = useI18n();
  const { t, timeAgo } = i18n;
  const [theme, setTheme] = useTheme();
  const [data, setData] = useState<Dashboard | null>(null);
  const [error, setError] = useState<unknown>(null);
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
      setError(e ?? new Error('unknown'));
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
  const alerts = useMemo(() => (data ? accountAlerts(data.accounts, t) : []), [data, t]);
  const attention = useMemo(() => buildAttention(data?.items ?? []), [data]);
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
            {data && <span className="updated">{t('app.updated', { when: timeAgo(data.generatedAt) })}</span>}
            {showItems && (
              <label className="search">
                <SearchIcon />
                <input
                  type="search"
                  placeholder={t('app.search')}
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  aria-label={t('app.searchLabel')}
                />
              </label>
            )}
            <button
              className={loading ? 'icon-btn spinning' : 'icon-btn'}
              onClick={() => void load(true)}
              disabled={loading}
              title={t('app.refresh')}
              aria-label={t('app.refresh')}
            >
              <RefreshIcon />
            </button>
          </div>
        </header>
        <div className="rule" />

        <div className="scroll">
          <div className="toolbar">
            <h2>{t(`view.${config.key}.title` as 'view.home.title')}</h2>
            {showItems && (
              <div className="modes" role="group" aria-label={t('mode.group')}>
                {MODES.map((m) => {
                  const label = t(`mode.${m.key}` as 'mode.grid');
                  return (
                    <button
                      key={m.key}
                      className={mode === m.key ? 'mode active' : 'mode'}
                      onClick={() => chooseMode(m.key)}
                      title={label}
                      aria-label={label}
                      aria-pressed={mode === m.key}
                    >
                      {m.icon}
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          {error !== null && (
            <div className="alert danger" role="alert">
              {t('app.serverDown', { error: failureText(error, i18n) })}
            </div>
          )}
          {alerts.map((alert) => (
            <div key={alert.key} className={`alert ${alert.level}`} role="alert">
              <span>{alert.text}</span>
              {view !== 'settings' && (
                <button className="link" onClick={() => chooseView('settings')}>
                  {t('app.manageAccounts')}
                </button>
              )}
            </div>
          ))}

          {!data && error === null && <p className="muted">{t('app.loading')}</p>}
          {data && showItems && (
            <ItemsView
              data={data}
              view={config}
              filter={filter}
              query={query}
              mode={mode}
              selectedId={selected?.id ?? null}
              attention={attention}
              onSelect={setSelectedId}
              onManageAccounts={() => chooseView('settings')}
            />
          )}
          {data && view === 'settings' && (
            <SettingsView
              accounts={data.accounts}
              onAccountsChanged={() => load(true)}
              theme={theme}
              onTheme={setTheme}
            />
          )}
        </div>
      </main>

      {showItems && (
        <DetailPanel
          item={selected}
          account={selectedAccount}
          starred={selectedStarred}
          attention={selected?.type === 'repo' ? attention.get(repoKey(selected.account, selected.repo)) : undefined}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
}
