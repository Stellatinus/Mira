import type { I18n } from './i18n';
import type { DashboardItem, Section } from './types';

/** Cose che aspettano te: review richieste, assegnate, menzioni, notifiche non lette. */
const NEEDS_ATTENTION: Section[] = ['review', 'assigned', 'mentions', 'notifications'];
/** Quelle che si scrivono come riga a parte; le assegnate stanno già nel «(3 tue)» accanto al totale. */
const EXTRA_LINES: Section[] = ['review', 'mentions', 'notifications'];

/** Le PR e tutto il resto (issue, ma anche release, discussioni… che arrivano dalle notifiche). */
export type Kind = 'pr' | 'issue';
export const KINDS: Kind[] = ['pr', 'issue'];

export interface KindAttention {
  /** Quanti elementi per sezione (una stessa PR può comparire in più sezioni o in più account). */
  counts: Partial<Record<Section, number>>;
  /** Elementi distinti che aspettano te in questo repository, per tutti gli account insieme. */
  needs: number;
  /** Elementi distinti assegnati a te in questo repository («tue»), per tutti gli account insieme. */
  assigned: number;
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
  const seenAssigned = new Map<string, Set<string>>();

  const empty = (): KindAttention => ({ counts: {}, needs: 0, assigned: 0 });
  const remember = (map: Map<string, Set<string>>, scope: string, url: string) => {
    let urls = map.get(scope);
    if (!urls) map.set(scope, (urls = new Set()));
    urls.add(url);
  };

  for (const item of items) {
    if (!NEEDS_ATTENTION.includes(item.section)) continue;

    const repo = repoKey(item.repo);
    let entry = result.get(repo);
    if (!entry) result.set(repo, (entry = { pr: empty(), issue: empty() }));

    const kind = kindOf(item);
    entry[kind].counts[item.section] = (entry[kind].counts[item.section] ?? 0) + 1;

    const scope = `${repo}|${kind}`;
    remember(seenNeeds, scope, item.url);
    if (item.section === 'assigned') remember(seenAssigned, scope, item.url);
  }

  for (const [repo, entry] of result) {
    for (const kind of KINDS) {
      entry[kind].needs = seenNeeds.get(`${repo}|${kind}`)?.size ?? 0;
      entry[kind].assigned = seenAssigned.get(`${repo}|${kind}`)?.size ?? 0;
    }
  }
  return result;
}

type RepoCounts = Pick<DashboardItem, 'openPrs' | 'openIssues' | 'unassignedIssues'>;

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

export interface KindSummary {
  /** Aperte nel repository. */
  open: number;
  /** Tra le aperte, quante sono assegnate a te. */
  mine: number;
  /** Solo per le issue: aperte senza assegnatario, su tutto il repository. */
  unassigned: number;
  /** Il resto che aspetta te (review richieste, menzioni, notifiche non lette), già scritto. */
  extra: string[];
}

export function kindSummary(
  repo: RepoCounts,
  attention: Attention | undefined,
  kind: Kind,
  t: I18n['t'],
): KindSummary {
  const { count: open } = indicator(repo, attention, kind);
  const counts = attention?.[kind].counts ?? {};
  return {
    open,
    // Non possono essere più di quelle aperte (gli elementi scaricati possono essere in ritardo su GitHub).
    mine: Math.min(attention?.[kind].assigned ?? 0, open),
    unassigned: kind === 'issue' ? Math.min(repo.unassignedIssues, open) : 0,
    extra: EXTRA_LINES.filter((section) => (counts[section] ?? 0) > 0).map((section) =>
      t(`attention.${section}` as 'attention.review', { count: counts[section] ?? 0 }),
    ),
  };
}

/** «31 aperte (3 tue)», poi le non assegnate e il resto: le righe di una sezione, in ordine di importanza. */
export function summaryLines(summary: KindSummary, t: I18n['t']): string[] {
  const lines: string[] = [];
  if (summary.open > 0) {
    const open = t('attention.open', { count: summary.open });
    lines.push(summary.mine > 0 ? `${open} (${t('attention.mine', { count: summary.mine })})` : open);
  }
  if (summary.unassigned > 0) lines.push(t('attention.unassigned', { count: summary.unassigned }));
  return [...lines, ...summary.extra];
}

/** Le stesse righe, per il tooltip delle card. */
export function attentionLines(repo: RepoCounts, attention: Attention | undefined, kind: Kind, t: I18n['t']): string[] {
  return summaryLines(kindSummary(repo, attention, kind, t), t);
}
