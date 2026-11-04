# 0181. The form check in the designer

- Status: accepted; builds on [ADR-0177](0177-the-form-check-on-the-server.md) (the check on the server, its issues
  and their paths), [ADR-0179](0179-the-form-checks-of-the-forms-rules.md) and
  [ADR-0180](0180-the-form-checks-of-the-site-and-of-what-a-form-collects.md) (the checks it runs),
  [ADR-0170](0170-the-inspector.md) (the inspector), [ADR-0175](0175-the-rules-builder.md) (the rules builder) and
  [ADR-0178](0178-seeing-rules-and-validation-rules-in-the-builder.md) (the rules overview)
- Date: 2026-11-04
- Roadmap step: 9.5f

## Context

The server checks a draft without publishing it and returns its problems, each with a weight (error, warning, hint),
a code, the JSON path of its place in the definition sent and an English sentence (ADR-0177). Publishing refuses the
errors. Nothing in the designer shows them yet. IA 5.1 and 5.7 and the Phase 2 wireframes (`form-check.html`,
`publish-dialog.html`) give:

- a badge in the canvas's toolbar, "2 problems" or "No problems", that opens a sidebar listing errors first, then
  warnings, each with **Fix it**, which selects the element and opens the setting; errors block publishing;
- a new blank form counts one error, "Add at least one field", whose Fix it opens the palette for the end of the page,
  so an empty form never reads "No problems";
- the publish dialog shows the check's result: errors block, each with Fix it; warnings are listed too, since the
  badge counts both.

They leave open:

1. **What the badge counts,** and where hints go.
2. **When the designer asks,** and what it shows while an answer is awaited.
3. **How a path becomes a Fix it,** and where each leads, while the Settings and Actions views are still to come
   (10.1, 10.2).
4. **In which language a problem is said:** the server's sentences are English, the backoffice is English and German.
5. **The empty form:** no check of 9.5b to 9.5e finds a form without fields.
6. **The publish dialog:** what blocks, while the answer is awaited and when none comes.

The owner answers no questions during the run (CLAUDE.md rule 8); where this record decides something the IA does not
say, it says so.

## Options

1. **The badge** counts every problem; or errors and warnings, hints listed but not counted; or errors alone.
2. **Asking:** on Publish only; after every change; or after the changes pause, the last ask's answer winning.
3. **Fix it** finds its place by the path at the moment it is pressed; or the path is read against the definition that
   was checked, giving an element's key, which later changes do not move.
4. **Language:** the server's sentence everywhere; or the designer's own sentence per code in English and German, with
   what it names read from the form at the path, and the server's where only it has the words.
5. **The empty form:** a designer rule outside the check; or a check on the server, an error as the IA says, which
   publishing then refuses.
6. **The publish dialog** lets Publish through and leaves the refusal to the server; or waits for the answer and keeps
   Publish off while an error needs fixing.

## Decision

1. **The badge counts errors and warnings: "1 problem", "2 problems", "No problems".** Hints are suggestions an
   editor may rightly decline (a consent field on a form whose basis is not consent, a reply-to field), so the sidebar
   lists them in a third box, "1 suggestion", and the badge does not count them: a form with only suggestions reads "No
   problems", as the IA's two states say. Before the first answer it says "Checking…", after a check that gave none
   "Not checked". Its icon is the alert in the danger colour while an error is found, in the warning colour for
   warnings alone, and a check in the positive colour for none, the text in the default colour, so the colour is never
   the only sign. It sits at the toolbar's end, before its "...". It changes after every pause in typing, so it does so
   quietly (emil-design-eng): no motion, no live region; the sidebar and the publish dialog say the result.
2. **The designer asks once the changes pause.** The form read is checked at once; after a change, 600 ms after the
   last; an answer awaited when the form changes again is dropped, only the last ask's answer is shown, and the last
   answer stays shown meanwhile, so the badge does not flicker while an editor types. The same form again, as a save
   gives it back, is not asked again unless the last check gave no answer. Opening the sidebar and Publish ask at once.
   Only a user who may design the form is checked, as only they may (ADR-0177). The workspace holds the check
   (`FormChecker`), so the badge, the sidebar and the publish dialog show the same answer.
3. **A path is read against the definition that was checked,** and Fix it keeps the key of the element it names, so it
   finds the element after later changes moved it, and leads nowhere once the element is gone:
   - a field, group or page with the inspector on the item the path names: its label, help, placeholder or title, a
     switch, the autocomplete or class, its alias, default value, prefill, keep, options or rows, a message, a check of
     its validation list by its kind (a pattern, each answer once, matching another field), a type's setting by its
     name (a nested setting's own box before its parent's). The inspector opens the item's section, and its More
     options where the item is among them, and moves the focus to the item's first box; to the section's header where
     no box takes the focus (a locked setting shows its value read-only), and to its heading where the element shows
     no such item. A repeatable group's child is found inside it;
   - a rule's place opens the rules builder on that rule, a validation rule on the field that shows its message (on
     its grid for a row's), with the element selected on its page. ADR-0178 expected the form check to open the rules
     overview; the builder is one step shorter, the overview stays in the canvas's "...";
   - a page or group without fields, a page without groups and a form without pages lead to the palette for the end of
     the page, the selection let go, as the page's Add field does (a page is added for a form without one);
   - the form's settings and outcomes lead to the Settings view, its actions to the Actions view. Both show their
     coming screen until 10.1 and 10.2 build them; those steps make the Fix it land on the setting (a roadmap change);
   - the whole form (`$`) and a path the designer does not show have no Fix it.

   A Fix it chosen in the publish dialog closes it and shows the Design view, which makes it once its canvas is read.
   A Fix it ends the wait for a link's field (ADR-0166), so the focus it sets stays.
4. **The designer says each problem in the user's language,** English and German: a sentence per code, with what it
   names read from the form at the path: the text a merge tag is in, by the inspector's name for it ("A merge tag in
   "Label" names nothing, so it shows nothing."), a type's setting by its label, the theme, a member group's name (one
   named by its key is "no such member group"), an alias, a dictionary key, a page's number, whether a form keeps its
   entries. The row names the element as the designer does (a page's title or "Page 2", a field's label), the form's
   settings "Form settings", an outcome "After sending", an action its name or "Action 2"; the empty form's row is
   "Add at least one field", as drawn. Six codes keep the server's sentence, whose words only the server has: a type's
   own check and a setting's bounds (`setting_invalid`), a value's bounds or kind (`invalid_value`, `limit_exceeded`),
   a redirect's problem (`invalid_redirect`) and a formula's, which names the character it is at (`invalid_formula`,
   `out_of_range`). They read in English until the server says its sentences in the user's language, as does a code a
   later server sends that this designer does not know. A test reads every code from the server's sources and fails
   for one without a sentence of the designer's, a place on that list, or on the list of codes the check never sends
   (reading a stored draft, the logic engine's while a form runs).
5. **A form without a field to fill in is an error of the check,** `fields_missing`: no field of the form holds a
   value (a type the site lacks counts, as the structural check refuses it already; texts, headings and images do
   not). It is at the end of the first page (its last group's `fields`, or its `groups` where it has none), where Fix it
   opens the palette. Publishing refuses it now: such a form gave visitors nothing to send.
6. **The publish dialog shows the check's result first and waits for it.** It asks for a check as it opens; while the
   answer is awaited it says "Checking the form…" and Publish waits. No problems: "The form check found no problems."
   Errors: a note read out as it comes, "This form can't be published yet: 1 error needs fixing.", each problem with
   its element, its sentence and Fix it, and Publish stays off. Warnings: "The form check found 1 warning. It doesn't
   block publishing." (or "It also has 1 warning, …" beside errors), each with Fix it. Five of each are listed, then
   "And 3 more, which the form check lists." Suggestions are left to the sidebar. A check that gives no answer says
   "The form check didn't answer. Publishing checks the form again." and lets Publish through: the server refuses the
   errors itself.

## Consequences

- Every problem of 9.5b, 9.5d and 9.5e has its words in English and German and a Fix it, but those about the whole
  form and places the designer does not show.
- A new blank form cannot be published until it has a field to fill in; no form that gave visitors anything to send is
  refused.
- The sidebar is Umbraco's sidebar modal with Close in its footer, as the rules overview; the dialog keeps its size.
- 10.1 and 10.2 gain the Fix it for the settings, outcomes and actions they build (roadmap change, see WORKLOG).
- The website's runtime does not change; the backoffice grows by the check's module, its sidebar and its texts.
