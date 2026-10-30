# 0114. Calculations on the page

- Status: accepted; builds on [ADR-0063](0063-the-calculation-language.md) (the language both runtimes share),
  [ADR-0111](0111-rules-on-the-page.md) (the state the results come with),
  [ADR-0097](0097-the-part-date-and-output-controls.md) (the outputs the server writes) and
  [ADR-0066](0066-answers-as-merge-tags.md) (how a result and a slider's value read); amends
  [ADR-0110](0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md) (the calculation language leaves the
  form chunk), [ADR-0109](0109-reading-answers-in-the-browser.md) (an unreadable answer's no value reaches the
  calculations again, as [ADR-0063](0063-the-calculation-language.md) decided it, in both runtimes) and
  [ADR-0113](0113-errors-on-the-page.md) (a passing send goes on in a task of its own)
- Date: 2026-10-30
- Roadmap step: 6.4f (decided and built); 6.4g recalls answers in texts, 6.4h fills defaults with merge tags, 6.4i
  sends what passes

## Context

A calculation field shows a result the server works out for the page's answers (ADR-0063): the form engine's value,
rounded to the field's places, in the culture's format between its prefix and its suffix, in an `<output>`; nothing
while it has no value, such as before a field it reads is answered, after an unreadable answer, an empty date or a
failed division. Without JavaScript the result changes only when the page is posted. 6.4c's rules already work every
calculation out in the browser, since a rule may look at one, so the form chunk carries the calculation language for
every form, though few forms have a calculation (section 4.12 lists calculations among the features loaded on demand).

A slider beside a calculation shows the same weakness: the server writes its value with its unit next to the thumb and
in `aria-valuetext`, and no step updates them as the thumb moves; the demo's room hire works its cost out of one.

Building it showed that neither runtime does what ADR-0063 decided for an unreadable answer. The reading stage
(ADR-0058 in Core, ADR-0109 in the browser) gives the rules no value for an answer its type cannot read, and the
calculations read the same answers, so `price * quantity` with a price of "12x" showed 0, a total no one asked for,
where ADR-0063 and section 4.3.3 leave the calculation without a value.

The questions: how the language leaves the form chunk while the form engine still decides rules and checks over the
results, when a result changes and what it says, what a screen reader hears, and how to prove the text is the server's.

## Options

**The language.** (1) In the form chunk, as now: about 2.5 KB for every form. (2) Its own chunk, loaded before the form
is enhanced when it has a calculation, and handed to the form engine. (3) A second build of the engine for forms with a
calculation: the rules and checks twice in the budget.

**When a result changes.** (1) When the visitor leaves a field: stale while they type. (2) With the rules, on every
`input` and `change` in one microtask, as 6.4c decides the page. (3) After a pause in typing: a delay without a reason.

**What a screen reader hears.** (1) The `<output>` as it is, a polite status whose text changes only when the result
does. (2) Silent while typing, said once in the form's live region on leaving the field: a second voice for one fact.

**An unreadable answer.** (1) As it is: the browser copies the server's 0. (2) The form engine learns which answers
their types could not read, and a calculation that reads one has no value, in both runtimes; the rules still see the
answer as unanswered.

**The proof.** (1) Unit tests of the formatting. (2) The server's own pages: forms with calculations and sliders
posted without JavaScript, rendered again with their answers, and the runtime's text compared with the server's,
element by element.

## Decision

Option 2, 2, 1, 2 and 2.

### The language (`@jfb/logic/calc`)

- **Its own module:** `packages/logic` exports the language as `@jfb/logic/calc` (`FORMULAS`: parse, check and work
  out), and the package's index no longer exports its values, only its types, so nothing the form chunk imports reaches
  it. `evaluateForm` takes the language as its fourth argument: null for a form without a calculation; a form with one
  and no language is a mistake of the caller, thrown as an error rather than refused as a form.
- **Loaded once, when needed:** the runtime makes one engine per form (`engine.ts`), the form engine's model of the
  form endpoint's answer (ADR-0111) and the language when that model has a calculation, imported as the chunk
  `calc-<hash>.js` once for the page. A form without a calculation never loads it. The form is enhanced once the
  language is there; when it cannot load, the form is left to the post without JavaScript, as when the form chunk
  cannot.

### The results (`outputs.ts`)

- **With the rules:** the page's state (6.4c) is decided on every `input` and `change` in one microtask, and now also
  for a form with a calculation and no rule; each calculation field's `<output>` then shows its result as the server's
  render writes it, a repeatable group's row by row: `calculationText` in `packages/logic`, which the merge tags already
  show a result with (ADR-0066), the culture's format with the field's places (2 unless its format sets 0 to 15),
  between its prefix and its suffix, each apart by a space. A calculation the rules hide, or without a value, shows
  nothing, as the server renders it. The rules' hooks stay what they were: a form without rules gets none of them.
- **A screen reader** hears the `<output>`, a polite status, when the result changes: the text is written only when it
  differs, so an answer elsewhere says nothing, and typing over the same result says it once.
- **A slider's value** follows the thumb on every `input`: its text beside the thumb when the field shows it, and its
  `aria-valuetext`, set when the text differs from the bare value and removed when not, as the server writes them
  (`sliderText`, the merge tags' display of a slider's value). It needs no rules and no language.

### An unreadable answer (both runtimes)

- **The answers carry it:** the form engine's answers (`FormAnswers` in Core and in `packages/logic`) list the
  fields whose answers their types could not read or found over their limits, outside repeatable groups and row by
  row; Core's submission reader and render and the page's reading fill them from the reading stage.
- **A calculation that reads one has no value:** the formula scope says which answers those are, and reading such a
  number or date gives no value, as an answer not in its wire form always did; a field the rules hide reads as
  unanswered as before, and an answer the formula never works out counts for nothing (`if` stops early). A
  formula's mistakes still come first.
- **Held by the vectors:** `calculations.json` gains formula cases with `unreadable` aliases and form cases with
  `unreadable` and `unreadableRows`, which NUnit and Vitest run alike, and mutants in both runtimes.
- **On the demo:** the room hire's cost is the room's hourly rate times the hours, the rooms a choice with "Other";
  a room the visitor describes leaves the cost empty until the library quotes it.

### A passing send (ADR-0113 amended)

The demo's send, once the room hire's cost showed, never went: the check of 6.4e ends within microtasks once the
words are loaded, the browser runs a listener's microtasks before the press's submit event ends, and a form ignores
a submission asked for while it fires one. A passing check now asks for the post in a task of its own, and a second
press waits until then; `errors.test.ts` holds it.

### Design (ADR-0019, ADR-0020)

- **`design-taste-frontend`:** reading this as a live value inside public website forms, for any visitor,
  trust-first, calm and restrained, on the package's own theme tokens and native CSS; dials 3 / 2 / 5. What applies
  passed its pre-flight: numbers in tabular figures (the themes' own), no motion on a value the keyboard changes, no
  dashes as decoration in visible copy.
- **`impeccable` (Operate, `harden`):** a result in the culture's format and never a guess (none for what cannot be
  worked out); long German texts wrap; the layout stays still while a result comes and goes. Looked at in Edge on
  the demo's room hire at 375 px in English and 1440 px in German, after choosing the hall and three hours and then
  a room the visitor describes: one column on the phone, the cost under its help, the slider's value beside its
  thumb; the empty result had let the fields below jump up, which the base rules now prevent. The bundled detector
  finds nothing in the changed runtime, stylesheet and views.
- **`emil-design-eng`:**

| Before | After | Why |
| --- | --- | --- |
| An empty result collapses its line | `output.jfb-output` keeps one line (`min-block-size: 1lh`) | The fields below stay put while the visitor types; layout shift reads as a glitch |
| The result could fade or count up to its new value | It changes at once | A value the keyboard drives changes dozens of times a minute; motion there is delay |
| Every answer rewrote the result | Only a changed result is written | The `<output>` is a polite status: it speaks only when the value changes |
| The slider's text beside it kept the server's value | It follows the thumb, the same text in `aria-valuetext` | One fact, said once, the same for sight and for a screen reader |

### The proof

`Rendering/CalculationFixtureTests` (integration) publishes a form with calculations of every format (a prefix and its
places, a suffix and no places, the default places, a result read by another calculation, the days between two dates,
a choice's number, a row's result in a repeatable group, a result a rule hides) and sliders with and without their
unit and shown value, posts answers as a browser without JavaScript does, with a required field left empty so the page
comes back, on a site in English and on one in German, and saves each page first rendered and rendered again in
`tests/fixtures/rendering/calculations-en.json` and `calculations-de.json`. The posts: every calculation answered; an
unreadable number, a division by zero and an empty date, each leaving its result without a value; and nothing.
`outputs.fixture.test.ts` blanks every result and slider text of each page, shows them again as the runtime does over
the page's reading, and expects the server's page element for element.

## Consequences

- A form without a calculation pays nothing for the language, and a form with one pays one small request, cached for a
  year under its content hash, before it is enhanced.
- The browser shows the result the server stores for the same answers, in the server's words, and the server still
  works out every result itself (ADR-0063).
- An empty result keeps its line in every theme (`base.css`), so a form's layout does not move as results come and go.
- **Revisit** if a screen reader's users find a result read out while they type too talkative; a pause before the text
  changes is the next option.
