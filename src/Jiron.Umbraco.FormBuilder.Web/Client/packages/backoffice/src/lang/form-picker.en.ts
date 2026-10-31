// The form picker's screens in English (ADR-0134, ADR-0135): the property, its sidebar, the block label and the data
// type's settings. Each key is used by an element or the manifests in ../picker, which lang.test.ts checks; the
// messages of the server's checks are in picker.en.ts. form-picker.de.ts has the same keys.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbFormPicker: {
    notAvailable: 'Form not available',
    notAvailableDetail: 'Deleted, unpublished or not one you can pick here',
    remove: (name: string) => `Remove ${name}`,
    removeNotAvailable: "Remove the form that isn't available",
    pickedFailed: "The picked forms couldn't be loaded.",
    tryAgain: 'Try again',
    headline: 'Choose a form',
    headlineMultiple: 'Choose forms',
    search: 'Search forms',
    searchPlaceholder: 'Search by name or alias',
    clearSearch: 'Clear search',
    loadFailed: "The forms couldn't be loaded.",
    searchFailed: "The forms couldn't be searched.",
    showMore: 'Show more',
    noForms: 'There are no published forms to pick yet.',
    noAllowedForms: 'None of the forms allowed here is published yet.',
    emptyFolder: 'No published forms in this folder yet.',
    noMatch: (text: string) => `No forms match "${text}".`,
    found: (count: number) =>
      count === 1 ? '1 form found' : `${new Intl.NumberFormat('en').format(count)} forms found`,
    labelNotAvailable: '(not available)',
    settingMultiple: 'Allow several forms',
    settingAllowedFolders: 'Allowed folders',
    settingAllowedFoldersHelp: 'The forms in these folders and in the folders below them can be picked.',
    settingAllowedForms: 'Allowed forms',
    settingAllowedFormsHelp:
      'These forms can be picked too. With no allowed folders or forms, every form can be picked.',
    settingTheme: 'Theme',
    settingThemeHelp: 'Every form placed here renders in this theme.',
    settingRedirect: 'Page after a send',
    settingRedirectHelp:
      "Visitors go to this page once they have sent a form placed here. Leave it empty to follow the form's own settings.",
    folderNotAvailable: 'Folder not available',
    folderNotAvailableDetail: 'Deleted, in the trash or not one you can see',
    removeFolderNotAvailable: "Remove the folder that isn't available",
    allowedFoldersFailed: "The allowed folders couldn't be loaded.",
    allowedFormsFailed: "The allowed forms couldn't be loaded.",
    headlineFolders: 'Choose folders',
    showFolders: (name: string) => `Show the folders in ${name}`,
    foldersFailed: "The folders couldn't be loaded.",
    noFolders: 'Folders show here once they hold a published form.',
    noSubfolders: 'No folder in here holds a published form yet.',
    themeOwn: "The form's own theme",
    themeMissing: (alias: string) => `${alias} (not on this site)`,
    themesFailed: "The themes couldn't be loaded.",
  },
} satisfies UmbLocalizationDictionary;
