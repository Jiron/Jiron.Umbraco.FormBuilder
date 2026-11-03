// The designer's editor model (ROADMAP 9.3a, ADR-0016): pure functions from one state to the next, which the canvas,
// the keyboard, drag and drop and Move to... all call, so each refuses the same moves. A state is the definition and the
// selected element; a function never changes what it is given. A move that would make an element wait on itself is
// refused, as the form engine would refuse the form: a group shown by a field it would then hold, for one. So is a field
// a repeatable group can't hold: another repeatable group, a type a form has once, or the field the email check reads.
import {
  hasCycle,
  LogicError,
  type Calculation,
  type Formulas,
  type LogicField,
  type LogicForm,
  type Rule,
} from '@jfb/logic';
import {
  allFields,
  isField,
  isPage,
  locate,
  type DesignDefinition,
  type DesignField,
  type DesignGroup,
  type DesignPage,
  type DesignRule,
  type ElementKind,
} from './definition';

/** What the editor shows: the definition and the selected element's key. */
export interface EditorState {
  readonly definition: DesignDefinition;
  readonly selection: string | null;
}

/** What the model needs to know of a field type. */
export interface FieldTraits {
  /** The kind a rule compares its answers as; none for a type no rule looks at. */
  readonly kind: string | null;
  /** Whether a form holds it once at most. */
  readonly onePerForm: boolean;
  /** Whether it holds child fields: the repeatable group. */
  readonly childFields: boolean;
}

/** What the model asks of its caller: the field types, fresh keys and, for formulas, the calculation language. */
export interface ModelContext {
  readonly traitsOf: (type: string) => FieldTraits | undefined;
  readonly newKey: () => string;
  readonly formulas?: Formulas | null;
}

/** Where an element goes: into a container (a page for a group, a group or repeatable group for a field, null for a page) at a place. */
export interface Place {
  readonly container: string | null;
  /** Its place among the container's elements once it is there; past the end is the end. */
  readonly index: number;
}

/** Why the model refused an edit. */
export type Refusal =
  /** No element or container with that key. */
  | 'missing'
  /** The element is there already. */
  | 'unchanged'
  /** There is no neighbour to step to. */
  | 'edge'
  /** The container can't hold it: a group outside a page, a field outside a group, an element inside itself. */
  | 'wrongPlace'
  /** An element would wait on itself through what holds it and the fields its rules look at. */
  | 'cycle'
  /** A repeatable group can't hold it. */
  | 'notInRepeater'
  /** A form holds its type once. */
  | 'onePerForm'
  /** A form keeps one page at least. */
  | 'lastPage'
  /** Another field has the alias, or had it, or the alias is a reserved name. */
  | 'aliasTaken'
  /** The field types are not read yet, so nothing can be checked against them. */
  | 'notReady';

/** An edit's result: the next state, or why there is none. */
export type Edit =
  { readonly ok: true; readonly state: EditorState } | { readonly ok: false; readonly refusal: Refusal };

const refused = (refusal: Refusal): Edit => ({ ok: false, refusal });
const done = (definition: DesignDefinition, selection: string | null): Edit => ({
  ok: true,
  state: { definition, selection },
});

/** The aliases no field may take, in any letter case (section 4.3.1), as the server keeps them. */
export const RESERVED_ALIASES: ReadonlySet<string> = new Set([
  'form',
  'entry',
  'page',
  'member',
  'query',
  'context',
  'site',
  'date',
  'ufprt',
]);

const MAX_ALIAS = 64;

/** Whether no field of the definition has or had an alias, in any letter case, and it is no reserved name. */
export function aliasFree(definition: DesignDefinition, alias: string): boolean {
  const lower = alias.toLowerCase();
  if (RESERVED_ALIASES.has(lower)) return false;
  return allFields(definition).every(
    (field) =>
      field.alias?.toLowerCase() !== lower &&
      !(field.formerAliases ?? []).some((former) => former.toLowerCase() === lower),
  );
}

/**
 * An alias no field of the definition has or had, from a base: the base itself while it is free, else the base with the
 * lowest free number from 2. A base that is no alias (a lowercase letter, then letters and digits) becomes `field`.
 * `alsoTaken` holds aliases, in lowercase, that count as taken too.
 */
export function uniqueAlias(
  definition: DesignDefinition,
  base: string,
  alsoTaken: ReadonlySet<string> = new Set(),
): string {
  const taken = new Set<string>(alsoTaken);
  for (const field of allFields(definition)) {
    if (field.alias) taken.add(field.alias.toLowerCase());
    for (const former of field.formerAliases ?? []) taken.add(former.toLowerCase());
  }
  const free = (alias: string) => !taken.has(alias.toLowerCase()) && !RESERVED_ALIASES.has(alias.toLowerCase());
  const start = /^[a-z][A-Za-z0-9]*$/.test(base) ? base.slice(0, MAX_ALIAS) : 'field';
  if (free(start)) return start;
  const stem = start.replace(/\d+$/, '') || 'field';
  for (let number = 2; ; number++) {
    const suffix = String(number);
    const alias = stem.slice(0, MAX_ALIAS - suffix.length) + suffix;
    if (free(alias)) return alias;
  }
}

/** Selects an element, or nothing with null. */
export function select(state: EditorState, key: string | null): Edit {
  if (key !== null && !locate(state.definition, key)) return refused('missing');
  return done(state.definition, key);
}

/**
 * Changes an element in place, keeping its key and where it is; a change to the key is ignored. It is checked as a move
 * is: a field's alias must be free, its type must be one a repeatable group around it can hold and, for a type a form
 * has once, the only one; and no rule may come to wait on itself. A change that changes nothing is `unchanged`.
 */
export function change<T extends DesignPage | DesignGroup | DesignField>(
  state: EditorState,
  key: string,
  update: (element: T) => T,
  context: ModelContext,
): Edit {
  const found = locate(state.definition, key);
  if (!found) return refused('missing');
  const next = { ...update(found.element as T), key };
  if (JSON.stringify(next) === JSON.stringify(found.element)) return refused('unchanged');
  const definition = replace(state.definition, key, next);
  if (found.kind === 'field') {
    const before = found.element as DesignField;
    const after = next as DesignField;
    const others = remove(state.definition, key);
    if (after.alias && after.alias !== before.alias && !aliasFree(others, after.alias)) return refused('aliasTaken');
    if (after.type !== before.type && context.traitsOf(after.type)?.onePerForm && hasType(others, after.type))
      return refused('onePerForm');
    if (found.container && isField(found.container) && !repeaterHolds(definition, after, context))
      return refused('notInRepeater');
  }
  if (makesCycle(state.definition, definition, context)) return refused('cycle');
  return done(definition, state.selection);
}

/**
 * Inserts a new page, group or field at a place and selects it. Each field in it takes a free alias if its own is taken;
 * a type a form has once is refused when the form has it already. The caller gives everything in it keys of its own.
 */
export function insert(
  state: EditorState,
  kind: ElementKind,
  element: DesignPage | DesignGroup | DesignField,
  place: Place,
  context: ModelContext,
): Edit {
  if (locate(state.definition, element.key)) throw new Error(`The definition has an element ${element.key} already.`);
  const once = fieldsIn(element).find(
    (field) => context.traitsOf(field.type)?.onePerForm && hasType(state.definition, field.type),
  );
  if (once) return refused('onePerForm');
  return placed(state.definition, state.definition, kind, freeAliases(state.definition, element), place, context);
}

/** Moves a page, group or field to a place and selects it. */
export function move(state: EditorState, key: string, place: Place, context: ModelContext): Edit {
  const found = locate(state.definition, key);
  if (!found) return refused('missing');
  if (place.container !== null && (place.container === key || within(found.element, place.container)))
    return refused('wrongPlace');
  const sameContainer = (found.container?.key ?? null) === place.container;
  const without = remove(state.definition, key);
  const index = Math.max(0, Math.min(place.index, sizeOf(without, place.container) ?? 0));
  if (sameContainer && index === found.index) return refused('unchanged');
  return placed(state.definition, without, found.kind, found.element, { container: place.container, index }, context);
}

/**
 * Moves an element one place up (-1) or down (1), as Alt+Up and Alt+Down do. At the end of its container a field steps
 * into the neighbouring group, across pages too, and a group into the neighbouring page; a repeatable group's child
 * stays among its siblings, and a page among the pages.
 */
export function step(state: EditorState, key: string, by: 1 | -1, context: ModelContext): Edit {
  const found = locate(state.definition, key);
  if (!found) return refused('missing');
  const siblings = found.container === null ? state.definition.pages.length : childrenOf(found.container).length;
  const index = found.index + by;
  if (index >= 0 && index < siblings)
    return move(state, key, { container: found.container?.key ?? null, index }, context);

  const neighbour = (list: readonly { key: string }[], at: string) =>
    list[list.findIndex((item) => item.key === at) + by];
  if (found.kind === 'group') {
    const page = neighbour(state.definition.pages, found.container.key) as DesignPage | undefined;
    if (!page) return refused('edge');
    return move(state, key, { container: page.key, index: by === 1 ? 0 : page.groups.length }, context);
  }
  if (found.kind === 'field' && locate(state.definition, found.container.key)?.kind === 'group') {
    const groups = state.definition.pages.flatMap((page) => page.groups);
    const group = neighbour(groups, found.container.key) as DesignGroup | undefined;
    if (!group) return refused('edge');
    return move(state, key, { container: group.key, index: by === 1 ? 0 : group.fields.length }, context);
  }
  return refused('edge');
}

/** Moves a field to the end of a group, as Move to... does. */
export function moveToGroup(state: EditorState, key: string, groupKey: string, context: ModelContext): Edit {
  const group = locate(state.definition, groupKey);
  if (group?.kind !== 'group') return refused('missing');
  return move(state, key, { container: groupKey, index: group.element.fields.length }, context);
}

/**
 * Duplicates a page, group or field just after it and selects the copy. Everything in the copy gets a fresh key, a
 * reference from the copy to something it copied points at the copy of it, and each copied field takes a free alias.
 */
export function duplicate(state: EditorState, key: string, context: ModelContext): Edit {
  const found = locate(state.definition, key);
  if (!found) return refused('missing');
  const once = fieldsIn(found.element).find((field) => context.traitsOf(field.type)?.onePerForm);
  if (once) return refused('onePerForm');

  const keys = new Map<string, string>();
  collectKeys(found.element, keys, context.newKey);
  const renamed = new Map<string, string>();
  const copy = repointFormulas(
    freeAliases(state.definition, rekey(found.element, keys) as DesignPage | DesignGroup | DesignField, renamed),
    renamed,
    context.formulas ?? null,
  );
  return placed(
    state.definition,
    state.definition,
    found.kind,
    copy,
    { container: found.container?.key ?? null, index: found.index + 1 },
    context,
  );
}

/**
 * Removes a page, group or field with everything in it. The selection goes to the element that took its place, else
 * the one before it, else its container, as the focus does after a delete (ADR-0016).
 */
export function removeElement(state: EditorState, key: string): Edit {
  const found = locate(state.definition, key);
  if (!found) return refused('missing');
  if (found.kind === 'page' && state.definition.pages.length === 1) return refused('lastPage');
  const siblings = found.container === null ? state.definition.pages : childrenOf(found.container);
  const next = siblings[found.index + 1] ?? siblings[found.index - 1] ?? found.container ?? null;
  return done(remove(state.definition, key), next?.key ?? null);
}

/**
 * Starts a new page at a place, as the palette's New page (under Layout) does: what follows the place on its page moves onto a
 * new page right after it. A place in a group splits the group there, its fields from that place on going into a group
 * of their own on the new page (the whole group when the place is its start); a place in a repeatable group splits
 * after it; a place on a page moves its groups from that place on. A page left without a group keeps an empty one, and
 * a new page with nothing to hold gets one. The new page is selected.
 */
export function splitPage(state: EditorState, place: Place, context: ModelContext): Edit {
  if (place.container === null) return refused('wrongPlace');
  const found = locate(state.definition, place.container);
  if (!found) return refused('missing');
  let pageKey: string;
  let groupIndex: number;
  let fieldIndex: number | null;
  if (found.kind === 'page') [pageKey, groupIndex, fieldIndex] = [found.element.key, place.index, null];
  else if (found.kind === 'group') [pageKey, groupIndex, fieldIndex] = [found.container.key, found.index, place.index];
  else {
    const group = locate(state.definition, found.container.key);
    if (group?.kind !== 'group') return refused('wrongPlace');
    [pageKey, groupIndex, fieldIndex] = [group.container.key, group.index, found.index + 1];
  }

  const pages = state.definition.pages;
  const at = pages.findIndex((page) => page.key === pageKey);
  const page = pages[at];
  if (!page) return refused('missing');
  const groups = page.groups;
  const empty = (): DesignGroup => ({ key: context.newKey(), fields: [] });
  let kept: DesignGroup[];
  let moved: DesignGroup[];
  const split = fieldIndex === null ? undefined : groups[groupIndex];
  if (!split || fieldIndex === null || fieldIndex <= 0) {
    kept = groups.slice(0, groupIndex);
    moved = groups.slice(groupIndex);
  } else {
    const rest = split.fields.slice(fieldIndex);
    kept = [...groups.slice(0, groupIndex), { ...split, fields: split.fields.slice(0, fieldIndex) }];
    // The fields that move keep their group's columns and style; its title, text and rules stay with the group.
    const look = Object.fromEntries(
      ['columns', 'style'].flatMap((name) => (name in split ? [[name, split[name]]] : [])),
    );
    moved = [
      ...(rest.length > 0 ? [{ ...look, key: context.newKey(), fields: rest }] : []),
      ...groups.slice(groupIndex + 1),
    ];
  }
  const added: DesignPage = { key: context.newKey(), groups: moved.length > 0 ? moved : [empty()] };
  const next: DesignDefinition = {
    ...state.definition,
    pages: [
      ...pages.slice(0, at),
      { ...page, groups: kept.length > 0 ? kept : [empty()] },
      added,
      ...pages.slice(at + 1),
    ],
  };
  if (makesCycle(state.definition, next, context)) return refused('cycle');
  return done(next, added.key);
}

/**
 * The keys of the elements, validation rules, actions and outcomes outside an element that name it or anything in it,
 * anywhere in what they hold: a rule's condition or operand, a field check's other field, a cascading list's parent, the
 * field a validation rule checks, an email action's recipient field, an attachment; and `settings` when the form's
 * settings name it, as the email check does. A delete leaves those naming nothing, which the form check then reports.
 */
export function referencesTo(definition: DesignDefinition, key: string): string[] {
  const found = locate(definition, key);
  if (!found) return [];
  const inside = new Set<string>();
  collectKeys(found.element, new Map(), () => '', inside);
  const holders: readonly { key: string; [other: string]: unknown }[] = [
    ...definition.pages,
    ...definition.pages.flatMap((page) => page.groups),
    ...allFields(definition),
    ...listOf(definition['validationRules']),
    ...listOf(definition['actions']),
    ...listOf(definition['outcomes']),
    { key: 'settings', settings: definition['settings'] },
  ];
  return holders.filter((holder) => !inside.has(holder.key) && names(holder, inside)).map((holder) => holder.key);
}

// What follows is the plumbing: placing, removing and replacing elements without changing what was given.

/** Places an element and checks the result: the container takes it, a repeatable group can hold it, no rule waits on itself. */
function placed(
  before: DesignDefinition,
  definition: DesignDefinition,
  kind: ElementKind,
  element: DesignPage | DesignGroup | DesignField,
  place: Place,
  context: ModelContext,
): Edit {
  const container = place.container === null ? null : locate(definition, place.container);
  if (place.container !== null && !container) return refused('missing');
  const fits =
    kind === 'page'
      ? container === null
      : kind === 'group'
        ? container?.kind === 'page'
        : container?.kind === 'group' ||
          (container?.kind === 'field' && context.traitsOf(container.element.type)?.childFields === true);
  if (!fits) return refused('wrongPlace');
  if (container?.kind === 'field' && !repeaterHolds(definition, element as DesignField, context))
    return refused('notInRepeater');

  const next = add(definition, element, place);
  if (makesCycle(before, next, context)) return refused('cycle');
  return done(next, element.key);
}

/** Whether a repeatable group can hold a field: no repeatable group, no type a form has once, not the email check's field. */
function repeaterHolds(definition: DesignDefinition, field: DesignField, context: ModelContext): boolean {
  const checked = emailCheckField(definition);
  return fieldsIn(field).every((one) => {
    const traits = context.traitsOf(one.type);
    return !traits?.childFields && !traits?.onePerForm && one.key !== checked;
  });
}

const emailCheckField = (definition: DesignDefinition): string | undefined => {
  const settings = definition['settings'] as { submission?: { verification?: { emailField?: unknown } } } | undefined;
  const field = settings?.submission?.verification?.emailField;
  return typeof field === 'string' ? field : undefined;
};

/**
 * Whether a change makes an element wait on itself where it did not before. A definition the form engine can't read at
 * all (two fields with one alias, say) is the form check's to report; the model lets such a change through.
 */
function makesCycle(before: DesignDefinition, after: DesignDefinition, context: ModelContext): boolean {
  try {
    return (
      hasCycle(logicOf(after, context), context.formulas ?? null) &&
      !hasCycle(logicOf(before, context), context.formulas ?? null)
    );
  } catch (error) {
    if (error instanceof LogicError) return false;
    throw error;
  }
}

/** The form engine's model of a definition, for the cycle check: what holds what, the rules, the aliases and formulas. */
export function logicOf(definition: DesignDefinition, context: Pick<ModelContext, 'traitsOf'>): LogicForm {
  const rules = (list: readonly DesignRule[] | null | undefined): Rule[] =>
    (list ?? []).map((rule) => ({
      effect: rule.effect,
      enabled: rule.enabled ?? true,
      match: rule.match ?? 'all',
      conditions: (rule.conditions ?? []) as Rule['conditions'],
    }));
  const field = (one: DesignField): LogicField => {
    const kind = (context.traitsOf(one.type)?.kind ?? null) as NonNullable<LogicField['kind']> | null;
    const formula = (one['settings'] as { formula?: unknown } | null | undefined)?.formula;
    return {
      id: one.key,
      kind,
      alias: one.alias ?? null,
      calculation: one.type === 'calculation' && typeof formula === 'string' ? { formula } : null,
      rules: rules(one.rules),
      fields: (one.fields ?? []).map(field),
      // A grid's rows are what rules look at; their kind does not change what waits on what.
      rows: (one.rows ?? []).map((row) => ({ id: row.key, kind: 'choice' })),
    };
  };
  return {
    pages: definition.pages.map((page) => ({
      id: page.key,
      rules: rules(page.rules),
      groups: page.groups.map((group) => ({
        id: group.key,
        rules: rules(group.rules),
        fields: group.fields.map(field),
      })),
    })),
  };
}

const childrenOf = (container: DesignPage | DesignGroup | DesignField): readonly { key: string }[] =>
  isPage(container) ? container.groups : (container.fields ?? []);

/** How many elements a container holds (null: the pages); undefined for no such container. */
function sizeOf(definition: DesignDefinition, container: string | null): number | undefined {
  if (container === null) return definition.pages.length;
  const found = locate(definition, container);
  return found ? childrenOf(found.element).length : undefined;
}

/** Whether a key is that of an element inside another. */
function within(element: DesignPage | DesignGroup | DesignField, key: string): boolean {
  return locate({ pages: [asPage(element)] }, key) !== undefined && element.key !== key;
}

const asPage = (element: DesignPage | DesignGroup | DesignField): DesignPage =>
  isPage(element)
    ? element
    : isField(element)
      ? { key: '', groups: [{ key: '', fields: [element] }] }
      : { key: '', groups: [element] };

/** The fields of an element, itself if it is one, and their children. */
function fieldsIn(element: DesignPage | DesignGroup | DesignField): DesignField[] {
  return allFields({ pages: [asPage(element)] });
}

const hasType = (definition: DesignDefinition, type: string): boolean =>
  allFields(definition).some((field) => field.type === type);

/** The definition without the element of that key. */
function remove(definition: DesignDefinition, key: string): DesignDefinition {
  const fields = (list: readonly DesignField[]): DesignField[] =>
    list
      .filter((field) => field.key !== key)
      .map((field) => (field.fields ? { ...field, fields: fields(field.fields) } : field));
  return {
    ...definition,
    pages: definition.pages
      .filter((page) => page.key !== key)
      .map((page) => ({
        ...page,
        groups: page.groups
          .filter((group) => group.key !== key)
          .map((group) => ({ ...group, fields: fields(group.fields) })),
      })),
  };
}

/** The definition with an element added at a place, whose container exists. */
function add(
  definition: DesignDefinition,
  element: DesignPage | DesignGroup | DesignField,
  place: Place,
): DesignDefinition {
  const at = <T>(list: readonly T[]): T[] => {
    const next = [...list];
    next.splice(Math.min(place.index, next.length), 0, element as T);
    return next;
  };
  if (place.container === null) return { ...definition, pages: at(definition.pages) };
  const fields = (list: readonly DesignField[]): DesignField[] =>
    list.map((field) =>
      field.key === place.container
        ? { ...field, fields: at(field.fields ?? []) }
        : field.fields
          ? { ...field, fields: fields(field.fields) }
          : field,
    );
  return {
    ...definition,
    pages: definition.pages.map((page) =>
      page.key === place.container
        ? { ...page, groups: at(page.groups) }
        : {
            ...page,
            groups: page.groups.map((group) =>
              group.key === place.container
                ? { ...group, fields: at(group.fields) }
                : { ...group, fields: fields(group.fields) },
            ),
          },
    ),
  };
}

/** The definition with the element of a key replaced. */
function replace(
  definition: DesignDefinition,
  key: string,
  element: DesignPage | DesignGroup | DesignField,
): DesignDefinition {
  const fields = (list: readonly DesignField[]): DesignField[] =>
    list.map((field) =>
      field.key === key ? (element as DesignField) : field.fields ? { ...field, fields: fields(field.fields) } : field,
    );
  return {
    ...definition,
    pages: definition.pages.map((page) =>
      page.key === key
        ? (element as DesignPage)
        : {
            ...page,
            groups: page.groups.map((group) =>
              group.key === key ? (element as DesignGroup) : { ...group, fields: fields(group.fields) },
            ),
          },
    ),
  };
}

/** Collects every key inside a value (its own included), each with a fresh one, and optionally the old keys alone. */
function collectKeys(value: unknown, keys: Map<string, string>, newKey: () => string, into?: Set<string>): void {
  if (Array.isArray(value)) {
    for (const item of value) collectKeys(item, keys, newKey, into);
    return;
  }
  if (typeof value !== 'object' || value === null) return;
  const record = value as Record<string, unknown>;
  if (typeof record['key'] === 'string') {
    if (!keys.has(record['key'])) keys.set(record['key'], newKey());
    into?.add(record['key']);
  }
  for (const item of Object.values(record)) collectKeys(item, keys, newKey, into);
}

/** The properties that hold a key: an element's own, and a reference to a field as a rule, a check or a list names it. */
const KEYED = new Set(['key', 'field', 'parent']);

/** A copy of a value with every key, and every reference to one, taken from the map. */
function rekey(value: unknown, keys: ReadonlyMap<string, string>): unknown {
  if (Array.isArray(value)) return value.map((item) => rekey(item, keys));
  if (typeof value !== 'object' || value === null) return value;
  return Object.fromEntries(
    Object.entries(value).map(([name, item]) => [
      name,
      KEYED.has(name) && typeof item === 'string' ? (keys.get(item) ?? item) : rekey(item, keys),
    ]),
  );
}

/** An element whose fields each take a free alias, each counting as taken for the next one; `renamed` collects them. */
function freeAliases<T extends DesignPage | DesignGroup | DesignField>(
  definition: DesignDefinition,
  element: T,
  renamed = new Map<string, string>(),
): T {
  const given = new Set<string>();
  return realias(element, (alias) => {
    const free = uniqueAlias(definition, alias, given);
    given.add(free.toLowerCase());
    renamed.set(alias, free);
    return free;
  });
}

/**
 * An element whose calculations name the new aliases of the fields that were renamed with them, at the places the
 * calculation language says each name stands. Without the language, or for a formula that does not read, a formula
 * stays as it is, which the form check then reports.
 */
function repointFormulas<T extends DesignPage | DesignGroup | DesignField>(
  element: T,
  renamed: ReadonlyMap<string, string>,
  formulas: Formulas | null,
): T {
  if (formulas === null || [...renamed].every(([from, to]) => from === to)) return element;
  const formula = (text: string): string => {
    let tree: Calculation;
    try {
      tree = formulas.parse(text);
    } catch {
      return text;
    }
    const names: { field: string; at: number }[] = [];
    const collect = (item: Calculation): void => {
      if (typeof item === 'string') return;
      if ('field' in item) {
        if (item.at !== undefined) names.push({ field: item.field, at: item.at });
      } else item.args.forEach(collect);
    };
    collect(tree);
    // From the end, so each place still stands where the language said.
    return names
      .sort((one, other) => other.at - one.at)
      .reduce((written, name) => {
        const to = renamed.get(name.field);
        return to === undefined ? written : written.slice(0, name.at) + to + written.slice(name.at + name.field.length);
      }, text);
  };
  const field = (one: DesignField): DesignField => {
    const settings = one['settings'] as { formula?: unknown } | null | undefined;
    const repointed =
      one.type === 'calculation' && typeof settings?.formula === 'string'
        ? { ...one, settings: { ...settings, formula: formula(settings.formula) } }
        : one;
    return repointed.fields ? { ...repointed, fields: repointed.fields.map(field) } : repointed;
  };
  if (isPage(element))
    return { ...element, groups: element.groups.map((group) => ({ ...group, fields: group.fields.map(field) })) };
  if (isField(element)) return field(element) as T;
  return { ...element, fields: (element as DesignGroup).fields.map(field) };
}

/** A copy of an element whose fields take new aliases, and leave their former ones behind: those were the original's. */
function realias<T extends DesignPage | DesignGroup | DesignField>(element: T, aliasOf: (alias: string) => string): T {
  const field = (one: DesignField): DesignField => {
    const rest = Object.fromEntries(Object.entries(one).filter(([name]) => name !== 'formerAliases')) as DesignField;
    return {
      ...rest,
      ...(one.alias ? { alias: aliasOf(one.alias) } : {}),
      ...(one.fields ? { fields: one.fields.map(field) } : {}),
    };
  };
  if (isPage(element))
    return { ...element, groups: element.groups.map((group) => ({ ...group, fields: group.fields.map(field) })) };
  if (isField(element)) return field(element) as T;
  return { ...element, fields: (element as DesignGroup).fields.map(field) };
}

const listOf = (value: unknown): { key: string }[] => (Array.isArray(value) ? (value as { key: string }[]) : []);

/**
 * Whether an element or item names any of these keys anywhere in it, its own key aside, leaving out what it holds (a
 * page's groups, a group's or repeatable group's fields), which are holders of their own. Keys are GUIDs, so a text
 * that equals one names it.
 */
function names(holder: unknown, keys: ReadonlySet<string>): boolean {
  const walk = (value: unknown): boolean => {
    if (typeof value === 'string') return keys.has(value);
    if (Array.isArray(value)) return value.some(walk);
    if (typeof value !== 'object' || value === null) return false;
    return Object.entries(value as Record<string, unknown>).some(
      ([name, item]) => name !== 'key' && name !== 'groups' && name !== 'fields' && walk(item),
    );
  };
  return walk(holder);
}
