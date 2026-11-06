// The publish dialog's token (ROADMAP 9.3a, 9.5f, 10.3d).
import type { Observable } from '@umbraco-cms/backoffice/external/rxjs';
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FieldTypeResponseModel } from '../api';
import type { FixTarget } from './check/form-check';
import type { CheckState } from './check/form-checker';
import type { PublishChanges } from './history/publish-changes';

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
   * What changed since the version visitors get, said once it is read (ROADMAP 10.3d), with what the dialog says
   * meanwhile; absent where History's words can't be loaded, and the dialog then says nothing of it.
   */
  changes?: { readonly said: Promise<PublishChanges>; readonly comparing: string };
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
