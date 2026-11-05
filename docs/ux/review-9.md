# Phase 9 review: the UX walk

Step 9.6a walks every screen of the Form Builder section as Phase 9 built it, the library, the designer and their
dialogs, at three widths, in Umbraco's light and dark themes and in English and German, and reads the result against
ROADMAP §4.13 with the three design skills of ADR-0020. It carries three items of earlier steps: the selected element's
bar over the element above it (9.4c), axe in the dark theme for the section's buttons (9.4d), and how the designer meets
a phone (9.5f). What changed is decided in [ADR-0182](../decisions/0182-the-phase-9-ux-walk.md).

## How the walk works

`tests/e2e/specs/ux/walk.spec.ts` (run with `JFB_E2E_UX=1`, project `ux`, then `ux-sheets`) seeds its own folders and
forms on the test host and visits about forty screens in twelve combinations: 375 × 812, 768 × 1024 and 1440 × 900,
light and dark, English and German. It finds its way by tags, `data-mark` and icons, never by words, so the German
walk takes the same path. For each shot it keeps axe's WCAG 2.2 A and AA findings inside the section's own elements,
the page's sideways overflow, boxes that scroll inside themselves and words cut by an ellipsis; `ux-sheets` puts each
screen's twelve shots on one contact sheet with those numbers under each.

## What was looked at

The last walk ran after the fixes (`.local/ux/9.6a-4`, not committed: twelve combinations of about forty screens).
These shots stand for it; "before" is the walk before the fixes.

| Shot | What it shows |
| --- | --- |
| [Overview](review-9/overview-1440.png) | the checklist with its dashed open steps, the health line |
| [Design view](review-9/design-1440.png) | the palette, the canvas with its room above each field, "Nothing selected" beside it |
| [A field selected](review-9/selected-1440.png) ([before](review-9/selected-before-1440.png)) | the bar and handle in the room above the field, the field above whole |
| [Dark](review-9/selected-dark.png) | the same in Umbraco's dark theme: white labels on the filled buttons, the sheet white |
| [A grid](review-9/grid-1440.png) | "Columns from" and "Add column" |
| [Rules overview, German](review-9/rules-overview-de.png) | rules said with the visit small and role-neutral |
| [Rule builder on a phone](review-9/rule-builder-phone-de.png) ([before](review-9/rule-builder-phone-de-before.png)) | one column inside the sidebar, nothing past its edge |
| [Palette at 768 px, dark](review-9/palette-768-dark-de.png) | the palette over the canvas |
| [Form check, dark](review-9/form-check-dark-de.png) | the icons in the text mixes, "checking again" beside Close |
| [Publish, blocked](review-9/publish-blocked-dark.png) | what to fix, nothing of the version, no comment box |
| [Undo toast, dark](review-9/undo-toast-dark.png) | "Email address deleted." in white on the positive fill |
| [Copied actions, dark](review-9/copied-dark.png) ([before](review-9/copied-dark-before.png)) | the form named once, the links in Umbraco's interactive colour |
| [Duplicate, German](review-9/duplicate-de.png) | "Dieses Formular …" under the headline that names it, as wide as a sentence |
| [Import](review-9/import-1440.png) | every count a label and a number |
| [Gallery on a phone](review-9/gallery-phone-de.png) ([before](review-9/gallery-phone-de-before.png)) | Preview under the card, the footer's two buttons in one row |
| [A folder on a phone, dark](review-9/folder-phone-dark-de.png) | the list in Umbraco's 620 px workspace, nothing cut |
| [Recycle Bin](review-9/recycle-bin-768.png) | the rows wrap beside icons that keep their size |
| [Read only](review-9/read-only-1440.png) ([before](review-9/read-only-before-1440.png)) | the name as the screen's heading, no box |
| [Create options on a phone](review-9/create-options-phone-de.png) | each description in one line |

## What the walk measured

- **The phone.** Umbraco's own page is at least 920 px wide in every section, its content and settings included: at 375
  and 768 px the window scrolls sideways (545 and 152 px), and a workspace is 620 px wide. The section's screens fit
  that workspace and add no width of their own (ADR-0182, decision 2); `narrowest.spec.ts` holds it in German.
- **The dark theme.** Umbraco's dark theme labels its positive, default and danger buttons in `#eeeeef` (4.37:1,
  4.34:1, 4.32:1 on their fills), its warning in the same grey (3.2:1), and its focus colour and `-standalone` danger
  and positive are 2.5:1 on its surface.
- **The bar.** A selected field's bar and handle stand 36 px above its frame; the default theme leaves 24 px between
  fields and 16 px under a legend, so the bar covered the heading above by 12 px, which a click then could not select.
- **The inspector.** It came only with a selection, and between 700 and 1024 px the palette folded as it came: the
  canvas changed its width at every selection and every Escape, the field clicked slid away, and the second click of a
  double-click missed it.

## design-taste-frontend

Reading this as: an operator's tool inside Umbraco's backoffice, for editors and designers who build forms all day, in
Umbraco's own language (UUI, its tokens and patterns), leaning toward restraint: the dials sit low (variance 3,
motion 3, density 5), since the brief is a native, calm tool and not a page of its own.

| Finding | What was done |
| --- | --- |
| The canvas's controls used radii of 8, 6 and 4 px, where the backoffice has two | Umbraco's control radius and the round one, nothing between (tokens.md, "Corners") |
| Chips in an 11 px type of their own and colours picked by hand | Umbraco's 12 px small type; the chips' and the sheet's colours named and documented ("The designer's sheet") |
| A dragged field's place drew a dark slab on the white sheet in the dark theme | the sheet's own grey for a field's or a group's place; a page's among the steps keeps Umbraco's |
| The overview's open steps drew an empty ring that reads as a radio button | a dashed ring: a step still to do, not a choice |
| Two items of the sidebar shared the envelope | Email layouts has the layout icon; a test keeps every item's icon its own |
| Text-only "Loading…" in three dialogs and the trashed item's screen, a spinner on the canvas | still skeleton lines in the shape of what comes, and the canvas's sheet with bones (tokens.md, "Waiting") |
| Move to's places in the browser's own radios, white in the dark theme | drawn as the template gallery draws its choice, in the theme's colours, the system's in forced colours |

## impeccable

The bundled engine (`impeccable.exe`, no downloads, hooks or live mode) read the section's markup and found nothing it
names; it cannot see colours mixed at run time, so the contrast findings came from the walk's axe runs and from
measuring the dark theme's values.

| Finding | What was done |
| --- | --- |
| Filled buttons' labels at 4.3:1 in the dark theme, the footer's Save draft and Publish… among them | the section sets UUI's contrast tokens to white on its elements (5.0:1), Umbraco's footer and dialogs inside them included |
| The state chip of a form with changes to publish, `#eeeeef` on the warning's fill, 3.2:1 | black, 5.7:1, as the other two themes have it |
| Focus rings at 2.5:1 on the dark surface | `--jfb-bo-color-focus`, UUI's focus mixed with its text (4.1:1 dark, 6.8:1 light), for the package's rings and UUI's controls inside its elements; the sheet's controls take a fixed ring, as the sheet is white |
| Text and icons in UUI's `-standalone` danger, warning and positive (2.5:1 dark; the warning 1.3:1 on high contrast's white) | the three `-text` mixes, a warning one added |
| The copied actions' links in the browser's own blue (1.4:1 dark), the undo toast's words in Umbraco's grey, the muted name of an unpublished form on the open tree row (2.3:1), the overview's check marks at 2.5:1 | Umbraco's interactive colour underlined, the toast's contrast colour, the row's own colour, the positive text mix |
| The selected field's bar over the heading above it | the canvas keeps 20 px of room of its own above each field and group (ADR-0182, decision 3) |
| The canvas changed its width at each selection (701 to 1024 px) | the inspector's column stays, with "Nothing selected" until a selection; the palette lies on its rail at those widths (decision 4) |
| The rule builder ran 73 px past a phone's sidebar | one column that never grows past it, its lists cut inside their own boxes |
| The gallery's footer wrapped "Use template" under the window's edge at 375 px in German | Preview moves under the cards, the footer keeps two buttons; the search shows from six templates on |
| The create dialog's descriptions cut at 375 px | each in one line of 44 characters at most, a test holding both languages to it |
| The skip link waited above the view, its box over Umbraco's back button and the form's name, which axe then found too small to hit on every designer screen | out of sight without a box until it takes the focus; the narrowest journey asks axe for the section's targets |
| A closed Rules section's summary cut its rule before what it depends on ("Required when How likely are you …") | the summary wraps (IA 5.1); the rules journey reads it whole |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| `:hover` styles a finger leaves behind | inside `@media (hover: hover)` | a tap on a phone leaves no grey or dashed outline on what was tapped |
| `:active { transform: scale(0.98) }` without a transition, on wide places to add a field too | `transition: transform 120ms` on the strong ease-out; wide places sink 1 px, buttons shrink to 0.97 | a press should answer, and a wide bar scaled reads as a jump |
| no press on the widths, the check badge, Add page | the same 120 ms press | every button answers the same way |
| menus that appeared in one frame | a 120 ms fade and a slight growth from their corner as they open, none as they close | it says where the menu came from; closing at once keeps a frequent action fast |
| insert points fading in for the keyboard too, handles that popped in | a fade for the pointer, there at once for the keyboard | a keyboard action is never animated |
| a switch's track at 120 ms and its knob at 160 ms | both 120 ms (`--jfb-bo-motion-fast`) | one timing for one control |
| the palette's fold and unfold put the focus before the panel was drawn, so it fell to the page | the focus waits for the panel | a lost focus makes a keyboard user start again from the top |
| the form check's "checking again" line pushed the list down at every pause | beside Close in the footer | the list never moves under the reader |
| Change type's dialog resized with each type's preview, so the centred list moved under the arrow keys | room for most previews | the list stays where the eye is |
| a folder's name, its words and its buttons arriving one after another | one skeleton until the name and the user's access have come with the list | one change instead of three |

With reduced motion nothing moves or grows; fades keep their time (tokens.md, "Motion").

## The words

Read in both languages against docs/ux/copy.md:

- The copied actions' result named the form copied to about ten times; it names it once, then lists what each action
  names that the form lacks.
- The duplicate dialog said the form's name in its headline and again in its sentence; the sentence says "this form".
- The import's summary had three ways to count; every count is a label and a number.
- "They show nothing in its place until you restore it" mixed the pages and the form; "Those pages show nothing in its
  place until you restore the form."
- The Undo toast said "Field deleted."; it names what it brings back.
- A rule read "Shown when The visitor is signed in"; a part of the visit is said small inside a sentence, and in
  German role-neutral ("die ausfüllende Person"). An option a source gives read as its value ("yes"); it reads as the
  label the server resolved.
- A grid's columns were "Options from" and "Add option"; they are "Columns from" and "Add column".
- The empty form said "pick a type on the left", false at 620 px; "Add your first field with Add field below".
- German: "Design" for theme and "Prüfung" for validation, against the vocabulary; "Theme" and "Validierung". "An den
  Platz angepasst" said the form was scaled, which it never is; "Schmaler als gewählt". The Depends-on help said "this
  field" for the other one.
- The read-only user got an input-looking name with the focus in it; whoever may not design reads it as the heading.
- The copy-actions dialog's headline named the form copied from, and Umbraco's one-line header cut it at 375 px; it
  says "Copy actions", and the form copied from heads its actions ("Actions from Spring").

## Kept as they are

- The form header's views and its second "…" are Umbraco's workspace header.
- "More options", Change type beside the type in the overlay inspector (ADR-0174), "Add a rule" in the inspector's
  line and "Add rule" in the overview's header (IA 5.1), names bare in headlines as copy.md and Umbraco write them.
- The badge doesn't count suggestions (ADR-0181). Preview and a summary of changes come with 10.3; "Learn more" links
  with the pages they lead to, in 17.2a.
- A rule's chip on the canvas, cut where the element is narrow (ADR-0178): one line on the element's edge, its rule
  said in full as the description of the button that selects the element and whole in the inspector.
- The forms tree's open row in Umbraco's colours, and `uui-menu-item`'s accessible name: Umbraco's (OWNER-TODO).

## The §4.13 checklist, screen by screen

One primary action where Umbraco puts it, at most three secondary actions in sight, each fact said once, nothing
squashed at the 620 px workspace, and the screen's states. "Umbraco's" means the part is Umbraco's own frame.

| Screen | Primary | Secondary in sight | Said twice? | At 620 px | States |
| --- | --- | --- | --- | --- | --- |
| Overview | the checklist's next step | the health line's "…" | no: the count once, beside the heading | the steps' buttons under their text | skeleton, failed with Try again, complete (checklist on request) |
| A folder | Create form (footer) | Choose a template… when empty; the "…" | no: the name is the screen's title only | Last changed hides below 520 px of list | skeleton until the name and access come, empty with a template, failed with Try again |
| Folder name | Create folder | Cancel | no | the dialog's width | blank refused with words |
| Create options | Umbraco's dialog | — | no | descriptions in one line | — |
| Template gallery | Use template | Cancel; Preview under the cards when alone | no: the card's name once, the preview names it only alone | Preview under the cards, the footer in one row | skeleton cards, failed, no match with Clear search |
| Import | Import as a new copy | Cancel, Choose another file… | no: each count once, after its label | one column | reading the file, refused with why, what it lacks and brings |
| Duplicate | Duplicate | Cancel | no: the headline names the form, the sentence says "this form" | 520 px at most | counting the actions waits, blank refused |
| Copy actions | Copy 2 actions | Cancel, Choose another form… | no: the form copied from heads its actions, the headline names none | one column, the headline never cut | skeleton, no actions, the result with a link each |
| The copied result | Open the form | Close | no: the form named once | one column | — |
| Trash confirm | Move to Recycle Bin | Cancel | no | Umbraco's dialog | names the pages that show nothing |
| Recycle Bin | Empty recycle bin… | a row's "…" | no | the rows wrap beside their icons | skeleton, empty, failed; the focus on the heading after a row leaves |
| Restore | Restore | Cancel | no | Umbraco's dialog | skeleton |
| The Design view | Publish… (footer) | Save draft; the toolbar's widths, language, badge and "…" | no: the state chip once, the badge once | the palette and inspector over the canvas | the sheet's skeleton, failed with Try again |
| A field selected | — (the bar's tools) | Move to…, Duplicate, Delete, "…" on the bar | no: chips step back while selected | the bar in the inspector's header, icons only under 430 px of sheet | — |
| Nothing selected | — | the inspector's fold | no | — | "Nothing selected" with how to choose |
| Palette search | — | — | no | over the canvas | no match |
| Inspector sections | — | Change type…, fold, close | no: a closed section's summary, wrapped | over the canvas | — |
| Options, grid | — | Add option / Add column, Paste… | no | one column | none yet |
| Rich text | — | the toolbar's buttons as one stop | no | one column | — |
| Rule builder | Add / Update | Cancel, Try it | no | one column that never grows past the sidebar | each row's problem in words |
| Rules overview | Close | Add rule | no | one column | no rules yet |
| Change type | Change type | Cancel | no | room for the preview | failed with words |
| Move to | Move | Cancel | no | one column | refused places with why |
| Keyboard shortcuts | Close | — | no | — | — |
| Form check | Close | Fix it per row | no: counts in the box headings only | Fix it under each sentence | checking, checking again beside Close, failed with Try again, clear |
| Publish | Publish | Cancel | no: the check's result once | the dialog's width | checking, errors (no version text), warnings, clear |
| Blank form | — | Add field, paste your questions | no | the sentence true at every width | — |
| Undo toast | Undo | — | no: names what it brings back | Umbraco's corner | — |
| Read only | — | Entries, Insights views | no: the name as the heading | — | the coming screen |

## What the journeys found

The carried items each have a journey that measures them (ROADMAP 9.6a, Done when):

- **The bar** (`designer-bar.spec.ts`, English and German): a heading above a text field; with the field selected, the
  bar's top lies at or below the heading's bottom, and a click on the heading's words selects the heading. Before the
  room, the bar's top was 12 px above the heading's bottom.
- **The dark theme** (`dark-theme.spec.ts`): axe finds nothing inside the section's own elements with every kind of
  filled button in sight (a folder's, the Design view's toolbar, selected element and footer, the undo toast, the
  publish, duplicate and copy dialogs, the copied actions' links, the recycle bin), the open unpublished row's label
  takes its row's colour, and four focus rings, the check badge's icon (3:1) and the warning chip's label (4.5:1) are
  measured. Its first run found that a form opened by its address inside a closed folder has no tree row to check:
  Umbraco opens no folder for it, so the journey opens a form at the top level.
- **The phone** (`narrowest.spec.ts`, German, 375 and 768 px): the page is exactly as wide as Umbraco's own Content
  section, and inside the section's own elements no text is cut by an ellipsis and no box scrolls sideways, on the
  Design view with a field selected, a folder's list and the template gallery; the overlay inspector keeps each of the
  element's tools inside itself, and the gallery's footer keeps its two buttons in one row inside the window.
- **The inspector's column** (`designer-inspector.spec.ts`, a 1024 px window): "Nothing selected" beside the canvas,
  the palette on its rail; selecting a field and clearing it leave the canvas's box and the next field's box as they
  were, and a double-click on a field not selected edits its label in place.

The whole backoffice project ran after the fixes: 67 of 70 passed at once. The three failures were a rule's sentence
the walk had changed ("and the query value ref is …"), a test's own locator, and one real conflict: the walk's motion
review asked that a cancelled Move to or Change type give the focus back to what opened it, but ADR-0169 and ADR-0174
had decided on the element, and from the element's "…" the opener is a menu item gone with its menu. The change was
taken back.

After the last walk's fixes the four journeys they touch ran again: 27 of 28 passed at once. The one failure was the
narrowest journey's own: at 768 px it measured the gallery's footer while Umbraco's sidebar was still sliding in from
the window's edge, 39 px out. It now measures where the sidebar comes to rest, and passed four runs of four.

## What the last walk found

The walk ran three times: before the fixes (`9.6a-2`), after them (`9.6a-3`), and once more after what that run found
(`9.6a-4`), 468 shots each. Inside the section's own elements:

| Measure | Before | Last | What remains, and whose |
| --- | --- | --- | --- |
| axe colour contrast | 142 shots | 20 | the forms tree's open row in the dark theme, `#eeeeef` on Umbraco's `#316dca` (4.34:1): Umbraco's colours, kept (ADR-0182, OWNER-TODO) |
| axe label in name | 458 | 468 | `uui-menu-item` names its own box "menuitem" on every tree row, and the tree is in every shot: Umbraco's (OWNER-TODO) |
| axe target size | 0 | 0 | the run between found 156: the skip link's box over Umbraco's back button and the form's name, now without a box until it takes the focus |
| axe region | 12 | 0 | — |
| words cut by an ellipsis | 96 | 16 | rule chips on the canvas where the element is narrow (ADR-0178); the run between also found a closed Rules section's summary and the copy-actions headline, both whole now |
| boxes that scroll inside the section | 4 | 0 | — |
| the page's sideways scroll | 545 px at 375, 152 px at 768 | the same | Umbraco's 920 px page, as in every section (ADR-0182, decision 2) |

## The tamper round

In a throwaway clone of `58e136a` (ADR-0117), 31 changes each took back one of the fixes, one at a time, after every
check had passed untampered, each run under a time limit and the file restored after it. All 31 made a named test fail;
none was missed, hung, or stopped without a failed test. They took back the column's and the rail's rules and the
focus after a fold (5), the inspector's "Nothing selected" (2), the dragged place's colours, the sheet's skeleton and
the toolbar menu's focus (4), a source's option by its label and the visit's words inside a sentence (5), the check's
line beside Close and the publish dialog's comment (3), a grid's words (2), Load more (2), the test entry's help (2),
the skeleton lines (2), the sidebar's icons and three texts (4).

The colours, rings and room are CSS that jsdom does not lay out, so no tamper of this round reaches them: the browser
journeys above hold them by measuring in a real browser, the bar's place, axe in the dark theme, the rings' contrast and
the page's width.
