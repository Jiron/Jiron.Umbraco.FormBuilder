// An option list as its screens hold it (ROADMAP 12.1, ADR-0214): the kinds in the create dialog's order; the field the
// option editor edits a list as, and the contents back from it; the keep times a kind offers and their words; a list's
// alias from its name; the preview's first options; and where each problem a save answered with shows.
import { describe, expect, it } from 'vitest';
import {
  contentsOf,
  fieldOf,
  firstOf,
  isAlias,
  isKind,
  KEEP_TIMES,
  keepOfNew,
  keepTimes,
  keepUnit,
  aliasOfName,
  KINDS,
  placeOf,
  problemsAt,
  sourceOf,
} from './option-list-model';

describe('an option list as its screens hold it', () => {
  it('offers the seven kinds, fixed options first, and knows no other', () => {
    expect(KINDS).toEqual(['manual', 'content', 'dataType', 'url', 'file', 'countries', 'code']);
    expect(KINDS.every(isKind)).toBe(true);
    expect([isKind('list'), isKind(''), isKind(null), isKind(undefined)]).toEqual([false, false, false, false]);
  });

  it("edits a list's own items, and a source's configuration, as a dropdown field's options", () => {
    const items = [{ value: 'a', label: { '': 'A' } }];
    expect([sourceOf('manual'), sourceOf('file'), sourceOf('url'), sourceOf('code')]).toEqual([
      'items',
      'items',
      'url',
      'code',
    ]);
    expect(fieldOf({ kind: 'manual', config: { ignored: true }, items })).toEqual({
      key: 'jfb-option-list',
      type: 'dropdown',
      options: { source: 'items', items },
    });
    expect(fieldOf({ kind: 'url', config: { url: 'https://example.test/o.json' }, items })).toEqual({
      key: 'jfb-option-list',
      type: 'dropdown',
      options: { source: 'url', config: { url: 'https://example.test/o.json' } },
    });
    // A kind this version lacks is edited as fixed options, so nothing of it is lost.
    expect(fieldOf({ kind: null, config: {}, items }).options).toEqual({ source: 'items', items });
  });

  it('takes the contents back from the field the editor changed, empty where it holds none', () => {
    const items = [{ value: 'b' }];
    expect(contentsOf({ key: 'x', type: 'dropdown', options: { source: 'items', items } })).toEqual({
      items,
      config: {},
    });
    expect(
      contentsOf({ key: 'x', type: 'dropdown', options: { source: 'code', config: { source: 'months' } } }),
    ).toEqual({ items: [], config: { source: 'months' } });
    expect(contentsOf({ key: 'x', type: 'dropdown' })).toEqual({ items: [], config: {} });
  });

  it('keeps a new JSON address an hour, and any other kind not at all', () => {
    expect(KINDS.map(keepOfNew)).toEqual([0, 0, 0, 3600, 0, 0, 0]);
  });

  it("offers the keep times from a kind's shortest on, and a list's own where it is none of them", () => {
    expect(keepTimes(0, 0)).toEqual(KEEP_TIMES);
    expect(keepTimes(60, 3600)).toEqual(KEEP_TIMES.filter((seconds) => seconds >= 60));
    expect(keepTimes(60, 7200)).toEqual([60, 300, 900, 3600, 7200, 21600, 86400, 604800, 2592000]);
    // A time below the kind's shortest is not offered: the save says why.
    expect(keepTimes(60, 30)).toEqual(KEEP_TIMES.filter((seconds) => seconds >= 60));
  });

  it('says a keep time in its largest whole unit', () => {
    expect([0, 45, 60, 300, 3600, 21600, 86400, 604800, 90].map(keepUnit)).toEqual([
      { unit: 'none', count: 0 },
      { unit: 'seconds', count: 45 },
      { unit: 'minutes', count: 1 },
      { unit: 'minutes', count: 5 },
      { unit: 'hours', count: 1 },
      { unit: 'hours', count: 6 },
      { unit: 'days', count: 1 },
      { unit: 'days', count: 7 },
      { unit: 'seconds', count: 90 },
    ]);
  });

  it("makes a list's alias from its name, as a field's is made, and holds aliases to their rule", () => {
    expect(aliasOfName('Opening hours')).toBe('openingHours');
    // An umlaut as its letter, as the test sources stay ASCII.
    expect(aliasOfName(`L${String.fromCharCode(0xe4)}nder der EU`)).toBe('landerDerEu');
    expect(aliasOfName('2024 sizes')).toMatch(/^[a-z]/);
    expect(aliasOfName('!!!')).toBe('list');
    expect(['sizes', 'openingHours2', 'a'].every(isAlias)).toBe(true);
    expect(['Sizes', '2sizes', 'opening-hours', '', `a${'b'.repeat(64)}`].some(isAlias)).toBe(false);
  });

  it('shows the first options and counts the rest', () => {
    const options = Array.from({ length: 11 }, (_, at) => at);
    expect(firstOf(options, 8)).toEqual({ first: [0, 1, 2, 3, 4, 5, 6, 7], more: 3 });
    expect(firstOf([1, 2], 8)).toEqual({ first: [1, 2], more: 0 });
  });

  it("puts each problem where its path says: the header's name or alias, the kind's box, the keep time or a line", () => {
    expect(
      ['$.name', '$.alias', '$.cacheSeconds', '$.file', '$.config.url', '$.items[2].value', '$.kind', '$'].map(placeOf),
    ).toEqual(['name', 'alias', 'keep', 'file', 'contents', 'contents', 'other', 'other']);
    const problems = [
      { code: 'a', path: '$.items[0].value', message: 'first' },
      { code: 'b', path: '$.name', message: 'name' },
      { code: 'c', path: '$.config.url', message: 'second' },
    ];
    expect(problemsAt(problems, 'contents').map((problem) => problem.message)).toEqual(['first', 'second']);
    expect(problemsAt(problems, 'keep')).toEqual([]);
  });
});
