// Settings › Access and a folder's or form's Permissions sidebar in English (ROADMAP 12.4a, IA 8, ADR-0013,
// ADR-0218, docs/ux/copy.md). The permissions keep their names from the library's words (`jfbLibrary_permission_*`).
// They load with the screen, the sidebar and the user group screen's Form Builder view (ADR-0262), not with the
// backoffice's start (ADR-0183).
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

const list = (names: readonly string[]) =>
  names.length <= 1 ? (names[0] ?? '') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;

export default {
  jfbAccess: {
    loading: 'Loading access…',
    failed: "Access couldn't be read.",
    retry: 'Try again',
    save: 'Save',
    // Settings > Access's own Save, named for what it saves (ADR-0257); the folder's dialog keeps the plain one.
    saveAccess: 'Save access',
    cancel: 'Cancel',
    saved: 'Access is saved.',
    saveFailed: "Access couldn't be saved. Try again in a moment.",
    invalid: "Access couldn't be saved as it is. Read it again and make your change once more.",
    conflict:
      'Someone else changed access meanwhile. Their grants are shown now; make your change again if it is still needed.',
    lockedNow: "The site's configuration has locked access meanwhile. It is shown as the configuration sets it.",

    everyForm: 'Every form',
    administrators: 'Administrators hold every permission everywhere, because they could grant themselves any.',
    noSection: (count: number) =>
      count === 1
        ? "A group can't open Form Builder yet. Add the section to its user group:"
        : `${count} groups can't open Form Builder yet. Add the section to their user groups:`,
    lockedGrants: (key: string) =>
      `The site's configuration sets what each group may do with every form (${key}), so it can't be changed here. Folders and forms can still have their own.`,
    lockedAll: (key: string) => `The site's configuration sets access (${key}), so it can't be changed here.`,
    matrixCaption: 'What each user group may do with every form',
    permission: 'Permission',
    part_forms: 'Forms',
    part_entries: 'Entries',
    part_library: 'Library, settings and privacy',
    cell: (permission: string, group: string) => `${permission}, ${group}`,
    alsoOn: (permission: string, besides: readonly string[]) =>
      `${list(besides)} ${besides.length === 1 ? 'is' : 'are'} on too: ${permission} needs ${besides.length === 1 ? 'it' : 'them'}.`,
    alsoOff: (permission: string, besides: readonly string[]) =>
      `${list(besides)} ${besides.length === 1 ? 'is' : 'are'} off too: ${besides.length === 1 ? 'it needs' : 'they need'} ${permission}.`,

    items: 'Folders and forms with their own access',
    addItem: 'Add folder or form…',
    itemsNone: 'None yet.',
    only: (names: readonly string[]) => `Only these groups: ${list(names)}`,
    onlyAdministrators: 'Only administrators',
    also: (names: readonly string[]) => `Also: ${list(names)}`,
    alsoNone: 'No group has more here',
    open: (name: string) => `Permissions for ${name}`,
    folder: 'Folder',
    form: 'Form',

    headline: (name: string) => `Permissions for ${name}`,
    restrict: 'Only these groups',
    restrictHelpFolder: 'Access given to every form or to a parent folder stops here. Administrators keep theirs.',
    restrictHelpForm: 'Access given to every form or to its folder stops here. Administrators keep theirs.',
    groupsHere: 'Groups with access here',
    addGroup: 'Add user group',
    addGroupNone: 'Choose a group',
    removeGroup: (group: string) => `Remove ${group}`,
    noneHereFolder: 'None yet. Access given to every form reaches this folder.',
    noneHereForm: 'None yet. Access given to every form reaches this form.',
    noneHereRestricted: 'None yet, so only administrators see what is here.',
    sidebarCaption: (name: string) => `What each user group may do in ${name}`,
    trashedFolder: 'This folder is in the Recycle Bin. Restore it to change its access.',
    trashedForm: 'This form is in the Recycle Bin. Restore it to change its access.',
    gone: 'This folder or form is no longer there.',
    restrictHeadline: (name: string) => `Restrict ${name}?`,
    restrictText: 'No user group has access of its own here yet, so only administrators will see its forms.',
    restrictFolder: 'Restrict folder',
    restrictForm: 'Restrict form',
    savedTarget: (name: string) => `The permissions of ${name} are saved.`,

    // The Form Builder view on Umbraco's user group screen, which shows and links and never saves (ADR-0262). Its tab
    // names Form Builder, so no headline says it again.
    groupNoSection: "This group can't open Form Builder yet. Add the section to it under Details first.",
    groupEveryForm: 'Permissions for every form',
    groupNoPermission: 'This group has no permission for forms yet, so its users see an empty section.',
    // A group without the section: its users see no section at all, so only that it has none.
    groupNoPermissionYet: 'No permission for forms yet.',
    groupNoneEveryForm: 'None for every form, only in the folders and forms below.',
    groupLockedGrants: (key: string) =>
      `The site's configuration sets what each group may do with every form (${key}).`,
    groupLockedAll: (key: string) => `The site's configuration sets access (${key}).`,
    groupItemNothing: 'No permission here',
    groupItemOwn: 'Only this group sees it',
    groupItemShared: (names: readonly string[]) => `Only this group and ${list(names)} see it`,
    groupOpen: 'Open Form Builder › Settings › Access',
  },
} satisfies UmbLocalizationDictionary;
