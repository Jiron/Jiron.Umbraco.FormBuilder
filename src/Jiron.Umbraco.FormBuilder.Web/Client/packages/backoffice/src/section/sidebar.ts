// What the Forms section's sidebar holds and who sees it (docs/ux/information-architecture.md, ADR-0021): the groups in
// their order, each item with the permission that shows it. It holds no Umbraco import, so the tests read it as it is.

/** A permission as the management API names it (ADR-0013). */
export type Permission =
  | 'design'
  | 'manageActions'
  | 'viewEntries'
  | 'viewSensitive'
  | 'editEntries'
  | 'deleteEntries'
  | 'export'
  | 'manageLibrary'
  | 'manageSettings'
  | 'managePermissions'
  | 'privacyTools';

/** What shows an item: any of these permissions, held globally unless it is enough to hold it on one form. */
export interface Needs {
  anyOf: readonly Permission[];
  /** The permission counts on any folder or form, not only globally. */
  anywhere?: boolean;
}

export interface SidebarItem extends Needs {
  /** The item's name in aliases and its text, `jfbSection_<id>`. */
  id: string;
  /** The entity type of its screen, whose workspace opens at `workspace/<entityType>`. */
  entityType: string;
  icon: string;
}

export interface SidebarGroup {
  /** The group's name in aliases and its heading's text, `jfbSection_<id>`. */
  id: string;
  items: readonly SidebarItem[];
}

const SETTINGS: Needs = { anyOf: ['manageSettings'] };
const LIBRARY: Needs = { anyOf: ['manageLibrary'] };
const ENTRIES: Needs = { anyOf: ['viewEntries'], anywhere: true };

/** The groups below the forms tree, in their order. */
export const SIDEBAR: readonly SidebarGroup[] = [
  {
    id: 'entries',
    items: [
      { id: 'inbox', entityType: 'jfb-inbox', icon: 'icon-inbox', ...ENTRIES },
      { id: 'spam', entityType: 'jfb-spam', icon: 'icon-block', ...ENTRIES },
    ],
  },
  {
    id: 'library',
    items: [
      { id: 'optionLists', entityType: 'jfb-option-lists', icon: 'icon-bulleted-list', ...LIBRARY },
      { id: 'themes', entityType: 'jfb-themes', icon: 'icon-palette', ...LIBRARY },
      // A layout, not a message: Settings' Email item has the envelope, and no two items share an icon.
      { id: 'emailLayouts', entityType: 'jfb-email-layouts', icon: 'icon-layout', ...LIBRARY },
      { id: 'templates', entityType: 'jfb-templates', icon: 'icon-layers-alt', ...LIBRARY },
    ],
  },
  {
    id: 'settings',
    items: [
      { id: 'general', entityType: 'jfb-settings-general', icon: 'icon-settings', ...SETTINGS },
      { id: 'email', entityType: 'jfb-settings-email', icon: 'icon-message', ...SETTINGS },
      { id: 'spamProtection', entityType: 'jfb-settings-spam', icon: 'icon-shield', ...SETTINGS },
      { id: 'files', entityType: 'jfb-settings-files', icon: 'icon-attachment', ...SETTINGS },
      { id: 'access', entityType: 'jfb-settings-access', icon: 'icon-lock', anyOf: ['managePermissions'] },
      { id: 'integrations', entityType: 'jfb-settings-integrations', icon: 'icon-link', ...SETTINGS },
    ],
  },
  {
    id: 'privacy',
    items: [
      { id: 'people', entityType: 'jfb-privacy-people', icon: 'icon-users', anyOf: ['privacyTools'] },
      {
        id: 'retention',
        entityType: 'jfb-privacy-retention',
        icon: 'icon-timer',
        anyOf: ['privacyTools', 'manageSettings'],
      },
      { id: 'dataCollection', entityType: 'jfb-privacy-collection', icon: 'icon-fingerprint', ...SETTINGS },
    ],
  },
  {
    id: 'activity',
    items: [
      { id: 'deliveries', entityType: 'jfb-deliveries', icon: 'icon-paper-plane', ...SETTINGS },
      { id: 'auditLog', entityType: 'jfb-audit-log', icon: 'icon-list', ...SETTINGS },
    ],
  },
];

/** What a user holds, as `GET me` says. */
export interface MyPermissions {
  global: readonly string[];
  anywhere: readonly string[];
}

/** What shows a group's heading: any of its items, since a heading stands only above items the user can see. */
export function needsOfGroup(group: SidebarGroup): Needs[] {
  return group.items.map(({ anyOf, anywhere }) => (anywhere ? { anyOf, anywhere } : { anyOf }));
}

/** Whether a user meets one of these. */
export function meets(permissions: MyPermissions, needs: readonly Needs[]): boolean {
  return needs.some((need) =>
    need.anyOf.some((permission) => (need.anywhere ? permissions.anywhere : permissions.global).includes(permission)),
  );
}

/** The permissions that show forms in the tree, which all work on folders and forms. */
export const FORMS_NEEDS: readonly Needs[] = [
  {
    anyOf: ['design', 'manageActions', 'viewEntries', 'viewSensitive', 'editEntries', 'deleteEntries', 'export'],
    anywhere: true,
  },
];
