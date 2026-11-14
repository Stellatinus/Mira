import type { FollowList } from './types';

/**
 * Se si seguono le release di un repository: i preferiti si seguono di base, più quelli aggiunti a mano, meno quelli
 * tolti. I nomi si confrontano in minuscolo («proprietario/nome»).
 */
export function isFollowed(repo: string, starred: boolean, follow: FollowList): boolean {
  const key = repo.toLowerCase();
  if (follow.muted.includes(key)) return false;
  return starred || follow.added.includes(key);
}
