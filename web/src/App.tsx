import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { getDashboard, getIssues } from './api';
import { accountAlerts } from './alerts';
import { buildAttention, repoKey } from './attention';
import { failureText } from './errors';
import { useI18n } from './i18n';
import { Dropdown } from './Dropdown';
import { AssignedIcon, CompactIcon, GridIcon, ListIcon, RefreshIcon, SearchIcon, SortIcon, StarIcon } from './icons';
import { DetailPanel } from './DetailPanel';
import { ItemFilterMenu } from './ItemFilterMenu';
import { DEFAULT_ITEM_FILTERS, DEFAULT_ITEM_SORT, ITEM_SORTS } from './itemview';
import type { ItemFilters, ItemSort } from './itemview';
import { ItemsView } from './ItemsView';
import type { Mode } from './ItemsView';
import { SettingsView } from './SettingsView';
import { Sidebar } from './Sidebar';
import { loadSnapshot, saveSnapshot } from './snapshot-cache';
import { isExtraView, VIEWS } from './sections';
import type { ViewKey } from './sections';
import { useRefreshInterval } from './refresh';
import { DEFAULT_REPO_FILTERS, DEFAULT_REPO_SORT, REPO_SORTS } from './repoview';
import type { RepoFilters, RepoSort } from './repoview';
import { RepoFilterMenu } from './RepoFilterMenu';
import { useTheme } from './theme';
import type { Dashboard, ExtraKey, ExtraSnapshot } from './types';
import { useViewModel } from './viewmodel';

const FILTER_KEY = 'mira.accountFilter';
const MODE_KEY = 'mira.mode';
const REPO_SORT_KEY = 'mira.repoSort';
const ITEM_SORT_KEY = 'mira.itemSort';
const SHOW_STARRED_KEY = 'mira.showStarred';

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
  // Si parte dall'ultima fotografia salvata (se c'è), così all'apertura si vede subito qualcosa.
  const [saved] = useState<Dashboard | null>(loadSnapshot);
  const [data, setData] = useState<Dashboard | null>(saved);
  const [fromCache, setFromCache] = useState(saved !== null);
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
  const [repoFilters, setRepoFilters] = useState<RepoFilters>(DEFAULT_REPO_FILTERS);
  // «Mostra preferiti»: spento di default, aggiunge ai repository anche i preferiti che non sono tra i propri.
  const [showStarred, setShowStarred] = useState(() => readStored(SHOW_STARRED_KEY, 'false') === 'true');
  // Le viste «Issue» e «Non assegnate» scaricano tutte le pagine alla richiesta, quando si aprono.
  const [extras, setExtras] = useState<Partial<Record<ExtraKey, { data: ExtraSnapshot | null; loading: boolean; error: unknown }>>>({});
  // «Assegnate a te»: attivo di default nella vista Issue; spegnendolo si vedono tutte.
  const [assignedOnly, setAssignedOnly] = useState(true);
  const [itemSort, setItemSort] = useState<ItemSort>(() => {
    const saved = readStored(ITEM_SORT_KEY, DEFAULT_ITEM_SORT);
    return ITEM_SORTS.find((s) => s === saved) ?? DEFAULT_ITEM_SORT;
  });
  // I filtri degli elementi valgono per la vista in cui sono stati scelti: cambiando vista si azzerano.
  const [itemFilters, setItemFilters] = useState<ItemFilters>(DEFAULT_ITEM_FILTERS);
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
      setFromCache(false);
      saveSnapshot(next);
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

  const loadExtra = useCallback(async (key: ExtraKey, force = false) => {
    setExtras((prev) => ({ ...prev, [key]: { data: prev[key]?.data ?? null, loading: true, error: null } }));
    try {
      const next = await getIssues(key, force);
      setExtras((prev) => ({ ...prev, [key]: { data: next, loading: false, error: null } }));
    } catch (e) {
      setExtras((prev) => ({ ...prev, [key]: { data: prev[key]?.data ?? null, loading: false, error: e ?? new Error('unknown') } }));
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
  const unreadNotifications = useMemo(
    () => (data?.items ?? []).filter((i) => i.section === 'notifications' && (filter === 'all' || i.account === filter)).length,
    [data, filter],
  );
  const isRepoView = config.tools === 'repos';
  const isItemView = config.tools === 'items';
  const extraKey = isExtraView(view) ? view : null;
  const extra = extraKey ? extras[extraKey] : undefined;
  const hasAccounts = (data?.accounts.length ?? 0) > 0;
  // Aprendo una vista «completa» si scaricano le sue issue (il server le tiene in cache per qualche minuto).
  useEffect(() => {
    if (extraKey && hasAccounts) void loadExtra(extraKey);
  }, [extraKey, hasAccounts, loadExtra]);
  // Nelle viste complete gli elementi scaricati a parte si uniscono a quelli della dashboard.
  const viewData = useMemo(
    () => (data && extra?.data ? { ...data, items: [...data.items, ...extra.data.items], errors: [...data.errors, ...extra.data.errors] } : data),
    [data, extra?.data],
  );
  const model = useViewModel({ data: viewData, view: config, filter, query, attention, repoSort, repoFilters, itemSort, itemFilters, showStarred, assignedOnly, t });
  // Con una sola sezione il conteggio sta accanto al titolo della vista, non in un'intestazione a parte.
  const countKey = showItems && config.sections.length === 1 ? config.sections[0] : null;
  const selected = viewData?.items.find((i) => i.id === selectedId) ?? null;
  // Un repository è visto da più account e può avere la stella di alcuni: il pannello li mostra tutti.
  const selectedInfo = selected?.type === 'repo' ? model.repoInfo.get(selected.id) : undefined;
  const selectedAccounts = (selectedInfo?.accounts ?? (selected ? [selected.account] : [])).map(
    (login) => data?.accounts.find((a) => a.login === login) ?? { login, avatarUrl: null },
  );

  function chooseFilter(login: string) {
    setStoredFilter(login);
    writeStored(FILTER_KEY, login);
  }

  function chooseRepoSort(next: RepoSort) {
    setRepoSort(next);
    writeStored(REPO_SORT_KEY, next);
  }

  function toggleShowStarred() {
    const next = !showStarred;
    setShowStarred(next);
    writeStored(SHOW_STARRED_KEY, String(next));
  }

  function chooseItemSort(next: ItemSort) {
    setItemSort(next);
    writeStored(ITEM_SORT_KEY, next);
  }

  function chooseMode(next: Mode) {
    setMode(next);
    writeStored(MODE_KEY, next);
  }

  function chooseView(next: ViewKey) {
    setView(next);
    setItemFilters(DEFAULT_ITEM_FILTERS);
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
        unreadNotifications={unreadNotifications}
      />

      <main className="main">
        <header className="main-head">
          <h1>{t(`view.${config.key}.title` as 'view.home.title')}</h1>
          <div className="head-actions">
            {data && (
              <span className="updated">
                {t(fromCache ? 'app.cachedUpdating' : 'app.updated', { when: timeAgo(data.generatedAt) })}
              </span>
            )}
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
              className={loading || extra?.loading ? 'icon-btn spinning' : 'icon-btn'}
              onClick={() => {
                void load(true);
                if (extraKey) void loadExtra(extraKey, true);
              }}
              disabled={loading || extra?.loading === true}
              title={t('app.refresh')}
              aria-label={t('app.refresh')}
            >
              <RefreshIcon />
            </button>
          </div>
        </header>
        <div className="rule" />

        <div className="scroll">
          {showItems && (
            <div className="toolbar">
              {/* A sinistra filtri, ordinamento e quanti sono; a destra i modi di visualizzazione. */}
              <div className="tools">
                {isRepoView && (
                  <>
                    <RepoFilterMenu
                      filters={repoFilters}
                      onChange={setRepoFilters}
                      facets={model.facets}
                    />
                    <Dropdown
                      label={t('repo.sort.label')}
                      icon={<SortIcon size={15} />}
                      text={t(`repo.sort.${repoSort}` as 'repo.sort.updated')}
                      value={repoSort}
                      options={REPO_SORTS.map((value) => ({ value, label: t(`repo.sort.${value}` as 'repo.sort.updated') }))}
                      onChange={chooseRepoSort}
                    />
                    <button
                      className={showStarred ? 'dd-btn highlighted' : 'dd-btn'}
                      aria-pressed={showStarred}
                      onClick={toggleShowStarred}
                    >
                      <StarIcon size={15} filled={showStarred} />
                      <span className="dd-text">{t('repo.showStarred')}</span>
                    </button>
                  </>
                )}
                {isItemView && (
                  <>
                    <ItemFilterMenu filters={itemFilters} onChange={setItemFilters} facets={model.itemFacets} />
                    <Dropdown
                      label={t('repo.sort.label')}
                      icon={<SortIcon size={15} />}
                      text={t(`item.sort.${itemSort}` as 'item.sort.updated')}
                      value={itemSort}
                      options={ITEM_SORTS.map((value) => ({ value, label: t(`item.sort.${value}` as 'item.sort.updated') }))}
                      onChange={chooseItemSort}
                    />
                  </>
                )}
                {view === 'issues' && (
                  <button
                    className={assignedOnly ? 'dd-btn highlighted' : 'dd-btn'}
                    aria-pressed={assignedOnly}
                    onClick={() => setAssignedOnly((current) => !current)}
                  >
                    <AssignedIcon size={15} />
                    <span className="dd-text">{t('issues.assignedToMe')}</span>
                  </button>
                )}
                {countKey && (
                  <span className="count" title={model.truncated(countKey) ? t('items.mostRecent') : undefined}>
                    {model.countText(countKey)}
                  </span>
                )}
              </div>
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
            </div>
          )}

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
          {extraKey && extra?.loading && !extra.data && <p className="muted">{t('items.loadingAll')}</p>}
          {extraKey && extra?.error != null && !extra.data && (
            <div className="alert danger" role="alert">
              {failureText(extra.error, i18n)}
            </div>
          )}
          {viewData && showItems && (!extraKey || extra?.data) && (
            <ItemsView
              data={viewData}
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
          accounts={selectedAccounts}
          starredBy={selectedInfo?.starredBy ?? []}
          attention={selected?.type === 'repo' ? attention.get(repoKey(selected.repo)) : undefined}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
}
