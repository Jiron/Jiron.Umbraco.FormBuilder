// The form picker's screens in English (ADR-0134): the property, its sidebar and the block label. Each key is used by
// an element in ../picker, which lang.test.ts checks; the messages of the server's checks are in picker.en.ts.
// form-picker.de.ts has the same keys.
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
  },
} satisfies UmbLocalizationDictionary;
