// The form picker's extensions (ADR-0134, ADR-0135): the schema named after the server's property editor
// (Picker/FormPickerPropertyEditor.cs) with its default UI and its settings, the two UIs only the settings use, the
// sidebar the picker and its settings open, and the block label's component.
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

/** The UI of the allowed folders and forms, set to one or the other by its `pick` setting. */
export const FORM_PICKER_ALLOWED_UI_ALIAS = 'Jiron.FormBuilder.PropertyEditorUi.FormPickerAllowed';

/** The UI of the theme. */
export const FORM_PICKER_THEME_UI_ALIAS = 'Jiron.FormBuilder.PropertyEditorUi.FormPickerTheme';

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'propertyEditorSchema',
    alias: FORM_PICKER_SCHEMA_ALIAS,
    name: 'Jiron Form Builder form picker',
    meta: {
      defaultPropertyEditorUiAlias: FORM_PICKER_UI_ALIAS,
      // The server's settings (Picker/FormPickerConfiguration.cs), in its order, for every UI of the schema.
      settings: {
        properties: [
          {
            alias: 'multiple',
            label: '#jfbFormPicker_settingMultiple',
            propertyEditorUiAlias: 'Umb.PropertyEditorUi.Toggle',
            config: [{ alias: 'ariaLabel', value: '#jfbFormPicker_settingMultiple' }],
          },
          {
            alias: 'allowedFolders',
            label: '#jfbFormPicker_settingAllowedFolders',
            description: '{#jfbFormPicker_settingAllowedFoldersHelp}',
            propertyEditorUiAlias: FORM_PICKER_ALLOWED_UI_ALIAS,
            config: [{ alias: 'pick', value: 'folders' }],
          },
          {
            alias: 'allowedForms',
            label: '#jfbFormPicker_settingAllowedForms',
            description: '{#jfbFormPicker_settingAllowedFormsHelp}',
            propertyEditorUiAlias: FORM_PICKER_ALLOWED_UI_ALIAS,
            config: [{ alias: 'pick', value: 'forms' }],
          },
          {
            alias: 'theme',
            label: '#jfbFormPicker_settingTheme',
            description: '{#jfbFormPicker_settingThemeHelp}',
            propertyEditorUiAlias: FORM_PICKER_THEME_UI_ALIAS,
          },
          {
            alias: 'redirect',
            label: '#jfbFormPicker_settingRedirect',
            description: '{#jfbFormPicker_settingRedirectHelp}',
            propertyEditorUiAlias: 'Umb.PropertyEditorUi.DocumentPicker',
            config: [{ alias: 'validationLimit', value: { min: 0, max: 1 } }],
          },
        ],
        defaultData: [{ alias: 'multiple', value: false }],
      },
    },
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
  // The settings' own UIs name no schema, so Umbraco offers neither as a data type's editor.
  {
    type: 'propertyEditorUi',
    alias: FORM_PICKER_ALLOWED_UI_ALIAS,
    name: 'Jiron Form Builder form picker allowed folders or forms',
    element: () => import('./form-picker-allowed.element'),
    meta: { label: 'Jiron Form Builder allowed folders or forms', icon: 'icon-folder', group: 'pickers' },
  },
  {
    type: 'propertyEditorUi',
    alias: FORM_PICKER_THEME_UI_ALIAS,
    name: 'Jiron Form Builder form picker theme',
    element: () => import('./form-picker-theme.element'),
    meta: { label: 'Jiron Form Builder theme', icon: 'icon-palette', group: 'pickers' },
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
