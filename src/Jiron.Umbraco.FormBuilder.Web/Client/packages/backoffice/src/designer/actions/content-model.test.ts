// The content action's settings as its editors read them (ROADMAP 10.2e, ADR-0192): where the item goes, a document
// type as the server describes it, the rows of the table and the setting they make, the fields a property can hold the
// answer of, and the rows a table starts with.
import { describe, expect, it } from 'vitest';
import type { PickField } from './action-editors';
import {
  contentTypeOf,
  decimalText,
  fieldsFor,
  mappingOf,
  parentSourceOf,
  rowsOf,
  sameMapping,
  starterRows,
  valueKindOf,
  type ContentProperty,
  type ContentType,
} from './content-model';

const field = (alias: string, type = 'text', more: Partial<PickField> = {}): PickField => ({
  key: `key-${alias}`,
  alias,
  label: alias,
  type,
  email: type === 'email',
  choice: false,
  file: type === 'file',
  sensitive: false,
  top: true,
  options: [],
  ...more,
});

const property = (alias: string, fieldTypes: string[], value: ContentProperty['value'] = 'text'): ContentProperty => ({
  alias,
  name: alias.toUpperCase(),
  group: null,
  fieldTypes,
  value,
});

const story: ContentType = {
  alias: 'story',
  name: 'Story',
  properties: [
    property('title', ['text', 'name']),
    property('email', ['text', 'email']),
    property('age', ['number'], 'wholeNumber'),
    property('photo', ['file'], null),
  ],
};

describe("the content action's settings", () => {
  it('reads where the item goes, under the item picked for anything else', () => {
    expect(['picked', 'page', 'siteRoot', 'other', undefined].map(parentSourceOf)).toEqual([
      'picked',
      'page',
      'siteRoot',
      'picked',
      'picked',
    ]);
  });

  it('reads a document type as the server describes it, a kind of value it does not know as none', () => {
    expect(
      contentTypeOf({
        alias: 'story',
        name: 'Story',
        properties: [
          { alias: 'title', name: 'Title', group: 'Content', fieldTypes: ['text'], value: 'text' },
          { alias: 'blocks', name: 'Blocks', fieldTypes: ['text'], value: 'blocks' },
          { alias: 'photo', name: 'Photo', group: null, fieldTypes: ['file'], value: null },
        ],
      }),
    ).toEqual({
      alias: 'story',
      name: 'Story',
      properties: [
        { alias: 'title', name: 'Title', group: 'Content', fieldTypes: ['text'], value: 'text' },
        { alias: 'blocks', name: 'Blocks', group: null, fieldTypes: ['text'], value: null },
        { alias: 'photo', name: 'Photo', group: null, fieldTypes: ['file'], value: null },
      ],
    });
    expect(['text', 'wholeNumber', 'decimal', 'date', 'boolean', 'rich', 3, undefined].map(valueKindOf)).toEqual([
      'text',
      'wholeNumber',
      'decimal',
      'date',
      'boolean',
      null,
      null,
      null,
    ]);
  });

  it("reads each item of a setting as a row, a field's answer before a value, and an item that is none as an empty row", () => {
    expect(
      rowsOf([
        { property: 'title', field: 'key-name' },
        { property: 'source', value: 'Web' },
        { property: 'both', field: 'key-name', value: 'Web' },
        { property: 'neither' },
        'title',
        null,
      ]),
    ).toEqual([
      { property: 'title', field: 'key-name' },
      { property: 'source', value: 'Web' },
      { property: 'both', field: 'key-name' },
      { property: 'neither' },
      { property: '' },
      { property: '' },
    ]);
    expect(rowsOf(undefined)).toEqual([]);
    expect(rowsOf({ property: 'title' })).toEqual([]);
  });

  it('keeps of the rows each with a property and what it takes, in their order, and none for none', () => {
    expect(
      mappingOf([
        { property: 'title', field: 'key-name' },
        { property: '' },
        { property: 'age' },
        { property: 'source', value: '' },
      ]),
    ).toEqual([
      { property: 'title', field: 'key-name' },
      { property: 'source', value: '' },
    ]);
    expect(mappingOf([{ property: '' }, { property: 'age' }])).toBeUndefined();
    expect(
      sameMapping([{ property: 'title', field: 'a' }, { property: '' }], [{ property: 'title', field: 'a' }]),
    ).toBe(true);
    expect(sameMapping(undefined, [])).toBe(true);
    expect(sameMapping([{ property: 'title', field: 'a' }], [{ property: 'title', field: 'b' }])).toBe(false);
  });

  it('offers a property the fields whose answer it can hold, each with one answer, and none for no property', () => {
    const fields = [
      field('name', 'name'),
      field('email', 'email'),
      field('guest', 'text', { top: false }),
      field('age', 'number'),
    ];
    expect(fieldsFor(story.properties[0], fields).map((one) => one.alias)).toEqual(['name']);
    expect(fieldsFor(story.properties[1], fields).map((one) => one.alias)).toEqual(['email']);
    expect(fieldsFor(story.properties[2], fields).map((one) => one.alias)).toEqual(['age']);
    expect(fieldsFor(undefined, fields)).toEqual([]);
  });

  it('starts a table with each property a field of its alias can fill, a sensitive one only where it may be copied', () => {
    const fields = [
      field('title', 'text'),
      field('email', 'email', { sensitive: true }),
      field('age', 'text'),
      field('photo', 'file'),
    ];
    expect(starterRows(story, fields)).toEqual([
      { property: 'title', field: 'key-title' },
      { property: 'photo', field: 'key-photo' },
    ]);
    expect(starterRows(story, fields, ['key-email'])).toEqual([
      { property: 'title', field: 'key-title' },
      { property: 'email', field: 'key-email' },
      { property: 'photo', field: 'key-photo' },
    ]);
  });

  it('writes a decimal with a decimal comma with a point, and anything else as it is', () => {
    expect(['1,5', ' -2,25 ', '1.5', '1,234.5', '1,2,3', 'many'].map(decimalText)).toEqual([
      '1.5',
      '-2.25',
      '1.5',
      '1,234.5',
      '1,2,3',
      'many',
    ]);
  });
});
