// The post reader's own decisions (ROADMAP 6.4b, ADR-0110), which the shared vectors of tests/vectors/posts.json cannot
// hold, since the server knows a site's own types by its registry: the browser knows them by the form endpoint's
// `builtIn` and the projection's child fields. The most rows any group takes is held to the served field types.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  holdQuery,
  LONGEST_QUERY_VALUE,
  MOST_QUERY_VALUES,
  MOST_ROWS,
  readPost,
  type PostField,
} from '../src/index.ts';
import { repositoryRoot } from '../tools/repository.ts';

interface ServedSetting {
  readonly name: string;
  readonly rules: { readonly maximum: string | null };
}

const served = (
  JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'field-types.json'), 'utf8')) as {
    readonly types: readonly { readonly alias: string; readonly settings: readonly ServedSetting[] }[];
  }
).types;

// A whole-number constant of Core's FormSubmissions, as its source declares it.
const submissionsConstant = (name: string): number => {
  const source = readFileSync(
    join(repositoryRoot(), 'src', 'Jiron.Umbraco.FormBuilder.Core', 'Submissions', 'FormSubmissions.cs'),
    'utf8',
  );
  const start = source.indexOf('public const int ' + name + ' = ');
  if (start < 0) throw new Error('FormSubmissions declares no ' + name);
  const value = source.slice(start, source.indexOf(';', start)).split(' = ')[1] ?? '';
  return Number(value.split('_').join(''));
};

const answers = (fields: readonly PostField[], entries: readonly (readonly [string, string])[]) => {
  const read = readPost(fields, entries);
  return {
    fields: Object.fromEntries(read.fields),
    rows: Object.fromEntries(
      [...read.rows].map(([alias, rows]) => [alias, rows.map((row) => Object.fromEntries(row))]),
    ),
  };
};

describe('the post reader', () => {
  it('takes as many rows as a repeatable group may have at most', () => {
    const maxRows = served
      .find((type) => type.alias === 'repeater')
      ?.settings.find((setting) => setting.name === 'maxRows');
    expect(maxRows?.rules.maximum).toBe(String(MOST_ROWS));
  });

  it("reads a site's own type under a built-in alias as the site's: no switch, no signature, no repeatable group", () => {
    const own = (field: PostField): PostField => ({ ...field, builtIn: false });
    expect(answers([own({ alias: 'updates', type: 'yesno', settings: { style: 'switch' } })], [])).toEqual({
      fields: {},
      rows: {},
    });
    expect(answers([own({ alias: 'sign', type: 'signature' })], [['sign.typed', 'Ada']])).toEqual({
      fields: { sign: { values: [], parts: [['typed', ['Ada']]] } },
      rows: {},
    });
    expect(
      answers(
        [own({ alias: 'guests', type: 'repeater' })],
        [
          ['guests', 'x'],
          ['jfb-rows-guests', '2'],
        ],
      ),
    ).toEqual({
      fields: { guests: { values: ['x'] } },
      rows: {},
    });
  });

  it("reads a site's own type that holds fields as a repeatable group", () => {
    const team: PostField = {
      alias: 'team',
      type: 'crew',
      builtIn: false,
      fields: [{ alias: 'member', type: 'text' }],
    };
    expect(
      answers(
        [team],
        [
          ['jfb-rows-team', '2'],
          ['team[1].member', 'Ada'],
          ['team', 'x'],
        ],
      ),
    ).toEqual({
      fields: {},
      rows: { team: [{}, { member: { values: ['Ada'] } }] },
    });
  });

  it('never takes a name of an object of its own for a field', () => {
    expect(
      answers(
        [{ alias: 'name', type: 'text' }],
        [
          ['constructor', 'x'],
          ['toString', 'y'],
          ['__proto__', 'z'],
        ],
      ),
    ).toEqual({
      fields: {},
      rows: {},
    });
    expect(answers([{ alias: 'constructor', type: 'text' }], [['constructor', 'x']])).toEqual({
      fields: { constructor: { values: ['x'] } },
      rows: {},
    });
  });
});

describe('the query values a submission carries', () => {
  it("holds them to Core's limits", () => {
    expect(MOST_QUERY_VALUES).toBe(submissionsConstant('MaxQueryValues'));
    expect(LONGEST_QUERY_VALUE).toBe(submissionsConstant('MaxValueLength'));
  });

  it('takes only the names the form lists, each value within the longest and at most the most of them', () => {
    const many = Array.from({ length: MOST_QUERY_VALUES + 3 }, (_, at) => 'v' + String(at));
    const held = holdQuery(
      [
        ['source', ['ad', 'x'.repeat(LONGEST_QUERY_VALUE + 1), 'x'.repeat(LONGEST_QUERY_VALUE)]],
        ['ref', many],
        ['other', ['kept out']],
        ['__proto__', ['a']],
      ],
      ['source', 'ref', 'missing'],
    );
    expect({ ...held }).toEqual({
      source: ['ad', 'x'.repeat(LONGEST_QUERY_VALUE)],
      ref: many.slice(0, MOST_QUERY_VALUES),
    });
    expect(Object.getPrototypeOf(held)).toBeNull();
  });
});
