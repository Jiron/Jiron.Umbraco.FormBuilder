// The form workspace's texts in German (ADR-0201): the inspector, the rules, the Settings view, the form check and
// the preview, which only the form's workspace shows. They load with the workspace, not with the backoffice's start
// (`designer/workspace-words.ts`), whose budget counts every word it loads (ADR-0183).
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import check from './check.de';
import inspector from './inspector.de';
import preview from './preview.de';
import rules from './rules.de';
import settings from './settings.de';

export default {
  ...inspector,
  ...rules,
  ...settings,
  ...check,
  ...preview,
} satisfies UmbLocalizationDictionary;
