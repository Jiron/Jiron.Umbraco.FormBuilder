// The form engine (ROADMAP section 4.3.3, ADR-0061), the same in C# (Core's FormRules): every rule of a form at once.
// The whole form is checked first, so the order of evaluation never changes an outcome; then visibility is decided in
// dependency order (an element after what holds it and after every field its show or hide rule looks at), a hidden
// field's answers are gone for every condition, and a repeatable group's child fields are decided row by row. What is
// required, the validation rules' messages, the actions that run and the outcome follow from the final visibility.
// Calculations are worked out in the same order, after the fields they read (ADR-0063).
import { calculate, checkFormula, parseFormula, type Calculation, type FormulaScope } from './calc.ts';
import { today } from './dates.ts';
import { formatDecimal, round } from './decimal.ts';
import { LogicError, own } from './errors.ts';
import type { Pattern } from './pattern.ts';
import {
  checkGroup,
  isField,
  isGroup,
  matchesGroup,
  type Context,
  type Group,
  type Kind,
  type Rule,
  type Scope,
} from './rules.ts';

/** A grid's row: a subject of conditions, shown with its grid. */
export interface LogicGridRow {
  readonly id: string;
  readonly kind: Kind;
}

/** A calculation's formula and the places its result is rounded to: 0 to 15, 2 when unset (ADR-0063). */
export interface LogicCalculation {
  readonly formula: string;
  readonly decimals?: number | null;
}

/** A field: one with child fields is a repeatable group. */
export interface LogicField {
  readonly id: string;
  /** The kind its answers compare as; none for a field whose answers no condition compares, or that holds none. */
  readonly kind?: Kind | null;
  /**
   * Whether a field without a kind can be required: a name, an address or a grid takes answers that no condition
   * compares (ADR-0062, ADR-0088). A field with a kind always can.
   */
  readonly canBeRequired?: boolean;
  /** The alias formulas name it by. */
  readonly alias?: string | null;
  /** A calculation field's formula, worked out instead of what is posted for it. */
  readonly calculation?: LogicCalculation | null;
  /** Whether the field is required of itself, whatever its rules say. */
  readonly required?: boolean;
  readonly rules?: readonly Rule[];
  /** A repeatable group's child fields, which have one answer per row. */
  readonly fields?: readonly LogicField[];
  /** A grid's rows. */
  readonly rows?: readonly LogicGridRow[];
}

/** A group of fields on a page (the conditions' own groups are `Group`). */
export interface LogicFormGroup {
  readonly id: string;
  readonly rules?: readonly Rule[];
  readonly fields?: readonly LogicField[];
}

/** A page. */
export interface LogicPage {
  readonly id: string;
  readonly rules?: readonly Rule[];
  readonly groups?: readonly LogicFormGroup[];
}

/** A form-level validation rule: while it matches, its field shows its message. */
export interface LogicValidationRule extends Group {
  readonly id: string;
  /** The field, child field or grid row that shows the message. */
  readonly field: string;
  readonly enabled: boolean;
}

/** An action, which runs unless its `run` rule does not match. */
export interface LogicAction {
  readonly id: string;
  readonly rules?: readonly Rule[];
}

/** An outcome: the first whose `use` rule matches, or that has none, is used. */
export interface LogicOutcome {
  readonly id: string;
  readonly rules?: readonly Rule[];
}

/** A form as its rules see it (ADR-0061). The visitor's browser has no actions or outcomes. */
export interface LogicForm {
  readonly pages: readonly LogicPage[];
  readonly validationRules?: readonly LogicValidationRule[];
  readonly actions?: readonly LogicAction[];
  readonly outcomes?: readonly LogicOutcome[];
}

/** The answers: each field's and grid row's texts by id, and each repeatable group's rows of its child fields' texts. */
export interface FormAnswers {
  readonly values?: Readonly<Record<string, readonly string[]>>;
  readonly rows?: Readonly<Record<string, readonly Readonly<Record<string, readonly string[]>>[]>>;
}

/** One row of a repeatable group: its child fields that are hidden or required, its messages and its calculations. */
export interface RowState {
  readonly hidden: string[];
  readonly required: string[];
  readonly messages: Record<string, string[]>;
  readonly calculations: Record<string, string | null>;
}

/** What a form's rules decide, every list in document order. */
export interface FormState {
  /** The pages, groups and fields (outside repeatable groups) that are hidden. */
  readonly hidden: string[];
  /** The shown fields that are required. */
  readonly required: string[];
  /** The ids of the validation rules that match, by the shown field that shows them. */
  readonly messages: Record<string, string[]>;
  /** The result of every shown calculation (outside repeatable groups) as canonical text, null without a value. */
  readonly calculations: Record<string, string | null>;
  /** Every repeatable group's rows. */
  readonly rows: Record<string, RowState[]>;
  /** The actions that run. */
  readonly actions: string[];
  /** The outcome used, or none. */
  readonly outcome: string | null;
}

type ElementType = 'page' | 'group' | 'field';

type Rows = readonly Readonly<Record<string, readonly string[]>>[];

interface RowBuilder {
  readonly hidden: string[];
  readonly required: string[];
  readonly messages: Map<string, string[]>;
  readonly calculations: Map<string, string | null>;
}

/** A calculation as the check read it: its tree, the aliases it names and its places. */
interface Formula {
  readonly tree: Calculation;
  readonly named: readonly string[];
  readonly decimals: number;
}

/** A page, group or field, with what the evaluation decides about it. */
interface Node {
  readonly id: string;
  readonly type: ElementType;
  /** The element that holds it; none for a page. */
  readonly container: Node | null;
  /** The repeatable group whose rows hold it; none outside one. */
  readonly repeater: Node | null;
  readonly kind: Kind | null;
  readonly required: boolean;
  /** Whether a field can be required: one with a kind, or one that takes answers no condition compares. */
  readonly canBeRequired: boolean;
  readonly rules: readonly Rule[];
  readonly repeats: boolean;
  readonly calculation: LogicCalculation | null;
  readonly dependents: Node[];
  waiting: number;
  hidden: boolean;
  /** A child field's being hidden, row by row. */
  hiddenIn: readonly boolean[];
  /** A repeatable group's rows. */
  rows: readonly RowBuilder[];
  /** A calculation as the check read it. */
  formula: Formula | null;
  /** A calculation's result, and a child calculation's row by row. */
  value: string | null;
  valueIn: readonly (string | null)[];
}

interface Subject {
  readonly kind: Kind;
  /** The element whose being shown decides the subject's: the field, or a grid row's grid. */
  readonly owner: Node;
}

const effectsOf = (type: ElementType): readonly string[] =>
  type === 'field' ? ['show', 'hide', 'require'] : ['show', 'hide'];

function node(
  id: string,
  type: ElementType,
  container: Node | null,
  field?: LogicField,
  repeater: Node | null = null,
): Node {
  const children = field?.fields ?? [];
  return {
    id,
    type,
    container,
    repeater,
    kind: field?.kind ?? null,
    required: field?.required === true,
    canBeRequired: (field?.kind ?? null) !== null || field?.canBeRequired === true,
    rules: [],
    repeats: children.length > 0,
    calculation: field?.calculation ?? null,
    dependents: [],
    waiting: 0,
    hidden: false,
    hiddenIn: [],
    rows: [],
    formula: null,
    value: null,
    valueIn: [],
  };
}

/**
 * The form's elements in document order, its subjects by id and the ids of its fields by alias; an id or an alias given
 * twice, or a calculation on a field that does not hold a number, is `invalid_form`.
 */
function index(form: LogicForm): { nodes: Node[]; subjects: Map<string, Subject>; aliases: Map<string, string> } {
  const nodes: Node[] = [];
  const subjects = new Map<string, Subject>();
  const aliases = new Map<string, string>();
  const ids = new Set<string>();
  const claim = (id: unknown): string => {
    if (typeof id !== 'string' || ids.has(id)) throw new LogicError('invalid_form');
    ids.add(id);
    return id;
  };
  const add = (created: Node, rules: readonly Rule[] | undefined): Node => {
    const element = { ...created, rules: rules ?? [] };
    nodes.push(element);
    return element;
  };
  const fields = (list: readonly LogicField[], container: Node, repeater: Node | null): void => {
    for (const field of list) {
      const element = add(node(claim(field.id), 'field', container, field, repeater), field.rules);
      // A repeatable group holds no repeatable group.
      if (repeater !== null && element.repeats) throw new LogicError('invalid_form');
      if (element.kind !== null) subjects.set(element.id, { kind: element.kind, owner: element });
      if (typeof field.alias === 'string' && element.kind !== null) {
        if (aliases.has(field.alias)) throw new LogicError('invalid_form');
        aliases.set(field.alias, element.id);
      }
      if (element.calculation !== null && element.kind !== 'number') throw new LogicError('invalid_form');
      for (const row of field.rows ?? []) subjects.set(claim(row.id), { kind: row.kind, owner: element });
      fields(field.fields ?? [], element, element.repeats ? element : repeater);
    }
  };
  for (const page of form.pages) {
    const p = add(node(claim(page.id), 'page', null), page.rules);
    for (const group of page.groups ?? [])
      fields(group.fields ?? [], add(node(claim(group.id), 'group', p), group.rules), null);
  }
  for (const item of [...(form.validationRules ?? []), ...(form.actions ?? []), ...(form.outcomes ?? [])])
    claim(item.id);
  return { nodes, subjects, aliases };
}

/** The places a calculation's result is rounded to: 0 to 15, 2 when unset (ADR-0005). */
function decimalsOf(calculation: LogicCalculation): number {
  const decimals = calculation.decimals ?? 2;
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > 15) throw new LogicError('invalid_form');
  return decimals;
}

/**
 * An element's rules: effects it takes, one rule per effect and not both show and hide (`invalid_rule`), then each
 * rule's conditions.
 */
function checkRules(
  rules: readonly Rule[],
  effects: readonly string[],
  canBeRequired: boolean,
  kinds: Readonly<Record<string, Kind>>,
  patterns: Map<string, Pattern>,
): void {
  const seen = new Set<string>();
  for (const rule of rules) {
    const effect = rule.effect;
    if (!effects.includes(effect) || (effect === 'require' && !canBeRequired) || seen.has(effect))
      throw new LogicError('invalid_rule');
    if ((effect === 'show' && seen.has('hide')) || (effect === 'hide' && seen.has('show')))
      throw new LogicError('invalid_rule');
    seen.add(effect);
    checkGroup(rule, kinds, patterns);
  }
}

/** The ids of the fields a group of conditions looks at, through its conditions and their operands. */
function looksAt(group: Group, into: Set<string>): void {
  const operand = (value: unknown): void => {
    if (isField(value)) into.add(value.field);
    else if (Array.isArray(value)) value.forEach(operand);
  };
  for (const item of group.conditions) {
    if (isGroup(item)) looksAt(item, into);
    else {
      if (typeof item.field === 'string') into.add(item.field);
      operand(item.value);
    }
  }
}

const ruleOf = (rules: readonly Rule[], ...effects: string[]): Rule | undefined =>
  rules.find((rule) => rule.enabled && effects.includes(rule.effect));

/**
 * The elements in dependency order, the ready ones in document order; a cycle is `cycle`. An element waits for what
 * holds it, for the fields its show or hide rule looks at and, for a calculation, for the fields its formula names.
 */
function order(
  nodes: readonly Node[],
  subjects: ReadonlyMap<string, Subject>,
  aliases: ReadonlyMap<string, string>,
): Node[] {
  for (const element of nodes) {
    const on = new Set<Node>();
    if (element.container !== null) on.add(element.container);
    for (const alias of element.formula?.named ?? []) {
      const owner = subjects.get(aliases.get(alias) ?? '')?.owner;
      if (owner !== undefined) on.add(owner);
    }
    const visibility = ruleOf(element.rules, 'show', 'hide');
    if (visibility !== undefined) {
      const named = new Set<string>();
      looksAt(visibility, named);
      for (const id of named) {
        const owner = subjects.get(id)?.owner;
        if (owner !== undefined) on.add(owner);
      }
    }
    for (const dependency of on) {
      dependency.dependents.push(element);
      element.waiting++;
    }
  }
  const ready = nodes.filter((element) => element.waiting === 0);
  // The loop also reaches the elements it makes ready.
  for (const element of ready) {
    for (const dependent of element.dependents) if (--dependent.waiting === 0) ready.push(dependent);
  }
  if (ready.length < nodes.length) throw new LogicError('cycle');
  return ready;
}

const push = (map: Map<string, string[]>, key: string, value: string): void => {
  const list = map.get(key);
  if (list === undefined) map.set(key, [value]);
  else list.push(value);
};

/** Checks the whole form, then decides what its rules decide (ADR-0061). */
export function evaluateForm(form: LogicForm, answers: FormAnswers, context: Context): FormState {
  const { nodes, subjects, aliases } = index(form);
  const kinds: Readonly<Record<string, Kind>> = Object.fromEntries([...subjects].map(([id, s]) => [id, s.kind]));
  const kindOf = (alias: string): Kind | undefined => subjects.get(aliases.get(alias) ?? '')?.kind;

  // The check, before anything is evaluated.
  const patterns = new Map<string, Pattern>();
  for (const element of nodes)
    checkRules(element.rules, effectsOf(element.type), element.canBeRequired, kinds, patterns);
  for (const rule of form.validationRules ?? []) {
    if (!subjects.has(rule.field)) throw new LogicError('unknown_field');
    checkGroup(rule, kinds, patterns);
  }
  for (const action of form.actions ?? []) checkRules(action.rules ?? [], ['run'], false, kinds, patterns);
  for (const outcome of form.outcomes ?? []) checkRules(outcome.rules ?? [], ['use'], false, kinds, patterns);
  for (const element of nodes) {
    if (element.calculation === null) continue;
    const tree = parseFormula(element.calculation.formula);
    element.formula = { tree, named: checkFormula(tree, kindOf), decimals: decimalsOf(element.calculation) };
  }
  const sequence = order(nodes, subjects, aliases);

  // Visibility, in dependency order.
  const values = answers.values ?? {};
  const rowsOf = (repeater: Node): Rows => own(answers.rows ?? {}, repeater.id) ?? [];
  // A calculation's answer is its result, never what was posted for it; row -1 is outside the rows.
  const given = (owner: Node, id: string, row: number): readonly string[] | undefined => {
    if (owner.formula !== null) {
      const value = row < 0 ? owner.value : (owner.valueIn[row] ?? null);
      return value === null ? [] : [value];
    }
    if (row < 0 || owner.repeater === null) return own(values, id);
    return own(rowsOf(owner.repeater)[row] ?? {}, id);
  };
  const answersOf = (id: string, repeater: Node | null, row: number): readonly string[] | undefined => {
    const owner = subjects.get(id)?.owner;
    if (owner === undefined) return undefined;
    if (owner.repeater === null) return owner.hidden ? undefined : given(owner, id, -1);
    const rows = rowsOf(owner.repeater);
    if (owner.repeater === repeater) return owner.hiddenIn[row] === true ? undefined : given(owner, id, row);
    return rows.flatMap((_, r) => (owner.hiddenIn[r] === true ? [] : (given(owner, id, r) ?? [])));
  };
  const global: Scope = { fields: kinds, context, answers: (id) => answersOf(id, null, -1) };
  const scopeOf = (repeater: Node | null, row: number): Scope =>
    repeater === null ? global : { fields: kinds, context, answers: (id) => answersOf(id, repeater, row) };
  const shown = (element: Node, scope: Scope): boolean => {
    const rule = ruleOf(element.rules, 'show', 'hide');
    if (rule === undefined) return true;
    return matchesGroup(rule, scope, patterns) === (rule.effect === 'show');
  };
  let day: number | undefined;
  const siteToday = (): number => (day ??= today(context.now, context.timeZone));
  // A shown calculation over its scope, rounded to its places; a failure of the arithmetic leaves it without a value.
  const work = (formula: Formula, scope: Scope): string | null => {
    const reads: FormulaScope = {
      kind: kindOf,
      answers: (alias) => {
        const id = aliases.get(alias);
        return id === undefined ? undefined : scope.answers(id);
      },
      today: siteToday,
    };
    try {
      const result = calculate(formula.tree, reads);
      return result === null ? null : formatDecimal(round(result, formula.decimals));
    } catch (error) {
      if (error instanceof LogicError && (error.code === 'division_by_zero' || error.code === 'out_of_range'))
        return null;
      throw error;
    }
  };
  for (const element of sequence) {
    const repeater = element.repeater;
    const formula = element.formula;
    if (repeater !== null) {
      element.hiddenIn = rowsOf(repeater).map((_, row) => repeater.hidden || !shown(element, scopeOf(repeater, row)));
      if (formula !== null)
        element.valueIn = element.hiddenIn.map((hidden, row) =>
          hidden ? null : work(formula, scopeOf(repeater, row)),
        );
    } else {
      element.hidden = element.container?.hidden === true || !shown(element, global);
      if (formula !== null && !element.hidden) element.value = work(formula, global);
    }
  }

  // What follows from the final visibility.
  const requires = (element: Node, scope: Scope): boolean => {
    if (element.required) return true;
    const rule = ruleOf(element.rules, 'require');
    return rule !== undefined && matchesGroup(rule, scope, patterns);
  };
  const hidden: string[] = [];
  const required: string[] = [];
  const messages = new Map<string, string[]>();
  const calculations = new Map<string, string | null>();
  for (const element of nodes) {
    if (element.repeats)
      element.rows = rowsOf(element).map(() => ({
        hidden: [],
        required: [],
        messages: new Map(),
        calculations: new Map(),
      }));
  }
  for (const element of nodes) {
    const repeater = element.repeater;
    if (repeater !== null) {
      repeater.rows.forEach((row, at) => {
        if (element.hiddenIn[at] === true) {
          row.hidden.push(element.id);
          return;
        }
        if (element.formula !== null) row.calculations.set(element.id, element.valueIn[at] ?? null);
        if (element.canBeRequired && requires(element, scopeOf(repeater, at))) row.required.push(element.id);
      });
    } else if (element.hidden) hidden.push(element.id);
    else {
      if (element.formula !== null) calculations.set(element.id, element.value);
      if (element.canBeRequired && requires(element, global)) required.push(element.id);
    }
  }
  for (const rule of form.validationRules ?? []) {
    const owner = subjects.get(rule.field)?.owner;
    if (!rule.enabled || owner === undefined) continue;
    const repeater = owner.repeater;
    if (repeater !== null) {
      repeater.rows.forEach((row, at) => {
        if (owner.hiddenIn[at] !== true && matchesGroup(rule, scopeOf(repeater, at), patterns))
          push(row.messages, rule.field, rule.id);
      });
    } else if (!owner.hidden && matchesGroup(rule, global, patterns)) push(messages, rule.field, rule.id);
  }
  const applies = (rules: readonly Rule[] | undefined, effect: string): boolean => {
    const rule = ruleOf(rules ?? [], effect);
    return rule === undefined || matchesGroup(rule, global, patterns);
  };

  return {
    hidden,
    required,
    messages: Object.fromEntries(messages),
    calculations: Object.fromEntries(calculations),
    rows: Object.fromEntries(
      nodes
        .filter((element) => element.repeats)
        .map((element) => [
          element.id,
          element.rows.map((row) => ({
            hidden: row.hidden,
            required: row.required,
            messages: Object.fromEntries(row.messages),
            calculations: Object.fromEntries(row.calculations),
          })),
        ]),
    ),
    actions: (form.actions ?? []).filter((action) => applies(action.rules, 'run')).map((action) => action.id),
    outcome: (form.outcomes ?? []).find((outcome) => applies(outcome.rules, 'use'))?.id ?? null,
  };
}
