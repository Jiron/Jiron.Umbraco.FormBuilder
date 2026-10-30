# 0123. The copy of the answers

- Status: accepted; extends [ADR-0100](0100-the-post-without-javascript.md) (the page after a sent post) and
  [ADR-0122](0122-the-summary-page.md) (its rendering, reused without "Change")
- Date: 2026-10-30
- Roadmap step: 6.5b (decided and built); 6.5d adds the copy after a send with JavaScript

## Context

A message outcome has carried "show a copy of the answers" (`showAnswers`) since the schema of ADR-0002, and §4.3.4
says what it shows: the submitted answers with a print button and a "fill in again" link. Step 6.5b builds it for the
post without JavaScript, reusing the summary page's rendering, only in the response to the submission: the redirect
after the post carries a short-lived, single-use signed token and never an answer, the page is never cached, sensitive
answers are masked, and printing needs no page of its own. The design of Phase 2 drew it (`docs/ux/website/book-sent.html`):
below the outcome's message and reference, the summary's sections and rows without "Change" (an unanswered question
reads "Not answered"), then "Print your answers" and a link to fill the form in again.

The page after a sent post already exists (ADR-0100): the redirect's `jfb-sent=<instance>.<token>` names the form, the
instance and the entry's submission id under a Data Protection purpose of its own, for 30 minutes, and every render of
it while the token counts shows the outcome again (a reload, the back button), from the stored entry. Open were: which
fields the copy lists, what makes it single use, what a second view shows, how printing works without JavaScript, and
where the copy sits in the markup.

## Options

**Which fields the copy lists:**

1. **The fields that hold a stored answer.** Simple, but an optional question left empty vanishes, which the design
   shows as "Not answered", and nothing tells a question the rules hid from one the visitor skipped.
2. **The fields the rules showed, worked out again from the stored answers.** The entry keeps no query values, so a
   rule on a query value could come out differently than it did for the visitor.
3. **The fields the rules showed, as the submission found them.** The submitted event already records the outcome the
   rules chose; it can record which pages, groups and fields they hid as well (their keys, never an answer), so the
   copy lists exactly what the visitor was asked.

**What makes the copy single use:**

1. **A token that names itself spent.** A signed token cannot remember being read; only the server can.
2. **A mark in the database, the first writer wins.** `jfbKeyValue` adds a key only when it is new, in one statement,
   on every server (ADR-0036): the request that adds `copy:<form>:<submission id>` shows the copy, and no other does.

**What a second view shows** (a reload, the back button, a second tab, while the token counts):

1. **Nothing of the entry,** the form as for any visitor. A visitor who reloads would think the entry was lost and send
   it again.
2. **The outcome and its reference as before (ADR-0100), without the copy,** and one line in its place that says the
   answers show only once.

## Decision

**Where it shows.** On the page after a post without JavaScript sent the form, when the outcome the rules chose is a
message with `showAnswers`, below the message and the reference, for that form and instance only. The public API's
receipt never carries answers; the copy after a send with JavaScript comes with 6.5d, from the same models.

**What it lists** (option 3). The submitted event records `hidden`, the keys of the pages, groups and fields the rules
hid for the entry. The copy has a section for each page the rules showed, in the form's order, and a row for each field
on it that the rules showed and that takes an answer (no text block, no captcha), from the version the entry was sent
with, labelled as the page labels it now: in the page's culture, a dictionary item through the dictionary, a merge tag
in a label or title rendered from the entry's answers. A row writes the answer as the summary page does (ADR-0122): one
line per line of the answer, a repeatable group's rows as a list, files by name and size, three bullets and "Hidden for
your privacy" for a sensitive field, "Not answered" for a field with no stored answer. A field whose answer is kept only
until the actions run has no row once it has none, since its answer may simply be gone. An entry whose event records no
`hidden` (made before this step) lists only its answered fields. A section's heading is the page's title, else
"Page 2" when the copy has several sections, else "Your answers". There is no "Change": the entry is sent.

**Single use and short-lived** (option 2). The copy shows only when all of these hold: the token reads for the form and
instance (ADR-0100: signed, 30 minutes), the entry was submitted less than the token's lifetime ago, the request is a
`GET`, and this request is the one that added the entry's mark. A mark older than the token's lifetime is useless, so
each new mark first deletes those, and the table never holds more than half an hour of them; since a mark is never older
than its entry, deleting it can never let a copy show twice. The page carries `jfb-sent`, so it is `no-store` and the
website output cache varies by it and stores nothing (ADR-0004, ADR-0104).

**A second view** (option 2). The outcome and the reference show as ADR-0100 decided, and in the copy's place one line:
"Your answers show only once, right after sending." The "Fill in the form again" link stays. An expired or broken token
shows the form as for any visitor, as before.

**Printing.** "Print your answers" is a `button` with the print icon and `hidden`; the runtime reveals it and binds it to
`window.print()`, on the page's first enhancement and on `JironFormBuilder.init(root)`. Without JavaScript it stays
hidden, and the browser's own print uses the same styles. The print styles hide the navigation, the buttons and the
progress (base rules for every theme, so an unstyled form prints clean too), keep a row, a heading and its first row on
one sheet, and print the light scheme in ink on white, since a dark scheme's light text would vanish on paper.

**"Fill in the form again"** is a link to the same page without `jfb-sent`, at the form's instance (`#jfb-1`); the
page after a sent post holds no other query (ADR-0100).

**The markup contract.** `Outcome.cshtml` writes the copy after the outcome's element, inside the form's wrapper: the
summary's view (`Summary.cshtml`, rows without their "Change" `dd`), or the note (`<p class="jfb-help" data-jfb-copy-shown>`)
on a second view, then a `jfb-navigation` with the print button (`data-jfb-print`) and the link
(`jfb-button jfb-button-link`). The outcome's element keeps the focus; the copy follows it in reading order.

**Words** (English and German now, the other five languages with 14.2): "Print your answers" / "Ihre Antworten drucken",
"Fill in the form again" / "Formular erneut ausfüllen", "Your answers show only once, right after sending." / "Ihre
Antworten werden nur einmal angezeigt, direkt nach dem Senden.", "Your answers" / "Ihre Antworten".

**The demo.** The job application's outcome shows the copy: three titled pages, a repeatable group, a file and a
consent.

## The design skills

**`design-taste-frontend`.** The design read: a confirmation receipt inside a website form for visitors who just sent
it, trust-first and accessibility-critical, in the default theme's ledger language, native CSS on the package's tokens.
Dials 3 / 2 / 5, as for the summary page. Its pre-flight applies in part: no dashes as separators in the words, one
radius (the theme's), button contrast and labels on one line (both buttons are short), no two actions with one intent
(print, fill in again and, with a redirect, continue are three), dark scheme tokens in use, nothing that moves. The
landing-page rules (hero, imagery, eyebrows) do not apply to a receipt and were left out; the roadmap's §4.13 wins where
they would disagree.

**`impeccable`** (bundled engine, Operate mode; `harden` and `polish` on the incumbent world). Context loaded PRODUCT.md
and DESIGN.md ("The Question Ledger"). Hardening: long answers wrap anywhere (the summary's `overflow-wrap`), German
words are longer and the buttons wrap to their own row on a phone, the copy mirrors right to left by logical properties,
an entry with no answers at all still has its rows ("Not answered"), and an outcome without a reference keeps the same
order. Polish: the copy reuses the summary's rows unchanged, so the two screens read as one system; the note on a second
view is a help line, not a notice, since nothing is wrong. The look in Edge (default light and dark, bootstrap5,
unstyled, 1280 and 375 px, print, the second view) found one defect: between two sections a row's rule and the
section's made a double line, so a section's last row now drops its own when another section follows, on the summary
page too.

**`emil-design-eng`:**

| Before | After | Why |
| --- | --- | --- |
| The print button fades in when the runtime reveals it | It appears at once (`hidden` removed) | It is seen once per page load; motion would only draw the eye to its late arrival |
| The copy's rows fade in one after another | The rows are static | A receipt is read, not watched; a stagger delays reading it |
| A press on Print without feedback | The theme's `:active` press on `.jfb-button`, as every button | The same press everywhere; the print dialog is the real feedback |
| A smooth scroll to the form on "Fill in the form again" | The browser's jump to `#jfb-1` | A new page load; scrolling it would only delay the first field |

## Consequences

- The submitted event's details grow by the keys of what the rules hid. They hold no answer, and an entry's timeline
  can later say "not asked" as well as "not answered" (Phase 11).
- `jfbKeyValue` holds at most half an hour of copy marks; `IKeyValueRepository` gains a delete of the keys under a prefix
  older than a time.
- A reload after sending keeps the outcome and loses the copy; a visitor who wants the answers prints them at once, as
  the button invites.
- A site that overrides `Outcome.cshtml` adds the copy from the built-in view to show it.
