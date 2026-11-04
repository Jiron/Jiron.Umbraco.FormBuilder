// The form check's token (ROADMAP 9.5f, IA 5.1, ADR-0181): a sidebar from the toolbar's badge that lists what the check
// found, errors first, each with Fix it.
import type { Observable } from '@umbraco-cms/backoffice/external/rxjs';
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FieldTypeResponseModel } from '../../api';
import type { FixTarget } from './form-check';
import type { CheckState } from './form-checker';

export const FORM_CHECK_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.FormCheck';

export interface FormCheckModalData {
  /** What the check shows, which the sidebar follows while it is open. */
  readonly check: Observable<CheckState>;
  /** Checks again, after a check that gave no answer. */
  readonly retry: () => void;
  readonly types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The language the designer shows, whose texts name the elements. */
  readonly culture: string;
  /** Called once the sidebar is shown. */
  readonly opened?: () => void;
  /**
   * Called once the sidebar is gone. It answers before it slides out, and its dialog, as it closes, gives the focus back
   * to the badge; a focus set after this stays.
   */
  readonly closed?: () => void;
}

/** Where Fix it leads. */
export interface FormCheckModalValue {
  readonly fix: FixTarget;
}

export const FORM_CHECK_MODAL = new UmbModalToken<FormCheckModalData, FormCheckModalValue>(FORM_CHECK_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
