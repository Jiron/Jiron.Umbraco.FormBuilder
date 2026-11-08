// Access's extensions (ROADMAP 12.4a, IA 8, ADR-0218): "Permissions..." in a folder's and a form's "...", for a user
// who may manage permissions, and the sidebar it opens. Settings > Access itself is the sidebar item's screen.
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE } from '../section/entity-types';
import { PERMISSION_CONDITION } from '../section/my-permissions';
import { PERMISSIONS_MODAL_ALIAS } from './permissions.modal.token';

// Umbraco's modal base types its optional members as "T | undefined", which exactOptionalPropertyTypes does not accept for a
// manifest's element (as for the library's sidebars, ADR-0134).
type Element = UmbExtensionManifest extends { element?: infer E } ? E : never;

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.Permissions',
    name: 'Jiron Form Builder Permissions',
    forEntityTypes: [FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE],
    weight: 650,
    api: (() => import('./permissions.action')) as never,
    meta: { icon: 'icon-lock', label: '#jfbLibrary_actionPermissions' },
    conditions: [{ alias: PERMISSION_CONDITION, needs: [{ anyOf: ['managePermissions'] }] }],
  },
  {
    type: 'modal',
    alias: PERMISSIONS_MODAL_ALIAS,
    name: 'Jiron Form Builder Permissions modal',
    element: (() => import('./permissions.modal.element')) as unknown as Element,
  },
];
