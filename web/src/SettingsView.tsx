import { AccountsView } from './AccountsView';
import { useI18n } from './i18n';
import { THEMES } from './theme';
import type { ThemePreference } from './theme';
import type { AccountStatus } from './types';

interface Props {
  accounts: AccountStatus[];
  onAccountsChanged: () => Promise<void>;
  theme: ThemePreference;
  onTheme: (theme: ThemePreference) => void;
}

export function SettingsView({ accounts, onAccountsChanged, theme, onTheme }: Props) {
  const { t, language, languages, setLanguage } = useI18n();

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
          <div className="seg" role="radiogroup" aria-labelledby="theme-label">
            {THEMES.map((option) => (
              <button
                key={option}
                role="radio"
                aria-checked={theme === option}
                className={theme === option ? 'seg-btn active' : 'seg-btn'}
                onClick={() => onTheme(option)}
              >
                {t(`theme.${option}` as 'theme.dark')}
              </button>
            ))}
          </div>
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

      <AccountsView accounts={accounts} onChanged={onAccountsChanged} />
    </>
  );
}
