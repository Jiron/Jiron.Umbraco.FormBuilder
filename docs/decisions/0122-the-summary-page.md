# 0122. The summary page

- Status: accepted; extends [ADR-0100](0100-the-post-without-javascript.md) (a move of its own, and where errors after
  Send show) and [ADR-0015](0015-themes-and-the-markup-contract.md) (`Summary.cshtml`)
- Date: 2026-10-30
- Roadmap step: 6.5a (decided and built); 6.5b reuses the rendering for the copy of the answers

## Context

A form's settings have had a summary page ("check your answers", §4.3.4) since the schema of ADR-0002, and nothing has
shown it. Step 6.5a asks for a page of every answer the rules leave shown before sending, grouped by page, with a
"Change" for each that opens its page with every answer kept, and the send from there. The design of Phase 2 drew it
(`docs/ux/website/book-check.html`): the third of three pages, titled "Check your answers", lists the answers of the
first two in the ledger of the default theme, each row with "Change", and then the page's own field, the consent,
before Send. The theme has carried the summary's rules since 6.3b. Open were: which page shows the answers, what a row
shows, what "Change" posts and where it leads, and where errors show when a send from that page fails.

## Options

**Which page shows the answers:**

1. **A page of the package's own after the last page,** titled by the package. The designer's canvas would not show
   it, the progress would name a step the editor never made, and nothing could follow the answers on it: a consent the
   visitor reads after checking them, as the design draws it, would have to come before.
2. **The last page the rules show, with the answers of every page before it** above its own fields. The editor makes
   the page and titles it ("Check your answers"), puts on it what belongs after the check (a consent, a note), and the
   canvas shows the form as visitors see it. The last page's own answers are on the page itself, in their fields.

**Where "Change" leads back:**

1. **To the field's page, then on with Next,** each page checked again on the way.
2. **To the field's page, then straight back to the summary.** Faster, but a change can make a later page need an
   answer (a rule shows a required field), which the jump would skip, or check pages the visitor never sees again.

**Where errors show when a send from the summary page fails** (a choice that filled up meanwhile, an answer another
entry took):

1. **On the first page that holds one,** as ADR-0100 decided for every send.
2. **On the summary page,** each in its answer's row, the error summary linking to that row's "Change", as the design
   draws it (ADR-0022's amendment): the visitor sees what went wrong among the answers they just checked.

## Decision

Option 2, option 1 and option 2.

- **Which page:** with `submission.summaryPage` on, the last page the rules show, when they show a page before it,
  shows the answers of every page they show before it, after its title and its text and before its groups. A form whose
  rules show one page shows no summary; the setting belongs to the settings of forms with several pages (the designer's
  "More options", 10.x).
- **What a row shows** (`Summary.cshtml`, `jfb-summary`): a section per page with the page's title as an `h3` (or
  "Page 2", as the progress names a page without one), then a description list. Each field that takes an answer and
  that the rules leave shown (the page, the group and the field) is a row: its label as the page shows it (`dt`), its
  answer (`dd`) and its "Change" (`dd`).
  - The answer is written as `entry.answers` writes it in the outcome (ADR-0014, ADR-0066): options by their labels,
    files by their names, a ticked box and a switch in words, numbers and dates in the culture; a repeatable group as a
    list of its rows, each its children's `Label: answer` apart by "; ". Line breaks show as line breaks.
  - An unanswered field reads "Not answered" (`data-jfb-empty`). A sensitive field shows the three bullets the outcome
    masks it with (`data-jfb-masked`), with "Hidden for your privacy" for screen readers; "Change" shows the answer on
    its page.
  - Content fields, hidden fields, the captcha's place and a calculation visitors do not see are not listed. A
    calculation visitors see is listed with its value and no "Change", as nobody types it.
- **"Change"** is a submit button, `jfb-nav=goto:<alias>`, after the visually hidden label it changes ("Change<span
  class="jfb-visually-hidden"> Email address</span>"), with `formaction` the form's own action and the field's control
  as the fragment, so the page opens at the field without JavaScript too. The handler moves to the page that holds the
  field without a check, every answer kept, as Back does (the post's files and a typed signature are kept as for Back);
  a name the form does not know shows the posted page again. From there the visitor moves on with Next, and each page is
  checked again on the way.
- **The default button:** the summary page starts with the visually hidden copy of its Send (ADR-0096), so Enter in a
  field on it sends, and never presses the first "Change".
- **Errors after Send on the summary page:** the page stays. An error at a field of a page before it shows in that
  field's row, after the answer (`jfb-error`), the row's "Change" is described by it, and the error summary links to
  the "Change"; the page's own fields show their errors at the fields, as on any page. A send from any other page still
  shows its errors on the first page that holds one (ADR-0100).
- **The markup** is the contract of ADR-0015: `div.jfb-summary > section[aria-labelledby] > h3 + dl > div > dt, dd,
  dd > button`. The view model is `PageViewModel.Summary` (`SummaryViewModel`), the view `FormViewPaths.Summary`, so a
  site can override it per theme and per form. The copy of the answers (6.5b) reuses it without "Change".
- **The themes:** `default` draws the ledger it has had since 6.3b, with the rows of a repeatable group one under
  another; `bootstrap5` draws the rows from Bootstrap's own variables (ADR-0108); `unstyled` shows a plain description
  list (ADR-0107).
- **With JavaScript,** the runtime checks only Next and Send and lets every other button's post go on (ADR-0113), so a
  "Change" posts as it does without JavaScript; the summary is the server's, rendered again with each post. Moving
  through pages in the browser waits with 6.5d (ADR-0120).
- **The words** "Change", "Not answered" and "Hidden for your privacy" are the package's, in English and German (14.2
  adds the other five), in `docs/ux/copy.md`.
- **The demo:** the specimen's second page becomes "Check your answers", with the summary page on, so its summary lists
  every field type; it shows the progress as a bar, and the job application keeps the steps.
- **The journeys** (Playwright, without and with JavaScript): the job application moved through its three pages with
  Next and Back, every answer kept, a page's errors checked on the server before the next; the specimen to its summary
  page, a "Change" back to a field and on again, and sent from there; and the job application's three pages with
  `dir="rtl"`, set on the form after each page loads, as the theme checks of 6.3 set it, since the demo has no language
  written right to left. Axe is clean on every page of them.

## The design skills

Loaded before the summary page was built (ADR-0019, ADR-0020); the owner answers no questions, so each choice is ours.

- **design-taste-frontend:** the design read is a trust-first step of a form on a host site, for every visitor, in the
  calm ledger of Phase 2: the package's own tokens in `default`, Bootstrap's variables in `bootstrap5`, plain markup in
  `unstyled`; dials 3, 2 and 5. The skill counts steps of a form outside its scope, so only its guards apply: no em
  dash, contrast, one corner scale, no decorative dots. Its rule against a hairline under every row loses to the
  ledger of ADR-0022, whose answers sit in rows under hairlines (the roadmap's tokens win, "How to use").
- **impeccable** (Operate mode, the bundled engine): DESIGN.md asks for the ledger's split on a wide form and the
  answer under its question on a narrow one, which the theme has had since 6.3b; the detector finds no anti-pattern
  in `Summary.cshtml`, `default.css` or `bootstrap5.css`. Hardening added what the mock left open: the masked answer's
  words for screen readers, a repeatable group's rows as a list, line breaks kept, a calculation without "Change", and
  the row's error described on its "Change".
- **emil-design-eng:** nothing new moves. "Change" is a link button that sinks 1 px when pressed, as every button
  does; the page it opens loads as any page after a post does, and the fragment's jump to the field is instant; an
  error in a row fades in as any error does, and with reduced motion nothing moves.

  | Before | After | Why |
  | --- | --- | --- |
  | A smooth scroll to the changed field | The fragment's instant jump | A page load that then glides adds delay to an action the visitor repeats |
  | A row of its own animation for an error | The theme's error fade | One motion for one meaning, as every error comes in |

## Consequences

- The summary is as correct as the last post: it shows the answers the server read, ruled and checked. A rule on the
  summary page's own fields that shows or hides a field of an earlier page changes the listed rows at the next post,
  and the pipeline stores what the rules show then.
- An editor who wants every answer checked, the last page's too, ends the form with a page that holds only what follows
  the check, as the design's form does.
- A site's own `Page.cshtml` writes the summary where it renders `Model.Summary` through `Model.Views.Summary`, as
  the built-in view does after the page's text.
- **Revisit** if editors ask for a summary of the package's own, without a page of theirs, or for a way back to the
  summary in one step that still checks every page between.
