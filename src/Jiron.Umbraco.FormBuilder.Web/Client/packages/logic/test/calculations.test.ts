// The calculation language as the form engine takes it, and the texts a result and a slider's value show as (ROADMAP
// 6.4f, ADR-0114). The vectors hold the language and the answers' display texts to Core's; these hold the parts the
// page uses on their own: which forms need the language, a form engine without it, and the texts outside a template.
import { describe, expect, it } from 'vitest';
import messages from '../../../../../../tests/vectors/messages.json';
import { FORMULAS } from '../src/calc.ts';
import {
  calculationText,
  evaluateForm,
  hasCalculations,
  LogicError,
  sliderText,
  type Context,
  type CultureFormats,
  type LogicField,
  type LogicForm,
} from '../src/index.ts';

const ENGLISH = messages.defaults.cultures['en-US'] as CultureFormats;
const SWISS = messages.defaults.cultures['de-CH'] as CultureFormats;

const CONTEXT: Context = {
  now: '2026-10-30T09:00:00Z',
  timeZone: 'Europe/Zurich',
  culture: 'en-US',
  member: { signedIn: false, groups: [] },
  query: {},
  hidden: [],
};

const COUNT: LogicField = { id: 'count', kind: 'number', alias: 'count' };
const TOTAL: LogicField = { id: 'total', kind: 'number', alias: 'total', calculation: { formula: 'count * 2' } };
const formOf = (...fields: LogicField[]): LogicForm => ({ pages: [{ id: 'p', groups: [{ id: 'g', fields }] }] });

describe('the form engine', () => {
  it('knows a form with a calculation, one in a repeatable group too, from one without', () => {
    expect(hasCalculations(formOf(COUNT))).toBe(false);
    expect(hasCalculations(formOf(COUNT, TOTAL))).toBe(true);
    expect(hasCalculations(formOf({ id: 'rows', fields: [COUNT, TOTAL] }))).toBe(true);
    expect(hasCalculations({ pages: [{ id: 'p' }] })).toBe(false);
  });

  it('decides a form without a calculation without the language', () => {
    expect(evaluateForm(formOf(COUNT), { values: { count: ['3'] } }, CONTEXT, null).calculations).toEqual({});
  });

  it("works a form's calculations out in the language it is given", () => {
    expect(evaluateForm(formOf(COUNT, TOTAL), { values: { count: ['3'] } }, CONTEXT, FORMULAS).calculations).toEqual({
      total: '6',
    });
  });

  it("calls a form with a calculation and no language the caller's mistake, not a refusal of the form", () => {
    const evaluate = () => evaluateForm(formOf(COUNT, TOTAL), {}, CONTEXT, null);
    expect(evaluate).toThrow('A form with a calculation needs the calculation language.');
    expect(evaluate).not.toThrow(LogicError);
  });
});

describe("a calculation's result", () => {
  it("shows in the culture's format with two places, unless its format sets others, from 0 to 15", () => {
    expect(calculationText({}, '1234.5', ENGLISH)).toBe('1,234.50');
    expect(calculationText({ format: { decimals: 0 } }, '1234.5', ENGLISH)).toBe('1,235');
    expect(calculationText({ format: { decimals: 20 } }, '0.5', ENGLISH)).toBe('0.' + '5'.padEnd(15, '0'));
    expect(calculationText({ format: { decimals: -1 } }, '2.5', ENGLISH)).toBe('3');
    expect(calculationText({ format: { decimals: '1' } }, '2.5', ENGLISH)).toBe('2.50');
    expect(calculationText({ format: { decimals: 2 } }, '1234.5', SWISS)).toBe(
      '1' + String.fromCharCode(0x2019) + '234.50',
    );
  });

  it('stands between its prefix and its suffix, each apart by a space and trimmed', () => {
    expect(calculationText({ format: { prefix: ' EUR ', suffix: 'a day' } }, '45', ENGLISH)).toBe('EUR 45.00 a day');
    expect(calculationText({ format: { suffix: '%', decimals: 0 } }, '12', ENGLISH)).toBe('12 %');
    expect(calculationText({ format: { prefix: '', suffix: 7 } }, '1', ENGLISH)).toBe('1.00');
    expect(calculationText(null, '1', ENGLISH)).toBe('1.00');
  });
});

describe("a slider's value", () => {
  it('shows as the culture writes the number, then the unit; the number alone without one', () => {
    expect(sliderText({ unit: 'hours' }, '2.5', ENGLISH)).toBe('2.5 hours');
    expect(sliderText({ unit: '  ' }, '1500', ENGLISH)).toBe('1,500');
    expect(sliderText(undefined, '3', ENGLISH)).toBe('3');
  });
});
