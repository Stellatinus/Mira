import type { I18n } from './i18n';
import type { DashboardItem, Section } from './types';

/** Cose che aspettano te: review richieste, assegnate, menzioni, notifiche non lette. */
const NEEDS_ATTENTION: Section[] = ['review', 'assigned', 'mentions', 'notifications'];
/** Cose tue ancora aperte: le mostriamo nel tooltip, ma non rendono urgente l'indicatore. */
const MINE_OPEN: Section[] = ['mine-prs', 'mine-issues'];

/** Le PR e tutto il resto (issue, ma anche release, discussioni… che arrivano dalle notifiche). */
export type Kind = 'pr' | 'issue';
export const KINDS: Kind[] = ['pr', 'issue'];

export interface KindAttention {
  /** Quanti elementi per sezione (una stessa PR può comparire in più sezioni o in più account). */
  counts: Partial<Record<Section, number>>;
  /** Elementi distinti che aspettano te in questo repository, per tutti gli account insieme. */
  needs: number;
}

/** PR e issue sono contate a parte, così un numero non può passare per l'altro. */
export type Attention = Record<Kind, KindAttention>;

/** Un repository è lo stesso per tutti gli account che lo vedono: la chiave non include l'account. */
export const repoKey = (repo: string) => repo.toLowerCase();

const kindOf = (item: DashboardItem): Kind => (item.type === 'pr' ? 'pr' : 'issue');

/**
 * Per ogni repository riassume cosa aspetta te, usando gli elementi già scaricati di tutti gli
 * account: nessuna chiamata in più a GitHub. I conteggi sono quelli degli elementi scaricati
 * (max 100 per sezione e account).
 */
export function buildAttention(items: DashboardItem[]): Map<string, Attention> {
  const result = new Map<string, Attention>();
  // Url già visti, per contare una volta sola un elemento che compare in più sezioni o account.
  const seenNeeds = new Map<string, Set<string>>();

  const empty = (): KindAttention => ({ counts: {}, needs: 0 });

  for (const item of items) {
    const needs = NEEDS_ATTENTION.includes(item.section);
    if (!needs && !MINE_OPEN.includes(item.section)) continue;

    const repo = repoKey(item.repo);
    let entry = result.get(repo);
    if (!entry) result.set(repo, (entry = { pr: empty(), issue: empty() }));

    const kind = kindOf(item);
    entry[kind].counts[item.section] = (entry[kind].counts[item.section] ?? 0) + 1;

    if (needs) {
      const scope = `${repo}|${kind}`;
      let urls = seenNeeds.get(scope);
      if (!urls) seenNeeds.set(scope, (urls = new Set()));
      urls.add(item.url);
    }
  }

  for (const [repo, entry] of result) {
    for (const kind of KINDS) entry[kind].needs = seenNeeds.get(`${repo}|${kind}`)?.size ?? 0;
  }
  return result;
}

/**
 * Il numero da mostrare per un tipo e se è urgente. Il numero è quante PR (o issue) sono aperte nel
 * repository, secondo GitHub; è urgente se tra queste ce n'è almeno una che aspetta te.
 */
export function indicator(
  repo: Pick<DashboardItem, 'openPrs' | 'openIssues'>,
  attention: Attention | undefined,
  kind: Kind,
): { count: number; urgent: boolean } {
  const needs = attention?.[kind].needs ?? 0;
  const open = kind === 'pr' ? repo.openPrs : repo.openIssues;
  return { count: Math.max(open, needs), urgent: needs > 0 };
}

/** Le righe di spiegazione («2 aperte», «1 assegnata a te»…), nell'ordine di importanza. */
export function attentionLines(
  repo: Pick<DashboardItem, 'openPrs' | 'openIssues'>,
  attention: Attention | undefined,
  kind: Kind,
  t: I18n['t'],
): string[] {
  const lines: string[] = [];
  const { count } = indicator(repo, attention, kind);
  if (count > 0) lines.push(t('attention.open', { count }));

  const counts = attention?.[kind].counts ?? {};
  for (const section of [...NEEDS_ATTENTION, ...MINE_OPEN]) {
    const n = counts[section] ?? 0;
    if (n > 0) lines.push(t(`attention.${section}` as 'attention.review', { count: n }));
  }
  return lines;
}
