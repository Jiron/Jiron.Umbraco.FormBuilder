# 0182. What the Phase 9 UX walk changed: the dark theme's labels and rings, the phone, and a designer that never shifts

- Status: accepted; supersedes the palette's fold on selection of [ADR-0170](0170-the-inspector.md) (its section "When
  the palette folds") and the one line of its closed section's summary, which now wraps, and builds on
  [ADR-0168](0168-the-canvas-draws-with-the-elements-views.md) (the canvas) and
  [ADR-0020](0020-three-design-skills.md) (the design reviews)
- Date: 2026-11-05
- Roadmap step: 9.6a

## Context

Step 9.6a walks every screen of the Form Builder section at 375, 768 and 1440 px, in Umbraco's light and dark themes
and in English and German (`tests/e2e/specs/ux/walk.spec.ts`, twelve combinations, about forty screens each), and has
the three design reviews of ADR-0020 read the contact sheets against ROADMAP §4.13. It also carries three open items:
the selected element's bar over the element above it (9.4c), axe in the dark theme for the section's buttons (9.4d),
and how the designer meets a phone (9.5f). What the walk measured:

1. **Umbraco's dark theme** labels its positive, default and danger buttons in `#eeeeef`: 4.37:1, 4.34:1 and 4.32:1
   on their fills, under AA. Its warning label is 3.2:1 on its fill (the state chip of a form with changes to
   publish). Its focus colour is 2.5:1 on its surface, and its `-standalone` danger and positive colours 2.5:1 there
   too; the high-contrast theme's warning is 1.3:1 on white. The light and high-contrast themes label filled buttons in
   white already.
2. **The phone.** Umbraco's own page is at least 920 px wide in every section, its content and settings included: at
   375 and 768 px the window scrolls sideways (545 and 152 px), and a workspace is 620 px wide. Nothing the section
   does can make Umbraco's page narrower.
3. **The bar.** A selected field's bar and handle stand 36 px above its frame (40 px for a group's), while the default
   theme leaves 24 px between fields and 16 px under a legend: the bar covered 12 px of the heading above, which a
   click then could not select.
4. **The inspector's column** came only with a selection, and between 700 and 1024 px the palette folded as it came:
   the canvas changed its width by 320 px (112 px at a 1024 px window) at every selection and every Escape, the field
   just clicked slid away, and the second click of a double-click, which edits a label in place, missed it.
5. Smaller things the reviews found: links in the copied actions' result in the browser's own blue (1.4:1 in the
   dark theme), the undo toast's words in Umbraco's grey, the muted label of an unpublished form on the open tree row
   (2.3:1), the overview's check marks in UUI's positive (2.5:1), radii of 8, 6 and 4 px on the canvas where the
   backoffice has two, an 11 px chip type, hover styles a finger leaves behind, presses that jump, a check sidebar
   whose "checking again" line pushes its list down, and screens that show their name, words and buttons one after
   the other as they are read.

The owner answers no questions during the run (CLAUDE.md rule 8); each choice below is this record's.

## Options

1. **The dark theme's labels and rings:** leave them to Umbraco (an upstream note); set the section's own colours on
   its own elements only; or set UUI's contrast tokens on the section's elements, so UUI's buttons inside them, and
   Umbraco's footer and dialogs drawn inside them, take them by inheritance.
2. **The phone:** a layout of the section's own for 375 px; the section's screens fitting Umbraco's 620 px workspace
   and adding no width of their own, held by a test; or leaving the phone out.
3. **The bar:** draw it inside the selected element's frame (over its own label); move the elements apart while one
   is selected; or give the canvas room of its own above every field and group, a little more than the visitor's page
   has.
4. **The inspector:** keep it coming with a selection and fold the palette with it (ADR-0170); fold the palette on
   selection up to 1700 px too, so the canvas shows the Desktop width more often (one review's proposal); or keep the
   inspector's column beside the canvas always, with a sentence until something is selected, and the palette on its
   rail whenever the view is 700 to 1024 px wide.

## Decision

1. **The section sets UUI's tokens where UUI falls short, on its own elements** (`styles/tokens.ts`):
   `--uui-color-positive-contrast`, `-default-contrast`, `-danger-contrast` and `-selected-contrast` white (5.0:1 in the
   dark theme, unchanged elsewhere), `--uui-color-warning-contrast` black (5.7:1). Its own focus ring is
   `--jfb-bo-color-focus`, UUI's focus colour mixed with its text 70 to 30 (4.1:1 on the dark surface, 6.8:1 light),
   which UUI's buttons, inputs, toggles, checkboxes, selects, menu items and references inside its elements take too.
   Text, icons and borders that say something failed, warns or holds use the three `-text` mixes, never UUI's
   `-standalone` colours. Umbraco's own tree rows, header and footer outside the section's elements keep Umbraco's
   colours (OWNER-TODO: notes to Umbraco HQ). `dark-theme.spec.ts` holds it: axe on every kind of filled button the
   section shows, and the state chip, the check badge's icon and four focus rings measured.
2. **The phone is Umbraco's 920 px page.** The section's screens fit the 620 px workspace it leaves, add no width of
   their own, cut no words and scroll nothing inside themselves; the overlay inspector keeps its tools inside itself.
   `narrowest.spec.ts` measures it at 375 and 768 px in German, the longer language. A layout for 375 px is not ours to
   make while Umbraco's page is 920 px.
3. **The canvas keeps room of its own:** 20 px above each field and group (`--bo-room`), so with the theme's 24 px
   between fields, 16 px under a legend and 32 px between groups the bar and handle never lie over the element above,
   and nothing moves when an element is selected. The canvas is a little airier than the visitor's page; its order,
   columns, widths and the rows of fields side by side hold. The insert point above a group's first field lies in that
   room, under a legend. `designer-bar.spec.ts` holds it in English and German: the bar's top below the heading's
   bottom, and a click on the heading selects it.
4. **The inspector's column is always beside the canvas above 700 px,** headed "Nothing selected" with one sentence
   and its fold until an element is selected; over a narrow canvas it still comes only for a selection. From 701 to
   1024 px the palette lies on its rail whenever the view is that wide, selected or not, its buttons opening it over
   the canvas; folding the inspector gives the palette back as the user left it. Selecting and clearing never change
   the canvas's width, so a double-click on a field edits its label. This replaces 9.4a's check ("selecting folds the
   palette, clearing opens it") with a stronger one: at a 1024 px window the canvas keeps its width and the field below
   its place when a field is selected and cleared, and a double-click on a field not selected edits its label in
   place. The fold on selection up to 1700 px is rejected: it moves the canvas again and adds a click per field while
   a form is built; who wants the Desktop width folds the palette or the inspector.
5. **The rest, each where it was found:** the copied actions' links in Umbraco's interactive colour, underlined; the
   undo toast's words in its contrast colour; the unpublished label on the open row in the row's colour; the
   overview's marks in the positive text mix; the canvas's radii Umbraco's two (`--uui-border-radius` and round); chips
   in Umbraco's 12 px small type, their colours and the sheet's named (docs/ux/tokens.md, "The designer's sheet");
   hover only where a pointer hovers (`@media (hover: hover)`); presses that sink or shrink over 120 ms
   (`--jfb-bo-motion-fast`, `--jfb-bo-motion-ease`), still with reduced motion; menus that open with a 120 ms fade and a
   slight growth from their corner and close at once; insert points and handles that fade in for the pointer and are
   there at once for the keyboard; the check sidebar's "checking again" beside Close; a dragged field's place in the
   sheet's colours; still skeleton lines while a box's words are read, a folder's screen waiting for its name and
   access with its list, and the canvas's sheet shape while its look is read. The state chip keeps its whole state up
   to 14rem and the form's name gives way first. The skip link to the form waits out of sight without a box until it
   takes the focus: moved above the view, its box lay over Umbraco's back button and the form's name, which axe then
   found too small to hit (`narrowest.spec.ts` asks axe for the section's targets). A closed inspector section's
   summary wraps instead of keeping to one line, so a rule's sentence keeps what the rule depends on, which an
   ellipsis cut (`designer-rules.spec.ts`).
6. **The words, in English and German** (docs/ux/copy.md): the copied actions' result names the form copied to once
   and lists the things each action names that the form lacks; the duplicate dialog says "this form" under a headline
   that names it, and stays as wide as a sentence; the import's summary gives every count one way, a label and a
   number; the trash confirmation says which pages show nothing; the Undo toast names what it brings back ("Email
   address deleted."); a rule says a part of the visit small inside its sentence ("Shown when the visitor is signed
   in"), in German role-neutral ("die ausfüllende Person"), and an option a source gives by the label the server
   resolved ("Yes", never "yes"); a grid's options are its columns ("Columns from", "Add column"); Change type ends
   in "…", as it asks before it changes; the empty form, the Depends-on help, Move to's help, the narrowed width's
   German and the test entry's step say what is true at every width and moment; German says "Theme" and
   "Validierung", as the vocabulary has them; the create dialog's descriptions fit its one line (44 characters); the
   consent's captcha switch has its line of help. Whoever may not design reads the form's name as the screen's
   heading, not as a box. The copy-actions dialog is headed "Copy actions", as Umbraco's header cuts a headline to one
   line, and the form copied from heads its actions ("Actions from Spring").
7. **The keyboard keeps its place:** the toolbar's "…" opens with the focus on its first item it can do; the palette's
   fold and unfold leave it on the rail and on the search; Load more asks once, waits visibly and hands the focus to
   the first new row; a row restored or deleted from the bin leaves it on the bin's heading.

## Consequences

- The section meets AA in Umbraco's dark theme for its buttons, its labels, its status colours and its focus rings;
  what remains is Umbraco's (three upstream notes in OWNER-TODO).
- The phone is held by a test that measures it, not by a layout of the section's own.
- The designer never shifts under the pointer: the canvas is 20 px airier per element than the visitor's page, and at
  a 1024 px window it is always 348 px wide beside the inspector's column, where it was 460 px until a selection.
- IA 5.1 and docs/ux/tokens.md say all of this; the walk and its contact sheets come back at every phase review.
- Rejected: the views' fold in the form's header and its second "…" (Umbraco's workspace header), renaming "More
  options" (§4.13 names it), the badge counting suggestions (ADR-0181), Preview and a summary of changes in the publish
  dialog (10.3), and "Learn more" links (17.2a, which writes the pages they lead to; roadmap change, see WORKLOG).
  Also kept as they are: Change type in the overlay inspector's header beside the type (ADR-0174 shows it once, there,
  in every way the inspector shows; the bar keeps its own row), names bare in headlines ("Move Email address", as
  copy.md and Umbraco write them), "Add a rule" in the inspector's line and "Add rule" in the overview's header (IA
  5.1 names both), an editable name in the input's look and a fixed title bold, box headings by their level, and the
  bin's rows with their "…" where a folder's rows have none (a trashed item has no screen whose menu could stand in).
  Move to's and Change type's Cancel give the focus to the element, as ADR-0169 and ADR-0174 decided, not back to
  what opened them: from the element's "…" that is a menu item gone with its menu. A rule's chip on the canvas keeps
  to one line on the element's edge and is cut where the element is narrow, as ADR-0178 decided: it says its rule in
  full as the description of the button that selects the element, and the inspector says it whole. The open row of the
  forms tree keeps Umbraco's colours (4.34:1 in the dark theme) and UUI's menu item its accessible name, both Umbraco's
  (OWNER-TODO).
