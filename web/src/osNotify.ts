import type { I18n } from './i18n';
import type { ViewKey } from './sections';
import type { NewItems } from './newItems';

/** Cosa avvisare con una notifica del sistema; spenta di default, si attiva dalle Impostazioni. */
export interface OsNotifyPrefs {
  enabled: boolean;
  mentions: boolean;
  reviews: boolean;
}

export const DEFAULT_OS_NOTIFY: OsNotifyPrefs = { enabled: false, mentions: true, reviews: true };
const KEY = 'mira.osNotify';

export function loadOsNotify(): OsNotifyPrefs {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return DEFAULT_OS_NOTIFY;
    const parsed = JSON.parse(raw) as Partial<OsNotifyPrefs>;
    return {
      enabled: parsed.enabled === true,
      mentions: parsed.mentions !== false,
      reviews: parsed.reviews !== false,
    };
  } catch {
    return DEFAULT_OS_NOTIFY;
  }
}

export function saveOsNotify(prefs: OsNotifyPrefs) {
  try {
    localStorage.setItem(KEY, JSON.stringify(prefs));
  } catch {
    // storage non disponibile: la scelta vale solo per questa sessione
  }
}

/** Lo stato del permesso del browser, oppure «unsupported» se non ha le notifiche. */
export type OsPermission = NotificationPermission | 'unsupported';

export const osPermission = (): OsPermission => (typeof Notification === 'undefined' ? 'unsupported' : Notification.permission);

/** Chiede il permesso: solo da un gesto dell'utente (un clic), mai da solo. */
export async function requestOsPermission(): Promise<OsPermission> {
  if (typeof Notification === 'undefined') return 'unsupported';
  try {
    return await Notification.requestPermission();
  } catch {
    return Notification.permission;
  }
}

/**
 * Mostra l'avviso di sistema per ciò che c'è di nuovo, uno per tipo (un solo elemento: il suo titolo; più elementi:
 * quanti sono). Non avvisa se il permesso manca, se il tipo è spento, o se la pagina è davanti all'utente (la vedrebbe già).
 * Cliccando l'avviso si torna a Mira e si apre la vista giusta.
 */
export function showNewItems(found: NewItems, prefs: OsNotifyPrefs, t: I18n['t'], open: (view: ViewKey) => void) {
  if (!prefs.enabled || osPermission() !== 'granted') return;
  if (document.visibilityState === 'visible' && document.hasFocus()) return;

  const show = (kind: 'mention' | 'review', items: NewItems['mentions'], view: ViewKey) => {
    if (items.length === 0) return;
    const single = items.length === 1;
    // Al singolare «Nuova menzione», al plurale «3 nuove menzioni» (le forme .one e .other della lingua).
    const notification = new Notification(
      t(`osnotify.${kind}` as 'osnotify.mention', { count: items.length }),
      {
        body: single ? `${items[0].title} · ${items[0].repo}` : items.slice(0, 3).map((i) => i.title).join('\n'),
        // Con lo stesso tag un nuovo avviso sostituisce il precedente invece di accumularsi.
        tag: `mira-${kind}`,
      },
    );
    notification.onclick = () => {
      window.focus();
      open(view);
      notification.close();
    };
  };

  if (prefs.mentions) show('mention', found.mentions, 'notifications');
  if (prefs.reviews) show('review', found.reviews, 'prs');
}
