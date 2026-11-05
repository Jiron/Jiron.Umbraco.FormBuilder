// The Forms section's texts in English (ADR-0057), one file per area merged here. A key is `<area>_<key>`: a `#` key
// in manifests and in what the management API serves, localize.term('<area>_<key>') in elements. de.ts has the same
// keys, which lang.test.ts checks.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import check from './check.en';
import designer from './designer.en';
import fieldTypes from './field-types.en';
import formPicker from './form-picker.en';
import inspector from './inspector.en';
import library from './library.en';
import mergeTags from './merge-tags.en';
import options from './options.en';
import picker from './picker.en';
import rules from './rules.en';
import section from './section.en';
import settings from './settings.en';

export default {
  ...fieldTypes,
  ...options,
  ...picker,
  ...formPicker,
  ...section,
  ...library,
  ...designer,
  ...inspector,
  ...mergeTags,
  ...rules,
  ...settings,
  ...check,
} satisfies UmbLocalizationDictionary;
