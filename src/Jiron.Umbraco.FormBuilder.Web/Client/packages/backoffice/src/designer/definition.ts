// A form's definition as the designer edits it (ROADMAP 9.3a): the draft's JSON as the management API gives it, typed
// where the editor model reads or writes it. Every other property is kept as it came, so a save sends back what the
// designer does not know about, untouched. Keys are GUIDs as text, and texts hold one string per language ('' is the
// default).

/** A text in each language: '' is the default. */
export type LocalizedText = Readonly<Record<string, string>>;

/** A rule on an element: show, hide or require for an element, run for an action, use for an outcome. */
export interface DesignRule {
  readonly key: string;
  readonly effect: string;
  readonly enabled?: boolean;
  readonly match?: 'all' | 'any';
  readonly conditions: readonly unknown[];
  readonly [other: string]: unknown;
}

/** A grid's row: a subject of conditions, shown with its grid. */
export interface DesignGridRow {
  readonly key: string;
  readonly label?: LocalizedText | null;
  readonly [other: string]: unknown;
}

/** A field; one whose type holds child fields is a repeatable group, and its children are `fields`. */
export interface DesignField {
  readonly key: string;
  readonly type: string;
  readonly alias?: string | null;
  readonly formerAliases?: readonly string[] | null;
  readonly label?: LocalizedText | null;
  readonly rules?: readonly DesignRule[] | null;
  readonly rows?: readonly DesignGridRow[] | null;
  readonly fields?: readonly DesignField[] | null;
  readonly [other: string]: unknown;
}

/** A group of fields on a page. */
export interface DesignGroup {
  readonly key: string;
  readonly title?: LocalizedText | null;
  readonly rules?: readonly DesignRule[] | null;
  readonly fields: readonly DesignField[];
  readonly [other: string]: unknown;
}

/** A page of the form. */
export interface DesignPage {
  readonly key: string;
  readonly title?: LocalizedText | null;
  readonly rules?: readonly DesignRule[] | null;
  readonly groups: readonly DesignGroup[];
  readonly [other: string]: unknown;
}

/** The whole definition: pages, then what the designer passes through (settings, validation rules, actions, outcomes). */
export interface DesignDefinition {
  readonly pages: readonly DesignPage[];
  readonly [other: string]: unknown;
}

/**
 * A definition as the model reads it: the server writes its canonical form, which leaves out every empty list, so a
 * page without groups, a group without fields and a rule without conditions come back with an empty one. Everything
 * else stays as it came.
 */
export function normalise(definition: unknown): DesignDefinition {
  const given = (definition ?? {}) as { pages?: readonly DesignPage[] | null } & Record<string, unknown>;
  const rules = <T extends { rules?: readonly DesignRule[] | null }>(element: T): T =>
    element.rules
      ? { ...element, rules: element.rules.map((rule) => (rule.conditions ? rule : { ...rule, conditions: [] })) }
      : element;
  const field = (one: DesignField): DesignField => rules(one.fields ? { ...one, fields: one.fields.map(field) } : one);
  return {
    ...given,
    pages: (given.pages ?? []).map((page) =>
      rules({
        ...page,
        groups: (page.groups ?? []).map((group) => rules({ ...group, fields: (group.fields ?? []).map(field) })),
      }),
    ),
  };
}

/** Whether an element is a page: it holds groups. */
export const isPage = (element: DesignPage | DesignGroup | DesignField): element is DesignPage =>
  Array.isArray((element as DesignPage).groups);

/** Whether an element is a field: it has a type. */
export const isField = (element: DesignPage | DesignGroup | DesignField): element is DesignField =>
  typeof (element as DesignField).type === 'string' && !isPage(element);

/** The kinds of element the designer selects and moves. */
export type ElementKind = 'page' | 'group' | 'field';

/** A page, group or field, with where it is: its container (null for a page) and its place there. */
export type Located =
  | { readonly kind: 'page'; readonly element: DesignPage; readonly container: null; readonly index: number }
  | { readonly kind: 'group'; readonly element: DesignGroup; readonly container: DesignPage; readonly index: number }
  | {
      readonly kind: 'field';
      readonly element: DesignField;
      /** The group that holds it, or the repeatable group whose child it is. */
      readonly container: DesignGroup | DesignField;
      readonly index: number;
    };

/** Finds a page, group or field by its key. */
export function locate(definition: DesignDefinition, key: string): Located | undefined {
  const inFields = (fields: readonly DesignField[], container: DesignGroup | DesignField): Located | undefined => {
    for (const [index, field] of fields.entries()) {
      if (field.key === key) return { kind: 'field', element: field, container, index };
      const child = inFields(field.fields ?? [], field);
      if (child) return child;
    }
    return undefined;
  };
  for (const [pageIndex, page] of definition.pages.entries()) {
    if (page.key === key) return { kind: 'page', element: page, container: null, index: pageIndex };
    for (const [groupIndex, group] of page.groups.entries()) {
      if (group.key === key) return { kind: 'group', element: group, container: page, index: groupIndex };
      const field = inFields(group.fields, group);
      if (field) return field;
    }
  }
  return undefined;
}

/** Every field of the definition, repeatable groups' children included, in document order. */
export function allFields(definition: DesignDefinition): DesignField[] {
  const out: DesignField[] = [];
  const add = (fields: readonly DesignField[]) => {
    for (const field of fields) {
      out.push(field);
      add(field.fields ?? []);
    }
  };
  for (const page of definition.pages) for (const group of page.groups) add(group.fields);
  return out;
}

/** The page a page, group or field is on. */
export function pageOf(definition: DesignDefinition, key: string): DesignPage | undefined {
  return definition.pages.find((page) => page.key === key || locateIn(page, key));
}

const locateIn = (page: DesignPage, key: string): boolean => locate({ pages: [page] }, key) !== undefined;
