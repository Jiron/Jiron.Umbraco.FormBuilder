// The culture formats as the server's system writes them (ROADMAP 26.1, ADR-0263): Windows' ICU writes the American time
// with an ordinary space before AM and PM, Linux's with a narrow no-break space (U+202F), and the browser's runtime gets
// whichever its server sends. Each prints as written; nothing reads a visitor's typing with these patterns.
import { describe, expect, it } from 'vitest';
import { formatCalendar, type CultureFormats } from '../src/index.ts';

const NARROW = String.fromCharCode(0x202f);

// An English culture's formats, with the time pattern given.
function american(time: string): CultureFormats {
  return {
    dates: {
      short: 'M\\/d\\/yyyy',
      medium: 'MMM d, yyyy',
      long: 'dddd, MMMM d, yyyy',
      time,
      dateTime: `M\\/d\\/yyyy ${time}`,
    },
    months: [
      'January',
      'February',
      'March',
      'April',
      'May',
      'June',
      'July',
      'August',
      'September',
      'October',
      'November',
      'December',
    ],
    monthsShort: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
    monthsGenitive: [
      'January',
      'February',
      'March',
      'April',
      'May',
      'June',
      'July',
      'August',
      'September',
      'October',
      'November',
      'December',
    ],
    monthsShortGenitive: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
    days: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
    daysShort: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'],
    am: 'AM',
    pm: 'PM',
    minus: '-',
    number: { decimal: '.', group: ',', sizes: [3], negative: '-n' },
    currency: { code: 'USD', symbol: '$', decimal: '.', group: ',', sizes: [3], positive: '$n', negative: '-$n' },
  };
}

describe('a time pattern with either space before its designator', () => {
  it.each([
    ['an ordinary space', ' '],
    ['a narrow no-break space', NARROW],
  ])('prints with %s, as the server wrote it', (_, space) => {
    const culture = american(`h\\:mm${space}tt`);

    expect(formatCalendar('09:05', 'time', culture)).toBe(`9:05${space}AM`);
    expect(formatCalendar('17:45:59', 'time', culture)).toBe(`5:45${space}PM`);
    expect(formatCalendar('2026-03-07T00:30', 'dateTime', culture)).toBe(`3/7/2026 12:30${space}AM`);
  });

  it('reads the wire form only, whatever the culture writes', () => {
    for (const space of [' ', NARROW]) {
      const culture = american(`h\\:mm${space}tt`);
      expect(formatCalendar(`9:05${space}AM`, 'time', culture)).toBeNull();
      expect(formatCalendar('09:05', 'iso', culture)).toBe('09:05:00');
    }
  });
});
