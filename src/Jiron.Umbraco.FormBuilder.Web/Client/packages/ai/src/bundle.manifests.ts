import type {
  ManifestJfbDesignerAction,
  ManifestJfbFieldTextAction,
} from '../../backoffice/public/types/jiron-form-builder';
import { DRAFT_MODAL_ALIAS } from './draft/draft.modal.token';
import {
  DESIGN_ANYWHERE,
  FOLDER,
  FORM_WORKSPACE,
  LIBRARY_ROOT,
  MANAGE_SETTINGS,
  SETTINGS_ENTITY_TYPE,
  SETTINGS_MENU,
} from './names';
import { TRANSLATE_MODAL_ALIAS } from './translate/translate.modal.token';

// Umbraco's modal base types its optional members as "T | undefined", which exactOptionalPropertyTypes does not accept
// for a manifest's element (as in the core, ADR-0134).
type Element = UmbExtensionManifest extends { element?: infer E } ? E : never;
const element = (loader: () => Promise<unknown>) => loader as unknown as Element;

const translate: ManifestJfbDesignerAction = {
  type: 'jfbDesignerAction',
  alias: 'Jiron.FormBuilder.AI.DesignerAction.Translate',
  name: 'Jiron Form Builder AI translate',
  api: (() => import('./translate/translate.action')) as never,
  meta: { label: '#jfbAI_translateAction' },
};

const suggest: ManifestJfbFieldTextAction = {
  type: 'jfbFieldTextAction',
  alias: 'Jiron.FormBuilder.AI.FieldTextAction.Suggest',
  name: 'Jiron Form Builder AI suggest',
  element: (() => import('./suggest/suggest.element')) as never,
  meta: { texts: ['help'] },
};

/**
 * The AI add-on's extensions (ADR-0018), registered through the one bundle its umbraco-package.json names:
 * "Draft a form with AI" in the library's create menu, the "Drafted by AI" mark in the form workspace's footer,
 * "Translate with AI" in the designer toolbar's "...", "Suggest help text" beside a field's help text, Settings > AI,
 * and its words in English and German. Umbraco's backoffice provides Lit and its own modules at run time.
 */
export const manifests: UmbExtensionManifest[] = [
  {
    type: 'localization',
    alias: 'Jiron.FormBuilder.AI.Localization.En',
    name: 'Jiron Form Builder AI English',
    meta: { culture: 'en' },
    js: () => import('./lang/en'),
  },
  {
    type: 'localization',
    alias: 'Jiron.FormBuilder.AI.Localization.De',
    name: 'Jiron Form Builder AI German',
    meta: { culture: 'de' },
    js: () => import('./lang/de'),
  },
  {
    // Last in the create menu, as the information architecture lists it (section 3).
    type: 'entityCreateOptionAction',
    alias: 'Jiron.FormBuilder.AI.EntityCreateOptionAction.Draft',
    name: 'Jiron Form Builder AI draft a form',
    forEntityTypes: [LIBRARY_ROOT, FOLDER],
    weight: 50,
    api: (() => import('./draft/draft.option')) as never,
    meta: { icon: 'icon-wand', label: '#jfbAI_draftOption', description: '#jfbAI_draftOptionDescription' },
    conditions: [DESIGN_ANYWHERE],
  },
  {
    type: 'modal',
    alias: DRAFT_MODAL_ALIAS,
    name: 'Jiron Form Builder AI draft modal',
    element: element(() => import('./draft/draft.modal.element')),
  },
  {
    type: 'workspaceFooterApp',
    alias: 'Jiron.FormBuilder.AI.WorkspaceFooterApp.Drafted',
    name: 'Jiron Form Builder AI drafted mark',
    element: (() => import('./mark/drafted-mark.element')) as never,
    conditions: [{ alias: 'Umb.Condition.WorkspaceAlias', match: FORM_WORKSPACE }],
  },
  translate as unknown as UmbExtensionManifest,
  {
    type: 'modal',
    alias: TRANSLATE_MODAL_ALIAS,
    name: 'Jiron Form Builder AI translate modal',
    element: element(() => import('./translate/translate.modal.element')),
  },
  suggest as unknown as UmbExtensionManifest,
  {
    // After the core's Settings items, the last of which weighs 100.
    type: 'menuItem',
    alias: 'Jiron.FormBuilder.AI.MenuItem.Settings',
    name: 'Jiron Form Builder AI settings menu item',
    weight: 50,
    meta: { label: '#jfbAI_settingsMenu', icon: 'icon-wand', entityType: SETTINGS_ENTITY_TYPE, menus: [SETTINGS_MENU] },
    conditions: [MANAGE_SETTINGS],
  },
  {
    type: 'workspace',
    alias: 'Jiron.FormBuilder.AI.Workspace.Settings',
    name: 'Jiron Form Builder AI settings screen',
    element: element(() => import('./settings/ai-settings.screen')),
    meta: { entityType: SETTINGS_ENTITY_TYPE },
  },
];
