import type { I18n } from './i18n';
import type { DashboardItem } from './types';

/** Etichetta estesa del tipo di elemento, per il pannello dettaglio. */
export function typeLabel(item: Pick<DashboardItem, 'type' | 'draft' | 'private'>, { t }: Pick<I18n, 't'>): string {
  switch (item.type) {
    case 'pr':
      return item.draft ? t('type.prDraft') : t('type.pr');
    case 'issue':
      return t('type.issue');
    case 'repo':
      return item.private ? t('type.repoPrivate') : t('type.repoPublic');
    case 'release':
      return t('type.release');
    case 'commit':
      return t('type.commit');
    case 'discussion':
      return t('type.discussion');
    default:
      return t('type.notification');
  }
}

/** Etichetta breve del tipo, per l'angolo delle card. */
export function tagLabel(item: Pick<DashboardItem, 'type' | 'draft'>, { t }: Pick<I18n, 't'>): string {
  switch (item.type) {
    case 'pr':
      return item.draft ? t('tag.prDraft') : t('tag.pr');
    case 'issue':
      return t('tag.issue');
    case 'release':
      return t('tag.release');
    case 'commit':
      return t('tag.commit');
    case 'discussion':
      return t('tag.discussion');
    default:
      return t('tag.notification');
  }
}

/** Il motivo di una notifica come lo dà GitHub (es. "review_requested"), tradotto se lo conosciamo. */
export function reasonLabel(reason: string, { t, has }: Pick<I18n, 't' | 'has'>): string {
  const key = `reason.${reason}`;
  return has(key) ? t(key as 'reason.mention') : reason.replaceAll('_', ' ');
}
