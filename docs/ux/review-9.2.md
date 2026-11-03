# Step 9.2: the design skills' review

ROADMAP 9.2 ships the form library: the trees' marks, Umbraco's create dialog with its four options, the screens of a folder, the
top of Forms and the Recycle Bin, each item's "...", the template gallery, the import, the duplicate dialog, copying actions, and
trash, restore and delete with their confirmations ([ADR-0166](../decisions/0166-the-form-library.md)). Per ADR-0019 and ADR-0020
the three design skills ran on the result; the design itself comes from Phase 2 ([information-architecture.md](information-architecture.md),
sections 3 and 4, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice was
decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the administrator, with folders, a draft, a
restricted form (through the test host's `form-restriction`, ADR-0166), a trashed form and a page that picks a form, made through
the APIs.

| Shot | What it shows |
| --- | --- |
| [the tree](review-9.2/tree-1440.png) | a draft muted and said once to screen readers, a lock on a restricted form and folder, the Recycle Bin last |
| [a folder](review-9.2/folder-1440.png) · [at 920 px](review-9.2/folder-920.png) | name, entries of the last 30 days, last changed and by whom; Create form in the footer |
| [an empty folder](review-9.2/folder-empty-1440.png) | one sentence and Choose a template…; the footer keeps Create form |
| [a folder's "..."](review-9.2/row-menu-1440.png) · [a form's "..."](review-9.2/tree-form-menu-1440.png) | Create… and Move to… (Rename… and Delete… live in the tree, Delete… only when empty); Duplicate… beside the form's "..." |
| [the create dialog](review-9.2/create-dialog-1440.png) | Umbraco's own: Form, Form from a template…, Import a form…, Folder…, each with one line |
| [the gallery](review-9.2/gallery-1440.png) · [at 920 px](review-9.2/gallery-920.png) | the cards as one radio group, the preview beside from 1000 px, Preview in the footer below |
| [the import](review-9.2/import-checked-1440.png) | the file, what it holds, what this site lacks, and where the draft arrives |
| [duplicating a restricted form](review-9.2/duplicate-restricted-1440.png) | the copy's name, and the one sentence on the access it gets |
| [copying actions](review-9.2/copy-actions-1440.png) · [the result](review-9.2/copy-actions-result-1440.png) | the target, the actions; then each action with what the target lacks and its link (the shot is from before the fix below) |
| [trash, used on a page](review-9.2/trash-in-use-1440.png) · [trash with Undo](review-9.2/trash-undo-toast-1440.png) | the page named before anything goes; a form no page picks goes at once |
| [the Recycle Bin](review-9.2/bin-1440.png) · [empty](review-9.2/bin-empty-1440.png) | trashed when and by whom, a row's "...", Empty recycle bin… as the one action; the empty bin's one sentence without a footer |
| [restore](review-9.2/restore-1440.png) · [delete for good](review-9.2/delete-for-good-1440.png) · [empty the bin](review-9.2/empty-bin-1440.png) | where it comes back; what goes for good, with numbers |
| [the header search](review-9.2/search-field-1440.png) | fields across forms by label, "Email address, in Contact" |
| [dark](review-9.2/folder-dark-1440.png) · [high contrast](review-9.2/folder-high-contrast-1440.png) | Umbraco's two other themes |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA, the whole page, what lies inside the library's elements counted) on a folder, an empty folder, the
  gallery, the import sidebar with notes, the duplicate dialog, the copy result, the Recycle Bin and the Restore sidebar, in
  `library.spec.ts`: none, after the heading levels below.
- **The bundled detector** (`impeccable detect --json`) on the fourteen elements and sidebars: `[]`.
- **The journeys** of `library.spec.ts`, one per action of the roadmap, with the Recycle Bin's run one after another.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors who keep a site's forms, calm and native, so the
skill's landing-page rules (variance, motion, display type) don't apply, and UUI's components and tokens are the system (the
roadmap wins, per ADR-0019). Dials: variance 2, motion 2, density 5. What it still asks held: one accent (Umbraco's), the one
radius scale, no pure-black shadow, every state composed (loading in the list's shape, failed with Try again, empty with its next
step), no spinners.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against the IA and copy.md:

| Finding | What was done |
| --- | --- |
| The gallery said a template's description twice, on the checked card and above the preview beside it | beside the cards the preview says only what the card does not ("1 page, 3 fields"); alone, below 1000 px, it names and describes the template |
| The checked card's radio was a thick ring, which reads as unchecked | a dot inside the ring, as a checked radio has |
| The copy result repeated a sentence for every place an action names a field, and said the actions "stay off", which they don't (ADR-0046: publishing refuses a missing field until it is fixed) | each sentence once per action; "Fix them before you publish …" |
| The Restore sidebar's dismiss read Close, which copy.md keeps for sidebars that change nothing; and "pages that pick it show it again" was said of every form | Cancel; the sidebar says where the item comes back and what comes with it (a folder's contents, a form's entries), and nothing for a form without entries |
| The Recycle Bin's own tree item was muted like a draft | only a live form without a published version is muted |
| "Choose a file…" was a primary button inside the import sidebar, beside the footer's primary | an outline button; Import as a new copy is the one primary |
| The create action read "Create……" | Umbraco's own label, whose kind adds the ellipsis |
| The sidebars' box headings came out as `h5` and the gallery's groups as `h2` under the sidebar's `h3`, and a screen's `h1` stood above it (axe: `heading-order`) | a screen's title `h2`, the sidebar's own `h3`, everything in it `h4`, the boxes through `headline-variant` |

**"Said twice?"** per screen (§4.13):

- **A folder:** its name is in the header only (the tree's highlight is Umbraco's); a form's marks (draft, restricted, new entries)
  are in the tree and not again in the list; "Today, by Ana Novak" is one cell, not two columns.
- **The Recycle Bin:** when and by whom in one column; Restore… and Delete… in the row's "..." and nowhere else.
- **The gallery:** name and description on the card, what it holds and the drawing beside it.
- **The import:** the file's name, size and date in its card; what it holds in one box; what the site lacks and what comes along
  in two, never mixed.
- **Confirmations:** the headline names the item, the text says what is lost and the pages that pick it, the button repeats the
  action ("Move to Recycle Bin", "Delete form", "Empty recycle bin").

**At most three secondary actions, one primary:** a folder has Create form, its header's "..." two items; the Recycle Bin has
Empty recycle bin… alone; every sidebar has Cancel and its one primary. **Nothing squashed:** at 920 px the folder list keeps its
three columns at a readable width, and the gallery shows one column with Preview in the footer.

## emil-design-eng

The library is used many times a day, so rows, the tree and the keyboard do not animate; the modals and toasts are Umbraco's, with
its own motion. Details reviewed:

| Before | After | Why |
| --- | --- | --- |
| Gallery cards with no feedback on press | `transform: scale(0.99)` on `:active`, colour transitions of 120 ms, hover only where a fine pointer hovers, none under reduced motion | a card is pressed, not just clicked; a touch screen keeps no stuck hover |
| A checked radio drawn as a 5 px ring | a 1 px ring with a 3 px gap and a filled dot | the state reads at a glance, as a native radio's does |
| A skeleton of one size for any list | two bars inside the list's own box | nothing jumps when the rows come |
| Undo in a toast of Umbraco's six seconds | ten seconds | time to read the sentence and reach the button |

## What the look found besides the design

The browser found five faults no unit test could: the library's workspaces were never registered as elements, so their screens
never drew (a manifest test now constructs every workspace's element); every refusal reached the screens without its code,
because Umbraco's client reads a problem only with a `type` (the management API now writes `about:blank`, ADR-0166); Umbraco's
router took the export's download link for a navigation (the link no longer joins the page); Undo restored the form but reloaded
nothing, because Umbraco had let go of the action (the action now hands Undo what it held); and a cancelled dialog behind a screen's
button left an unhandled rejection.

## What stays open, and where

- **Renaming a form** is the form's header, with 9.3a; **Save as template…** comes with 12.3 and **Permissions…** with 12.4a.
- **The copy result's links** open the target with the action selected (`?action=`), which 10.2's Actions view honours.
- **The gallery's preview** draws plain controls until 9.3b's preview elements take its place.
