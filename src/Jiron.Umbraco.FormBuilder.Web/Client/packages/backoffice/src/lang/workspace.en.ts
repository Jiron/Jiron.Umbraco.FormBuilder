// The form workspace's texts in English (ADR-0201): the inspector, the rules, the Settings view, the outcomes, the form
// check and the preview, which only the form's workspace shows. They load with the workspace, not with the backoffice's
// start (`designer/workspace-words.ts`), whose budget counts every word it loads (ADR-0183).
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import check from './check.en';
import inspector from './inspector.en';
import outcomes from './outcomes.en';
import preview from './preview.en';
import rules from './rules.en';
import settings from './settings.en';

export default {
  ...inspector,
  ...rules,
  ...settings,
  ...outcomes,
  ...check,
  ...preview,
} satisfies UmbLocalizationDictionary;
