# 0113. Errors on the page

- Status: accepted; builds on [ADR-0112](0112-checks-and-messages-on-the-page.md) (which errors, worded how),
  [ADR-0022](0022-default-theme-and-tokens.md) (the error summary) and [ADR-0004](0004-rendering-and-submission-paths.md)
  (the runtime renders the server's summary and inline errors and moves the focus); amends
  [ADR-0015](0015-themes-and-the-markup-contract.md) (the runtime's templates)
- Date: 2026-10-30; amended 2026-10-31 (6.8b: the server's errors join only a refused send; a field the page leaves out is the server's; 6.8c: a group's choices carry its error, not its fieldset)
- Amended by [ADR-0114](0114-calculations-on-the-page.md): a passing send asks for the post in a task of its own,
  since a form ignores a submission asked for while its submit event still runs
- Roadmap step: 6.4e (decided and built); 6.4i sends with `fetch` and shows the server's errors in place, 6.5 moves
  between pages in the browser

## Context

6.4d gives the runtime each shown field's first error, worded as the server words it, and holds it to the server's own
pages. What the visitor sees is still the post without JavaScript: the page renders again with its inline errors and
the error summary. 6.4e shows them in the browser: as the visitor leaves a field, after a refused send with the focus
on the summary, and gone once fixed, with `jfb:invalid` for the site's analytics (section 4.9).

The open questions: where the markup comes from (a theme adds classes through its class map, and a site may override
any view per theme and per form, ADR-0105), when an error shows and when it goes, what the summary does as answers are
fixed, what a send does whose errors are on another page of a form the server pages, and what `jfb:invalid` says.

## Options

**The markup.** (1) The runtime writes the contract's markup with the package's classes only: a theme's classes
(`invalid-feedback d-block`, `alert alert-danger`) and a site's own summary view are lost. (2) The model data carries
the theme's class map: it is the form endpoint's answer, which a headless page shares and which knows no theme, and a
site's own view is still lost. (3) The form view writes a template the same views and class map render.

**When a field's error shows.** (1) As the visitor types: red mid-word. (2) As they leave any field, one they only
tabbed through too. (3) As they leave a field they changed, or one that shows an error; and gone on the input that
fixes it.

**The summary as answers are fixed.** (1) Rebuilt on every change: it is a `role="alert"`, so it would speak on every
keystroke. (2) Kept until the next send, as without JavaScript. (3) Kept, and removed once no error shows on the form.

**A send whose errors are on another page** of a form the server pages. (1) Show what the browser has: the summary
would link to fields on hidden pages. (2) Send, and the server shows the first page that holds one, as without
JavaScript, until 6.5 pages in the browser.

**`jfb:invalid`.** (1) One per field as its error shows: noise for analytics. (2) One per refused send or Next, naming
each field and kind.

## Decision

Option 3, 3, 3, 2 and 2.

### The markup (ADR-0015 amended)

- **The templates:** a form writes `<template data-jfb-templates>` with the error summary as its `ErrorSummary` view
  renders it for one link (an empty heading and an empty link) and an error paragraph
  `<p class="<the error slot's classes>" data-jfb-error>`. A template's content is inert: never posted, outside the
  accessibility tree, its ids clash with nothing. A site's own form view without it gets the contract's markup with the
  package's classes.
- **An inline error** goes last in the field's fieldset when a legend labels the field, otherwise last in the field;
  its id is `<target>-error`. `aria-invalid="true"` goes on the control the target names, and the error's id last in
  its `aria-describedby`; an email address's repeat input gets both too. A fieldset's role takes no invalid state
  (6.8c): a group of choices gives both to each of its own radio buttons and boxes instead (a grid's to the row the
  error names, or to every row), after an option's description, and another group's fieldset names the error in its
  `aria-describedby` alone, while a name's or an address's parts get `aria-invalid` as the server sets it (the needed
  parts that are empty, or every part when none is). Clearing undoes exactly that.
- **The summary** takes the place of a summary already there, or goes after the progress and a notice, before the
  required note and the pages, as the form view places it. Its heading counts its links (`summaryHeading`), its links
  follow the page's order, and it takes the focus. With the form's `validationSummary` off it shows only the empty
  submission's message, as on the server, and a refused send moves the focus to the first field with an error.
- **A summary link** moves the focus to its control (or the first control inside it) and brings its label into view
  at once, without smooth scrolling.

### When errors show and go

- **Leaving a field** (the focus leaves its `data-jfb-field`, not moving between its own controls) that the visitor
  changed, or that shows an error, checks the form and shows that field's error, or clears it. A new error is said once
  in the form's live region, as its summary link reads it ("Email address: Enter a complete email address…"), so a
  screen reader hears it without the focus moving back (WCAG 4.1.3).
- **An answer fixed** clears its error on the input that fixes it: each `input` and `change` checks the form again in
  a microtask while any error shows; a message that changes waits for the visitor to leave the field.
- **A refused send or Next** (`jfb-nav` `submit` or `next`, the hidden default button too) shows every error of its
  scope, the summary, and fires `jfb:invalid`. A passing check lets the post go on as without JavaScript, through
  `requestSubmit` with the button pressed, until 6.4i sends it; a second press while a check runs does nothing; and when
  the words cannot load, the post goes on and the server checks it.
- **A send whose errors lie on another page** of a form the server pages goes on, and the server shows the first page
  that holds one, until 6.5.
- **The server's errors** after a post without JavaScript stay while their field is unchanged, also the ones only the
  server can make (an answer used before, files, options from a source), and a refused send keeps them in its summary.
  They join only a send the page's own checks refuse (6.8b): a send they alone would stop goes on, and the server checks
  it again, so an optional upload the server refused and the visitor has since emptied can be sent.
- **A field the page leaves out**, as one a member's own value stands in for (ADR-0124), is the server's to check: an
  error the page's checks find at a field with no element on the page is dropped, and the others still refuse the send
  (6.8b). Before, such an error let every send through unchecked.
- **The summary** stays until no error shows on the form, then goes; the empty submission's goes at the first answer.

### `jfb:invalid`

Dispatched on the form, bubbling, once per refused send or Next, with `{ key, alias, scope, errors }`: `scope` is
`send` or `page`, and each error is `{ field, kind }`, the field's path as the page names it (`data-jfb-alias`, with a
row's index) or null for the empty submission, and the kind of error (`required`, `invalid`, `check`, `rule`,
`empty`, …). Never an answer or a message.

### Design (ADR-0019, ADR-0020)

- **`design-taste-frontend`:** reading this as an error behaviour inside public website forms, for any visitor,
  trust-first and accessibility-critical, calm and restrained, on the package's own theme tokens and native CSS; dials
  3 / 2 / 5. The skill targets landing pages; what applies here passed its pre-flight: error text below its field,
  contrast (the themes' error colours, checked by axe), motion only with a reason and under reduced motion, no dashes as
  decoration in visible copy.
- **`impeccable` (Operate, `harden`):** never block a send on what only the server can judge; keep every answer; say
  an error that appears without a focus change; ignore a second press while a check runs; fall back to the post without
  JavaScript when the words cannot load; long German messages wrap in the themes' flex rows. Looked at in Edge on the
  demo's contact form at 375 px in English and 1440 px in German, after leaving a field and after a refused send: one
  column on the phone, each error under its field with the theme's mark, the summary above the form; the bundled
  detector finds nothing in the changed views and runtime.
- **`emil-design-eng`:**

| Before | After | Why |
| --- | --- | --- |
| A changed message re-inserts its error paragraph | The paragraph's text changes in place; only a new error enters | The theme's rise (`@starting-style`) plays for real arrivals, never as a flicker while the visitor corrects |
| Errors shown as the visitor types | Shown when they leave the field; cleared on the keystroke that fixes it | Reward early, punish late: no red while someone is mid-word |
| Moving between radios or a name's parts counts as leaving | Only leaving the field's wrapper counts | The focus moves inside one question often; checking there is noise |
| A fixed error animates out | It is removed at once | An exit faster than the entry; the fix is the feedback |
| A summary link scrolls smoothly to its field | It jumps at once, label into view, then the focus | A keyboard action never animates |
| The summary rebuilt on each refusal, its fade replayed | Updated in place on a second refusal, then focused | The focus and `role="alert"` say it; a second fade is motion without meaning |

## Consequences

- The browser shows what the server would show for the same answers, in the theme's markup and a site's own summary
  view, and the server still checks every post.
- `checks.fixture.test.ts` holds the display to the server's pages: clearing the server's errors leaves the first
  render's markup, and showing the runtime's errors again gives the server's page, in the default and the `bootstrap5`
  themes.
- Every form's markup grows by its templates, a few hundred bytes, the same for every visitor and cacheable.
- **Revisit** in 6.5, when Send on the last page can show an earlier page's errors in the browser.

## A group's choices carry its error (6.8c, 2026-10-31)

The phase review found a group's error state on its fieldset only, where the group role does not support
`aria-invalid`, so a screen reader on a radio button or a box of a refused group heard nothing of it. Now the views
write no `aria-invalid` on any fieldset: a group of choices (radio buttons, boxes, yes or no as buttons, a scale, a
rating, a grid) gives `aria-invalid` and the error's id to each of its choices, a grid's only to the row a row's error
names; a ranking, whose moves are buttons, and a group of parts or rows keep the error's id on the fieldset, and a
name's or an address's parts carry `aria-invalid` as before. The element's templates write the same, and the runtime
marks and clears the same controls (a page error's `gridRow` says which row). `checks.fixture.test.ts` holds the
runtime's marks to the server's page, whose form gained a required group of boxes for it. The themes' error borders key
on the choices' own mark (`default.css`, `bootstrap5.css`). In Edge the refused choices read as invalid, each
described by the error; a signature's typed name keeps its own mark on the server, and the runtime's goes with the pad
(6.4n).
