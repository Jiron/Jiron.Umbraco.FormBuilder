// The form library in English (docs/ux/copy.md; ADR-0166): the trees' marks, the screens of a folder and the Recycle Bin, the
// create options, each item's "..." and the dialogs and sidebars they open. Numbers are written as English writes them.
const number = (value: number) => new Intl.NumberFormat('en').format(value);
const count = (value: number, one: string, many: string) => `${number(value)} ${value === 1 ? one : many}`;

/** A day from a list ("Today", "3 Nov") inside a sentence: "today", or the date after the word it needs. */
const onDay = (day: string, before: string) =>
  day === 'Today' || day === 'Yesterday' ? day.toLowerCase() : before ? `${before} ${day}` : day;

/** "It's picked on 3 pages: Home, Contact, Events." with "and 2 more" past the names a confirmation lists. */
const pickedOn = (pages: number, names: string, more: number) => {
  const where = count(pages, 'page', 'pages');
  if (!names) return `It's picked on ${where}.`;
  return more > 0
    ? `It's picked on ${where}: ${names} and ${number(more)} more.`
    : `It's picked on ${where}: ${names}.`;
};

export default {
  jfbLibrary: {
    recycleBin: 'Recycle Bin',
    topOfForms: 'the top level of Forms',
    sameFolder: 'the same folder',
    untitledForm: 'Untitled form',
    notPublished: 'not published',
    restricted: 'Restricted',
    unseenEntries: 'New entries',
    inPlace: (name: string, place: string) => `${name}, in ${place}`,
    moreActionsFor: (name: string) => `More actions for ${name}`,
    loading: 'Loading',
    tryAgain: 'Try again',
    none: 'none',
    today: 'Today',
    yesterday: 'Yesterday',
    dayBy: (day: string, name: string) => `${day}, by ${name}`,

    actionReload: 'Reload',
    actionRenameFolder: 'Rename…',
    actionMove: 'Move to…',
    actionDeleteFolder: 'Delete…',
    actionDuplicate: 'Duplicate…',
    actionCopyActions: 'Copy actions to…',
    actionExport: 'Export as JSON',
    actionTrash: 'Trash',
    actionEmptyBin: 'Empty recycle bin…',
    actionRestore: 'Restore…',
    actionDeleteForGood: 'Delete…',
    optionForm: 'Form',
    optionFormDescription: 'A new, empty form.',
    optionFromTemplate: 'Form from a template…',
    optionFromTemplateDescription: 'A contact form or another to adapt.',
    optionImport: 'Import a form…',
    optionImportDescription: 'A form a site exported, as JSON.',
    optionFolder: 'Folder…',
    optionFolderDescription: 'Keeps forms together, with its own access.',
    createForm: 'Create form',
    createFolder: 'Create folder',

    columnName: 'Name',
    columnEntries: 'Entries, last 30 days',
    columnChanged: 'Last changed',
    columnTrashed: 'Trashed',
    columnActions: 'Actions',
    listFailed: "The forms couldn't be loaded.",
    emptyFolder: 'No forms here yet. A template gives you a working form in a minute.',
    emptyFolderReadOnly: 'No forms here yet.',
    chooseTemplate: 'Choose a template…',
    loadMore: 'Show more',
    folderName: 'Folder name',
    binFailed: "The Recycle Bin couldn't be loaded.",
    binEmpty:
      'The Recycle Bin is empty. Trashed forms wait here with their entries until someone restores them or deletes them for good.',
    emptyBin: 'Empty recycle bin…',
    itemGone: "This item doesn't exist any more, or you don't have access to it.",
    inBinSince: (day: string) => `In the Recycle Bin since ${onDay(day, '')}.`,
    notInBin: "This item isn't in the Recycle Bin any more.",

    newFolder: 'New folder',
    renameFolder: (name: string) => `Rename ${name}`,
    rename: 'Rename',
    name: 'Name',
    nameNeeded: 'Enter a name.',
    deleteFolderHeadline: (name: string) => `Delete ${name}?`,
    deleteFolderText: 'The folder is empty, so nothing else is deleted with it.',
    deleteFolderConfirm: 'Delete folder',

    trashHeadline: (name: string) => `Move ${name} to the Recycle Bin?`,
    trashInUse: (pages: number, names: string, more: number) =>
      `${pickedOn(pages, names, more)} ${pages === 1 ? 'That page shows' : 'Those pages show'} nothing in its place until you restore the form.`,
    trashConfirm: 'Move to Recycle Bin',
    trashed: (name: string) => `${name} moved to the Recycle Bin.`,
    undo: 'Undo',
    mayNotChange: (name: string) =>
      `You may not change ${name}. An administrator can give your user group access in Settings › Access.`,

    widenHeadline: (name: string) => `Give more user groups access to ${name}?`,
    widenText: (groups: number) =>
      groups > 0
        ? `In its new place, ${count(groups, 'more user group sees', 'more user groups see')} it and what it holds.`
        : 'In its new place, more user groups see it and what it holds.',
    widenRestoreText: 'Where it comes back, more user groups see it than before it went to the Recycle Bin.',
    widenConfirm: 'Give them access',

    restore: 'Restore',
    restoreToTop: (name: string) => `${name} comes back at the top level of Forms.`,
    restoreToFolder: (name: string, folder: string) => `${name} goes back to ${folder}.`,
    restoreToWhereItWas: (name: string) => `${name} goes back where it was.`,
    restoreFolderTrashed: (name: string, folder: string) =>
      `Its folder, ${folder}, is in the Recycle Bin too, so ${name} comes back at the top level of Forms.`,
    restoreFormComesBack: (entries: number) => `Its ${count(entries, 'entry comes', 'entries come')} back with it.`,
    restoreFolderComesBack: 'The forms and folders that went to the Recycle Bin with it come back with it.',

    deleteFormHeadline: (name: string) => `Delete ${name} for good?`,
    deleteFormText: (entries: number, files: number) => {
      if (entries === 0) return 'This deletes the form. It has no entries.';
      const held = `its ${count(entries, 'entry', 'entries')}`;
      return files > 0
        ? `This deletes the form with ${held} and ${count(files, 'uploaded file', 'uploaded files')}.`
        : `This deletes the form with ${held}.`;
    },
    deleteFormInUse: (pages: number, names: string, more: number) =>
      `${pickedOn(pages, names, more).replace("It's picked", "It's still picked")} ${pages === 1 ? 'It keeps' : 'They keep'} showing nothing in its place.`,
    deleteFormConfirm: (entries: number) =>
      entries === 0 ? 'Delete form' : entries === 1 ? 'Delete form and entry' : 'Delete form and entries',
    deleteTrashedFolderHeadline: (name: string) => `Delete ${name} for good?`,
    deleteTrashedFolderText:
      'This deletes the folder for good, with the forms, folders and entries that went to the Recycle Bin with it.',
    deleteTrashedFolderConfirm: 'Delete folder and contents',
    cannotUndo: "You can't undo this.",

    emptyHeadline: 'Empty the Recycle Bin?',
    emptyText: (forms: number, entries: number, files: number, folders: number) => {
      if (forms === 0)
        return folders === 1
          ? 'This deletes the folder in it for good.'
          : `This deletes the ${count(folders, 'folder', 'folders')} in it for good.`;
      const gone = `This deletes ${count(forms, 'form', 'forms')} for good`;
      const their = forms === 1 ? 'its' : 'their';
      if (entries === 0) return `${gone}. ${forms === 1 ? 'It has' : 'They have'} no entries.`;
      const held = `${their} ${count(entries, 'entry', 'entries')}`;
      return files > 0
        ? `${gone}, with ${held} and ${count(files, 'uploaded file', 'uploaded files')}.`
        : `${gone}, with ${held}.`;
    },
    emptyLeft: (left: number) =>
      `${count(left, 'item', 'items')} you may not delete ${left === 1 ? 'stays' : 'stay'} in it.`,
    emptyConfirm: 'Empty recycle bin',
    emptyNothingYours: "There's nothing in the Recycle Bin that you may delete for good.",

    folderNotEmpty: 'This folder still holds forms or folders. Move or delete them first.',
    formGone: "This form doesn't exist any more, or you don't have access to it.",
    folderGone: "This folder doesn't exist any more, or you don't have access to it.",
    templateGone: "This template doesn't exist any more. Choose another one.",
    changedMeanwhile: 'Someone changed this after you opened it. Reload the page, then try again.',
    missingPermission: (permission: string) =>
      `This needs the "${permission}" permission. An administrator can grant it in Settings › Access.`,
    failed: "That didn't work. Try again, or reload the page.",
    permission_design: 'Design forms',
    permission_manageActions: 'Manage actions',
    permission_viewEntries: 'View entries',
    permission_viewSensitive: 'View sensitive data',
    permission_editEntries: 'Edit entries',
    permission_deleteEntries: 'Delete entries',
    permission_export: 'Export entries',
    permission_manageLibrary: 'Manage the library',
    permission_manageSettings: 'Manage settings',
    permission_managePermissions: 'Manage permissions',
    permission_privacyTools: 'Use the privacy tools',

    duplicateHeadline: (name: string) => `Duplicate ${name}`,
    duplicate: 'Duplicate',
    copysName: 'Name of the copy',
    copyName: (name: string) => `${name} (copy)`,
    copyActionsToo: (actions: number) =>
      actions === 1 ? 'Copy its action too' : `Copy its ${number(actions)} actions too`,
    duplicateRestricted: (where: string) =>
      `This form has access settings of its own, which the copy doesn't take: it lands in ${where} and gets the access set there.`,

    copyActionsHeadline: 'Copy actions',
    copyTo: 'Copy to',
    noTargetYet: 'No form chosen yet',
    chooseForm: 'Choose a form…',
    chooseAnotherForm: 'Choose another form…',
    whichActions: (source: string) => `Actions from ${source}`,
    noActionsToCopy: 'This form has no actions to copy.',
    copyCount: (actions: number) => (actions === 1 ? 'Copy 1 action' : `Copy ${number(actions)} actions`),
    copied: (actions: number, target: string) => `${count(actions, 'action', 'actions')} copied to ${target}.`,
    copiedClean: (actions: number) =>
      actions === 1 ? 'It finds everything it uses there.' : 'They find everything they use there.',
    copiedNeedsFixing: (actions: number, troubled: number) =>
      actions === 1
        ? "It names things that form doesn't have. Fix it before you publish the form:"
        : troubled === actions
          ? "They name things that form doesn't have. Fix them before you publish the form:"
          : "Some of them name things that form doesn't have. Fix those before you publish the form:",
    copyLacksField: (alias: string) => `The field "${alias}"`,
    copyLacksElement: 'A page, group or field',
    copyLacksTag: (tag: string) => `The merge tag ${tag}`,
    fixIn: 'Fix it in the form',
    openForm: (name: string) => `Open ${name}`,

    galleryHeadline: 'Choose a template',
    searchTemplates: 'Search templates',
    templates: 'Templates',
    builtIn: 'Built in',
    savedHere: 'Saved on this site',
    galleryLoading: 'Loading the templates',
    galleryFailed: "The templates couldn't be loaded.",
    galleryNoMatch: (query: string) => `No template matches "${query}".`,
    clearSearch: 'Clear search',
    holds: (pages: number, fields: number) => `${count(pages, 'page', 'pages')}, ${count(fields, 'field', 'fields')}`,
    preview: 'Preview',
    allTemplates: 'All templates',
    useTemplate: 'Use template',
    template_contact: 'Contact',
    template_contactDescription: 'Name, email address and a message, then a thank-you message.',

    importHeadline: 'Import a form',
    importIntro:
      'Choose a JSON file that a site running Jiron Form Builder exported. The form arrives as a new copy; nothing here changes.',
    chooseFile: 'Choose a file…',
    chooseAnotherFile: 'Choose another file…',
    readingFile: 'Reading the file',
    importTooLarge: 'This file is larger than 5 MB, so it is not a form export. Choose the JSON file the export saved.',
    importUnreadable: (reason: string) =>
      reason
        ? `This file can't be imported: ${reason}`
        : "This file can't be imported. Choose the JSON file the export saved.",
    fileExported: (size: string, day: string) => `${size}, exported ${onDay(day, 'on')}`,
    whatItHolds: 'What it holds',
    holdsForm: 'Form',
    holdsPages: 'Pages',
    holdsFields: 'Fields',
    holdsRules: 'Rules',
    holdsOutcomes: 'Outcomes',
    holdsActions: 'Actions',
    exportsNeverHold: 'An export never holds entries, files or access settings.',
    whatItCannotImport: 'What this site lacks',
    whatComesAlong: 'What comes along',
    noteThemeMissing: (theme: string) => `This site has no theme "${theme}", so the form uses the default theme.`,
    noteListReused: (list: string) => `This site already has the option list "${list}", so the form uses it.`,
    noteListCreated: (list: string) => `The option list "${list}" comes along and is added to this site.`,
    noteListMissing: (list: string) =>
      `This site has no option list "${list}". Fields that use it show no options until you choose one.`,
    noteListUnnamed: 'A field uses an option list the file does not name. It shows no options until you choose one.',
    noteActionConfiguration: (key: string) =>
      `An action's setting "${key}" needs a value on this site. The action stays off until it has one.`,
    noteActionType: (type: string) => `This site has no action type "${type}", so that action stays off.`,
    notePageMissing: 'An outcome sends visitors to a page this site lacks. Choose another page for it.',
    noteOther: (code: string) => `The import noted "${code}". Check the form before you publish it.`,
    arrivesAsDraft: (where: string) =>
      `It arrives as a draft in ${where}, so visitors see nothing until you publish it.`,
    importAsNewCopy: 'Import as a new copy',
  },
};
