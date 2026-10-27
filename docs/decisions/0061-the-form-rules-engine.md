# 0061. The form rules engine

- Status: accepted; builds on [ADR-0005](0005-shared-logic-and-test-vectors.md) (conditions, their check and the
  vectors), [ADR-0030](0030-porting-the-shared-logic.md) (where the logic lives) and
  [ADR-0035](0035-structural-validation-and-public-projection.md) (what publishing refuses); amended in 5.3a by
  [ADR-0063](0063-the-calculation-language.md) (calculations) and in 5.4d by
  [ADR-0072](0072-a-submissions-answers.md) (the rules run again once the server resolves a hidden field's value)
- Date: 2026-10-26
- Roadmap step: 5.2a (decided and built); 5.2b checks the values, 6.1 runs the engine in the pipeline, 6.4 in the
  visitor's browser, 9.x in the preview

## Context

Since 3.4b both runtimes check and evaluate one condition or one rule against answers and a context (ADR-0005,
ADR-0030). A form has many rules, and they depend on each other: section 4.3.3 says a hidden field is never required,
never validated and never stored, and a rule that looks at a hidden field sees it as empty. Whether a field is shown
can therefore depend on whether another field is shown, through its rules and through the group and the page that
hold it. Step 5.2a evaluates all of a form's rules at once, the same in C# and TypeScript. What needed deciding:

1. **What the engine reads.** Core holds a `FormDefinition`; the visitor's browser will hold the public projection
   (6.4), which carries no actions or outcomes. The vectors have to describe a form that both runtimes read alike.
2. **The order of evaluation,** and what happens to a form whose rules depend on themselves: publishing refuses it
   (`cycle`, ADR-0035), but a draft keeps it and the preview evaluates drafts.
3. **Repeatable groups.** A repeatable group's child fields have one answer per row. "Show the end date in a row whose
   kind is a range" needs the row's own answers.
4. **How deeply conditions nest.** Both evaluators recurse through nested groups of conditions. A definition read by
   Core nests at most 64 levels in all, but the browser and the headless element read JSON without such a limit.
5. **What the engine answers.**

## Options

1. **The input.**
   - Each runtime evaluates its own model, the definition in C# and the projection in TypeScript: two adapters inside
     the engines, and no vector could describe a form for both.
   - **A small logic model of the form,** the same in both runtimes: pages, their groups, their fields (a repeatable
     group's child fields, a grid's rows as subjects of conditions), each with its rules, and the form-level validation
     rules, actions and outcomes. Each runtime turns what it holds into it; the vectors write it directly. Chosen.
2. **The order.**
   - Evaluate every rule again until nothing changes: ends only without cycles, and costs one pass per level of
     dependency.
   - **One pass in dependency order:** every element after the element that holds it and after every field its
     switched-on show or hide rule looks at, the ready elements taken in document order, and a cycle refused as
     `cycle` before anything is evaluated. Chosen.
3. **Repeatable groups.**
   - Their child fields' answers taken together, all rows as one multi-answer field: a row could not depend on itself.
   - **Row by row:** a child field's rules, and a validation rule whose message a child field shows, run once per row,
     and there a sibling child field's answers are that row's. Everywhere else a child field's answers are those of
     every row in which it is shown, in row order. Chosen.
4. **Nesting.**
   - No limit in the engine: a visitor's browser could be made to recurse until its stack runs out.
   - **At most 10 levels,** the rule's own list of conditions being the first, checked with the rest before anything is
     evaluated (`too_deep`), so a group too deep fails in both runtimes alike; publishing refuses it through the same
     limit. A rules builder rarely needs more than three. Chosen.
5. **The answer.** A list of everything shown, or **what is hidden and what is required**, because the pipeline strips
   and requires by those, with the messages of the validation rules, the actions that run and the outcome that is
   used. Chosen.

## Decision

**The model** (`Logic/FormRules.cs` in Core, `packages/logic/src/form.ts`, internal in Core as ADR-0030 decided):

| Part | Holds |
| --- | --- |
| `LogicForm` | its pages, its form-level validation rules, its actions and its outcomes |
| `LogicPage` | its id, its rules and its groups |
| `LogicFormGroup` | its id, its rules and its fields (the conditions' own groups keep the name `LogicGroup`) |
| `LogicField` | its id, the kind its answers compare as (none for a field that holds no value), whether it is `required`, its rules, a repeatable group's child fields, the ids and kinds of a grid's rows, and since 5.3a its alias and, for a calculation, its formula and places (ADR-0063) |
| `LogicValidationRule` | its id, the field that shows its message, whether it is switched on, and its conditions |
| `LogicAction`, `LogicOutcome` | their id and their rules |

Core builds it from a definition with `DefinitionLogic.From(definition, registry)`: the ids are the keys in the `D`
format, a field's kind is its type's (ADR-0055), a switched-off action is left out because it never runs, and the same
conversion of conditions serves the publish checks. The
runtime of 6.4 builds it from the public projection, which has no actions or outcomes. The answers are the texts of
each field and grid row by its id, and for each repeatable group its rows, each holding its child fields' texts.

**The check,** of the whole form before anything is evaluated, so that the order of evaluation never changes an outcome:

- every condition as ADR-0005 checks it, against the form's subjects (the fields that hold a value, child fields and
  grid rows), with one compiled pattern per text for the whole form;
- groups of conditions nested deeper than 10 levels: `too_deep`;
- a rule whose effect the element does not take (pages and groups `show` and `hide`; fields `show`, `hide` and
  `require`, `require` only on a field that holds a value; actions `run`; outcomes `use`), a second rule of one effect,
  or both `show` and `hide`: `invalid_rule`;
- a validation rule whose message no field that holds a value can show: `unknown_field`;
- an id given twice: `invalid_form`;
- since 5.3a, every calculation's formula as ADR-0063 checks it, against the aliases of the fields that hold a value;
  an alias given twice, or a calculation on a field that does not hold a number, is `invalid_form`;
- an element whose being shown, or a calculation whose result, depends on itself: `cycle`.

**Evaluating.** Visibility first, in the dependency order above. An element is hidden when the element that holds it
is hidden, its switched-on `show` rule does not match, or its switched-on `hide` rule matches; a switched-off rule
counts as none. A hidden field's answers are gone: a condition sees it as empty, also through an operand, and a grid's
rows are gone with it. A repeatable group's child fields are decided row by row; a hidden repeatable group hides every
row's child fields. Then, over the final visibility:

- a shown field that holds a value is **required** when it is `required` itself or its switched-on `require` rule
  matches;
- a switched-on **validation rule** that matches puts its id among the messages of its field, when that field is shown;
- an **action** runs unless its switched-on `run` rule does not match;
- the **outcome** is the first in order without a switched-on `use` rule or whose rule matches, and none when no outcome
  qualifies (the pipeline then shows its default message).

**Calculations (5.3a, ADR-0063)** are worked out in the same order, after the fields their formulas name, over the
answers as conditions read them, and rounded to their places. A hidden calculation has no value, and rules, other
calculations and validation read the rounded result, never what was posted for the field.

**The answer,** `FormState`: the hidden ids in document order (pages, groups, and fields outside repeatable groups), the
required ids, the messages by field, each repeatable group's rows with their own hidden, required and messages, the
actions that run, and the outcome; since 5.3a also `calculations`, each shown calculation's result as canonical text
or null without a value, outside repeatable groups and in each row. A context's `hidden` list, which the single-condition vectors use, plays no part:
the engine decides what is hidden.

**Publishing** refuses a group of conditions nested deeper than the engine's limit at the group's path (`too_deep`),
with `Rules.MaxDepth` the one limit both use.

**The vectors** gain a runnable `form`: a case gives the form in the model's shape, its `values` by id and its `rows`
by repeatable group, and expects the `FormState` as JSON, or `{ "error": code }`. They live in `tests/vectors/rules.json`,
written from `suites/rules.mjs` like the others, and both runners run them.

**The mutation check** is `tests/vectors/mutants.mjs`, in the repository: it puts each mutant into the sources of
either runtime in turn, runs that runtime's vector runner, and puts the file back; a mutant that no vector catches is
listed, and the script fails unless it is written down as an equivalent change, with the reason. A mutant written down
as equivalent that a vector catches fails it too, so the notes stay true. A lone surrogate is the one thing no vector
can hold, because .NET's JSON reader refuses it; its mutants also run the runtime's own text test. Its `--check`
only checks that every mutant's code is still there, and the scripts' tests run it, so a change to the logic that
moves a mutant's code brings the script up to date in the same commit.

## Consequences

- The pipeline (6.1) strips and requires by `FormState`, and the visitor's browser (6.4) decides the same from the same
  model, so the two never disagree about a hidden field.
- The engine's errors are those of a draft: publishing has refused every one of them before a form goes live, so the
  website never meets one, and the preview shows them as issues.
- A rule in a repeatable group can look at its own row; a rule elsewhere looks at every shown row's answers, with "some
  answer" semantics for `equals` and "the first answer" for comparisons (ADR-0005).
- `uniquePerForm` needs other entries and is the pipeline's (6.1); a field's other checks are 5.2b's.
- **Revisit** the depth limit if editors' rules reach it, and the row semantics if forms need a rule that looks at one
  particular row of another repeatable group.
