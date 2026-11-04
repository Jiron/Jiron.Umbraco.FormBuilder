// The words of rules in German (ROADMAP 9.4d, ADR-0005), with the keys of rules.en.ts.
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';

export default {
  jfbRules: {
    op_equals: 'ist',
    op_notEquals: 'ist nicht',
    op_isOneOf: 'ist eines von',
    op_isNoneOf: 'ist keines von',
    op_isEmpty: 'ist leer',
    op_isNotEmpty: 'ist nicht leer',
    op_countAtLeast: 'hat mindestens',
    op_countAtMost: 'hat höchstens',
    op_contains: 'enthält',
    op_notContains: 'enthält nicht',
    op_startsWith: 'beginnt mit',
    op_endsWith: 'endet mit',
    op_matches: 'passt zu einem Muster',
    op_greaterThan: 'ist größer als',
    op_lessThan: 'ist kleiner als',
    op_between: 'liegt zwischen',
    op_isChecked: 'ist angekreuzt',
    op_isNotChecked: 'ist nicht angekreuzt',
  },
} satisfies UmbLocalizationDictionary;
