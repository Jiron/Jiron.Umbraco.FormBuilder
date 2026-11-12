# 0241. The UX polish pass: scroll areas that hold what they scroll, a column that keeps to a phone's window, and the top tasks counted on the built screens

- Status: accepted; amends decision 2 of [ADR-0182](0182-the-phase-9-ux-walk.md) (the phone) and builds on
  [ADR-0020](0020-three-design-skills.md) (the design reviews)
- Date: 2026-11-12
- Roadmap step: 16.5 (part 16.6, the UX polish pass)

## Context

Part 16.6 walks the whole product's journeys at 375, 768 and 1440 px, light and dark, in English and German, reruns
the three design skills, counts the clicks of each persona's top five tasks on the built screens, and takes three items
carried in:

1. **From 15.3:** at 1440 × 900 the Design view's outer scroll area (`uui-scroll-container#main`) scrolled by 1,052 px
   beside the canvas's own scroller, before any selection. The view kept to its height; the palette's hidden reasons of
   unavailable field types (`.visually-hidden`, `position: absolute`) sat in a scrolling list that was not positioned,
   so they took the Design view as their containing block and escaped the list's clip.
2. **From 14.1:** on a phone, Availability's two date boxes and the retention days side by side, an action card's
   controls beside its text, the rule builder's target and „wenn" on one row, and „Vorlage speichern" on two lines in
   a dialog. Umbraco's page stays at least 920 px wide, so a 375 px window scrolls sideways and shows only the first
   part of the 620 px workspace (ADR-0182); the views laid out for those 620 px.
3. **From 14.1 too:** the walk set the backoffice's language in the browser only, so the texts the server writes in the
   user's language (the Overview's alert, the digest's preview, the email layout's sample) stayed English in its
   German shots.

The owner answers no questions during the run (CLAUDE.md rule 8); each choice below is this record's.

## Options

1. **The overflow:** give the Design view `overflow: hidden`; position only the palette's list; or position every
   scroll area of the backoffice and hold that with a test.
2. **The phone:** keep ADR-0182's "the phone is Umbraco's 920 px page" and fix nothing; a layout of the section's own
   for 375 px; or let a view's column keep to the window's width while the window is narrower than Umbraco's page, so
   the container queries the views already have take over.
3. **Server-side German:** a German site default for the whole host; switch the admin's language per walk; or a twin
   of the admin who keeps German on the server.

## Decision

1. **Every scroll area of the backoffice positions its box** (`position: relative` beside its `overflow: auto`), so an
   absolutely positioned helper inside it is clipped by it and scrolls with its content; the entries list's status line
   had the same leak. Hiding the view's overflow would have hidden the symptom and clipped the inspector's popovers. A
   source test holds the rule for every scroll area (`styles/scroll-areas.test.ts`), and the designer's browser test
   measures every box around the Design view at 1440 px. **The visually hidden rule takes no room** of its own: no
   padding, border or margin, as the website's rule has it, defined once in the tokens (a caption's padding made the
   Overview's table scroll sideways by 17 px once its scroll area held it).
2. **A view's column keeps to a phone's window** (amends ADR-0182, decision 2): while the window is narrower than
   Umbraco's page (below 920 px), the column of the Settings view, the Actions view, Settings › General and its
   screens, Access, People and Retention starts at the workspace's start and is no wider than the window less the
   view's padding (`styles/phone.ts`). Once the workspace is in sight the whole column is too, and the container
   queries lay it out for the phone: two boxes one under the other, a closed group's line under its name. A wider
   window changes nothing. A narrow action card puts its switch and "…" on a row under its text, where Test was
   already in the "…"; the rule builder's target takes its own line in a narrow sidebar, „wenn" under it. The walks pan
   to the workspace's start before a phone shot, where a reader pans to read it.
3. **The admin's German twin**, a test user whose language the server keeps (`TestUser.Language`, set through
   Umbraco's user update), signs the walks in for their German looks. Nothing changes for the shared host's other users.
4. **A dialog whose title names the object** takes the verb alone, as the copy guide says: the template dialog's
   „Speichern" and "Save".
5. **The top tasks are counted on the built screens** by browser tests that read each persona's count from
   `docs/ux/top-tasks.md`, so the document and the screens cannot drift: the designer's, entry handler's, viewer's and
   privacy officer's first five and the three of a user without grants in `top-tasks.spec.ts`, the administrator's in
   the site-wide specs that already change and restore those settings, and the checklist's on its own host. Where the
   built path is shorter, the document says so; where a click was not needed, it went: "Find a member instead…" opens
   Umbraco's member picker at once.
6. **The member search is offered only to who may open Umbraco's Members section.** Counting the privacy officer's
   tasks found that Umbraco refuses its member list (403) to a user without Members, so the picker opened empty for the
   very persona People is for. The search by address finds a member's entries too (ADR-0223), so the others find a
   member by the member's address, and the line for nobody found no longer points them to a search they cannot use.
7. **Pasting several questions selects none of them, and a new page is shown, not selected.** The editor's journey at
   375 px found the inspector of the last pasted question lying over the narrow canvas, so the questions just added
   were out of sight until it was closed; and Add page, meant to show the new page with the focus on its Add field,
   also selected the page, whose inspector then covered that very button. One pasted question is still selected, as a
   field from the palette is, to be edited at once; several are left to be looked over, the focus on the first of them;
   a new page waits for its first field. At every width, so the designer does not behave by the window's size.
8. **Esc in the entries list keeps the focus in the list.** It cleared the cursor and dropped the focus to the page,
   where the list's one-key shortcuts no longer reach (they act only while the focus is in the list, WCAG 2.1.4), so
   j and k did nothing until the user clicked back in. The focus now rests on the table itself, outlined while it does,
   and j or k starts again from the first or last row.
9. **The preview says when a content action keeps test entries from running their actions.** A content action whose
   rights the user lacks in Umbraco is refused for test entries too (ADR-0235), and the preview showed that as "the
   preview couldn't be loaded", or turned its switch back without a word. The note now says why test entries run no
   actions, and the switch and Run actions… go for the rest of the visit, as neither could work; the draft still shows.
10. **A rule on a yes or no question offers its two answers.** The rules builder asked the server for a choice's options,
    which a yes or no has none of, so it offered a box, and a typed "Yes" never matched the answer `yes`. The builder now
    lists the two answers as the field names them (the user's own words where it names none), the rule's words say them
    the same way, and the form check warns at a value that is none of a choice's own options (ADR-0179, amended).
11. **What the design assessments found, each fixed where it was.** The Overview's Recent entries box opens the Inbox,
    whose item may lie below a long forms tree. The erase dialog names a lone form in its sentence rather than repeat the
    counts in a row. A problem about a setting changed since the check answered goes with the change. Every element
    takes the section's tokens, which the Inbox's badge lacked in the dark theme (a source test holds it). An empty
    value in the insights reads "no data", never a dash. A configuration key wraps after its colons only. The folder's
    Permissions sidebar hears its area's heading and no longer shows it over Add user group. The insights table's cells
    sit closer in a narrow box, so the German table fits a 768 px window.

## Consequences

- A new scroll area needs `position` beside its `overflow`, or the source test names its file and line.
- On a phone, a column view's right part of the workspace stays empty when the page is panned fully to the right, where
  Umbraco's own view tabs are; the column itself is always whole at the workspace's start.
- `docs/ux/top-tasks.md` is now checked: a change of a path's clicks fails a browser test until the document says it.
- The preview has a state of its own for the refusal (`actionsRefused`, a ticket's `refused`, a run's `refused`); any other
  refusal still reads as a failure to load.
