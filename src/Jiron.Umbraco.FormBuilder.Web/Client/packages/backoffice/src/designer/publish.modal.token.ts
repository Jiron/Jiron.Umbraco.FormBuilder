// The publish dialog's token (ROADMAP 9.3a, 9.5f).
import type { Observable } from '@umbraco-cms/backoffice/external/rxjs';
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FieldTypeResponseModel } from '../api';
import type { FixTarget } from './check/form-check';
import type { CheckState } from './check/form-checker';

export const PUBLISH_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Publish';

/** The form to publish, by the name its header shows, with what the form check finds in its draft (ADR-0181). */
export interface PublishModalData {
  name: string;
  /** What the check shows, which the dialog follows while it is open; the action asks for a check as it opens it. */
  check: Observable<CheckState>;
  types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The language whose texts name the elements: the site's default. */
  culture: string;
  /**
   * Called once the dialog is gone. It answers before it closes, and as it closes it gives the focus back to Publish...;
   * a Fix it made after this keeps its focus.
   */
  closed?: () => void;
}

/** The comment for the version, empty for none; or the problem whose Fix it was chosen instead. */
export type PublishModalValue = { readonly comment: string } | { readonly fix: FixTarget };

export const PUBLISH_MODAL = new UmbModalToken<PublishModalData, PublishModalValue>(PUBLISH_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
