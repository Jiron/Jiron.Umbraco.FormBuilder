// The Forms section's texts in German (ADR-0057), one file per area merged here, with the keys of en.ts.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import fieldTypes from './field-types.de';
import formPicker from './form-picker.de';
import picker from './picker.de';

export default { ...fieldTypes, ...picker, ...formPicker } satisfies UmbLocalizationDictionary;
