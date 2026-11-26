// A page's post read by the names its controls carry (ROADMAP 6.4b, ADR-0058, ADR-0100, ADR-0110), as Web's
// FormPostReader reads a post without JavaScript, so the browser's rules and checks start from the answers the server
// would take: `<alias>` and `<alias>.<part>`, a repeatable group's `<group>[<row>].<child>` up to the rows the page
// shows, a consent's wording, the context and query values, and a yes or no switch that posts nothing as `no`. The
// shared vectors of tests/vectors/posts.json hold the two readers equal.
import { own } from './errors.ts';
import { BUILT_IN_TYPES, type PostedAnswer } from './reading.ts';
import { clean } from './text.ts';

/** A field as the page names it, from the form endpoint's projection and its `fields` (ADR-0084, ADR-0109). */
export interface PostField {
  /** The name its controls carry; a field without one posts nothing. */
  readonly alias?: string | null;
  readonly type: string;
  /** Whether its type is the built-in one of its alias; unless said otherwise, it is. */
  readonly builtIn?: boolean;
  /** Its effective public settings, as the projection writes them. */
  readonly settings?: Readonly<Record<string, unknown>> | null;
  /** A repeatable group's child fields. */
  readonly fields?: readonly PostField[];
}

/** One field's answer as posted: its texts, and its parts in the order they were first posted. */
export interface PostAnswer extends PostedAnswer {
  readonly values: readonly string[];
}

/** A post as the pipeline takes it (ADR-0088): the answers by the fields' aliases, the groups' rows, the context and query values. */
export interface ReadPost {
  /** Each field's answer outside repeatable groups, by its alias; a field the post holds nothing of is left out. */
  readonly fields: ReadonlyMap<string, PostAnswer>;
  /** Each repeatable group's rows, as many as the page shows, each its children's answers by their aliases. */
  readonly rows: ReadonlyMap<string, readonly ReadonlyMap<string, PostAnswer>[]>;
  /** The context values, by name (ADR-0093). */
  readonly context: ReadonlyMap<string, string>;
  /** The query values the post carries on, by name (ADR-0088). */
  readonly query: ReadonlyMap<string, readonly string[]>;
}

/** The prefix of every name of the package's own; an alias never holds a hyphen. */
const PREFIX = 'jfb-';
const ROWS = 'jfb-rows-';
const CONSENT = 'jfb-consent-';
const ORDER = 'jfb-order-';
const CONTEXT = 'jfb-context-';
const QUERY = 'jfb-query-';
/** The most rows any repeatable group may have, as its `maxRows` setting may say at most. */
export const MOST_ROWS = 100;
/** The most values of one query name a submission carries, and the longest each may be: Core's FormSubmissions'. */
export const MOST_QUERY_VALUES = 20;
export const LONGEST_QUERY_VALUE = 2000;
/** The part a consent's wording hash is posted as, and the part a signature's typed name is. */
const WORDING = 'wording';
const TYPED = 'typed';
/** What a switch left off is sent as (ADR-0096). */
const NO = 'no';
const NUL = String.fromCharCode(0);
const POSTED_BREAK = String.fromCharCode(13, 10);
const BREAK = String.fromCharCode(10);

/** Reads a post: the page's names and values in the order posted, for the form's fields in the order of its pages. */
export function readPost(fields: readonly PostField[], entries: Iterable<readonly [string, string]>): ReadPost {
  const post = new Post(fields);
  for (const [name, value] of entries) post.take(name, value);
  post.switches();
  return post.result();
}

/** A field's name: where it is, and the part after its first dot, if any. */
interface Path {
  readonly group: string | null;
  readonly row: number;
  readonly alias: string;
  readonly part: string | null;
}

class Answer {
  values: string[] = [];
  readonly parts = new Map<string, string[]>();

  take(part: string | null, text: string): void {
    if (part === null) {
      this.values.push(text);
      return;
    }
    const given = this.parts.get(part);
    if (given === undefined) this.parts.set(part, [text]);
    else given.push(text);
  }

  switchedOff(): void {
    if (!this.values.some((value) => value.length > 0)) this.values = [NO];
  }

  // The answer as the pipeline takes it, a signature's without its typed name; null when nothing is left.
  posted(signature: boolean): PostAnswer | null {
    const parts = [...this.parts].filter(([name]) => !signature || name !== TYPED);
    if (this.values.length === 0 && parts.length === 0) return null;
    return parts.length === 0
      ? { values: [...this.values] }
      : { values: [...this.values], parts: parts.map(([name, texts]) => [name, [...texts]] as const) };
  }
}

/** A field the page names: one with an alias. */
type Named = PostField & { readonly alias: string };

class Post {
  private readonly top = new Map<string, Named>();
  private readonly children = new Map<Named, Map<string, Named>>();
  private readonly order: readonly Named[];
  private readonly answers = new Map<Named, Answer>();
  private readonly posted = new Map<Named, Map<number, Map<Named, Answer>>>();
  private readonly counts = new Map<Named, number>();
  private readonly context = new Map<string, string>();
  private readonly query = new Map<string, string[]>();

  constructor(fields: readonly PostField[]) {
    this.order = fields.filter(named);
    for (const field of this.order) this.top.set(field.alias, field);
    for (const group of this.order.filter(isGroup)) {
      this.children.set(group, new Map((group.fields ?? []).filter(named).map((child) => [child.alias, child])));
    }
  }

  take(name: string, value: string): void {
    if (name.startsWith(PREFIX)) {
      this.own(name, value);
      return;
    }
    const path = pathOf(name);
    if (path !== null) this.answerOf(path)?.take(path.part, texts(value));
  }

  // A yes or no switch posts nothing while it is off: outside groups, and in each row the page shows.
  switches(): void {
    for (const field of this.order) {
      if (isSwitch(field)) made(this.answers, field).switchedOff();
      if (!isGroup(field)) continue;
      const count = this.rowsOf(field).length;
      for (const child of (field.fields ?? []).filter(isSwitch)) {
        for (let row = 0; row < count; row++) made(this.row(field, row), child).switchedOff();
      }
    }
  }

  result(): ReadPost {
    const fields = new Map<string, PostAnswer>();
    const rows = new Map<string, ReadonlyMap<string, PostAnswer>[]>();
    for (const field of this.order) {
      if (isGroup(field)) {
        const posted = this.rowsOf(field);
        if (posted.length === 0) continue;
        rows.set(
          field.alias,
          posted.map((row) => {
            const answers = new Map<string, PostAnswer>();
            for (const [child, answer] of row) {
              const given = answer.posted(isSignature(child));
              if (given !== null) answers.set(child.alias, given);
            }
            return answers;
          }),
        );
        continue;
      }
      const given = this.answers.get(field)?.posted(isSignature(field));
      if (given != null) fields.set(field.alias, given);
    }
    return { fields, rows, context: this.context, query: this.query };
  }

  // The package's own names, read as posted: a group's row count (its last value says), a consent's wording, a
  // ranking's order, a context or a query value.
  private own(name: string, text: string): void {
    if (name.startsWith(ROWS)) {
      const group = this.top.get(name.slice(ROWS.length));
      if (group === undefined || !isGroup(group)) return;
      const count = wholeNumber(text);
      if (count === null) this.counts.delete(group);
      else this.counts.set(group, Math.min(count, MOST_ROWS));
    } else if (name.startsWith(CONSENT)) {
      const path = pathOf(name.slice(CONSENT.length));
      if (path !== null && path.part === null) this.answerOf(path)?.take(WORDING, texts(text));
    } else if (name.startsWith(ORDER)) {
      // The order a page without JavaScript shows a ranking in is no answer, but it names the field's row.
      const path = pathOf(name.slice(ORDER.length));
      if (path !== null && path.part === null) this.answerOf(path);
    } else if (name.startsWith(CONTEXT)) {
      this.context.set(name.slice(CONTEXT.length), text);
    } else if (name.startsWith(QUERY)) {
      const key = name.slice(QUERY.length);
      const given = this.query.get(key);
      if (given === undefined) this.query.set(key, [text]);
      else given.push(text);
    }
  }

  // The answer a path names, made when it is the first of its field; null for a name of no field.
  private answerOf(path: Path): Answer | null {
    if (path.group === null) {
      const field = this.top.get(path.alias);
      return field !== undefined && !isGroup(field) ? made(this.answers, field) : null;
    }
    const group = this.top.get(path.group);
    const child = group === undefined ? undefined : this.children.get(group)?.get(path.alias);
    return group === undefined || child === undefined ? null : made(this.row(group, path.row), child);
  }

  private row(group: Named, at: number): Map<Named, Answer> {
    let rows = this.posted.get(group);
    if (rows === undefined) this.posted.set(group, (rows = new Map()));
    let row = rows.get(at);
    if (row === undefined) rows.set(at, (row = new Map()));
    return row;
  }

  // A group's rows, as many as the page shows (or, for a post that does not say, as far as it names one); a row whose
  // controls posted nothing is empty.
  private rowsOf(group: Named): Map<Named, Answer>[] {
    const posted = this.posted.get(group);
    const count =
      this.counts.get(group) ?? (posted === undefined || posted.size === 0 ? 0 : Math.max(...posted.keys()) + 1);
    return Array.from({ length: count }, (_, row) => posted?.get(row) ?? new Map<Named, Answer>());
  }
}

/** A posted text, each line break as the runtime sends it. */
const texts = (value: string): string => value.replaceAll(POSTED_BREAK, BREAK);

function made<K>(answers: Map<K, Answer>, key: K): Answer {
  let answer = answers.get(key);
  if (answer === undefined) answers.set(key, (answer = new Answer()));
  return answer;
}

const named = (field: PostField): field is Named => typeof field.alias === 'string';

/** The built-in type a field's type is, or none for a site's own. */
const declaredOf = (field: PostField) => (field.builtIn === false ? undefined : own(BUILT_IN_TYPES, field.type));

/** A repeatable group: a built-in type that holds fields, or a site's own type that does. */
function isGroup(field: PostField): boolean {
  const declared = declaredOf(field);
  return declared === undefined ? field.builtIn === false && (field.fields?.length ?? 0) > 0 : declared.childFields;
}

const isSignature = (field: PostField): boolean => field.type === 'signature' && declaredOf(field) !== undefined;

const isSwitch = (field: PostField): boolean =>
  named(field) && field.type === 'yesno' && declaredOf(field) !== undefined && field.settings?.['style'] === 'switch';

/** A field's name: `alias`, `alias.part`, `group[row].child` or `group[row].child.part`; null for any other. */
function pathOf(name: string): Path | null {
  const open = name.indexOf('[');
  if (open < 0) {
    const [alias, part] = split(name);
    return alias.length > 0 ? { group: null, row: 0, alias, part } : null;
  }
  const close = name.indexOf(']', open);
  if (open === 0 || close < 0 || close + 1 >= name.length || name[close + 1] !== '.') return null;
  const row = wholeNumber(name.slice(open + 1, close));
  if (row === null || row >= MOST_ROWS) return null;
  const [child, part] = split(name.slice(close + 2));
  return child.length > 0 ? { group: name.slice(0, open), row, alias: child, part } : null;
}

/** An alias and the part after its first dot, if any; a part that is empty is none. */
function split(name: string): readonly [string, string | null] {
  const dot = name.indexOf('.');
  return dot < 0 ? [name, null] : [name.slice(0, dot), dot < name.length - 1 ? name.slice(dot + 1) : null];
}

/**
 * The query values of the names the form lists, each held to the pipeline's limits (ROADMAP 6.4c, ADR-0088, ADR-0100):
 * Web's FormPostHandler holds a post's, or on a first post its page's address's, this way, so a long address never
 * refuses a submission, and the rules on the page read the same values.
 */
export function holdQuery(
  query: Iterable<readonly [string, readonly string[]]>,
  listed: readonly string[],
): Record<string, string[]> {
  const held: Record<string, string[]> = Object.create(null) as Record<string, string[]>;
  for (const [name, values] of query) {
    if (listed.includes(name))
      held[name] = values.filter((value) => value.length <= LONGEST_QUERY_VALUE).slice(0, MOST_QUERY_VALUES);
  }
  return held;
}

/**
 * Whether a post holds no answer at all (ROADMAP 6.4d, ADR-0112), as Core's FormSubmissions refuses a submission as
 * empty: every posted text of every field, and of every child of a repeatable group's rows, blank once cleaned, a
 * consent's wording aside. A switch left off posts `no`, so a form with one is never empty.
 */
export function isEmptyPost(post: ReadPost): boolean {
  const blank = (answer: PostAnswer): boolean =>
    answer.values.every(isBlank) &&
    (answer.parts ?? []).every(([part, texts]) => part === WORDING || texts.every(isBlank));
  return (
    [...post.fields.values()].every(blank) &&
    [...post.rows.values()].every((rows) => rows.every((row) => [...row.values()].every(blank)))
  );
}

const isBlank = (text: string): boolean => clean(text).length === 0;

/**
 * What a send is refused with (ADR-0266), as Core's FormSubmissions decides it: its shown fields' errors when it has
 * any, a send with no answer at all among them, and `empty` only for a send with no answer at all and no field's error.
 */
export function sendRefusal<E>(post: ReadPost, errors: readonly E[]): readonly E[] | 'empty' {
  return errors.length === 0 && isEmptyPost(post) ? 'empty' : errors;
}

/**
 * A whole number as .NET reads one without styles: ASCII digits only, then any number of NUL characters, which .NET's
 * parsing passes over; at most 2,147,483,647.
 */
function wholeNumber(text: string): number | null {
  let end = text.length;
  while (end > 0 && text[end - 1] === NUL) end--;
  const number = text.slice(0, end);
  if (!/^[0-9]+$/.test(number)) return null;
  const digits = number.replace(/^0+/, '');
  if (digits.length > 10 || (digits.length === 10 && digits > '2147483647')) return null;
  return Number(digits);
}
