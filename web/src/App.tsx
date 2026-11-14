import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import {
  deleteSeen,
  getDashboard,
  getIssues,
  getSeen,
  markNotificationRead,
  markRepositoryNotificationsRead,
  putSeen,
  unsubscribeNotification,
} from './api';
import { accountAlerts } from './alerts';
import { useAnnounce } from './announce';
import { buildAttention, repoKey } from './attention';
import { failureText } from './errors';
import { useI18n } from './i18n';
import { searchNearlyExhausted } from './limits';
import { Dropdown } from './Dropdown';
import { CompactIcon, EyeIcon, GridIcon, ListIcon, RefreshIcon, SearchIcon, SortIcon, StarIcon } from './icons';
import { DetailPanel } from './DetailPanel';
import { ItemFilterMenu } from './ItemFilterMenu';
import { DEFAULT_ITEM_SORT, defaultItemFilters, ITEM_SORTS } from './itemview';
import type { ItemFilters, ItemSort } from './itemview';
import { ItemsView, lastNumber } from './ItemsView';
import type { Mode } from './ItemsView';
import { SettingsView } from './SettingsView';
import { Sidebar } from './Sidebar';
import { isHidden, threadIdOf, tomorrowMorning } from './seen';
import { loadSnapshot, saveSnapshot } from './snapshot-cache';
import { hashFor, isExtraView, VIEWS, viewFromHash } from './sections';
import type { ViewKey } from './sections';
import { useRefreshInterval } from './refresh';
import { DEFAULT_REPO_FILTERS, DEFAULT_REPO_SORT, REPO_SORTS } from './repoview';
import type { RepoFilters, RepoSort } from './repoview';
import { RepoFilterMenu } from './RepoFilterMenu';
import { useTheme } from './theme';
import type { Dashboard, DashboardItem, ExtraKey, ExtraSnapshot, SeenEntry } from './types';
import { useViewModel } from './viewmodel';

const FILTER_KEY = 'mira.accountFilter';
const MODE_KEY = 'mira.mode';
const REPO_SORT_KEY = 'mira.repoSort';
const ITEM_SORT_KEY = 'mira.itemSort';
const SHOW_STARRED_KEY = 'mira.showStarred';
const SHOW_SEEN_KEY = 'mira.showSeen';

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
  const announce = useAnnounce();
  const [theme, setTheme] = useTheme();
  // Si parte dall'ultima fotografia salvata (se c'è), così all'apertura si vede subito qualcosa.
  const [saved] = useState<Dashboard | null>(loadSnapshot);
  const [data, setData] = useState<Dashboard | null>(saved);
  const [fromCache, setFromCache] = useState(saved !== null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(false);
  // La vista sta nell'URL (hash): segnalibri, Indietro e ricarica funzionano.
  const [view, setView] = useState<ViewKey>(() => viewFromHash() ?? 'home');
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
  // Elementi già visti o rimandati (stato locale, salvato dal server): nascosti finché non cambiano.
  const [seen, setSeen] = useState<ReadonlyMap<string, SeenEntry>>(() => new Map());
  const [showSeen, setShowSeen] = useState(() => readStored(SHOW_SEEN_KEY, 'false') === 'true');
  // Le viste «Issue» e «Non assegnate» scaricano tutte le pagine alla richiesta, quando si aprono.
  const [extras, setExtras] = useState<Partial<Record<ExtraKey, { data: ExtraSnapshot | null; loading: boolean; error: unknown }>>>({});
  // «Assegnate a te»: attivo di default nella vista Issue; spegnendolo si vedono tutte.
  const [itemSort, setItemSort] = useState<ItemSort>(() => {
    const saved = readStored(ITEM_SORT_KEY, DEFAULT_ITEM_SORT);
    return ITEM_SORTS.find((s) => s === saved) ?? DEFAULT_ITEM_SORT;
  });
  // I filtri degli elementi valgono per la vista in cui sono stati scelti: cambiando vista si azzerano.
  const [itemFilters, setItemFilters] = useState<ItemFilters>(() => defaultItemFilters(view));
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  // Sale quando una card si sceglie da tastiera: il pannello porta il focus sul suo titolo.
  const [panelFocus, setPanelFocus] = useState(0);
  const [, setTick] = useState(0);
  const mainRef = useRef<HTMLElement>(null);
  const dataRef = useRef<Dashboard | null>(null);
  const firstLoad = useRef(true);
  const lastLoad = useRef(0);
  const [refreshMinutes, setRefreshMinutes] = useRefreshInterval();

  // Il testo degli annunci si legge al momento: così cambiare lingua non fa ricaricare i dati.
  const say = useRef({ announce, t });
  say.current = { announce, t };

  const load = useCallback(async (force = false) => {
    setLoading(true);
    try {
      const next = await getDashboard(force);
      lastLoad.current = Date.now();
      setData(next);
      setFromCache(false);
      saveSnapshot(next);
      setError(null);
      // Si annuncia l'esito dell'aggiornamento fatto a mano (non quello automatico, che parlerebbe ogni pochi minuti).
      if (force) {
        say.current.announce(
          say.current.t('announce.updated', {
            assigned: next.items.filter((i) => i.section === 'assigned').length,
            unread: next.items.filter((i) => i.section === 'notifications').length,
          }),
        );
      }
      if (firstLoad.current) {
        firstLoad.current = false;
        // Nessun account ancora collegato: si parte dalle impostazioni per aggiungerne uno.
        if (next.accounts.length === 0) {
          window.history.replaceState(null, '', hashFor('settings'));
          setView('settings');
        }
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

  // Lo stato «visto» non blocca nulla: se non si legge, semplicemente non si nasconde niente.
  useEffect(() => {
    getSeen()
      .then((list) => setSeen(new Map(list.map((entry) => [entry.url, entry]))))
      .catch(() => undefined);
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
    // Se le ricerche di un account sono quasi finite, l'aggiornamento automatico aspetta: non le esaurisce del tutto.
    const auto = () => {
      if (!searchNearlyExhausted(dataRef.current?.accounts)) void load();
    };
    const timer = window.setInterval(auto, interval);
    const onVisible = () => {
      if (document.visibilityState === 'visible' && Date.now() - lastLoad.current >= interval) auto();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [load, refreshMinutes]);

  dataRef.current = data;
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
  const model = useViewModel({ data: viewData, view: config, filter, query, attention, repoSort, repoFilters, itemSort, itemFilters, showStarred, seen, showSeen, t });
  // Cercando o filtrando, i conteggi cambiano in silenzio: si dice quanti risultati ci sono, con un po' di ritardo
  // per non parlare a ogni tasto. Un aggiornamento dei dati non cambia gli argomenti dell'effetto e non annuncia nulla.
  const shownCount = showItems ? config.sections.reduce((sum, key) => sum + (model.bySection.get(key)?.length ?? 0), 0) : 0;
  const shownRef = useRef(shownCount);
  shownRef.current = shownCount;
  const searchMounted = useRef(false);
  useEffect(() => {
    if (!searchMounted.current) {
      searchMounted.current = true;
      return;
    }
    const timer = window.setTimeout(() => {
      const count = shownRef.current;
      say.current.announce(count === 0 ? say.current.t('announce.noResults') : say.current.t('announce.results', { count }));
    }, 600);
    return () => window.clearTimeout(timer);
  }, [query, repoFilters, itemFilters, showStarred, filter]);

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

  // Segna come letta una notifica, tutte quelle di un repository, o smette di seguire la conversazione. Si toglie subito dalla
  // lista (e dal totale) e si fa su GitHub dopo; se GitHub rifiuta si dice e si rilegge tutto.
  async function notifyAction(item: DashboardItem, action: 'read' | 'unsubscribe' | 'repoRead') {
    const affected = (i: DashboardItem) =>
      i.section === 'notifications' &&
      i.account === item.account &&
      (action === 'repoRead' ? i.repo.toLowerCase() === item.repo.toLowerCase() : i.id === item.id);
    const removed = (data?.items ?? []).filter(affected).length;

    setData((current) =>
      current && {
        ...current,
        items: current.items.filter((i) => !affected(i)),
        totals: current.totals.map((t) =>
          t.account === item.account && t.section === 'notifications' && t.total !== null
            ? { ...t, total: Math.max(0, t.total - removed) }
            : t,
        ),
      },
    );
    setSelectedId(null);
    headingRef.current?.focus();

    try {
      if (action === 'read') await markNotificationRead(item.account, threadIdOf(item));
      else if (action === 'unsubscribe') await unsubscribeNotification(item.account, threadIdOf(item));
      else await markRepositoryNotificationsRead(item.account, item.repo);
      say.current.announce(say.current.t(`notify.announce.${action}` as 'notify.announce.read', { repo: item.repo }));
    } catch {
      say.current.announce(say.current.t('notify.error'));
      void load(true);
    }
  }

  function toggleShowSeen() {
    const next = !showSeen;
    setShowSeen(next);
    writeStored(SHOW_SEEN_KEY, String(next));
  }

  // Segna come visto, rimanda o riporta da vedere: si aggiorna subito e si salva dopo; se il salvataggio fallisce si torna indietro.
  async function changeSeen(item: DashboardItem, action: 'seen' | 'snoozed' | 'unseen') {
    const previous = seen.get(item.url);
    const entry: SeenEntry | undefined =
      action === 'unseen'
        ? undefined
        : {
            url: item.url,
            updatedAt: item.updatedAt,
            snoozeUntil: action === 'snoozed' ? tomorrowMorning().toISOString() : null,
            savedAt: new Date().toISOString(),
          };
    const apply = (value: SeenEntry | undefined) =>
      setSeen((current) => {
        const next = new Map(current);
        if (value) next.set(item.url, value);
        else next.delete(item.url);
        return next;
      });

    apply(entry);
    try {
      if (entry) await putSeen(entry.url, entry.updatedAt, entry.snoozeUntil);
      else await deleteSeen(item.url);
      say.current.announce(say.current.t(`seen.announce.${action}` as 'seen.announce.seen'));
      // L'elemento sparisce dall'elenco: il pannello non ha più senso e il focus passa al titolo della pagina.
      if (entry && isHidden(entry, item, Date.now()) && !showSeen) {
        setSelectedId(null);
        headingRef.current?.focus();
      }
    } catch {
      apply(previous);
      say.current.announce(say.current.t('seen.error'));
    }
  }

  function chooseItemSort(next: ItemSort) {
    setItemSort(next);
    writeStored(ITEM_SORT_KEY, next);
  }

  function chooseMode(next: Mode) {
    setMode(next);
    writeStored(MODE_KEY, next);
  }

  function selectItem(id: string, viaKeyboard = false) {
    setSelectedId(id);
    if (viaKeyboard) setPanelFocus((n) => n + 1);
  }

  // Chiudendo il pannello il focus torna sulla card da cui si era partiti.
  const closePanel = useCallback(() => {
    const id = selectedId;
    setSelectedId(null);
    if (id === null) return;
    window.requestAnimationFrame(() => {
      document.querySelector<HTMLElement>(`[data-card-id="${CSS.escape(id)}"] .ci-select`)?.focus();
    });
  }, [selectedId]);

  // Esc chiude il pannello, ma non se un menu aperto sta già usando Esc per chiudersi.
  useEffect(() => {
    if (selectedId === null) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !document.querySelector('[aria-expanded="true"]')) closePanel();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [selectedId, closePanel]);

  function applyView(next: ViewKey) {
    setView(next);
    setItemFilters(defaultItemFilters(next));
    setSelectedId(null);
  }

  // Cambiare vista scrive l'hash; l'ascolto di `hashchange` (sotto) la applica, anche per Indietro e Avanti.
  // Scegliere la vista in cui si è già riporta filtri e selezione allo stato iniziale, come prima.
  function chooseView(next: ViewKey) {
    if (viewFromHash() === next) {
      applyView(next);
    } else {
      window.location.hash = hashFor(next);
    }
  }

  // Un hash che non è una vista (come `#sec-review`) non cambia nulla.
  useEffect(() => {
    const onHash = () => {
      const next = viewFromHash();
      if (next !== null) applyView(next);
    };
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
    // applyView usa solo setter di stato: non serve rilegarlo a ogni render.
  }, []);

  // Il titolo della scheda dice dove si è, con le notifiche non lette davanti; al cambio di vista il focus passa
  // al titolo della pagina, così il lettore di schermo la annuncia.
  const viewLabel = t(`view.${config.key}.label` as 'view.home.label');
  useEffect(() => {
    document.title = `${unreadNotifications > 0 ? `(${unreadNotifications}) ` : ''}${viewLabel} · Mira`;
  }, [viewLabel, unreadNotifications]);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const firstView = useRef(true);
  useEffect(() => {
    if (firstView.current) {
      firstView.current = false;
      return;
    }
    headingRef.current?.focus();
  }, [view]);

  const classes = ['shell'];
  if (!showItems) classes.push('no-panel');
  if (selected) classes.push('has-selection');

  return (
    <div className={classes.join(' ')}>
      {/* Da tastiera si salta la barra laterale; visibile solo al focus. Niente #hash: l'URL non cambia. */}
      <a
        className="skip-link"
        href="#main"
        onClick={(event) => {
          event.preventDefault();
          mainRef.current?.focus();
        }}
      >
        {t('a11y.skip')}
      </a>
      <Sidebar
        view={view}
        onView={chooseView}
        accounts={data?.accounts ?? []}
        filter={filter}
        onFilter={chooseFilter}
        needsAttention={alerts.length > 0}
        unreadNotifications={unreadNotifications}
      />

      <main className="main" id="main" tabIndex={-1} ref={mainRef}>
        <header className="main-head">
          <h1 ref={headingRef} tabIndex={-1}>
            {t(`view.${config.key}.title` as 'view.home.title')}
          </h1>
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
                // aria-disabled e non disabled: un pulsante disabilitato perde il focus, e chi usa la tastiera
                // dovrebbe ripartire dall'inizio della pagina a ogni aggiornamento.
                if (loading || extra?.loading === true) return;
                void load(true);
                if (extraKey) void loadExtra(extraKey, true);
              }}
              aria-disabled={loading || extra?.loading === true}
              title={t('app.refresh')}
              aria-label={t('app.refresh')}
            >
              <RefreshIcon />
            </button>
          </div>
        </header>
        <div className="rule" />

        <div className="scroll" aria-busy={loading || extra?.loading === true}>
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
                    <ItemFilterMenu filters={itemFilters} onChange={setItemFilters} facets={model.itemFacets} assignable={view === 'issues'} hasChecks={model.hasChecks} />
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
                {!isRepoView && (model.seenCount > 0 || showSeen) && (
                  <button
                    className={showSeen ? 'dd-btn highlighted' : 'dd-btn'}
                    aria-pressed={showSeen}
                    onClick={toggleShowSeen}
                  >
                    <EyeIcon size={15} />
                    <span className="dd-text">{t('seen.show')}</span>
                    <span className="count">{model.seenCount}</span>
                  </button>
                )}
                {countKey && (
                  <span className="count" title={model.truncated(countKey) ? t('items.mostRecent') : undefined}>
                    {model.countText(countKey)}
                    <span className="visually-hidden"> {t('items.unit', { count: lastNumber(model.countText(countKey)) })}</span>
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
              seen={seen}
              showSeen={showSeen}
              onSelect={selectItem}
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
          seen={selected ? seen.get(selected.url) : undefined}
          onSeen={(action) => selected && void changeSeen(selected, action)}
          onNotify={(action) => selected && void notifyAction(selected, action)}
          focusSignal={panelFocus}
          onClose={closePanel}
        />
      )}
    </div>
  );
}
