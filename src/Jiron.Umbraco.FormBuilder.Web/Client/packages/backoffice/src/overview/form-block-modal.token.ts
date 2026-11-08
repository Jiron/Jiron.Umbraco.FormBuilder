// "Create a form block type..." (docs/ux/information-architecture.md, section 10, ADR-0130), as a token the manifest and
// the Overview name without loading the dialog's element, which loads when the dialog opens.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export const FORM_BLOCK_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.FormBlock';

/** The value says whether the types now exist, so the Overview reads the checklist again. */
export interface FormBlockModalValue {
  created: boolean;
}

export const FORM_BLOCK_MODAL = new UmbModalToken<never, FormBlockModalValue>(FORM_BLOCK_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
