// What the Form Builder section's sidebar holds and who sees it (docs/ux/information-architecture.md, ADR-0257): the five
// places in their order, each with its tabs, and each tab with the permissions that show it. The sidebar, the tabs along
// a screen's top and the manifests are all drawn from this table. It holds no Umbraco import, so the tests read it as it
// is.

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

/** What shows a tab: any of these permissions, held globally unless it is enough to hold it on one form. */
export interface Needs {
  anyOf: readonly Permission[];
  /** The permission counts on any folder or form, not only globally. */
  anywhere?: boolean;
}

/** One screen of a place, a workspace at its own address. */
export interface Tab extends Needs {
  /** The tab's name in aliases and its text, `jfbSection_<id>`. */
  id: string;
  /** The entity type of its screen, whose workspace opens at `workspace/<entityType>`. */
  entityType: string;
  icon: string;
}

export interface Place {
  /** The place's name in aliases and its text, `jfbSection_<id>`. */
  id: string;
  icon: string;
  tabs: readonly Tab[];
  /** The entity types of the editors its tabs open, on which the place is the open one too. */
  editors?: readonly string[];
}

const SETTINGS: Needs = { anyOf: ['manageSettings'] };
const LIBRARY: Needs = { anyOf: ['manageLibrary'] };
const ENTRIES: Needs = { anyOf: ['viewEntries'], anywhere: true };

/** The places before the forms tree, in their order, each with its tabs in theirs. */
export const PLACES: readonly Place[] = [
  {
    id: 'inbox',
    icon: 'icon-inbox',
    tabs: [
      { id: 'inbox', entityType: 'jfb-inbox', icon: 'icon-inbox', ...ENTRIES },
      { id: 'spam', entityType: 'jfb-spam', icon: 'icon-block', ...ENTRIES },
    ],
  },
  {
    id: 'library',
    icon: 'icon-books',
    tabs: [
      { id: 'optionLists', entityType: 'jfb-option-lists', icon: 'icon-bulleted-list', ...LIBRARY },
      { id: 'themes', entityType: 'jfb-themes', icon: 'icon-palette', ...LIBRARY },
      // A layout, not a message: Settings' Email tab has the envelope, and no two tabs share an icon.
      { id: 'emailLayouts', entityType: 'jfb-email-layouts', icon: 'icon-layout', ...LIBRARY },
      { id: 'templates', entityType: 'jfb-templates', icon: 'icon-layers-alt', ...LIBRARY },
    ],
    // A template opens in a dialog on its tab, not in an editor of its own.
    editors: ['jfb-option-list', 'jfb-theme', 'jfb-email-layout'],
  },
  {
    id: 'settings',
    icon: 'icon-settings',
    tabs: [
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
    icon: 'icon-key',
    tabs: [
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
    icon: 'icon-activity',
    tabs: [
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

/** Whether a user meets one of these. */
export function meets(permissions: MyPermissions, needs: readonly Needs[]): boolean {
  return needs.some((need) =>
    need.anyOf.some((permission) => (need.anywhere ? permissions.anywhere : permissions.global).includes(permission)),
  );
}

/** What shows the places' list: any tab of any place. */
export const PLACES_NEEDS: readonly Needs[] = PLACES.flatMap((place) => place.tabs);

/** A place's tabs a user may open, in their order. */
export function tabsFor(place: Place, permissions: MyPermissions): Tab[] {
  return place.tabs.filter((tab) => meets(permissions, [tab]));
}

/** The places a user may open, each with the first of its tabs the user may open, where the place leads. */
export function placesFor(permissions: MyPermissions): { place: Place; first: Tab }[] {
  return PLACES.flatMap((place) => {
    const [first] = tabsFor(place, permissions);
    return first ? [{ place, first }] : [];
  });
}

/** The place a workspace belongs to, by its entity type: one of its tabs or an editor they open. */
export function placeOf(entityType: string): Place | undefined {
  return PLACES.find(
    (place) => place.tabs.some((tab) => tab.entityType === entityType) || place.editors?.includes(entityType),
  );
}

/** The permissions that show forms in the tree, which all work on folders and forms. */
export const FORMS_NEEDS: readonly Needs[] = [
  {
    anyOf: ['design', 'manageActions', 'viewEntries', 'viewSensitive', 'editEntries', 'deleteEntries', 'export'],
    anywhere: true,
  },
];
