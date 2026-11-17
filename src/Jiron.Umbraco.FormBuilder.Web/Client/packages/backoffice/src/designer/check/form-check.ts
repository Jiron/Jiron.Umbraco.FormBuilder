// The form check in the designer (ROADMAP 9.5f, IA 5.1, ADR-0181): what the server's check of the draft found, and
// where each problem's Fix it leads. The server names each problem's place by a JSON path into the definition it was
// sent; this reads the path against that definition and gives the element's key, so Fix it still finds the element
// after later changes moved it. Pure.
import type { FieldSettingResponseModel, FieldTypeResponseModel } from '../../api';
import { validationRulesOf, type DesignDefinition, type DesignField } from '../definition';
import { messageFieldOf } from '../rules/rule-list';

/** How much a problem weighs: an error blocks publishing, a warning doesn't, a hint is a suggestion. */
export type CheckSeverity = 'error' | 'warning' | 'hint';

/** One problem the check found, as the management API sends it. */
export interface CheckIssue {
  readonly severity: CheckSeverity;
  readonly code: string;
  /** Where it is, as a JSON path into the definition checked: `$.pages[0].groups[1].fields[2].label['']`. */
  readonly path: string;
  /** The server's sentence, in English. */
  readonly message: string;
}

/** One step of a JSON path: a property's name, or an array's index. */
export type PathStep = string | number;

/**
 * The steps of a path as the server writes it (JsonPaths): `$`, then `.name` for a name that is an identifier,
 * `['name']` for any other (a quote and a backslash escaped by a backslash, a control character as `\u` and four hex
 * digits), and `[0]` for an index. Null for a path it doesn't write.
 */
export function parsePath(path: string): PathStep[] | null {
  if (!path.startsWith('$')) return null;
  const steps: PathStep[] = [];
  let at = 1;
  while (at < path.length) {
    const char = path[at];
    if (char === '.') {
      const name = /^[A-Za-z_][A-Za-z0-9_]*/.exec(path.slice(at + 1))?.[0];
      if (!name) return null;
      steps.push(name);
      at += 1 + name.length;
    } else if (char === '[' && path[at + 1] === "'") {
      let name = '';
      at += 2;
      for (;;) {
        const next = path[at];
        if (next === undefined) return null;
        if (next === "'") break;
        if (next === BACKSLASH) {
          const escaped = path[at + 1];
          if (escaped === "'" || escaped === BACKSLASH) {
            name += escaped;
            at += 2;
          } else if (escaped === 'u' && /^[0-9a-f]{4}$/i.test(path.slice(at + 2, at + 6))) {
            name += String.fromCharCode(Number.parseInt(path.slice(at + 2, at + 6), 16));
            at += 6;
          } else return null;
        } else {
          name += next;
          at += 1;
        }
      }
      if (path[at + 1] !== ']') return null;
      steps.push(name);
      at += 2;
    } else if (char === '[') {
      const digits = /^[0-9]+/.exec(path.slice(at + 1))?.[0];
      if (!digits || path[at + 1 + digits.length] !== ']') return null;
      steps.push(Number(digits));
      at += 2 + digits.length;
    } else return null;
  }
  return steps;
}

const BACKSLASH = String.fromCharCode(0x5c);

/**
 * What a problem's place is in the inspector: one of its items, by the kind the inspector draws it as, with the
 * property, the setting or the message it edits, in the section it is in when the element's type says so.
 */
export type InspectorItem =
  | { readonly kind: 'text'; readonly property: 'label' | 'help' | 'placeholder' | 'title' }
  | {
      readonly kind: 'switch';
      readonly property: 'required' | 'hideLabel' | 'sensitive' | 'includeInEmails';
    }
  | { readonly kind: 'plain'; readonly property: 'autocomplete' | 'cssClass' }
  | {
      readonly kind:
        | 'span'
        | 'columns'
        | 'style'
        | 'alias'
        | 'defaultValue'
        | 'prefill'
        | 'keep'
        | 'pattern'
        | 'unique'
        | 'matches'
        | 'options'
        | 'rows';
    }
  | { readonly kind: 'message'; readonly message: string }
  /** A type's setting, by its name, and the nested setting's within it. */
  | { readonly kind: 'setting'; readonly name: string; readonly inner?: string };

/** Where Fix it leads. */
export type FixTarget =
  /**
   * The element, selected, with the inspector on the item when there is one, in the language of the text the problem
   * is in where it is one ('' for the site's default).
   */
  | { readonly kind: 'element'; readonly key: string; readonly item?: InspectorItem; readonly culture?: string }
  /** The rules builder on a rule of the element, or on a validation rule that shows its message on it. */
  | { readonly kind: 'rule'; readonly key: string; readonly rule: string }
  /** The field picker, to add at the end of the page; to a new page where the form has none. */
  | { readonly kind: 'picker'; readonly page: string | null }
  /**
   * After sending on the Design view, with the outcome's sidebar on the setting the problem's path names, and the key of
   * the outcome the path is in, as the definition checked holds it, so a move of the outcomes since leads to the same one
   * (ADR-0185, ADR-0256).
   */
  | { readonly kind: 'outcome'; readonly path: string; readonly key?: string }
  /**
   * A view of the form where the place is edited: its settings, or its actions, with the problem's path, which that view
   * leads to (ADR-0184), and the key of the action the path is in, as the definition checked holds it.
   */
  | {
      readonly kind: 'view';
      readonly view: 'settings' | 'actions';
      readonly path: string;
      readonly key?: string;
    };

/** A field's properties that the inspector edits, by their name in the definition. */
const FIELD_ITEMS: Readonly<Record<string, InspectorItem>> = {
  label: { kind: 'text', property: 'label' },
  help: { kind: 'text', property: 'help' },
  placeholder: { kind: 'text', property: 'placeholder' },
  required: { kind: 'switch', property: 'required' },
  hideLabel: { kind: 'switch', property: 'hideLabel' },
  sensitive: { kind: 'switch', property: 'sensitive' },
  includeInEmails: { kind: 'switch', property: 'includeInEmails' },
  autocomplete: { kind: 'plain', property: 'autocomplete' },
  cssClass: { kind: 'plain', property: 'cssClass' },
  span: { kind: 'span' },
  alias: { kind: 'alias' },
  formerAliases: { kind: 'alias' },
  defaultValue: { kind: 'defaultValue' },
  prefill: { kind: 'prefill' },
  keep: { kind: 'keep' },
  options: { kind: 'options' },
  rows: { kind: 'rows' },
};

/** The checks of a field's validation list, by their kind, as the inspector edits them. */
const CHECK_ITEMS: Readonly<Record<string, InspectorItem>> = {
  pattern: { kind: 'pattern' },
  uniquePerForm: { kind: 'unique' },
  matchesField: { kind: 'matches' },
};

/** The field types by alias, whose settings say which take a text in each language. */
export type CheckTypes = Readonly<Record<string, FieldTypeResponseModel>>;

/** Whether a setting holds a text in each language, whose next step is a language. */
const isText = (setting: FieldSettingResponseModel | undefined) =>
  setting?.kind === 'localizedText' || setting?.kind === 'localizedHtml';

/** An item, with the language of the text it is in where the place is one. */
interface Found {
  readonly item?: InspectorItem | undefined;
  readonly culture?: string | undefined;
}

/** A text's language: the step after it, or the site's default for the text as a whole. */
const languageAt = (step: PathStep | undefined): string | undefined =>
  step === undefined ? '' : typeof step === 'string' ? step : undefined;

/** The item of a field that the steps after it name, if the inspector edits it, with the language of its text. */
function fieldItem(field: DesignField, rest: readonly PathStep[], types: CheckTypes | undefined): Found {
  const [first, second, third, fourth] = rest;
  if (typeof first !== 'string') return {};
  if (first === 'settings' && typeof second === 'string') {
    const setting = (types && Object.hasOwn(types, field.type) ? types[field.type] : undefined)?.settings.find(
      (one) => one.name === second,
    );
    // A text setting's next step is its language, a nested setting's its own setting's name.
    if (isText(setting)) return { item: { kind: 'setting', name: second }, culture: languageAt(third) };
    if (typeof third !== 'string') return { item: { kind: 'setting', name: second } };
    const nested = setting?.settings.find((one) => one.name === third);
    const item: InspectorItem = { kind: 'setting', name: second, inner: third };
    return isText(nested) ? { item, culture: languageAt(fourth) } : { item };
  }
  if (first === 'messages' && typeof second === 'string')
    return { item: { kind: 'message', message: second }, culture: languageAt(third) };
  if (first === 'validation' && typeof second === 'number') {
    const check = (field['validation'] as readonly Readonly<Record<string, unknown>>[] | undefined)?.[second];
    const kind = check?.['kind'];
    const item = typeof kind === 'string' && Object.hasOwn(CHECK_ITEMS, kind) ? CHECK_ITEMS[kind] : undefined;
    return third === 'message' ? { item, culture: languageAt(fourth) } : { item };
  }
  const item = Object.hasOwn(FIELD_ITEMS, first) ? FIELD_ITEMS[first] : undefined;
  return item?.kind === 'text' ? { item, culture: languageAt(second) } : { item };
}

/** The item of a group or a page that the steps after it name, with the language of its title. */
function containerItem(rest: readonly PathStep[], group: boolean): Found {
  const [first, second] = rest;
  if (first === 'title') return { item: { kind: 'text', property: 'title' }, culture: languageAt(second) };
  if (group && first === 'columns') return { item: { kind: 'columns' } };
  if (group && first === 'style') return { item: { kind: 'style' } };
  return {};
}

/** Fix it on an element, with the item and the language it found. */
const onElement = (key: string, found: Found): FixTarget => ({
  kind: 'element',
  key,
  ...(found.item ? { item: found.item } : {}),
  ...(found.item && found.culture !== undefined ? { culture: found.culture } : {}),
});

/** An element's own rule that the steps after it name, by its key. */
function ruleAt(rules: unknown, rest: readonly PathStep[]): string | null {
  const [first, index] = rest;
  if (first !== 'rules' || typeof index !== 'number' || !Array.isArray(rules)) return null;
  const key = (rules[index] as { key?: unknown } | undefined)?.key;
  return typeof key === 'string' ? key : null;
}

/**
 * Where Fix it leads for a problem at this path of the definition checked: the element it names, with its item, its
 * rule, or the field picker for a page without fields; After sending for an outcome; a view for the form's settings
 * and actions. Null where
 * the path names nothing the designer shows, or the whole form. The field types tell a text setting's language from a
 * nested setting's name.
 */
export function fixOf(definition: DesignDefinition, path: string, types?: CheckTypes): FixTarget | null {
  const steps = parsePath(path);
  if (!steps || steps.length === 0) return null;
  const [top, ...after] = steps;
  if (top === 'settings') return { kind: 'view', view: 'settings', path };
  // An outcome's or an action's Fix it carries its key, so it finds the one it is about wherever the list moved it.
  if (top === 'outcomes' || top === 'actions') {
    const list = definition[top];
    const [index] = after;
    const key =
      typeof index === 'number' && Array.isArray(list)
        ? (list[index] as { key?: unknown } | undefined)?.key
        : undefined;
    const known = typeof key === 'string' ? { key } : {};
    return top === 'outcomes' ? { kind: 'outcome', path, ...known } : { kind: 'view', view: 'actions', path, ...known };
  }
  if (top === 'validationRules') return validationFix(definition, after);
  if (top !== 'pages') return null;
  const [pageIndex, ...inPage] = after;
  // The form has no pages: a field added makes the first.
  if (pageIndex === undefined) return { kind: 'picker', page: definition.pages[0]?.key ?? null };
  if (typeof pageIndex !== 'number') return null;
  const page = definition.pages[pageIndex];
  if (!page) return null;
  const [pageStep, groupIndex, ...inGroup] = inPage;
  if (pageStep === 'groups') {
    // The page has no groups, or a group no fields: the field picker adds at the end of the page.
    if (groupIndex === undefined) return { kind: 'picker', page: page.key };
    const group = typeof groupIndex === 'number' ? page.groups[groupIndex] : undefined;
    if (!group) return null;
    const [groupStep] = inGroup;
    if (groupStep === 'fields') return fieldFix(page.key, group.fields, inGroup.slice(1), types);
    const rule = ruleAt(group.rules, inGroup);
    if (rule) return { kind: 'rule', key: group.key, rule };
    return onElement(group.key, containerItem(inGroup, true));
  }
  const rule = ruleAt(page.rules, inPage);
  if (rule) return { kind: 'rule', key: page.key, rule };
  return onElement(page.key, containerItem(inPage, false));
}

// A field at `fields[i]` of its group or its repeatable group, its children at `fields[j]` of it.
function fieldFix(
  page: string,
  fields: readonly DesignField[],
  steps: readonly PathStep[],
  types: CheckTypes | undefined,
): FixTarget | null {
  const [index, ...rest] = steps;
  // A group without fields: the field picker adds at the end of the page.
  if (index === undefined) return { kind: 'picker', page };
  const field = typeof index === 'number' ? fields[index] : undefined;
  if (!field) return null;
  if (rest[0] === 'fields') {
    // A repeatable group without fields: the field itself, where its fields are added.
    if (rest.length === 1) return { kind: 'element', key: field.key };
    return fieldFix(page, field.fields ?? [], rest.slice(1), types);
  }
  const rule = ruleAt(field.rules, rest);
  if (rule) return { kind: 'rule', key: field.key, rule };
  return onElement(field.key, fieldItem(field, rest, types));
}

// A validation rule opens in the builder on the field that shows its message; a grid's row's, on the grid.
function validationFix(definition: DesignDefinition, steps: readonly PathStep[]): FixTarget | null {
  const [index] = steps;
  const rule = typeof index === 'number' ? validationRulesOf(definition)[index] : undefined;
  if (!rule || typeof rule.key !== 'string') return null;
  const field = messageFieldOf(definition, rule.field);
  return field ? { kind: 'rule', key: field.key, rule: rule.key } : null;
}

/** The problems by their weight, each in the order the check found them. */
export function bySeverity(issues: readonly CheckIssue[]): Readonly<Record<CheckSeverity, readonly CheckIssue[]>> {
  return {
    error: issues.filter((issue) => issue.severity === 'error'),
    warning: issues.filter((issue) => issue.severity === 'warning'),
    hint: issues.filter((issue) => issue.severity === 'hint'),
  };
}

/** A severity as the API sends it, in any letter case; one this version doesn't know is shown as a warning. */
export function severityOf(text: string): CheckSeverity {
  const lower = text.toLowerCase();
  return lower === 'error' || lower === 'hint' ? lower : 'warning';
}

/** The problems as the management API sends them. */
export const issuesOf = (
  issues: readonly {
    readonly severity: string;
    readonly code: string;
    readonly path: string;
    readonly message: string;
  }[],
): CheckIssue[] =>
  issues.map((issue) => ({
    severity: severityOf(issue.severity),
    code: issue.code,
    path: issue.path,
    message: issue.message,
  }));
