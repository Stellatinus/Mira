import { useCallback, useEffect, useState } from 'react';
import { getRepoPage } from './api';
import { failureText } from './errors';
import { useI18n } from './i18n';
import { ExternalIcon, ItemIcon, RefreshIcon } from './icons';
import type { RepoMilestone, RepoPageData, RepoPageItem } from './types';

interface Props {
  /** «proprietario/nome» del repository. */
  repo: string;
  onBack: () => void;
}

/** Le milestone in ordine di «timeline»: aperte per scadenza (senza scadenza in fondo), poi le chiuse. */
export function sortMilestones(milestones: RepoMilestone[]): RepoMilestone[] {
  const due = (m: RepoMilestone) => (m.dueOn ? Date.parse(m.dueOn) : Number.POSITIVE_INFINITY);
  return [...milestones].sort(
    (a, b) =>
      Number(a.state === 'closed') - Number(b.state === 'closed') || due(a) - due(b) || a.number - b.number,
  );
}

/** La pagina di un repository: timeline delle milestone, PR aperte e tutte le issue aperte. */
export function RepoPage({ repo, onBack }: Props) {
  const i18n = useI18n();
  const { t, timeAgo, formatDate } = i18n;
  const [data, setData] = useState<RepoPageData | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(
    async (refresh: boolean) => {
      setLoading(true);
      try {
        setData(await getRepoPage(repo, refresh));
        setError(null);
      } catch (e) {
        setError(e);
      } finally {
        setLoading(false);
      }
    },
    [repo],
  );

  useEffect(() => {
    setData(null);
    void load(false);
  }, [load]);

  const milestones = data ? sortMilestones(data.milestones) : [];
  const titleOf = (number: number | null) => milestones.find((m) => m.number === number)?.title;

  return (
    <main className="main" id="main" tabIndex={-1}>
      <header className="main-head">
        <div className="repo-head">
          <button className="btn" onClick={onBack}>
            ← {t('repoPage.back')}
          </button>
          <h1 tabIndex={-1}>{repo}</h1>
        </div>
        <div className="head-actions">
          {data && (
            <a className="btn repo-ext" href={data.url} target="_blank" rel="noreferrer">
              <ExternalIcon size={14} /> {t('repoPage.openGithub')}
            </a>
          )}
          <button
            className={loading ? 'icon-btn spinning' : 'icon-btn'}
            onClick={() => {
              if (!loading) void load(true);
            }}
            aria-disabled={loading}
            title={t('app.refresh')}
            aria-label={t('app.refresh')}
          >
            <RefreshIcon />
          </button>
        </div>
      </header>
      <div className="rule" />

      <div className="scroll repo-page" aria-busy={loading}>
        {error !== null && (
          <div className="alert danger" role="alert">
            {failureText(error, i18n)}
          </div>
        )}
        {data === null && error === null && <p className="muted">{t('repoPage.loading')}</p>}

        {data && (
          <>
            {data.description && <p className="repo-desc">{data.description}</p>}

            <section aria-labelledby="rp-timeline">
              <h2 id="rp-timeline" className="repo-section">
                {t('repoPage.timeline')}
              </h2>
              {milestones.length === 0 ? (
                <p className="muted">{t('repoPage.noMilestones')}</p>
              ) : (
                <ol className="timeline">
                  {milestones.map((m) => {
                    const overdue = m.state === 'open' && m.dueOn !== null && Date.parse(m.dueOn) < Date.now();
                    return (
                      <li key={m.number} className={m.state === 'closed' ? 'tl-item closed' : 'tl-item'}>
                        <span className={overdue ? 'tl-dot overdue' : 'tl-dot'} aria-hidden="true" />
                        <div className="tl-body">
                          <a href={m.url} target="_blank" rel="noreferrer" className="tl-title">
                            {m.title}
                          </a>
                          <span className="tl-meta">
                            {m.state === 'closed' ? t('repoPage.closed') : overdue ? t('repoPage.overdue') : t('repoPage.open')}
                            {m.dueOn && <> · <span title={formatDate(m.dueOn)}>{t('repoPage.due', { when: formatDate(m.dueOn) })}</span></>}
                            {' · '}
                            {t('repoPage.issuesProgress', { open: m.openIssues, closed: m.closedIssues })}
                          </span>
                          <div
                            className="tl-bar"
                            role="progressbar"
                            aria-valuemin={0}
                            aria-valuemax={100}
                            aria-valuenow={Math.round(m.progress)}
                            aria-label={t('repoPage.progress', { title: m.title })}
                          >
                            <span style={{ width: `${Math.min(100, Math.max(0, m.progress))}%` }} />
                          </div>
                          {m.description && <p className="tl-desc">{m.description}</p>}
                        </div>
                      </li>
                    );
                  })}
                </ol>
              )}
            </section>

            <ItemList
              id="rp-prs"
              type="pr"
              title={t('repoPage.prs')}
              empty={t('repoPage.noPrs')}
              items={data.prs}
              total={data.prTotal}
              milestoneTitle={titleOf}
            />
            <ItemList
              id="rp-issues"
              type="issue"
              title={t('repoPage.issues')}
              empty={t('repoPage.noIssues')}
              items={data.issues}
              total={data.issueTotal}
              milestoneTitle={titleOf}
            />
            <p className="muted repo-foot">{t('repoPage.updated', { when: timeAgo(data.generatedAt) })}</p>
          </>
        )}
      </div>
    </main>
  );
}

function ItemList({
  id,
  type,
  title,
  empty,
  items,
  total,
  milestoneTitle,
}: {
  id: string;
  type: 'pr' | 'issue';
  title: string;
  empty: string;
  items: RepoPageItem[];
  total: number;
  milestoneTitle: (number: number | null) => string | undefined;
}) {
  const { t, timeAgo } = useI18n();
  return (
    <section aria-labelledby={id}>
      <h2 id={id} className="repo-section">
        {title} <span className="count">{total}</span>
      </h2>
      {items.length === 0 ? (
        <p className="muted">{empty}</p>
      ) : (
        <ul className="repo-items">
          {items.map((item) => (
            <li key={item.number} className="repo-item">
              <ItemIcon type={type} size={16} />
              <div className="ri-body">
                <a href={item.url} target="_blank" rel="noreferrer" className="ri-title">
                  {item.title}
                </a>
                <span className="ri-meta">
                  #{item.number}
                  {item.draft && <> · {t('repoPage.draft')}</>}
                  {item.author && <> · {item.author}</>}
                  {' · '}
                  {timeAgo(item.updatedAt)}
                  {item.assignees.length > 0 && <> · {t('repoPage.assignedTo', { who: item.assignees.join(', ') })}</>}
                  {item.milestone !== null && milestoneTitle(item.milestone) && <> · {milestoneTitle(item.milestone)}</>}
                </span>
              </div>
            </li>
          ))}
        </ul>
      )}
      {total > items.length && <p className="muted">{t('repoPage.mostRecent', { shown: items.length, total })}</p>}
    </section>
  );
}
