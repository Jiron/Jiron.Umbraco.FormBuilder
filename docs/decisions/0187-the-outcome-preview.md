# 0187. An outcome's live preview: the site's own views render it for a sample entry, beside its settings

- Status: accepted
- Date: 2026-11-05
- Roadmap step: 10.1c; builds on [ADR-0185](0185-the-outcomes-editor.md) (the outcome's sidebar),
  [ADR-0168](0168-the-canvas-draws-with-the-elements-views.md) (the canvas draws with the site's look), [ADR-0123](0123-the-copy-of-the-answers.md)
  (the copy of the answers), [ADR-0155](0155-pages-in-the-browser.md) (the runtime's outcome after a send) and IA 5.2

## Context

IA 5.2 puts an outcome's settings "beside a live preview with a sample entry", and 10.1c asks for the outcome as visitors
get it, made by the server from the draft, as it makes a real entry's, with a sensitive field's answer masked. What the
survey found:

1. **The server makes an outcome in two parts.** `SubmissionOutcomes` renders the message (its merge tags in the HTML
   context, then the one sanitiser) and the redirect's address (in the URL context, held to the publish rule) for an
   entry's scope; the page's `Outcome.cshtml` then writes it in the form's markup, with the reference, the link on and,
   where the outcome offers it, the copy of the answers (`Summary.cshtml` without Change), which `FormModels` builds from
   the shown form. The runtime fills in what the view leaves to it after a send: the countdown, from at least ten
   seconds (WCAG 2.2.1), and the print button.
2. **No sample entry is shared.** The action test runs answer each field with its label, privately; IA 5.2 and 8.1 name
   a "sample entry made from the form's fields" for the actions' Test and their previews too (10.2), and 10.3's preview
   fills a form "with sample answers that pass validation for every field type".
3. **The canvas draws with the default theme** (ADR-0168): its classes and stylesheets, in a shadow root, inert.
4. **The sidebar is Umbraco's medium one,** 800 px, one column.

## Options

For what renders the preview:

1. **The backoffice writes the outcome from data,** with a second writer of the copy's markup next to the Razor views.
   Two writers of one markup need a fixture to hold them together, and a site's own view of an outcome would not show.
2. **The server renders the outcome's markup with the site's own views**, for the draft, as a send's page does, and the
   backoffice puts it in the form's look.

For the sample entry:

1. **Each field's label as its answer,** as the test runs do. A choice holds no option then, an email address no address,
   and a number or a date an answer its type cannot read.
2. **An answer of the field's kind that the field itself takes:** each candidate held to the field's limits, read by its
   type and checked by its type's checks and its own, as a member's value that stands in for a hidden field is
   (`FieldValues.StandsIn`).

For where the preview sits:

1. **Below the settings,** as the wireframe draws it in one column: a change at the message is seen only after scrolling
   past every other setting.
2. **Beside the settings,** in Umbraco's large sidebar, kept in view while the settings scroll; below them where the
   sidebar has no room for two columns.

## Decision

**Option 2 in each.**

- **The endpoint.** `POST /umbraco/jfb/management/api/v1/form/{key}/outcome-preview` takes the draft's definition as
  the sidebar would make it with Update, the outcome's key and the language the view shows, and needs `design` on the
  form, as the form check does; it changes nothing. It answers what visitors get: the outcome's markup, or for a
  redirect the address they go to, and the seconds a message waits before its redirect. A definition it cannot read is
  refused with its problems (400), a form or an outcome it does not know with 404, a language the site lacks takes the
  site's default.
- **What it renders.** `SubmissionOutcomes` renders the outcome for the sample entry's scope, as for a real entry; a
  redirect that cannot be followed gives the site's thank-you message, as visitors get. The site's `Outcome.cshtml`
  (the form's own override first, as ADR-0093 looks for views) writes it in the default theme the canvas draws in, with what the
  runtime shows after a send: the message (the site's thank-you where the outcome has none), the reference line, "Go
  now" and the countdown with "Stay here" for a redirect after the message, and the copy of the answers built by the
  same code as a real entry's. The backoffice fills in the countdown from the runtime's own least seconds
  (`@jfb/runtime/countdown`, one source) and shows the print button, as the runtime does.
- **The sample entry** (`SampleEntries`, Core, for every preview and test run): entry number 42, as the reference's
  example in the Settings view, submitted now, in the state an entry of the form arrives in (approved at once, submitted
  for approval, or unverified with email verification), in the language shown, for the form as the site runs it, with
  the site's setting locks applied, as the form check reads it. Every field that holds a value is answered, the rules not
  applied, since the preview shows the outcome being edited, not the one the rules would choose. A field's own default
  value comes first, rendered for the sample as the page renders it, and is a hidden field's only answer, since visitors
  send no other. Then each field gets the first of a few candidates of its own kind (a dropdown that takes several asks
  for several) that its type reads and its checks take: a name, an email address, a phone number, an address and a web
  address of one made-up person (Alex Berger, `alex.berger@example.com`, a number and a street of no real place) for
  those types; the field's label for any other text; the first option (then more, for a field that asks for several) of
  a field with choices, from its list or source where it has one; checked for a checkbox, ticked for a consent (with the
  hash of its wording, as a form posts it) and yes for a yes-or-no question; for a country, its own default, then its
  first offered, then the language's country; a small number within the field's checks; today, a week on or thirty years
  back for a date; half past nine for a time. A calculation is worked out from the other answers and rounded to its
  places, as the rules work it out. A field whose type takes none of them, a file and a signature among them, is not
  answered, and the copy of the answers says "Not answered" for it, as a real entry's copy says for a field left empty.
  The sample has no page and no site of its own, so a tag of the page or of a member says nothing in the preview. The
  action test runs take the same sample.
- **Sensitive fields** are masked as the outcome masks them for a real entry: in a field's tag, in `entry.answers` and
  in the copy, by the same code.
- **The sidebar** becomes Umbraco's large one. From 960 px of its content on, the settings take the start and the
  preview the end, kept at the top while the settings scroll; below that, the preview follows the settings. Its heading
  is "Preview with a sample entry"; the outcome shows in the site's look on a sheet like the canvas's. Unlike the
  canvas's drawing it is not inert: a screen reader reads it as visitors' page reads, the sample's answers included,
  while none of its links and buttons is a stop of Tab and a press on one goes nowhere. A redirect shows as one line,
  "Visitors go straight to …", with the address the sample entry gives.
- **Live.** The preview is asked once the changes pause, as the form check is (600 ms), the newest answer winning; a
  change that leaves the draft as it was asks nothing. Until the first answer the frame holds its place, empty and busy;
  later answers replace the last one without a flash. An ask that gets no answer says "The preview couldn't be shown."
  with Try again, as copy.md words a failed load; the next change asks again too.

## Consequences

- `OutcomePreviewController`, `OutcomePreviews` (Web) and `SampleEntries` (Core) are new; `FormModels` builds the copy's
  sections from a shown form and a scope, for a page and for the preview alike; `RazorViewText` writes a view to text,
  for the preview and for mail; `ActionTestRuns` takes its sample from `SampleEntries`.
- The permission matrix has a row for the endpoint (design on the form), and the OpenAPI snapshot and the client have it.
- The view is written in the culture previewed, as a page in it is (a site's own view may read the dictionary or format
  a date), and the preview takes the language's direction, as the visitor's page does.
- The sidebar's look in the walk changes from medium to large; the outcome's settings keep their order.
- Not here: the form's own theme, which the full preview of 10.3 shows, and a sample answer for every field type, which
  10.3 audits.
