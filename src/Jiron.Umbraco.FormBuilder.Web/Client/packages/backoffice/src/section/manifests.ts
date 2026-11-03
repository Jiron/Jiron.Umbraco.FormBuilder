// The Forms section (ADR-0021, ADR-0165): the section with its one dashboard, the sidebar of docs/ux/information-architecture.md
// and a workspace behind each item. The table in sidebar.ts holds the items; screens that later phases build replace
// their workspace's element here, and show coming-screen.element until then.
import { manifests as library } from '../library/manifests';
import { manifests as overview } from '../overview/manifests';
import { manifests as tree } from '../tree/manifests';
import {
  ENTRY_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  LIBRARY_ROOT_ENTITY_TYPE,
  SECTION_ALIAS,
  SECTION_PATHNAME,
} from './entity-types';
import { PERMISSION_CONDITION } from './my-permissions';
import { FORMS_NEEDS, needsOfGroup, SIDEBAR, type SidebarGroup, type SidebarItem } from './sidebar';

/** The menu of the forms tree. */
const FORMS_MENU_ALIAS = 'Jiron.FormBuilder.Menu.Forms';

const INSIDE_SECTION = { alias: 'Umb.Condition.SectionAlias', match: SECTION_ALIAS };

const pascal = (id: string): string => id.charAt(0).toUpperCase() + id.slice(1);

/** The weight that keeps things in the table's order: Umbraco puts the higher first. */
const weightOf = (index: number, count: number): number => (count - index) * 100;

const comingScreen = (id: string, entityType: string): UmbExtensionManifest => ({
  type: 'workspace',
  alias: `Jiron.FormBuilder.Workspace.${pascal(id)}`,
  name: `Jiron Form Builder ${id} screen`,
  element: () => import('./coming-screen.element'),
  meta: { entityType },
});

const itemManifests = (group: SidebarGroup, item: SidebarItem, index: number): UmbExtensionManifest[] => [
  {
    type: 'menuItem',
    alias: `Jiron.FormBuilder.MenuItem.${pascal(item.id)}`,
    name: `Jiron Form Builder ${item.id} menu item`,
    weight: weightOf(index, group.items.length),
    meta: {
      label: `#jfbSection_${item.id}`,
      icon: item.icon,
      entityType: item.entityType,
      menus: [`Jiron.FormBuilder.Menu.${pascal(group.id)}`],
    },
    conditions: [{ alias: PERMISSION_CONDITION, needs: [item] }],
  },
  comingScreen(item.id, item.entityType),
];

const groupManifests = (group: SidebarGroup, index: number): UmbExtensionManifest[] => [
  {
    type: 'sectionSidebarApp',
    kind: 'menu',
    alias: `Jiron.FormBuilder.SidebarApp.${pascal(group.id)}`,
    name: `Jiron Form Builder ${group.id} group`,
    weight: weightOf(index, SIDEBAR.length),
    meta: { label: `#jfbSection_${group.id}`, menu: `Jiron.FormBuilder.Menu.${pascal(group.id)}` },
    // A heading stands only above items the user can see (ADR-0021).
    conditions: [INSIDE_SECTION, { alias: PERMISSION_CONDITION, needs: needsOfGroup(group) }],
  },
  { type: 'menu', alias: `Jiron.FormBuilder.Menu.${pascal(group.id)}`, name: `Jiron Form Builder ${group.id} menu` },
  ...group.items.flatMap((item, itemIndex) => itemManifests(group, item, itemIndex)),
];

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'section',
    alias: SECTION_ALIAS,
    name: 'Jiron Form Builder section',
    weight: 50,
    meta: { label: '#jfbSection_forms', pathname: SECTION_PATHNAME },
    conditions: [{ alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS }],
  },
  {
    type: 'condition',
    alias: PERMISSION_CONDITION,
    name: 'Jiron Form Builder permission condition',
    api: () => import('./permission.condition'),
  },
  {
    // The sidebar header is Umbraco's own, with the root's "+" and "..." (ADR-0021); the tree sits under it.
    type: 'sectionSidebarApp',
    kind: 'menuWithEntityActions',
    alias: 'Jiron.FormBuilder.SidebarApp.Forms',
    name: 'Jiron Form Builder forms tree',
    weight: (SIDEBAR.length + 1) * 100,
    meta: { label: '#jfbSection_forms', menu: FORMS_MENU_ALIAS, entityType: LIBRARY_ROOT_ENTITY_TYPE },
    conditions: [INSIDE_SECTION, { alias: PERMISSION_CONDITION, needs: FORMS_NEEDS }],
  },
  {
    // In the tree's place for a user who may see no form: who can change that.
    type: 'sectionSidebarApp',
    alias: 'Jiron.FormBuilder.SidebarApp.NoForms',
    name: 'Jiron Form Builder without forms',
    weight: (SIDEBAR.length + 1) * 100,
    element: () => import('./no-forms.element'),
    conditions: [INSIDE_SECTION, { alias: PERMISSION_CONDITION, needs: FORMS_NEEDS, none: true }],
  },
  { type: 'menu', alias: FORMS_MENU_ALIAS, name: 'Jiron Form Builder forms menu' },
  ...SIDEBAR.flatMap(groupManifests),
  comingScreen('form', FORM_ENTITY_TYPE),
  comingScreen('entry', ENTRY_ENTITY_TYPE),
  ...tree(FORMS_MENU_ALIAS, SECTION_ALIAS),
  ...library,
  ...overview,
];
