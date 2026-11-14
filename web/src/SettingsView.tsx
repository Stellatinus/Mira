import { useEffect, useState } from 'react';
import { getVersion } from './api';
import { AccountsView } from './AccountsView';
import { useI18n } from './i18n';
import { RadioGroup } from './RadioGroup';
import { REFRESH_OPTIONS } from './refresh';
import { clearSnapshot, snapshotSizeKb } from './snapshot-cache';
import { THEMES } from './theme';
import type { ThemePreference } from './theme';
import type { AccountStatus, UpdateInfo } from './types';

interface Props {
  accounts: AccountStatus[];
  onAccountsChanged: () => Promise<void>;
  theme: ThemePreference;
  onTheme: (theme: ThemePreference) => void;
  /** Ogni quanti minuti si aggiorna da sola (0 = mai). */
  refreshMinutes: number;
  onRefreshMinutes: (minutes: number) => void;
  /** Il controllo degli aggiornamenti: l'ultimo esito, se è in corso, come rifarlo e se parte da solo all'apertura. */
  update: UpdateInfo | null;
  checkingUpdate: boolean;
  onCheckUpdate: () => void;
  checkUpdates: boolean;
  onCheckUpdates: (enabled: boolean) => void;
}

export function SettingsView({
  accounts,
  onAccountsChanged,
  theme,
  onTheme,
  refreshMinutes,
  onRefreshMinutes,
  update,
  checkingUpdate,
  onCheckUpdate,
  checkUpdates,
  onCheckUpdates,
}: Props) {
  const { t, language, languages, setLanguage, errorText } = useI18n();
  const [cleared, setCleared] = useState(false);
  const [version, setVersion] = useState<string | null>(null);

  useEffect(() => {
    getVersion()
      .then((v) => setVersion(v.version))
      .catch(() => setVersion(null));
  }, []);

  const refreshLabel = (minutes: number) =>
    minutes === 0
      ? t('refresh.off')
      : minutes < 60
        ? t('refresh.minutes', { count: minutes })
        : t('refresh.hours', { count: minutes / 60 });

  return (
    <>
      <section className="card">
        <header className="card-head">
          <h2>{t('settings.appearance')}</h2>
        </header>
        <div className="setting">
          <div className="setting-text">
            <strong id="theme-label">{t('settings.theme')}</strong>
            <p className="muted small">{t('settings.themeHint')}</p>
          </div>
          <RadioGroup
            className="seg"
            itemClass="seg-btn"
            labelledBy="theme-label"
            value={theme}
            options={THEMES.map((option) => ({ value: option, label: t(`theme.${option}` as 'theme.dark') }))}
            onChange={onTheme}
          />
        </div>
      </section>

      <section className="card">
        <header className="card-head">
          <h2>{t('settings.language')}</h2>
        </header>
        <div className="setting">
          <div className="setting-text">
            <label htmlFor="language" className="strong">
              {t('settings.languageLabel')}
            </label>
            <p className="muted small">{t('settings.languageHint')}</p>
          </div>
          <select id="language" className="select" value={language.code} onChange={(e) => setLanguage(e.target.value)}>
            {languages.map((l) => (
              <option key={l.code} value={l.code} lang={l.code}>
                {l.name}
              </option>
            ))}
          </select>
        </div>
      </section>

      <section className="card">
        <header className="card-head">
          <h2>{t('settings.refresh')}</h2>
        </header>
        <div className="setting">
          <div className="setting-text">
            <label htmlFor="refresh" className="strong">
              {t('settings.refreshLabel')}
            </label>
            <p className="muted small">{t('settings.refreshHint')}</p>
          </div>
          <select
            id="refresh"
            className="select"
            value={refreshMinutes}
            onChange={(e) => onRefreshMinutes(Number(e.target.value))}
          >
            {REFRESH_OPTIONS.map((minutes) => (
              <option key={minutes} value={minutes}>
                {refreshLabel(minutes)}
              </option>
            ))}
          </select>
        </div>
      </section>

      <section className="card">
        <header className="card-head">
          <h2>{t('settings.cache')}</h2>
        </header>
        <div className="setting">
          <div className="setting-text">
            <strong>{t('settings.cacheLabel')}</strong>
            <p className="muted small">{t('settings.cacheHint', { size: snapshotSizeKb() })}</p>
          </div>
          <button
            className="btn"
            disabled={cleared}
            onClick={() => {
              clearSnapshot();
              setCleared(true);
            }}
          >
            {cleared ? t('settings.cacheCleared') : t('settings.cacheClear')}
          </button>
        </div>
      </section>

      <AccountsView accounts={accounts} onChanged={onAccountsChanged} />

      <section className="card">
        <header className="card-head">
          <h2>{t('settings.about')}</h2>
        </header>
        <div className="setting">
          <div className="setting-text">
            <strong>{t('settings.version')}</strong>
            <p className="muted small">Mira {version ?? '—'}</p>
          </div>
          <a className="btn" href="https://github.com/Stellatinus/Mira/releases" target="_blank" rel="noreferrer">
            {t('settings.releases')}
            <span className="visually-hidden"> ({t('a11y.newTab')})</span>
          </a>
        </div>
        <div className="setting">
          <div className="setting-text">
            <strong>{t('update.title')}</strong>
            <p className={update?.error ? 'warn-text small' : 'muted small'} role="status">
              {checkingUpdate
                ? t('update.checking')
                : !update
                  ? t('update.unknown')
                  : update.error
                    ? `${t('update.error')} (${errorText(update.error)})`
                    : update.latest === null
                      ? t('update.dev')
                      : update.available
                        ? t('update.available', { version: update.latest })
                        : t('update.upToDate')}
              {!checkingUpdate && update?.available && update.url && (
                <>
                  {' '}
                  <a href={update.url} target="_blank" rel="noreferrer">
                    {t('update.notes')}
                    <span className="visually-hidden"> ({t('a11y.newTab')})</span>
                  </a>
                </>
              )}
            </p>
          </div>
          <button className="btn" aria-disabled={checkingUpdate} onClick={() => !checkingUpdate && onCheckUpdate()}>
            {t('update.check')}
          </button>
        </div>
        <div className="setting">
          <div className="setting-text">
            <label htmlFor="check-updates" className="strong">
              {t('update.auto')}
            </label>
            <p className="muted small">{t('update.autoHint')}</p>
          </div>
          <input id="check-updates" type="checkbox" checked={checkUpdates} onChange={(e) => onCheckUpdates(e.target.checked)} />
        </div>
      </section>
    </>
  );
}
