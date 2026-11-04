# 0178. Seeing rules, and validation rules in the builder

- Status: accepted; builds on [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (rules on the
  elements they affect, form-level validation rules), [ADR-0005](0005-shared-logic-and-test-vectors.md) (one engine in
  both runtimes), [ADR-0170](0170-the-inspector.md) (the inspector's Rules section) and
  [ADR-0175](0175-the-rules-builder.md) (the rules builder, which split this step from 9.5a)
- Date: 2026-11-04
- Roadmap step: 9.5c

## Context

The rules builder of 9.5a builds a page's, group's or field's rule from the inspector. Nothing yet shows rules where
an editor looks at the form, nor all of them in one place, and form-level validation rules ("show this message on
field X when the rule matches", ADR-0002) have no screen at all, though both runtimes evaluate them and show their
message at the field. IA 5.1 and the Phase 2 wireframes give:

- chips on the canvas, on the elements rules affect and not on the selected one (`designer.html` draws one under a
  field's control);
- a rules overview, a sidebar from the canvas's "..." with every rule by page, each linking to its element, the
  outcomes' rules under After sending, a conflict (a hide rule and a require rule that hold for the same answer) warned
  on its row, Add rule that first asks what the rule affects, and one sentence when there are none
  (`rules-builder.html`);
- validation rules "in the same builder", with a field and a message in its first line (ADR-0175).

They leave open:

1. **Where a chip sits, what it says, and whether it is a control.** In the flow under the control, a chip goes as its
   field is selected, which moves every field below it; as a button, every element with a rule gains a second stop.
2. **What counts as a conflict,** and how the designer can know that two rules hold "for the same answer" without
   saying so when they don't.
3. **How Add rule asks what the rule affects,** and where the editor lands.
4. **How a validation rule fits the builder,** whose first line is an effect and an element.
5. **What the overview lists for actions,** whose rules run on the server and whose cards show their own (IA 5.3).

The owner answers no questions during the run (CLAUDE.md rule 8); where this record decides something the IA does not
say, it says so.

## Options

1. **Chips.** In the flow, as drawn; or out of the flow at the element's top end, beside the translation chip of
   ADR-0170. A chip that opens the builder; or a chip that describes the button that selects its element.
2. **Conflicts.** None in the designer, leaving them to the form check of 9.5d; a rule-text heuristic (the same option
   named in both rules); or the engine itself, given sets of answers, saying a conflict only where it finds both rules
   holding.
3. **Add rule.** Open the builder with no element, its first line asking; or ask first, inside the overview.
4. **Validation rules.** A second builder; a mode of the builder opened from the inspector's Validation section; or a
   fourth effect in the builder's first line, kept where the definition keeps validation rules.
5. **Actions.** List their rules too; or keep to the IA: the pages and After sending.

## Decision

1. **A chip per rule in use, out of the flow at the element's top end,** beside the translation chip, on pages, groups
   and fields, and none on the selected element, whose rules the inspector lists. Selecting an element moves nothing
   below it, which an in-flow chip would. A chip says its rule as the builder does ("Shown when Country is Other"),
   with the rule icon, shortened with an ellipsis where the element is narrow, and in full as the description of the
   button that selects its element, so a screen reader hears it after the element's name. It is no control: a click
   goes through to the element, whose inspector lists its rules, each a button that opens the builder; a second stop per
   element would double the keyboard path on a form of 300 fields (section 4.12). This leaves the wireframe's chip
   that opens the builder. A rule switched off has no chip, as it changes nothing for visitors; the inspector and the
   overview mark it "Off". Rule chips are a cool neutral, the translation chip's amber stays for a text that is missing.
2. **A conflict is what the engine finds.** For a field with a require rule and a hide rule (or a show rule, which
   hides the field while it does not hold), both in use, the overview tries answers through `evaluateRule` of
   `@jfb/logic` with answers a visitor can give, for each field and part of the visit the two rules look at: no answer,
   the values the rules name, a choice's own options where the form has them (and nothing else), one more and one less
   than a number, the days before and after a date (a day counted from today counted as the rule counts it), a minute
   either side of a time, where two answers are compared each other's values, a language of the site's (never none),
   and member groups only with the visitor signed in. For a text the rules only test for being equal (is, is one of,
   is empty, never contains, starts with or a pattern, nor compared with another answer), one text they don't name
   stands for every other, and is said as "is none of" those they do ("is not empty" where they name none); at most
   4,096 sets. The first set for which the field is hidden and required is the conflict, said on the field's row with
   the answers that matter ("Conflict: when Topic is Press, one rule hides it and the other requires it. A hidden field
   is never required, so visitors can send without it."; files as "Upload has 2 files"). Answers the conflict holds for
   whatever they are, taken together, go unsaid, each one left out staying free while the next is tried, so the words
   hold whatever the others are; "whatever the answers" only where all of them are free together. So a conflict said
   is always real; one that only answers no rule names would show (a pattern's) may go unsaid, and the form check of
   9.5d, which reasons about the whole form (a group's or a page's rule hiding a required field too), says the rest. A
   rule the engine refuses has no conflict: the form check names it. The overview works its conflicts out once, as it
   opens.
3. **Add rule asks inside the overview,** in a box at its top: "What should the rule affect?", a list of the form's
   pages, groups and fields by page (those the builder can give a new rule, so not a group that already decides
   whether it shows, as the inspector offers no Add a rule there), Continue and Cancel; Continue with nothing
   chosen says so at the list. The overview then closes, the element is selected on its page, and the builder opens on
   it for a new rule, with the first effect the element can take; Add lands the rule as from the inspector, with the
   focus on its line. A row leads to its element the same way, the focus on the button that selects it; an outcome's
   row opens Settings, where outcomes are edited. The overview's Add rule sits in its header, Close in its footer.
4. **A validation rule is a rule whose effect is "Show a message on".** The builder offers it after Show, Hide and
   Require for a field whose answer rules compare and the visitor gives: not a name, an address, a grid or a repeatable
   group, whose answers no rule compares, nor a value kept on the server or a field a member's value stands in for
   (ADR-0124). Both runtimes refuse a whole form with a validation rule on any of those, so publishing refuses it first
   (the structure check, `invalid_reference` for a value the visitor's page lacks, `unknown_field` for answers rules
   cannot compare). Its first line reads "Show a message on | this field:
   Email | when", the message's box under it in the language the designer shows (the default's text under an empty box
   in another), and a message that says nothing in any language is refused at its box, as an empty row is at its row.
   Its conditions may look at its own field ("Email ends with gmail.com"), as nothing a rule reads depends on a message.
   Try it says "The message shows on Email." or "The message doesn't show on Email.". The builder edits it as a rule
   with the effect `message` and a `message`; the Design view keeps it in the definition's `validationRules` with its
   field, and changing the effect to or from a message moves the rule between there and the element in one step of
   undo, everything else it held kept. A field shows any number of messages, so a message never clashes. The inspector
   lists a field's validation rules among its rules ("Shows "Use your work address" when Email ends with
   "gmail.com""), each opening the builder; the Rules section's help says a rule may also sit on the field it shows a
   message on. A rule written without a message (through the API) shows "the standard message" in words and opens
   saying what it lacks. A validation rule on a grid's row (through the API too) is listed, chipped and opened on its
   grid, which the canvas shows in the row's place, and stays on its row when kept. Deleting a field takes the
   validation rules that show their message on it or inside it along, as its own rules go, in the same step of undo; a
   validation rule that only looks at it stays, naming nothing, for the form check. A rule whose element can no longer
   have what it does (its type changed) opens with that effect still shown and the first line saying so, and is kept
   only once another is chosen, so the builder never changes a rule without saying it.
5. **The overview keeps to the IA:** the pages, then After sending. Actions' rules show on their cards in the Actions
   view (IA 5.3, Phase 10), where their rules are edited.

## Consequences

- One sentence of a rule reads the same on the canvas, in the inspector, in the overview and in the builder
  (`rule-words.ts`), now also for a validation rule and an outcome's rule ("Used when Topic is Press").
- `@jfb/logic` exports `applyRelative` for the overview's day counted from today; the website runtime does not use it,
  so its bundle keeps its size.
- The canvas works out every element's chips once per change of the definition, keeping an element's list where its
  words stay the same, so a field a change didn't touch keeps its state and is not drawn again (section 4.12).
- Publishing refuses a validation rule that would make either runtime refuse the whole form, which it accepted
  before; no form could have run with one, so none that worked is refused now.
- 9.5d's form check covers the conflicts the overview cannot see, and 9.5f opens the overview from the form check.
