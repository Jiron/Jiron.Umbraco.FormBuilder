// A version's comment (ROADMAP 10.3a, IA 5.6, ADR-0196): Umbraco's dialog, as the publish dialog takes its comment.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { VERSION_COMMENT_MODAL_ALIAS } from '../constants';

export interface CommentModalData {
  /** The version, as a sentence names it: "version 14". */
  readonly name: string;
  /** Its comment now, empty for none. */
  readonly comment: string;
}

/** The comment to keep, empty to remove it. */
export interface CommentModalValue {
  readonly comment: string;
}

/** The most characters a comment holds, as the server keeps it (ADR-0045). */
export const MOST_COMMENT = 500;

export const COMMENT_MODAL = new UmbModalToken<CommentModalData, CommentModalValue>(VERSION_COMMENT_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
