# Phase 11 review: the UX walk

Step 11.4's last part, 11.6, walks every screen Phase 11 added: a form's Entries view with its Filter menu, the filter
dialog and the chips, Export… with its sidebar, the "…" with the summary email's and the keys' sidebars, the bulk bar,
the entry panel with Edit answers, its timeline, its notes and its "…", the entry's own screen, the Inbox with its Form
filter, Spam, a form's Insights view with its range, its Answers as charts, as tables and filtered, the Privacy group of
a form's Settings view, Settings › Data collection, and Forms at a glance on the section's overview. It reads the result
against ROADMAP §4.13 with the three design skills of ADR-0020, and closes the phase with an independent review of the
exports and the phase's wiring sweep. What changed is decided in
[ADR-0213](../decisions/0213-the-phase-11-ux-walk.md).

## How the walk works

`tests/e2e/specs/ux/walk.spec.ts` (`JFB_E2E_UX=1`, project `ux`, then `ux-sheets`) now visits Phase 11's 26 screens
beside those of Phases 9 and 10, in twelve combinations, 375 × 812, 768 × 1024 and 1440 × 900, light and dark, English
and German. It finds its way by tags, `data-mark` and icons, never by words. Its Phase 11 forms come from the API: 27
entries waiting for approval with a position, an amount and a sensitive salary, three of them in Spam
(`/jfb-testing/entries-form`), two months of a form's visits counted as its beacons would
(`/jfb-testing/insights-form`), and 60 entries of a form with an answer of every kind (`/jfb-testing/answers-form`).
Each shot keeps axe's WCAG 2.2 A and AA findings inside the section's own elements, the page's sideways overflow, boxes
that scroll inside themselves and words cut by an ellipsis.

## What was looked at

The last walk ran after the fixes (`.local/ux/11.6-2`, not committed), every screen of Phases 9 to 11 in all twelve
looks. These shots stand for it; "before" is the first walk (`.local/ux/11.6-1`).

| Shot | What it shows |
| --- | --- |
| [A form's Entries at 768 px, German](review-11/header-entries-768-de.png) ([before](review-11/header-entries-768-de-before.png)) | "Veröffentlicht" under the name, clear of Umbraco's "…"; each row's state under its name; before, the chip under the "…" and a state column squeezing the names |
| [Beside the panel, dark](review-11/beside-panel-dark.png) ([before](review-11/beside-panel-dark-before.png)) | name, state and time one line each; the open row in the mixed colour |
| [Entries on a phone, German](review-11/entries-phone-de.png) | the chip under the name, the states under the names |
| [Two rows ticked at 768 px](review-11/bulk-768.png) | the bulk bar with its count, Clear selection, three actions and the "…" |
| [Export…, German](review-11/export-de.png) | the format, the entries in one line, the fields with the sensitive ones left out |
| [The entry's own screen](review-11/entry-screen.png) | the title, the form once, the state chip, the actions in the bar |
| [The Inbox](review-11/inbox.png) | the form column, the count and the "…" together |
| [Spam at 768 px, dark, German](review-11/spam-768-dark-de.png) | no state column, the form under each entry |
| [Insights, dark, German](review-11/insights-dark-de.png) | the figures with their changes, the charts with Show as table |
| [Answers](review-11/answers.png) | a box per field: shares, a ranking's positions |
| [Answers as tables at 768 px, dark, German](review-11/answers-tables-768-dark-de.png) | each box as its table |
| [Answers filtered, dark, German](review-11/answers-filtered-dark-de.png) | the chip, Clear filters, and the line when nothing matches |
| [The Privacy group, German](review-11/privacy-group-de.png) | the form's switch and what it counts |
| [Data collection, dark, German](review-11/data-collection-dark-de.png) | the site's switch, "Einblicke" |
| [Forms at a glance at 768 px](review-11/glance-768.png) ([before](review-11/glance-768-before.png)) | each change on one line; before, "no / change" |

## What the first walk measured

The first walk (`.local/ux/11.6-1`, not committed) shot the Phase 11 screens in all twelve looks, 312 shots. Inside the
section's own elements:

- **The form's header beside six views.** Umbraco's header shares one row between the form's name and chip, its own
  "…" and the views' tabs, each part as wide as its contents ask. With Phase 11's Entries and Insights the tabs ask for
  more, and at Umbraco's narrowest workspace, 620 px at 375 and 768 px, the name and the chip keep 166 px. The name
  keeps its 6rem (ADR-0206), so "Veröffentlicht" ran 39 px past its box, under Umbraco's "…", "Published" 7 px, and at
  1024 px the German chip 2 px. The narrowest journey had not seen it: its user has four views.
- **The list beside the panel,** and in German at 768 px: the state's column squeezed the names, so "Person 0023" and
  "Waiting for approval" each took two lines.
- **The toolbar's "…"** on a line of its own where the toolbar wrapped, away from the count.
- **The open row in the dark theme:** Umbraco's colour for what is open carries the row's text at 4.34:1 (axe's
  colour contrast, on the entry panel's five shots at 1440 px, dark).
- **A row under the bulk bar** at 768 px (axe's target size, two shots).
- **The "select all" box** while some rows are ticked (axe's `aria-conditional-attr`, twelve shots): UUI's checkbox,
  below.
- **Forms at a glance** broke its change lines in two at 620 px: "no / change", "up from / none".
- **Three screens not shot:** Spam had no entries, the Privacy group was not the one opened, and Answers took "Show as
  tables" from the look before, as every look signs in as the same admin.
- **The rest** was what Phases 9 and 10 left as it is: Umbraco's 920 px page, its tree's rows' names and its open row
  in the dark theme.

## design-taste-frontend

Reading this as: an operator's tool inside Umbraco's backoffice, for the editors and teams who work through entries,
export them and read what a form's visitors did, in Umbraco's own language (UUI, its tokens, sidebars and toasts),
leaning toward restraint: variance 3, motion 2, density 6, as a list of entries is denser than a designer's screen.

| Finding | What was done |
| --- | --- |
| The state chip under Umbraco's "…" beside six views | where the name's 6rem and the chip do not fit on one line, the chip goes under the name, both inside Umbraco's 70 px bar (ADR-0213, decision 1) |
| Two lines for each name and state beside the panel | below 44rem, and beside the panel, a row's state sits under its name: one line per fact (decision 2) |
| The toolbar's "…" alone on its second line | the count and the "…" keep together at the toolbar's end (decision 3) |
| Forms at a glance: "no / change" | a change line keeps to one line; the form's name gives way instead (decision 7) |
| One accent across the phase | held: UUI's selected colour for the charts' bars and the answers' shares (`--jfb-bo-color-selected`), Umbraco's own colours for the states, one dot for what is unseen |
| The bulk bar floats over the list's last rows while it scrolls | kept: it sticks to the view's foot, apart from the workspace's footer (IA 6.1); the rows the keyboard reaches now stop above it |
| Small sidebars for the columns, Export…, the summary email and Forward…; small dialogs for the filters, a set's name, the keys and the Inbox's forms; a medium dialog for an image answer | kept: each is the size its content asks for, as in Phase 10, and the list stays in sight beside a sidebar |

## impeccable

The bundled detector (`impeccable detect --json`, no downloads, hooks or live mode) on Phase 11's 22 elements and
views (the entries view, the entry panel and screen, the eight entries sidebars, the Inbox's and Spam's screens and the
Inbox's badge, the Insights view with its chart, table, Answers and Forms at a glance, Data collection and the action
toast), and again on the three files 11.6 changed (the entries view, the form's header and the tokens): `[]`. It
cannot see a layout's widths, so the walk's measures and its shots carried the critique and the audit.

| Finding | What was done |
| --- | --- |
| The open row's text at 4.34:1 in the dark theme (WCAG 1.4.3) | `--jfb-bo-color-current`, Umbraco's colour for what is open mixed 85% with the surface: 5.0:1 dark, 9.9:1 light and high contrast; a look found no contrast finding left in the dark list beside the panel (decision 5) |
| A row the keyboard reaches could lie wholly under the bulk bar (WCAG 2.4.11) | while rows are ticked, the rows stop 8rem short of the bottom when scrolled to; a look walked the cursor with `j` at 768 px: the row's foot at 718 px, the bar's top at 727 px, no target-size finding (decision 6) |
| The chip ran under Umbraco's "…", which then covered its words | the chip under the name; `narrowest.spec.ts` holds it as the admin with six views at 375, 768 and 1024 px (decision 1) |
| "Auswertung" on the Insights tab, "Auswertungen" in History, no glossary row | "Einblicke" in both, and copy.md's glossary has the word (decision 4) |
| The "select all" box while some rows are ticked: `aria-checked="false"` on a box the browser reports as mixed (axe's `aria-conditional-attr`) | kept as Umbraco's: UUI's checkbox writes it on its own native input, in Umbraco's collections as in the entries list; Edge's own accessibility tree reports the box as mixed, "Select the entries on this page", so a screen reader hears the right state. Added to OWNER-TODO's report |
| The walk left Spam, the Privacy group and Answers' tables unshot or shot in the wrong state | the walk seeds three entries into Spam, opens the Privacy group by its id and sets "Show as tables" for each shot (decision 8) |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| the chip under Umbraco's "…" at 620 px | the chip under the name, moved without motion | it moves once, when the window's width changes; nobody watches a layout follow a resize |
| the entry panel opening beside the list, and `j` and `k` reading on through the entries | kept without motion | it is opened dozens of times a day, by key; at once is fastest |
| the bulk bar appearing on the first tick | kept without motion | ticking is repeated; the bar's actions must be there at once |
| the open row in Umbraco's flat colour | the mixed colour, still without a transition | it follows `j` and `k` row by row; a fade would trail the keys |
| Export…'s More options: its chevron turning | kept: 160 ms on the strong ease-out, none with reduced motion | it says the section opened from that button |
| chips' remove, the menus' items, the column chooser and the sort | kept: `pressOf`, 120 ms to `scale(0.97)` | every small button answers a press the same way since 10.4 |
| Answers dimming to 60% while a new range reads | kept: 150 ms on opacity | the same boxes keep their place; only their numbers change |
| Forms at a glance pulsing while it reads | kept: none with reduced motion, gone once the numbers come | it says numbers are on the way without a spinner |

## The words

Read in both languages against copy.md, whose glossary now has "insights", "Einblicke" in German, the word the view's
tab, History's property and Settings › Data collection share; "Auswertung" is gone from the dictionaries. The phase's
new strings each have their row in copy.md, the test entry's line in the panel ("Test entry, sent from the preview",
"Testeintrag, aus der Vorschau gesendet") and the export's "kept until" line among them, and the dictionaries' tests
hold both languages to the same keys.

## Kept as they are

- Umbraco's 920 px page, its tree's rows' names and its open row in the dark theme (ADR-0182, OWNER-TODO).
- UUI's "select all" while some rows are ticked: `aria-checked="false"` on a box the browser reports as mixed, as in
  Umbraco's own collections (OWNER-TODO).
- The bulk bar over the list's last rows while it scrolls: it sticks to the view's foot, apart from the workspace's
  footer (IA 6.1), and a row the keyboard reaches stops above it.
- The name's box 6 px lower than Umbraco's other headers while the chip is under it, so both keep inside the bar.
- Forms' own words in the charts, such as a page named "Your message" in a German backoffice: a form's texts are its
  editors', in the languages they wrote.

## The §4.13 checklist, screen by screen

One primary action where Umbraco puts it, at most three secondary actions in sight, each fact said once, nothing
squashed at the 620 px workspace, and the screen's states.

| Screen | Primary | Secondary in sight | Said twice? | At 620 px | States |
| --- | --- | --- | --- | --- | --- |
| The form's header | (the footer's) | the "…" | no: the state once, as its chip | the chip under the name, both inside the bar | — |
| A form's Entries view | Publish… (footer); a row's name opens it | Filter, Export…, the "…" | no: the count once, at the toolbar's end | a row's state under its name, the form's under the title in the Inbox | skeleton, first use in the words of what the user may do, no results, nothing found (sensitive fields not searched), failed with Try again, Show more |
| Filter menu | — | its saved sets | no | its popover | no sets yet |
| Filter dialog | Apply | Cancel | no | Umbraco's small dialog | — |
| Chips | — | each chip's remove, Save as a set, Clear | no: each filter once, as its chip | the chips wrap | — |
| Export… | Export 24 entries | Cancel, More options | no: which entries once, under Entries | Umbraco's small sidebar | sensitive fields left out until chosen, in the background, ready, failed, kept until |
| The "…": summary email, keys | Save / Close | Cancel | no | Umbraco's small sidebar and dialog | — |
| The bulk bar | the first offered action | Clear selection, two more, the "…" | no: the count once | wraps to two lines | busy, Undo in the toast, too late to undo |
| The entry panel | Approve (by state) | Reject, Mark as spam, Forward…, the "…" | no: the state chip only where no row shows it | covers the list | reading, failed with Try again, gone |
| Edit answers | Save | Cancel | no | one column | each problem under its field |
| Timeline, Notes | Add note (Notes) | a note's Edit | no: who and when once a line | one column | none yet, failed with Try again |
| The entry's own screen | Approve (by state) | Reject, Mark as spam, Forward…, the "…" | no: the form named once, under the title | one column | not found |
| Inbox | — | Filter, the "…" | no: a row's form once | the form under the title | first use with the summary email |
| Spam | Empty Spam… | a row's Not spam, Delete… | no: no state column, everything here is spam | as the list | empty, a row Not spam took out says its state |
| Insights | — | the range, Compare, Overview · Answers, a chart's Show as table | no: each figure once, its change under it | the figures two by two | reading, no visits yet, insights off for the form or the site |
| Answers | — | the range, Filter, Show as tables | no: a field's answered count once, in its box's header | one column of boxes | reading, no entries in the range, filtered |
| A form's Privacy group | Publish… (footer) | its switch | no: the closed group's line says what is set | one column | off for the whole site, said once under the switch |
| Settings › Data collection | Save | — | no | one column | reading, set by the host's configuration and so locked, failed with Try again, not allowed, a save someone made first |
| Forms at a glance | — | the range | no: each change once, under its figure | a change on one line, the name gives way | reading, no views yet, failed with Try again |

## What the journeys found

- **The narrowest journey** (`narrowest.spec.ts`) now also opens, as the admin with all six views, a published form on
  its Entries view and one with changes waiting on its Insights view, in German at 375, 768 and 1024 px: the name's
  box is 96 px wide at least, the chip lies inside the section's own box, under the name at 375 and 768 px, both inside
  Umbraco's bar, and nothing of the section is cut; 5 of 5 with the designer's two runs. With the header's box back on
  one line, all three of the new runs failed on the chip's right edge, 39 px past it at 375 and 768 px and 2 px at
  1024 px, so the journey holds the fix.
- **The looks** (`.local/ux/11.6-look`, not committed) measured the header at 375, 768, 1024 and 1440 px in both
  languages: under the name wherever the two do not fit, the name's box 27 px high and the chip 21 px, 9 px clear of
  the bar's edges; beside the name at 1024 px in English and at 1440 px, where all six tabs show. In the dark list
  beside the panel the open row measured `#3064b5` under `#eeeeef` with no contrast finding; with two rows ticked at
  768 px, `j` stopped the cursor's row at 718 px with the bar's top at 727 px. Forms at a glance at 768 px: no box
  scrolls sideways and every change line is one line high, in both languages.

## What the last walk found

The walk ran twice: the first (`11.6-1`) on Phase 11's 26 screens in all twelve looks, 312 shots; the last (`11.6-2`)
after the fixes on all 99 screens of Phases 9 to 11, as the header changed on every screen of a form: 1,176 of its
1,188 shots, the twelve others below. Phase 11's screens, inside the section's own elements:

| Measure | First | Last | What remains, and whose |
| --- | --- | --- | --- |
| axe colour contrast | 10 shots | 0 | — |
| axe target size | 2 | 0 | — |
| axe `aria-conditional-attr` | 12 | 12 | UUI's "select all" while some rows are ticked; Edge reports it mixed (OWNER-TODO) |
| the chip past its box, under Umbraco's "…" | every form screen at 375 and 768 px | 0 | measured by the looks and held by `narrowest.spec.ts` |
| words cut by an ellipsis | 0 | 0 | — |
| boxes that scroll inside the section | 0 | 0 | — |
| the page's sideways scroll beyond Umbraco's | 0 | 0 | Umbraco's own 545 and 152 px at 375 and 768 px (ADR-0182) |
| shots that could not be taken | 35 | 0 | Spam, the Privacy group and Answers' tables, which the walk now reaches |

The screens of Phases 9 and 10 measured as in Phase 10's last walk: the action sidebar's radio half under its sticky
footer at 375 px in English (7 shots, review-10.2c), the outcome sidebar's Cancel at 768 px dark (2, review-10.1c),
rule chips cut where an element is narrow (16, ADR-0178) and the move dialog's tree rows' names (12). The walk's own
"coming" screen had become the Inbox, which Phase 11 built; it now opens Privacy's People, and was shot in all twelve
looks again (`11.6-3`). Umbraco's tree rows' names (every shot) and its open row in the dark theme stay Umbraco's
(ADR-0182, OWNER-TODO).

## The exports' review and the wiring sweep

An independent reader went through the exports' code (11.4) and a second through the phase's wiring
(`ff38c584..57b9b311`), each finding checked against the code before it was fixed. What they found led to three commits
of their own:

- `60bde781`: a background file downloads through a plain link with a ticket that holds for one minute, as a link
  carries no backoffice token, and every ZIP of files goes to the background; an export is laid out by the version the
  site serves, or served last once the form went offline, never by a draft; a field is sensitive when its version or
  the field catalogue marks it; each reference and time received is the one the entry's receipt gave; a CSV quotes a
  cell holding any of the three delimiters; the worker renews its lease every minute and deletes what it stored when
  the host stops or an attempt fails; and the panel's Print leaves the dialog's choices as they were (ADR-0210).
- `24675a9e`: the panel names a test entry as one, and the tests now assert what the sweep found unasserted.
- `d335b57c`: a form deleted for good is forgotten in every user's choices, its columns, its export choices and its
  filter sets.

## The tamper rounds

Each tamper took back one rule, with the file restored after it, and counts as caught only when a named test failed.

- **The exports, round A (36):** 24 caught; seven missed and four that the analyzers would not compile went into round
  B, and one no longer applied after the review's fixes.
- **The exports, round B (38):** the first round's misses written again where they did not compile, and one for each
  rule the review's fixes added. In the client, 8 of 8 caught. On the server, 24 of 29 caught; four again left code
  the analyzers call unused, and one missed: a group's field that only the field catalogue marks sensitive went along,
  which no test had. `EntryExportTests` now marks a top-level field and a group's field sensitive in the catalogue alone.
- **The exports, round C (6):** that miss and its top-level twin, both caught by the new test, and the four rewritten so
  they compile: three caught at once. The fourth, a ticket's action that waits for a signed-in user, passed: the
  filter's test ran its "anonymous" request with a sign-in that had no Umbraco user, not with none. It now runs as a
  plain link does, without any sign-in, and the tamper was caught.
- **The header, in the browser (1):** the header's box back on one line, built and run through the narrowest journey's
  new runs: all three failed on the chip's right edge.
