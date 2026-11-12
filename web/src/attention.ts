import type { I18n } from './i18n';
import type { DashboardItem, Section } from './types';

/** Cose che aspettano te: review richieste, assegnate, menzioni, notifiche non lette. */
const NEEDS_ATTENTION: Section[] = ['review', 'assigned', 'mentions', 'notifications'];
/** Cose tue ancora aperte: utili da sapere, ma non urgenti. */
const MINE_OPEN: Section[] = ['mine-prs', 'mine-issues'];

/** Le PR e tutto il resto (issue, ma anche release, discussioni… che arrivano dalle notifiche). */
export type Kind = 'pr' | 'issue';
export const KINDS: Kind[] = ['pr', 'issue'];

export interface KindAttention {
  /** Quanti elementi per sezione (una stessa PR può comparire in più sezioni). */
  counts: Partial<Record<Section, number>>;
  /** Elementi distinti che aspettano te in questo repository. */
  needs: number;
  /** Elementi tuoi ancora aperti, esclusi quelli già contati in `needs`. */
  mine: number;
}

/** PR e issue sono contate a parte, così un numero non può passare per l'altro. */
export type Attention = Record<Kind, KindAttention>;

export const repoKey = (account: string, repo: string) => `${account}|${repo}`.toLowerCase();

const kindOf = (item: DashboardItem): Kind => (item.type === 'pr' ? 'pr' : 'issue');

/**
 * Per ogni repository (di un account) riassume cosa c'è di aperto che lo riguarda,
 * usando gli elementi già scaricati: nessuna chiamata in più a GitHub.
 * I conteggi sono quelli degli elementi scaricati (max 100 per sezione e account).
 */
export function buildAttention(items: DashboardItem[]): Map<string, Attention> {
  const result = new Map<string, Attention>();
  // Url già visti, per contare una volta sola un elemento che compare in più sezioni.
  const seenNeeds = new Map<string, Set<string>>();
  const seenMine = new Map<string, Set<string>>();

  const empty = (): KindAttention => ({ counts: {}, needs: 0, mine: 0 });
  const seen = (map: Map<string, Set<string>>, key: string) => {
    let set = map.get(key);
    if (!set) map.set(key, (set = new Set()));
    return set;
  };

  for (const item of items) {
    const needs = NEEDS_ATTENTION.includes(item.section);
    if (!needs && !MINE_OPEN.includes(item.section)) continue;

    const repo = repoKey(item.account, item.repo);
    let entry = result.get(repo);
    if (!entry) result.set(repo, (entry = { pr: empty(), issue: empty() }));

    const kind = kindOf(item);
    const target = entry[kind];
    target.counts[item.section] = (target.counts[item.section] ?? 0) + 1;

    const scope = `${repo}|${kind}`;
    if (needs) seen(seenNeeds, scope).add(item.url);
    else seen(seenMine, scope).add(item.url);
  }

  for (const [repo, entry] of result) {
    for (const kind of KINDS) {
      const scope = `${repo}|${kind}`;
      const needing = seenNeeds.get(scope) ?? new Set<string>();
      entry[kind].needs = needing.size;
      entry[kind].mine = [...(seenMine.get(scope) ?? [])].filter((url) => !needing.has(url)).length;
    }
  }
  return result;
}

/** Il numero da mostrare per un tipo e se è urgente (qualcosa aspetta te) o solo «tuo e aperto». */
export function indicator(attention: KindAttention): { count: number; urgent: boolean } {
  const urgent = attention.needs > 0;
  return { count: urgent ? attention.needs : attention.mine, urgent };
}

/** Le righe di spiegazione («2 assegnate a te», «1 notifica non letta»…), nell'ordine di importanza. */
export function attentionLines(attention: KindAttention, t: I18n['t']): string[] {
  return [...NEEDS_ATTENTION, ...MINE_OPEN]
    .filter((section) => (attention.counts[section] ?? 0) > 0)
    .map((section) => t(`attention.${section}` as 'attention.review', { count: attention.counts[section] ?? 0 }));
}
