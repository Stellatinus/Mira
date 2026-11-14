import { useId, useMemo, useState } from 'react';
import type { MouseEvent } from 'react';
import { Avatar } from './Avatar';
import { useI18n } from './i18n';
import { attentionLines, indicator, KINDS, repoKey } from './attention';
import type { Attention } from './attention';
import { ExternalIcon, ItemIcon, PullRequestIcon, StarIcon } from './icons';
import { reasonLabel, tagLabel } from './labels';
import { ciState } from './checks';
import { reviewBadge } from './review';
import type { ViewConfig } from './sections';
import { isHidden } from './seen';
import type { SeenMap } from './seen';
import { Tile } from './Tile';
import type { ViewModel } from './viewmodel';
import type { AccountStatus, Dashboard, DashboardError, DashboardItem, Section } from './types';

export type Mode = 'grid' | 'list' | 'compact';

/** Nella Home ogni sezione mostra poche card; nelle altre viste c'è molto più spazio. */
const COLLAPSED_HOME = 6;
const COLLAPSED_OTHER = 24;

interface Props {
  data: Dashboard;
  view: ViewConfig;
  /** Login dell'account scelto, oppure "all". */
  filter: string;
  mode: Mode;
  selectedId: string | null;
  /** Cosa c'è di aperto per ogni repository, ricavato dagli elementi già scaricati. */
  attention: Map<string, Attention>;
  /** Gli elementi di ogni sezione, già filtrati e ordinati, con i loro conteggi. */
  model: ViewModel;
  /** Elementi visti o rimandati e se vanno mostrati comunque (allora portano un'etichetta). */
  seen: SeenMap;
  showSeen: boolean;
  /** `viaKeyboard`: la scelta viene da Invio o Spazio, e il focus deve passare al pannello. */
  onSelect: (id: string, viaKeyboard?: boolean) => void;
  onManageAccounts: () => void;
}

/** L'ultimo numero di un conteggio («36», «123 di 214», «100+»): è quello che decide il singolare o il plurale. */
export const lastNumber = (text: string) => Number(/\d+(?=\D*$)/.exec(text)?.[0] ?? 0);

const sectionTitle = (key: Section) => `section.${key}.title` as 'section.review.title';
const sectionHint = (key: Section) => `section.${key}.hint` as 'section.review.hint';

export function ItemsView({ data, view, filter, mode, selectedId, attention, model, seen, showSeen, onSelect, onManageAccounts }: Props) {
  const i18n = useI18n();
  const { t, errorText } = i18n;
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const accountsByLogin = useMemo(() => new Map(data.accounts.map((a) => [a.login, a])), [data.accounts]);
  const { bySection, needle, totalLabel } = model;

  if (data.accounts.length === 0) {
    return (
      <div className="empty">
        <h2>{t('items.noAccount.title')}</h2>
        <p className="muted">{t('items.noAccount.text')}</p>
        <button className="btn primary" onClick={onManageAccounts}>
          {t('items.noAccount.action')}
        </button>
      </div>
    );
  }

  const shownTotal = view.sections.reduce((sum, key) => sum + (bySection.get(key)?.length ?? 0), 0);
  const repoFiltering = view.sections.some(model.narrowed);
  // Una vista con una sola sezione ha già il titolo e il conteggio nella riga in alto: niente intestazione ripetuta.
  const showGroupHeader = view.sections.length > 1;
  const collapsedCount = view.key === 'home' ? COLLAPSED_HOME : COLLAPSED_OTHER;
  const showAccount = data.accounts.length > 1 && filter === 'all';

  // "account / sezione: errore" (senza sezione quando l'errore riguarda l'account intero).
  const problemLine = (problem: DashboardError) => {
    const where = problem.section === 'account' ? '' : ` / ${t(sectionTitle(problem.section as Section))}`;
    return `${problem.account}${where}: ${errorText(problem.error)}`;
  };

  return (
    <>
      {view.key === 'home' && (
        <div className="stats">
          {view.sections.map((key) =>
            // Una sezione senza elementi non viene disegnata: il suo riquadro non può essere un link verso il nulla.
            (bySection.get(key)?.length ?? 0) === 0 ? (
              <div key={key} className="stat zero" data-section={key}>
                <strong>{totalLabel(key)}</strong>
                <span>{t(sectionTitle(key))}</span>
              </div>
            ) : (
              <a
                key={key}
                className="stat"
                data-section={key}
                href={`#sec-${key}`}
                // Sposta la pagina alla sezione senza scrivere nell'URL: l'hash è della vista, e Indietro non deve
                // passare da una tappa per ogni riquadro cliccato.
                onClick={(event) => {
                  event.preventDefault();
                  document.getElementById(`sec-${key}`)?.scrollIntoView();
                }}
              >
                <strong>{totalLabel(key)}</strong>
                <span>{t(sectionTitle(key))}</span>
              </a>
            ),
          )}
        </div>
      )}

      {shownTotal === 0 && (
        <div className="empty">
          <h2>{needle || repoFiltering ? t('items.noResults.title') : t('items.empty.title')}</h2>
          <p className="muted">
            {needle
              ? t('items.noResults.text')
              : repoFiltering
                ? t(view.tools === 'items' ? 'items.noResults.itemFilterText' : 'items.noResults.filterText')
                : t('items.empty.text')}
          </p>
        </div>
      )}

      {view.sections.map((key) => {
        const items = bySection.get(key) ?? [];
        if (items.length === 0) return null;

        const isExpanded = expanded[key] === true;
        const visible = isExpanded ? items : items.slice(0, collapsedCount);
        const truncated = model.truncated(key);

        return (
          <section
            key={key}
            id={`sec-${key}`}
            className="group"
            data-section={key}
            // Il nome della regione è il titolo; senza intestazione visibile (vista con una sola sezione) è un'etichetta.
            aria-labelledby={showGroupHeader ? `sec-title-${key}` : undefined}
            aria-label={showGroupHeader ? undefined : t(sectionTitle(key))}
          >
            {showGroupHeader && (
              <header className="group-head">
                <h2 id={`sec-title-${key}`}>{t(sectionTitle(key))}</h2>
                <span className="count" title={truncated ? t('items.mostRecent') : undefined}>
                  {model.countText(key)}
                  {/* Il numero da solo non dice cosa conta. */}
                  <span className="visually-hidden"> {t('items.unit', { count: lastNumber(model.countText(key)) })}</span>
                </span>
                <span className="hint muted">{t(sectionHint(key))}</span>
              </header>
            )}

            <div className={`cards ${mode}`}>
              {visible.map((item) => {
                // Un repository è una card sola anche se lo vedono più account: gli avatar sono tutti i suoi account.
                const info = item.type === 'repo' ? model.repoInfo.get(item.id) : undefined;
                const logins = info?.accounts ?? [item.account];
                return (
                  <ItemCard
                    key={item.id}
                    item={item}
                    accounts={logins.map((login) => accountsByLogin.get(login) ?? { login, avatarUrl: null })}
                    starredBy={info?.starredBy ?? []}
                    attention={item.type === 'repo' ? attention.get(repoKey(item.repo)) : undefined}
                    selected={item.id === selectedId}
                    seenState={showSeen ? seenStateOf(seen.get(item.url), item) : null}
                    showAccount={showAccount}
                    onSelect={onSelect}
                  />
                );
              })}
            </div>

            {items.length > collapsedCount && (
              <button className="more" onClick={() => setExpanded({ ...expanded, [key]: !isExpanded })}>
                {isExpanded
                  ? t('items.showLess')
                  : truncated
                    ? t('items.showRecent', { count: items.length })
                    : t('items.showAll', { count: items.length })}
              </button>
            )}
            {truncated && isExpanded && (
              <p className="truncated muted">
                {t('items.truncatedNote', { shown: items.length, total: totalLabel(key) })}
              </p>
            )}
          </section>
        );
      })}

      {data.errors.length > 0 && (
        <details className="problems">
          <summary>{t('items.problems', { count: data.errors.length })}</summary>
          <ul>
            {data.errors.map((problem) => (
              <li key={`${problem.account}:${problem.section}`}>{problemLine(problem)}</li>
            ))}
          </ul>
        </details>
      )}
    </>
  );
}

interface CardProps {
  item: DashboardItem;
  /** Gli account da mostrare come avatar (più di uno per un repository visto da più account). */
  accounts: Pick<AccountStatus, 'login' | 'avatarUrl'>[];
  /** Gli account che hanno messo la stella al repository. */
  starredBy: string[];
  attention?: Attention;
  selected: boolean;
  /** Se l'elemento è già visto o rimandato (mostrato perché si è chiesto «mostra già visti»). */
  seenState: 'seen' | 'snoozed' | null;
  showAccount: boolean;
  onSelect: (id: string, viaKeyboard?: boolean) => void;
}

/**
 * Indicatori sulle card dei repository, uno per le PR e uno per le issue, così un numero non può
 * passare per l'altro. Il numero è quante ce ne sono di aperte nel repository; il colore dice il tipo
 * (PR blu, issue arancio); il riempimento dice se tra queste qualcosa aspetta te (pieno) o no (solo contorno).
 */
function AttentionBadges({ item, attention }: { item: DashboardItem; attention?: Attention }) {
  const { t } = useI18n();

  const badges = KINDS.map((kind) => {
    const { count, urgent } = indicator(item, attention, kind);
    if (count === 0) return null;

    const text = `${t(`attention.kind.${kind}` as 'attention.kind.pr')}: ${attentionLines(item, attention, kind, t).join(' · ')}`;
    return (
      <span key={kind} className={urgent ? `ci-attn ${kind} urgent` : `ci-attn ${kind}`} title={text} aria-label={text} role="img">
        {kind === 'pr' ? <PullRequestIcon size={13} /> : <ItemIcon type="issue" size={13} />}
        {count}
      </span>
    );
  });

  if (badges.every((badge) => badge === null)) return null;
  return <span className="ci-attns">{badges}</span>;
}

function seenStateOf(entry: Parameters<typeof isHidden>[0], item: DashboardItem): 'seen' | 'snoozed' | null {
  if (!isHidden(entry, item, Date.now())) return null;
  return entry?.snoozeUntil ? 'snoozed' : 'seen';
}

function ItemCard({ item, accounts, starredBy, attention, selected, seenState, showAccount, onSelect }: CardProps) {
  const i18n = useI18n();
  const { t, timeAgo } = i18n;
  const when = item.reason ? `${reasonLabel(item.reason, i18n)} · ${timeAgo(item.updatedAt)}` : timeAgo(item.updatedAt);
  const titleId = useId();

  const stop = (event: MouseEvent) => event.stopPropagation();

  // La card non è un pulsante: con role="button" i figli diventano presentazionali (il link e il titolo
  // spariscono per i lettori di schermo). Il controllo che seleziona è un vero <button> dentro il titolo;
  // il clic del mouse sul resto della card seleziona comunque (il clic sul pulsante risale fin qui).
  return (
    <article
      className={selected ? 'item-card selected' : 'item-card'}
      aria-labelledby={titleId}
      data-card-id={item.id}
      onClick={(event) => {
        // Il pulsante del titolo e il link hanno i loro clic: qui solo il resto della card.
        if (!(event.target as HTMLElement).closest('.ci-select, a')) onSelect(item.id);
      }}
    >
      {item.type === 'repo' ? (
        // Le stelle non sono sulla card: stanno nel pannello dei dettagli.
        <AttentionBadges item={item} attention={attention} />
      ) : (
        <span className="ci-tag">
          <span className="tag-text">{tagLabel(item, i18n)}</span>
          {reviewBadge(item.review) && (
            <span className={`ci-review ${reviewBadge(item.review)}`}>{t(`review.badge.${reviewBadge(item.review)}` as 'review.badge.approved')}</span>
          )}
          {ciState(item.checks) && (
            <span className={`ci-review ci-${ciState(item.checks)}`}>{t(`checks.badge.${ciState(item.checks)}` as 'checks.badge.success')}</span>
          )}
          {seenState && <span className="ci-seen">· {t(`seen.badge.${seenState}` as 'seen.badge.seen')}</span>}
        </span>
      )}
      <span className="ci-actions">
        {starredBy.length > 0 && (
          // Preferito: la stella (senza numero) dice che almeno un account l'ha messa; il tooltip dice chi.
          <span
            className="ci-star"
            role="img"
            title={t('repo.starredBy', { accounts: starredBy.join(', ') })}
            aria-label={t('repo.starredBy', { accounts: starredBy.join(', ') })}
          >
            <StarIcon size={14} filled />
          </span>
        )}
        <a
          className="ci-open"
          href={item.url}
          target="_blank"
          rel="noreferrer"
          onClick={stop}
          aria-label={`${t('items.openOnGitHub')}: ${item.title} (${t('a11y.newTab')})`}
        >
          <ExternalIcon size={15} />
        </a>
      </span>
      <Tile item={item} iconSize={26} />
      <h3 className="ci-title" id={titleId}>
        <button
          type="button"
          className="ci-select"
          aria-pressed={selected}
          title={item.title}
          // detail === 0: il clic è nato da tastiera (Invio, Spazio), non dal mouse.
          onClick={(event) => onSelect(item.id, event.detail === 0)}
        >
          <span className="ci-title-text">{item.title}</span>
        </button>
      </h3>
      <div className="rule" />
      <div className="ci-meta">
        <span className="k">{item.repo}</span>
        <span className="v">{when}</span>
      </div>
      {showAccount && (
        <span className="ci-acct" title={accounts.map((a) => a.login).join(', ')}>
          {accounts.slice(0, 3).map((account) => (
            <Avatar key={account.login} account={account} size={22} />
          ))}
          {/* Gli avatar sono decorativi: il nome degli account lo legge questo testo. */}
          <span className="visually-hidden">{t('items.seenBy', { accounts: accounts.map((a) => a.login).join(', ') })}</span>
        </span>
      )}
    </article>
  );
}
