// The Forms section's texts in English (ADR-0057), one file per area merged here. A key is `<area>_<key>`: a `#` key
// in manifests and in what the management API serves, localize.term('<area>_<key>') in elements. de.ts has the same
// keys, which lang.test.ts checks.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import fieldTypes from './field-types.en';
import formPicker from './form-picker.en';
import library from './library.en';
import picker from './picker.en';
import section from './section.en';

export default { ...fieldTypes, ...picker, ...formPicker, ...section, ...library } satisfies UmbLocalizationDictionary;
