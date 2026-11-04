// The words of rules in English (ROADMAP 9.4d, ADR-0005): each operator as a condition says it, after the field it
// looks at. The rules builder (9.5a) says its conditions with them too.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbRules: {
    op_equals: 'is',
    op_notEquals: 'is not',
    op_isOneOf: 'is one of',
    op_isNoneOf: 'is none of',
    op_isEmpty: 'is empty',
    op_isNotEmpty: 'is not empty',
    op_countAtLeast: 'has at least',
    op_countAtMost: 'has at most',
    op_contains: 'contains',
    op_notContains: "doesn't contain",
    op_startsWith: 'starts with',
    op_endsWith: 'ends with',
    op_matches: 'matches a pattern',
    op_greaterThan: 'is more than',
    op_lessThan: 'is less than',
    op_between: 'is between',
    op_isChecked: 'is ticked',
    op_isNotChecked: 'is not ticked',
  },
} satisfies UmbLocalizationDictionary;
