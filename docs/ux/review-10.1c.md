# Step 10.1c: an outcome's live preview

ROADMAP 10.1c shows an outcome beside its settings as visitors get it, with a sample entry made from the form's fields,
as each setting changes: the server renders it from the draft, as it renders a real entry's
([ADR-0187](../decisions/0187-the-outcome-preview.md), building on ADR-0185, ADR-0168, ADR-0123 and ADR-0155). Per
ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.2, which puts the settings "beside a live preview
with a sample entry", and the wireframe `form-settings.html`, state `outcome`, "Preview with a sample entry"). The owner
answers no questions during the run (CLAUDE.md rule 8), so `impeccable`'s `shape` interview and every other choice were
decided here or in the ADR.

## What was looked at

The UX walk (`tests/e2e/specs/ux/walk.spec.ts`, ROADMAP 9.6a), run with `JFB_UX_ONLY=settings` on the e2e host in Edge:
at 375, 768 and 1440 px, in Umbraco's light and dark themes, in English and German, each shot with what axe and the
layout measures found. The walk's form now has a name and a sensitive email, and its message outcome greets the sample
by name, names the email and shows a copy of the answers; its redirect for German visitors goes to `/de/danke`.

| Shot | What it shows |
| --- | --- |
| [a message's sidebar](review-10.1c/settings-outcome-1440.png) | the settings at the start, the preview at the end: the message with "Alex Berger" in place of `{{ field.name }}` and the email masked, the sample's reference 2026-00042, and the copy of the answers with the email masked, Print your answers and Fill in the form again |
| [the same, dark, German](review-10.1c/settings-outcome-1440-dark-de.png) | Umbraco's dark theme and German words around the visitor's page, which keeps the site's look and the visitors' language |
| [a redirect's sidebar](review-10.1c/settings-outcome-redirect-1440-dark-de.png) | "Besucher gelangen direkt zu /de/danke" on the sidebar itself: visitors get no page of the form |
| [on a tablet](review-10.1c/settings-outcome-768-dark.png) | one column: the preview after the settings |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on every screen of the Settings view in the twelve ways, 120 shots: nothing in the view's,
  the sidebar's or the preview's own elements, the visitor's page in the preview included. Umbraco's tree reports
  `label-content-name-mismatch` on its menu items on every shot, as on every screen of the section (ADR-0182,
  OWNER-TODO).
- **One false reading:** at 768 px in the dark theme, axe found the sidebar's Cancel label at 1.15:1 "on `#ffffff`".
  At that width the preview follows the settings, and its white sheet scrolls under the sidebar's footer. Axe takes the
  background from every box under the label's rectangle, the clipped sheet among them. The browser's own hit test at
  the label's centre, through every shadow root, finds only the footer's surface (`rgb(33, 38, 46)`) under the button,
  where its `#eeeeef` label is 13.1:1. The shot shows the same.
- **The bundled detector** (`impeccable detect --json`): `[]` for the preview, its previewer and the sidebar.
- **The layout measures:** no box scrolls sideways inside the sidebar or the preview, and no word is cut by an ellipsis
  at any width. The window scrolls sideways by 545 px at 375 and 152 px at 768, Umbraco's 920 px page (ADR-0182).
- **The budgets:**
  - The backoffice at start is 61.54 KB of 64 (gzip): 0.17 more, for the preview's words, which load with the
    settings' words. The largest file is 46.14 KB, and the preview loads with the outcome's sidebar.
  - The runtime with every feature is 59.81 KB of 60: 0.01 more, as the countdown's least seconds now have a module
    of their own, which the backoffice imports too.
- **The journeys**, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors who want to see what a visitor gets
after sending while they write it, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 /
5, as for 10.1a and 10.1b. What it still asks held:

- **One look for the visitor's page:** the preview draws the site's own markup in the default theme's stylesheets on a
  white sheet like the canvas's, whatever Umbraco's theme; everything around it is Umbraco's surface and words. No
  colour of the package's own.
- **Every state composed:** waiting for the first answer (the sheet's place held, busy, still), a later change (the last
  preview kept, busy, no flash), shown, a redirect (one line on the sidebar), and no answer ("The preview couldn't be
  shown." with Try again, said by the live region).
- **Pre-flight:** no emoji, no gradient, no spinner in the content, no motion of its own; the copy in plain words from
  copy.md's voice ("Preview with a sample entry", its help, the redirect line, the failure line).

## impeccable

Operate mode, a refinement inside the incumbent world; the code, IA 5.2, the wireframe and copy.md were the context
(`impeccable context`, the bundled engine, no hooks or live mode). The `shape` brief, decided rather than asked: an
editor writing an outcome wants to see the visitor's page as they type, with believable answers in place of the tags,
nothing about it able to leak a real answer, and nothing in it to press. Critique against it and what the looks found:

| Finding | What was done |
| --- | --- |
| The wireframe's one column puts the preview under every setting, out of sight while the message is typed | Umbraco's large sidebar, the preview beside the settings and kept at the top while they scroll; one column where the sidebar has no room for two |
| A redirect's line stood on the white visitor's page, which visitors never see for a redirect, heavy in the dark theme | the line stands on the sidebar; only the visitor's page is white |
| `inert` hid the preview from screen readers, the sample's answers with it | readable; no stop of Tab in it, and a press with any button goes nowhere |
| A preview that got no answer could only be asked again by changing something | "The preview couldn't be shown." with Try again, as copy.md words a failed load, said as it comes |
| A new place in the list asked for a preview that could not differ | a place change asks nothing: the server shows the outcome by its key |
| The sample answered every field with its label, so a choice held no option, a number or a date nothing its type reads | each field the first answer of its own kind that the field takes (ADR-0187) |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the preview is asked once the changes pause (600 ms), as the form check is | typing asks once, not with each key |
| — | the last preview stays, busy, until the next answer, which replaces it at once | it changes with every pause in typing; a spinner or a fade each time would pull the eye from the box being typed in |
| — | the countdown shows its first second, still | the preview is the moment after sending; a running count beside the editor's work would move all the time |
| — | the preview kept in view while the settings scroll | the setting being changed and its effect stay side by side |
| a redirect drawn on the visitor's white page | its line on the sidebar | a white box for one line was the heaviest thing on the screen, and visitors see no such page |

## The words

English and German for every string, in `lang/settings.en.ts` and `settings.de.ts`, from copy.md's voice: "Preview with
a sample entry" · „Vorschau mit einem Beispieleintrag", its help line, "Visitors go straight to" · „Besucher gelangen
direkt zu", and "The preview couldn't be shown." · „Die Vorschau konnte nicht angezeigt werden." with Try again ·
„Erneut versuchen"; copy.md has the help and the failure rows. The visitor's page in the preview keeps the visitors'
language, whatever the backoffice's.

## Kept as they are

- **Umbraco's 920 px page** on a phone, and its dark theme's colours on the tree's open row, as every section (ADR-0182).
- **The visitor's headings:** the copy's page headings (`h3`) and a message's own headings stand under the preview's
  `h4`, as the site's views write them. A level changed in the preview alone would show a page no visitor gets, and
  going up a level skips none.
- **A tag of the page, the site or a member says nothing** in the preview: the sample has none of them (ADR-0187).
- **The default theme**, not the form's own, and a sample answer for every field type: the full preview of 10.3 shows
  the first and audits the second (ADR-0187).

## A reader's review

A reader who had not seen the work read the change against the step, the ADR and the code. They found no security
problem and no race in the previewer, and eight findings. Each was fixed before the tamper round, with a test, apart
from the ones kept above:

- **The copy left out rows that visitors always get.** It was built with no record of hidden fields, so it skipped
  every unanswered field, where a real entry's copy says "Not answered" for each; the test asserted the gap. The copy
  now counts nothing as hidden, since the rules are not applied to the sample, and the test holds the row.
- **The failure state** stood on the white sheet, where Try again took the dark theme's light label (about 1.1:1), and
  nothing said it. It now stands on the sidebar, said as it comes (`role="alert"`), as the designer's other failures
  are.
- **The site's setting locks** were not applied, so a locked list of countries or a calculation's visibility previewed
  otherwise than visitors get it. The sample now takes the form as the site runs it, and the action test runs do too.
- **A dropdown that takes several** got one choice and stayed unanswered: the sample now reads the field's own kind, not
  its type's.
- **Two answers differed from a real entry's.** A hidden field was answered with its label, where visitors send its
  default value, and the entry was always "submitted", where a real one may also arrive approved or unverified. The
  default value now comes first and is a hidden field's only answer, and the state is the one an entry of the form
  arrives in.
- **The culture:** a site's own `Outcome` view that reads the dictionary or formats a date wrote in the backoffice
  user's language. The view is now written in the culture previewed (`RazorViewText`, which the emails share).
- **The narrow layout's check** also passed with the preview beside the settings once the sidebar had scrolled. It now
  asks for the preview below the settings' end, and a new check holds the preview in view while the settings beside it
  scroll.
- **Smaller points:**
  - the language's direction was not set, so a right-to-left language previewed left to right;
  - a middle press still opened a link;
  - `java&#9;script:` slipped past the client's clean-up (the server's sanitiser already strips it);
  - a comment said nothing loads, while images do;
  - the architecture and the ADR's wording lagged behind the code;
  - the visitor's form was built at every ask, even with no copy to show.

  Each is fixed.

## The tamper round

In a throwaway clone of `66a6e8e` (ADR-0117), 46 changes each took back one rule of the step. They ran one at a time,
after every check had passed untampered, each under a time limit and with the file restored after it: the sample entry
(17), the server's preview (10), the previewer and its markup (16), and the sidebar (3).

The first run caught 37. The rest:

- **Missed (3):**
  - **A calculation left unrounded:** the test's 3 × 2 / 4 is 1.5 to one place either way. It now works out 6 / 7 to
    two places.
  - **The request's culture not given back:** no caller could see the code that gave it back, as an async method's
    change of the culture stays in it. The code is gone, and the tamper in its place makes the method not async, which
    leaves the culture with the caller.
  - **The last preview dropped while asking:** the previewer's own test holds it. The tamper had run the sidebar's
    tests, where the sheet keeps what it drew.
- **Did not build (5):** four left a parameter or a method unread, which warnings as errors refuse, and were rewritten to
  keep it read. The fifth, a copy of the answers whatever the outcome says, found one rule in two places:
  `OutcomePreviews` gives the visitor's form only for a message that shows the copy, and the model checked the outcome
  again. The model's check is gone, and the tamper now changes the one rule.

The second run of these eight, on `e1d6667`, caught all eight. The two columns, the sticky preview and the one column
below 960 px are CSS that jsdom does not lay out: the journey and the walk's shots hold them.

## What the journeys found

`form-outcomes.spec.ts` has a third journey, on a form with a name and a sensitive email:

1. It opens the default message's sidebar and finds the preview beside the settings: the site's thank-you and the
   sample's reference, ending in 00042.
2. It writes "Thanks, " with a tag of each field: the preview says "Thanks, Alex Berger" with the email masked, and the
   address shows nowhere.
3. It turns the copy of the answers on: the rows Your name and Email, the email masked there too, and Print your
   answers. Axe runs inside the sidebar.
4. It makes the outcome a message and then a redirect after 5 seconds: the countdown says 10 seconds, and Go now leads
   to `/thanks`.
5. With the copy off, at 1440 × 560, it scrolls the settings to Add a rule, and the preview's heading is still in view.
6. At 900 px the preview follows the settings, and Cancel leaves the draft as it was.

`form-settings.spec.ts`, `designer-form-check.spec.ts` and `designer-rules-overview.spec.ts` ran beside it.

The first run of the new check failed: with the copy, the preview was taller than the 560 px window, and a box kept in
view that is taller than the window still leaves it. The journey now turns the copy off before it scrolls. With the
preview's `position: sticky` taken out, the journey fails at that check.

## The gate

The gate runs on the commit of this review in a throwaway clone (ADR-0117), with its clean clones and the whole browser
suite; WORKLOG has its result.
