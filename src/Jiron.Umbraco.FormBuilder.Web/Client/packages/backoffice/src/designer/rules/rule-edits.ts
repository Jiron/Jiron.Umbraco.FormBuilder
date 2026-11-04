// How a rule lands in the draft (ROADMAP 9.5a, ADR-0175): through the editor model's `change`, which refuses a rule
// that makes an element wait on itself, as one step to undo. A rule the first line moved leaves the element it was on
// and joins its new one in that same step.
import type { DesignRule } from '../definition';
import { change, type Edit, type EditorState, type ModelContext } from '../editor-model';
import { withRule } from './rule-draft';

/**
 * Puts a rule on its element, in place of the one with its key or at the end. `from` is the element it was on, which
 * loses it where it differs; null for a new rule. The selection stays as it was, as `change` leaves it.
 */
export function landRule(
  state: EditorState,
  from: string | null,
  target: string,
  rule: DesignRule,
  context: ModelContext,
): Edit {
  let now = state;
  if (from !== null && from !== target) {
    const left = change(
      now,
      from,
      (element) => ({ ...element, rules: withRule(element.rules, rule.key, null) }),
      context,
    );
    if (!left.ok) return left;
    now = left.state;
  }
  return change(now, target, (element) => ({ ...element, rules: withRule(element.rules, rule.key, rule) }), context);
}

/** Takes a rule off its element. */
export function dropRule(state: EditorState, holder: string, key: string, context: ModelContext): Edit {
  return change(state, holder, (element) => ({ ...element, rules: withRule(element.rules, key, null) }), context);
}
