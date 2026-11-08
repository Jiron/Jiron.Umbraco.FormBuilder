// The Forms section's texts in English (ADR-0057), one file per area merged here. A key is `<area>_<key>`: a `#` key
// in manifests and in what the management API serves, localize.term('<area>_<key>') in elements. de.ts has the same
// keys, which lang.test.ts checks. The form workspace's own areas load with it (workspace.en.ts, ADR-0201), and the
// Actions and History views' with them.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import designer from './designer.en';
import fieldTypes from './field-types.en';
import formPicker from './form-picker.en';
import library from './library.en';
import mergeTags from './merge-tags.en';
import options from './options.en';
import picker from './picker.en';
import section from './section.en';

export default {
  ...fieldTypes,
  ...options,
  ...picker,
  ...formPicker,
  ...section,
  ...library,
  ...designer,
  ...mergeTags,
} satisfies UmbLocalizationDictionary;
