// Merge tags (ROADMAP section 4.3.3, ADR-0014, ADR-0064), the same in C# (Core's MergeTags): a template's text with
// `{{ source.path | filter: arg }}` tags, parsed once for the context it renders in, then rendered over the values its
// sources give. Nothing in a template runs as code, and neither parsing nor rendering throws, whatever the template,
// the values or the culture data.
import {
  DATE_FORMATS,
  formatCalendar,
  formatCurrency,
  formatNumber,
  type CultureFormats,
  type DateFormat,
} from './culture.ts';
import { formatDecimal, parseDecimal } from './decimal.ts';
import { isTrimmed, lower, upper } from './text.ts';

/** The places a template renders in, each with its own encoder (ADR-0014, ADR-0064). */
export type TemplateContext = 'html' | 'text' | 'header' | 'url' | 'json' | 'jsonString' | 'form' | 'xml';

export const TEMPLATE_CONTEXTS: readonly TemplateContext[] = [
  'html',
  'text',
  'header',
  'url',
  'json',
  'jsonString',
  'form',
  'xml',
];

/** A value's item: text, or text with the wire number or the wire date behind it (ADR-0064). */
export type MergeItem = string | { readonly text: string; readonly number?: string; readonly date?: string };

/** What a source gives for a path: one item or a list of them. */
export type MergeValue = MergeItem | readonly MergeItem[];

/** The sources: a path's value, or undefined for a path no source knows. */
export type MergeValues = (path: string) => MergeValue | undefined;

/** The problems of a template (ADR-0064); the warnings are TEMPLATE_WARNINGS, the rest errors and limits. */
export type TemplateProblemCode =
  | 'malformed_tag'
  | 'unclosed_tag'
  | 'tag_in_markup'
  | 'tag_in_escape'
  | 'tag_before_path'
  | 'invalid_argument'
  | 'filter_not_for_context'
  | 'filter_after_encoding'
  | 'too_many_tags'
  | 'too_many_filters'
  | 'too_long'
  | 'unknown_path'
  | 'unknown_filter'
  | 'field_after_text'
  | 'source_not_for_place';

/** A problem and the UTF-16 index where it starts. */
export interface TemplateProblem {
  readonly code: TemplateProblemCode;
  readonly at: number;
}

/** The problems after which a tag renders empty; after any other, it renders as it is written. */
export const TEMPLATE_WARNINGS: ReadonlySet<TemplateProblemCode> = new Set<TemplateProblemCode>([
  'unknown_path',
  'unknown_filter',
]);

/** The most tags a template has. */
export const MAX_TEMPLATE_TAGS = 200;
/** The most filters a tag has. */
export const MAX_TAG_FILTERS = 10;
/** The longest output, in UTF-16 code units. */
export const MAX_TEMPLATE_OUTPUT = 100_000;

/** How a tag's value is written where it stands: with a context's encoder, or as a complete JSON value. */
export type Writer = Exclude<TemplateContext, 'json'> | 'jsonValue';

/** A filter's argument: a string's text, or a number as written. */
export interface TemplateArgument {
  readonly string: boolean;
  readonly text: string;
  readonly at: number;
}

export interface TemplateFilter {
  readonly name: string;
  readonly at: number;
  readonly args: readonly TemplateArgument[];
}

/** The template's own text. */
export interface TextPart {
  readonly kind: 'text';
  readonly at: number;
  readonly text: string;
}

/** A tag with an error of its own, which renders as it is written. */
export interface WrittenPart {
  readonly kind: 'written';
  readonly at: number;
  readonly text: string;
  readonly problem: TemplateProblem;
}

/** A well-formed tag in its place: a path or a string, its filters, how it is written, and its filters' problems. */
export interface TagPart {
  readonly kind: 'tag';
  readonly at: number;
  readonly text: string;
  readonly path: string | null;
  readonly pathAt: number;
  readonly literal: string | null;
  readonly filters: readonly TemplateFilter[];
  readonly writer: Writer;
  readonly problems: readonly TemplateProblem[];
}

/** A limit: the output ends here. */
export interface StopPart {
  readonly kind: 'stop';
  readonly at: number;
  readonly problem: TemplateProblem;
}

export type TemplatePart = TextPart | WrittenPart | TagPart | StopPart;

/** A template parsed for its context. */
export interface ParsedTemplate {
  readonly context: TemplateContext;
  readonly parts: readonly TemplatePart[];
  /** The problems that do not depend on values, in the order of the template. */
  readonly problems: readonly TemplateProblem[];
}

/** A rendered template: its text, and its problems in the order of the template. */
export interface RenderedTemplate {
  readonly text: string;
  readonly problems: readonly TemplateProblem[];
}

const BACKSLASH = String.fromCharCode(92);
const REPLACEMENT = String.fromCharCode(0xfffd);
const ELLIPSIS = String.fromCharCode(0x2026);
const HEX = '0123456789ABCDEF';

const isHigh = (unit: number): boolean => unit >= 0xd800 && unit <= 0xdbff;
const isLow = (unit: number): boolean => unit >= 0xdc00 && unit <= 0xdfff;
const isDigit = (code: number): boolean => code >= 48 && code <= 57;
const isLowerLetter = (code: number): boolean => code >= 97 && code <= 122;
const isLetter = (code: number): boolean => (code >= 65 && code <= 90) || isLowerLetter(code);
/** An ASCII capital as its small letter; any other code unchanged. */
const small = (code: number): number => (code >= 65 && code <= 90 ? code + 32 : code);

const textOf = (item: MergeItem): string => (typeof item === 'string' ? item : item.text);
const isList = (value: MergeValue): value is readonly MergeItem[] => Array.isArray(value);

// ---- Encoders ----

/** A character as markup writes it: `&`, `<`, `>`, `"` and `'` as references, `apos` being the one for `'`. */
function markupCharacter(ch: string, apos: string): string {
  switch (ch) {
    case '&':
      return '&amp;';
    case '<':
      return '&lt;';
    case '>':
      return '&gt;';
    case '"':
      return '&quot;';
    case "'":
      return apos;
    default:
      return ch;
  }
}

function escapeHtml(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i++) out += markupCharacter(text.charAt(i), '&#39;');
  return out;
}

/** The `html` filter: escaped, with each line break (CRLF, CR or LF) as `<br>`. */
function htmlText(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const unit = text.charCodeAt(i);
    if (unit === 13 || unit === 10) {
      out += '<br>';
      if (unit === 13 && text.charCodeAt(i + 1) === 10) i++;
    } else {
      out += markupCharacter(text.charAt(i), '&#39;');
    }
  }
  return out;
}

/** Escaped for XML, with the characters XML 1.0 cannot carry (most controls, U+FFFE, U+FFFF, lone surrogates) as U+FFFD. */
function escapeXml(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const unit = text.charCodeAt(i);
    if (isHigh(unit) && isLow(text.charCodeAt(i + 1))) {
      out += text.slice(i, i + 2);
      i++;
    } else if (
      unit === 9 ||
      unit === 10 ||
      unit === 13 ||
      (unit >= 0x20 && unit <= 0xfffd && !isHigh(unit) && !isLow(unit))
    ) {
      out += markupCharacter(text.charAt(i), '&apos;');
    } else {
      out += REPLACEMENT;
    }
  }
  return out;
}

/** A header's value: a CRLF, each other line break and each control character a space, then trimmed. */
function headerValue(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const unit = text.charCodeAt(i);
    if (unit === 13 && text.charCodeAt(i + 1) === 10) {
      out += ' ';
      i++;
    } else {
      out += unit < 0x20 || (unit >= 0x7f && unit <= 0x9f) || unit === 0x2028 || unit === 0x2029 ? ' ' : text.charAt(i);
    }
  }
  let start = 0;
  let end = out.length;
  while (start < end && isTrimmed(out.charCodeAt(start))) start++;
  while (end > start && isTrimmed(out.charCodeAt(end - 1))) end--;
  return out.slice(start, end);
}

/** The UTF-8 bytes of a code point. */
function utf8(codePoint: number): number[] {
  if (codePoint < 0x80) return [codePoint];
  if (codePoint < 0x800) return [0xc0 | (codePoint >> 6), 0x80 | (codePoint & 0x3f)];
  if (codePoint < 0x10000) {
    return [0xe0 | (codePoint >> 12), 0x80 | ((codePoint >> 6) & 0x3f), 0x80 | (codePoint & 0x3f)];
  }
  return [
    0xf0 | (codePoint >> 18),
    0x80 | ((codePoint >> 12) & 0x3f),
    0x80 | ((codePoint >> 6) & 0x3f),
    0x80 | (codePoint & 0x3f),
  ];
}

/**
 * Percent-encoded UTF-8, lone surrogates as U+FFFD: as one URI component (all but `A-Z a-z 0-9 - . _ ~`), or for a
 * form body (all but `A-Z a-z 0-9 * - . _`, a space as `+`).
 */
function percent(text: string, form: boolean): string {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    let codePoint = text.charCodeAt(i);
    if (isHigh(codePoint) && isLow(text.charCodeAt(i + 1))) {
      codePoint = (codePoint - 0xd800) * 0x400 + (text.charCodeAt(i + 1) - 0xdc00) + 0x10000;
      i++;
    } else if (isHigh(codePoint) || isLow(codePoint)) {
      codePoint = 0xfffd;
    }
    const kept =
      isLetter(codePoint) ||
      isDigit(codePoint) ||
      codePoint === 45 ||
      codePoint === 46 ||
      codePoint === 95 ||
      codePoint === (form ? 42 : 126);
    if (kept) out += String.fromCharCode(codePoint);
    else if (form && codePoint === 32) out += '+';
    else for (const byte of utf8(codePoint)) out += '%' + HEX.charAt(byte >> 4) + HEX.charAt(byte & 15);
  }
  return out;
}

/** The escapes of JSON string content, beside `\u` for the other control characters. */
const JSON_ESCAPES: ReadonlyMap<number, string> = new Map([
  [34, BACKSLASH + '"'],
  [92, BACKSLASH + BACKSLASH],
  [8, BACKSLASH + 'b'],
  [12, BACKSLASH + 'f'],
  [10, BACKSLASH + 'n'],
  [13, BACKSLASH + 'r'],
  [9, BACKSLASH + 't'],
  [60, BACKSLASH + 'u003c'],
  [62, BACKSLASH + 'u003e'],
  [38, BACKSLASH + 'u0026'],
  [0x2028, BACKSLASH + 'u2028'],
  [0x2029, BACKSLASH + 'u2029'],
]);

/** Escaped as JSON string content, lone surrogates as U+FFFD. */
function jsonContent(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const unit = text.charCodeAt(i);
    if (isHigh(unit) && isLow(text.charCodeAt(i + 1))) {
      out += text.slice(i, i + 2);
      i++;
    } else if (isHigh(unit) || isLow(unit)) {
      out += REPLACEMENT;
    } else {
      out +=
        JSON_ESCAPES.get(unit) ?? (unit < 0x20 ? BACKSLASH + 'u' + unit.toString(16).padStart(4, '0') : text.charAt(i));
    }
  }
  return out;
}

/** An item as a complete JSON value: a number for a wire number behind it, otherwise its text as a string. */
function jsonItem(item: MergeItem): string {
  if (typeof item !== 'string' && item.number !== undefined) {
    const number = parseDecimal(item.number);
    if (number !== null) return formatDecimal(number);
  }
  return '"' + jsonContent(textOf(item)) + '"';
}

const ENCODERS: Readonly<Record<Exclude<Writer, 'jsonValue'>, (text: string) => string>> = {
  html: escapeHtml,
  xml: escapeXml,
  text: (text) => text,
  header: headerValue,
  url: (text) => percent(text, false),
  form: (text) => percent(text, true),
  jsonString: jsonContent,
};

// ---- Where a tag may stand ----

/**
 * Reads the template's own text, never a value, to know how a tag there is written, or that none may stand there. A
 * tag that writes a value leaves the scan where it was, since each encoder keeps its place; a tag written out as it
 * stands is read like the rest of the template's text, because that is what the output then holds.
 */
interface Scan {
  read(text: string, from: number, to: number): void;
  place(): Writer | TemplateProblemCode;
}

/** A context without places to refuse. */
class AnywhereScan implements Scan {
  private readonly writer: Writer;

  constructor(writer: Writer) {
    this.writer = writer;
  }

  read(): void {
    // Nothing is refused here, so nothing needs reading.
  }

  place(): Writer {
    return this.writer;
  }
}

const TEXT = 0;
const OPEN = 1;
const END_OPEN = 2;
const BANG = 3;
const IN_TAG = 4;
const COMMENT_START = 5;
const COMMENT = 6;
const CDATA = 7;
const DECLARATION = 8;
const DOCTYPE = 9;
const INSTRUCTION = 10;
const RAW = 11;
const RAW_END = 12;
const ENDLESS = 13;

// Where in a tag the scan is, as the HTML tokenizer reads a tag: a quote opens a value only after `=`. The scan need
// not know where an attribute's name ends or the next one starts, nor whether the tag closes itself, since neither
// changes where the tag ends: so a `/`, and the end of a quoted value, leave it before an attribute.
const TAG_NAME = 0;
const BEFORE_ATTRIBUTE = 1;
const ATTRIBUTE = 2;
const BEFORE_VALUE = 3;
const QUOTED = 4;
const UNQUOTED = 5;

/** In an HTML comment after "--!", where a `>` ends it. */
const DASHES_AND_BANG = -1;

/** The most of an element's name kept: more than any raw element's, so a longer name is none of them. */
const NAME_KEPT = 10;

/** In a script, the start of an escape that the script's end tag may not end. */
const SCRIPT_ESCAPE = '<!--';

/** The spaces of the HTML tokenizer, carriage return included (the parser reads it as a line feed). */
const isSpace = (c: number): boolean => c === 9 || c === 10 || c === 12 || c === 13 || c === 32;

/** The HTML elements whose content is not markup, where an escaped value is no safer than a raw one. */
const RAW_ELEMENTS: ReadonlySet<string> = new Set([
  'script',
  'style',
  'textarea',
  'title',
  'xmp',
  'iframe',
  'noembed',
  'noframes',
  'noscript',
  'plaintext',
]);

/**
 * The elements after which a browser may read a raw element's content as markup: inside svg and math it is foreign
 * content, and select and frameset may drop a raw element's start tag. Which one a browser does depends on the whole
 * document, which the scan does not build.
 */
const DOUBT_AFTER: ReadonlySet<string> = new Set(['svg', 'math', 'select', 'frameset']);

/**
 * HTML and XML: a tag may stand only in text content. The scan reads the template as the HTML tokenizer does (XML's
 * well-formed markup reads alike), and refuses a doubtful place: a `<` before a letter, `/`, `!` or `?`, or right
 * before a tag. Where it cannot know how a browser reads on, it refuses every tag that follows (ENDLESS).
 */
class MarkupScan implements Scan {
  private readonly html: boolean;
  private state = TEXT;
  /** In a tag: where in it the scan is. */
  private part = TAG_NAME;
  /** In a quoted attribute value or an XML declaration's literal: its quote, 0 outside a literal. */
  private quote = 0;
  /** In a tag: the start of its element's name, in small letters. */
  private name = '';
  private closing = false;
  /** After `<!`: what follows, until it is a comment, a CDATA section or a declaration. */
  private bang = '';
  /**
   * Dashes or brackets seen (DASHES_AND_BANG after "--!" in an HTML comment), a question mark seen, or in a raw
   * element the characters of its end tag matched.
   */
  private run = 0;
  /** In a raw element: its name. */
  private raw = '';
  /** In a script: the characters of SCRIPT_ESCAPE matched. */
  private escape = 0;
  /** Whether an element of DOUBT_AFTER has started. */
  private doubt = false;
  /** In a raw element: whether a browser may read its content as markup. */
  private doubtful = false;

  constructor(html: boolean) {
    this.html = html;
  }

  read(text: string, from: number, to: number): void {
    for (let i = from; i < to; i++) this.step(text.charCodeAt(i));
  }

  place(): Writer | TemplateProblemCode {
    if (this.state !== TEXT) return 'tag_in_markup';
    return this.html ? 'html' : 'xml';
  }

  private startTag(closing: boolean, first: number): void {
    this.state = IN_TAG;
    this.part = TAG_NAME;
    this.closing = closing;
    this.name = first < 0 ? '' : String.fromCharCode(small(first));
  }

  private endTag(): void {
    if (this.html && !this.closing) {
      if (DOUBT_AFTER.has(this.name)) this.doubt = true;
      if (RAW_ELEMENTS.has(this.name)) {
        // Nothing ends a plaintext element; noscript is raw only while scripts run.
        this.state = this.name === 'plaintext' ? ENDLESS : RAW;
        this.raw = this.name;
        this.doubtful = this.doubt || this.name === 'noscript';
        this.run = 0;
        return;
      }
    }
    this.state = TEXT;
  }

  private step(c: number): void {
    switch (this.state) {
      case TEXT:
        if (c === 60) this.state = OPEN;
        break;
      case OPEN:
        if (c === 33) {
          this.state = BANG;
          this.bang = '';
        } else if (c === 47) {
          this.state = END_OPEN;
        } else if (c === 63) {
          this.state = this.html ? DECLARATION : INSTRUCTION;
          this.run = 0;
        } else if (isLetter(c) || !this.html) {
          // In XML any other `<` opens a tag; in HTML only one before a letter does.
          this.startTag(false, c);
        } else {
          this.state = TEXT;
          this.step(c);
        }
        break;
      case END_OPEN:
        // A letter opens an end tag, "</>" is nothing, and anything else opens a comment that ends at `>`.
        if (isLetter(c)) this.startTag(true, c);
        else this.state = c === 62 ? TEXT : DECLARATION;
        break;
      case BANG:
        this.afterBang(c);
        break;
      case IN_TAG:
        this.inTag(c);
        break;
      case COMMENT_START:
        // The HTML tokenizer ends a comment at a `>` right after "<!--" or "<!---".
        if (c === 62) {
          this.state = TEXT;
        } else if (c === 45 && this.run === 0) {
          this.run = 1;
        } else {
          this.state = COMMENT;
          this.run = c === 45 ? 2 : 0;
        }
        break;
      case COMMENT:
      case CDATA:
        if (c === (this.state === COMMENT ? 45 : 93)) this.run = this.run < 0 ? 1 : this.run + 1;
        else if (c === 62 && (this.run >= 2 || this.run === DASHES_AND_BANG)) this.state = TEXT;
        // An HTML comment ends at "--!>" too.
        else this.run = this.html && c === 33 && this.run >= 2 ? DASHES_AND_BANG : 0;
        break;
      case DECLARATION:
        if (c === 62) this.state = TEXT;
        break;
      case DOCTYPE:
        // XML's: its literals may hold a `>`, and its internal subset markup declarations the scan does not read.
        if (this.quote !== 0) {
          if (c === this.quote) this.quote = 0;
        } else if (c === 34 || c === 39) {
          this.quote = c;
        } else if (c === 91) {
          this.state = ENDLESS;
        } else if (c === 62) {
          this.state = TEXT;
        }
        break;
      case INSTRUCTION:
        if (c === 62 && this.run === 1) this.state = TEXT;
        else this.run = c === 63 ? 1 : 0;
        break;
      case RAW:
        this.inRaw(c);
        break;
      case RAW_END:
        // The end tag's name is complete if a space, `/` or `>` follows.
        if (isSpace(c) || c === 47) {
          this.startTag(true, -1);
          this.part = BEFORE_ATTRIBUTE;
        } else if (c === 62) {
          this.state = TEXT;
        } else if (this.doubtful) {
          this.state = ENDLESS;
        } else {
          this.state = RAW;
          this.run = 0;
          this.inRaw(c);
        }
        break;
      default:
      // ENDLESS stays.
    }
  }

  private afterBang(c: number): void {
    this.bang += String.fromCharCode(c);
    if ('--'.startsWith(this.bang)) {
      if (this.bang.length === 2) {
        this.state = this.html ? COMMENT_START : COMMENT;
        this.run = 0;
      }
    } else if ((!this.html || this.doubt) && '[CDATA['.startsWith(this.bang)) {
      // XML's CDATA section; in HTML one after svg or math, which a browser ends at "]]>" or at the first `>`.
      if (this.bang.length === 7) {
        this.state = this.html ? ENDLESS : CDATA;
        this.run = 0;
      }
    } else if (this.html) {
      // A comment that ends at the first `>`, as a document type declaration does.
      this.state = c === 62 ? TEXT : DECLARATION;
    } else {
      this.state = DOCTYPE;
      this.quote = 0;
      this.step(c);
    }
  }

  /**
   * A tag, read as the HTML tokenizer reads one, so the scan leaves it where a browser does: a quote in a name or an
   * unquoted value is a character like any other.
   */
  private inTag(c: number): void {
    switch (this.part) {
      case TAG_NAME:
        if (isSpace(c) || c === 47) this.part = BEFORE_ATTRIBUTE;
        else if (c === 62) this.endTag();
        else if (this.name.length < NAME_KEPT) this.name += String.fromCharCode(small(c));
        break;
      case BEFORE_ATTRIBUTE:
        if (c === 62) this.endTag();
        else if (!isSpace(c) && c !== 47) this.part = ATTRIBUTE;
        break;
      case ATTRIBUTE:
        if (c === 47) this.part = BEFORE_ATTRIBUTE;
        else if (c === 61) this.part = BEFORE_VALUE;
        else if (c === 62) this.endTag();
        break;
      case BEFORE_VALUE:
        if (c === 34 || c === 39) {
          this.part = QUOTED;
          this.quote = c;
        } else if (c === 62) {
          this.endTag();
        } else if (!isSpace(c)) {
          this.part = UNQUOTED;
        }
        break;
      case QUOTED:
        if (c === this.quote) this.part = BEFORE_ATTRIBUTE;
        break;
      default:
        // UNQUOTED
        if (isSpace(c)) this.part = BEFORE_ATTRIBUTE;
        else if (c === 62) this.endTag();
    }
  }

  /**
   * A raw element's content, which ends at "</", the element's name without regard to case, and a space, `/` or `>`.
   * In a script, "<!--" may start an escape that the end tag does not end, so nothing ends the script after it. Where a
   * browser may read the content as markup instead, it may hold no other `<`, so that both readings end alike.
   */
  private inRaw(c: number): void {
    if (this.raw === 'script') {
      this.escape = c === SCRIPT_ESCAPE.charCodeAt(this.escape) ? this.escape + 1 : c === 60 ? 1 : 0;
      if (this.escape === SCRIPT_ESCAPE.length) {
        this.state = ENDLESS;
        return;
      }
    }
    const end = '</' + this.raw;
    if (small(c) === end.charCodeAt(this.run)) {
      this.run++;
      if (this.run === end.length) this.state = RAW_END;
    } else if (this.run > 0 && this.doubtful) {
      this.state = ENDLESS;
    } else {
      // A mismatch starts again, at 1 if it is another `<`.
      this.run = c === 60 ? 1 : 0;
    }
  }
}

const OUTSIDE = 0;
const IN_STRING = 1;
const ESCAPED = 2;

/** JSON: inside a string literal a value is string content, outside it a complete JSON value. */
class JsonScan implements Scan {
  private state: number;

  constructor(inString: boolean) {
    this.state = inString ? IN_STRING : OUTSIDE;
  }

  read(text: string, from: number, to: number): void {
    for (let i = from; i < to; i++) {
      const c = text.charCodeAt(i);
      if (this.state === OUTSIDE) {
        if (c === 34) this.state = IN_STRING;
      } else if (this.state === IN_STRING) {
        if (c === 92) this.state = ESCAPED;
        else if (c === 34) this.state = OUTSIDE;
      } else {
        this.state = IN_STRING;
      }
    }
  }

  place(): Writer | TemplateProblemCode {
    if (this.state === ESCAPED) return 'tag_in_escape';
    return this.state === IN_STRING ? 'jsonString' : 'jsonValue';
  }
}

const LEAD = 0;
const SCHEME = 1;
const COLON = 2;
const SLASHES = 3;
const SLASH = 4;
const AUTHORITY = 5;
const RELATIVE = 6;
const PATH = 7;
const NEVER = 8;

/** A path, query or fragment starts: `/`, `\`, `?` or `#`. */
const startsPath = (c: number): boolean => c === 47 || c === 92 || c === 63 || c === 35;

/**
 * An address: a tag may stand only after its path starts, so no value chooses its scheme or host. It reads the
 * address as browsers do: tabs and line breaks dropped, leading spaces and controls skipped, `\` as `/`.
 */
class UrlScan implements Scan {
  private state = LEAD;
  private scheme = '';

  read(text: string, from: number, to: number): void {
    for (let i = from; i < to; i++) {
      const c = text.charCodeAt(i);
      if (c !== 9 && c !== 10 && c !== 13) this.step(c);
    }
  }

  place(): Writer | TemplateProblemCode {
    return this.state === PATH ? 'url' : 'tag_before_path';
  }

  private step(c: number): void {
    switch (this.state) {
      case LEAD:
        if (c <= 32) return;
        if (c === 47 || c === 92) this.state = SLASH;
        else if (c === 63 || c === 35) this.state = PATH;
        else if (isLetter(c)) {
          this.state = SCHEME;
          this.scheme = String.fromCharCode(small(c));
        } else this.state = RELATIVE;
        break;
      case SCHEME:
        if (isLetter(c) || isDigit(c) || c === 43 || c === 45 || c === 46) {
          // Only "http" and "https" are allowed, so a longer scheme need not be kept.
          if (this.scheme.length < 6) this.scheme += String.fromCharCode(small(c));
        } else if (c === 58) this.state = this.scheme === 'http' || this.scheme === 'https' ? COLON : NEVER;
        else this.state = startsPath(c) ? PATH : RELATIVE;
        break;
      case COLON:
        this.state = c === 47 || c === 92 ? SLASHES : NEVER;
        break;
      case SLASHES:
        if (c !== 47 && c !== 92) this.state = c === 63 || c === 35 ? PATH : AUTHORITY;
        break;
      case SLASH:
        this.state = c === 47 || c === 92 ? SLASHES : PATH;
        break;
      case AUTHORITY:
      case RELATIVE:
        if (startsPath(c)) this.state = PATH;
        break;
      default:
      // PATH and NEVER stay.
    }
  }
}

function scanFor(context: TemplateContext): Scan {
  switch (context) {
    case 'html':
    case 'xml':
      return new MarkupScan(context === 'html');
    case 'json':
    case 'jsonString':
      return new JsonScan(context === 'jsonString');
    case 'url':
      return new UrlScan();
    default:
      return new AnywhereScan(context);
  }
}

// ---- The grammar ----

/** What a tag's text holds, or where its grammar fails, or the eleventh filter's `|`. */
type TagReading =
  | {
      readonly path: string | null;
      readonly literal: string | null;
      readonly filters: readonly TemplateFilter[];
    }
  | { readonly fail: number }
  | { readonly limit: number };

/** Reads a tag's text between `{{` and `}}` by the grammar of ADR-0014. */
class TagReader {
  private readonly text: string;
  private readonly end: number;
  private position: number;
  private failure = -1;

  constructor(text: string, from: number, end: number) {
    this.text = text;
    this.position = from;
    this.end = end;
  }

  read(): TagReading {
    this.spaces();
    const start = this.position;
    let path: string | null = null;
    let literal: string | null = null;
    if (this.code() === 34) {
      literal = this.string();
      if (literal === null) return { fail: this.failure };
    } else {
      if (!this.name()) return { fail: this.position };
      while (this.code() === 46) {
        this.position++;
        if (!this.segment()) return { fail: this.position };
      }
      path = this.text.slice(start, this.position);
    }
    const filters: TemplateFilter[] = [];
    for (;;) {
      this.spaces();
      if (this.position === this.end) return { path, literal, filters };
      if (this.code() !== 124) return { fail: this.position };
      if (filters.length === MAX_TAG_FILTERS) return { limit: this.position };
      this.position++;
      this.spaces();
      const at = this.position;
      if (!this.name()) return { fail: this.position };
      const name = this.text.slice(at, this.position);
      const args: TemplateArgument[] = [];
      const back = this.position;
      this.spaces();
      if (this.code() === 58) {
        this.position++;
        for (;;) {
          this.spaces();
          const arg = this.argument();
          if (arg === null) return { fail: this.failure };
          args.push(arg);
          const mark = this.position;
          this.spaces();
          if (this.code() !== 44) {
            this.position = mark;
            break;
          }
          this.position++;
        }
      } else {
        this.position = back;
      }
      filters.push({ name, at, args });
    }
  }

  /** The code unit at the position, or -1 at the tag's end. */
  private code(offset = 0): number {
    const index = this.position + offset;
    return index < this.end ? this.text.charCodeAt(index) : -1;
  }

  private spaces(): void {
    while (this.code() === 32 || this.code() === 9) this.position++;
  }

  /** A source's or filter's name: a small letter, then letters and digits. */
  private name(): boolean {
    if (!isLowerLetter(this.code())) return false;
    while (isLetter(this.code()) || isDigit(this.code())) this.position++;
    return true;
  }

  /** A path's segment: letters, digits, `_` and `-`. */
  private segment(): boolean {
    const start = this.position;
    for (;;) {
      const c = this.code();
      if (!isLetter(c) && !isDigit(c) && c !== 95 && c !== 45) break;
      this.position++;
    }
    return this.position > start;
  }

  /** A string's text; its only escapes are a quote and a backslash after a backslash. */
  private string(): string | null {
    const start = this.position++;
    let out = '';
    while (this.position < this.end) {
      const c = this.code();
      if (c === 34) {
        this.position++;
        return out;
      }
      if (c === 92) {
        const next = this.code(1);
        if (next !== 34 && next !== 92) {
          this.failure = this.position;
          return null;
        }
        out += String.fromCharCode(next);
        this.position += 2;
      } else {
        out += this.text.charAt(this.position++);
      }
    }
    this.failure = start;
    return null;
  }

  private argument(): TemplateArgument | null {
    const at = this.position;
    const c = this.code();
    if (c === 34) {
      const text = this.string();
      return text === null ? null : { string: true, text, at };
    }
    if (c === 45) this.position++;
    // The wire form of ADR-0005: no leading zero, and at least one digit after a point.
    if (!isDigit(this.code()) || (this.code() === 48 && isDigit(this.code(1)))) {
      this.failure = this.position;
      return null;
    }
    while (isDigit(this.code())) this.position++;
    if (this.code() === 46) {
      this.position++;
      if (!isDigit(this.code())) {
        this.failure = this.position;
        return null;
      }
      while (isDigit(this.code())) this.position++;
    }
    return { string: false, text: this.text.slice(at, this.position), at };
  }
}

/**
 * Where the tag that starts at `start` ends: the first `}}` outside a string literal; when a string does not close,
 * the first `}}` after the start. -1 when there is none.
 */
function closingOf(text: string, start: number): number {
  let inString = false;
  let first = -1;
  for (let i = start + 2; i < text.length; i++) {
    const c = text.charCodeAt(i);
    const closes = c === 125 && text.charCodeAt(i + 1) === 125;
    if (closes && first < 0) first = i;
    if (inString) {
      if (c === 92) i++;
      else if (c === 34) inString = false;
    } else if (c === 34) {
      inString = true;
    } else if (closes) {
      return i;
    }
  }
  return inString ? first : -1;
}

// ---- Filters ----

/** A value on its way through the filters: its items, whether it is a list, and the encoding a filter gave it. */
interface Value {
  readonly items: readonly MergeItem[];
  readonly list: boolean;
  readonly encoded: 'html' | 'url' | 'json' | null;
}

interface FilterSpec {
  /** Where the arguments do not fit: the filter for a wrong count, otherwise the argument; null when they fit. */
  readonly check: (filter: TemplateFilter) => number | null;
  readonly apply: (value: Value, filter: TemplateFilter, culture: CultureFormats) => Value;
  /** Whether the filter encodes, so it comes last. */
  readonly encodes?: boolean;
}

/** A whole number from 0 to `high`, written as a number. */
function isWhole(arg: TemplateArgument, high: number): boolean {
  if (arg.string || arg.text.length === 0 || arg.text.length > 6) return false;
  for (let i = 0; i < arg.text.length; i++) if (!isDigit(arg.text.charCodeAt(i))) return false;
  return Number(arg.text) <= high;
}

/** Three capital letters, as ISO 4217 writes a currency. */
function isCurrencyCode(arg: TemplateArgument): boolean {
  if (!arg.string || arg.text.length !== 3) return false;
  for (let i = 0; i < 3; i++) {
    const c = arg.text.charCodeAt(i);
    if (c < 65 || c > 90) return false;
  }
  return true;
}

/**
 * The check of a filter's arguments: between `low` and `high` of them, each fitting its rule in turn (none for any
 * further one).
 */
function argumentsFit(
  low: number,
  high: number,
  ...rules: ((arg: TemplateArgument) => boolean)[]
): (filter: TemplateFilter) => number | null {
  return (filter: TemplateFilter): number | null => {
    if (filter.args.length < low || filter.args.length > high) return filter.at;
    for (let k = 0; k < filter.args.length; k++) {
      const arg = filter.args[k];
      const rule = rules[k];
      if (arg !== undefined && rule !== undefined && !rule(arg)) return arg.at;
    }
    return null;
  };
}

const anything = (): boolean => true;
const aString = (arg: TemplateArgument): boolean => arg.string;
const none = argumentsFit(0, 0);

const each = (value: Value, map: (item: MergeItem) => MergeItem, encoded = value.encoded): Value => ({
  items: value.items.map(map),
  list: value.list,
  encoded,
});

const isEmpty = (value: Value): boolean => value.items.every((item) => textOf(item).length === 0);

/** The first `count` code points and, when it cut, the kept part without trailing spaces and the suffix. */
function truncated(text: string, count: number, suffix: string): string {
  let i = 0;
  for (let kept = 0; i < text.length && kept < count; kept++) {
    i += isHigh(text.charCodeAt(i)) && isLow(text.charCodeAt(i + 1)) ? 2 : 1;
  }
  if (i >= text.length) return text;
  let end = i;
  while (end > 0 && isTrimmed(text.charCodeAt(end - 1))) end--;
  return text.slice(0, end) + suffix;
}

const places = (arg: TemplateArgument | undefined): number | null => (arg === undefined ? null : Number(arg.text));

const FILTERS: ReadonlyMap<string, FilterSpec> = new Map<string, FilterSpec>([
  [
    'default',
    {
      check: argumentsFit(1, 1, anything),
      apply: (value, filter) =>
        isEmpty(value) ? { items: [filter.args[0]?.text ?? ''], list: false, encoded: null } : value,
    },
  ],
  ['upper', { check: none, apply: (value) => each(value, (item) => upper(textOf(item))) }],
  ['lower', { check: none, apply: (value) => each(value, (item) => lower(textOf(item))) }],
  [
    'truncate',
    {
      check: argumentsFit(1, 2, (arg) => isWhole(arg, MAX_TEMPLATE_OUTPUT), aString),
      apply: (value, filter) => {
        const count = Number(filter.args[0]?.text ?? '0');
        const suffix = filter.args[1]?.text ?? ELLIPSIS;
        return each(value, (item) => truncated(textOf(item), count, suffix));
      },
    },
  ],
  [
    'date',
    {
      check: argumentsFit(0, 1, (arg) => arg.string && DATE_FORMATS.has(arg.text)),
      apply: (value, filter, culture) => {
        const format = (filter.args[0]?.text ?? 'short') as DateFormat;
        return each(
          value,
          (item) => formatCalendar(typeof item === 'string' ? item : (item.date ?? item.text), format, culture) ?? item,
        );
      },
    },
  ],
  [
    'number',
    {
      check: argumentsFit(0, 1, (arg) => isWhole(arg, 20)),
      apply: (value, filter, culture) =>
        each(
          value,
          (item) =>
            formatNumber(
              typeof item === 'string' ? item : (item.number ?? item.text),
              places(filter.args[0]),
              culture,
            ) ?? item,
        ),
    },
  ],
  [
    'currency',
    {
      check: argumentsFit(1, 2, isCurrencyCode, (arg) => isWhole(arg, 20)),
      apply: (value, filter, culture) => {
        const code = filter.args[0]?.text ?? '';
        return each(
          value,
          (item) =>
            formatCurrency(
              typeof item === 'string' ? item : (item.number ?? item.text),
              code,
              places(filter.args[1]),
              culture,
            ) ?? item,
        );
      },
    },
  ],
  [
    'join',
    {
      check: argumentsFit(0, 1, aString),
      apply: (value, filter) =>
        value.list
          ? { items: [value.items.map(textOf).join(filter.args[0]?.text ?? ', ')], list: false, encoded: null }
          : value,
    },
  ],
  ['html', { check: none, apply: (value) => each(value, (item) => htmlText(textOf(item)), 'html'), encodes: true }],
  ['url', { check: none, apply: (value) => each(value, (item) => percent(textOf(item), false), 'url'), encodes: true }],
  [
    'json',
    {
      check: none,
      apply: (value) => ({
        items: [value.list ? '[' + value.items.map(jsonItem).join(',') + ']' : jsonItem(value.items[0] ?? '')],
        list: false,
        encoded: 'json',
      }),
      encodes: true,
    },
  ],
]);

/** The problems of a tag's filters that do not depend on its value, from left to right. */
function filterProblems(filters: readonly TemplateFilter[], writer: Writer): TemplateProblem[] {
  const problems: TemplateProblem[] = [];
  let encoded = false;
  let reported = false;
  for (const filter of filters) {
    if (encoded && !reported) {
      problems.push({ code: 'filter_after_encoding', at: filter.at });
      reported = true;
    }
    const spec = FILTERS.get(filter.name);
    if (spec === undefined) {
      problems.push({ code: 'unknown_filter', at: filter.at });
      continue;
    }
    if ((filter.name === 'html' && writer !== 'html') || (filter.name === 'json' && writer === 'jsonString')) {
      problems.push({ code: 'filter_not_for_context', at: filter.at });
    }
    const wrong = spec.check(filter);
    if (wrong !== null) problems.push({ code: 'invalid_argument', at: wrong });
    if (spec.encodes === true) encoded = true;
  }
  return problems;
}

/** A value through a tag's filters, written as its place writes it. */
function write(source: MergeValue, tag: TagPart, culture: CultureFormats): string {
  let value: Value = isList(source)
    ? { items: source, list: true, encoded: null }
    : { items: [source], list: false, encoded: null };
  for (const filter of tag.filters) value = FILTERS.get(filter.name)?.apply(value, filter, culture) ?? value;
  if (tag.writer === 'jsonValue') {
    if (value.encoded === 'json') return textOf(value.items[0] ?? '');
    return value.list ? '[' + value.items.map(jsonItem).join(',') + ']' : jsonItem(value.items[0] ?? '');
  }
  // A list's separator is encoded as its items were, so `url` in an address leaves no raw space.
  const separator = value.encoded === 'url' ? percent(', ', false) : ', ';
  const text = value.list ? value.items.map(textOf).join(separator) : textOf(value.items[0] ?? '');
  if ((value.encoded === 'html' && tag.writer === 'html') || (value.encoded === 'url' && tag.writer === 'url')) {
    return text;
  }
  return ENCODERS[tag.writer](text);
}

// ---- Parsing and rendering ----

const isWriter = (place: Writer | TemplateProblemCode): place is Writer =>
  place === 'jsonValue' || (TEMPLATE_CONTEXTS as readonly string[]).includes(place);

const problemsOf = (part: TemplatePart): readonly TemplateProblem[] =>
  part.kind === 'text' ? [] : part.kind === 'tag' ? part.problems : [part.problem];

/** Problems in the order of the template; those at one place keep their order. */
const ordered = (problems: readonly TemplateProblem[]): TemplateProblem[] => [...problems].sort((a, b) => a.at - b.at);

/**
 * The problem a tag's path has where the template stands (ADR-0068): answer recall refuses a field that comes after the
 * text with `field_after_text`. A tag whose path has one renders as it is written, like any tag with an error.
 */
export type PathProblem = (path: string) => TemplateProblemCode | null;

/**
 * Parses a template for the context it renders in (ADR-0064): its text, each tag's path or string and filters, how
 * each tag is written where it stands, and every problem that does not depend on values; with `pathProblem`, the
 * problem a path has where the template stands, as Core's template rules give it. Never throws.
 */
export function parseTemplate(text: string, context: TemplateContext, pathProblem?: PathProblem): ParsedTemplate {
  const scan = scanFor(context);
  const parts: TemplatePart[] = [];
  const written = (at: number, end: number, code: TemplateProblemCode, where: number): void => {
    parts.push({ kind: 'written', at, text: text.slice(at, end), problem: { code, at: where } });
    scan.read(text, at, end);
  };
  let position = 0;
  let tags = 0;
  while (position < text.length) {
    const start = text.indexOf('{{', position);
    const until = start < 0 ? text.length : start;
    if (until > position) {
      scan.read(text, position, until);
      parts.push({ kind: 'text', at: position, text: text.slice(position, until) });
    }
    if (start < 0) break;
    if (++tags > MAX_TEMPLATE_TAGS) {
      parts.push({ kind: 'stop', at: start, problem: { code: 'too_many_tags', at: start } });
      break;
    }
    const close = closingOf(text, start);
    if (close < 0) {
      written(start, text.length, 'unclosed_tag', start);
      break;
    }
    position = close + 2;
    const place = scan.place();
    if (!isWriter(place)) {
      written(start, position, place, start);
      continue;
    }
    const reading = new TagReader(text, start + 2, close).read();
    if ('limit' in reading) {
      parts.push({ kind: 'stop', at: start, problem: { code: 'too_many_filters', at: reading.limit } });
      break;
    }
    if ('fail' in reading) {
      written(start, position, 'malformed_tag', reading.fail);
      continue;
    }
    let pathAt = start + 2;
    while (text.charCodeAt(pathAt) === 32 || text.charCodeAt(pathAt) === 9) pathAt++;
    const refused = reading.path === null ? null : (pathProblem?.(reading.path) ?? null);
    if (refused !== null) {
      written(start, position, refused, pathAt);
      continue;
    }
    const problems = filterProblems(reading.filters, place);
    parts.push({
      kind: 'tag',
      at: start,
      text: text.slice(start, position),
      path: reading.path,
      pathAt,
      literal: reading.literal,
      filters: reading.filters,
      writer: place,
      problems,
    });
    if (problems.some((problem) => !TEMPLATE_WARNINGS.has(problem.code))) scan.read(text, start, position);
  }
  return { context, parts, problems: ordered(parts.flatMap(problemsOf)) };
}

/**
 * Renders a parsed template over its sources' values and a culture's formats (ADR-0064): a tag with an error as it is
 * written, one with warnings only as nothing, and the output at most MAX_TEMPLATE_OUTPUT code units long. Never
 * throws.
 */
export function renderTemplate(
  template: ParsedTemplate,
  values: MergeValues,
  culture: CultureFormats,
): RenderedTemplate {
  const problems: TemplateProblem[] = [];
  let out = '';
  let stopped = false;
  const add = (piece: string, at: number): void => {
    if (out.length + piece.length <= MAX_TEMPLATE_OUTPUT) {
      out += piece;
      return;
    }
    // Up to the limit, but never between the two halves of a surrogate pair.
    let room = MAX_TEMPLATE_OUTPUT - out.length;
    if (room > 0 && isHigh(piece.charCodeAt(room - 1)) && isLow(piece.charCodeAt(room))) room--;
    out += piece.slice(0, room);
    problems.push({ code: 'too_long', at });
    stopped = true;
  };
  for (const part of template.parts) {
    if (stopped) break;
    if (part.kind === 'text') {
      add(part.text, part.at);
    } else if (part.kind === 'written') {
      problems.push(part.problem);
      add(part.text, part.at);
    } else if (part.kind === 'stop') {
      problems.push(part.problem);
      stopped = true;
    } else {
      const value = part.literal ?? values(part.path ?? '');
      if (value === undefined) problems.push({ code: 'unknown_path', at: part.pathAt });
      problems.push(...part.problems);
      if (part.problems.some((problem) => !TEMPLATE_WARNINGS.has(problem.code))) add(part.text, part.at);
      else if (value !== undefined && part.problems.length === 0) add(write(value, part, culture), part.at);
    }
  }
  return { text: out, problems: ordered(problems) };
}
