// The Forms section's texts in German (ADR-0057), one file per area merged here, with the keys of en.ts.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import check from './check.de';
import designer from './designer.de';
import fieldTypes from './field-types.de';
import formPicker from './form-picker.de';
import inspector from './inspector.de';
import library from './library.de';
import mergeTags from './merge-tags.de';
import options from './options.de';
import picker from './picker.de';
import rules from './rules.de';
import section from './section.de';
import settings from './settings.de';

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
