# 0111. Rules on the page

- Status: accepted; extends [ADR-0015](0015-themes-and-the-markup-contract.md) (the markup contract gains the hooks of
  a form with rules) and [ADR-0110](0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md) (the chunk
  applies the rules to what it reads); keeps [ADR-0093](0093-the-razor-rendering.md)'s note, which now asks only for an
  asterisk that shows
- Date: 2026-10-30
- Roadmap step: 6.4c (decided and built); 6.4d and 6.4e check the answers in the state the rules give, 6.5 moves
  through the pages, 6.4f shows the calculations' results as the visitor types

## Context

Without JavaScript the server renders every element in the state its rules give for the answers it has, and a rule
that depends on an answer on the same page takes effect when the page is posted ([ADR-0015](0015-themes-and-the-markup-contract.md)).
With JavaScript, step 6.4c applies the rules as the visitor answers, and the page has to look exactly as the server
would render it: the state is in attributes (`hidden`, `data-jfb-hidden`, `disabled`, `required`), and a label's
mark follows the form's required-marker style ([ADR-0093](0093-the-razor-rendering.md)).

Three questions. **The model:** the form engine of `packages/logic` takes Core's `LogicForm`, which Core makes from the
definition ([ADR-0061](0061-the-form-rules-engine.md)); the browser holds the form endpoint's answer instead.
**The context:** the server's render evaluates the rules with its clock, the site's time zone, the culture, no member
and the query values a post carried ([ADR-0100](0100-the-post-without-javascript.md)). **What the page cannot tell
by itself:** which of a field's controls take `required` (a name's parts only some, a grid's row required by itself
always), which mark a label carries in the other state, and which disabled control stays disabled when its field
shows (a dropdown without options).

## Options

**The model.** (1) Put Core's model into the page's model data: a second copy of the rules beside the projection's,
and more to keep equal. (2) Make it in the browser from the answer, as Core makes it from the definition, held to it
by a fixture both runtimes read.

**What the page cannot tell.** (1) The runtime works it out per field type: a second copy of the views' decisions.
(2) The views say it as hooks of the markup contract, on a form with rules only, and the runtime only flips
attributes: it never builds markup, so a site's own views and the themes keep working. For the marks: (a) the
runtime adds and removes them from a `<template>` the form carries; or (b) the server writes each mark a label may
show in either state, `hidden` when it does not show now, and the runtime flips `hidden`. The base rules hide
`[hidden]` in every theme (`display: none !important`), and a hidden element is out of the accessibility tree.

**Reading a field a rule shows again.** Its controls are disabled while it is hidden, so `FormData` leaves its answer
out. (1) Enable the controls for a moment to read them: a write to the page for every reading. (2) Decide, show, and
when a field is shown again read and decide once more, until nothing more is shown.

## Decision

Option 2 for the model, option 2b for the hooks, option 2 for the reading.

### The model and the context

- `logicFormOf(form, fields)` (`packages/logic/src/projection.ts`) makes the engine's model from the answer: the keys
  are the ids, a field's kinds and whether it can be required are the answer's `fields` ([ADR-0109](0109-reading-answers-in-the-browser.md)),
  a grid's rows are subjects, a calculation carries its formula and places, and only switched-on rules are there, as
  the projection holds them. `tests/fixtures/definitions/logic.json` holds the answer and Core's model of the full
  definition fixture and each demo form; `LogicModelFixtureTests` writes and checks it in NUnit (with
  `JFB_UPDATE_SNAPSHOTS=1` it writes the file and fails), and `projection.test.ts` checks the browser's model against
  it in Vitest.
- **The context** is the render's, as far as a page knows it: the browser's clock to the whole second in UTC, the
  site's time zone and the page's culture from the model data, no member (a cached page is the same for everyone),
  and the query values of the names the form lists (`settings.queryValues`): those a post carried on in the
  `jfb-query-<name>` inputs, or else the page's address's, as the post handler reads a first post's from its
  `Referer`. `holdQuery` (`packages/logic/src/posts.ts`) holds them as the handler does: at most 20 values of a name,
  none longer than 2,000 characters, the limits of Core's `FormSubmissions`, which `posts.test.ts` reads from the C#.

### The hooks (ADR-0015)

The views write them only when the form has a rule on a page, a group or a field (validation rules change no element):

| Hook | Where | What the runtime does |
| --- | --- | --- |
| `data-jfb-requires` | a control whose `required` follows its field: the field's own controls, the parts a name or an address needs once it is required, a grid's radios in a row the row itself does not require | `required` while the field is required |
| `data-jfb-mark="required"` | a label's mark that shows while the field is required: the asterisk, and a part's mark (its asterisk, or "(optional)" on a part the field does not need) | shown while the field is required, else `hidden` |
| `data-jfb-mark="optional"` | a label's mark that shows while the field is not required: "(optional)", and the asterisk of a grid's row required by itself | shown while the field is not required, else `hidden` |
| `data-jfb-disabled` | a control the server disables whether or not its field shows (a dropdown without options) | stays `disabled` when its field shows |
| `data-jfb-required-note` | the note above the first page, written `hidden` while no asterisk shows (asterisk style only) | shown while an asterisk shows outside an element a rule hides |

`FormBuilderHtml.Mark` writes every mark: the views no longer each spell it, and a form without rules gets the markup
it had. A field's, a part's and a grid row's models gain `IsMarkShown` and `MarkShownWhile` (a `MarkWhile` value);
`FieldViewModel.Hooks`, `PartModel.FollowsRequired` and `GridRowModel.FollowsRequired` say where `data-jfb-requires`
goes; `FormViewModel.IsRequiredNoteHidden` and `Hooks` write the note. The views keep contract version 1, as no
version has been released (section 4.14 counts from the first).

**The note asks only for an asterisk that shows.** The server counted a grid's row asterisk even while a rule hid the
grid; it now counts it only while the grid shows, as it counts a field's, and the runtime decides it the same way.

### Showing the state

`rulesOf(form, model)` (`packages/runtime/src/rules.ts`, in the chunk) runs when the frame attaches the form, and
again in a microtask after the form's `input` and `change` events, once for all the events of a turn:

1. Read the page (6.4b) and evaluate the form (`evaluateForm`).
2. Show the state. A page, a group or a field the rules hide carries `hidden` and `data-jfb-hidden`, a field its
   controls `disabled` (a hidden page's or group's fields are hidden with it, as the engine says); a page that is not
   the current one (the one the server showed) stays `hidden` without `data-jfb-hidden`. A repeatable group's child
   takes its row's state. `required`, the marks and the note follow as the table says. Only an attribute that changes
   is written, so a page's own observers see real changes only, and the first application of a page the server
   rendered changes nothing.
3. When a field was shown again, read and decide once more, as its answer now counts; at most once per element, since
   the rules have no cycle, and usually not at all.

**Nothing animates.** A field shows or hides the moment the answer that decides it changes, as the server renders it:
the change answers the visitor's own action, comes often while a form is filled in, and `hidden` takes the element out
of the layout and the accessibility tree at once, which a transition would have to put off. A theme may add one.

A form without rules gets no listeners. When the engine refuses the rules (it cannot, for a published form, unless the
page and its model data disagree), the form keeps the post without JavaScript, which reports it. A current page that a
rule hides is hidden like any other; moving to the next shown page is 6.5's.

## Consequences

- The page shows what the server would render for the same answers, by one engine, one model and the views' own
  hooks; a site's override of a view keeps working with rules once it writes the hooks, which its upgrade notes say.
- A form with rules carries its other marks `hidden` in its markup, a few bytes per label; a form without rules is
  unchanged.
- Each change reads and decides the whole form, which is cheap for the forms the designer builds; 6.4n measures it on
  the kitchen sink.
- **Revisit** if a field type needs a hook the table lacks, or if reading the whole form on each change shows in the
  kitchen sink's timings.
