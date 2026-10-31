// The form picker's texts in English (ADR-0129): the messages its checks send as `#jfbPicker_<key>` when a page or a
// data type is saved (Picker/FormPickerEditors.cs). picker.de.ts has the same keys.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbPicker: {
    notAKey: 'This value is not a key. Pick it again from the list.',
    oneOnly: 'This picker takes one form. Remove the others.',
    noSuchForm: 'A picked form no longer exists or is in the trash. Pick another one.',
    notAllowed: 'This picker does not allow a picked form. Pick one from its folders or forms.',
    noSuchTheme: 'The site has no theme with this name.',
  },
} satisfies UmbLocalizationDictionary;
