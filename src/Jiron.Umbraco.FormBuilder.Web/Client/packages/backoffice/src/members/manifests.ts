// The member editor's Form entries view (ROADMAP 14.3, IA 10, ADR-0231): after Details and Info, on a member that exists,
// for a user with the Form Builder section, whose permissions then say which forms' entries it lists.
import { UMB_MEMBER_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/member';
import { SECTION_ALIAS } from '../section/entity-types';

/** The view's alias. */
export const MEMBER_ENTRIES_VIEW_ALIAS = 'Jiron.FormBuilder.WorkspaceView.MemberEntries';

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'workspaceView',
    alias: MEMBER_ENTRIES_VIEW_ALIAS,
    name: 'Jiron Form Builder member entries view',
    element: () => import('./member-entries.view'),
    // Umbraco's Details weighs 1000 and Info 500.
    weight: 400,
    meta: { label: '#jfbSection_memberEntries', pathname: 'form-entries', icon: 'icon-inbox' },
    conditions: [
      { alias: 'Umb.Condition.WorkspaceAlias', match: UMB_MEMBER_WORKSPACE_ALIAS },
      { alias: 'Umb.Condition.WorkspaceEntityIsNew', match: false },
      { alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS },
    ],
  },
];
