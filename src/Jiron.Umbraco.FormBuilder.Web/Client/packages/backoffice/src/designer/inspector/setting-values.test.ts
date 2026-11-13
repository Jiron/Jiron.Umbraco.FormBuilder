// A setting's value as the inspector reads, checks and writes it (ROADMAP 9.4a, ADR-0170), against the schema the field
// types endpoint serves (docs/api/field-types.json).
import { describe, expect, it } from 'vitest';
import type { FieldSettingResponseModel } from '../../api';
import type { DesignField } from '../definition';
import { must } from '../../testing';
import { builtInTypes } from '../preview/fixtures';
import {
  isDate,
  ownText,
  parseChoices,
  parseFileSize,
  parseSetting,
  readDateBound,
  settingsOf,
  valueOf,
  withKey,
  without,
  withSetting,
  withText,
  writeDateBound,
} from './setting-values';

const settingOf = (type: string, name: string): FieldSettingResponseModel => {
  const found = builtInTypes[type]?.settings.find((setting) => setting.name === name);
  if (!found) throw new Error(`${type} has no setting ${name}`);
  return found;
};

const NO_RULES: FieldSettingResponseModel['rules'] = {
  required: false,
  minimumIsExclusive: false,
  maximumIsExclusive: false,
  allowedValues: [],
};

const setting = (extra: Partial<FieldSettingResponseModel>): FieldSettingResponseModel => ({
  name: 'thing',
  kind: 'text',
  choices: [],
  settings: [],
  label: 'Thing',
  editor: 'text',
  group: 'advanced',
  supportsMergeTags: false,
  isPublic: false,
  rules: NO_RULES,
  ...extra,
});

const field: DesignField = { key: 'f', type: 'text', alias: 'name', settings: { maxLength: 80 } };

describe("a setting's value", () => {
  it("is the host's locked one, else the field's own, else the type's default", () => {
    const maxLength = settingOf('text', 'maxLength');
    expect(valueOf(settingsOf(field), maxLength)).toBe(80);
    expect(valueOf({}, maxLength)).toBe(255);
    const locked = { ...maxLength, lock: { hidden: false, configurationKey: 'K', value: 120 } };
    expect(valueOf(settingsOf(field), locked)).toBe(120);
    const lockedToDefault = { ...maxLength, lock: { hidden: false, configurationKey: 'K' } };
    expect(valueOf(settingsOf(field), lockedToDefault)).toBe(255);
    expect(settingsOf({ key: 'x', type: 'text' })).toEqual({});
  });

  it('is written to the field, and taken away where it is the default or nothing, with the settings left empty', () => {
    const maxLength = settingOf('text', 'maxLength');
    expect(withSetting(field, maxLength, 120).settings).toEqual({ maxLength: 120 });
    expect(withSetting(field, maxLength, 255)).not.toHaveProperty('settings');
    expect(withSetting(field, maxLength, undefined)).toEqual({ key: 'f', type: 'text', alias: 'name' });
    const counter = settingOf('text', 'counter');
    expect(withSetting(field, counter, true).settings).toEqual({ maxLength: 80, counter: true });
  });

  it('keeps a record without a key, or with it set', () => {
    expect(without({ a: 1, b: 2 }, 'a')).toEqual({ b: 2 });
    expect(withKey({ a: 1 }, 'b', 2)).toEqual({ a: 1, b: 2 });
    expect(withKey({ a: 1, b: 2 }, 'b', undefined)).toEqual({ a: 1 });
  });
});

describe('a text in a language', () => {
  it("is shown as the language's own words where another language than the default has none", () => {
    expect(ownText({ '': 'Name', 'de-DE': 'Name (de)' }, 'de-DE')).toBe('Name (de)');
    expect(ownText({ '': 'Name', de: 'Name (de)' }, 'de-CH')).toBe('Name (de)');
    expect(ownText({ '': 'Name' }, 'de-DE')).toBe('');
    expect(ownText(null, 'de-DE')).toBe('');
  });

  it("goes back where the language reads it from, a new language's text to its own key, and empty takes it away", () => {
    expect(withText({ '': 'Name' }, 'en-US', 'Your name', true)).toEqual({ '': 'Your name' });
    expect(withText({ '': 'Name', 'en-US': 'Name (us)' }, 'en-US', 'Yours', true)).toEqual({
      '': 'Name',
      'en-US': 'Yours',
    });
    expect(withText({ '': 'Name' }, 'de-DE', 'Ihr Name', false)).toEqual({ '': 'Name', 'de-DE': 'Ihr Name' });
    expect(withText({ '': 'Name', de: 'N' }, 'de-DE', 'Ihr Name', false)).toEqual({ '': 'Name', de: 'Ihr Name' });
    expect(withText({ '': 'Name', 'de-DE': 'N' }, 'de-DE', '', false)).toEqual({ '': 'Name' });
    expect(withText({ '': 'Name' }, 'en-US', '', true)).toBeUndefined();
    expect(withText({ '': 'Color', 'en-GB': 'Colour' }, 'en-US', 'Hue', true)).toEqual({
      '': 'Hue',
      'en-GB': 'Colour',
    });
  });

  it('keeps its default, as the server needs, empty where only another language has words', () => {
    expect(withText(undefined, 'de-DE', 'Hallo', false)).toEqual({ '': '', 'de-DE': 'Hallo' });
    expect(withText({ '': 'Name', 'de-DE': 'Name' }, 'en-US', '', true)).toEqual({ '': '', 'de-DE': 'Name' });
    expect(withText({ '': '', 'de-DE': 'Name' }, 'de-DE', '', false)).toBeUndefined();
    expect(withText({ '': '', 'de-DE': 'Name' }, 'en-US', 'Name', true)).toEqual({ '': 'Name', 'de-DE': 'Name' });
    expect(Object.keys(withText({ 'de-DE': 'Alt' }, 'fr-FR', 'Nom', false) ?? {})).toEqual(['', 'de-DE', 'fr-FR']);
  });
});

describe('what is typed into a setting', () => {
  it("is a whole number within the setting's limits", () => {
    const maxLength = settingOf('text', 'maxLength');
    expect(parseSetting(maxLength, ' 80 ')).toEqual({ ok: true, value: 80 });
    expect(parseSetting(maxLength, '8.5')).toEqual({ ok: false, problem: { kind: 'wholeNumber' } });
    expect(parseSetting(maxLength, 'x')).toEqual({ ok: false, problem: { kind: 'wholeNumber' } });
    expect(parseSetting(maxLength, '0')).toEqual({
      ok: false,
      problem: { kind: 'below', limit: '1', exclusive: false },
    });
    expect(parseSetting(maxLength, '4001')).toEqual({
      ok: false,
      problem: { kind: 'above', limit: '4000', exclusive: false },
    });
    expect(parseSetting(maxLength, '')).toEqual({ ok: true, value: undefined });
  });

  it('is a number in its wire form, with exclusive limits as the rules say', () => {
    const step = setting({
      kind: 'number',
      editor: 'number',
      rules: { ...NO_RULES, minimum: '0', minimumIsExclusive: true },
    });
    expect(parseSetting(step, '0.5')).toEqual({ ok: true, value: '0.5' });
    expect(parseSetting(step, '007.25')).toEqual({ ok: true, value: '7.25' });
    expect(parseSetting(step, '-0.5')).toEqual({ ok: false, problem: { kind: 'below', limit: '0', exclusive: true } });
    expect(parseSetting(step, '0')).toEqual({ ok: false, problem: { kind: 'below', limit: '0', exclusive: true } });
    expect(parseSetting(step, '1,5')).toEqual({ ok: false, problem: { kind: 'number' } });
    expect(parseSetting(step, '1.')).toEqual({ ok: false, problem: { kind: 'number' } });
    const below = setting({
      kind: 'number',
      editor: 'number',
      rules: { ...NO_RULES, maximum: '10', maximumIsExclusive: true },
    });
    expect(parseSetting(below, '10')).toEqual({ ok: false, problem: { kind: 'above', limit: '10', exclusive: true } });
    expect(parseSetting(below, '9.99')).toEqual({ ok: true, value: '9.99' });
  });

  it("is a file's largest size in megabytes, stored as bytes, empty for the site's limit", () => {
    const maxBytes = settingOf('file', 'maxBytes');
    expect(maxBytes.editor).toBe('fileSize');
    expect(parseFileSize(maxBytes, ' 10 ')).toEqual({ ok: true, value: 10_485_760 });
    expect(parseFileSize(maxBytes, '2,5')).toEqual({ ok: true, value: 2_621_440 });
    expect(parseFileSize(maxBytes, '0.25')).toEqual({ ok: true, value: 262_144 });
    expect(parseFileSize(maxBytes, '')).toEqual({ ok: true, value: undefined });
    for (const wrong of ['2.555', 'ten', '-1', '0'])
      expect(parseFileSize(maxBytes, wrong)).toEqual({ ok: false, problem: { kind: 'megabytes' } });
    expect(parseFileSize(maxBytes, '999999999999')).toMatchObject({ ok: false, problem: { kind: 'above' } });
  });

  it('is a time as 14:30', () => {
    const min = settingOf('time', 'min');
    expect(parseSetting(min, '09:05')).toEqual({ ok: true, value: '09:05' });
    expect(parseSetting(min, '24:00')).toEqual({ ok: false, problem: { kind: 'time' } });
    expect(parseSetting(min, '9:05')).toEqual({ ok: false, problem: { kind: 'time' } });
  });

  it('is a list with one item a line, a date list of real dates, within its counts and lengths', () => {
    const excluded = settingOf('date', 'excluded');
    expect(parseSetting(excluded, '2026-12-24\n\n 2026-12-31 \r\n')).toEqual({
      ok: true,
      value: ['2026-12-24', '2026-12-31'],
    });
    expect(parseSetting(excluded, '2026-02-30')).toEqual({ ok: false, problem: { kind: 'date' } });
    expect(parseSetting(excluded, '  \n ')).toEqual({ ok: true, value: undefined });
    const counted = setting({
      kind: 'list',
      editor: 'list',
      itemKind: 'text',
      rules: { ...NO_RULES, maximumCount: 2, maximumLength: 3 },
    });
    expect(parseSetting(counted, 'a\nb\nc')).toEqual({ ok: false, problem: { kind: 'tooMany', limit: 2 } });
    expect(parseSetting(counted, 'abcd')).toEqual({ ok: false, problem: { kind: 'tooLong', limit: 3 } });
    const least = setting({ kind: 'list', editor: 'list', itemKind: 'text', rules: { ...NO_RULES, minimumCount: 2 } });
    expect(parseSetting(least, 'a')).toEqual({ ok: false, problem: { kind: 'tooFew', limit: 2 } });
  });

  it("is a text within its lengths and allowed values, and a required setting's empty box says so", () => {
    const prefix = settingOf('text', 'prefix');
    expect(parseSetting(prefix, ' EUR ')).toEqual({ ok: true, value: 'EUR' });
    expect(parseSetting(prefix, 'x'.repeat(51))).toEqual({ ok: false, problem: { kind: 'tooLong', limit: 50 } });
    const short = setting({ rules: { ...NO_RULES, minimumLength: 2 } });
    expect(parseSetting(short, 'a')).toEqual({ ok: false, problem: { kind: 'tooShort', limit: 2 } });
    // A character beyond the Basic Multilingual Plane counts twice, as the server and the browser count it.
    const two = setting({ rules: { ...NO_RULES, maximumLength: 1 } });
    expect(parseSetting(two, String.fromCodePoint(0x1f600))).toEqual({
      ok: false,
      problem: { kind: 'tooLong', limit: 1 },
    });
    const allowed = setting({ rules: { ...NO_RULES, allowedValues: ['a', 'b'] } });
    expect(parseSetting(allowed, 'c')).toEqual({ ok: false, problem: { kind: 'notAllowed' } });
    expect(parseSetting(allowed, 'b')).toEqual({ ok: true, value: 'b' });
    const required = setting({ rules: { ...NO_RULES, required: true } });
    expect(parseSetting(required, '  ')).toEqual({ ok: false, problem: { kind: 'required' } });
    expect(parseSetting({ ...required, kind: 'list', editor: 'list', itemKind: 'text' }, '\n')).toEqual({
      ok: false,
      problem: { kind: 'required' },
    });
  });

  it("is a choice list within the setting's counts", () => {
    const parts = setting({
      kind: 'list',
      editor: 'list',
      itemKind: 'choice',
      rules: { ...NO_RULES, minimumCount: 1, maximumCount: 2 },
    });
    expect(parseChoices(parts, ['a'])).toEqual({ ok: true, value: ['a'] });
    expect(parseChoices(parts, ['a', 'b', 'c'])).toEqual({ ok: false, problem: { kind: 'tooMany', limit: 2 } });
    expect(parseChoices(parts, [])).toEqual({ ok: false, problem: { kind: 'tooFew', limit: 1 } });
    expect(parseChoices({ ...parts, rules: { ...parts.rules, minimumCount: null } }, [])).toEqual({
      ok: true,
      value: undefined,
    });
    const schemes = must(
      builtInTypes['url']?.settings.find((each) => each.name === 'schemes'),
      'the schemes',
    );
    expect(parseChoices(schemes, [])).toEqual({ ok: false, problem: { kind: 'tooFew', limit: 1 } });
    expect(parseChoices({ ...parts, rules: { ...parts.rules, required: true } }, [])).toEqual({
      ok: false,
      problem: { kind: 'required' },
    });
    const least = setting({
      kind: 'list',
      editor: 'list',
      itemKind: 'choice',
      rules: { ...NO_RULES, minimumCount: 2 },
    });
    expect(parseChoices(least, ['a'])).toEqual({ ok: false, problem: { kind: 'tooFew', limit: 2 } });
  });
});

describe("a date's limit", () => {
  it('is read as none, a date, a number of units from today, or steps it keeps as they are', () => {
    expect(readDateBound(null)).toEqual({ kind: 'none' });
    expect(readDateBound('2026-12-31')).toEqual({ kind: 'date', date: '2026-12-31' });
    expect(readDateBound('2026-13-01')).toEqual({ kind: 'none' });
    expect(readDateBound({ today: '+2w' })).toEqual({ kind: 'today', count: 2, unit: 'w' });
    expect(readDateBound({ today: '-10d' })).toEqual({ kind: 'today', count: -10, unit: 'd' });
    expect(readDateBound({ today: '+1m-1d' })).toEqual({ kind: 'steps', steps: '+1m-1d' });
    expect(readDateBound({ today: 'soon' })).toEqual({ kind: 'none' });
  });

  it('is written as settings store it', () => {
    expect(writeDateBound({ kind: 'none' })).toBeUndefined();
    expect(writeDateBound({ kind: 'date', date: '2026-12-31' })).toBe('2026-12-31');
    expect(writeDateBound({ kind: 'today', count: 0, unit: 'd' })).toEqual({ today: '+0d' });
    expect(writeDateBound({ kind: 'today', count: -3, unit: 'y' })).toEqual({ today: '-3y' });
    expect(writeDateBound({ kind: 'steps', steps: '+1m-1d' })).toEqual({ today: '+1m-1d' });
  });

  it('knows a real date', () => {
    expect(isDate('2028-02-29')).toBe(true);
    expect(isDate('2026-02-29')).toBe(false);
    expect(isDate('2026-1-01')).toBe(false);
  });
});
