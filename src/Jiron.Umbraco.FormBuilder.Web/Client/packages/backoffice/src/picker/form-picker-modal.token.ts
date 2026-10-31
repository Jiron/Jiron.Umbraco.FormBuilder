// The form picker's sidebar (ADR-0134, ADR-0135): what the property or a setting gives it and what it answers. The
// token's alias is the modal's manifest alias, as Umbraco looks the element up by it.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { PickerItem } from './picker-data';

/** The modal's alias, which its manifest registers. */
export const FORM_PICKER_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.FormPicker';

/** What the property or a setting gives the sidebar. */
export interface FormPickerModalData {
  /** What is chosen: forms, as the property and the allowed forms choose, or folders, as the allowed folders do. */
  pick: 'forms' | 'folders';
  /** Whether several may be chosen; one otherwise. */
  multiple: boolean;
  /** The picker's data type, whose settings limit what the sidebar offers, or none. */
  dataTypeKey?: string;
  /** Whether those settings name allowed folders or forms, which changes what an empty library says. */
  limited: boolean;
}

/** What the sidebar answers: the chosen keys in order, and those forms or folders as it showed them. */
export interface FormPickerModalValue {
  selection: string[];
  items: PickerItem[];
}

/** Opens the form picker's sidebar. */
export const FORM_PICKER_MODAL = new UmbModalToken<FormPickerModalData, FormPickerModalValue>(FORM_PICKER_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
