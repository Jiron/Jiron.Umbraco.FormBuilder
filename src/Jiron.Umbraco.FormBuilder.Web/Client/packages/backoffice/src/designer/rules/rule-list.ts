// The form's rules, element by element (ROADMAP 9.5c, IA 5.1, ADR-0178): what the rules overview lists by page, what
// the chips on the canvas say, and where the builder finds a rule it is asked to open. A field's rules are its own and
// the validation rules that show their message on it, said as the builder says them (a message rule). Pure.
import {
  allFields,
  locate,
  validationRulesOf,
  type DesignDefinition,
  type DesignPage,
  type DesignRule,
  type DesignValidationRule,
  type ElementKind,
  type LocalizedText,
} from '../definition';
import { textIn } from '../preview/field-models';
import { asBuilderRule, type RuleHolder } from './rule-edits';
import { ruleWords, type RuleWordsContext } from './rule-words';

/** A rule with where it is kept. */
export interface HeldRule {
  readonly holder: RuleHolder;
  readonly rule: DesignRule;
}

/**
 * An element's rules, its own first, then the validation rules that show their message on it or, for a grid, on one
 * of its rows, which the canvas shows in the grid's place.
 */
export function rulesOn(definition: DesignDefinition, key: string): HeldRule[] {
  const found = locate(definition, key);
  if (!found) return [];
  const own = (found.element.rules ?? []).map((rule) => ({ holder: { kind: 'element', key } as const, rule }));
  if (found.kind !== 'field') return own;
  const rows = new Set((found.element.rows ?? []).map((row) => row.key));
  const messages = validationRulesOf(definition)
    .filter((rule) => rule.field === key || rows.has(rule.field))
    .map(held);
  return [...own, ...messages];
}

/** A validation rule as the builder edits it, with where it is kept. */
const held = (rule: DesignValidationRule): HeldRule => ({
  holder: { kind: 'validation', field: rule.field },
  rule: asBuilderRule(rule),
});

/** An element that has rules, with them. */
export interface RuledElement {
  readonly key: string;
  readonly kind: ElementKind;
  readonly rules: readonly HeldRule[];
}

/** A page and the elements on it that have rules, in the form's order: the page, then its groups and their fields. */
export interface RuledPage {
  readonly page: DesignPage;
  readonly index: number;
  readonly elements: readonly RuledElement[];
}

/**
 * Every page of the form with the elements on it that have rules; a page with none is left out. It reads the form
 * once, as the canvas asks it on every change of a form of 300 fields (section 4.12).
 */
export function rulesByPage(definition: DesignDefinition): RuledPage[] {
  // A grid row's validation rules are listed on its grid.
  const grids = new Map<string, string>();
  for (const field of allFields(definition)) for (const row of field.rows ?? []) grids.set(row.key, field.key);
  const messages = new Map<string, HeldRule[]>();
  for (const rule of validationRulesOf(definition)) {
    const on = grids.get(rule.field) ?? rule.field;
    const list = messages.get(on) ?? [];
    list.push(held(rule));
    messages.set(on, list);
  }
  return definition.pages.flatMap((page, index) => {
    const elements: RuledElement[] = [];
    const add = (key: string, kind: ElementKind, own: readonly DesignRule[] | null | undefined) => {
      const rules: HeldRule[] = [
        ...(own ?? []).map((rule) => ({ holder: { kind: 'element', key } as const, rule })),
        ...(kind === 'field' ? (messages.get(key) ?? []) : []),
      ];
      if (rules.length > 0) elements.push({ key, kind, rules });
    };
    add(page.key, 'page', page.rules);
    for (const group of page.groups) {
      add(group.key, 'group', group.rules);
      for (const field of allFields({ pages: [{ key: '', groups: [group] }] })) add(field.key, 'field', field.rules);
    }
    return elements.length > 0 ? [{ page, index, elements }] : [];
  });
}

/** An outcome of the form that has a rule, by its place. */
export interface RuledOutcome {
  readonly index: number;
  readonly kind: string;
  readonly message: string;
  readonly url: string;
  readonly toPage: boolean;
  readonly rules: readonly DesignRule[];
}

/** The outcomes that have rules, with what they do: a message's text, or where a redirect goes. */
export function ruledOutcomes(definition: DesignDefinition, culture: string): RuledOutcome[] {
  const outcomes = Array.isArray(definition['outcomes'])
    ? (definition['outcomes'] as readonly Readonly<Record<string, unknown>>[])
    : [];
  return outcomes.flatMap((outcome, index) => {
    const rules = Array.isArray(outcome['rules'])
      ? (outcome['rules'] as readonly DesignRule[]).map((rule) =>
          rule.conditions ? rule : { ...rule, conditions: [] },
        )
      : [];
    if (rules.length === 0) return [];
    const redirect = (outcome['redirect'] ?? {}) as { url?: unknown; content?: unknown };
    return [
      {
        index,
        kind: outcome['kind'] === 'redirect' ? 'redirect' : 'message',
        message: plainText(textIn(outcome['message'] as LocalizedText | undefined, culture)),
        url: typeof redirect.url === 'string' ? redirect.url : '',
        toPage: typeof redirect.content === 'string' && redirect.content !== '',
        rules,
      },
    ];
  });
}

/** An outcome's message as one line of plain text: its markup and line breaks gone. */
export function plainText(html: string): string {
  return html
    .replace(/<[^>]*>/g, ' ')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * The values of a choice's own options, or of a grid's for one of its rows, which are all a visitor can answer it
 * with; none where they come from elsewhere (a library list, a data source), hang on another field's answer (a
 * cascading list), or where "Other" lets the visitor type an answer of their own. The form check reads the same.
 */
export function ownOptionValues(definition: DesignDefinition, key: string): string[] {
  const field = allFields(definition).find((one) => one.key === key || (one.rows ?? []).some((row) => row.key === key));
  const options = field?.['options'] as
    { source?: unknown; parent?: unknown; items?: readonly { value?: unknown }[] } | null | undefined;
  const other = (field?.['settings'] as { other?: unknown } | null | undefined)?.other === true;
  if ((options?.source ?? 'items') !== 'items' || options?.parent != null || other) return [];
  return (options?.items ?? []).flatMap((item) =>
    typeof item.value === 'string' && item.value !== '' ? [item.value] : [],
  );
}

/**
 * The chips of the canvas, by the key of the element they are on: each rule in use in words. A rule switched off
 * changes nothing on the website, so it has no chip; the inspector and the overview say it is off.
 */
export function chipsOf(definition: DesignDefinition, words: RuleWordsContext): Map<string, string[]> {
  const chips = new Map<string, string[]>();
  for (const { elements } of rulesByPage(definition))
    for (const element of elements) {
      const said = element.rules.filter(({ rule }) => rule.enabled !== false).map(({ rule }) => ruleWords(words, rule));
      if (said.length > 0) chips.set(element.key, said);
    }
  return chips;
}
