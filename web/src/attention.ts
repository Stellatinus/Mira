import type { I18n } from './i18n';
import type { DashboardItem, Section } from './types';

/** Cose che aspettano te: review richieste, assegnate, menzioni, notifiche non lette. */
const NEEDS_ATTENTION: Section[] = ['review', 'assigned', 'mentions', 'notifications'];
/** Cose tue ancora aperte: utili da sapere, ma non urgenti. */
const MINE_OPEN: Section[] = ['mine-prs', 'mine-issues'];

export interface Attention {
  /** Quanti elementi per sezione (una stessa PR può comparire in più sezioni). */
  counts: Partial<Record<Section, number>>;
  /** Elementi distinti che aspettano te in questo repository. */
  needs: number;
  /** Elementi tuoi ancora aperti, esclusi quelli già contati in `needs`. */
  mine: number;
}

export const repoKey = (account: string, repo: string) => `${account}|${repo}`.toLowerCase();

/**
 * Per ogni repository (di un account) riassume cosa c'è di aperto che lo riguarda,
 * usando gli elementi già scaricati: nessuna chiamata in più a GitHub.
 * I conteggi sono quelli degli elementi scaricati (max 100 per sezione e account).
 */
export function buildAttention(items: DashboardItem[]): Map<string, Attention> {
  const urlsNeeding = new Map<string, Set<string>>();
  const urlsMine = new Map<string, Set<string>>();
  const result = new Map<string, Attention>();

  const entry = (key: string): Attention => {
    let found = result.get(key);
    if (!found) {
      found = { counts: {}, needs: 0, mine: 0 };
      result.set(key, found);
    }
    return found;
  };
  const add = (map: Map<string, Set<string>>, key: string, url: string) => {
    let set = map.get(key);
    if (!set) map.set(key, (set = new Set()));
    set.add(url);
  };

  for (const item of items) {
    const needs = NEEDS_ATTENTION.includes(item.section);
    if (!needs && !MINE_OPEN.includes(item.section)) continue;

    const key = repoKey(item.account, item.repo);
    const counts = entry(key).counts;
    counts[item.section] = (counts[item.section] ?? 0) + 1;
    add(needs ? urlsNeeding : urlsMine, key, item.url);
  }

  for (const [key, attention] of result) {
    const needing = urlsNeeding.get(key) ?? new Set<string>();
    attention.needs = needing.size;
    attention.mine = [...(urlsMine.get(key) ?? [])].filter((url) => !needing.has(url)).length;
  }
  return result;
}

/** Le righe di spiegazione («2 assegnate a te», «1 notifica non letta»…), nell'ordine di importanza. */
export function attentionLines(attention: Attention, t: I18n['t']): string[] {
  return [...NEEDS_ATTENTION, ...MINE_OPEN]
    .filter((section) => (attention.counts[section] ?? 0) > 0)
    .map((section) => t(`attention.${section}` as 'attention.review', { count: attention.counts[section] ?? 0 }));
}
