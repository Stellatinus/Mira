import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import type { ReactNode } from 'react';

type Announce = (text: string) => void;

const Context = createContext<Announce | null>(null);

/**
 * Una regione «live» (visivamente nascosta) per dire ai lettori di schermo ciò che cambia senza che il
 * focus si muova: fine di un caricamento, risultati di una ricerca, «Copiato». `announce(testo)` la aggiorna.
 */
export function AnnounceProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState({ text: '', n: 0 });

  const announce = useCallback<Announce>((text) => setMessage((prev) => ({ text, n: prev.n + 1 })), []);
  const value = useMemo(() => announce, [announce]);

  return (
    <Context.Provider value={value}>
      {children}
      {/* Lo spazio che si alterna fa ripetere anche due annunci uguali di fila. */}
      <div className="visually-hidden" role="status" aria-live="polite" aria-atomic="true">
        {message.text}
        {message.n % 2 === 0 ? '' : ' '}
      </div>
    </Context.Provider>
  );
}

export function useAnnounce(): Announce {
  const value = useContext(Context);
  if (!value) throw new Error('useAnnounce va usato dentro AnnounceProvider');
  return value;
}
