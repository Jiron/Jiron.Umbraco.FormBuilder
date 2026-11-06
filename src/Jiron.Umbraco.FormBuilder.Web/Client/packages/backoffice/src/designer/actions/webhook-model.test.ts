// The webhook's settings as its editors read them (ROADMAP 10.2d, ADR-0191): the way of sending its values say, as the
// server reads them; references as the server takes them; the rows of a mapping and of the headers, what can't be kept
// of each by the server's rules, and the object of those that can; and where a table and a body start.
import { describe, expect, it } from 'vitest';
import type { PickField } from './action-editors';
import {
  everyAnswer,
  formatOf,
  headerProblem,
  holdsASecret,
  isOwnHeader,
  isReference,
  mappingProblem,
  modeOf,
  objectOf,
  referenceName,
  rowsOf,
  starterBody,
  starterChatBody,
  targetOf,
  type Row,
} from './webhook-model';

const field = (alias: string, more: Partial<PickField> = {}): PickField => ({
  key: alias,
  alias,
  label: alias,
  email: false,
  choice: false,
  file: false,
  sensitive: false,
  top: true,
  options: [],
  ...more,
});

const rows = (...pairs: [string, string][]): Row[] => pairs.map(([name, value]) => ({ name, value }));

describe("the webhook's settings", () => {
  it('reads its target and format, its own address and JSON for anything else', () => {
    expect(['slack', 'teams', 'url', 'other', undefined].map(targetOf)).toEqual([
      'slack',
      'teams',
      'url',
      'url',
      'url',
    ]);
    expect(['form', 'xml', 'json', 'csv', null].map(formatOf)).toEqual(['form', 'xml', 'json', 'json', 'json']);
  });

  it('reads the way of sending as the server does: a body wins, then no answers, then the mapping', () => {
    expect(modeOf('url', {})).toBe('answers');
    expect(modeOf('url', { fields: { email: '{{ field.email }}' } })).toBe('mapping');
    expect(modeOf('url', { fields: {} })).toBe('mapping');
    expect(modeOf('url', { fields: ['email'] })).toBe('answers');
    expect(modeOf('url', { withoutAnswers: true, fields: { a: 'b' } })).toBe('none');
    expect(modeOf('url', { template: '{}', withoutAnswers: true })).toBe('body');
    // A chat: its message, or a body of its own; the format, the mapping and the switch mean nothing for it.
    expect(modeOf('slack', { fields: { a: 'b' }, withoutAnswers: true })).toBe('message');
    expect(modeOf('teams', { template: '{}' })).toBe('body');
  });

  it("takes a reference as the server does: its beginning, then 1 to 64 letters, digits and '_'", () => {
    expect(isReference('$JironFormBuilder:Secrets:CrmToken')).toBe(true);
    expect(isReference('$JironFormBuilder:Secrets:A_1')).toBe(true);
    expect(isReference(`$JironFormBuilder:Secrets:${'a'.repeat(64)}`)).toBe(true);
    expect(isReference(`$JironFormBuilder:Secrets:${'a'.repeat(65)}`)).toBe(false);
    expect(isReference('$JironFormBuilder:Secrets:')).toBe(false);
    expect(isReference('$JironFormBuilder:Secrets:Crm-Token')).toBe(false);
    expect(isReference('$jironformbuilder:secrets:CrmToken')).toBe(false);
    expect(isReference('Bearer abc')).toBe(false);
    expect(referenceName('$JironFormBuilder:Secrets:CrmHook')).toBe('CrmHook');
  });

  it('reads the rows of an object of names and texts, and none of anything else', () => {
    expect(rowsOf({ email: '{{ field.email }}', count: 3, name: '' })).toEqual(
      rows(['email', '{{ field.email }}'], ['name', '']),
    );
    expect([rowsOf(null), rowsOf(['a']), rowsOf('a'), rowsOf(undefined)]).toEqual([[], [], [], []]);
  });

  it("says what can't be kept of a mapping's row: a value without a name once it is left, a wrong name, one twice", () => {
    const typed = rows(['email', 'a'], ['', 'b'], ['full name', 'c'], ['email', 'd'], ['', ''], ['x.y-z_1', 'e']);
    expect(typed.map((_, at) => mappingProblem(typed, at, false))).toEqual([
      null,
      null,
      { at: 'name', kind: 'wrong' },
      { at: 'name', kind: 'twice' },
      null,
      null,
    ]);
    expect(mappingProblem(typed, 1, true)).toEqual({ at: 'name', kind: 'empty' });
    expect(mappingProblem(rows([`${'n'.repeat(101)}`, 'v']), 0, true)).toEqual({ at: 'name', kind: 'wrong' });
    // The rows that can be kept, in their order; the first of two of a name.
    expect(objectOf(typed, mappingProblem)).toEqual({ email: 'a', 'x.y-z_1': 'e' });
  });

  it("says what can't be kept of a header's row by the server's rules, a credential given as itself above all", () => {
    const typed = rows(
      ['X-Source', 'forms'],
      ['Authorization', 'Bearer abc'],
      ['Api-Key', '$JironFormBuilder:Secrets:CrmKey'],
      ['x-source', 'again'],
      ['Content-Type', 'text/plain'],
      ['X-Jfb-Test', 'true'],
      ['Host', 'example.com'],
      ['Bad Name', 'x'],
      ['X-Note', 'gr\u00fc\u00dfe'],
      ['X-Empty', ''],
    );
    expect(typed.map((_, at) => headerProblem(typed, at, true))).toEqual([
      null,
      { at: 'value', kind: 'credential' },
      null,
      { at: 'name', kind: 'twice' },
      { at: 'name', kind: 'own' },
      { at: 'name', kind: 'own' },
      { at: 'name', kind: 'own' },
      { at: 'name', kind: 'wrong' },
      { at: 'value', kind: 'wrong' },
      null,
    ]);
    expect(objectOf(typed, headerProblem)).toEqual({
      'X-Source': 'forms',
      'Api-Key': '$JironFormBuilder:Secrets:CrmKey',
      'X-Empty': '',
    });
    expect(
      ['Authorization', 'X-Api-Key', 'Cookie', 'X-Token', 'Password', 'X-Secret', 'X-Source'].map(holdsASecret),
    ).toEqual([true, true, true, true, true, true, false]);
    expect(['content-length', 'X-JFB-Delivery', 'User-Agent', 'Accept'].map(isOwnHeader)).toEqual([
      true,
      true,
      true,
      false,
    ]);
  });

  it('starts a table with every field a webhook sends by itself, by its alias, a sensitive one left out', () => {
    expect(
      everyAnswer([
        field('email'),
        field('notes', { sensitive: true }),
        field('name'),
        field('', { label: 'No alias' }),
      ]),
    ).toEqual(rows(['email', '{{ field.email }}'], ['name', '{{ field.name }}']));
  });

  it("starts a body in the format's own language with the rows, and a chat's with its message", () => {
    const pairs = rows(['email', '{{ field.email }}'], ['1st', '{{ field.first }}']);
    expect(starterBody('json', pairs)).toBe('{\n  "email": "{{ field.email }}",\n  "1st": "{{ field.first }}"\n}');
    expect(starterBody('json', [])).toBe('{}');
    expect(starterBody('form', pairs)).toBe('email={{ field.email }}&1st={{ field.first }}');
    expect(starterBody('xml', pairs)).toBe(
      '<submission>\n  <email>{{ field.email }}</email>\n  <_1st>{{ field.first }}</_1st>\n</submission>',
    );
    const slack = JSON.parse(starterChatBody('slack', 'Hi "you"\n{{ entry.answers }}')) as { text: string };
    expect(slack).toEqual({ text: 'Hi "you"\n{{ entry.answers }}' });
    const teams = JSON.parse(starterChatBody('teams', '**New**')) as {
      attachments: { content: { type: string; body: { text: string }[] } }[];
    };
    expect([teams.attachments[0]?.content.type, teams.attachments[0]?.content.body[0]?.text]).toEqual([
      'AdaptiveCard',
      '**New**',
    ]);
  });
});
