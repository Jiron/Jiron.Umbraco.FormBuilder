// Formulas an editor might type, and many no editor would (ROADMAP 5.3a, ADR-0063), the same as Core's
// FormulaFuzzTests: 10,000 random formulas, built from the grammar and then broken by chance, are read, checked and
// worked out over random answers. Each gives its result or refuses with a LogicError, nothing else, and each takes at
// most 50 ms: the fastest of three runs, so that a garbage collection or the first compilation of a path does not count
// against the formula.
import { describe, expect, it } from 'vitest';
import {
  evaluateFormula,
  LogicError,
  MAX_FORMULA_DEPTH,
  MAX_FORMULA_LENGTH,
  type FormulaScope,
  type Kind,
} from '../src/index.ts';
import { today } from '../src/dates.ts';
import { Random, show } from './fuzzing.ts';

const FORMULAS = 10_000;
const SEED = 5301;
const RUNS = 3;
const BUDGET_MS = 50;

const kinds: Readonly<Record<string, Kind>> = {
  qty: 'number',
  price: 'number',
  size: 'choice',
  extras: 'choices',
  gift: 'checkbox',
  arrival: 'date',
  birth: 'dateTime',
  name: 'text',
  photos: 'files',
  start: 'time',
};
const siteToday = today('2026-10-26T10:00:00Z', 'Europe/Zurich');

/** What a formula can come to: a value, none, or each mistake and failure. */
const outcomesToReach = [
  'value',
  'no value',
  'invalid_formula',
  'unknown_operator',
  'invalid_operand',
  'too_deep',
  'out_of_range',
  'unknown_field',
  'operator_not_for_kind',
  'constant_formula',
  'division_by_zero',
];

// Mostly the fields arithmetic takes; now and then any field, or none the form has.
const amounts = ['qty', 'qty', 'price', 'price', 'size', 'extras', 'gift'];
const aliases = [...Object.keys(kinds), 'unknown', 'min', 'Qty'];
const numbers = ['0', '1', '2', '3', '10', '12', '0.5', '2.5', '99.99', '0.001'];
const oddNumbers = ['00', '1.', '.5', '1e5', '007', '1,5'];
const functions = ['min', 'max', 'round', 'if', 'and', 'or', 'not', 'daysBetween', 'yearsBetween'];
const oddFunctions = ['today', 'pow', 'Round', 'sum', 'constructor'];
const operators = ['+', '-', '*', '/', '=', '!=', '<', '<=', '>', '>='];
const places = ['0', '0', '1', '2', '2', '3', '20', '21', '1.5', '99999999999'];
const spaces = [' ', ' ', ' ', '  ', '\t', '\n', '\r\n'];

// What a mistake puts into a formula: the language's own characters, and some it does not have.
const noise = [
  ...'(),.+-*/=!<> 09aZ_$"\'%^&|',
  String.fromCharCode(0xe9),
  String.fromCharCode(0x2212),
  String.fromCharCode(0xff11),
  String.fromCharCode(0xd83d, 0xde00),
  String.fromCharCode(0xd800),
  String.fromCharCode(0),
];

const answerChoices: readonly (readonly string[])[] = [
  [],
  ['0'],
  ['1'],
  ['2.5'],
  ['-3'],
  ['12', '7'],
  ['0.333'],
  ['abc'],
  ['1e5'],
  [' 4 '],
  ['2026-10-26'],
  ['2024-02-29'],
  ['2026-10-26T23:30'],
  ['2026-13-01'],
  ['true'],
  ['false'],
  ['9'.repeat(60)],
  ['1' + '0'.repeat(59)],
  ['0.' + '0'.repeat(30) + '1'],
];

/** Random formulas from the grammar of ADR-0063, broken now and then, and random answers to them. */
class FormulaGenerator {
  private readonly random: Random;
  private budget = 0;

  constructor(seed: number) {
    this.random = new Random(seed);
  }

  formula(): string {
    const r = this.random;
    const kind = r.next(50);
    // About the longest formula there is, a little shorter or longer than the limit; or nested as deep as the limit,
    // or one level deeper.
    let text = kind === 0 ? this.long() : kind === 1 ? this.nested(MAX_FORMULA_DEPTH + r.between(-1, 2)) : this.built();

    // Then, now and then, broken: characters dropped, put in or doubled.
    const mistakes = r.next(6) === 0 ? r.between(1, 4) : 0;
    for (let m = 0; m < mistakes; m++) text = this.break(text);
    return text;
  }

  answers(): Map<string, readonly string[] | undefined> {
    // A hidden field has no answers at all.
    return new Map(
      Object.keys(kinds).map((alias) => [
        alias,
        this.random.next(8) === 0 ? undefined : this.random.pick(answerChoices),
      ]),
    );
  }

  private built(): string {
    this.budget = this.random.between(1, 120);
    return this.expression(0);
  }

  private long(): string {
    const terms = Math.floor(MAX_FORMULA_LENGTH / 6) + this.random.between(-2, 3);
    return Array.from({ length: terms }, () => 'qty').join(' + ') + ' '.repeat(this.random.next(4));
  }

  private nested(levels: number): string {
    let open = '';
    for (let level = 0; level < levels; level++) open += this.random.next(3) === 0 ? 'not(' : '(';
    return open + 'qty' + ')'.repeat(levels);
  }

  private expression(depth: number): string {
    const r = this.random;
    if (--this.budget <= 0 || depth > MAX_FORMULA_DEPTH + 2) return this.leaf();
    switch (r.next(10)) {
      case 0:
      case 1:
      case 2:
        return this.leaf();
      case 3:
      case 4:
        return this.expression(depth) + this.space() + r.pick(operators) + this.space() + this.expression(depth);
      case 5:
        return this.expression(depth) + this.space() + '/' + this.space() + this.expression(depth);
      case 6:
        return '(' + this.space() + this.expression(depth + 1) + this.space() + ')';
      case 7:
        return '-'.repeat(r.between(1, 4)) + this.expression(depth);
      default:
        return this.call(depth);
    }
  }

  private call(depth: number): string {
    const r = this.random;
    const name = r.next(20) === 0 ? r.pick(oddFunctions) : r.pick(functions);
    // Now and then the wrong number of arguments.
    const wrong = r.next(15) === 0;
    let count: number;
    switch (name) {
      case 'today':
        count = wrong ? 1 : 0;
        break;
      case 'not':
        count = wrong ? 2 : 1;
        break;
      case 'if':
        count = wrong ? 2 : 3;
        break;
      case 'daysBetween':
      case 'yearsBetween':
        count = wrong ? 1 : 2;
        break;
      case 'round':
        count = wrong ? 3 : r.between(1, 3);
        break;
      default:
        count = wrong ? r.between(0, 2) : r.between(2, 5);
    }
    const args: string[] = [];
    for (let i = 0; i < count; i++) {
      if (name === 'daysBetween' || name === 'yearsBetween') {
        const pick = r.next(12);
        args.push(
          pick < 3
            ? 'today()'
            : pick < 7
              ? 'arrival'
              : pick < 10
                ? 'birth'
                : pick === 10
                  ? r.pick(aliases)
                  : this.expression(depth + 1),
        );
      } else if (name === 'round' && i === 1) {
        args.push(r.next(8) === 0 ? this.expression(depth + 1) : r.pick(places));
      } else {
        args.push(this.expression(depth + 1));
      }
    }
    return name + '(' + this.space() + args.join(',' + this.space()) + this.space() + ')';
  }

  private leaf(): string {
    const r = this.random;
    const pick = r.next(40);
    if (pick < 14) return r.pick(numbers);
    if (pick === 14) return r.pick(oddNumbers);
    if (pick < 17) return r.next(2) === 0 ? '1' + '0'.repeat(r.between(55, 62)) : '0.' + '3'.repeat(r.between(1, 70));
    if (pick === 17) return 'today()';
    return pick < 22 ? r.pick(aliases) : r.pick(amounts);
  }

  private space(): string {
    return this.random.next(3) === 0 ? this.random.pick(spaces) : '';
  }

  private break(text: string): string {
    const r = this.random;
    const at = r.next(text.length + 1);
    switch (r.next(3)) {
      case 0:
        if (text.length > 0) {
          const from = Math.min(at, text.length - 1);
          return text.slice(0, from) + text.slice(from + 1);
        }
        return text + r.pick(noise);
      case 1:
        return text.slice(0, at) + r.pick(noise) + text.slice(at);
      default:
        return text.slice(0, at) + text.slice(at, Math.min(text.length, at + r.between(1, 8))) + text.slice(at);
    }
  }
}

const scopeOf = (answers: ReadonlyMap<string, readonly string[] | undefined>): FormulaScope => ({
  kind: (alias) => (Object.hasOwn(kinds, alias) ? kinds[alias] : undefined),
  answers: (alias) => answers.get(alias),
  today: () => siteToday,
});

describe('random formulas', () => {
  it('10,000 give a result or a mistake, each in at most 50 ms', () => {
    const generator = new FormulaGenerator(SEED);
    const outcomes = new Map<string, number>();
    const failures: string[] = [];
    let slowest = { ms: 0, formula: '' };
    for (let i = 0; i < FORMULAS; i++) {
      const formula = generator.formula();
      const scope = scopeOf(generator.answers());
      let fastest = Number.POSITIVE_INFINITY;
      let outcome = '';
      for (let run = 0; run < RUNS; run++) {
        const start = performance.now();
        try {
          outcome = evaluateFormula(formula, scope) === null ? 'no value' : 'value';
        } catch (error) {
          if (!(error instanceof LogicError)) {
            failures.push(`formula ${i}: ${String(error)} for ${show(formula)}`);
            break;
          }
          outcome = error.code;
        }
        fastest = Math.min(fastest, performance.now() - start);
      }
      outcomes.set(outcome, (outcomes.get(outcome) ?? 0) + 1);
      if (fastest > BUDGET_MS && Number.isFinite(fastest))
        failures.push(`formula ${i}: ${fastest.toFixed(1)} ms for ${show(formula)}`);
      if (Number.isFinite(fastest) && fastest > slowest.ms) slowest = { ms: fastest, formula };
    }

    console.log(
      [...outcomes]
        .sort(([a], [b]) => (a < b ? -1 : 1))
        .map(([code, count]) => `${code} ${count}`)
        .join(', '),
    );
    console.log(`slowest: ${slowest.ms.toFixed(3)} ms for ${show(slowest.formula)}`);
    expect(failures).toEqual([]);
    // The formulas reach every outcome, so the generator explores the language and not one corner of it.
    expect(
      outcomesToReach.filter((code) => (outcomes.get(code) ?? 0) < FORMULAS / 500),
      'outcomes reached fewer than 20 times',
    ).toEqual([]);
  });

  it('the generator gives the same formulas for the same seed', () => {
    const first = new FormulaGenerator(SEED);
    const second = new FormulaGenerator(SEED);
    const take = (generator: FormulaGenerator): string[] => Array.from({ length: 100 }, () => generator.formula());
    expect(take(first)).toEqual(take(second));
  });
});
