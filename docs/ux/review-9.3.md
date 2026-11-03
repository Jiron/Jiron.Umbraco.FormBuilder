# Step 9.3b: the design skills' review

ROADMAP 9.3b ships the designer's canvas and palette: pages as steps, groups with their columns, every field drawn as the website
draws it, insert points, the three canvas widths, the palette with its search, groups and recently used types, labels edited in
place, and questions pasted as fields ([ADR-0168](../decisions/0168-the-canvas-draws-with-the-elements-views.md)). The template
gallery's preview now draws with the same models and views. Per ADR-0019 and ADR-0020 the three design skills ran on the result; the
design itself comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.1, the
[designer wireframe](wireframes/designer.html) and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md
rule 8), so every choice was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a contact form, a blank form
and a form holding the server's sink (a field of every built-in type in two columns, and a repeatable group holding one of each),
made through the APIs.

| Shot | What it shows |
| --- | --- |
| [a new form](review-9.3/empty-form-1440.png) | one sentence pointing to the palette and to pasting questions, Add field, and Send |
| [pasting questions](review-9.3/paste-1440.png) · [the result](review-9.3/pasted-1440.png) | one line per question, the guessed types in a table before anything is added; the five fields, the last one selected |
| [an empty later page](review-9.3/empty-page-1440.png) | the new page selected with its Duplicate and Delete, its own sentence, Send and Back |
| [the contact form](review-9.3/designer-1440.png) · [a field selected](review-9.3/selected-1440.png) · [in two columns](review-9.3/selected-columns-1440.png) | the default theme on a white sheet, the steps, the width switch; the selected field's outline and its two tools |
| [an insert point](review-9.3/insert-point-1440.png) | "Add a field here" on the line between two fields, on hover and on focus |
| [a label in place](review-9.3/label-in-place-1440.png) | the label's box where the label was, its text chosen |
| [the canvas's "…"](review-9.3/menu-1440.png) | Undo and Redo with their shortcuts, off while there is nothing to undo |
| [Phone width](review-9.3/phone-width-1440.png) | the form at 375 px, its button across the width as on a phone |
| [After sending](review-9.3/after-sending-1440.png) | the form's message as visitors see it, with Change |
| [the palette folded](review-9.3/palette-rail-1440.png) | the rail with its expand button and search; the canvas takes the room, and "Narrowed to fit" goes |
| [Delete with Undo](review-9.3/delete-undo-1440.png) | the toast, and the selection on the next field |
| [every field: choices](review-9.3/sink-choices-1440.png) · [rows](review-9.3/sink-rows-1440.png) · [files](review-9.3/sink-files-1440.png) · [a repeatable group](review-9.3/sink-repeater-1440.png) | two columns whose labels, help and controls line up; the drop frame and the pad as visitors with JavaScript see them; the group's first row with its own fields |
| [at 920 px](review-9.3/designer-920.png) · [the palette over the canvas](review-9.3/palette-over-920.png) | the narrowest workspace: the canvas alone; the palette opened from Add field over a scrim, with the insert point it will fill |
| [dark](review-9.3/designer-dark-1440.png) · [high contrast](review-9.3/designer-high-contrast-1440.png) | Umbraco's two other themes around the visitor's white sheet |
| [the gallery](review-9.3/gallery-1440.png) | a template drawn by the same preview as the canvas |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA, the whole page, what lies inside the designer's elements counted) on a canvas of three pages, the
  Paste questions dialog and the palette over the canvas at 920 px, in `designer.spec.ts`, and on the gallery in `library.spec.ts`:
  none.
- **The bundled detector** (`impeccable detect --json`) on the canvas, a canvas field, their styles, the palette, the paste dialog,
  the Design view and the preview: `[]` for each.
- **The journeys** of `designer.spec.ts`: a form of three pages built with the mouse, five fields from a pasted list, fields
  side by side each selected by a click, and the narrowest workspace with its skip link, its palette over the canvas and
  Delete with Undo; the gallery's journeys in `library.spec.ts`.
- **The themes' specs** lay the server's sink out in the default and the bootstrap5 theme and check that fields side by side share
  their rows (the finding below).

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors building a site's forms, calm and native; the
canvas itself is the visitor's form, so its look is the default theme's, not a new one. The skill's landing-page rules (variance,
motion, display type) don't apply, and UUI's components and tokens are the system for everything around the sheet (the roadmap wins,
per ADR-0019). Dials: variance 2, motion 2, density 5. What it still asks held: one accent (Umbraco's), the one radius scale, no
pure-black shadow, every state composed (a new form, an empty page, a group without fields, a field visitors don't see, the
outcome), and no spinner where the shape is known.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1, the wireframe and copy.md:

| Finding | What was done |
| --- | --- |
| The insert point's "+" was clipped by the line's own box | the words and the icon sit in a pill of their own on the line |
| An insert point showed only for `:focus-visible`, so one opened by a click disappeared while the palette was open for it | it shows on hover, on focus and while the palette is open for it |
| The skip link past the palette was a link, and Umbraco's router took its click for a move: it opened Content | a button that moves the focus to the form |
| The palette never opened over the canvas: its threshold was 620 px, and Umbraco's own header keeps the view at 620 px or wider | up to 700 px the palette opens over the canvas, so the narrowest workspace (920 px window) gets it, as IA 5.1 says; IA 5.1 now names the range |
| A file field and a signature showed the look visitors get without JavaScript (a bare file input running past its frame; a typed name) | the canvas and the gallery show them as the website's runtime leaves them: the drop frame, and the pad with "Type your name instead" |
| An image whose picture the user may not open (no Media section) said "no picture chosen yet" | "Image: its picture can't be shown here"; a user who may open it sees the picture |
| In two columns, fields beside a field drawn as a fieldset (choices, a rating, a signature) drifted by rows, and a list stretched to the height of a drop frame beside it. The website does the same: the fieldset took two of its eight shared rows | both themes give a fieldset all its rows and keep each part at its own height; the themes' specs lay out the server's sink and check it |

Kept as they are:

- **"Narrowed to fit" at a 1440 px window.** With the tree and the palette open the canvas has about 810 px, less than Desktop's
  960; IA 5.1 says so rather than scaling the form, and folding the palette gives the width back (the rail shot).
- **The tools on the selected field's top edge** cover the corner of the field above, as in the wireframe. Inside the outline they
  would cover a long label instead.
- **A repeatable group in a half-width column** leaves its row's fields about 180 px each (the sink's repeater). That is the website's
  layout too; the canvas shows it honestly.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| Duplicate, Delete, Add field and an empty group's place had no press state | `transform: scale(0.98)` on `:active`, 120 ms with a strong ease-out, none for reduced motion | a press should answer at once, as the palette's items already do |
| — | the insert point fades in over 120 ms with a strong ease-out, and not at all for reduced motion | it appears on every hover between fields, tens of times a minute: fast, and only opacity |
| — | selecting, editing a label in place, switching widths and pages: no motion | keyboard-heavy actions repeated all day; animation would make them feel late |
| — | the toast is Umbraco's own | the backoffice's notifications move as Umbraco's do |

## A reader's review

A reader who had not built the step read its diff for defects. Each finding below was traced in the code, fixed and given a
test:

| Finding | What was done |
| --- | --- |
| The delete toast's Undo took back whatever came last: a delete, then Ctrl+D, then Undo removed the duplicate and left the field deleted | the toast's Undo takes back its delete only while that is still the last change, and the next change closes the toast |
| The theme's new start alignment reached the canvas's control layer, which then had no height in a group of two columns | the layer covers its field whatever a theme aligns the parts by; a journey selects both fields of a two-column group by a click ([the shot](review-9.3/selected-columns-1440.png)) |
| F2 on a heading without text hid the field's button and left nothing to edit or focus | a heading without text is not edited in place, and an edit where no label shows ends at once |
| Deleting a page's last field left the selection on a group the canvas doesn't draw, and the focus on nothing | the selection is let go and the focus goes to Add field; a page with more than one group, or a titled one, shows its groups |
| Closing the palette over the canvas, or folding the palette, dropped the focus, and with it the view's shortcuts | Esc, Close and a click beside the palette give the focus back to what opened it; folding puts it on the rail |
| Tab from the palette over the canvas went on to controls lying under it | the canvas is out of reach while the palette lies on it, under a light scrim ([the shot](review-9.3/palette-over-920.png)) |
| A label written for screen readers only was edited inside its one-pixel box | it shows while it is edited |
| The edit box started from and wrote the default text while the canvas showed another culture's | it starts from and writes back the text the canvas shows |
| The paste dialog's help never reached screen readers through UUI's box | the dialog uses the browser's own box with its label and help ([the shot](review-9.3/paste-1440.png)) |
| A canvas or gallery preview that couldn't be loaded showed a loader for ever | each says so; the canvas offers Try again |
| A canvas put back in the page stopped measuring its width; each image read made another media repository | both fixed |

## The tamper round

Each check was tampered with in a throwaway clone, and a named test had to fail: 18 changes to the designer's code against its
Vitest files (four first slipped through and their tests now name the value: a selected field's `aria-current`, a slider's start
between two steps as near, the 50 pasted lines, an import of the views without `from`), 6 to the canvas endpoint against its
integration tests and the permission matrix (three first did not compile and were written again so that they did), and 6
built into the client against the journeys and the themes' specs. Holding a theme's parts to their own heights first slipped
through in bootstrap5, whose lists keep their height in a stretched wrapper; the themes' specs now also hold an email box
beside a long text to a box's height, which both themes would stretch. The canvas's controls losing their height under a
theme's alignment made the two-column journey fail, as the reader had expected.

# Step 9.3c: the moves

ROADMAP 9.3c makes the canvas move things: a handle on every field and group, pages dragged by their steps, palette types dropped on
the page, refusals explained where they happen, Move to…, Alt+Up and Alt+Down across groups and pages, Up and Down between
elements, the Keyboard shortcuts dialog, and a focus that stays where it belongs
([ADR-0169](../decisions/0169-the-designers-moves.md), on ADR-0016's engine). The three design skills ran on the result as for 9.3b.

## What was looked at (9.3c)

The same e2e host in Edge, as the designer, with a contact form, a blank form built with the keyboard only, a form with two repeatable
groups and a form of 300 fields.

| Shot | What it shows |
| --- | --- |
| [a handle](review-9.3/moves-handle-1440.png) | the tab at the top left of the field under the pointer, its frame dashed |
| [a move refused](review-9.3/moves-refusal-in-place-1440.png) | Alt+Up on the first field: "It can't move any further." beside its handle, the tools on the right |
| [a drag](review-9.3/moves-drag-line-1440.png) · [the drop](review-9.3/moves-dropped-1440.png) | the dashed slot where the field was, the line with its round ends, the copy with its name; the selection, its tools and the insert points stepped back. Then the field in its new place, selected, and "Field moved." with Undo |
| [a refused drop](review-9.3/moves-drag-refused-1440.png) | a repeatable group dragged into another's row: the line in the refusal colour, the reason under the copy |
| [Move to…](review-9.3/moves-move-to-1440.png) | "To the end of", the places by page, where it is now, the other repeatable group off with its reason, Move off until a place is chosen |
| [the shortcuts](review-9.3/moves-shortcuts-1440.png) | what the keys act on, then each shortcut with its keys, and Close |
| [a page by its step](review-9.3/moves-page-drag-1440.png) | the step's slot, the upright line before Page 1, the copy |
| [at 920 px](review-9.3/moves-selected-920.png) | the narrowest workspace: the handle on the left and the bar on the right of the selected field |

**Checks that ran on the last look:**

- **Axe** on the Design view after the keyboard build and after the mouse moves, on Move to… and on the shortcuts, in
  `designer-moves.spec.ts`: none (the first run found Move to…'s name, "Move Date to…", didn't hold its visible text; it is
  "Move to…, Date" now, WCAG 2.5.3).
- **The bundled detector** on the canvas, its field, its styles, the drag, the two dialogs and the palette: `[]` for each.
- **The journeys:** a form of three pages built with the keyboard only, the focused element asserted after every operation; fields,
  a group, a page, a palette type and a refused drop with the mouse, Escape, and a palette type dropped on an empty page.
- **The budget:** a form of 300 fields in the `budgets` project, measured alone: the slowest interaction 80 ms from the input to
  the next paint (Event Timing), and no frame over 50 ms (Long Animation Frames). The first measure found up to 168 ms: every
  selection drew all hundred fields of the page again, and the handle's `:has()` rule was checked for every field at every move
  of the mouse; a field's state is now kept while it is unchanged, and the handle shows by sibling rules (ADR-0169).

## design-taste-frontend (9.3c)

The same read as 9.3b: Operate mode inside Umbraco's own system, dials 2 / 2 / 5. What it asks of the moves held: one accent (the
handle, the line and the selection share Umbraco's), one radius scale (the handle's and the tools' tabs alike), and every state
composed: hover, focus, selected, dragging, refused, dropped, and an empty page that still takes a drop.

## impeccable (9.3c)

| Finding | What was done |
| --- | --- |
| While a field was dragged, the selected field's frame and tools stayed up; dropped at the top of the selected field, the line ran along its frame and could not be seen | the selection's frame, its tools, the handles, the insert points and any refusal step back while a drag lasts: the line is the one accent |
| The dragged field's own handle stayed above its slot | it goes with the rest |
| The reason sat under the line, where the copy that follows the pointer covered it; over a step there was no line, so no reason; near the top edge it was cut off | the reason follows the pointer under the copy, the same in every case |
| The line between two steps stood upright with both round ends at its top | one at each end |
| A long reason in German ran past its pill | it wraps within 320 px |

Kept as they are: the handle covers the bottom corner of the field above, as the tools do on the right (inside the frame it would
cover the label); a refusal note may grow upwards on a narrow field, below the tools, until the next change.

## emil-design-eng (9.3c)

| Before | After | Why |
| --- | --- | --- |
| — | a drop settles over 150 ms with `cubic-bezier(0.23, 1, 0.32, 1)` from where the copy was, as a transform | the one move the eye must follow from the hand to the page; a strong ease-out answers at once, on the compositor |
| — | keyboard moves, Move to… and Undo: no motion | repeated all day, one after another; motion would make them late (ADR-0016) |
| a line that eases from place to place (considered) | the line jumps | it follows the hand: any easing lags behind it |
| a drag that started on any press | 6 px first, and a finger moving up or down over a step or a type scrolls | a click stays a click, and a scroll stays a scroll |
| pressing and moving a handle, a step or a type selected text around it | none of them selects text | a drag that paints a selection looks broken |
| — | reduced motion: no settle; forced colours: the handle, the line, the slot and the notes in system colours | the same information without colour or motion |

## The reader's review (9.3c)

| Finding | What was done |
| --- | --- |
| F2, Ctrl+D and Alt+Up/Down on a field that had the focus but not the selection: after the edit, or after Undo, the focus went to nothing, and with it the view's keys | what a key or a drop acts on is selected first, so Undo goes back to it with the focus; Undo that leaves nothing selected gives the focus to Add field |
| Nothing could be dropped on a page that shows only its sentence, the most natural first drag | the sentence stands for the page's one group: a field dropped anywhere on the page goes there; the mouse journey drops a type on a new page |
| Over a step the reason didn't show, and near an edge it was cut off | the reason follows the pointer |
| In columns, a field dragged out of a row of two made the other look alone, so hovering its own place moved it | a row is judged with the dragged field still counted |
| The shortcuts said "what is selected" though the keys act on the focused element; "or" between two keys was hidden from screen readers; the German "where it is now" read oddly | the dialog says once that the keys act on the field, group or page you are on; "or" is said; "(hier ist es jetzt)" |
| Move to… read a refusal's reason twice | it is the place's description only |
| A refusal note could cover Move to… on a narrow field | it stays below the tools and leaves them their room |
| The keys worked during a drag | they change nothing until it ends |
| Escape was taken from the page before a drag began, and the release after an Escape clicked the handle | Escape is the drag's only once it has begun, and that release clicks nothing |
| A page let go anywhere went to the end of the steps | it lands only near the steps |
| A journey's check could never fail (`arrayContaining`) | it waits for the moved field to be first |

## Left for later steps

- **9.4** opens the inspector beside the canvas, and with it the palette's fold to a rail while an element is selected between 620
  and 1024 px (added to 9.4).
- **10.3** shows the form in its own theme in Preview.
