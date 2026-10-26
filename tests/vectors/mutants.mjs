#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The mutation check of the shared logic (ROADMAP 5.2a, ADR-0061). Each mutant changes one piece of code in one
 * runtime; that runtime's vector runner runs, and the file is put back. A mutant that no vector catches survives, and
 * the check fails unless the mutant is written down here as an equivalent change, with the reason. A mutant written
 * down as equivalent that a vector catches fails the check too, so the notes stay true.
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
  ts('form-container-ignored', 'form.ts', '    else element.hidden = element.container?.hidden === true || !shown(element, global);', '    else element.hidden = !shown(element, global);'),
  ts('form-repeater-hidden-ignored', 'form.ts', 'repeater.hidden || !shown(element, scopeOf(repeater, row)))', '!shown(element, scopeOf(repeater, row)))'),
  ts('form-hidden-answers-kept', 'form.ts', '    if (owner.repeater === null) return owner.hidden ? undefined : own(values, id);', '    if (owner.repeater === null) return own(values, id);'),
  ts('form-hidden-row-answers-kept', 'form.ts', '    if (owner.repeater === repeater) return owner.hiddenIn[row] === true ? undefined : own(rows[row] ?? {}, id);', '    if (owner.repeater === repeater) return own(rows[row] ?? {}, id);'),
  ts('form-row-scope-lost', 'form.ts', '    if (owner.repeater === repeater) return', '    if (owner.repeater === repeater && repeater === null) return'),
  ts('form-hidden-rows-seen-outside', 'form.ts', '    return rows.flatMap((answer, r) => (owner.hiddenIn[r] === true ? [] : (own(answer, id) ?? [])));', '    return rows.flatMap((answer) => own(answer, id) ?? []);'),
  ts('form-show-inverted', 'form.ts', "    return matchesGroup(rule, scope, patterns) === (rule.effect === 'show');", "    return matchesGroup(rule, scope, patterns) === (rule.effect === 'hide');"),
  ts('form-switched-off-counts', 'form.ts', '  rules.find((rule) => rule.enabled && effects.includes(rule.effect));', '  rules.find((rule) => effects.includes(rule.effect));'),
  ts('form-required-ignored', 'form.ts', '    if (element.required) return true;' + NL, ''),
  ts('form-require-unmatched', 'form.ts', '    return rule !== undefined && matchesGroup(rule, scope, patterns);', '    return rule !== undefined;'),
  ts('form-hidden-required', 'form.ts',
    lines('    } else if (element.hidden) hidden.push(element.id);', '    else if (element.kind !== null && requires(element, global)) required.push(element.id);'),
    lines('    } else {', '      if (element.hidden) hidden.push(element.id);', '      if (element.kind !== null && requires(element, global)) required.push(element.id);', '    }')),
  ts('form-hidden-row-required', 'form.ts', '        else if (element.kind !== null && requires(element, scopeOf(repeater, at))) row.required.push(element.id);', '        if (element.kind !== null && requires(element, scopeOf(repeater, at))) row.required.push(element.id);'),
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
  cs('cs-form-hidden-answers-kept', 'FormRules.cs', '                return owner.Hidden ? null : answers.Values.GetValueOrDefault(id);', '                return answers.Values.GetValueOrDefault(id);'),
  cs('cs-form-hidden-row-answers-kept', 'FormRules.cs', '                return owner.HiddenIn[row] ? null : rows[row].GetValueOrDefault(id);', '                return rows[row].GetValueOrDefault(id);'),
  cs('cs-form-hidden-rows-seen-outside', 'FormRules.cs', '                if (!owner.HiddenIn[r] && rows[r].TryGetValue(id, out var given))', '                if (rows[r].TryGetValue(id, out var given))'),
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
  cs('cs-form-ids-twice', 'FormRules.cs', 'id is not null && ids.Add(id) ? id : throw', '(ids.Add(id ?? string.Empty) || id is not null) && id is not null ? id : throw'),
  cs('cs-form-nested-repeater', 'FormRules.cs', '                if (repeater is not null && element.Repeats)', '                if (repeater is not null && element.Repeats && element.Id.Length < 0)'),
  cs('cs-form-require-without-kind', 'FormRules.cs', ' || (effect == "require" && !holdsValue) ||', ' || (effect == "require" && !holdsValue && effect.Length == 0) ||'),
  cs('cs-form-show-and-hide', 'FormRules.cs', '(effect == "show" && seen.Contains("hide")) || (effect == "hide" && seen.Contains("show"))', '(effect == "show" && seen.Contains("show!")) || (effect == "hide" && seen.Contains("hide!"))'),
  cs('cs-form-context-hidden-used', 'FormRules.cs', '{ Lookup = id => AnswersOf(id, null, -1) };', '{ Lookup = id => context.Hidden.Contains(id, StringComparer.Ordinal) ? null : AnswersOf(id, null, -1) };'),
  cs('cs-form-grid-rows-no-subjects', 'FormRules.cs', '                    subjects[Claim(row.Id)] = new SubjectOf(row.Kind, element);', '                    Claim(row.Id);'),
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

// The file being changed, so that Ctrl+C puts it back.
let current = null;
process.on('SIGINT', () => {
  if (current !== null) writeFileSync(current.path, current.text);
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
    writeFileSync(path, text.replace(mutant.from, () => mutant.to));
    outcome = await verdict(mutant);
  } finally {
    writeFileSync(path, text);
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
