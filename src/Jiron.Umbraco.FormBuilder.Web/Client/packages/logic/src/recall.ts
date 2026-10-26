// Answer recall (ROADMAP section 4.3.3, ADR-0068): a visitor text's `field.*` tags filled in from the answers given
// so far, the same as Core's field source renders them in visitor texts. Only a field before the text may be named,
// by its alias; one the rules hid shows nothing, and the answers show as their types write them (display.ts). The
// vectors of tests/vectors/merge-tags.json hold both runtimes to one behaviour.
import type { FieldFile } from './checks.ts';
import type { CultureFormats } from './culture.ts';
import { displayAnswer, valueParts, type AnswerValue, type DisplayedField, type DisplayWords } from './display.ts';
import { own } from './errors.ts';
import type { MergeItem, MergeValue, MergeValues, PathProblem } from './template.ts';

/** What a field whose answer may not be shown shows in its tags once it is answered: three bullets (ADR-0066). */
export const MASKED = String.fromCharCode(0x2022).repeat(3);

/** A field of a form as answer recall reads it, its texts in the page's culture. */
export interface RecallField extends DisplayedField {
  readonly id: string;
  /** The alias tags name it by. */
  readonly alias?: string | null;
  /** Its label in the page's culture; `.label` shows the alias without one. */
  readonly label?: string | null;
  /** Whether it holds a value, as a repeatable group does through its children; a heading or a paragraph does not. */
  readonly holdsValue: boolean;
  /**
   * How its tags show its answers: as they are by default, `masked` as three bullets once it is answered, and `none`
   * not at all, for a field whose value stays on the server.
   */
  readonly recall?: 'masked' | 'none';
  /** A repeatable group's child fields, whose answers come row by row. */
  readonly fields?: readonly RecallField[];
}

/** A group of fields on a page. */
export interface RecallGroup {
  readonly id: string;
  readonly fields?: readonly RecallField[];
}

/** A page. */
export interface RecallPage {
  readonly id: string;
  readonly groups?: readonly RecallGroup[];
}

/** A form as answer recall reads it: its pages, groups and fields in their order. */
export interface RecallForm {
  readonly pages: readonly RecallPage[];
}

/** A field's answer: its values in the wire form, and for a field that stores files the file each stands for. */
export interface RecallAnswer {
  readonly values: readonly AnswerValue[];
  readonly files?: readonly FieldFile[];
}

/** The answers given so far: each field's by id, and each repeatable group's rows of its child fields' answers. */
export interface RecallAnswers {
  readonly fields?: Readonly<Record<string, RecallAnswer>>;
  readonly rows?: Readonly<Record<string, readonly Readonly<Record<string, RecallAnswer>>[]>>;
}

/** What a visitor text renders with: the values of its `field.*` tags, and the problem a path has where the text stands. */
export interface Recall {
  readonly values: MergeValues;
  readonly pathProblem: PathProblem;
}

/** A field that holds a value, and the repeatable group whose child it is. */
interface Target {
  readonly field: RecallField;
  readonly repeater: RecallField | null;
}

const isRepeater = (field: RecallField): boolean => (field.fields ?? []).length > 0;

/** The form's elements in document order (pages, groups, fields, a repeatable group's children after it), with what holds each. */
function elements(form: RecallForm): { order: Map<string, number>; holders: Map<string, readonly string[]> } {
  const order = new Map<string, number>();
  const holders = new Map<string, readonly string[]>();
  const visit = (id: string, held: readonly string[]): void => {
    if (!order.has(id)) order.set(id, order.size);
    holders.set(id, held);
  };
  const fields = (list: readonly RecallField[], held: readonly string[]): void => {
    for (const field of list) {
      visit(field.id, held);
      fields(field.fields ?? [], [...held, field.id]);
    }
  };
  for (const page of form.pages) {
    visit(page.id, []);
    for (const group of page.groups ?? []) {
      visit(group.id, [page.id]);
      fields(group.fields ?? [], [page.id, group.id]);
    }
  }
  return { order, holders };
}

/** The fields that hold a value in the order of the form, each repeatable group followed by its children. */
function targets(form: RecallForm): Target[] {
  const found: Target[] = [];
  for (const field of form.pages.flatMap((page) => page.groups ?? []).flatMap((group) => group.fields ?? [])) {
    if (!field.holdsValue) continue;
    found.push({ field, repeater: null });
    for (const child of field.fields ?? []) if (child.holdsValue) found.push({ field: child, repeater: field });
  }
  return found;
}

/** A part of a value with parts by its name, as display.ts reads them; null for none. */
const partOf = (text: string, name: string): string | null =>
  valueParts(text)?.find(([key]) => key === name)?.[1] ?? null;

const joined = (items: readonly MergeItem[]): string =>
  items.map((item) => (typeof item === 'string' ? item : item.text)).join(', ');

/**
 * Answer recall for a visitor text (ADR-0068): the values of its `field.*` tags and, as `pathProblem` for parsing, the
 * refusal of a field that does not come before the text with `field_after_text`. `owner` is the page, group or field
 * whose text it is (its title, description, label, help or placeholder, or a text block's text); null, or an id the
 * form lacks, for a text after every field. Fields are named by their current aliases. A field the rules hid (its id,
 * or its repeatable group's, in `hidden`) shows nothing, as does one whose recall is `none`; one whose recall is
 * `masked` shows three bullets once it is answered.
 */
export function recallAnswers(
  form: RecallForm,
  owner: string | null,
  answers: RecallAnswers,
  hidden: readonly string[],
  words: DisplayWords,
  culture: CultureFormats,
): Recall {
  const { order, holders } = elements(form);
  const fields = targets(form);
  const hiddenIds = new Set(hidden);
  const at = owner === null ? undefined : order.get(owner);
  const holding = new Set(owner === null ? [] : (holders.get(owner) ?? []));

  const before = (target: Target): boolean => {
    if (at === undefined) return true;
    const position = order.get(target.field.id);
    return position !== undefined && position < at && !holding.has(target.field.id);
  };

  // The field a path names and what of it: null for the answer itself, `value`, `label` or a part.
  const locate = (path: string): { target: Target; member: string | null } | null => {
    if (!path.startsWith('field.')) return null;
    const segments = path.slice('field.'.length).split('.');
    const first = fields.find((found) => found.field.alias === segments[0]);
    if (first === undefined) return null;
    let target = first;
    let rest = segments.slice(1);
    if (target.repeater === null && isRepeater(target.field) && rest.length > 0) {
      const child = (target.field.fields ?? []).find((each) => each.alias === rest[0]);
      if (child !== undefined) {
        target = { field: child, repeater: target.field };
        rest = rest.slice(1);
      }
    }
    const isGroup = target.repeater === null && isRepeater(target.field);
    if (rest.length === 0) return { target, member: null };
    if (rest.length !== 1) return null;
    const [member = ''] = rest;
    if (member === 'label') return { target, member };
    if (isGroup) return null;
    if (member === 'value') return { target, member };
    return member.length > 0 && target.field.parts === true ? { target, member } : null;
  };

  const answerOf = (
    field: RecallField,
    row: Readonly<Record<string, RecallAnswer>> | undefined,
  ): RecallAnswer | null => {
    const answer = row === undefined ? own(answers.fields ?? {}, field.id) : own(row, field.id);
    return answer !== undefined && answer.values.length > 0 ? answer : null;
  };
  const rowsOf = (repeater: RecallField): readonly Readonly<Record<string, RecallAnswer>>[] =>
    own(answers.rows ?? {}, repeater.id) ?? [];
  const isHidden = (target: Target): boolean =>
    hiddenIds.has(target.field.id) || (target.repeater !== null && hiddenIds.has(target.repeater.id));
  const isMasked = (target: Target): boolean =>
    target.field.recall === 'masked' || target.repeater?.recall === 'masked';
  const shows = (target: Target): boolean => !isHidden(target) && target.field.recall !== 'none';
  const texts = (field: RecallField, answer: RecallAnswer): MergeItem[] =>
    displayAnswer(field, answer.values, answer.files ?? [], words, culture);
  const isMany = (field: RecallField): boolean =>
    field.kind === 'choices' || field.kind === 'files' || field.rows !== undefined;

  // A field's value: nothing where it does not show, the bullets once masked and answered, its items otherwise.
  const resolve = (target: Target, items: (answer: RecallAnswer) => MergeItem[]): MergeValue => {
    if (!shows(target)) return '';
    if (target.repeater === null) {
      const answer = answerOf(target.field, undefined);
      if (isMasked(target)) return answer === null ? '' : MASKED;
      const found = answer === null ? [] : items(answer);
      if (isMany(target.field) || found.length > 1) return found;
      return found[0] ?? '';
    }
    const given = rowsOf(target.repeater)
      .map((row) => answerOf(target.field, row))
      .filter((answer): answer is RecallAnswer => answer !== null);
    if (isMasked(target)) return given.length === 0 ? '' : MASKED;
    return given
      .map(items)
      .filter((row) => row.length > 0)
      .map((row): MergeItem => (row.length === 1 ? (row[0] as MergeItem) : joined(row)));
  };

  // A repeatable group: each answered row as its children's `Label: answer` apart by "; ".
  const rows = (repeater: RecallField): MergeValue => {
    const group: Target = { field: repeater, repeater: null };
    if (!shows(group)) return '';
    const lines: string[] = [];
    for (const row of rowsOf(repeater)) {
      const pieces: string[] = [];
      for (const child of repeater.fields ?? []) {
        if (!child.holdsValue) continue;
        const target: Target = { field: child, repeater };
        const answer = answerOf(child, row);
        if (!shows(target) || answer === null) continue;
        const text = isMasked(target) ? MASKED : joined(texts(child, answer));
        if (text.length > 0) pieces.push(`${labelOf(child)}: ${text}`);
      }
      if (pieces.length > 0) lines.push(pieces.join('; '));
    }
    if (isMasked(group)) return lines.length > 0 ? MASKED : '';
    return lines;
  };

  const values: MergeValues = (path) => {
    const found = locate(path);
    if (found === null) return undefined;
    const { target, member } = found;
    if (member === 'label') return labelOf(target.field);
    if (member === 'value') {
      return resolve(target, (answer) =>
        answer.values.map(({ text }): MergeItem => {
          const kind = target.field.kind;
          if (kind === 'number') return { text, number: text };
          if (kind === 'date' || kind === 'time' || kind === 'dateTime') return { text, date: text };
          return text;
        }),
      );
    }
    if (member !== null) {
      return resolve(target, (answer) =>
        answer.values.map(({ text }) => partOf(text, member)).filter((part): part is string => part !== null),
      );
    }
    if (target.repeater === null && isRepeater(target.field)) return rows(target.field);
    return resolve(target, (answer) => texts(target.field, answer));
  };

  const pathProblem: PathProblem = (path) => {
    const found = locate(path);
    return found !== null && !before(found.target) ? 'field_after_text' : null;
  };

  return { values, pathProblem };
}

/** A field's label, or its alias when it has none. */
function labelOf(field: RecallField): string {
  const label = field.label ?? '';
  return label.length > 0 ? label : (field.alias ?? '');
}
