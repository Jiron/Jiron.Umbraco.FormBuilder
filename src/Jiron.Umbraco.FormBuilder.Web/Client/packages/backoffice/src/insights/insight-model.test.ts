// What the insights work out before they draw (ROADMAP 11.5, IA 5.5, ADR-0211): each range's span and the span it
// compares with, a span the server reads, a change in words, points of a share, the time to complete by its bucket,
// the words of each kind of error, and the bars.
import { describe, expect, it } from 'vitest';
import {
  axisOf,
  changeOf,
  compareOf,
  completionOf,
  dayOf,
  daysIn,
  isSpan,
  kindKey,
  mostAt,
  pointsOf,
  sharesOf,
  spanOf,
  utcToday,
} from './insight-model';

const today = new Date(2026, 10, 7, 9, 30);

describe('a date range', () => {
  it('ends today, or covers last month whole', () => {
    expect(spanOf('last7', today)).toEqual({ from: '2026-11-01', to: '2026-11-07' });
    expect(spanOf('last30', today)).toEqual({ from: '2026-10-09', to: '2026-11-07' });
    expect(spanOf('thisMonth', today)).toEqual({ from: '2026-11-01', to: '2026-11-07' });
    expect(spanOf('lastMonth', today)).toEqual({ from: '2026-10-01', to: '2026-10-31' });
    expect(spanOf('thisYear', today)).toEqual({ from: '2026-01-01', to: '2026-11-07' });
    expect(spanOf('lastMonth', new Date(2026, 0, 15)), 'across a year').toEqual({
      from: '2025-12-01',
      to: '2025-12-31',
    });
  });

  it('ends on the UTC day the stats count, wherever the user is', () => {
    expect(dayOf(utcToday(new Date(Date.UTC(2026, 10, 8, 1)))), 'one in the morning, UTC').toBe('2026-11-08');
    expect(dayOf(utcToday(new Date(Date.UTC(2026, 10, 7, 23, 59)))), 'a minute before midnight, UTC').toBe(
      '2026-11-07',
    );
    expect(spanOf('last7', utcToday(new Date(Date.UTC(2026, 10, 8, 1))))).toEqual({
      from: '2026-11-02',
      to: '2026-11-08',
    });
  });

  it('compares a calendar range with the same days of the one before, and a rolling one with the span just before', () => {
    expect(compareOf('thisMonth', spanOf('thisMonth', today))).toEqual({ from: '2026-10-01', to: '2026-10-07' });
    expect(compareOf('lastMonth', spanOf('lastMonth', today))).toEqual({ from: '2026-09-01', to: '2026-09-30' });
    expect(compareOf('thisYear', spanOf('thisYear', today))).toEqual({ from: '2025-01-01', to: '2025-11-07' });
    expect(compareOf('last7', spanOf('last7', today))).toEqual({ from: '2026-10-25', to: '2026-10-31' });
    expect(compareOf('custom', { from: '2026-02-27', to: '2026-03-02' })).toEqual({
      from: '2026-02-23',
      to: '2026-02-26',
    });
    expect(
      compareOf('thisMonth', { from: '2026-03-01', to: '2026-03-31' }),
      'the 31st against a shorter month',
    ).toEqual({
      from: '2026-02-01',
      to: '2026-02-28',
    });
    expect(compareOf('thisYear', { from: '2028-01-01', to: '2028-02-29' }), 'a leap day').toEqual({
      from: '2027-01-01',
      to: '2027-02-28',
    });
  });

  it('is one the server reads only with real days, in order, at most a year apart', () => {
    expect(isSpan({ from: '2026-11-07', to: '2026-11-07' })).toBe(true);
    expect(isSpan({ from: '2025-11-07', to: '2026-11-07' })).toBe(true);
    expect(daysIn({ from: '2025-11-07', to: '2026-11-07' })).toBe(366);
    expect(isSpan({ from: '2025-11-06', to: '2026-11-07' })).toBe(false);
    expect(isSpan({ from: '2026-11-08', to: '2026-11-07' })).toBe(false);
    expect(isSpan({ from: '2026-02-30', to: '2026-03-01' })).toBe(false);
    expect(isSpan({ from: '', to: '2026-03-01' })).toBe(false);
    expect(dayOf(new Date(2026, 2, 29, 0, 30))).toBe('2026-03-29');
  });
});

describe('a change', () => {
  it('is said as a whole percent of the number before, the same, or up from none', () => {
    expect(changeOf(4812, 4540)).toEqual({ kind: 'up', percent: 6 });
    expect(changeOf(300, 400)).toEqual({ kind: 'down', percent: 25 });
    expect(changeOf(7, 7)).toEqual({ kind: 'same' });
    expect(changeOf(0, 0)).toEqual({ kind: 'same' });
    expect(changeOf(5, 0)).toEqual({ kind: 'fromNone' });
    expect(changeOf(0, 5)).toEqual({ kind: 'down', percent: 100 });
  });

  it('of a share is said in points with one decimal', () => {
    expect(pointsOf(0.081, 0.077)).toBe(0.4);
    expect(pointsOf(0.05, 0.075)).toBe(2.5);
  });
});

describe('the time to complete', () => {
  it('is its bucket in seconds, minutes, an hour or longer', () => {
    expect(completionOf('15')).toEqual({ unit: 'seconds', count: 15 });
    expect(completionOf('60')).toEqual({ unit: 'minutes', count: 1 });
    expect(completionOf('180')).toEqual({ unit: 'minutes', count: 3 });
    expect(completionOf('3600')).toEqual({ unit: 'hour' });
    expect(completionOf('more')).toEqual({ unit: 'longer' });
    expect(completionOf('soon')).toBeUndefined();
  });
});

describe('the words and the bars', () => {
  it('name each kind of error the server knows, and another reason for any other', () => {
    expect(kindKey('required')).toBe('kind_required');
    expect(kindKey('rule')).toBe('kind_rule');
    expect(kindKey('server'), 'a kept error of the server counts nothing').toBe('kind_other');
    expect(kindKey('mood')).toBe('kind_other');
  });

  it('draw each value as its share of the most, mark the most, and label the first, middle and last', () => {
    expect(sharesOf([2, 4, 0])).toEqual([0.5, 1, 0]);
    expect(sharesOf([0, 0])).toEqual([0, 0]);
    expect(mostAt([3, 9, 9, 1])).toBe(1);
    expect(mostAt([0, 0])).toBe(-1);
    expect(axisOf(['a', 'b', 'c', 'd', 'e'])).toEqual(['a', 'c', 'e']);
    expect(axisOf(['a', 'b'])).toEqual(['a', 'b']);
  });
});
