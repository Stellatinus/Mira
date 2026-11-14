import type { DashboardItem, PrReview, ReviewInfo, SeenEntry } from './types';

export type ReviewBadge = 'approved' | 'changes' | 'required';

/** L'esito complessivo che GitHub dà a una PR; manca se il repository non chiede review. */
export function reviewBadge(review: ReviewInfo | null | undefined): ReviewBadge | null {
  switch (review?.decision) {
    case 'APPROVED':
      return 'approved';
    case 'CHANGES_REQUESTED':
      return 'changes';
    case 'REVIEW_REQUIRED':
      return 'required';
    default:
      return null;
  }
}

/** L'ultima review di ciascuno (le più recenti per prime), senza i commenti nudi di chi non ha deciso nulla due volte. */
export function latestPerReviewer(reviews: PrReview[]): PrReview[] {
  const byLogin = new Map<string, PrReview>();
  const time = (r: PrReview) => (r.at ? Date.parse(r.at) : 0);
  for (const review of reviews) {
    const key = (review.login ?? '').toLowerCase();
    const previous = byLogin.get(key);
    if (!previous || time(review) >= time(previous)) byLogin.set(key, review);
  }
  return [...byLogin.values()].sort((a, b) => time(b) - time(a));
}

/**
 * Quanti commenti sono arrivati dopo l'ultima volta che l'utente ha visto la PR (cioè dopo il suo «visto»), tra gli
 * ultimi che Mira conosce. Senza un «visto» non c'è un prima da cui contare: restituisce null.
 */
export function newCommentsSince(item: Pick<DashboardItem, 'review'>, seen: SeenEntry | undefined): number | null {
  if (!seen || !item.review) return null;
  const since = Date.parse(seen.updatedAt);
  return item.review.comments.filter((c) => Date.parse(c.at) > since).length;
}
