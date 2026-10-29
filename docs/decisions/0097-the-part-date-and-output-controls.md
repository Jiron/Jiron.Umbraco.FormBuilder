# 0097. The part, date and output controls

- Status: accepted; extends [ADR-0015](0015-themes-and-the-markup-contract.md) (how a field with parts, a slider, a
  calculation, a file field, a signature, the captcha's place and a site's own type meet the contract),
  [ADR-0093](0093-the-razor-rendering.md) (their view models; the page's rules read the answers as the pipeline
  does), [ADR-0058](0058-field-answers-and-values.md) (the parts a page asks for, and a signature's typed part),
  [ADR-0062](0062-the-built-in-types-checks.md) (a date's window on the page) and
  [ADR-0063](0063-the-calculation-language.md) (a calculation's value on the server's page)
- Date: 2026-10-29
- Roadmap step: 6.2e (decided and built); 6.2f renders the repeater, 6.2k stores a signature typed without
  JavaScript, 6.3 styles the classes, 6.4 enhances the controls (the pad, the drop zone, the live calculation), 7.2
  fills the captcha's place

## Context

Step 6.2e renders the controls of the types with parts, dates and outputs on a Razor page: a date, a time and a date
and time, the slider, a name, an address, a file field, a signature, a calculation and the captcha's place, and a
site's own type. The specimen in `docs/ux/website/fields.html` and its README name the classes; the markup contract
(ADR-0015) names the hooks: a `legend` for a field with parts, an id per control and per part, `<alias>.<part>` for
parts (ADR-0058).

Open were: which date a window relative to today names on a page, and what a bound the browser cannot hold does;
which step a date and time and a time give the browser; what a date, a date and time and a slider show before an
answer; how a page without JavaScript shows a calculation, and which answers the page's rules and calculations read;
which parts of a name or an address a page asks for, which it marks and which carry an error; what a file input
says it takes; how a visitor without JavaScript signs; what the captcha's place is; and what a site's own type's view
is handed.

## Options

**A calculation on a page without JavaScript:**

1. **Leave it to the runtime** (6.4). A page without JavaScript shows an empty output, and a calculation a later
   page's rule reads has no value there.
2. **Work it out on the server** with the rules' own evaluation (`FormRules.Evaluate`), over the page's answers read
   as the pipeline reads a submission: cleaned, held to their limits and read by their types. The page, the
   pipeline and the browser (which shares the vectors) cannot disagree.
3. **Work it out from the answers as posted.** Spaces around a number or an answer longer than its type takes
   would give a value the pipeline never stores.

**A window relative to today:**

1. **No `min` and `max`** for a relative bound, only the server's check. The browser's picker offers days the
   server refuses.
2. **The bound for the site's today** (its time zone, ADR-0043) when the page renders, as the server checks it. A
   page cached across midnight carries the day before's window until the runtime (6.4) moves it; the server's check
   still decides.
3. **The visitor's today** in the browser only. A visitor in another zone sees another window than the server
   checks.

**A signature without JavaScript:**

1. **A file input for a picture of a signature.** Few visitors have one, and its checks are a pad's.
2. **The name typed instead,** which counts as the signature, as the pad's "type your name instead" does with
   JavaScript (§4.3.2): one text input named `<alias>.typed`, which 6.2k's post turns into the stored signature.
3. **Nothing,** so a required signature cannot be given without JavaScript.

## Decision

Option 2 for each.

- **The page's answers** are read as the pipeline reads a submission (`SubmissionReader.Clean`, the type's limits,
  the type's reading), a grid's rows by row. A hidden field's posted answer shows nowhere: publishing keeps rules and
  formulas off it, and a visitor text shows nothing of it (ADR-0068). The rules, the
  calculations and the recalled texts all read them. A first render's answers are what the controls hold: a
  field's default (without merge tags), and without one the start its type gives.
- **Starts:** a date with `defaultToday` starts on the site's today; a date and time with it at the site's time now,
  back to its step from midnight; a slider in the middle of its range on its step (half a step up when two are as
  near), where a browser places a range without a value. A default comes before `defaultToday`.
- **Dates and times:** the browser's own inputs. A window's bounds are dates, or the site's today moved by their
  offsets; a bound that falls outside the calendar sets none. A date and time's window runs from `T00:00` of its
  first day to `T23:59` of its last, and its step (minutes times 60) is given only when it divides the day, since the
  browser counts steps on from its earliest moment across days and the server from each day's midnight; a time gives
  its step always.
- **A slider:** `jfb-range` with the range input and, when it shows its value, an `output` beside it for sight
  (`aria-hidden`). The input's `aria-valuetext` says the value as it shows, in the culture's numbers with its unit,
  when that differs from the number. A range has no `required` and its label no mark: it always has a value.
- **A calculation:** an `output` (`jfb-output`) with the field's id and name, labelled by the field's `label`, its
  value formatted as the answer is shown (ADR-0066: the culture's numbers, its places, two by default, its prefix
  and suffix), an empty number counting as zero (ADR-0063). It has no `for`: nothing reads it. It is never marked,
  since the visitor gives it no answer, and one visitors do not see (`visible: false`) renders nothing.
- **A name and an address** are fieldsets with the label as the legend (`FieldLabelElement.Legend`) and the parts in
  `jfb-parts`, each part a `jfb-part` (`jfb-part-wide` for a street line, and the country when no region is asked)
  with its own label and input: a name the parts it lists (first and last by default), an address its street, its
  postcode and its town always, then the parts it lists, both in the type's order. The first part carries the
  field's id, so the error summary links to it; the others `-<part>`. Each part has its `autocomplete` token,
  `autocapitalize="words"` (but the postcode) and no spell check; an address's country is a dropdown of every
  country in the page's culture (ADR-0096).
- **Parts that are needed:** a required field needs the parts it requires, or, when it lists none, every part it
  asks for (an address: its street, postcode and town). When it needs fewer than it asks for, the form's style marks
  the parts: the asterisk on the needed ones, or "(optional)" on the others; otherwise the legend's mark says it
  once. After a post with the field's error, the needed parts that are empty carry `aria-invalid`, or every part
  when none of them is empty (the error is then about what was given).
- **A file field:** `jfb-upload` with the browser's own file input, its `accept` the extensions the field takes, in
  the order of its groups and then its own (`FileFieldType.Extensions`, the list the server checks), `multiple` when
  it takes more than one, and `data-jfb-dropzone` for the runtime's drop zone. The server decides what it takes; the
  hint only narrows the picker.
- **A signature** is a fieldset with the label as the legend, then in `jfb-signature` a line that says typing the
  name counts as signing and the name's input (`<alias>.typed`, `autocomplete="name"`, a visually hidden label, the
  line as its description). The runtime adds the pad and the way between the two (6.4); 6.2k stores the typed name
  as the signature (ROADMAP 6.2k, strengthened in this step).
- **The captcha's place** renders only when the form uses a captcha: an empty `jfb-captcha` with the field's id and
  `data-jfb-captcha` naming the provider, which 7.2 fills. The theme hides the place while it is empty, so a page
  without JavaScript shows no empty panel; a post from such a page is scored, not refused (ADR-0012).
- **A site's own type** gets `CustomControlModel`: its public settings as JSON with the texts that hold tags
  rendered for the page (6.2c), and its answer's parts. A built-in type without a model of its own gets none.
- **View headers** name a site's own type's view by its alias, dots and dashes included (`fields/acme.mood`), so its
  header is read as the built-in ones are.
- The words for the parts and the signature are the package's, in English and German (14.2 adds the others).

## Consequences

- A page without JavaScript shows every value the runtime would: a calculation for the answers it holds, a slider's
  start and a date's today; the runtime works them out again as the visitor answers.
- Rendering a form evaluates its rules once per render, over answers read by their types; the page and the stored
  entry agree on a calculation for the same answers, which a test checks against the entries endpoint.
- A page cached across midnight (6.2l) offers the day before's window until the runtime corrects it, and a slider
  or a date's start is the moment the page was stored; the server's checks decide either way.
- The specimen (`docs/ux/website/fields.html`) follows the decisions: a name's and an address's parts by their
  names (ADR-0058), no `for` on the calculation's output, and the slider's output hidden from screen readers.
- **Revisit** if the server comes to count a date and time's step across days, or the runtime needs a typed
  signature's input to carry the pad's state.
