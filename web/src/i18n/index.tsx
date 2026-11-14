import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import type { ErrorInfo } from '../types';
import { DEFAULT_LANGUAGE, detectLanguage, findLanguage, LANGUAGES } from './languages';
import type { Language } from './languages';
import type { MessageKey } from './it';

const STORAGE_KEY = 'mira.language';

/** "alert.expiresIn.one" e "alert.expiresIn.other" si chiamano entrambe "alert.expiresIn". */
type PluralBase<K extends string> = K extends `${infer B}.${'zero' | 'one' | 'two' | 'few' | 'many' | 'other'}` ? B : K;
export type TKey = PluralBase<MessageKey>;
export type Params = Record<string, string | number>;

export interface I18n {
  language: Language;
  languages: Language[];
  setLanguage: (code: string) => void;
  /** Traduce una chiave, sostituendo i `{parametri}`; con `count` sceglie la forma plurale. */
  t: (key: TKey, params?: Params) => string;
  /** Come `t`, ma i parametri possono essere elementi React (link, grassetto…). */
  tx: (key: TKey, nodes: Record<string, ReactNode>) => ReactNode[];
  /** Vero se la chiave esiste nella lingua corrente (per chiavi costruite a runtime). */
  has: (key: string) => boolean;
  /** Messaggio per un errore con codice, in arrivo dal backend. */
  errorText: (error: ErrorInfo) => string;
  timeAgo: (iso: string) => string;
  formatDate: (iso: string) => string;
}

const Context = createContext<I18n | null>(null);

function readStored(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function initialLanguage(): Language {
  return findLanguage(readStored()) ?? detectLanguage(navigator.languages?.length ? navigator.languages : [navigator.language]);
}

function fill(template: string, params?: Params): string {
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (whole, name: string) => (name in params ? String(params[name]) : whole));
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(initialLanguage);

  useEffect(() => {
    document.documentElement.lang = language.code;
  }, [language]);

  const setLanguage = useCallback((code: string) => {
    const next = findLanguage(code) ?? findLanguage(DEFAULT_LANGUAGE) ?? LANGUAGES[0];
    setLanguageState(next);
    try {
      localStorage.setItem(STORAGE_KEY, next.code);
    } catch {
      // storage non disponibile: la scelta vale solo per questa sessione
    }
  }, []);

  const value = useMemo<I18n>(() => {
    const { messages, locale } = language;
    const plural = new Intl.PluralRules(locale);
    const relative = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
    const date = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' });
    const table = messages as Record<string, string | undefined>;

    const lookup = (key: string, params?: Params): string => {
      let template: string | undefined;
      if (params && typeof params.count === 'number') {
        template = table[`${key}.${plural.select(params.count)}`] ?? table[`${key}.other`];
      }
      template ??= table[key];
      return template === undefined ? key : fill(template, params);
    };

    const t = (key: TKey, params?: Params) => lookup(key, params);
    const has = (key: string) => table[key] !== undefined;

    const tx = (key: TKey, nodes: Record<string, ReactNode>): ReactNode[] => {
      const template = table[key] ?? key;
      return template.split(/\{(\w+)\}/g).map((part, index) => (index % 2 === 1 ? (nodes[part] ?? '') : part));
    };

    const errorText = (error: ErrorInfo): string => {
      // Un limite di richieste che dice quando si potrà riprovare ha il suo testo, con l'orario.
      if (error.code === 'rate-limited' && error.detail && !Number.isNaN(Date.parse(error.detail))) {
        const at = new Date(error.detail);
        const sameDay = at.toDateString() === new Date().toDateString();
        const time = new Intl.DateTimeFormat(locale, sameDay ? { timeStyle: 'short' } : { dateStyle: 'short', timeStyle: 'short' }).format(at);
        return lookup('error.rate-limited.until', { time });
      }
      const key = `error.${error.code}`;
      if (!has(key)) return error.code;
      return lookup(key, {
        detail: error.detail ?? '',
        cause: error.cause ? errorText(error.cause) : '',
      });
    };

    const timeAgo = (iso: string): string => {
      const seconds = (new Date(iso).getTime() - Date.now()) / 1000;
      const units: [Intl.RelativeTimeFormatUnit, number][] = [
        ['day', 86400],
        ['hour', 3600],
        ['minute', 60],
      ];
      for (const [unit, size] of units) {
        if (Math.abs(seconds) >= size) return relative.format(Math.round(seconds / size), unit);
      }
      return lookup('time.now');
    };

    return {
      language,
      languages: LANGUAGES,
      setLanguage,
      t,
      tx,
      has,
      errorText,
      timeAgo,
      formatDate: (iso: string) => date.format(new Date(iso)),
    };
  }, [language, setLanguage]);

  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useI18n(): I18n {
  const value = useContext(Context);
  if (!value) throw new Error('useI18n va usato dentro I18nProvider');
  return value;
}
