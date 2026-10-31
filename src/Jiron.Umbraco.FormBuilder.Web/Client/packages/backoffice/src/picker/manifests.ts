// The form picker's extensions (ADR-0134): the schema named after the server's property editor
// (Picker/FormPickerPropertyEditor.cs) with its default UI, the sidebar it opens, and the block label's component.
import type { ManifestModal } from '@umbraco-cms/backoffice/modal';
import { FORM_PICKER_MODAL_ALIAS } from './form-picker-modal.token';

// Umbraco's modal base types its optional members as "T | undefined", which this project's exactOptionalPropertyTypes
// does not accept for a modal manifest's element; Umbraco loads the class the same either way, so the loader is typed
// as the manifest expects.
const sidebar = (() => import('./form-picker-modal.element')) as unknown as NonNullable<ManifestModal['element']>;

/** The server's property editor alias, which the schema takes. */
export const FORM_PICKER_SCHEMA_ALIAS = 'Jiron.FormBuilder.FormPicker';

/** The property editor UI, the schema's default. */
export const FORM_PICKER_UI_ALIAS = 'Jiron.FormBuilder.PropertyEditorUi.FormPicker';

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'propertyEditorSchema',
    alias: FORM_PICKER_SCHEMA_ALIAS,
    name: 'Jiron Form Builder form picker',
    meta: { defaultPropertyEditorUiAlias: FORM_PICKER_UI_ALIAS },
  },
  {
    type: 'propertyEditorUi',
    alias: FORM_PICKER_UI_ALIAS,
    name: 'Jiron Form Builder form picker',
    element: () => import('./form-picker.element'),
    meta: {
      label: 'Jiron Form Builder form picker',
      icon: 'icon-document',
      group: 'pickers',
      propertyEditorSchemaAlias: FORM_PICKER_SCHEMA_ALIAS,
      supportsReadOnly: true,
    },
  },
  {
    type: 'modal',
    alias: FORM_PICKER_MODAL_ALIAS,
    name: 'Jiron Form Builder form picker sidebar',
    element: sidebar,
  },
  {
    type: 'ufmComponent',
    alias: 'Jiron.FormBuilder.Ufm.FormName',
    name: 'Jiron Form Builder form name',
    api: () => import('./form-name.ufm'),
    meta: { alias: 'jfbFormName' },
  },
];
