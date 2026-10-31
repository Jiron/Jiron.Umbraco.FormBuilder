// The form picker's sidebar (ADR-0134): what the property gives it and what it answers. The token's alias is the
// modal's manifest alias, as Umbraco looks the element up by it.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { PickerItem } from './picker-data';

/** The modal's alias, which its manifest registers. */
export const FORM_PICKER_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.FormPicker';

/** What the property gives the sidebar. */
export interface FormPickerModalData {
  /** Whether several forms may be chosen; one otherwise. */
  multiple: boolean;
  /** The picker's data type, whose settings limit what the sidebar offers, or none. */
  dataTypeKey?: string;
  /** Whether those settings name allowed folders or forms, which changes what an empty library says. */
  limited: boolean;
}

/** What the sidebar answers: the chosen forms' keys in order, and those forms as it showed them. */
export interface FormPickerModalValue {
  selection: string[];
  items: PickerItem[];
}

/** Opens the form picker's sidebar. */
export const FORM_PICKER_MODAL = new UmbModalToken<FormPickerModalData, FormPickerModalValue>(FORM_PICKER_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
