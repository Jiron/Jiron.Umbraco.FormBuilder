// How a rule lands in the draft (ROADMAP 9.5a, 9.5c, ADR-0175, ADR-0178): a page's, group's or field's rule through the
// editor model's `change`, which refuses a rule that makes an element wait on itself; a form-level validation rule in
// the definition's own list, which no rule reads, so it never waits on anything. Each lands as one step to undo: a rule
// the first line moved, to another element or to a message, leaves where it was and joins its new place in that step.
import { locate, validationRulesOf, type DesignRule, type DesignValidationRule } from '../definition';
import { change, type Edit, type EditorState, type ModelContext } from '../editor-model';
import { without } from '../inspector/setting-values';
import { withRule } from './rule-draft';

/**
 * Where a rule the builder opened is kept: on a page, group or field, or among the form's validation rules, with the
 * field or grid row it shows its message on.
 */
export type RuleHolder =
  { readonly kind: 'element'; readonly key: string } | { readonly kind: 'validation'; readonly field: string };

/**
 * A validation rule as the builder edits it: a rule whose effect is `message`, on the element it shows its message on.
 * Everything else it holds is kept as it came.
 */
export function asBuilderRule(rule: DesignValidationRule): DesignRule {
  return { ...without(rule, 'field'), key: rule.key, effect: 'message', conditions: rule.conditions };
}

/** A rule the builder gives with the effect `message` as the validation rule the definition keeps, on its field. */
export function asValidationRule(field: string, rule: DesignRule): DesignValidationRule {
  return { ...without(rule, 'effect'), key: rule.key, field, conditions: rule.conditions };
}

/** A definition with its validation rules changed; a list left empty goes, as the server's canonical form leaves it out. */
function withValidation(
  state: EditorState,
  update: (rules: readonly DesignValidationRule[]) => DesignValidationRule[],
): Edit {
  const before = validationRulesOf(state.definition);
  const after = update(before);
  if (JSON.stringify(after) === JSON.stringify(before)) return { ok: false, refusal: 'unchanged' };
  const rest = { ...without(state.definition, 'validationRules'), pages: state.definition.pages };
  const definition = after.length > 0 ? { ...rest, validationRules: after } : rest;
  return { ok: true, state: { definition, selection: state.selection } };
}

/** The validation rules with one put in place of the one with its key, or at the end; or taken out with null. */
function withValidationRule(
  rules: readonly DesignValidationRule[],
  key: string,
  rule: DesignValidationRule | null,
): DesignValidationRule[] {
  const at = rules.findIndex((each) => each.key === key);
  if (rule === null) return rules.filter((_, index) => index !== at);
  if (at < 0) return [...rules, rule];
  return rules.map((each, index) => (index === at ? rule : each));
}

/** Takes a rule from where it is kept. */
export function dropBuilt(state: EditorState, holder: RuleHolder, key: string, context: ModelContext): Edit {
  return holder.kind === 'validation'
    ? withValidation(state, (rules) => withValidationRule(rules, key, null))
    : change(state, holder.key, (element) => ({ ...element, rules: withRule(element.rules, key, null) }), context);
}

/**
 * The field a validation rule shows its message on once it lands on `target`: the grid row it was on where `target` is
 * that row's grid, which the canvas shows in its place; else `target`.
 */
function messageField(state: EditorState, from: RuleHolder | null, target: string): string {
  if (from?.kind !== 'validation' || from.field === target) return target;
  const found = locate(state.definition, target);
  const rows = found?.kind === 'field' ? (found.element.rows ?? []) : [];
  return rows.some((row) => row.key === from.field) ? from.field : target;
}

/**
 * Puts a rule the builder gives where it belongs: one whose effect is `message` among the validation rules, on the
 * field `target` (or the row of it it was on); any other on the element `target`, in place of the one with its key or
 * at the end. `from` is where it was kept, which loses it where that differs; null for a new rule. The selection stays
 * as it was, as `change` leaves it.
 */
export function landBuilt(
  state: EditorState,
  from: RuleHolder | null,
  target: string,
  rule: DesignRule,
  context: ModelContext,
): Edit {
  const validation = rule.effect === 'message';
  const stays =
    from === null || (validation ? from.kind === 'validation' : from.kind === 'element' && from.key === target);
  let now = state;
  if (!stays && from !== null) {
    const left = dropBuilt(now, from, rule.key, context);
    if (!left.ok) return left;
    now = left.state;
  }
  if (validation) {
    const field = messageField(state, from, target);
    return withValidation(now, (rules) => withValidationRule(rules, rule.key, asValidationRule(field, rule)));
  }
  return change(now, target, (element) => ({ ...element, rules: withRule(element.rules, rule.key, rule) }), context);
}

/** Puts a rule on its element, in place of the one with its key or at the end; `from` is the element it was on. */
export function landRule(
  state: EditorState,
  from: string | null,
  target: string,
  rule: DesignRule,
  context: ModelContext,
): Edit {
  return landBuilt(state, from === null ? null : { kind: 'element', key: from }, target, rule, context);
}

/** Takes a rule off its element. */
export function dropRule(state: EditorState, holder: string, key: string, context: ModelContext): Edit {
  return dropBuilt(state, { kind: 'element', key: holder }, key, context);
}
