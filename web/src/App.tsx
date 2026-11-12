import { useCallback, useEffect, useRef, useState } from 'react';
import { getDashboard } from './api';
import { accountAlerts } from './alerts';
import { timeAgo } from './format';
import { Star } from './icons';
import { AccountsView } from './AccountsView';
import { DashboardView } from './DashboardView';
import type { Dashboard } from './types';

type View = 'overview' | 'accounts';

const REFRESH_MS = 5 * 60 * 1000;

export default function App() {
  const [data, setData] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [view, setView] = useState<View>('overview');
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
        // Nessun account ancora collegato: si parte dalla pagina per aggiungerne uno.
        if (next.accounts.length === 0) setView('accounts');
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

  const needsAttention = data ? accountAlerts(data.accounts).length > 0 : false;

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <Star />
          <span>Mira</span>
        </div>
        <nav className="tabs">
          <button className={view === 'overview' ? 'tab active' : 'tab'} onClick={() => setView('overview')}>
            Panoramica
          </button>
          <button className={view === 'accounts' ? 'tab active' : 'tab'} onClick={() => setView('accounts')}>
            Account
            {needsAttention && <span className="attention" title="C'è qualcosa da sistemare" />}
          </button>
        </nav>
        <div className="spacer" />
        {data && <span className="updated">Aggiornato {timeAgo(data.generatedAt)}</span>}
        <button className="btn" onClick={() => void load(true)} disabled={loading}>
          {loading ? 'Aggiorno…' : 'Aggiorna'}
        </button>
      </header>

      <main className="content">
        {error && (
          <div className="alert danger" role="alert">
            {error}. Controlla che il server di Mira sia avviato.
          </div>
        )}
        {!data && !error && <p className="muted">Carico…</p>}
        {data && view === 'overview' && (
          <DashboardView data={data} onManageAccounts={() => setView('accounts')} />
        )}
        {data && view === 'accounts' && <AccountsView accounts={data.accounts} onChanged={() => load(true)} />}
      </main>
    </div>
  );
}
