// What the Answers work out before they draw (ROADMAP 11.5b, ADR-0212): the fields nobody answered apart from the
// others, a share of nothing, whether several choices add up past the answers, a ranking's bars from its positions, a
// grid's columns across rows that differ, and the next free-text answers added after those shown.
import { describe, expect, it } from 'vitest';
import type { InsightAnswerOptionResponseModel, InsightAnswerResponseModel } from '../api';
import { addsUp, gridColumns, lineKey, rankBars, shareOf, splitAnswered, withPage } from './answer-model';

const option = (label: string, count: number, more: Partial<InsightAnswerOptionResponseModel> = {}) => ({
  label,
  count,
  older: false,
  other: false,
  ...more,
});

const field = (key: string, answered: number, more: Partial<InsightAnswerResponseModel> = {}) =>
  ({
    key,
    label: key,
    kind: 'options',
    answered,
    removed: false,
    several: false,
    options: [],
    rows: [],
    ticked: 0,
    texts: [],
    ...more,
  }) as InsightAnswerResponseModel;

describe('the answers model', () => {
  it('keeps the fields with answers in their order, and names those nobody answered apart', () => {
    const { answered, unanswered } = splitAnswered([field('a', 3), field('b', 0), field('c', 1), field('d', 0)]);
    expect(answered.map((each) => each.key)).toEqual(['a', 'c']);
    expect(unanswered.map((each) => each.key)).toEqual(['b', 'd']);
  });

  it('takes a share of the whole, and none of nothing', () => {
    expect(shareOf(163, 389)).toBeCloseTo(0.419, 3);
    expect(shareOf(5, 0)).toBe(0);
    expect(shareOf(-2, 10)).toBe(0);
  });

  it('says the shares add up past the answers only where an answer chooses several and they do', () => {
    const options = [option('Coffee', 80), option('Tea', 60)];
    expect(addsUp({ several: true, options, answered: 100 })).toBe(true);
    expect(addsUp({ several: true, options, answered: 140 })).toBe(false);
    expect(addsUp({ several: false, options, answered: 100 })).toBe(false);
  });

  it("draws a ranking's first place as a whole bar and the last as the shortest, and an option without a place as none", () => {
    const bars = rankBars([
      option('Price', 50, { position: 1 }),
      option('Speed', 50, { position: 2.5 }),
      option('Support', 50, { position: 3 }),
      option('Gone', 4, { position: null }),
    ]);
    expect(bars).toEqual({ values: [3, 1.5, 1, 0], whole: 3 });
  });

  it("finds a grid's columns across its rows, an older one and the visitors' own texts after the live ones", () => {
    const columns = gridColumns([
      { key: 'r1', label: 'Food', answered: 40, options: [option('Good', 30), option('Bad', 10)], removed: false },
      {
        key: 'r2',
        label: 'Room',
        answered: 20,
        options: [option('Bad', 15), option('Fair', 3, { older: true }), option('', 2, { other: true })],
        removed: false,
      },
    ]);
    expect(columns.map(lineKey)).toEqual(['live:Good', 'live:Bad', 'older:Fair', 'other']);
    // A live column and an older one of the same label are two columns.
    expect(lineKey(option('Fair', 1))).not.toBe(lineKey(option('Fair', 1, { older: true })));
  });

  it('adds the next answers after those shown, and ends where the page says', () => {
    const text = (entry: string) => ({ entry, received: '2026-11-07T10:00:00Z', text: entry });
    const shown = { texts: [text('e1'), text('e2')], more: 'one' };
    expect(withPage(shown, { texts: [text('e3')], more: 'two' })).toEqual({
      texts: [text('e1'), text('e2'), text('e3')],
      more: 'two',
    });
    expect(withPage(shown, { texts: [], more: null }).more).toBeNull();
  });
});
