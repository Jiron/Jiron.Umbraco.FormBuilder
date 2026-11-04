# 0179. The form check's checks of the form's rules

- Status: accepted; builds on [ADR-0005](0005-shared-logic-and-test-vectors.md) (one engine in both runtimes, shared
  vectors), [ADR-0174](0174-changing-a-fields-type-in-the-designer.md) (conditions a change of type leaves),
  [ADR-0177](0177-the-form-check-on-the-server.md) (the form check) and
  [ADR-0178](0178-seeing-rules-and-validation-rules-in-the-builder.md) (the rules overview's search for conflicts)
- Date: 2026-11-04
- Roadmap step: 9.5d (split in two, see WORKLOG; 9.5e checks the site and the promised ones, 9.5f shows the check in
  the designer)

## Context

The form check of 9.5b weighs what the field types, the settings and the merge tags already know. 9.5d adds what only
reasoning about the form's rules finds: a required field that a rule can hide, a page no visitor reaches, and the
conditions a change of type left (ADR-0174 promised them to the form check). The rules overview of 9.5c already finds
one such thing in the browser: a field whose own hide (or show) rule and require rule hold for the same answers, found
by the engine itself over sets of answers a visitor can give (ADR-0178). The server had no way to ask whether rules can
hold, and D10 wants the two runtimes never to disagree.

Three things are open:

1. **Which required fields are a problem.** A field required of itself and shown by a rule is required only while it
   shows. ROADMAP §1.1 names "a required field that a rule can hide" among the problems "before visitors find them".
2. **What "unreachable" means,** and how a check can say it without being wrong: the search tries a handful of answers,
   not all of them.
3. **Where the search lives,** so that the overview and the form check say the same.

## Options

1. **Required fields.** Warn about every required field with a show or hide rule on it or around it; or only where a
   require rule, which the editor wrote for some answers, holds while a rule hides the field.
2. **Unreachable pages.** A page whose show rule never holds for the answers tried; or one whose rules look only at
   later pages; or one that shows for none of the answers a visitor can have given when reaching it, said only where the
   answers tried stand for all.
3. **The search.** Keep it in the designer and write a second one in C#; or move it into `@jfb/logic` beside the
   engine, port it to Core, and hold both to shared vectors.

## Decision

1. **A require rule that holds while a rule hides its field** (`required_can_hide`, a warning, at the require rule's
   path): its field's own show or hide rule, its repeatable group's, its group's or its page's, the nearest first. The
   rule search finds a set of answers for which the require rule holds and one of them hides the field, and the
   sentence names whose rule it is. A field required of itself while shown is ordinary design and no issue: a warning
   on every conditional question would bury the real ones (§4.13).
2. **A page no visitor reaches** (`page_unreachable`, a warning, at its first show or hide rule): one whose rules let it
   show for none of the answers a visitor can have given when it is reached going forward, the fields of the earlier
   pages and the visit tried, those of the page itself and of the later pages not yet answered, but for those a default
   value, a prefill or a calculation answers before the visitor does (they are tried as any other answer). It is said
   only where the answers tried stand for every answer a visitor gives (below), so a page said to be unreachable is;
   otherwise the check says nothing. The sentence says "a visitor going forward never reaches it": the Back button
   may still lead to it after a later answer, which is no way through a form. A page shown by a field on itself is a
   cycle the structural validation already refuses.
3. **Conditions a change of type left** stay the structural validation's error (`operator_not_for_kind`), which the
   form check lists and publishing refuses; 9.5d proves the refusal through the API.
4. **One search, two runtimes.** The rules overview's search moves from the designer into `@jfb/logic` (`search.ts`,
   exported beside the engine; the website runtime leaves it out of its bundle) and is ported to Core
   (`Logic/RuleSearch.cs`). Both pass `tests/vectors/search.json` (D10): conflicts by their answers and hiding rule, and
   whether an element shows, can never show or can't be told. The search grows two things:
   - **several hiding rules** (`conflictOf(requiring, hidings, scope)`): the field is hidden when any of them hides it.
     The overview still passes the field's own rules, the form check its group's and page's too;
   - **whether an element ever shows** (`showsAtAll(rules, scope, unanswered)`), which answers `never` only where every
     subject's answers tried stand for all: ticks and the signing in; the site's languages; numbers of files; whole
     days not compared with another answer, between whose neighbours lies no other day (not times, which may hold
     seconds); a choice whose options the form knows; a text or a number only tested for being equal, one answer tried
     being none the rules name and standing for the others; and several choices where at most two named options
     matter and a count tells at most two ticked from fewer ("at least" two) or one from more ("at most" one). A rule
     the engine refuses, days counted from today, a trimmed search (4,096 sets, or more than 32 sets of one choice of
     several), a repeatable group's child read from outside it (it holds every row's answers), a query value (a link
     may give it twice) or any other subject make it `unknown`. No day before today is tried: no visit is on one.
5. **What the form hides counts as no answer.** A conflict weighs each set of answers as the form sees it: the scope's
   `hidden` hook (`formSight` in TypeScript, `RuleSearch.FormSight` in C#) asks the form engine itself
   (`evaluateForm`, `FormRules.Evaluate`) which fields, grid rows (with their grid) and repeatable groups' children
   (given in one row, as a visitor with one row gives them) it hides for them, and their answers count as none. A
   require rule and a hiding rule that hold together only for an answer the form never takes are no conflict. Where the
   engine refuses the form, or a form with calculations is given no calculation language, the hook can't tell and no
   conflict is said. Whether an element ever shows needs no hook: no answer is always tried, so the answers a hidden
   field leaves are among those tried. The designer builds the engine's model with the rules model's facts
   (`logicOf` with `factsOf`: the kinds the builder offers, which fields without a kind can be required), so the
   overview and the form check weigh the same form.
6. **What the server tries** (`RuleSearchScopes`): the kinds of the fields and grid rows as the rules see them, each
   choice's own options (none for a library list or a data source, whose options the check does not resolve, for a
   cascading list, whose options hang on its parent's answer, or for a choice whose "Other" takes any text: such a
   choice is tried with the values the rules name and never counts as complete; the designer's `ownOptionValues` knows
   none for the same choices), the site's languages, today in the site's time zone (UTC where the zone is unknown), the
   form's sight and its repeatable groups' children. The numbers tried step by one from plain decimals below 10^15, and
   a neighbour below a ten-thousandth (other than 0) is not tried, as C# writes it with an exponent and JavaScript
   doesn't; a count is read only from a plain decimal; texts are compared folded, as the engine compares them.

## Consequences

- The rules overview and the form check find the same conflicts for a field's own rules, and the form check also those
  of the rules around it.
- Fewer answers known means fewer findings, never wrong ones: a form with library option lists, cascading lists or
  "Other" has its conflicts said where the values its rules name find them, and its pages said unreachable only where
  the rest stands for all; a form the engine refuses has no conflict said until it is mended, which its errors ask for.
- Each set of answers a conflict search tries runs the whole form's engine once; the search tries at most 4,096 sets
  per require rule, and the overview finds its conflicts once while it is open.
- The designer says these codes in 9.5f, with a "fix it" that opens the rules builder on the rule.
- Codes added: `required_can_hide`, `page_unreachable` (warnings).
