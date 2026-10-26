// Merge-tag templates an editor might write, and many no editor would (ROADMAP 5.3b, ADR-0064), the same as Core's
// TemplateFuzzTests and TemplateInjectionTests:
// - 10,000 random templates, built from the grammar in every context and then broken by chance, are parsed and rendered
//   over random values with each culture of merge-tags.json. None throws, each takes at most 50 ms (the fastest of three
//   runs, so that a garbage collection or the first compilation of a path does not count against it), the output keeps
//   its limit, the problems come in the order of the template, and rendering adds only the problems values cause.
// - Hostile values through every filter, wherever a tag may stand, add no element, attribute, header line, address part
//   or JSON token; wherever a tag may not stand, the template renders as it is written.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  MAX_TAG_FILTERS,
  MAX_TEMPLATE_OUTPUT,
  MAX_TEMPLATE_TAGS,
  parseTemplate,
  renderTemplate,
  TEMPLATE_CONTEXTS,
  TEMPLATE_WARNINGS,
  type CultureFormats,
  type MergeValue,
  type ParsedTemplate,
  type RenderedTemplate,
  type TemplateContext,
  type TemplateProblemCode,
} from '../src/index.ts';
import { vectorsFolder } from '../tools/repository.ts';
import { Random, show } from './fuzzing.ts';

const TEMPLATES = 10_000;
const SEED = 5302;
const RUNS = 3;
const BUDGET_MS = 50;

const ch = (...codes: number[]): string => String.fromCharCode(...codes);
const BS = ch(92);
const CR = ch(13);
const LF = ch(10);
const TAB = ch(9);
const EMOJI = ch(0xd83d, 0xde00);

/** The cultures of merge-tags.json, so the templates print with the formats the vectors use, the odd ones included. */
const cultures: readonly CultureFormats[] = Object.values(
  (
    JSON.parse(readFileSync(join(vectorsFolder(), 'merge-tags.json'), 'utf8')) as {
      readonly defaults: { readonly cultures: Readonly<Record<string, CultureFormats>> };
    }
  ).defaults.cultures,
);

/** What a template can come to: no problem, or each problem. */
const outcomesToReach = [
  'clean',
  'malformed_tag',
  'unclosed_tag',
  'tag_in_markup',
  'tag_in_escape',
  'tag_before_path',
  'invalid_argument',
  'filter_not_for_context',
  'filter_after_encoding',
  'too_many_tags',
  'too_many_filters',
  'too_long',
  'unknown_path',
  'unknown_filter',
] as const satisfies readonly (TemplateProblemCode | 'clean')[];

// ---- Random templates ----

// The paths values come from; an unknown one now and then, and heads the grammar refuses.
const paths = [
  'field.name',
  'field.qty',
  'field.birth',
  'field.when',
  'field.extras',
  'field.empty',
  'field.long',
  'query.ref',
  'form.title',
  'entry.number',
];
const unknownPaths = ['nope.x', 'field.missing', 'field.Name'];
const oddHeads = ['Field', '1', '.x', 'field.', 'field..x', '-x', '', 'field.$', 'field name'];
const literals = ['"x"', '"a ' + BS + '" b"', '"}}"', '"{{"', '""', '"' + BS + BS + '"', '"<b>&"', '"open'];

/** Each filter's arguments: those it takes, and some it refuses. */
const filterArguments: ReadonlyMap<string, readonly (readonly string[])[]> = new Map([
  ['default', [['"web"'], ['""'], ['"<x>"'], [], ['1'], ['"a"', '"b"']]],
  ['upper', [[], [], [], ['"x"']]],
  ['lower', [[], [], [], ['1']]],
  ['truncate', [['5'], ['0'], ['1', '"..."'], ['100000'], ['100001'], ['1.5'], ['-1'], ['"5"'], [], ['3', '4']]],
  ['date', [[], ['"short"'], ['"medium"'], ['"long"'], ['"time"'], ['"dateTime"'], ['"iso"'], ['"iso8601"'], ['1']]],
  ['number', [[], ['0'], ['2'], ['20'], ['21'], ['"2"'], ['2', '3']]],
  ['currency', [['"EUR"'], ['"CHF"'], ['"JPY"', '0'], ['"KWD"'], ['"eur"'], ['"EURO"'], ['1'], [], ['"EUR"', '21']]],
  ['join', [[], ['"; "'], ['"</p>"'], ['1']]],
  ['html', [[], [], [], ['"x"']]],
  ['url', [[], [], [], ['1']]],
  ['json', [[], [], [], ['""']]],
]);
const filterNames = [...filterArguments.keys()];
const oddFilters = ['shout', 'Upper', 'default2', 'escape'];
const oddArguments: readonly (readonly string[])[] = [[], ['1'], ['"x"']];
const spaces = [' ', ' ', '', '', '  ', TAB];

const words = ['Hello ', 'a', ' ', 'Dear ', '!', '.', 'x1'];

// The characters of every context's syntax, so that tags land in each kind of place.
const snippets = [
  '<p>',
  '</p>',
  '<a href="',
  '">',
  "'",
  '"',
  '<script>',
  '</script>',
  '<style>',
  '<!--',
  '-->',
  '<![CDATA[',
  ']]>',
  '<?x ',
  '?>',
  '<!DOCTYPE x>',
  '<br/>',
  '<',
  '>',
  '&amp;',
  '<textarea>',
  '</TEXTAREA>',
  '<title>',
  '</title>',
  '<plaintext>',
  '{"a": ',
  '"b"',
  '"' + BS,
  BS,
  '}',
  '[',
  ']',
  ',',
  ': ',
  'https://',
  'http://',
  'x.test',
  '/path',
  '?q=',
  '#f',
  'javascript:',
  '//',
  'mailto:',
  '&',
  CR + LF,
  LF,
  TAB,
  '{',
  '}',
  '{{',
  '}}',
  '|',
  ch(0xe9),
  EMOJI,
  ch(0xd800),
  ch(0),
];

// What a mistake puts into a template: the grammar's own characters, and some it does not have.
const noise = [
  ...['{', '}', '|', '"', ':', ',', '.', ' ', '-', '_', '<', '>', '/', "'", 'a', 'b', 'Z', '0', '9'],
  BS,
  CR,
  LF,
  ch(0xe9),
  ch(0xd800),
  ch(0),
];

const texts: readonly MergeValue[] = [
  'Anna',
  '',
  '  ',
  '<b>"x"</b>',
  "O'Brien & Co",
  'a' + CR + LF + 'b',
  'https://evil.test/?a=1&b=2',
  ch(0xd800),
  EMOJI + ' ' + ch(0x2028),
  '{{ field.name }}',
  'x'.repeat(3000),
  ch(0x130) + 'stanbul ' + ch(0x1f80),
  'Stra' + ch(0xdf) + 'e',
];
const numbers: readonly MergeValue[] = [
  { text: '1,234.5', number: '1234.5' },
  { text: '-0.5', number: '-0.5' },
  { text: 'x', number: '1e5' },
  { text: '12', number: '9'.repeat(40) },
  '42',
  '-7.125',
  'abc',
  '0.000000000000000000005',
  '-0',
];
const dates: readonly MergeValue[] = [
  { text: '15.06.2026', date: '2026-06-15' },
  { text: 'x', date: '2026-02-30' },
  '2026-06-15',
  '2026-06-15T08:05:09',
  '23:59:59',
  '2026-06-15T24:00',
  'yesterday',
  '0001-01-01',
  '9999-12-31T23:59:59',
];
const lists: readonly MergeValue[] = [
  [],
  ['a', 'b'],
  ['<i>', '"q"', ''],
  [{ text: '1', number: '1' }, '2'],
  [''],
  [{ text: 'x', date: '2026-01-01' }],
];
// Long enough that two or three of them pass the output's limit, one ending in a surrogate pair.
const longs: readonly MergeValue[] = ['x'.repeat(34_000), 'y'.repeat(50_000), 'z'.repeat(69_998) + EMOJI];

const families: ReadonlyMap<string, readonly MergeValue[]> = new Map([
  ['field.name', texts],
  ['field.qty', numbers],
  ['field.birth', dates],
  ['field.when', dates],
  ['field.extras', lists],
  ['field.empty', ['', [], ['']]],
  ['field.long', longs],
  ['query.ref', texts],
  ['form.title', texts],
  ['entry.number', numbers],
]);
const anyFamily = [texts, numbers, dates, lists];

interface RandomTemplate {
  readonly text: string;
  readonly context: TemplateContext;
}

/** Random templates from the grammar of ADR-0064 in every context, broken now and then, and random values for them. */
class TemplateGenerator {
  private readonly random: Random;

  constructor(seed: number) {
    this.random = new Random(seed);
  }

  template(): RandomTemplate {
    const r = this.random;
    const context = r.pick(TEMPLATE_CONTEXTS);
    let text: string;
    switch (r.next(50)) {
      case 0:
        // As many tags as a template may have, a little fewer or more.
        text = Array.from({ length: MAX_TEMPLATE_TAGS + r.between(-1, 3) }, () => '{{ field.name }}').join(' ');
        break;
      case 1:
        // As many filters as a tag may have, a little fewer or more.
        text = '{{ field.name' + ' | upper'.repeat(MAX_TAG_FILTERS + r.between(-1, 3)) + ' }}';
        break;
      case 2:
        // Output about as long as a template may give.
        text =
          r.next(2) === 0
            ? 'a'.repeat(MAX_TEMPLATE_OUTPUT - r.next(10)) + '{{ field.name }}'
            : '{{ field.long }}'.repeat(r.between(2, 4));
        break;
      default:
        text = this.built();
    }
    // Then, now and then, broken: characters dropped, put in or doubled.
    const mistakes = r.next(6) === 0 ? r.between(1, 4) : 0;
    for (let m = 0; m < mistakes; m++) text = this.broken(text);
    return { text, context };
  }

  /** A value for each path, or none now and then: mostly of the path's kind, sometimes of any. */
  values(): ReadonlyMap<string, MergeValue> {
    const r = this.random;
    const values = new Map<string, MergeValue>();
    for (const path of paths) {
      if (r.next(8) === 0) continue;
      const family = path !== 'field.long' && r.next(10) === 0 ? r.pick(anyFamily) : (families.get(path) ?? texts);
      values.set(path, r.pick(family));
    }
    return values;
  }

  culture(): CultureFormats {
    return this.random.pick(cultures);
  }

  private built(): string {
    const r = this.random;
    let text = '';
    const pieces = r.between(1, 30);
    for (let p = 0; p < pieces; p++) {
      const k = r.next(10);
      text += k < 4 ? this.tag() : k < 7 ? r.pick(words) : r.pick(snippets);
    }
    return text;
  }

  private tag(): string {
    const r = this.random;
    const k = r.next(20);
    const head = k < 14 ? r.pick(paths) : k < 16 ? r.pick(unknownPaths) : k < 19 ? r.pick(literals) : r.pick(oddHeads);
    const count = r.next(40) === 0 ? r.between(4, 13) : r.pick([0, 0, 1, 1, 1, 2, 2, 3]);
    let filters = '';
    for (let f = 0; f < count; f++) {
      const name = r.next(25) === 0 ? r.pick(oddFilters) : r.pick(filterNames);
      const args = r.pick(filterArguments.get(name) ?? oddArguments);
      filters += this.space() + '|' + this.space() + name;
      if (args.length > 0) filters += this.space() + ':' + this.space() + args.join(',' + this.space());
    }
    return '{{' + this.space() + head + filters + this.space() + '}}';
  }

  private space(): string {
    return this.random.pick(spaces);
  }

  private broken(text: string): string {
    const r = this.random;
    const at = r.next(text.length + 1);
    switch (r.next(3)) {
      case 0: {
        if (text.length === 0) return text;
        const i = Math.min(at, text.length - 1);
        return text.slice(0, i) + text.slice(i + 1);
      }
      case 1:
        return text.slice(0, at) + r.pick(noise) + text.slice(at);
      default:
        return text.slice(0, at) + text.slice(at, Math.min(text.length, at + r.between(1, 8))) + text.slice(at);
    }
  }
}

/** What is wrong with a rendering, or null: the output's limit, the problems' order, and the problems parsing found. */
function broken(text: string, parsed: ParsedTemplate, rendered: RenderedTemplate): string | null {
  if (rendered.text.length > MAX_TEMPLATE_OUTPUT) return `${rendered.text.length} code units of output`;
  for (let k = 0; k < rendered.problems.length; k++) {
    const at = rendered.problems[k]?.at ?? -1;
    if (at < 0 || at > text.length || (k > 0 && at < (rendered.problems[k - 1]?.at ?? 0))) {
      return 'problems out of order or outside the template: ' + JSON.stringify(rendered.problems);
    }
  }
  // Until a limit stops it, rendering reports what parsing found, and beside it only the paths no source knows.
  if (rendered.problems.every((problem) => problem.code !== 'too_long')) {
    const found = rendered.problems.filter((problem) => problem.code !== 'unknown_path');
    if (JSON.stringify(found) !== JSON.stringify(parsed.problems)) {
      return `rendering reported ${JSON.stringify(found)}, parsing ${JSON.stringify(parsed.problems)}`;
    }
  }
  return null;
}

describe('random templates', () => {
  it('10,000 render without throwing, each in at most 50 ms, within the limit and with problems in order', () => {
    const generator = new TemplateGenerator(SEED);
    const outcomes = new Map<string, number>();
    const failures: string[] = [];
    let slowest = { ms: 0, template: '' };
    for (let i = 0; i < TEMPLATES; i++) {
      const { text, context } = generator.template();
      const values = generator.values();
      const culture = generator.culture();
      const once = (): readonly [ParsedTemplate, RenderedTemplate] => {
        const parsed = parseTemplate(text, context);
        return [parsed, renderTemplate(parsed, (path) => values.get(path), culture)];
      };
      let fastest = Number.POSITIVE_INFINITY;
      let result: readonly [ParsedTemplate, RenderedTemplate];
      try {
        result = once();
        for (let run = 0; run < RUNS; run++) {
          const start = performance.now();
          once();
          fastest = Math.min(fastest, performance.now() - start);
        }
      } catch (error) {
        failures.push(`template ${i} in ${context}: ${String(error)} for ${show(text)}`);
        continue;
      }
      const wrong = broken(text, ...result);
      if (wrong !== null) failures.push(`template ${i} in ${context}: ${wrong} for ${show(text)}`);
      const codes = new Set<string>(result[1].problems.map((problem) => problem.code));
      for (const code of codes.size === 0 ? ['clean'] : codes) outcomes.set(code, (outcomes.get(code) ?? 0) + 1);
      if (fastest > BUDGET_MS) failures.push(`template ${i} in ${context}: ${fastest.toFixed(1)} ms for ${show(text)}`);
      if (fastest > slowest.ms) slowest = { ms: fastest, template: text };
    }

    console.log(
      [...outcomes]
        .sort(([a], [b]) => (a < b ? -1 : 1))
        .map(([code, count]) => `${code} ${count}`)
        .join(', '),
    );
    console.log(`slowest: ${slowest.ms.toFixed(3)} ms for ${show(slowest.template)}`);
    expect(failures).toEqual([]);
    // The templates reach every outcome, so the generator explores the language and not one corner of it.
    expect(
      outcomesToReach.filter((code) => (outcomes.get(code) ?? 0) < TEMPLATES / 500),
      'outcomes reached fewer than 20 times',
    ).toEqual([]);
  }, 300_000);

  it('the generator gives the same templates for the same seed', () => {
    const first = new TemplateGenerator(SEED);
    const second = new TemplateGenerator(SEED);
    const take = (generator: TemplateGenerator): RandomTemplate[] =>
      Array.from({ length: 100 }, () => generator.template());
    expect(take(first)).toEqual(take(second));
  });
});

// ---- Hostile values ----

const SECRET = 'SECRET-7f3a';

/** Values meant to break out of their place: markup, quotes, addresses, line breaks, JSON, odd code units and tags. */
const hostile: readonly MergeValue[] = [
  '<script>alert(1)</script>',
  '"><img src=x onerror=alert(1)>',
  "' onmouseover='alert(1)",
  '</p><p>',
  '&lt;b&gt; & &amp;',
  '<![CDATA[x]]>',
  '<!-- x -->',
  ']]>',
  'javascript:alert(1)',
  '//evil.test/x',
  'https://evil.test',
  '../../admin',
  '?a=1&b=2#f',
  '%2F%2e%00',
  BS + BS + 'evil.test',
  'x' + CR + LF + 'Set-Cookie: a=b',
  'x' + LF + 'Bcc: a@evil.test',
  'x' + CR + 'y',
  'x' + ch(0x2028) + 'y' + ch(0x2029),
  'x' + ch(0x85) + 'y',
  TAB + 'x' + ch(0x7f),
  '"}, "admin": true, "x": {"',
  BS + '"',
  ch(0) + ch(0x1f),
  '</script><script>alert(1)</script>',
  ch(0xd800) + 'x' + ch(0xdc00),
  ch(0xfffe) + ch(0xffff),
  '{{ field.secret }}',
  '}}{{',
  '',
  [],
  ['a"b', '<c>', 'd' + CR + LF + 'e'],
  [
    { text: '1</p>', number: '12' },
    { text: '<x>', number: '1e5' },
  ],
  { text: '<b>', date: '2026-01-01' },
  { text: '"', number: '-0.5' },
];

/** Every filter, alone and in the chains an editor might write, with arguments that are hostile too. */
const chains = [
  '',
  ' | upper',
  ' | lower',
  ' | truncate: 3',
  ' | truncate: 2, "<' + BS + '"&"',
  ' | default: "' + BS + '"><x>"',
  ' | join',
  ' | join: "</p>"',
  ' | date',
  ' | date: "long"',
  ' | number: 2',
  ' | currency: "EUR"',
  ' | html',
  ' | url',
  ' | json',
  ' | upper | html',
  ' | join: "&" | url',
  ' | truncate: 4 | json',
  ' | default: "' + BS + '"}" | json',
];

const isControl = (unit: number): boolean => unit < 0x20 || (unit >= 0x7f && unit <= 0x9f);
const isHigh = (unit: number): boolean => unit >= 0xd800 && unit <= 0xdbff;
const isLow = (unit: number): boolean => unit >= 0xdc00 && unit <= 0xdfff;

/** The characters that start or end markup or an attribute value. */
const MARKUP = ['<', '>', '"', "'", '&'];

/** Text content with no markup but the line breaks the `html` filter writes, and no references but these. */
function isMarkupText(piece: string, references: readonly string[], breaks: boolean): boolean {
  for (let i = 0; i < piece.length;) {
    const reference = references.find((name) => piece.startsWith(name, i));
    if (reference !== undefined) i += reference.length;
    else if (breaks && piece.startsWith('<br>', i)) i += 4;
    else if (MARKUP.includes(piece.charAt(i))) return false;
    else i++;
  }
  return true;
}

const isHtmlText = (piece: string): boolean => isMarkupText(piece, ['&amp;', '&lt;', '&gt;', '&quot;', '&#39;'], true);

/** XML text content, with every code unit one XML 1.0 can carry. */
function isXmlText(piece: string): boolean {
  for (let i = 0; i < piece.length; i++) {
    const unit = piece.charCodeAt(i);
    if (isHigh(unit) && isLow(piece.charCodeAt(i + 1))) i++;
    else if (!(
      unit === 9 ||
      unit === 10 ||
      unit === 13 ||
      (unit >= 0x20 && unit <= 0xfffd && !isHigh(unit) && !isLow(unit))
    ))
      return false;
  }
  return isMarkupText(piece, ['&amp;', '&lt;', '&gt;', '&quot;', '&apos;'], false);
}

/** One header line: no line break and no control character. */
function isHeaderLine(output: string): boolean {
  for (let i = 0; i < output.length; i++) {
    const unit = output.charCodeAt(i);
    if (isControl(unit) || unit === 0x2028 || unit === 0x2029) return false;
  }
  return true;
}

/** Percent-encoded: only the characters kept, and `%` with two capital hex digits. */
function isPercentEncoded(piece: string, kept: string): boolean {
  const hex = '0123456789ABCDEF';
  for (let i = 0; i < piece.length; i++) {
    const c = piece.charAt(i);
    if (c === '%') {
      if (!hex.includes(piece.charAt(i + 1)) || !hex.includes(piece.charAt(i + 2)) || i + 2 >= piece.length)
        return false;
      i += 2;
    } else if (!/[A-Za-z0-9]/.test(c) && !kept.includes(c)) {
      return false;
    }
  }
  return true;
}

/** No character a script or markup around the JSON could read: `<`, `>`, `&`, U+2028 and U+2029 are escaped. */
const isScriptSafe = (output: string): boolean =>
  !/[<>&]/.test(output) && !output.includes(ch(0x2028)) && !output.includes(ch(0x2029));

function isJsonStringContent(piece: string): boolean {
  try {
    return typeof JSON.parse('"' + piece + '"') === 'string' && isScriptSafe(piece);
  } catch {
    return false;
  }
}

const isScalar = (value: unknown): boolean => typeof value === 'string' || typeof value === 'number';

/**
 * A JSON object with exactly these keys, the last holding what a tag writes: a string inside a string literal,
 * outside one a string, a number or an array of them.
 */
const isJsonObject =
  (keys: readonly string[], inString: boolean) =>
  (output: string): boolean => {
    let parsed: unknown;
    try {
      parsed = JSON.parse(output);
    } catch {
      return false;
    }
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return false;
    const document = parsed as Readonly<Record<string, unknown>>;
    const value = document[keys[keys.length - 1] ?? ''];
    const written = inString
      ? typeof value === 'string'
      : isScalar(value) || (Array.isArray(value) && value.every(isScalar));
    return JSON.stringify(Object.keys(document)) === JSON.stringify(keys) && written && isScriptSafe(output);
  };

interface Place {
  readonly context: TemplateContext;
  readonly template: (tag: string) => string;
  /** Whether the output holds the template's own text and, where the tag stood, nothing that changes its structure. */
  readonly safe: (output: string) => boolean;
}

/** One tag between two texts, whose piece of the output must pass the check. */
const between = (
  context: TemplateContext,
  before: string,
  after: string,
  check: (piece: string) => boolean,
): Place => ({
  context,
  template: (tag) => before + tag + after,
  safe: (output) =>
    output.length >= before.length + after.length &&
    output.startsWith(before) &&
    output.endsWith(after) &&
    check(output.slice(before.length, output.length - after.length)),
});

/** One tag between two texts, whose whole output must pass the check. */
const around = (
  context: TemplateContext,
  before: string,
  after: string,
  check: (output: string) => boolean,
): Place => ({
  context,
  template: (tag) => before + tag + after,
  safe: check,
});

const isUrlComponent = (piece: string): boolean => isPercentEncoded(piece, '-._~');
const isFormValue = (piece: string): boolean => isPercentEncoded(piece, '*-._+');

/** Where a tag may stand, in every context whose output has a structure. */
const allowed: readonly Place[] = [
  between('html', '<p>', '</p>', isHtmlText),
  between('html', '<div class="a">Hi ', '!</div>', isHtmlText),
  between('html', '<script>x</script>', '', isHtmlText),
  between('html', '<!-- c -->', '<br>', isHtmlText),
  between('html', '', '', isHtmlText),
  between('html', '<a"b>', '', isHtmlText),
  between('html', '<p a=x"y>', '</p>', isHtmlText),
  between('html', '<p a="x>y">', '</p>', isHtmlText),
  between('html', '<script"x>', '', isHtmlText),
  between('html', '<!-->', '', isHtmlText),
  between('html', '<!-- a --!>', '', isHtmlText),
  between('html', '</ a="b>', '', isHtmlText),
  between('html', '<svg><title>Logo</title></svg>', '', isHtmlText),
  between('html', '<noscript>x</noscript>', '', isHtmlText),
  between('html', '<script></script<</script>', '', isHtmlText),
  between('xml', '<a>', '</a>', isXmlText),
  between('xml', '<![CDATA[x]]>', '', isXmlText),
  between('xml', '<!DOCTYPE a SYSTEM "b>c">', '', isXmlText),
  between('header', 'Re: ', '', isHeaderLine),
  between('header', '', ' (form)', isHeaderLine),
  between('url', 'https://example.test/a?q=', '&x=1', isUrlComponent),
  between('url', '/path/', '', isUrlComponent),
  between('url', '?q=', '', isUrlComponent),
  between('url', '#', '', isUrlComponent),
  between('url', 'https://example.test/', '/', isUrlComponent),
  between('form', 'a=1&b=', '', isFormValue),
  between('jsonString', 'Hello ', '!', isJsonStringContent),
  around('json', '{"a": "', '"}', isJsonObject(['a'], true)),
  around('json', '{"b": ', '}', isJsonObject(['b'], false)),
  around('json', '{"a": "x' + BS + '"y", "b": ', '}', isJsonObject(['a', 'b'], false)),
  around('json', '{"a": "x", "b": "', BS + 'n"}', isJsonObject(['a', 'b'], true)),
];

/** Where a tag may not stand: in markup, before an address's path, and after a backslash in a JSON string. */
const refused: readonly (readonly [TemplateContext, string, string, TemplateProblemCode])[] = [
  ['html', '<a href="', '">x</a>', 'tag_in_markup'],
  ['html', '<a href=', '>x</a>', 'tag_in_markup'],
  ['html', '<img ', '>', 'tag_in_markup'],
  ['html', '<p', '>', 'tag_in_markup'],
  ['html', '</', '>', 'tag_in_markup'],
  ['html', '<', '', 'tag_in_markup'],
  ['html', "<p title='", "'>", 'tag_in_markup'],
  ['html', '<script>', '</script>', 'tag_in_markup'],
  ['html', '<script>if (a</b) ', '</script>', 'tag_in_markup'],
  ['html', '<STYLE>', '</style>', 'tag_in_markup'],
  ['html', '<textarea>', '</textarea>', 'tag_in_markup'],
  ['html', '<title>', '</title>', 'tag_in_markup'],
  ['html', '<!-- ', ' -->', 'tag_in_markup'],
  ['html', '<!DOCTYPE ', '>', 'tag_in_markup'],
  ['html', '<?php ', ' ?>', 'tag_in_markup'],
  ['html', '<plaintext></plaintext>', '', 'tag_in_markup'],
  ['html', '<a x" onclick="if(a>b)', '">', 'tag_in_markup'],
  ['html', '<a b"c="d>', '" f>', 'tag_in_markup'],
  ['html', "<p a='x>", "'>", 'tag_in_markup'],
  ['html', '<a title = "x>', '">', 'tag_in_markup'],
  ['html', '<script/>', '</script>', 'tag_in_markup'],
  ['html', '{{ field.v | truncate: "<script>" }}', '', 'tag_in_markup'],
  ['html', '<p>{{ field.v | upper | html | x: 1 }}<style>', '', 'tag_in_markup'],
  ['html', '<!--><a onclick="-->', '">', 'tag_in_markup'],
  ['html', '<!-- --!><a onclick="-->', '">', 'tag_in_markup'],
  ['html', '</ a="x><a onclick="//>', '">', 'tag_in_markup'],
  ['html', '<script><!--<script></script>', '', 'tag_in_markup'],
  ['html', '<svg><style><a onclick="</style>', '">', 'tag_in_markup'],
  ['html', '<svg><foreignObject><script></svg>', '', 'tag_in_markup'],
  ['html', '<svg><![CDATA[ <a b=" ]]><a onclick="//>', '">', 'tag_in_markup'],
  ['html', '<noscript><a href="</noscript>', '">', 'tag_in_markup'],
  ['html', '<select><style><option onclick="</style>', '">', 'tag_in_markup'],
  ['xml', '<a b="', '"/>', 'tag_in_markup'],
  ['xml', '<', '/>', 'tag_in_markup'],
  ['xml', '<![CDATA[', ']]>', 'tag_in_markup'],
  ['xml', '<?pi ', '?>', 'tag_in_markup'],
  ['xml', '<!-- ', ' -->', 'tag_in_markup'],
  ['xml', '<_a b="', '"/>', 'tag_in_markup'],
  ['xml', '<!DOCTYPE a [<!ENTITY b "c">]>', '', 'tag_in_markup'],
  ['xml', '<!-->', '-->', 'tag_in_markup'],
  ['url', '', '', 'tag_before_path'],
  ['url', ' ', '', 'tag_before_path'],
  ['url', 'https://', '', 'tag_before_path'],
  ['url', 'https://', '/x', 'tag_before_path'],
  ['url', 'https://user@', '/', 'tag_before_path'],
  ['url', 'http', '', 'tag_before_path'],
  ['url', 'java', ':alert(1)', 'tag_before_path'],
  ['url', 'javascript:', '', 'tag_before_path'],
  ['url', 'javascript:/', '', 'tag_before_path'],
  ['url', 'mailto:', '', 'tag_before_path'],
  ['url', 'data:', '', 'tag_before_path'],
  ['url', '//', '', 'tag_before_path'],
  ['url', BS + BS, '', 'tag_before_path'],
  ['url', 'HTTPS://', '', 'tag_before_path'],
  ['url', 'https:' + TAB + '//', '', 'tag_before_path'],
  ['url', 'https:' + LF + '//', '', 'tag_before_path'],
  ['json', '{"a": "' + BS, '"}', 'tag_in_escape'],
  ['json', '["' + BS, '"]', 'tag_in_escape'],
  ['jsonString', 'x' + BS, '', 'tag_in_escape'],
];

const sources =
  (value: MergeValue) =>
  (path: string): MergeValue | undefined =>
    path === 'field.v' ? value : path === 'field.secret' ? SECRET : undefined;

describe('hostile values', () => {
  it('add no element, attribute, header line, address part or JSON token where a tag may stand', () => {
    const failures: string[] = [];
    let checked = 0;
    let renders = 0;
    for (const culture of cultures) {
      for (const place of allowed) {
        for (const chain of chains) {
          const text = place.template('{{ field.v' + chain + ' }}');
          const parsed = parseTemplate(text, place.context);
          for (const value of hostile) {
            const rendered = renderTemplate(parsed, sources(value), culture);
            renders++;
            // A tag with an error renders as it is written, which is the editor's own text.
            const asWritten = rendered.problems.some((problem) => !TEMPLATE_WARNINGS.has(problem.code));
            const safe = asWritten ? rendered.text === text : place.safe(rendered.text);
            if (!asWritten) checked++;
            if (!safe || rendered.text.includes(SECRET)) {
              failures.push(
                `${place.context}: ${show(text)} with ${show(JSON.stringify(value))} gave ${show(rendered.text)}`,
              );
            }
          }
        }
      }
    }
    expect(failures.slice(0, 20)).toEqual([]);
    // Most values reach the output, so the checks are not empty.
    expect(checked).toBeGreaterThan(renders / 2);
  });

  it('leave the template as it is written where a tag may not stand', () => {
    const failures: string[] = [];
    for (const [context, before, after, code] of refused) {
      for (const chain of ['', ' | url', ' | json', ' | html']) {
        const text = before + '{{ field.v' + chain + ' }}' + after;
        const parsed = parseTemplate(text, context);
        for (const value of hostile) {
          for (const culture of cultures) {
            const rendered = renderTemplate(parsed, sources(value), culture);
            if (rendered.text !== text || !rendered.problems.some((problem) => problem.code === code)) {
              failures.push(
                `${context}: ${show(text)} with ${show(JSON.stringify(value))} gave ${show(rendered.text)}`,
              );
            }
          }
        }
      }
    }
    expect(failures.slice(0, 20)).toEqual([]);
  });
});

// No vector can hold a lone surrogate, since .NET's JSON reader refuses one, so this test and Core's
// TemplateInjectionTests cover them.
describe('lone surrogates', () => {
  it('become U+FFFD wherever the output must be well formed', () => {
    const lone = 'a' + ch(0xd800) + 'b' + ch(0xdc00);
    const r = ch(0xfffd);
    const bytes = '%EF%BF%BD';
    const cases: readonly (readonly [TemplateContext, string, string])[] = [
      ['xml', '{{ field.v }}', 'a' + r + 'b' + r],
      ['url', '/a/{{ field.v }}', '/a/a' + bytes + 'b' + bytes],
      ['form', '{{ field.v }}', 'a' + bytes + 'b' + bytes],
      ['jsonString', '{{ field.v }}', 'a' + r + 'b' + r],
      ['json', '{{ field.v }}', '"a' + r + 'b' + r + '"'],
      ['text', '{{ field.v | url }}', 'a' + bytes + 'b' + bytes],
      ['text', '{{ field.v | json }}', '"a' + r + 'b' + r + '"'],
    ];
    const culture = cultures[0];
    if (culture === undefined) throw new Error('merge-tags.json has no culture');
    for (const [context, template, expected] of cases) {
      const rendered = renderTemplate(parseTemplate(template, context), sources(lone), culture);
      expect(rendered.text, `${context}: ${template}`).toBe(expected);
    }
  });
});
