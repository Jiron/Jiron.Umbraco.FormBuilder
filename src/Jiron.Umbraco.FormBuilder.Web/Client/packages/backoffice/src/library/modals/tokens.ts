// The form library's dialogs and sidebars (docs/ux/information-architecture.md, sections 3 and 4; ADR-0166), as tokens the actions
// open without loading the elements.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FormResponseModel } from '../../api';

export const FOLDER_NAME_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.FolderName';
export const DUPLICATE_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Duplicate';
export const RESTORE_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Restore';
export const COPY_ACTIONS_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.CopyActions';
export const TEMPLATE_GALLERY_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.TemplateGallery';
export const IMPORT_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Import';
export const TEMPLATE_NAME_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.TemplateName';

/** A template's name and description (ADR-0217): a form or a group saved as one, or a saved one renamed. */
export interface TemplateNameModalData {
  headline: string;
  confirmLabel: string;
  name?: string;
  description?: string | null;
  /** One line on what the template leaves out, under the fields. */
  note?: string;
}

export interface TemplateNameModalValue {
  name: string;
  description: string | null;
}

export const TEMPLATE_NAME_MODAL = new UmbModalToken<TemplateNameModalData, TemplateNameModalValue>(
  TEMPLATE_NAME_MODAL_ALIAS,
  { modal: { type: 'dialog', size: 'small' } },
);

/** A folder's name: a new folder's, or a folder's new one. */
export interface FolderNameModalData {
  headline: string;
  confirmLabel: string;
  name?: string;
}

export interface FolderNameModalValue {
  name: string;
}

export const FOLDER_NAME_MODAL = new UmbModalToken<FolderNameModalData, FolderNameModalValue>(FOLDER_NAME_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});

/** A copy of a form: its name, and whether it takes the actions. */
export interface DuplicateModalData {
  form: FormResponseModel;
  /** The name of the folder the copy lands in; null at the top of Forms, or for a folder that did not read. */
  folderName: string | null;
  /** Whether the copy lands in a folder, not at the top of Forms. */
  inFolder: boolean;
  /** Whether the user may manage actions where the copy lands, so that it may take the form's. */
  mayTakeActions: boolean;
}

export interface DuplicateModalValue {
  name: string;
  includeActions: boolean;
}

export const DUPLICATE_MODAL = new UmbModalToken<DuplicateModalData, DuplicateModalValue>(DUPLICATE_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});

/** A form or folder to take out of the Recycle Bin. */
export interface RestoreModalData {
  kind: 'form' | 'folder';
  key: string;
  name: string;
}

export interface RestoreModalValue {
  restore: true;
}

export const RESTORE_MODAL = new UmbModalToken<RestoreModalData, RestoreModalValue>(RESTORE_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});

/** The form whose actions are copied to another. */
export interface CopyActionsModalData {
  sourceKey: string;
  sourceName: string;
}

export interface CopyActionsModalValue {
  targetKey: string;
}

export const COPY_ACTIONS_MODAL = new UmbModalToken<CopyActionsModalData, CopyActionsModalValue>(
  COPY_ACTIONS_MODAL_ALIAS,
  {
    modal: { type: 'sidebar', size: 'medium' },
  },
);

/** Where a new form lands: a folder, or the top of Forms. */
export interface NewFormModalData {
  folderKey: string | null;
}

/** The new form. */
export interface NewFormModalValue {
  formKey: string;
}

export const TEMPLATE_GALLERY_MODAL = new UmbModalToken<NewFormModalData, NewFormModalValue>(
  TEMPLATE_GALLERY_MODAL_ALIAS,
  {
    modal: { type: 'sidebar', size: 'large' },
  },
);

/** The form whose draft the import sidebar replaces (ADR-0220), as the action read it. */
export interface ReplaceTarget {
  formKey: string;
  name: string;
  concurrencyVersion: number;
  /** Whether the form's own workspace holds changes not saved yet, which the replace discards. */
  unsaved: boolean;
}

/** Where a new form lands; or, to replace a form's draft with the file, which form. */
export interface ImportModalData extends NewFormModalData {
  replace?: ReplaceTarget;
}

export const IMPORT_MODAL = new UmbModalToken<ImportModalData, NewFormModalValue>(IMPORT_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'medium' },
});
