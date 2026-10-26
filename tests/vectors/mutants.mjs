#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The mutation check of the shared logic (ROADMAP 5.2a, ADR-0061), and since 5.3a of the calculations (ADR-0063).
 * Each mutant changes one piece of code in one runtime; that runtime's vector runner runs, and the file is put back. A
 * mutant that no vector catches survives, and the check fails unless the mutant is written down here as an equivalent
 * change, with the reason. A mutant written down as equivalent that a vector catches fails the check too, so the notes
 * stay true.
 *
 *   node tests/vectors/mutants.mjs                run every mutant (a TypeScript one takes seconds, a C# one a build)
 *   node tests/vectors/mutants.mjs --check        only check that each mutant's code is there exactly once
 *   node tests/vectors/mutants.mjs --runtime ts   only one runtime's mutants: ts or cs
 *   node tests/vectors/mutants.mjs --name fold    only the mutants whose name contains the text
 *
 * Every file is put back however the run ends, Ctrl+C included. A run that outlasts its time is stopped with its whole
 * process tree and counts as caught: the mutant made the logic hang. After a C# run the build output holds the last
 * mutant's build until the next build.
 */
import { spawn, spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const LOGIC = 'src/Jiron.Umbraco.FormBuilder.Web/Client/packages/logic/';
const CORE = 'src/Jiron.Umbraco.FormBuilder.Core/Logic/';
const VITEST = join(ROOT, 'src/Jiron.Umbraco.FormBuilder.Web/Client/node_modules/vitest/vitest.mjs');
const UNIT = 'tests/Jiron.Umbraco.FormBuilder.Tests.Unit/Jiron.Umbraco.FormBuilder.Tests.Unit.csproj';
const NL = String.fromCharCode(10);
const lines = (...parts) => parts.join(NL);

/** The tests each runtime runs for a mutant: its vector runner, unless the mutant names more. */
const VECTORS_TS = ['test/vectors.test.ts'];
const VECTORS_CS = 'FullyQualifiedName~VectorTests';
const TIME = { ts: 120_000, cs: 900_000 };

const ts = (name, file, from, to, more = {}) => ({ runtime: 'ts', name, file: LOGIC + 'src/' + file, from, to, ...more });
const cs = (name, file, from, to, more = {}) => ({ runtime: 'cs', name, file: CORE + file, from, to, ...more });
const negated = (op, body) => [
  ts(`op-${op}`, 'rules.ts', `    case '${op}':${NL}      return ${body}`, `    case '${op}':${NL}      return !${body}`),
];

const mutants = [
  // ---- Every operator, flipped (ROADMAP 5.2a: each needs a failing vector) ----
  ts('op-equals', 'rules.ts',
    lines("    case 'equals':", "    case 'isOneOf':", '      return anyEqual();'),
    lines("    case 'equals':", '      return !anyEqual();', "    case 'isOneOf':", '      return anyEqual();')),
  ts('op-isOneOf', 'rules.ts',
    lines("    case 'equals':", "    case 'isOneOf':", '      return anyEqual();'),
    lines("    case 'equals':", '      return anyEqual();', "    case 'isOneOf':", '      return !anyEqual();')),
  ts('op-notEquals', 'rules.ts',
    lines("    case 'notEquals':", "    case 'isNoneOf':", '      return !anyEqual();'),
    lines("    case 'notEquals':", '      return anyEqual();', "    case 'isNoneOf':", '      return !anyEqual();')),
  ts('op-isNoneOf', 'rules.ts',
    lines("    case 'notEquals':", "    case 'isNoneOf':", '      return !anyEqual();'),
    lines("    case 'notEquals':", '      return !anyEqual();', "    case 'isNoneOf':", '      return anyEqual();')),
  ts('op-isEmpty', 'rules.ts', '      return values.length === 0;', '      return values.length !== 0;'),
  ts('op-isNotEmpty', 'rules.ts', '      return values.length > 0;', '      return values.length <= 0;'),
  ts('op-countAtLeast', 'rules.ts', '      return values.length >= Number(value);', '      return values.length < Number(value);'),
  ts('op-countAtMost', 'rules.ts', '      return values.length <= Number(value);', '      return values.length > Number(value);'),
  ...negated('contains', 'anyText('),
  ts('op-notContains', 'rules.ts', `    case 'notContains':${NL}      return !anyText(`, `    case 'notContains':${NL}      return anyText(`),
  ...negated('startsWith', 'anyText('),
  ...negated('endsWith', 'anyText('),
  ts('op-matches', 'rules.ts', '      return values.some((v) => pattern.test(v));', '      return !values.some((v) => pattern.test(v));'),
  ts('op-greaterThan', 'rules.ts', '      return compareFirst(kind, values, operands(), (n) => n > 0);', '      return compareFirst(kind, values, operands(), (n) => n <= 0);'),
  ts('op-lessThan', 'rules.ts', '      return compareFirst(kind, values, operands(), (n) => n < 0);', '      return compareFirst(kind, values, operands(), (n) => n >= 0);'),
  ts('op-between', 'rules.ts',
    lines('      return (', '        compareFirst(kind, values, resolve(low, scope), (n) => n >= 0) &&'),
    lines('      return !(', '        compareFirst(kind, values, resolve(low, scope), (n) => n >= 0) &&')),
  ...negated('isChecked', 'isChecked('),
  ts('op-isNotChecked', 'rules.ts', `    case 'isNotChecked':${NL}      return !isChecked(`, `    case 'isNotChecked':${NL}      return isChecked(`),

  // ---- What the check of a condition refuses ----
  ts('kinds-ordered-take-text', 'rules.ts', "const ORDERED: readonly Kind[] = ['number', 'date', 'time', 'dateTime'];", "const ORDERED: readonly Kind[] = ['number', 'date', 'time', 'dateTime', 'text'];"),
  ts('kinds-text-only-take-choice', 'rules.ts', "const TEXT: readonly Kind[] = ['text'];", "const TEXT: readonly Kind[] = ['text', 'choice'];"),
  ts('kinds-checkable-take-text', 'rules.ts', "const CHECKABLE: readonly Kind[] = ['checkbox', 'choice', 'choices'];", "const CHECKABLE: readonly Kind[] = ['checkbox', 'choice', 'choices', 'text'];"),
  ts('kinds-unchecked', 'rules.ts', "  if (!operator.kinds.includes(kind)) throw new LogicError('operator_not_for_kind');" + NL, ''),
  ts('count-leading-zero', 'rules.ts', 'if (text.length < 1 || text.length > 6 || (text.length > 1 && text.charCodeAt(0) === 48)) return false;', 'if (text.length < 1 || text.length > 6) return false;'),
  ts('count-seven-digits', 'rules.ts', 'if (text.length < 1 || text.length > 6 ||', 'if (text.length < 1 || text.length > 7 ||'),
  ts('depth-one-more', 'rules.ts', "  if (depth > MAX_DEPTH) throw new LogicError('too_deep');", "  if (depth > MAX_DEPTH + 1) throw new LogicError('too_deep');"),
  ts('match-any-as-all', 'rules.ts', "  return group.match === 'all' ? group.conditions.every(test) : group.conditions.some(test);", '  return group.conditions.every(test);'),
  ts('context-hidden-ignored', 'rules.ts', '  answers: (field) => (subject.context.hidden.includes(field) ? undefined : own(subject.values, field)),', '  answers: (field) => own(subject.values, field),'),

  // ---- The text module ----
  ts('text-trim-bom', 'text.ts', '0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff,', '0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000,'),
  ts('text-trim-end', 'text.ts', '  while (end > start && TRIMMED.has(text.charCodeAt(end - 1))) end--;' + NL, ''),
  ts('text-nfc', 'text.ts', "  return replaceInvalid(value).normalize('NFC');", '  return replaceInvalid(value);'),
  ts('text-noncharacter-kept', 'text.ts', '      out += isHigh(unit) || isLow(unit) || isNoncharacter(unit) ? REPLACEMENT : value.charAt(i);', '      out += isHigh(unit) || isLow(unit) ? REPLACEMENT : value.charAt(i);'),
  ts('text-astral-noncharacter-kept', 'text.ts', '      out += isNoncharacter(codePoint) ? REPLACEMENT : value.slice(i, i + 2);', '      out += value.slice(i, i + 2);'),
  ts('text-noncharacter-range', 'text.ts', '(codePoint >= 0xfdd0 && codePoint <= 0xfdef)', '(codePoint >= 0xfdd0 && codePoint <= 0xfdd0)'),
  ts('text-lone-surrogate-kept', 'text.ts', '      out += isHigh(unit) || isLow(unit) || isNoncharacter(unit) ? REPLACEMENT : value.charAt(i);', '      out += isNoncharacter(unit) ? REPLACEMENT : value.charAt(i);', {
    tests: [...VECTORS_TS, 'test/text.test.ts'],
    why: "no vector can hold a lone surrogate: .NET's JSON reader refuses one, so text.test.ts covers it",
  }),
  ts('fold-greek', 'text.ts', '  [0x0370, 0x03ff],' + NL, ''),
  ts('fold-cyrillic', 'text.ts', '  [0x0400, 0x052f],' + NL, ''),
  ts('fold-armenian', 'text.ts', '  [0x0530, 0x058f],' + NL, ''),
  ts('fold-latin-extended-additional', 'text.ts', '  [0x1e00, 0x1eff],' + NL, ''),
  ts('fold-greek-extended', 'text.ts', '  [0x1f00, 0x1fff],' + NL, ''),
  ts('fold-full-width-capitals', 'text.ts', '  [0xff21, 0xff3a],' + NL, ''),
  ts('fold-full-width-small', 'text.ts', '  [0xff41, 0xff5a],' + NL, '', {
    equivalent: 'the block holds only small letters, which fold to themselves inside it or out',
  }),
  ts('fold-deseret', 'text.ts', '  [0xff41, 0xff5a],' + NL, '  [0xff41, 0xff5a],' + NL + '  [0x10400, 0x1044f],' + NL),
  ts('fold-dotted-capital-i', 'text.ts', '} else if (codePoint === DOTTED_CAPITAL_I || codePoint === DOTLESS_SMALL_I) {', '} else if (codePoint === DOTLESS_SMALL_I) {'),
  ts('fold-dotless-small-i', 'text.ts', '} else if (codePoint === DOTTED_CAPITAL_I || codePoint === DOTLESS_SMALL_I) {', '} else if (codePoint === DOTTED_CAPITAL_I) {', {
    equivalent: "JavaScript's simple mappings take the dotless i through I to i anyway; the case keeps the rule explicit",
  }),
  ts('fold-full-uppercase', 'text.ts', '      const upper = single(ch.toUpperCase(), ch);', '      const upper = ch.toUpperCase();'),
  ts('fold-lowercase-only', 'text.ts', '      const upper = single(ch.toUpperCase(), ch);', '      const upper = ch;'),
  ts('pattern-not-normalised', 'pattern.ts', '  const points = codePoints(normalise(pattern));', '  const points = codePoints(pattern);'),

  // ---- own(): data read from JSON answers only for its own keys (ADR-0030) ----
  ts('own-inherited', 'errors.ts', '  return Object.hasOwn(record, key) ? record[key] : undefined;', '  return record[key];'),

  // ---- The decimal module ----
  ts('decimal-compare-sign', 'decimal.ts', '  if (x.negative !== y.negative) return x.negative ? -1 : 1;', '  if (x.negative !== y.negative) return x.negative ? 1 : -1;'),
  ts('decimal-compare-length', 'decimal.ts', '  let magnitude = Math.sign(x.integer.length - y.integer.length);', '  let magnitude = 0;'),
  ts('decimal-compare-integer', 'decimal.ts', '  if (magnitude === 0) magnitude = order(x.integer, y.integer);', '  if (magnitude === 0) magnitude = order(y.integer, x.integer);'),
  ts('decimal-compare-fraction', 'decimal.ts', '  if (magnitude === 0) magnitude = order(x.fraction, y.fraction);', '  if (magnitude === 0) magnitude = Math.sign(x.fraction.length - y.fraction.length);'),
  ts('decimal-compare-negative', 'decimal.ts', '  return x.negative ? -magnitude : magnitude;', '  return magnitude;'),
  ts('decimal-minus-zero', 'decimal.ts', "  return { negative: minus && !(integer === '0' && fraction === ''), integer, fraction };", '  return { negative: minus, integer, fraction };'),
  ts('decimal-leading-zero', 'decimal.ts', '  if (integer.length === 0 || (integer.length > 1 && integer.charCodeAt(0) === ZERO)) return null;', '  if (integer.length === 0) return null;'),
  ts('decimal-empty-fraction', 'decimal.ts', '    if (fractionEnd === intEnd + 1 || fractionEnd !== text.length) return null;', '    if (fractionEnd !== text.length) return null;'),
  ts('decimal-trailing-zeros', 'decimal.ts', '    fraction = trimZeros(text.slice(intEnd + 1, fractionEnd));', '    fraction = text.slice(intEnd + 1, fractionEnd);'),
  ts('decimal-not-normalised', 'decimal.ts', '  while (s > 0 && n % 10n === 0n) {', '  while (s < 0 && n % 10n === 0n) {'),
  ts('decimal-half-down', 'decimal.ts', '  if (2n * abs(r) < abs(den)) return q;', '  if (2n * abs(r) <= abs(den)) return q;'),
  ts('decimal-negative-half-up', 'decimal.ts', '  return num < 0n !== den < 0n ? q - 1n : q + 1n;', '  return q + 1n;'),
  ts('decimal-division-places', 'decimal.ts', 'export const DIVISION_PLACES = 20;', 'export const DIVISION_PLACES = 19;'),
  ts('decimal-max-digits', 'decimal.ts', 'export const MAX_DIGITS = 60;', 'export const MAX_DIGITS = 61;'),
  ts('decimal-stored-digits', 'decimal.ts', 'export const STORED_DIGITS = 15;', 'export const STORED_DIGITS = 16;'),
  ts('decimal-round-places', 'decimal.ts', 'export const MAX_ROUND_PLACES = 20;', 'export const MAX_ROUND_PLACES = 21;'),
  ts('decimal-division-by-zero', 'decimal.ts', "  if (b.n === 0n) throw new LogicError('division_by_zero');" + NL, ''),
  ts('decimal-format-minus-zero', 'decimal.ts', "  return d.n < 0n ? '-' + text : text;", "  return d.n <= 0n ? '-' + text : text;"),

  // ---- The date module, the instant parser and the time zone check (ADR-0030) ----
  ts('date-leap-centuries', 'dates.ts', 'const isLeap = (y: number): boolean => (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;', 'const isLeap = (y: number): boolean => y % 4 === 0 || y % 400 === 0;'),
  ts('date-leap-400', 'dates.ts', 'const isLeap = (y: number): boolean => (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;', 'const isLeap = (y: number): boolean => y % 4 === 0 && y % 100 !== 0;'),
  ts('date-november-31', 'dates.ts', '  return m === 4 || m === 6 || m === 9 || m === 11 ? 30 : 31;', '  return m === 4 || m === 6 || m === 9 ? 30 : 31;'),
  ts('date-month-13', 'dates.ts', 'y < 1 || m < 1 || m > 12 || d < 1 ||', 'y < 1 || m < 1 || m > 13 || d < 1 ||'),
  ts('date-day-zero', 'dates.ts', 'y < 1 || m < 1 || m > 12 || d < 1 ||', 'y < 1 || m < 1 || m > 12 || d < 0 ||'),
  ts('date-year-zero', 'dates.ts', 'y < 1 || m < 1 || m > 12 || d < 1 ||', 'y < 0 || m < 1 || m > 12 || d < 1 ||'),
  ts('time-hour-24', 'dates.ts', 'h > 23 || min > 59 || s > 59) return null;', 'h > 24 || min > 59 || s > 59) return null;'),
  ts('time-minute-60', 'dates.ts', 'h > 23 || min > 59 || s > 59) return null;', 'h > 23 || min > 60 || s > 59) return null;'),
  ts('time-second-60', 'dates.ts', 'h > 23 || min > 59 || s > 59) return null;', 'h > 23 || min > 59 || s > 60) return null;'),
  ts('date-time-space', 'dates.ts', "  if (text.length < 11 || text[10] !== 'T') return null;", "  if (text.length < 11 || (text[10] !== 'T' && text[10] !== ' ')) return null;"),
  ts('instant-ten-places', 'dates.ts', '    if (count < 1 || count > 9) return null;', '    if (count < 1 || count > 10) return null;'),
  ts('instant-empty-fraction', 'dates.ts', '    if (count < 1 || count > 9) return null;', '    if (count > 9) return null;'),
  ts('instant-small-z', 'dates.ts', "  if (i === text.length - 1 && text[i] === 'Z') return 0;", "  if (i === text.length - 1 && (text[i] === 'Z' || text[i] === 'z')) return 0;"),
  ts('instant-offset-sign', 'dates.ts', "  return sign === '-' ? -seconds : seconds;", '  return seconds;'),
  ts('instant-offset-hour-24', 'dates.ts', '  if (hours === null || minutes === null || hours > 23 || minutes > 59) return null;', '  if (hours === null || minutes === null || hours > 24 || minutes > 59) return null;'),
  ts('instant-before-1970', 'dates.ts', '  return instant >= FIRST_INSTANT && instant <= LAST_INSTANT ? instant : null;', '  return instant <= LAST_INSTANT ? instant : null;'),
  ts('instant-after-9998', 'dates.ts', '  return instant >= FIRST_INSTANT && instant <= LAST_INSTANT ? instant : null;', '  return instant >= FIRST_INSTANT ? instant : null;'),
  ts('time-zone-offset-taken', 'dates.ts', '  if (first >= 97 && first <= 122) {', '  if (first >= 0) {'),
  ts('time-zone-ignored', 'dates.ts', lines("      created = new Intl.DateTimeFormat('en-US', {", '        timeZone,'), lines("      created = new Intl.DateTimeFormat('en-US', {", "        timeZone: 'UTC',")),
  ts('relative-week', 'dates.ts', "    else if (step.unit === 'w') current += step.amount * 7;", "    else if (step.unit === 'w') current += step.amount * 6;"),
  ts('relative-year', 'dates.ts', "    else current = addMonths(current, step.unit === 'm' ? step.amount : step.amount * 12);", "    else current = addMonths(current, step.unit === 'm' ? step.amount : step.amount * 11);"),
  ts('relative-no-clamp', 'dates.ts', '  return dayNumber(year, month, Math.min(d, daysInMonth(year, month)));', '  return dayNumber(year, month, d);'),
  ts('relative-five-digits', 'dates.ts', '    while (end < text.length && end - i <= 4 && number(text, end, 1) !== null) end++;', '    while (end < text.length && end - i <= 5 && number(text, end, 1) !== null) end++;', {
    equivalent: 'the count check refuses a fifth digit as well; the loop only stops reading early',
  }),
  ts('relative-range', 'dates.ts', '    if (!(current >= MIN_DAY && current <= MAX_DAY)) return null;', '    if (!(current <= MAX_DAY)) return null;', {
    equivalent: 'four digits of days or weeks never reach year 1 from 1970 on, and a month or year step there is already NaN',
  }),

  // ---- The pattern module, its limits and the instruction count (ADR-0030) ----
  ts('pattern-caret-anywhere', 'pattern.ts', '        if (position === 0) top = push(top, pc + 1);', '        top = push(top, pc + 1);'),
  ts('pattern-dollar-anywhere', 'pattern.ts', '        if (position === points.length) top = push(top, pc + 1);', '        top = push(top, pc + 1);'),
  ts('pattern-negation-ignored', 'pattern.ts', '  for (const [low, high] of set.ranges) if (value >= low && value <= high) return !set.negated;', '  for (const [low, high] of set.ranges) if (value >= low && value <= high) return true;'),
  ts('pattern-reversed-range', 'pattern.ts', '      if (high.ranges !== null || (high.raw && high.point === HYPHEN) || high.point < item.point) refuse();', '      if (high.ranges !== null || (high.raw && high.point === HYPHEN)) refuse();'),
  ts('pattern-hyphen-inside', 'pattern.ts', '      if (hyphen && !first && this.peek() !== BRACKET_CLOSE) refuse();' + NL, ''),
  ts('pattern-empty-class', 'pattern.ts', '    if (this.peek() === BRACKET_CLOSE) refuse(); // "[]" and "[^]" mean different things in common dialects', '    // "[]" and "[^]" mean different things in common dialects'),
  ts('pattern-raw-bracket', 'pattern.ts', '    if (c === BRACKET) refuse();' + NL, ''),
  ts('pattern-any-escape', 'pattern.ts', '        return c >= 32 && c <= 126 && !isAlphanumeric(c) ? point(c) : refuse();', '        return point(c);'),
  ts('pattern-surrogate-escape', 'pattern.ts', '        return isSurrogate(value) ? refuse() : point(value);', '        return point(value);'),
  ts('pattern-digits-beyond-ascii', 'pattern.ts', 'const DIGITS: readonly Range[] = [[48, 57]];', 'const DIGITS: readonly Range[] = [[48, 57], [0x660, 0x669]];'),
  ts('pattern-space-takes-no-break-space', 'pattern.ts', lines('  [9, 13],', '  [32, 32],', '];'), lines('  [9, 13],', '  [32, 32],', '  [0xa0, 0xa0],', '];')),
  ts('pattern-word-without-underscore', 'pattern.ts', '  [95, 95],' + NL, ''),
  ts('pattern-dot-takes-carriage-return', 'pattern.ts', '  [13, 13],' + NL, ''),
  ts('pattern-dot-takes-line-separator', 'pattern.ts', lines('  [13, 13],', '  [0x2028, 0x2029],'), '  [13, 13],'),
  ts('pattern-length-limit', 'pattern.ts', 'export const MAX_PATTERN_LENGTH = 500;', 'export const MAX_PATTERN_LENGTH = 501;'),
  ts('pattern-count-limit', 'pattern.ts', 'export const MAX_COUNT = 1000;', 'export const MAX_COUNT = 1001;'),
  ts('pattern-count-out-of-order', 'pattern.ts', '(high !== null && (high > MAX_COUNT || high < low))', '(high !== null && high > MAX_COUNT)'),
  ts('pattern-weight-limit', 'pattern.ts', 'export const MAX_WEIGHT = 2000;', 'export const MAX_WEIGHT = 2001;'),
  ts('pattern-instruction-limit', 'pattern.ts', 'export const MAX_INSTRUCTIONS = 10000;', 'export const MAX_INSTRUCTIONS = 10001;'),
  ts('pattern-instructions-of-alternatives', 'pattern.ts', '      return cap(options + 2 * (node.options.length - 1), MAX_INSTRUCTIONS);', '      return cap(options, MAX_INSTRUCTIONS);'),
  ts('pattern-instructions-of-a-star', 'pattern.ts', '(node.min > 0 ? 1 : 2), MAX_INSTRUCTIONS);', '1, MAX_INSTRUCTIONS);'),
  ts('pattern-instructions-of-a-range', 'pattern.ts', '      return cap(size(node.node) * node.max + (node.max - node.min), MAX_INSTRUCTIONS);', '      return cap(size(node.node) * node.max, MAX_INSTRUCTIONS);'),

  // ---- The form engine (ADR-0061) ----
  ts('form-container-ignored', 'form.ts', '      element.hidden = element.container?.hidden === true || !shown(element, global);', '      element.hidden = !shown(element, global);'),
  ts('form-repeater-hidden-ignored', 'form.ts', 'repeater.hidden || !shown(element, scopeOf(repeater, row)))', '!shown(element, scopeOf(repeater, row)))'),
  ts('form-hidden-answers-kept', 'form.ts', '    if (owner.repeater === null) return owner.hidden ? undefined : given(owner, id, -1);', '    if (owner.repeater === null) return given(owner, id, -1);'),
  ts('form-hidden-row-answers-kept', 'form.ts', '    if (owner.repeater === repeater) return owner.hiddenIn[row] === true ? undefined : given(owner, id, row);', '    if (owner.repeater === repeater) return given(owner, id, row);'),
  ts('form-row-scope-lost', 'form.ts', '    if (owner.repeater === repeater) return', '    if (owner.repeater === repeater && repeater === null) return'),
  ts('form-hidden-rows-seen-outside', 'form.ts', '    return rows.flatMap((_, r) => (owner.hiddenIn[r] === true ? [] : (given(owner, id, r) ?? [])));', '    return rows.flatMap((_, r) => given(owner, id, r) ?? []);'),
  ts('form-show-inverted', 'form.ts', "    return matchesGroup(rule, scope, patterns) === (rule.effect === 'show');", "    return matchesGroup(rule, scope, patterns) === (rule.effect === 'hide');"),
  ts('form-switched-off-counts', 'form.ts', '  rules.find((rule) => rule.enabled && effects.includes(rule.effect));', '  rules.find((rule) => effects.includes(rule.effect));'),
  ts('form-required-ignored', 'form.ts', '    if (element.required) return true;' + NL, ''),
  ts('form-require-unmatched', 'form.ts', '    return rule !== undefined && matchesGroup(rule, scope, patterns);', '    return rule !== undefined;'),
  ts('form-hidden-required', 'form.ts',
    lines('    } else if (element.hidden) hidden.push(element.id);', '    else {'),
    lines('    } else {', '      if (element.hidden) hidden.push(element.id);')),
  ts('form-hidden-row-required', 'form.ts', lines('          row.hidden.push(element.id);', '          return;'), '          row.hidden.push(element.id);'),
  ts('form-message-on-hidden', 'form.ts', '    } else if (!owner.hidden && matchesGroup(rule, global, patterns)) push(messages, rule.field, rule.id);', '    } else if (matchesGroup(rule, global, patterns)) push(messages, rule.field, rule.id);'),
  ts('form-message-in-hidden-row', 'form.ts', '        if (owner.hiddenIn[at] !== true && matchesGroup(rule, scopeOf(repeater, at), patterns))', '        if (matchesGroup(rule, scopeOf(repeater, at), patterns))'),
  ts('form-message-switched-off', 'form.ts', '    if (!rule.enabled || owner === undefined) continue;', '    if (owner === undefined) continue;'),
  ts('form-actions-need-a-rule', 'form.ts', '    return rule === undefined || matchesGroup(rule, global, patterns);', '    return rule !== undefined && matchesGroup(rule, global, patterns);'),
  ts('form-outcome-last', 'form.ts', '    outcome: (form.outcomes ?? []).find(', '    outcome: (form.outcomes ?? []).findLast('),
  ts('form-cycle-ignored', 'form.ts', "  if (ready.length < nodes.length) throw new LogicError('cycle');" + NL, ''),
  ts('form-container-no-dependency', 'form.ts', '    if (element.container !== null) on.add(element.container);' + NL, ''),
  ts('form-require-orders', 'form.ts', lines("    const visibility = ruleOf(element.rules, 'show', 'hide');", '    if (visibility !== undefined) {'), lines("    const visibility = ruleOf(element.rules, 'show', 'hide', 'require');", '    if (visibility !== undefined) {')),
  ts('form-operand-not-looked-at', 'form.ts', '    if (isField(value)) into.add(value.field);', '    if (isField(value)) return;'),
  ts('form-list-not-looked-at', 'form.ts', '    else if (Array.isArray(value)) value.forEach(operand);', '    else if (Array.isArray(value)) return;'),
  ts('form-group-not-looked-at', 'form.ts', '    if (isGroup(item)) looksAt(item, into);', '    if (isGroup(item)) continue;'),
  ts('form-ids-twice', 'form.ts', "    if (typeof id !== 'string' || ids.has(id)) throw new LogicError('invalid_form');", "    if (typeof id !== 'string') throw new LogicError('invalid_form');"),
  ts('form-nested-repeater', 'form.ts', "      if (repeater !== null && element.repeats) throw new LogicError('invalid_form');" + NL, ''),
  ts('form-require-without-kind', 'form.ts', "(effect === 'require' && !holdsValue) || ", ''),
  ts('form-show-and-hide', 'form.ts', lines("    if ((effect === 'show' && seen.has('hide')) || (effect === 'hide' && seen.has('show')))", "      throw new LogicError('invalid_rule');", ''), ''),
  ts('form-message-field-unchecked', 'form.ts', "    if (!subjects.has(rule.field)) throw new LogicError('unknown_field');" + NL, ''),
  ts('form-validation-rule-unchecked', 'form.ts', lines('    checkGroup(rule, kinds, patterns);', '  }', '  for (const action'), lines('  }', '  for (const action')),
  ts('form-actions-unchecked', 'form.ts', "  for (const action of form.actions ?? []) checkRules(action.rules ?? [], ['run'], true, kinds, patterns);" + NL, ''),
  ts('form-outcomes-unchecked', 'form.ts', "  for (const outcome of form.outcomes ?? []) checkRules(outcome.rules ?? [], ['use'], true, kinds, patterns);" + NL, ''),
  ts('form-order-before-check', 'form.ts', lines('  // The check, before anything is evaluated.', '  const patterns'), lines('  order(nodes, subjects);', '  const patterns')),
  ts('form-context-hidden-used', 'form.ts', '  const global: Scope = { fields: kinds, context, answers: (id) => answersOf(id, null, -1) };', '  const global: Scope = { fields: kinds, context, answers: (id) => (context.hidden.includes(id) ? undefined : answersOf(id, null, -1)) };'),
  ts('form-grid-rows-no-subjects', 'form.ts', '      for (const row of field.rows ?? []) subjects.set(claim(row.id), { kind: row.kind, owner: element });', '      for (const row of field.rows ?? []) claim(row.id);'),

  // ---- The built-in types' checks (ROADMAP 5.2b, ADR-0062) ----
  ts('check-number-settings-ignored', 'checks.ts', "typeof settings[name] === 'number' ? (settings[name] as number) : undefined;", "typeof settings[name] === 'bigint' ? (settings[name] as number) : undefined;"),
  ts('check-text-settings-ignored', 'checks.ts', "typeof settings[name] === 'string' ? (settings[name] as string) : undefined;", "typeof settings[name] === 'bigint' ? (settings[name] as string) : undefined;"),
  ts('check-list-settings-ignored', 'checks.ts', "(item): item is string => typeof item === 'string')", "(item): item is string => typeof item !== 'string')"),
  ts('check-length-min-inclusive', 'checks.ts', "if (min !== undefined && v.value.length < min) v.fail('minLength');", "if (min !== undefined && v.value.length <= min) v.fail('minLength');"),
  ts('check-length-max-inclusive', 'checks.ts', "else if (max !== undefined && v.value.length > max) v.fail('maxLength');", "else if (max !== undefined && v.value.length >= max) v.fail('maxLength');"),
  ts('check-length-both', 'checks.ts', "  else if (max !== undefined && v.value.length > max) v.fail('maxLength');", "  if (max !== undefined && v.value.length > max) v.fail('maxLength');"),
  ts('check-words-min-inclusive', 'checks.ts', "if (min !== undefined && count < min) v.fail('minWords');", "if (min !== undefined && count <= min) v.fail('minWords');"),
  ts('check-words-max-inclusive', 'checks.ts', "else if (max !== undefined && count > max) v.fail('maxWords');", "else if (max !== undefined && count >= max) v.fail('maxWords');"),
  ts('check-words-both', 'checks.ts', "  else if (max !== undefined && count > max) v.fail('maxWords');", "  if (max !== undefined && count > max) v.fail('maxWords');"),
  ts('check-words-not-checked', 'checks.ts', lines('    length(v);', '    words(v);'), '    length(v);'),
  ts('check-words-every-character', 'text.ts', '    if (!space && !inWord) words++;', '    if (!space) words++;'),
  ts('check-words-spaces-only', 'text.ts', '    const space = TRIMMED.has(value.charCodeAt(i));', '    const space = value.charCodeAt(i) === 32;'),
  ts('check-range-min-inclusive', 'checks.ts', "if (min !== undefined && (compareText(v.value, min) ?? 0) < 0) v.fail('min');", "if (min !== undefined && (compareText(v.value, min) ?? 0) <= 0) v.fail('min');"),
  ts('check-range-max-inclusive', 'checks.ts', "else if (max !== undefined && (compareText(v.value, max) ?? 0) > 0) v.fail('max');", "else if (max !== undefined && (compareText(v.value, max) ?? 0) >= 0) v.fail('max');"),
  ts('check-range-both', 'checks.ts', "  else if (max !== undefined && (compareText(v.value, max) ?? 0) > 0) v.fail('max');", "  if (max !== undefined && (compareText(v.value, max) ?? 0) > 0) v.fail('max');"),
  ts('check-step-from-zero', 'checks.ts', "const from = parseDecimal(min ?? '0');", "const from = parseDecimal('0');"),
  ts('check-step-of-zero', 'checks.ts', 'by === null || by.n === 0n) return;', 'by === null) return;'),
  ts('check-step-inverted', 'checks.ts', "  if (!isMultiple(subtract(value, from), by)) v.fail('step');", "  if (isMultiple(subtract(value, from), by)) v.fail('step');"),
  ts('check-step-multiple-inverted', 'decimal.ts', '(b.n * pow10(s - b.s)) === 0n;', '(b.n * pow10(s - b.s)) !== 0n;'),
  ts('check-step-multiple-scale', 'decimal.ts', lines('export function isMultiple(a: Decimal, b: Decimal): boolean {', '  const s = Math.max(a.s, b.s);'), lines('export function isMultiple(a: Decimal, b: Decimal): boolean {', '  const s = Math.min(a.s, b.s);')),
  ts('check-places-whole', 'checks.ts', "(decimals === 0 ? '1' :", "(decimals === 0 ? '0.1' :"),
  ts('check-places-one-more', 'checks.ts', "`0.${'0'.repeat(decimals - 1)}1`", "`0.${'0'.repeat(decimals)}1`"),
  ts('check-places-ignored', 'checks.ts', "range(v, text(v.settings, 'step') ?? (decimals === undefined ? undefined : stepOfPlaces(decimals)));", "range(v, text(v.settings, 'step'));"),
  ts('check-whole-fractions', 'checks.ts', 'if (d === null || d.s !== 0) return false;', 'if (d === null || d.s < 0) return false;'),
  ts('check-whole-low-exclusive', 'checks.ts', 'd.n >= BigInt(low)', 'd.n > BigInt(low)'),
  ts('check-whole-high-exclusive', 'checks.ts', 'd.n <= BigInt(high)', 'd.n < BigInt(high)'),
  ts('check-rating-from-zero', 'checks.ts', 'isWholeNumber(v.value, 1, count)', 'isWholeNumber(v.value, 0, count)'),
  ts('check-scale-one-more', 'checks.ts', 'isWholeNumber(v.value, from, to)', 'isWholeNumber(v.value, from, to + 1)'),
  ts('check-bound-date-none', 'checks.ts', "  if (typeof limit === 'string') return parseDate(limit);", "  if (typeof limit === 'string') return undefined;"),
  ts('check-bound-relative-from-1970', 'checks.ts', 'applyRelative(today(), steps);', 'applyRelative(0, steps);'),
  ts('check-bound-no-offset-no-limit', 'checks.ts', 'return steps === null ? null :', 'return steps === null ? undefined :'),
  ts('check-bound-shape-no-limit', 'checks.ts', lines('  }', '  return null;', '}'), lines('  }', '  return undefined;', '}')),
  ts('check-window-low-outside-ignored', 'checks.ts', '    low !== null &&', '    low !== -1 &&'),
  ts('check-window-high-outside-ignored', 'checks.ts', '    high !== null &&', '    high !== -1 &&'),
  ts('check-window-low-exclusive', 'checks.ts', '(low === undefined || day >= low)', '(low === undefined || day > low)'),
  ts('check-window-high-exclusive', 'checks.ts', '(high === undefined || day <= high)', '(high === undefined || day < high)'),
  ts('check-weekday-before-1970', 'checks.ts', 'const weekday = (((day + 4) % 7) + 7) % 7;', 'const weekday = (day + 4) % 7;'),
  ts('check-weekday-shifted', 'checks.ts', 'const weekday = (((day + 4) % 7) + 7) % 7;', 'const weekday = (((day + 3) % 7) + 7) % 7;'),
  ts('check-weekday-sunday-taken', 'checks.ts', '(weekday === 0 || weekday === 6)', '(weekday === 6)'),
  ts('check-weekday-saturday-taken', 'checks.ts', '(weekday === 0 || weekday === 6)', '(weekday === 0)'),
  ts('check-weekday-always', 'checks.ts', "!(v.settings['weekdaysOnly'] === true && (", "!(v.settings['weekdaysOnly'] !== 1 && ("),
  ts('check-excluded-every', 'checks.ts', "!texts(v.settings, 'excluded').some(", "!texts(v.settings, 'excluded').every("),
  ts('check-minute-step-from-midnight', 'checks.ts', '(seconds - from) % (stepMinutes * 60) === 0', 'seconds % (stepMinutes * 60) === 0'),
  ts('check-minute-step-in-seconds', 'checks.ts', '(seconds - from) % (stepMinutes * 60) === 0', '(seconds - from) % stepMinutes === 0'),
  ts('check-time-min-inclusive', 'checks.ts', "if (earliest !== null && seconds < earliest) v.fail('min');", "if (earliest !== null && seconds <= earliest) v.fail('min');"),
  ts('check-time-max-inclusive', 'checks.ts', "else if (latest !== null && seconds > latest) v.fail('max');", "else if (latest !== null && seconds >= latest) v.fail('max');"),
  ts('check-time-both', 'checks.ts', "    else if (latest !== null && seconds > latest) v.fail('max');", "    if (latest !== null && seconds > latest) v.fail('max');"),
  ts('check-time-step-from-midnight', 'checks.ts', "onMinuteStep(seconds, earliest ?? 0, number(v.settings, 'stepMinutes'))", "onMinuteStep(seconds, 0, number(v.settings, 'stepMinutes'))"),
  ts('check-datetime-window-skipped', 'checks.ts', lines('    dateWindow(v, day);', "    if (!onMinuteStep(seconds, 0, number(v.settings, 'stepMinutes'))) v.fail('step');"), "    if (!onMinuteStep(seconds, 0, number(v.settings, 'stepMinutes'))) v.fail('step');"),
  ts('check-datetime-step-skipped', 'checks.ts', lines('    dateWindow(v, day);', "    if (!onMinuteStep(seconds, 0, number(v.settings, 'stepMinutes'))) v.fail('step');"), '    dateWindow(v, day);'),
  ts('check-datetime-time-misread', 'checks.ts', 'const seconds = parseTime(v.value.slice(11));', 'const seconds = parseTime(v.value.slice(10));'),
  ts('check-confirm-twice-taken', 'checks.ts', "if (again.length !== 1 || fold(again[0] ?? '') !== fold(v.value)) v.fail('confirmation');", "if (fold(again[0] ?? '') !== fold(v.value)) v.fail('confirmation');"),
  ts('check-confirm-exact', 'checks.ts', "fold(again[0] ?? '') !== fold(v.value)", "(again[0] ?? '') !== v.value"),
  ts('check-confirm-skipped', 'checks.ts', "if (v.settings['confirm'] !== true) return;", "if (v.settings['confirm'] !== 1) return;"),
  ts('check-scheme-last-colon', 'checks.ts', "const colon = v.value.indexOf(':');", "const colon = v.value.lastIndexOf(':');"),
  ts('check-scheme-not-lowered', 'checks.ts', 'asciiLower(v.value.slice(0, colon))', 'v.value.slice(0, colon)'),
  ts('check-scheme-shift', 'checks.ts', 'letter.charCodeAt(0) + 32', 'letter.charCodeAt(0) + 31'),
  ts('check-scheme-full-lowercase', 'checks.ts', "value.replace(/[A-Z]/g, (letter) => String.fromCharCode(letter.charCodeAt(0) + 32))", 'value.toLowerCase()', {
    equivalent: 'only the Kelvin sign lowercases to an ASCII letter, k, and no scheme a field allows (https, http) holds one',
  }),
  ts('check-scheme-any', 'checks.ts', "texts(v.settings, 'schemes').some((allowed) => allowed === scheme)", "texts(v.settings, 'schemes').some((allowed) => allowed !== scheme)"),
  ts('check-required-for-optional', 'checks.ts', "errors: field.required === true ? ['required'] : []", "errors: field.required !== false ? ['required'] : []"),
  ts('check-required-never', 'checks.ts', "errors: field.required === true ? ['required'] : []", 'errors: []'),
  ts('check-kinds-twice', 'checks.ts', '      if (!errors.includes(kind)) errors.push(kind);', '      errors.push(kind);', {
    equivalent: 'no check of a built-in type reports a kind twice; the guard keeps the promise of each kind once for a check that might',
  }),

  // ---- The choice, people and file types' checks, and the field's own checks (ROADMAP 5.2c, ADR-0062) ----
  ts('check-options-some', 'checks.ts', '!v.answers.values.every((value) => options.has(value))', '!v.answers.values.some((value) => options.has(value))'),
  ts('check-options-of-read-values', 'checks.ts', '!v.answers.values.every((value) => options.has(value))', '!v.values.every((value) => options.has(value))'),
  ts('check-options-list-as-none', 'checks.ts', 'options: field.options === undefined ? null : new Set(field.options),', 'options: new Set(field.options ?? []),'),
  ts('check-selections-min-inclusive', 'checks.ts', '(min !== undefined && count < min) ||', '(min !== undefined && count <= min) ||'),
  ts('check-selections-max-inclusive', 'checks.ts', "(max !== undefined && count > max)) v.fail('selections')", "(max !== undefined && count >= max)) v.fail('selections')"),
  ts('check-selections-of-own-values', 'checks.ts', '  const count = v.values.length;', '  const count = v.answers.values.length;'),
  ts('check-dropdown-one-choice-counted', 'checks.ts', "    if (v.settings['multiple'] === true) selections(v);", '    selections(v);'),
  ts('check-dropdown-options-skipped', 'checks.ts', lines('  dropdown(v) {', '    amongOptions(v);'), '  dropdown(v) {'),
  ts('check-radio-options-skipped', 'checks.ts', '  radio: amongOptions,', '  radio() {},'),
  ts('check-checkboxes-options-skipped', 'checks.ts', lines('  checkboxes(v) {', '    amongOptions(v);'), '  checkboxes(v) {'),
  ts('check-checkboxes-selections-skipped', 'checks.ts', lines('    selections(v);', '  },', '  grid(v) {'), lines('  },', '  grid(v) {')),
  ts('check-required-parts-lost', 'checks.ts', '    required: field.required === true,', '    required: false,'),
  ts('read-skipped', 'checks.ts', 'own(READ, field.type)?.(field, answers, settings) ?? answers.values', 'answers.values'),
  ts('read-radio-other-ignored', 'checks.ts', 'return answers.values.length === 0 && other !== undefined ? [other] : answers.values;', 'return answers.values;'),
  ts('read-radio-other-beside-an-option', 'checks.ts', 'return answers.values.length === 0 && other !== undefined ? [other]', 'return other !== undefined ? [other]', {
    equivalent: 'no check of radio buttons reads the text of their value: which text stands for the answer shows only in what the entry stores',
  }),
  ts('read-checkboxes-other-ignored', 'checks.ts', 'return other === undefined || answers.values.includes(other) ? answers.values : [...answers.values, other];', 'return answers.values;'),
  ts('read-checkboxes-other-repeat-counted', 'checks.ts', 'return other === undefined || answers.values.includes(other) ?', 'return other === undefined ?'),
  ts('read-other-not-offered-taken', 'checks.ts', "return settings['other'] === true && other.length === 1", 'return other.length === 1'),
  ts('read-grid-unknown-rows-taken', 'checks.ts', 'grid: (field, answers) => (field.rows ?? []).flatMap((row) => part(answers, row)),', 'grid: (_, answers) => Object.values(answers.parts ?? {}).flat(),'),
  ts('read-name-unasked-parts-taken', 'checks.ts', "name: (_, answers, settings) => partsOf(answers, texts(settings, 'parts')),", 'name: (_, answers) => Object.values(answers.parts ?? {}).flat(),'),
  ts('read-address-unasked-parts-taken', 'checks.ts', "address: (_, answers, settings) => partsOf(answers, [...ADDRESS, ...texts(settings, 'parts')]),", 'address: (_, answers) => Object.values(answers.parts ?? {}).flat(),'),
  ts('read-address-without-the-three', 'checks.ts', "address: (_, answers, settings) => partsOf(answers, [...ADDRESS, ...texts(settings, 'parts')]),", "address: (_, answers, settings) => partsOf(answers, texts(settings, 'parts')),"),
  ts('check-parts-listed-not-asked', 'checks.ts', '? listed.filter((name) => asked.includes(name)) :', '? listed :'),
  ts('check-parts-listed-ignored', 'checks.ts', 'const needed = listed.length > 0 ?', 'const needed = listed.length > 99 ?'),
  ts('check-parts-unlisted-when-optional', 'checks.ts', ': v.required ? unlisted : [];', ': unlisted;'),
  ts('check-parts-given', 'checks.ts', "part(v.answers, name).length === 0)) v.fail('required')", "part(v.answers, name).length === 1)) v.fail('required')"),
  ts('check-name-first-and-last-only', 'checks.ts', '    requiredParts(v, asked, asked);', "    requiredParts(v, asked, ['first', 'last']);"),
  ts('check-address-every-part-asked', 'checks.ts', "requiredParts(v, [...ADDRESS, ...texts(v.settings, 'parts')], ADDRESS);", "requiredParts(v, [...ADDRESS, ...texts(v.settings, 'parts')], [...ADDRESS, ...texts(v.settings, 'parts')]);"),
  ts('check-address-without-a-street', 'checks.ts', "const ADDRESS = ['street', 'postcode', 'city'];", "const ADDRESS = ['postcode', 'city'];"),
  ts('check-grid-scale-ignored', 'checks.ts', "if (typeof scale === 'object' && scale !== null) {", 'if (false) {'),
  ts('check-grid-options-first', 'checks.ts', "if (typeof scale === 'object' && scale !== null) {", "if (typeof scale === 'object' && scale !== null && options === null) {"),
  ts('check-grid-scale-to-open', 'checks.ts', 'column = (value) => isWholeNumber(value, from, to);', 'column = (value) => isWholeNumber(value, from, undefined);'),
  ts('check-grid-scale-from-open', 'checks.ts', 'column = (value) => isWholeNumber(value, from, to);', 'column = (value) => isWholeNumber(value, undefined, to);'),
  ts('check-grid-options-any', 'checks.ts', 'column = (value) => options.has(value);', 'column = () => true;'),
  ts('check-grid-some', 'checks.ts', '!v.values.every(column)', '!v.values.some(column)'),
  ts('check-ranking-at-most', 'checks.ts', 'if (v.values.length !== ranked ||', 'if (v.values.length > ranked ||'),
  ts('check-ranking-at-least', 'checks.ts', 'if (v.values.length !== ranked ||', 'if (v.values.length < ranked ||'),
  ts('check-ranking-top-past-options', 'checks.ts', ': Math.min(top, options.size);', ': top;'),
  ts('check-ranking-top-ignored', 'checks.ts', 'const ranked = top === undefined ? options.size', 'const ranked = top !== null ? options.size'),
  ts('check-ranking-options-skipped', 'checks.ts', "ranked || !v.values.every((value) => options.has(value))) v.fail('invalid');", "ranked) v.fail('invalid');"),
  ts('check-ranking-list-checked', 'checks.ts', lines('    if (options === null) return;', "    const top = number(v.settings, 'top');"), "    const top = number(v.settings, 'top');"),
  ts('check-country-allowed-ignored', 'checks.ts', 'if (allowed.length > 0 &&', 'if (allowed.length > 99 &&'),
  ts('check-country-none-allowed', 'checks.ts', 'if (allowed.length > 0 &&', 'if (allowed.length >= 0 &&'),
  ts('check-file-count-inclusive', 'checks.ts', "v.values.length > most) v.fail('fileCount')", "v.values.length >= most) v.fail('fileCount')"),
  ts('check-file-defaults-with-types-only', 'checks.ts', "types.length === 0 && extensions.length === 0 ? ['images', 'pdf']", "types.length === 0 ? ['images', 'pdf']"),
  ts('check-file-defaults-without-pdf', 'checks.ts', "? ['images', 'pdf'] : types;", "? ['images'] : types;"),
  ts('check-file-own-extensions-ignored', 'checks.ts', '(group) => FILE_TYPES.get(group) ?? []), ...extensions]);', '(group) => FILE_TYPES.get(group) ?? [])]);'),
  ts('check-file-svg-an-image', 'checks.ts', "'avif', 'heic', 'heif']],", "'avif', 'heic', 'heif', 'svg']],"),
  ts('check-file-heic-no-image', 'checks.ts', "'avif', 'heic', 'heif']],", "'avif', 'heif']],"),
  ts('check-file-office-without-xlsx', 'checks.ts', "'xls', 'xlsx', 'ppt'", "'xls', 'ppt'"),
  ts('check-file-text-without-csv', 'checks.ts', "['text', ['txt', 'csv']],", "['text', ['txt']],"),
  ts('check-file-archives-without-7z', 'checks.ts', "['zip', '7z', 'rar'", "['zip', 'rar'"),
  ts('check-file-type-every', 'checks.ts', 'files.some((file) => !taken.has(extensionOf(file.name)))', 'files.every((file) => !taken.has(extensionOf(file.name)))'),
  ts('check-file-size-inclusive', 'checks.ts', 'files.some((file) => file.size > bytes)', 'files.some((file) => file.size >= bytes)'),
  ts('check-file-size-every', 'checks.ts', 'files.some((file) => file.size > bytes)', 'files.every((file) => file.size > bytes)'),
  ts('check-extension-first-point', 'checks.ts', "  const point = name.lastIndexOf('.');", "  const point = name.indexOf('.');"),
  ts('check-extension-without-a-point', 'checks.ts', "return point < 0 ? '' :", 'return point < 0 ? name :'),
  ts('check-extension-not-lowered', 'checks.ts', 'asciiLower(name.slice(point + 1))', 'name.slice(point + 1)'),
  ts('check-extension-with-its-point', 'checks.ts', 'asciiLower(name.slice(point + 1))', 'asciiLower(name.slice(point))'),
  ts('own-checks-without-values', 'checks.ts', ": [], checks: [] };", ': [], checks: ownChecks(field, read, values) };'),
  ts('own-pattern-ignored', 'checks.ts', "    if (check.kind === 'pattern') {", "    if (check.kind === 'patterns') {"),
  ts('own-pattern-unsafe-fails', 'checks.ts', 'holds = pattern === null || values.every(', 'holds = pattern !== null && values.every('),
  ts('own-matches-ignored', 'checks.ts', "} else if (check.kind === 'matchesField') {", "} else if (check.kind === 'matches') {"),
  ts('own-matches-as-text', 'checks.ts', 'holds = sameValues(field.kind, values,', "holds = sameValues('text', values,"),
  ts('own-matches-no-other-values', 'checks.ts', "own(others, check.field ?? '') ?? []", '[]'),
  ts('own-checks-counted-not-indexed', 'checks.ts', '    if (!holds) failing.push(index);', '    if (!holds) failing.push(failing.length);'),
  ts('same-values-any-count', 'rules.ts', 'return values.length === others.length && values.every(', 'return values.every('),
  ts('same-values-numbers-as-text', 'rules.ts', "    if (kind === 'number') return compareText(a, b) === 0;" + NL, ''),

  // ---- The calculation language and calculations in forms (ROADMAP 5.3a, ADR-0063) ----
  ts('calc-length-one-more', 'calc.ts', 'if (this.text.length > MAX_FORMULA_LENGTH) throw', 'if (this.text.length > MAX_FORMULA_LENGTH + 1) throw'),
  ts('calc-length-at-the-start', 'calc.ts', "throw new LogicError('invalid_formula', MAX_FORMULA_LENGTH);", "throw new LogicError('invalid_formula', 0);"),
  ts('calc-rest-ignored', 'calc.ts', "    if (this.peek() !== -1) throw new LogicError('invalid_formula', this.position);" + NL, ''),
  ts('calc-tab-no-space', 'calc.ts', 'code === 32 || code === 9 || code === 10', 'code === 32 || code === 10'),
  ts('calc-carriage-return-no-space', 'calc.ts', 'code === 10 || code === 13;', 'code === 10;'),
  ts('calc-no-break-space-a-space', 'calc.ts', 'code === 32 || code === 9', 'code === 32 || code === 160 || code === 9'),
  ts('calc-letters-past-z', 'calc.ts', '(code >= 65 && code <= 90) || (code >= 97 && code <= 122)', '(code >= 65 && code <= 122)'),
  ts('calc-equals-as-not-equal', 'calc.ts', "if (c === 61) op = 'eq';", "if (c === 61) op = 'ne';"),
  ts('calc-exclamation-mark-alone', 'calc.ts', "else if (c === 33 && next === 61) op = 'ne';", "else if (c === 33) op = 'ne';"),
  ts('calc-less-or-equal-as-less', 'calc.ts', "else if (c === 60) op = next === 61 ? 'le' : 'lt';", "else if (c === 60) op = 'lt';"),
  ts('calc-greater-or-equal-as-greater', 'calc.ts', "else if (c === 62) op = next === 61 ? 'ge' : 'gt';", "else if (c === 62) op = 'gt';"),
  ts('calc-two-characters-read-as-one', 'calc.ts', "op === 'ne' || op === 'le' || op === 'ge' ? 2 : 1", "op === 'ne' || op === 'le' ? 2 : 1"),
  ts('calc-comparisons-in-a-row', 'calc.ts', 'return { op, args: [left, this.sum()], at };', 'return { op, args: [left, this.comparison()], at };'),
  ts('calc-subtract-as-add', 'calc.ts', "left = { op: c === 43 ? 'add' : 'sub',", "left = { op: 'add',"),
  ts('calc-divide-as-multiply', 'calc.ts', "left = { op: c === 42 ? 'mul' : 'div',", "left = { op: 'mul',"),
  ts('calc-sum-right-to-left', 'calc.ts', 'args: [left, this.product()], at };', 'args: [this.product(), left], at };'),
  ts('calc-product-right-to-left', 'calc.ts', 'args: [left, this.unary()], at };', 'args: [this.unary(), left], at };'),
  ts('calc-negations-not-counted', 'calc.ts', 'return negations % 2 === 1 ?', 'return negations > 0 ?'),
  ts('calc-leading-zero-taken', 'calc.ts', 'if (text.charCodeAt(start) === 48 && isDigit(text.charCodeAt(start + 1))) {', 'if (false) {'),
  ts('calc-point-without-digits-taken', 'calc.ts', "if (i === fraction) throw new LogicError('invalid_formula', i);", "if (i < fraction) throw new LogicError('invalid_formula', i);"),
  ts('calc-range-without-its-position', 'calc.ts', 'if (error instanceof LogicError) throw new LogicError(error.code, start);', 'if (error instanceof LogicError) throw new LogicError(error.code);'),
  ts('calc-unknown-function-a-syntax-error', 'calc.ts', "if (arity === undefined) throw new LogicError('unknown_operator', start);", "if (arity === undefined) throw new LogicError('invalid_formula', start);"),
  ts('calc-functions-ignore-case', 'calc.ts', 'const arity = FUNCTIONS.get(name);', 'const arity = FUNCTIONS.get(name.toLowerCase()) ?? FUNCTIONS.get(name);'),
  ts('calc-calls-not-counted', 'calc.ts',
    lines('    this.enter(start);', '    this.position++;', '    const args: Calculation[] = [];'),
    lines('    this.depth++;', '    this.position++;', '    const args: Calculation[] = [];')),
  ts('calc-parentheses-not-counted', 'calc.ts',
    lines('      this.enter(start);', '      this.position++;', '      const inner = this.comparison();'),
    lines('      this.depth++;', '      this.position++;', '      const inner = this.comparison();')),
  ts('calc-depth-one-more', 'calc.ts', 'if (++this.depth > MAX_FORMULA_DEPTH)', 'if (++this.depth > MAX_FORMULA_DEPTH + 1)'),
  ts('calc-depth-kept-after-closing', 'calc.ts', lines('    this.position++;', '    this.depth--;'), '    this.position++;'),
  ts('calc-most-arguments-ignored', 'calc.ts',
    "if (args.length < arity[0] || args.length > arity[1]) throw new LogicError('invalid_operand', start);",
    "if (args.length < arity[0]) throw new LogicError('invalid_operand', start);"),
  ts('calc-fewest-arguments-ignored', 'calc.ts',
    "if (args.length < arity[0] || args.length > arity[1]) throw new LogicError('invalid_operand', start);",
    "if (args.length > arity[1]) throw new LogicError('invalid_operand', start);"),
  ts('calc-date-arguments-not-checked', 'calc.ts', "if (!isField(arg) && !isToday(arg)) throw", "if (!isField(arg) && !isToday(arg) && k < 0) throw"),
  ts('calc-date-argument-at-the-call', 'calc.ts', "throw new LogicError('invalid_operand', starts[k]);", "throw new LogicError('invalid_operand', start);"),
  ts('calc-round-to-two-places', 'calc.ts', "return { op: 'round', args: [value], scale: 0, at: start };", "return { op: 'round', args: [value], scale: 2, at: start };"),
  ts('calc-round-places-with-a-point', 'calc.ts', "typeof places !== 'string' || places.includes('.') ||", "typeof places !== 'string' ||"),
  ts('calc-round-places-past-twenty', 'calc.ts', 'Number(places) > MAX_ROUND_PLACES) {', 'Number(places) > MAX_ROUND_PLACES + 1) {'),
  ts('calc-round-places-at-the-call', 'calc.ts', "throw new LogicError('invalid_operand', starts[1]);", "throw new LogicError('invalid_operand', start);"),
  ts('calc-today-anywhere', 'calc.ts', "if (calculation.op === 'today') throw", "if (calculation.op === 'today' && calculation.args.length < 0) throw"),
  ts('calc-today-refused-in-date-functions', 'calc.ts', '  if (DATE_FUNCTIONS.has(calculation.op)) return;' + NL, ''),
  ts('calc-numbers-take-text', 'calc.ts', "new Set<Kind>(['number', 'choice', 'choices', 'checkbox'])", "new Set<Kind>(['number', 'choice', 'choices', 'checkbox', 'text'])"),
  ts('calc-numbers-without-checkboxes', 'calc.ts', "new Set<Kind>(['number', 'choice', 'choices', 'checkbox'])", "new Set<Kind>(['number', 'choice', 'choices'])"),
  ts('calc-numbers-without-several-choices', 'calc.ts', "new Set<Kind>(['number', 'choice', 'choices', 'checkbox'])", "new Set<Kind>(['number', 'choice', 'checkbox'])"),
  ts('calc-dates-without-date-times', 'calc.ts', "new Set<Kind>(['date', 'dateTime'])", "new Set<Kind>(['date'])"),
  ts('calc-dates-take-times', 'calc.ts', "new Set<Kind>(['date', 'dateTime'])", "new Set<Kind>(['date', 'dateTime', 'time'])"),
  ts('calc-kinds-swapped', 'calc.ts', '(date ? DATES : NUMBERS)', '(date ? NUMBERS : DATES)'),
  ts('calc-unknown-field-at-the-start', 'calc.ts', "if (found === undefined) throw new LogicError('unknown_field', item.at);", "if (found === undefined) throw new LogicError('unknown_field', 0);"),
  ts('calc-constant-taken', 'calc.ts', "if (named.size === 0) throw new LogicError('constant_formula', 0);", "if (named.size < 0) throw new LogicError('constant_formula', 0);"),
  ts('calc-formula-not-checked', 'calc.ts', '  checkFormula(tree, scope.kind);' + NL, ''),
  ts('calc-answers-not-added', 'calc.ts', '    sum = add(sum, value);', '    sum = value;'),
  ts('calc-empty-answers-read', 'calc.ts', '    if (text.length === 0) continue;' + NL, ''),
  ts('calc-answers-not-cleaned', 'calc.ts', '    const text = clean(answer);', '    const text = answer;'),
  ts('calc-checkbox-other-value-unchecked', 'calc.ts', "(text === 'true' ? ONE : null)", "(text === 'true' ? ONE : ZERO)"),
  ts('calc-unreadable-answer-skipped', 'calc.ts', lines('    if (value === null) return null;', '    sum = add(sum, value);'), lines('    if (value === null) continue;', '    sum = add(sum, value);')),
  ts('calc-today-without-the-scope', 'calc.ts', 'if (isToday(calculation)) return scope.today();', 'if (isToday(calculation)) return 0;'),
  ts('calc-several-dates-take-the-first', 'calc.ts', 'if (answers.length !== 1 || text === undefined) return null;', 'if (text === undefined) return null;'),
  ts('calc-dates-not-cleaned', 'calc.ts', '(scope.answers(calculation.field) ?? []).map(clean).filter(', '(scope.answers(calculation.field) ?? []).filter('),
  ts('calc-date-time-read-as-a-date', 'calc.ts', 'return parseDateTime(text) === null ? null : parseDate(text.slice(0, 10));', 'return parseDate(text.slice(0, 10));'),
  ts('calc-date-read-as-a-date-time', 'calc.ts', "if (kind === 'date') return parseDate(text);", "if (kind === 'date') return parseDate(text.slice(0, 10));"),
  ts('calc-years-backwards-not-negated', 'calc.ts', 'if (to < from) return -yearsFrom(to, from);', 'if (to < from) return yearsFrom(to, from);'),
  ts('calc-years-complete-a-day-late', 'calc.ts', 'b.m * 100 + b.d < a.m * 100 + a.d ? years - 1', 'b.m * 100 + b.d <= a.m * 100 + a.d ? years - 1'),
  ts('calc-years-by-month-only', 'calc.ts', 'b.m * 100 + b.d < a.m * 100 + a.d ? years - 1', 'b.m < a.m ? years - 1'),
  ts('calc-days-backwards', 'calc.ts', "integer(op === 'daysBetween' ? to - from :", "integer(op === 'daysBetween' ? from - to :"),
  ts('calc-equals-at-or-below', 'calc.ts', "['eq', (order: number) => order === 0],", "['eq', (order: number) => order <= 0],"),
  ts('calc-not-equal-only-above', 'calc.ts', "['ne', (order: number) => order !== 0],", "['ne', (order: number) => order > 0],"),
  ts('calc-less-inclusive', 'calc.ts', "['lt', (order: number) => order < 0],", "['lt', (order: number) => order <= 0],"),
  ts('calc-less-or-equal-exclusive', 'calc.ts', "['le', (order: number) => order <= 0],", "['le', (order: number) => order < 0],"),
  ts('calc-greater-inclusive', 'calc.ts', "['gt', (order: number) => order > 0],", "['gt', (order: number) => order >= 0],"),
  ts('calc-greater-or-equal-exclusive', 'calc.ts', "['ge', (order: number) => order >= 0],", "['ge', (order: number) => order > 0],"),
  ts('calc-comparison-gives-one-for-false', 'calc.ts', '=== true ? ONE : ZERO;', '=== true ? ZERO : ONE;'),
  ts('calc-add-as-subtract', 'calc.ts', "  ['add', add],", "  ['add', subtract],"),
  ts('calc-no-value-on-the-left-as-zero', 'calc.ts',
    lines('    const a = calculate(first, scope);', '    if (a === null) return null;'),
    lines('    const a = calculate(first, scope) ?? ZERO;', '    if (a === null) return null;')),
  ts('calc-right-before-left', 'calc.ts',
    lines('    const a = calculate(first, scope);', '    if (a === null) return null;', '    const b = calculate(second, scope);', '    if (b === null) return null;'),
    lines('    const b = calculate(second, scope);', '    if (b === null) return null;', '    const a = calculate(first, scope);', '    if (a === null) return null;')),
  ts('calc-negation-ignored', 'calc.ts', 'return a === null ? null : negate(a);', 'return a;'),
  ts('calc-round-ignored', 'calc.ts', 'return a === null ? null : round(a, scale);', 'return a;'),
  ts('calc-round-scale-unchecked', 'calc.ts', 'if (scale === undefined || !Number.isInteger(scale) || scale < 0 || scale > MAX_ROUND_PLACES) {', 'if (scale === undefined) {'),
  ts('calc-min-as-max', 'calc.ts', "(op === 'min' ? compare(a, best) < 0 :", "(op === 'min' ? compare(a, best) > 0 :"),
  ts('calc-max-as-min', 'calc.ts', ': compare(a, best) > 0)) best = a;', ': compare(a, best) < 0)) best = a;'),
  ts('calc-min-no-value-skipped', 'calc.ts', lines('        if (a === null) return null;', '        if (best === null'), lines('        if (a === null) continue;', '        if (best === null')),
  ts('calc-if-inverted', 'calc.ts', 'return value(condition.n !== 0n ? 1 : 2);', 'return value(condition.n === 0n ? 1 : 2);'),
  ts('calc-if-works-out-both', 'calc.ts',
    lines('      if (condition === null) return null;', '      return value(condition.n'),
    lines('      if (condition === null || value(1) === null || value(2) === null) return null;', '      return value(condition.n')),
  ts('calc-if-no-value-as-false', 'calc.ts',
    lines('      if (condition === null) return null;', '      return value(condition.n'),
    lines('      if (condition === null) return value(2);', '      return value(condition.n')),
  ts('calc-and-or-never-stop', 'calc.ts', "if ((a.n !== 0n) === (op === 'or')) return", "if ((a.n !== 0n) === (op === 'or') && k < 0) return"),
  ts('calc-or-gives-its-argument', 'calc.ts', "=== (op === 'or')) return op === 'or' ? ONE : ZERO;", "=== (op === 'or')) return op === 'or' ? a : ZERO;"),
  ts('calc-and-of-true-values-false', 'calc.ts', "return op === 'or' ? ZERO : ONE;", "return op === 'or' ? ZERO : ZERO;"),
  ts('calc-and-no-value-skipped', 'calc.ts', lines('        if (a === null) return null;', '        if ((a.n'), lines('        if (a === null) continue;', '        if ((a.n')),
  ts('calc-not-inverted', 'calc.ts', ': a.n === 0n ? ONE : ZERO;', ': a.n !== 0n ? ONE : ZERO;'),
  ts('calc-no-first-date-as-zero', 'calc.ts', lines('      if (from === null) return null;', '      const to'), lines('      if (from === null) return ZERO;', '      const to')),
  ts('calc-no-second-date-as-zero', 'calc.ts', lines('      if (to === null) return null;', '      return integer('), lines('      if (to === null) return ZERO;', '      return integer(')),
  ts('decimal-leading-zeros-counted', 'decimal.ts', '  while (first < all.length - 1 && all.charCodeAt(first) === ZERO) first++;' + NL, ''),
  ts('decimal-sixty-digits-out-of-range', 'decimal.ts', 'if (all.length - first > MAX_DIGITS)', 'if (all.length - first >= MAX_DIGITS)'),
  ts('decimal-read-without-the-sign', 'decimal.ts',
    lines('  const n = BigInt(all);', '  return { n: parts.negative ? -n : n, s: parts.fraction.length };'),
    lines('  const n = BigInt(all);', '  return { n, s: parts.fraction.length };')),
  ts('decimal-negate-ignored', 'decimal.ts', '  return { n: -d.n, s: d.s };', '  return d;'),
  ts('decimal-compare-at-the-smaller-scale', 'decimal.ts',
    lines('export function compare(a: Decimal, b: Decimal): number {', '  const s = Math.max(a.s, b.s);'),
    lines('export function compare(a: Decimal, b: Decimal): number {', '  const s = Math.min(a.s, b.s);')),
  ts('decimal-compare-inverted', 'decimal.ts', 'return x < y ? -1 : x > y ? 1 : 0;', 'return x < y ? 1 : x > y ? -1 : 0;'),
  ts('form-calculation-on-any-kind', 'form.ts', "      if (element.calculation !== null && element.kind !== 'number') throw new LogicError('invalid_form');" + NL, ''),
  ts('form-alias-twice', 'form.ts', "        if (aliases.has(field.alias)) throw new LogicError('invalid_form');" + NL, ''),
  ts('form-alias-without-a-value', 'form.ts', "if (typeof field.alias === 'string' && element.kind !== null) {", "if (typeof field.alias === 'string') {"),
  ts('form-no-places-when-unset', 'form.ts', 'const decimals = calculation.decimals ?? 2;', 'const decimals = calculation.decimals ?? 0;'),
  ts('form-sixteen-places', 'form.ts', 'decimals > 15) throw', 'decimals > 16) throw'),
  ts('form-negative-places', 'form.ts', 'decimals < 0 ||', 'decimals < -1 ||'),
  ts('form-places-with-a-fraction', 'form.ts', '!Number.isInteger(decimals) || ', ''),
  ts('form-formulas-not-checked', 'form.ts', 'named: checkFormula(tree, kindOf),', 'named: [],'),
  ts('form-calculation-waits-for-nothing', 'form.ts', '    for (const alias of element.formula?.named ?? []) {', '    for (const alias of [] as string[]) {'),
  ts('form-posted-value-read', 'form.ts', '    if (owner.formula !== null) {', '    if (owner.formula === undefined) {'),
  ts('form-row-reads-the-value-outside', 'form.ts', 'const value = row < 0 ? owner.value : (owner.valueIn[row] ?? null);', 'const value = owner.value;'),
  ts('form-out-of-range-thrown', 'form.ts', "(error.code === 'division_by_zero' || error.code === 'out_of_range')", "error.code === 'division_by_zero'"),
  ts('form-division-by-zero-thrown', 'form.ts', "(error.code === 'division_by_zero' || error.code === 'out_of_range')", "error.code === 'out_of_range'"),
  ts('form-result-not-rounded', 'form.ts', 'formatDecimal(round(result, formula.decimals))', 'formatDecimal(result)'),
  ts('form-hidden-calculation-worked-out', 'form.ts', 'if (formula !== null && !element.hidden) element.value = work(formula, global);', 'if (formula !== null) element.value = work(formula, global);'),
  ts('form-hidden-row-calculation-worked-out', 'form.ts', '          hidden ? null : work(formula, scopeOf(repeater, row)),', '          work(formula, scopeOf(repeater, row)),'),
  ts('form-row-calculation-over-the-form', 'form.ts', '          hidden ? null : work(formula, scopeOf(repeater, row)),', '          hidden ? null : work(formula, global),'),
  ts('form-calculations-left-out', 'form.ts', '      if (element.formula !== null) calculations.set(element.id, element.value);', '      if (element.formula === undefined) calculations.set(element.id, element.value);'),
  ts('form-row-calculations-left-out', 'form.ts', 'if (element.formula !== null) row.calculations.set(', 'if (element.formula === undefined) row.calculations.set('),

  // ---- C#: where .NET differs (ROADMAP 5.2a) ----
  // .NET's invariant casing maps neither Turkish i, so Core folds both itself.
  cs('cs-fold-dotted-capital-i', 'TextSemantics.cs', '            if (rune.Value is DottedCapitalI or DotlessSmallI)', '            if (rune.Value is DotlessSmallI)'),
  cs('cs-fold-dotless-small-i', 'TextSemantics.cs', '            if (rune.Value is DottedCapitalI or DotlessSmallI)', '            if (rune.Value is DottedCapitalI)'),
  cs('cs-noncharacter-kept', 'TextSemantics.cs', ': char.IsSurrogate(c) || IsNoncharacter(c))', ': char.IsSurrogate(c))'),
  cs('cs-astral-noncharacter-kept', 'TextSemantics.cs', 'if (pair ? IsNoncharacter(char.ConvertToUtf32(c, value[i + 1])) :', 'if (pair ? char.ConvertToUtf32(c, value[i + 1]) < 0 :'),
  cs('cs-noncharacter-range', 'TextSemantics.cs', 'codePoint is >= 0xFDD0 and <= 0xFDEF ||', 'codePoint is >= 0xFDD0 and <= 0xFDD0 ||'),
  cs('cs-lone-surrogate-kept', 'TextSemantics.cs', ': char.IsSurrogate(c) || IsNoncharacter(c))', ': IsNoncharacter(c))', {
    tests: `${VECTORS_CS}|FullyQualifiedName~TextSemanticsTests`,
    why: "no vector can hold a lone surrogate: .NET's JSON reader refuses one, so TextSemanticsTests covers it",
  }),
  cs('cs-fold-greek', 'TextSemantics.cs', '(0x0000, 0x024F), (0x0370, 0x03FF), ', '(0x0000, 0x024F), '),
  cs('cs-fold-cyrillic', 'TextSemantics.cs', '(0x0370, 0x03FF), (0x0400, 0x052F), ', '(0x0370, 0x03FF), '),
  cs('cs-fold-armenian', 'TextSemantics.cs', '(0x0400, 0x052F), (0x0530, 0x058F),', '(0x0400, 0x052F),'),
  cs('cs-fold-latin-extended-additional', 'TextSemantics.cs', '(0x1E00, 0x1EFF), (0x1F00, 0x1FFF), ', '(0x1F00, 0x1FFF), '),
  cs('cs-fold-greek-extended', 'TextSemantics.cs', '(0x1E00, 0x1EFF), (0x1F00, 0x1FFF), ', '(0x1E00, 0x1EFF), '),
  cs('cs-fold-full-width-capitals', 'TextSemantics.cs', '(0xFF21, 0xFF3A), (0xFF41, 0xFF5A),', '(0xFF41, 0xFF5A),'),
  cs('cs-fold-full-width-small', 'TextSemantics.cs', '(0xFF21, 0xFF3A), (0xFF41, 0xFF5A),', '(0xFF21, 0xFF3A),', {
    equivalent: 'the block holds only small letters, which fold to themselves inside it or out',
  }),
  cs('cs-fold-deseret', 'TextSemantics.cs', '(0xFF21, 0xFF3A), (0xFF41, 0xFF5A),', '(0xFF21, 0xFF3A), (0xFF41, 0xFF5A), (0x10400, 0x1044F),'),
  cs('cs-fold-lowercase-only', 'TextSemantics.cs', 'var folded = Rune.ToLowerInvariant(Rune.ToUpperInvariant(rune));', 'var folded = Rune.ToLowerInvariant(rune);'),
  cs('cs-trim-bom', 'TextSemantics.cs', '(char)0x3000, (char)0xFEFF,', '(char)0x3000,'),
  cs('cs-nfc', 'TextSemantics.cs', '=> ReplaceInvalid(value).Normalize(NormalizationForm.FormC);', '=> ReplaceInvalid(value);'),

  // ---- C#: the form engine and the depth of groups ----
  cs('cs-depth-one-more', 'Rules.cs', '        if (depth > MaxDepth)', '        if (depth > MaxDepth + 1)'),
  cs('cs-answers-not-cleaned', 'Rules.cs', '            return lookup(alias) is { } given ? NonEmpty(given) : [];', '            return lookup(alias) is { } given ? [.. given] : [];'),
  cs('cs-form-container-ignored', 'FormRules.cs', '                element.Hidden = element.Container?.Hidden == true || !Shown(element, global);', '                element.Hidden = !Shown(element, global);'),
  cs('cs-form-repeater-hidden-ignored', 'FormRules.cs', '                    hiddenIn[row] = repeater.Hidden || !Shown(element, ScopeOf(repeater, row));', '                    hiddenIn[row] = !Shown(element, ScopeOf(repeater, row));'),
  cs('cs-form-hidden-answers-kept', 'FormRules.cs', '                return owner.Hidden ? null : Given(owner, id, -1);', '                return Given(owner, id, -1);'),
  cs('cs-form-hidden-row-answers-kept', 'FormRules.cs', '                return owner.HiddenIn[row] ? null : Given(owner, id, row);', '                return Given(owner, id, row);'),
  cs('cs-form-hidden-rows-seen-outside', 'FormRules.cs', '                if (!owner.HiddenIn[r] && Given(owner, id, r) is { } given)', '                if (Given(owner, id, r) is { } given)'),
  cs('cs-form-show-inverted', 'FormRules.cs', 'Matches(rule, scope) == (rule.Effect == "show");', 'Matches(rule, scope) == (rule.Effect == "hide");'),
  cs('cs-form-switched-off-counts', 'FormRules.cs', '            if (rule.Enabled && effects.Contains(rule.Effect))', '            if (effects.Contains(rule.Effect))'),
  cs('cs-form-required-ignored', 'FormRules.cs', '            element.Required || (RuleOf(element.Rules, "require") is { } rule && Matches(rule, scope));', '            !element.Required && (RuleOf(element.Rules, "require") is { } rule && Matches(rule, scope));'),
  cs('cs-form-require-inverted', 'FormRules.cs', '            element.Required || (RuleOf(element.Rules, "require") is { } rule && Matches(rule, scope));', '            element.Required || (RuleOf(element.Rules, "require") is { } rule && !Matches(rule, scope));'),
  cs('cs-form-message-on-hidden', 'FormRules.cs', '            else if (!owner.Hidden && Rules.MatchesGroup(conditions, global, patterns))', '            else if (Rules.MatchesGroup(conditions, global, patterns))'),
  cs('cs-form-message-in-hidden-row', 'FormRules.cs', '                    if (!owner.HiddenIn[row] && Rules.MatchesGroup(conditions, ScopeOf(repeater, row), patterns))', '                    if (Rules.MatchesGroup(conditions, ScopeOf(repeater, row), patterns))'),
  cs('cs-form-message-switched-off', 'FormRules.cs', '        foreach (var rule in form.ValidationRules.Where(rule => rule.Enabled))', '        foreach (var rule in form.ValidationRules)'),
  cs('cs-form-actions-need-a-rule', 'FormRules.cs', '=> RuleOf(rules, effect) is not { } rule || Matches(rule, global);', '=> RuleOf(rules, effect) is { } rule && Matches(rule, global);'),
  cs('cs-form-outcome-last', 'FormRules.cs', '            form.Outcomes.FirstOrDefault(outcome =>', '            form.Outcomes.LastOrDefault(outcome =>'),
  cs('cs-form-cycle-ignored', 'FormRules.cs', '        return ready.Count < nodes.Count ? throw new LogicException(LogicErrors.Cycle) : ready;', '        return ready;'),
  cs('cs-form-require-orders', 'FormRules.cs', '            if (RuleOf(element.Rules, "show", "hide") is { } visibility)', '            if (RuleOf(element.Rules, "show", "hide", "require") is { } visibility)'),
  cs('cs-form-operand-not-looked-at', 'FormRules.cs', '                    into.Add(field.Field);', '                    into.Remove(field.Field);'),
  cs('cs-form-list-not-looked-at', 'FormRules.cs', '                        Operand(item);', '                        into.Remove(string.Empty);'),
  cs('cs-form-group-not-looked-at', 'FormRules.cs', '                LooksAt(group.Conditions, into);', '                into.Remove(string.Empty);'),
  cs('cs-form-ids-twice', 'FormRules.cs', 'id is not null && ids.Add(id) ? id : throw', '(ids.Add(id ?? string.Empty) || id is not null) && id is not null ? id : throw'),
  cs('cs-form-nested-repeater', 'FormRules.cs', '                if (repeater is not null && element.Repeats)', '                if (repeater is not null && element.Repeats && element.Id.Length < 0)'),
  cs('cs-form-require-without-kind', 'FormRules.cs', ' || (effect == "require" && !holdsValue) ||', ' || (effect == "require" && !holdsValue && effect.Length == 0) ||'),
  cs('cs-form-show-and-hide', 'FormRules.cs', '(effect == "show" && seen.Contains("hide")) || (effect == "hide" && seen.Contains("show"))', '(effect == "show" && seen.Contains("show!")) || (effect == "hide" && seen.Contains("hide!"))'),
  cs('cs-form-context-hidden-used', 'FormRules.cs', '{ Lookup = id => AnswersOf(id, null, -1) };', '{ Lookup = id => context.Hidden.Contains(id, StringComparer.Ordinal) ? null : AnswersOf(id, null, -1) };'),
  cs('cs-form-grid-rows-no-subjects', 'FormRules.cs', '                    subjects[Claim(row.Id)] = new SubjectOf(row.Kind, element);', '                    Claim(row.Id);'),
  // ---- C#: where .NET differs in the calculation language (ROADMAP 5.3a) ----
  cs('cs-calc-functions-ignore-case', 'Calculations.cs',
    lines('            ["today"] = (0, 0),', '        }.ToFrozenDictionary(StringComparer.Ordinal);', '', '    private static readonly FrozenSet<string> DateFunctions'),
    lines('            ["today"] = (0, 0),', '        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);', '', '    private static readonly FrozenSet<string> DateFunctions')),
  cs('cs-calc-names-start-in-any-script', 'Calculations.cs', 'if (c != -1 && char.IsAsciiLetter((char)c))', 'if (c != -1 && char.IsLetter((char)c))', {
    equivalent: 'a name goes on in ASCII letters and digits only, so one that starts with another letter is empty and leaves that letter next, which no part of the grammar takes: the formula fails at it as before',
  }),
  cs('cs-calc-names-go-on-in-any-script', 'Calculations.cs', 'while (_position < text.Length && char.IsAsciiLetterOrDigit(text[_position]))', 'while (_position < text.Length && char.IsLetterOrDigit(text[_position]))'),
  cs('cs-calc-digits-of-any-script', 'Calculations.cs',
    lines('            var i = start;', '            while (i < text.Length && char.IsAsciiDigit(text[i]))'),
    lines('            var i = start;', '            while (i < text.Length && char.IsDigit(text[i]))')),
  cs('cs-calc-white-space-of-any-kind', 'Calculations.cs', "private static bool IsSpace(int c) => c is ' ' or 9 or 10 or 13;", 'private static bool IsSpace(int c) => c >= 0 && char.IsWhiteSpace((char)c);'),
  cs('cs-calc-places-with-a-point', 'Calculations.cs', 'int.TryParse(places.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var scale)', 'int.TryParse(places.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)'),
  cs('cs-form-aliases-ignore-case', 'FormRules.cs', 'var aliases = new Dictionary<string, string>(StringComparer.Ordinal);', 'var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);'),
];

// ---- Running ----

const args = process.argv.slice(2);
const option = (name) => {
  const at = args.indexOf(name);
  return at >= 0 ? args[at + 1] : undefined;
};
const checkOnly = args.includes('--check');
const runtime = option('--runtime');
const filter = option('--name');
if (runtime !== undefined && runtime !== 'ts' && runtime !== 'cs') throw new Error('--runtime takes ts or cs');

const names = mutants.map((mutant) => mutant.name);
const twice = names.filter((name, index) => names.indexOf(name) !== index);
if (twice.length > 0) throw new Error(`mutants named twice: ${twice.join(', ')}`);

const chosen = mutants.filter((m) => (runtime === undefined || m.runtime === runtime) && (filter === undefined || m.name.includes(filter)));
const missing = [];
for (const mutant of chosen) {
  const text = readFileSync(join(ROOT, mutant.file), 'utf8');
  const count = text.split(mutant.from).length - 1;
  if (count !== 1) missing.push(`${mutant.name}: its code is there ${count} times in ${mutant.file}`);
}
if (missing.length > 0) {
  console.error(missing.join(NL));
  process.exit(1);
}
if (checkOnly) {
  console.log(`mutants: ${chosen.length} mutants, each one's code there exactly once`);
  process.exit(0);
}

// Errors of a file another process holds open for a moment, as a virus scanner or an editor's watcher may on Windows.
const HELD = new Set(['EBUSY', 'EPERM', 'EACCES', 'UNKNOWN']);

/**
 * Writes a file and reads it back, trying again for up to ten seconds while another process holds it open, so that
 * neither a mutant nor putting the file back is lost to a moment's lock. It waits synchronously, so Ctrl+C can use it.
 */
function write(path, text) {
  const until = Date.now() + 10_000;
  for (;;) {
    try {
      writeFileSync(path, text);
      if (readFileSync(path, 'utf8') === text) return;
    } catch (error) {
      if (!HELD.has(error.code) || Date.now() > until) {
        throw new Error(`could not write ${path} (${error.code}); if a mutant is in it, put the file back from git`, { cause: error });
      }
    }
    if (Date.now() > until) throw new Error(`${path} does not read back as written; put the file back from git`);
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 100);
  }
}

// The file being changed, so that Ctrl+C puts it back.
let current = null;
process.on('SIGINT', () => {
  if (current !== null) write(current.path, current.text);
  process.exit(130);
});

/** Runs a command; past its time, its whole process tree is stopped. */
function run(command, commandArgs, cwd, timeout, env = {}) {
  return new Promise((resolve) => {
    const child = spawn(command, commandArgs, { cwd, env: { ...process.env, ...env }, windowsHide: true });
    let output = '';
    child.stdout.on('data', (chunk) => (output += chunk));
    child.stderr.on('data', (chunk) => (output += chunk));
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === 'win32') spawnSync('taskkill', ['/T', '/F', '/PID', String(child.pid)]);
      else child.kill('SIGKILL');
    }, timeout);
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code, output, timedOut });
    });
  });
}

/** caught, survived or broken (the mutant does not compile or run, so it says nothing). */
async function verdict(mutant) {
  if (mutant.runtime === 'ts') {
    const result = await run(process.execPath, [VITEST, 'run', ...(mutant.tests ?? VECTORS_TS)], join(ROOT, LOGIC), TIME.ts);
    if (result.timedOut) return 'caught';
    if (result.code === 0) return 'survived';
    return /Tests\s+\d+ failed/.test(result.output) ? 'caught' : 'broken';
  }
  const result = await run(
    'dotnet',
    ['test', UNIT, '-nologo', '--filter', mutant.tests ?? VECTORS_CS],
    ROOT,
    TIME.cs,
    { MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0' },
  );
  if (result.timedOut) return 'caught';
  if (/error CS\d+|error MSB\d+|Build FAILED/.test(result.output)) return 'broken';
  if (/Failed:\s+[1-9]/.test(result.output)) return 'caught';
  return result.code === 0 ? 'survived' : 'broken';
}

const problems = [];
let caught = 0;
let equivalent = 0;
for (const mutant of chosen) {
  const path = join(ROOT, mutant.file);
  const text = readFileSync(path, 'utf8');
  current = { path, text };
  let outcome;
  try {
    write(path, text.replace(mutant.from, () => mutant.to));
    outcome = await verdict(mutant);
  } finally {
    write(path, text);
    current = null;
  }
  let line;
  if (mutant.equivalent !== undefined) {
    if (outcome === 'survived') {
      equivalent++;
      line = `equivalent (${mutant.equivalent})`;
    } else {
      line = `${outcome.toUpperCase()}, but written down as equivalent`;
      problems.push(mutant.name);
    }
  } else if (outcome === 'caught') {
    caught++;
    line = 'caught';
  } else {
    line = outcome.toUpperCase();
    problems.push(mutant.name);
  }
  console.log(`mutant ${mutant.runtime}/${mutant.name}: ${line}`);
}

console.log(`mutants: ${chosen.length} run, ${caught} caught, ${equivalent} equivalent, ${problems.length} to fix`);
if (problems.length > 0) {
  console.error(`mutants to fix: ${problems.join(', ')}`);
  process.exit(1);
}
