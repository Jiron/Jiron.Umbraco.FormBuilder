// The dialogs of a form's Entries view (ROADMAP 11.1, IA 6.1, ADR-0207): one part of the filter, the columns, the name of
// a filter set, and the keyboard shortcuts. Each is Umbraco's dialog or sidebar; the view loads their words first.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { EntryListFieldResponseModel, EntryListFilterRequestModel } from '../api';

export const ENTRY_FILTER_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.EntryFilter';
export const ENTRY_COLUMNS_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.EntryColumns';
export const ENTRY_SET_NAME_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.EntrySetName';
export const ENTRY_KEYS_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.EntryKeys';

/** The part of the filter a dialog changes. */
export type FilterPart = 'state' | 'received' | 'actions' | 'field';

export interface EntryFilterModalData {
  readonly part: FilterPart;
  /** The filter as it is; the dialog answers it with its part changed. */
  readonly filter: EntryListFilterRequestModel;
  /** The field of a field's filter. */
  readonly field?: EntryListFieldResponseModel;
  /** The place of the field's filter being changed; none for a new one. */
  readonly index?: number;
}

export interface EntryFilterModalValue {
  readonly filter: EntryListFilterRequestModel;
}

export const ENTRY_FILTER_MODAL = new UmbModalToken<EntryFilterModalData, EntryFilterModalValue>(
  ENTRY_FILTER_MODAL_ALIAS,
  { modal: { type: 'dialog', size: 'small' } },
);

export interface EntryColumnsModalData {
  readonly fields: readonly EntryListFieldResponseModel[];
  readonly columns: readonly string[];
}

export interface EntryColumnsModalValue {
  readonly columns: readonly string[];
}

export const ENTRY_COLUMNS_MODAL = new UmbModalToken<EntryColumnsModalData, EntryColumnsModalValue>(
  ENTRY_COLUMNS_MODAL_ALIAS,
  { modal: { type: 'sidebar', size: 'small' } },
);

export interface EntrySetNameModalData {
  /** Saves the set under a name: undefined when it was saved, otherwise what to say beside the name. */
  readonly save: (name: string) => Promise<string | undefined>;
}

export const ENTRY_SET_NAME_MODAL = new UmbModalToken<EntrySetNameModalData, never>(ENTRY_SET_NAME_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});

export const ENTRY_KEYS_MODAL = new UmbModalToken<never, never>(ENTRY_KEYS_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});

/** The most columns a list shows beside its details, as the server keeps them. */
export const MOST_COLUMNS = 20;

/** The longest name of a filter set. */
export const MOST_SET_NAME = 60;

export const ENTRY_FORWARD_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.EntryForward';
export const ENTRY_IMAGE_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.EntryImage';

export interface EntryForwardModalData {
  readonly form: string;
  readonly entry: string;
  readonly number: number;
  /** Whether the user may send sensitive values along. */
  readonly maySendSensitive: boolean;
}

export interface EntryForwardModalValue {
  /** Whom it went to, as the toast names them. */
  readonly to: string;
}

/** Forward... (IA 6.3): a sidebar, its primary Forward. */
export const ENTRY_FORWARD_MODAL = new UmbModalToken<EntryForwardModalData, EntryForwardModalValue>(
  ENTRY_FORWARD_MODAL_ALIAS,
  { modal: { type: 'sidebar', size: 'small' } },
);

export interface EntryImageModalData {
  readonly form: string;
  readonly file: string;
  readonly name: string;
  /** The line under the name: its field, type and size. */
  readonly meta: string;
}

/** An uploaded image, through the authorised download (IA 6.3, Files). */
export const ENTRY_IMAGE_MODAL = new UmbModalToken<EntryImageModalData, never>(ENTRY_IMAGE_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'medium' },
});
