// The sidebar of "Draft a form with AI" (docs/ux/information-architecture.md, section 4; ADR-0018).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export const DRAFT_MODAL_ALIAS = 'Jiron.FormBuilder.AI.Modal.Draft';

/** Where the form lands: a folder, or the top of the library with null. */
export interface DraftModalData {
  readonly folderKey: string | null;
}

/** The drafted form. */
export interface DraftModalValue {
  readonly formKey: string;
}

export const DRAFT_MODAL = new UmbModalToken<DraftModalData, DraftModalValue>(DRAFT_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'medium' },
});
