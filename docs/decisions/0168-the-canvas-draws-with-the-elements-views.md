# 0168. The canvas draws with the element's views, in the default theme

- Status: accepted
- Date: 2026-11-03
- Roadmap step: 9.3b (decided and built); 9.3c adds the moves, 9.4 the inspector beside the canvas, 10.3 the preview in the form's
  own theme

## Context

Step 9.3b draws the form on the Design view's canvas, as visitors will see it (`docs/ux/information-architecture.md`, section 5.1),
with the palette beside it, insert points, inline label editing and pasted questions. The roadmap names "small Lit preview elements
per type", and ADR-0166 asks for one drawing of a field in the backoffice, which the template gallery's preview (9.2) uses too. Four
things had to be decided:

- **How a field is drawn.** Visitors see the markup the server renders (the Razor views) or the headless element writes (its views
  in `@jfb/element`, held to the server's markup by the element fixtures, ADR-0128). A third drawing for the backoffice would be a
  third thing to keep in step.
- **Where the canvas gets what a field needs.** The views take view models with a field's words already said (labels, "(optional)",
  "Choose one", a counter's "Up to 255 characters") and the theme's classes, which the server builds from the definition, the
  culture and the site's settings.
- **Which theme the canvas shows.** IA 5.1 said "in its theme". The bootstrap5 theme needs the Bootstrap stylesheet the site loads,
  unstyled has no stylesheet at all, and a site's own theme may lean on the site's page (its fonts, its resets).
- **What a draft may hold.** A draft is what an editor saved, not yet through the server's sanitiser, and the canvas shows it inside
  the backoffice.

## Options

For the drawing:

1. **A Lit element per field type** with its own markup, as the roadmap's wording suggests. About thirty elements, a third drawing
   of every field, and nothing that notices when it drifts from the website.
2. **The element's views, fed by view models the designer builds** from the draft in the browser (`preview/field-models.ts`, the
   client's statement of the server's `FormModels`). One drawing on the website and in the backoffice; a test holds the designer's
   models to the server's markup for every built-in type.
3. **The server renders each field** through an endpoint. Exactly the website's markup, but a round trip for every keystroke of a
   label, and undo would wait on the network.

For the theme:

1. **The form's own theme on the canvas.** Faithful for the default theme, wrong or broken for bootstrap5, unstyled and a site's own.
2. **The default theme on the canvas, every theme in Preview.** The canvas shows the form's structure in one calm, known look; the
   preview (10.3) opens the form in its own theme on the site's page, where its stylesheets belong.

## Decision

**Option 2 for the drawing, option 2 for the theme.**

- **One drawing.** The canvas and the gallery's preview build each field's view model from the draft (`fieldModel`, `pageModel`,
  `formSettings`) and write it with `writeField`, `writePage`, `writeOutcome` and `writeNavigation`, which `@jfb/element` now exports
  from `@jfb/element/views`. `field-models.test.ts` writes every field of the server's sink (a field of every built-in type, and a
  repeatable group holding one of each) and compares its elements and classes with the server's markup for the same draft
  (`tests/fixtures/rendering/designer-sink.json`, written by the server's fixture test). `drawing.test.ts` holds the rest of
  ADR-0166's rule: only the canvas and the preview import the views, only the models and the canvas's label finder name a field's
  classes, and the gallery shows a template with `jfb-form-preview`. A site's own field type gets its label and help; its control comes
  with 15.1's `jfbFieldPreview`.
- **What the canvas reads.** `GET /umbraco/jfb/management/api/v1/canvas?culture=` answers the words the views say in the user's
  language (`RenderingWords`, with Yes and No), the default theme's classes, its stylesheets' addresses and the site's required marker.
  The stylesheets are fetched once and adopted into the canvas's shadow root, so nothing of the theme leaks into the backoffice and
  nothing of the backoffice into the form. Any backoffice user of the section may read it (it holds no form data).
- **A field on the canvas.** Each field is one `jfb-canvas-field`, which is the field's wrapper itself (`.jfb-field`), so the theme's
  rules for a group's fields (its columns, the shared rows of fields side by side) hold. What the views write is inert; on top of it
  lie the designer's controls: a button that selects the field and names it for screen readers ("Email address, email field"), the
  insert point below it, and Duplicate and Delete while it is selected. A field visitors don't see (a hidden field, a heading without
  text, a captcha before one is chosen, an image without a picture or with one the user may not open) shows a quiet note in its
  place, so it can be found and selected. A repeatable group shows its first row, its fields each a canvas field of their own.
- **As visitors with JavaScript see it.** An upload shows its drop frame and a signature its empty pad (`asEnhanced`), as the
  website's runtime leaves them; the canvas leaves out the site's own button labels and uses the form's.
- **Kept while unchanged.** Models are kept per field object and the shared context per look, types and settings, so a field an
  edit didn't touch keeps its markup and a control that has the focus keeps it.
- **A draft's rich text** (a paragraph, a message after sending) is copied through an allowlist of plain structural tags without a
  single attribute; scripts, styles, frames, media and forms go with what they hold. The server's sanitiser still decides what
  visitors get.
- **The default theme on the canvas**, on a white sheet whatever the backoffice's theme. Every other theme shows in Preview (10.3).
  IA 5.1 is updated to say so.
- **Widths.** Desktop 960 px, Tablet 768 px and Phone 375 px are the form's own widths; with less room the form reflows, never
  scaled, and "Narrowed to fit" says so (IA 5.1). The chosen width is kept per browser.
- **The palette** stays beside the canvas in a view wider than 700 px and folds to a rail by its own button; up to 700 px it opens
  over the canvas from Add field or an insert point, and Esc closes it. The narrowest workspace, 620 px, lies in that range:
  Umbraco's own header keeps the view from getting narrower, so a threshold below 620 px would never be met. The palette's fold to a
  rail while an element is selected (IA 5.1, between 620 and 1024 px) comes with the inspector in 9.4, which it makes room for.
  Recently used keeps the last three types per browser.
- **Pasted questions** become fields, one per line (up to 50), each with the type its words suggest in English or German (an email
  address, a phone number, "How many…", a yes-or-no question…) and shown in a table before they are added in one undoable step.
- **Inline labels.** Double-click or F2 edits a label in place; Enter keeps it, Escape puts the old one back, and an empty label
  leaves the field as it was.
- **Keyboard.** Delete (with an Undo toast), Ctrl+D, Ctrl+Z, Ctrl+Y and Esc work now; 9.3c adds the moves and the `?` overlay. The
  skip link past the palette is a button, since Umbraco's router takes a link's click for a move to another place.

## Consequences

- Positive: a field looks the same on the website, in the headless element, on the canvas and in the gallery, and a change to the
  server's rendering that the designer doesn't follow fails a test.
- Positive: the canvas never loads a site's stylesheets into the backoffice, and a draft's markup can't run there.
- Negative: `field-models.ts` states again in TypeScript what `FormModels` builds in C#. The structure test over every built-in type
  is what keeps them together; a new type or setting needs both sides.
- Negative: a form in bootstrap5 or a site's own theme looks different on the canvas than on the site until the editor opens Preview.
- Found on the way: in a group of two or more columns, a field drawn as a fieldset (choices, a rating, a signature) took two of its
  eight shared rows instead of all of them, so the fields beside it drifted, and a list stretched to the height of its row. Both
  themes are fixed, and their website specs lay the server's sink out and check the rows.
