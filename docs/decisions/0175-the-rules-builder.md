# 0175. The rules builder

- Status: accepted; builds on [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (rules on the
  elements they affect), [ADR-0005](0005-shared-logic-and-test-vectors.md) (operators, operands and how
  values compare), [ADR-0061](0061-the-form-rules-engine.md) (groups ten levels deep), [ADR-0167](0167-the-form-workspace-and-the-editor-model.md)
  (the editor model) and [ADR-0170](0170-the-inspector.md) (the inspector's Rules section)
- Date: 2026-11-04
- Roadmap step: 9.5a (9.5c builds the chips, the rules overview and the form-level validation rules on it)

## Context

A rule shows, hides or requires a page, group or field while its conditions match (section 4.3.3). Each condition looks
at a field's answers or at the visit (the language, the member, an allowed query value, today's date), compares by one
of eighteen operators, and may compare with a text, another field's answers or a date relative to today; conditions
nest in "all" and "any" groups ten levels deep. Both runtimes check a condition before they evaluate it, and publishing
refuses one that fails the check. Until now the designer could only count an element's rules (ADR-0170).

IA 5.1 and the Phase 2 wireframe (`docs/ux/wireframes/rules-builder.html`) give the builder's frame: a sidebar whose
first line names what the rule does and to what ("Show **this field: Which country?** when"), plain-language rows, "all"
and "any" groups that appear only once a second one is added, the switch **Use this rule**, and Delete rule, Cancel
and Update (Add for a new rule). They left open:

1. **The step's size.** 9.5a also lists chips on the canvas, the rules overview and form-level validation rules.
2. **What a condition may look at,** and which operators each offers. The engine takes every operator for some kinds
   that never need it (`countAtLeast` on a short text, `equals "true"` on a checkbox).
3. **How a value is given:** an option, a number, a date or a date relative to today, another answer, a member group.
4. **When Update is refused,** and what happens to a rule already broken (a type change keeps conditions whose
   operator the new type doesn't take, ADR-0174).
5. **The live rule preview** that the roadmap names (D10), which neither the IA nor the wireframe draws.
6. **How the change lands:** one step of undo, a target changed in the first line, a rule that makes an element wait
   on itself.

The owner answers no questions during the run (CLAUDE.md rule 8). The design skills' discovery round was answered from
the roadmap's personas and IA 5.1; where this record decides something the IA does not say, it says so.

## Options

1. **Size.** One step (a builder, a sidebar overview, chips and a second kind of rule in one review, one tamper round
   and one gate); or the builder first, opened from the inspector, then what shows rules and what builds on the builder.
2. **Operators.** Every operator the engine takes for the kind; or the ones a sentence about that kind means, with any
   other operator a draft already holds still shown and kept.
3. **Values.** One box of text for every value, typed in the wire form (`2026-12-01`, `+2d`); or a control per kind, with
   the source of the value (a value, today, another answer) chosen first where there is more than one.
4. **Refusals.** Keep whatever is given, as a draft does, and leave it to the form check; or refuse Update while a
   condition is incomplete or fails the shared check, saying why at its row.
5. **Preview.** None in the builder (the full preview in Phase 10 runs the rules); or a "Try it" in the builder, as the
   pattern editor has (ADR-0173): answers for what the rule looks at, and what the element then does.
6. **Landing.** Write the rule when the sidebar closes, through the editor model's `change`, as one step.

## Decision

1. **The step is split.** 9.5a is the builder, opened from the inspector's Rules section, with every condition, groups,
   the switch, Delete with Undo and the live preview; its Done-when stays as written. **9.5c** (after the form check,
   9.5b) adds the chips on the canvas, the rules overview (from "...", with its conflicts and Add rule) and form-level
   validation rules in the same builder, with a Done-when of its own. Nothing of 9.5a's text is dropped; the builder's
   first line is written for a validation rule's "on **field**, show **message**" too.
2. **Where it starts.** The inspector's Rules section lists the element's rules as sentences ("Shown when Country is
   Other"), each a button that opens it, and ends with **Add a rule**; its closed line holds "Always shown" or the first
   rule's sentence with "Add a rule" beside the header (IA 5.1). The builder is a medium sidebar, the whole width at
   620 px.
3. **The first line** is the effect and the element: Show, Hide or Require (a field whose type can be required), then
   the element, "this field: Which country?" first and the form's pages, groups and fields after it by page. Changing
   the element moves the rule there; when the rule is still empty, its first condition then looks at the element it was
   started from, so a rule begun from the question it depends on lands on the right element (IA 5.1). An effect the
   element has already, or show beside hide, is refused at the line, with the rule that has it named.
4. **What a condition looks at:** every field of the form whose type has a rule kind, by page in the order of the form
   (a choice with `multiple` on as several choices, each of a grid's rows as one or several, as the server reads them),
   then **About the visit:** the language (on a site with more than one; a rule that looks at it stays readable on a site
   of one), whether the visitor is a signed-in member, the visitor's member groups, each query value the form allows
   (Settings, Developer), and today's date. The element itself and, for a show or hide rule, the fields inside it are
   left out: their rule would wait on itself. So is what the visitor's page has no answer of, which publishing refuses
   in a page's, group's or field's rule: a value kept on the server (the hidden field) and a field a member's value
   stands in for (ADR-0124). Phase 10's actions, whose rules run on the server, will offer those.
5. **Operators** are the engine's (`operatorsFor` in `@jfb/logic`, the table its check uses), narrowed to what a sentence
   about the kind means: text has is, is not, contains, doesn't contain, starts with, ends with, is one of, is none of,
   matches a pattern and the empty pair; a choice is, is not, is one of, is none of and the empty pair; several choices
   have ticked, hasn't ticked, is one of, is none of, has at least, has at most and the empty pair; a checkbox is ticked
   or not; files have at least, at most and the empty pair; numbers, dates and times is, is not, more than (after),
   less than (before), between and the empty pair. All eighteen are offered somewhere. An operator a draft holds that
   the narrowed list lacks is shown and kept. Words come from `jfbRules_op_*`, with variants for dates and times ("is
   after"), several choices ("has ticked") and the visit ("is signed in", "include").
6. **Values** have a control per kind: an option from the field's options as the canvas resolves them (any source),
   several by ticking for "is one of"; a number box; the browser's date, time and date-and-time boxes; whole numbers for
   counts; a pattern with the site's check under it; a language from the site's; a member group from Umbraco's list (or
   typed, for a user who can't read the list). Where a value can come from elsewhere, a select before it says from
   where: **a value**; for dates also **a day counted from today** (a number, days, weeks, months or years, before or
   after); and **the answer to** each other field of the same kind, which is how two fields are compared ("is after the
   answer to Arrival"). An operand the controls can't show (a relative date of several steps) is shown as written and
   kept.
7. **Refusals at the row.** Update (or Add) checks every condition with the engine's check, `checkGroup`, condition by
   condition. A row without its question, operator or value, or one the check refuses, says why under itself ("Choose a
   value", "These answers can't be compared this way"), on the control it is about (the question, the operator, what it
   compares with, or the value), the first such control takes the focus, and nothing is written. A rule that is broken
   already opens with its rows saying so; Cancel leaves it as it was. What the builder's controls can't show is kept: an
   operator the narrowed list lacks, an option since removed, another answer in a list; a value where the operator takes
   none (written by hand or through the API) is said and can be taken away.
8. **Condition groups.** A rule's conditions are a list in which each item is a condition or a condition group (never
   just "group", which is a form's group of fields). The list's "[all of these are true]" or "[any of these is true]"
   shows once it holds two items, and "and" or "or" joins the items. **Add condition group** turns the rule's conditions
   into its first condition group and starts a second; inside one it starts a nested one, to the tenth level, after
   which it is not offered. Removing a condition group's last condition removes the group.
9. **Try it** (the live rule preview) opens under the conditions: a box for each field and each part of the visit the
   rule looks at, then one sentence, said as it changes: "Which country? shows.", "...stays hidden.", "...is required.",
   "...is not required." The answer comes from `evaluateRule` in `@jfb/logic` through `tryRule`, the builder's one
   adapter from its boxes to the engine; a test runs every condition and rule case of `tests/vectors/rules.json` through
   that adapter, and again as the builder writes the rule and as its boxes give the answers, and expects the vectors'
   answers and error codes (D10). While a condition is incomplete it says so; while the rule is off, what the element
   then does (it shows, it is not required), the switch's help saying why.
10. **Landing.** Update writes the rule through the editor model's `change`, which refuses a rule that makes an element
    wait on itself (said at the first line, nothing written); a moved rule leaves the old element and joins the new one
    in the same step of undo. Delete rule acts at once and offers Undo in the toast (IA 5.1); the focus goes back to the
    rule's line in the inspector, or to Add a rule when it was the last. The sidebar answers before it slides out, and
    its dialog, as it closes, gives the focus back to what opened it, so the focus is set once the builder is gone.

## Consequences

- The designer edits rules with the same table of operators and the same check both runtimes use, so the builder can't
  write a condition publishing would refuse, and its preview can't disagree with the website about a rule.
- `@jfb/logic` exports `operatorsFor` and its operand shapes; the website runtime doesn't use them, so its bundle keeps
  its size.
- 9.5b's form check names a broken rule and its Fix it opens the builder on it; 9.5c's overview and chips say rules with
  the builder's sentences, and its validation rules use the builder with a message and a field in the first line.
- The member groups list is Umbraco's; a user without the right to read it types a group's name, as rules compare
  names.
