import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { getDashboard } from './api';
import { accountAlerts } from './alerts';
import { buildAttention, repoKey } from './attention';
import { failureText } from './errors';
import { useI18n } from './i18n';
import { Dropdown } from './Dropdown';
import { CompactIcon, FilterIcon, GridIcon, ListIcon, RefreshIcon, SearchIcon, SortIcon } from './icons';
import { DetailPanel } from './DetailPanel';
import { ItemsView } from './ItemsView';
import type { Mode } from './ItemsView';
import { SettingsView } from './SettingsView';
import { Sidebar } from './Sidebar';
import { VIEWS } from './sections';
import type { ViewKey } from './sections';
import { useRefreshInterval } from './refresh';
import { DEFAULT_REPO_SORT, isRepoSection, REPO_FILTERS, REPO_SORTS } from './repoview';
import type { RepoFilter, RepoSort } from './repoview';
import { useTheme } from './theme';
import type { Dashboard } from './types';
import { useViewModel } from './viewmodel';

const FILTER_KEY = 'mira.accountFilter';
const MODE_KEY = 'mira.mode';
const REPO_SORT_KEY = 'mira.repoSort';

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
  const [repoSort, setRepoSort] = useState<RepoSort>(() => {
    const saved = readStored(REPO_SORT_KEY, DEFAULT_REPO_SORT);
    return REPO_SORTS.find((s) => s === saved) ?? DEFAULT_REPO_SORT;
  });
  // Il filtro non si ricorda: riaprendo Mira si devono rivedere tutti i repository.
  const [repoFilter, setRepoFilter] = useState<RepoFilter>('all');
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [, setTick] = useState(0);
  const firstLoad = useRef(true);
  const lastLoad = useRef(0);
  const [refreshMinutes, setRefreshMinutes] = useRefreshInterval();

  const load = useCallback(async (force = false) => {
    setLoading(true);
    try {
      const next = await getDashboard(force);
      lastLoad.current = Date.now();
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
    // Ogni 30 secondi si ridisegna, così «2 minuti fa» resta giusto.
    const tick = window.setInterval(() => setTick((n) => n + 1), 30_000);
    return () => window.clearInterval(tick);
  }, [load]);

  // Aggiornamento automatico, se attivo nelle Impostazioni. Tornando sulla scheda si aggiorna solo
  // se è passato l'intervallo scelto (un timer di una scheda in secondo piano può restare indietro).
  useEffect(() => {
    if (refreshMinutes === 0) return;
    const interval = refreshMinutes * 60_000;
    const timer = window.setInterval(() => void load(), interval);
    const onVisible = () => {
      if (document.visibilityState === 'visible' && Date.now() - lastLoad.current >= interval) void load();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [load, refreshMinutes]);

  const filter = data?.accounts.some((a) => a.login === storedFilter) ? storedFilter : 'all';
  const alerts = useMemo(() => (data ? accountAlerts(data.accounts, t) : []), [data, t]);
  const attention = useMemo(() => buildAttention(data?.items ?? []), [data]);
  const config = VIEWS.find((v) => v.key === view) ?? VIEWS[0];
  const showItems = view !== 'settings';
  const isRepoView = config.sections.some(isRepoSection);
  const model = useViewModel({ data, view: config, filter, query, attention, repoSort, repoFilter, t });
  // Con una sola sezione il conteggio sta accanto al titolo della vista, non in un'intestazione a parte.
  const countKey = showItems && config.sections.length === 1 ? config.sections[0] : null;
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

  function chooseRepoSort(next: RepoSort) {
    setRepoSort(next);
    writeStored(REPO_SORT_KEY, next);
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
            <h2 className="view-title">
              {t(`view.${config.key}.title` as 'view.home.title')}
              {countKey && (
                <span className="count" title={model.truncated(countKey) ? t('items.mostRecent') : undefined}>
                  {model.countText(countKey)}
                </span>
              )}
            </h2>
            {isRepoView && (
              <div className="tools">
                <Dropdown
                  label={t('repo.filter.label')}
                  icon={<FilterIcon size={15} />}
                  text={repoFilter === 'all' ? t('repo.filter.button') : t(`repo.filter.${repoFilter}` as 'repo.filter.all')}
                  value={repoFilter}
                  options={REPO_FILTERS.map((value) => ({ value, label: t(`repo.filter.${value}` as 'repo.filter.all') }))}
                  onChange={setRepoFilter}
                  highlighted={repoFilter !== 'all'}
                />
                <Dropdown
                  label={t('repo.sort.label')}
                  icon={<SortIcon size={15} />}
                  text={t(`repo.sort.${repoSort}` as 'repo.sort.updated')}
                  value={repoSort}
                  options={REPO_SORTS.map((value) => ({ value, label: t(`repo.sort.${value}` as 'repo.sort.updated') }))}
                  onChange={chooseRepoSort}
                />
              </div>
            )}
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
              mode={mode}
              selectedId={selected?.id ?? null}
              attention={attention}
              model={model}
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
              refreshMinutes={refreshMinutes}
              onRefreshMinutes={setRefreshMinutes}
            />
          )}
        </div>
      </main>

      {showItems && (
        <DetailPanel
          item={selected}
          account={selectedAccount}
          starred={selectedStarred}
          attention={selected?.type === 'repo' ? attention.get(repoKey(selected.repo)) : undefined}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
}
