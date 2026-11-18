// The Form Builder section (ADR-0021, ADR-0165, ADR-0171, ADR-0257): the section with its one dashboard, the sidebar of
// docs/ux/information-architecture.md (the five places, then the tree of forms) and a workspace behind each tab. The
// table in sidebar.ts holds the places and their tabs, and each tab has its screen here: a tab without one fails at once
// (ADR-0165).
import { manifests as access } from '../access/manifests';
import { manifests as activity } from '../activity/manifests';
import { manifests as designer } from '../designer/manifests';
import { manifests as entries } from '../entries/manifests';
import { manifests as library } from '../library/manifests';
import { manifests as optionLists } from '../option-lists/manifests';
import { manifests as themes } from '../themes/manifests';
import { manifests as emailLayouts } from '../email-layouts/manifests';
import { manifests as overview } from '../overview/manifests';
import { manifests as privacy } from '../privacy/manifests';
import { manifests as tree } from '../tree/manifests';
import { ENTRY_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, SECTION_ALIAS, SECTION_PATHNAME } from './entity-types';
import { PERMISSION_CONDITION } from './my-permissions';
import { SETTINGS_MENU_ALIAS } from './added-tabs';
import { FORMS_NEEDS, PLACES, PLACES_NEEDS } from './sidebar';

/** The menu of the forms tree. */
const FORMS_MENU_ALIAS = 'Jiron.FormBuilder.Menu.Forms';

const INSIDE_SECTION = { alias: 'Umb.Condition.SectionAlias', match: SECTION_ALIAS };

const pascal = (id: string): string => id.charAt(0).toUpperCase() + id.slice(1);

/** The screen of each tab, and of an entry opened on its own. */
type ScreenLoader = NonNullable<Extract<UmbExtensionManifest, { type: 'workspace' }>['element']>;

const SCREENS: Readonly<Record<string, ScreenLoader>> = {
  inbox: () => import('../entries/inbox.screen'),
  spam: () => import('../entries/spam.screen'),
  general: () => import('../settings/general.screen'),
  email: () => import('../settings/email.screen'),
  spamProtection: () => import('../settings/protection.screen'),
  files: () => import('../settings/files.screen'),
  access: () => import('../access/access.screen'),
  integrations: () => import('../settings/integrations.screen'),
  people: () => import('../privacy/people.screen'),
  retention: () => import('../privacy/retention.screen'),
  dataCollection: () => import('../settings/privacy.screen'),
  deliveries: () => import('../activity/deliveries.screen'),
  auditLog: () => import('../activity/audit.screen'),
  optionLists: () => import('../option-lists/option-lists.screen'),
  themes: () => import('../themes/themes.screen'),
  emailLayouts: () => import('../email-layouts/email-layouts.screen'),
  templates: () => import('../templates/templates.screen'),
  entry: () => import('../entries/entry.workspace'),
};

/** The screen of a tab; one without is a mistake of this table, found as the section loads. */
function screenOf(id: string): ScreenLoader {
  const screen = SCREENS[id];
  if (!screen) throw new Error(`The tab ${id} has no screen.`);
  return screen;
}

const workspaceOf = (id: string, entityType: string): UmbExtensionManifest => ({
  type: 'workspace',
  alias: `Jiron.FormBuilder.Workspace.${pascal(id)}`,
  name: `Jiron Form Builder ${id} screen`,
  element: screenOf(id),
  meta: { entityType },
});

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'section',
    alias: SECTION_ALIAS,
    name: 'Jiron Form Builder section',
    weight: 50,
    meta: { label: '#jfbSection_section', pathname: SECTION_PATHNAME },
    conditions: [{ alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS }],
  },
  {
    type: 'condition',
    alias: PERMISSION_CONDITION,
    name: 'Jiron Form Builder permission condition',
    api: () => import('./permission.condition'),
  },
  {
    // The places come first, before the tree: what grows comes after what is fixed (ADR-0254, ADR-0257).
    type: 'sectionSidebarApp',
    alias: 'Jiron.FormBuilder.SidebarApp.Places',
    name: 'Jiron Form Builder places',
    weight: 300,
    element: () => import('./places.element'),
    conditions: [INSIDE_SECTION, { alias: PERMISSION_CONDITION, needs: PLACES_NEEDS }],
  },
  {
    // The sidebar header is Umbraco's own, with the root's "+" and "..." (ADR-0021); the tree sits under it.
    type: 'sectionSidebarApp',
    kind: 'menuWithEntityActions',
    alias: 'Jiron.FormBuilder.SidebarApp.Forms',
    name: 'Jiron Form Builder forms tree',
    weight: 200,
    meta: { label: '#jfbSection_forms', menu: FORMS_MENU_ALIAS, entityType: LIBRARY_ROOT_ENTITY_TYPE },
    conditions: [INSIDE_SECTION, { alias: PERMISSION_CONDITION, needs: FORMS_NEEDS }],
  },
  {
    // In the tree's place for a user who may see no form: who can change that.
    type: 'sectionSidebarApp',
    alias: 'Jiron.FormBuilder.SidebarApp.NoForms',
    name: 'Jiron Form Builder without forms',
    weight: 200,
    element: () => import('./no-forms.element'),
    conditions: [INSIDE_SECTION, { alias: PERMISSION_CONDITION, needs: FORMS_NEEDS, none: true }],
  },
  { type: 'menu', alias: FORMS_MENU_ALIAS, name: 'Jiron Form Builder forms menu' },
  // The menu a site's or an add-on's Settings tab names (added-tabs.ts), kept so the public name stays good.
  { type: 'menu', alias: SETTINGS_MENU_ALIAS, name: 'Jiron Form Builder settings menu' },
  ...PLACES.flatMap((place) => place.tabs.map((tab) => workspaceOf(tab.id, tab.entityType))),
  workspaceOf('entry', ENTRY_ENTITY_TYPE),
  ...tree(FORMS_MENU_ALIAS, SECTION_ALIAS),
  ...library,
  ...designer,
  ...entries,
  ...optionLists,
  ...themes,
  ...emailLayouts,
  ...overview,
  ...access,
  ...activity,
  ...privacy,
];
