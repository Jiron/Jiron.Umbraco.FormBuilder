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
| [the contact form](review-9.3/designer-1440.png) · [a field selected](review-9.3/selected-1440.png) | the default theme on a white sheet, the steps, the width switch; the selected field's outline and its two tools |
| [an insert point](review-9.3/insert-point-1440.png) | "Add a field here" on the line between two fields, on hover and on focus |
| [a label in place](review-9.3/label-in-place-1440.png) | the label's box where the label was, its text chosen |
| [the canvas's "…"](review-9.3/menu-1440.png) | Undo and Redo with their shortcuts, off while there is nothing to undo |
| [Phone width](review-9.3/phone-width-1440.png) | the form at 375 px, its button across the width as on a phone |
| [After sending](review-9.3/after-sending-1440.png) | the form's message as visitors see it, with Change |
| [the palette folded](review-9.3/palette-rail-1440.png) | the rail with its expand button and search; the canvas takes the room, and "Narrowed to fit" goes |
| [Delete with Undo](review-9.3/delete-undo-1440.png) | the toast, and the selection on the next field |
| [every field: choices](review-9.3/sink-choices-1440.png) · [rows](review-9.3/sink-rows-1440.png) · [files](review-9.3/sink-files-1440.png) · [a repeatable group](review-9.3/sink-repeater-1440.png) | two columns whose labels, help and controls line up; the drop frame and the pad as visitors with JavaScript see them; the group's first row with its own fields |
| [at 920 px](review-9.3/designer-920.png) · [the palette over the canvas](review-9.3/palette-over-920.png) | the narrowest workspace: the canvas alone, the palette opened from Add field with the insert point it will fill |
| [dark](review-9.3/designer-dark-1440.png) · [high contrast](review-9.3/designer-high-contrast-1440.png) | Umbraco's two other themes around the visitor's white sheet |
| [the gallery](review-9.3/gallery-1440.png) | a template drawn by the same preview as the canvas |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA, the whole page, what lies inside the designer's elements counted) on a canvas of three pages, the
  Paste questions dialog and the palette over the canvas at 920 px, in `designer.spec.ts`, and on the gallery in `library.spec.ts`:
  none.
- **The bundled detector** (`impeccable detect --json`) on the canvas, a canvas field, their styles, the palette, the paste dialog,
  the Design view and the preview: `[]` for each.
- **The journeys** of `designer.spec.ts`: a form of three pages built with the mouse, five fields from a pasted list, and the
  narrowest workspace with its skip link, its palette over the canvas and Delete with Undo; the gallery's journeys in
  `library.spec.ts`.
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

## Left for later steps

- **9.3c** adds drag and drop, Move to…, the arrow keys and the `?` overlay, with focus kept after every move.
- **9.4** opens the inspector beside the canvas, and with it the palette's fold to a rail while an element is selected between 620
  and 1024 px (added to 9.4).
- **10.3** shows the form in its own theme in Preview.
