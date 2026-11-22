// Access's extensions (ROADMAP 12.4a, IA 8, ADR-0218): "Permissions..." in a folder's and a form's "...", for a user
// who may manage permissions, and the sidebar it opens. Settings > Access itself is a tab's screen. And the Form
// Builder view on Umbraco's user group screen (ROADMAP 25.1, IA 10, ADR-0262), after Details, on a group that exists,
// for a user with the section who may manage permissions: it shows what the group may do and links to Settings > Access.
import { UMB_USER_GROUP_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/user-group';
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE, SECTION_ALIAS } from '../section/entity-types';
import { PERMISSION_CONDITION } from '../section/my-permissions';
import { PERMISSIONS_MODAL_ALIAS } from './permissions.modal.token';

/** The user group screen's Form Builder view. */
export const USER_GROUP_ACCESS_VIEW_ALIAS = 'Jiron.FormBuilder.WorkspaceView.UserGroupAccess';

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
  {
    type: 'workspaceView',
    alias: USER_GROUP_ACCESS_VIEW_ALIAS,
    name: 'Jiron Form Builder user group access view',
    element: () => import('./user-group-access.view'),
    // Umbraco's Details weighs 90; this view comes after it.
    weight: 80,
    meta: { label: '#jfbSection_section', pathname: 'form-builder', icon: 'icon-jfb' },
    conditions: [
      { alias: 'Umb.Condition.WorkspaceAlias', match: UMB_USER_GROUP_WORKSPACE_ALIAS },
      { alias: 'Umb.Condition.WorkspaceEntityIsNew', match: false },
      { alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS },
      { alias: PERMISSION_CONDITION, needs: [{ anyOf: ['managePermissions'] }] },
    ],
  },
];
