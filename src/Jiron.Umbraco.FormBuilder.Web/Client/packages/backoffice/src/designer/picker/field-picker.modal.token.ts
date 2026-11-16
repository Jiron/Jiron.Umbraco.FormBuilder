// The field picker's token (ROADMAP 22.1, ADR-0256): Umbraco's small sidebar, opened from every Add field and insert
// point, which answers with what was picked; Close and Esc answer nothing.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FieldGroupResponseModel, FieldTypeResponseModel } from '../../api';
import type { SavedGroup } from '../saved-groups';

export const FIELD_PICKER_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.FieldPicker';

/** What the picker offers: the field types and their groups in the server's order, and the site's saved groups. */
export interface FieldPickerModalData {
  readonly types: readonly FieldTypeResponseModel[];
  readonly groups: readonly FieldGroupResponseModel[];
  readonly saved: readonly SavedGroup[];
  /** Called once the picker is shown. */
  readonly opened?: () => void;
  /**
   * Called once the picker is gone. Its sidebar answers before it slides out, and its dialog, as it closes, gives the
   * focus back to what opened it; a focus set after this stays.
   */
  readonly closed?: () => void;
}

/** What was picked: a field of a type, a group, a copy of a saved group, or Paste questions... for the same place. */
export type FieldPick =
  | { readonly kind: 'field'; readonly type: string }
  | { readonly kind: 'group' }
  | { readonly kind: 'savedGroup'; readonly saved: SavedGroup }
  | { readonly kind: 'paste' };

export interface FieldPickerModalValue {
  readonly pick: FieldPick;
}

export const FIELD_PICKER_MODAL = new UmbModalToken<FieldPickerModalData, FieldPickerModalValue>(
  FIELD_PICKER_MODAL_ALIAS,
  { modal: { type: 'sidebar', size: 'small' } },
);
