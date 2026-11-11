// A rule in words (ROADMAP 9.5a, ADR-0175): how each kind of condition, value, group and effect is said, in English.
import type { Kind } from '@jfb/logic';
import { describe, expect, it } from 'vitest';
import startDe from '../../lang/de';
import start from '../../lang/en';
import workspaceDe from '../../lang/workspace.de';
import workspace from '../../lang/workspace.en';
import { valueText } from './rule-names';
import { conditionWords, operatorTerm, relativeWords, ruleWords, type RuleWordsContext } from './rule-words';

const flatten = (...areas: object[]) =>
  Object.fromEntries(
    areas.flatMap((dictionary) =>
      Object.entries(dictionary as Record<string, Record<string, unknown>>).flatMap(([area, texts]) =>
        Object.entries(texts).map(([key, value]) => [`${area}_${key}`, value]),
      ),
    ),
  );
const termIn =
  (flat: Record<string, unknown>) =>
  (key: string, ...args: unknown[]): string => {
    const value = flat[key];
    return typeof value === 'function' ? (value as (...given: unknown[]) => string)(...args) : String(value ?? key);
  };
const term = termIn(flatten(start, workspace));

const NAMES: Record<string, string> = {
  country: 'Country',
  arrival: 'Arrival',
  departure: 'Departure',
  topics: 'Topics',
  uploads: 'Uploads',
  agree: 'Agree',
  which: 'Which country?',
  guests: 'Guests',
};
const KINDS: Record<string, Kind> = {
  country: 'choice',
  arrival: 'date',
  departure: 'date',
  topics: 'choices',
  uploads: 'files',
  agree: 'checkbox',
  which: 'text',
  guests: 'number',
};
const CONTEXT: RuleWordsContext = {
  term,
  nameOf: (key) => NAMES[key] ?? key,
  kindOf: (field) => KINDS[field],
  optionOf: (_, value) => ({ other: 'Other', shipping: 'Shipping' })[value] ?? value,
  languageOf: (culture) => (culture === 'de-DE' ? 'German' : culture),
  valueOf: (kind, text) => valueText('en-GB', kind, text),
  textOf: (texts) => texts?.['de-DE'] ?? texts?.[''] ?? '',
};

const say = (condition: Record<string, unknown>) => conditionWords(CONTEXT, condition);

describe('a condition in words', () => {
  it('names the field, says the operator for its kind, and the value as the kind reads it', () => {
    expect(say({ field: 'country', op: 'equals', value: 'other' })).toBe('Country is Other');
    expect(say({ field: 'country', op: 'isOneOf', value: ['de', 'other'] })).toBe('Country is one of de, Other');
    expect(say({ field: 'which', op: 'contains', value: 'Dr' })).toBe('Which country? contains "Dr"');
    expect(say({ field: 'which', op: 'isEmpty' })).toBe('Which country? is empty');
    expect(say({ field: 'guests', op: 'between', value: ['1', '4'] })).toBe('Guests is between 1 and 4');
    expect(say({ field: 'guests', op: 'greaterThan', value: '2' })).toBe('Guests is more than 2');
    expect(say({ field: 'arrival', op: 'greaterThan', value: '2026-12-01' })).toBe('Arrival is after 1 Dec 2026');
    expect(say({ field: 'topics', op: 'isChecked', value: 'shipping' })).toBe('Topics has ticked Shipping');
    expect(say({ field: 'topics', op: 'countAtMost', value: '1' })).toBe('Topics has at most 1 ticked');
    expect(say({ field: 'uploads', op: 'countAtLeast', value: '2' })).toBe('Uploads has at least 2 files');
    expect(say({ field: 'uploads', op: 'countAtLeast', value: '1' })).toBe('Uploads has at least 1 file');
    expect(say({ field: 'agree', op: 'isNotChecked' })).toBe('Agree is not ticked');
  });

  it('says another answer, and a day counted from today', () => {
    expect(say({ field: 'departure', op: 'greaterThan', value: { field: 'arrival' } })).toBe(
      'Departure is after the answer to Arrival',
    );
    expect(say({ field: 'arrival', op: 'lessThan', value: { today: '' } })).toBe('Arrival is before today');
    expect(say({ field: 'arrival', op: 'greaterThan', value: { today: '+2w' } })).toBe(
      'Arrival is after today plus 2 weeks',
    );
    expect(relativeWords(CONTEXT, '-1d')).toBe('today minus 1 day');
    expect(relativeWords(CONTEXT, '-1m+1d')).toBe('today -1m+1d');
    expect(relativeWords(CONTEXT, 'soon')).toBe('soon');
  });

  it('says the parts of the visit in words of their own', () => {
    expect(say({ context: 'culture', op: 'equals', value: 'de-DE' })).toBe('The language is German');
    expect(say({ context: 'memberSignedIn', op: 'isChecked' })).toBe('The visitor is signed in');
    expect(say({ context: 'memberSignedIn', op: 'isNotChecked' })).toBe('The visitor is not signed in');
    expect(say({ context: 'memberGroup', op: 'isChecked', value: 'Gold' })).toBe('Member groups include Gold');
    expect(say({ context: 'memberGroup', op: 'isNoneOf', value: ['Gold', 'Silver'] })).toBe(
      'Member groups include none of Gold, Silver',
    );
    expect(say({ context: 'query.ref', op: 'equals', value: 'web' })).toBe('The query value ref is "web"');
    expect(say({ context: 'today', op: 'greaterThan', value: '2026-12-01' })).toBe("Today's date is after 1 Dec 2026");
    expect(operatorTerm('greaterThan', 'time', null)).toBe('jfbRules_op_after');
    expect(operatorTerm('isChecked', 'checkbox', null)).toBe('jfbRules_op_isChecked');
  });
});

describe('a rule in words', () => {
  it('says what it does, joins conditions by its match, and brackets a group of several', () => {
    expect(
      ruleWords(CONTEXT, {
        effect: 'show',
        match: 'any',
        conditions: [
          { field: 'country', op: 'equals', value: 'other' },
          {
            match: 'all',
            conditions: [
              { field: 'guests', op: 'greaterThan', value: '2' },
              { field: 'agree', op: 'isChecked' },
            ],
          },
        ],
      }),
    ).toBe('Shown when Country is Other or (Guests is more than 2 and Agree is ticked)');
    expect(
      ruleWords(CONTEXT, {
        effect: 'hide',
        conditions: [{ match: 'any', conditions: [{ field: 'agree', op: 'isChecked' }] }],
      }),
    ).toBe('Hidden when Agree is ticked');
    expect(ruleWords(CONTEXT, { effect: 'require', conditions: [{ field: 'which', op: 'isNotEmpty' }] })).toBe(
      'Required when Which country? is not empty',
    );
    expect(ruleWords(CONTEXT, { effect: 'nope', conditions: null })).toBe('Shown when ');
  });

  it('says a part of the visit small inside the sentence, as a line of a list starts it big', () => {
    expect(
      ruleWords(CONTEXT, {
        effect: 'show',
        match: 'all',
        conditions: [
          { context: 'memberSignedIn', op: 'isChecked' },
          { context: 'culture', op: 'equals', value: 'de-DE' },
          { context: 'today', op: 'greaterThan', value: '2026-12-01' },
          { context: 'query.ref', op: 'equals', value: 'web' },
        ],
      }),
    ).toBe(
      'Shown when the visitor is signed in and the language is German and today\'s date is after 1 Dec 2026 and the query value ref is "web"',
    );
    expect(
      ruleWords(CONTEXT, { effect: 'hide', conditions: [{ context: 'memberGroup', op: 'isChecked', value: 'Gold' }] }),
    ).toBe('Hidden when member groups include Gold');
  });

  it("says a validation rule's message in the language shown, the standard one where it has none, and an outcome's use", () => {
    const conditions = [{ field: 'which', op: 'endsWith', value: 'land' }];
    expect(
      ruleWords(CONTEXT, { effect: 'message', conditions, message: { '': 'Name a country', 'de-DE': 'Land' } }),
    ).toBe('Shows "Land" when Which country? ends with "land"');
    expect(ruleWords(CONTEXT, { effect: 'message', conditions, message: { '': '  ' } })).toBe(
      'Shows the standard message when Which country? ends with "land"',
    );
    expect(ruleWords(CONTEXT, { effect: 'message', conditions })).toBe(
      'Shows the standard message when Which country? ends with "land"',
    );
    expect(
      ruleWords(CONTEXT, { effect: 'use', conditions: [{ field: 'country', op: 'equals', value: 'other' }] }),
    ).toBe('Used when Country is Other');
    // An action's rule (ROADMAP 10.2a).
    expect(
      ruleWords(CONTEXT, { effect: 'run', conditions: [{ field: 'country', op: 'equals', value: 'other' }] }),
    ).toBe('Runs when Country is Other');
  });
});

describe('a rule in German words', () => {
  // German puts a clause's verb last and quotes a field's name inside a sentence (ADR-0228, the German walk of 14.1).
  const LOW = '\u201e';
  const HIGH = '\u201c';
  const GERMAN: RuleWordsContext = {
    ...CONTEXT,
    term: termIn(flatten(startDe, workspaceDe)),
    nameOf: (key) => ({ guests: 'G\u00e4ste', which: 'Welches Land?' })[key] ?? key,
    languageOf: (culture) => (culture === 'de-DE' ? 'Deutsch' : culture),
    valueOf: (kind, text) => valueText('de-DE', kind, text),
  };
  const rule = (effect: string, condition: Record<string, unknown>) =>
    ruleWords(GERMAN, { effect, conditions: [condition] });

  it('says each condition with its verb last, a name in quotes', () => {
    expect(rule('require', { field: 'guests', op: 'lessThan', value: '7' })).toBe(
      `Pflicht, wenn ${LOW}G\u00e4ste${HIGH} kleiner als 7 ist`,
    );
    expect(rule('use', { context: 'culture', op: 'startsWith', value: 'de' })).toBe(
      `Verwendet, wenn die Sprache mit ${LOW}de${HIGH} beginnt`,
    );
    expect(rule('show', { context: 'memberSignedIn', op: 'isChecked' })).toBe(
      'Sichtbar, wenn die ausf\u00fcllende Person angemeldet ist',
    );
    expect(rule('hide', { field: 'which', op: 'notContains', value: 'land' })).toBe(
      `Verborgen, wenn ${LOW}Welches Land?${HIGH} ${LOW}land${HIGH} nicht enth\u00e4lt`,
    );
    expect(rule('show', { field: 'guests', op: 'between', value: ['1', '4'] })).toBe(
      `Sichtbar, wenn ${LOW}G\u00e4ste${HIGH} zwischen 1 und 4 liegt`,
    );
    expect(rule('show', { field: 'which', op: 'isEmpty' })).toBe(`Sichtbar, wenn ${LOW}Welches Land?${HIGH} leer ist`);
  });

  it("keeps a list's line in its own order, the name as it is", () => {
    expect(conditionWords(GERMAN, { field: 'guests', op: 'lessThan', value: '7' })).toBe(
      'G\u00e4ste ist kleiner als 7',
    );
  });
});
