# 0108. The bootstrap5 theme

- Status: accepted; builds [ADR-0015](0015-themes-and-the-markup-contract.md)'s `bootstrap5` on
  [ADR-0105](0105-the-theme-registry-and-the-base-rules.md)'s registry, scoped as
  [ADR-0106](0106-the-default-theme-ships.md) scopes the default theme; changes its class map (a plain button, the
  outcome) and adds a theme-only class accessor for the file input
- Date: 2026-10-30; amended 2026-10-31 (6.8d: the focus of what the theme draws)
- Roadmap step: 6.3d (decided and built); 6.4 enhances the controls with the runtime, 12.2 builds the library themes

## Context

ADR-0015 describes `bootstrap5` as Bootstrap 5's classes through a class map, with a small stylesheet for what
Bootstrap lacks, and Bootstrap itself the site's. Step 6.3a gave it the class map; the demo showed its forms on the
base rules alone. Step 6.3d loaded Bootstrap 5.3.8 on the demo's specimen, a form with a field of every built-in type,
and looked at it in Edge, light, dark and right to left. Without a stylesheet of its own:

- every field's legend (a group of choices, a grid's rows) took Bootstrap's heading size, since Bootstrap sizes a
  legend as a heading;
- picture choices were 600 px pictures beside small radio buttons; yes or no, a scale and a rating were plain radio
  buttons, the rating's without visible numbers before 6.3c;
- a grid's rows had no columns, a ranking was a bare list, groups of two or three columns were one column;
- an answer that needed a change showed only its message: Bootstrap marks `.is-invalid` and `.was-validated`, and the
  markup marks `aria-invalid`;
- help, counters and errors sat a full line away from their field, as Bootstrap's paragraphs do;
- the error summary's heading was as large as the page's;
- the plain buttons (Back, Add a guest, a ranking's moves) were `btn-outline-secondary`, whose grey text Bootstrap keeps
  in its dark mode: 3.3:1 on the dark body, below WCAG's 4.5:1;
- the file input was the browser's own, since it has no class of the package's for the map to join.

And Bootstrap had to reach the demo and the tests without being shipped.

## Options

**What the stylesheet styles with:**

1. Tokens of its own (`--jfb-*`) with Bootstrap's values as defaults: a second set of colours a Bootstrap site would
   have to keep in step with its own.
2. **Bootstrap's own variables** (`--bs-*`): a site's Bootstrap, its colours and its colour modes reach the form
   without any setting.

**Choices drawn as buttons:**

1. Slots for Bootstrap's `.btn-check` pattern (`btn-check` on the input, `btn btn-outline-primary` on the label):
   three more slots in the contract, and the outlined blue text is as faint in Bootstrap's dark mode (3.4:1).
2. **The stylesheet draws them** in Bootstrap's shapes from Bootstrap's emphasis colours, which hold their contrast in
   both modes.

**Getting Bootstrap to the demo and the tests:**

1. A link to a CDN: a third-party host on a visitor's page (CLAUDE.md rule 4), and a test that needs the network.
2. An npm development package that the demo serves from the tests' `node_modules`: the demo would depend on a test
   folder's install.
3. **A copy in the demo:** Bootstrap 5.3.8's two compiled stylesheets and its licence, byte for byte.

## Decision

- **Bootstrap draws what it has classes for, through the class map; the stylesheet draws the rest** from Bootstrap's
  variables, every rule nested under `.jfb-form:where(.jfb-theme-bootstrap5)` and logical: field legends read as
  labels and group legends as headings; groups keep a heading's space, a card group a card's border; fields side by
  side share their rows as in the default theme, so their controls line up; help, counters and errors sit close to
  their field; an answer that needs a change takes Bootstrap's invalid colour from `aria-invalid`; options in a row;
  picture choices as cards, the whole card the target; yes or no and a scale as Bootstrap-shaped buttons, a long scale
  in even rows on a phone; a rating as stars (Lucide's, in Bootstrap's primary colour, its numbers for screen readers
  as ADR-0107 asks); a grid's columns with their heads, stacked on a phone; a ranking as a bordered list whose move
  buttons are quiet through Bootstrap's own `--bs-btn-*` variables; a repeatable group's rows as bordered panels; the
  progress steps and bar; the error summary, notices and the outcome as Bootstrap's alerts with a form-sized heading,
  a send that failed and a pending outcome through Bootstrap's alert variables. Transitions only colour, and stop when
  the visitor asks for less motion; in forced colours the stars and the chosen buttons take the system's.
- **Bootstrap's own controls** (6.8h) take the same outline in place of their focus shadow: the shadow stays below 3:1
  on any ground, and the theme claims WCAG 2.2 AA for what it renders, so a site's Bootstrap keeps its colours here and
  gives up that one shadow inside a form, a refused control's red one too. A card's radio button stands inside the
  card's own outline, so it takes none.
- **The focus of what the theme draws** (6.8d): a rating, a scale, yes or no as buttons, a choice card and a ranking's
  move show a 2 px outline in `--bs-primary-text-emphasis`, 2 px outside. Bootstrap's own focus shadow is about 1.4:1
  and forced colours drop it; the emphasis colour is the one Bootstrap derives from the site's primary for each colour
  mode, 13:1 on its light ground and 6:1 on its dark one with Bootstrap's defaults, so it follows the site where
  `--bs-primary` itself might not hold 3:1. A focus never animates. Bootstrap's own controls keep their shadow, and in
  forced colours take an outline in `Highlight`.
  It weighs 3.8 KB gzip of its 10 KB budget, Lucide's ISC notice included, and `Bootstrap5Theme.Stylesheet` names it.
- **No tokens and no dark scheme of its own:** Bootstrap's colour modes (`data-bs-theme`) are the scheme.
- **The class map:** a plain button is `btn btn-secondary`, which holds its contrast in both of Bootstrap's modes;
  the outcome is `alert alert-success`. `ThemeClassesModel.FileInput` gives the file input the theme's classes for a
  control and none of the package's own, so Bootstrap styles it as `.form-control` and the default theme's upload
  stays as it is.
- **Right to left** is an RTL page with Bootstrap's right-to-left build, as Bootstrap does it; the stylesheet's
  logical properties serve both builds. A form whose direction differs from its page's follows the build the site
  loads, since a Bootstrap build has one direction.
- **The demo and the tests:** `demo/.../wwwroot/demo/vendor/bootstrap-5.3.8/` holds `bootstrap.min.css`,
  `bootstrap.rtl.min.css` and Bootstrap's `LICENSE` as published (npm `bootstrap@5.3.8`); a script test holds each to
  its SHA-256. Git keeps them as they are (`-text -diff`): a vendored file is not ours to change, and Bootstrap's CSS
  holds one character outside ASCII, which the text check refuses in our own CSS. The demo's layout loads Bootstrap
  only on a page whose forms show in `bootstrap5`, in Bootstrap's dark mode with `?scheme=dark` (its "auto" needs a
  script, which the demo does not add). Such a page is a Bootstrap site: its ground and text are Bootstrap's body
  colours, which Bootstrap's form colours are made for. The browser tests put the RTL build in place of the other for
  a right-to-left page. THIRD-PARTY-NOTICES lists the copy under "In the demo", and Lucide's star under "Shipped
  to sites".

## Consequences

- A Bootstrap site gets forms that look like the rest of its Bootstrap and follow its colours and colour modes; a site
  that changes Bootstrap's palette owns the contrast of its colours, as it does for its own forms.
- Bootstrap's reboot scrolls smoothly unless the visitor asks for less motion, so a press without JavaScript lands
  with a short scroll; the browser test reads where it lands with reduced motion, since the place is the same.
- Stock Bootstrap's red and grey reach AA on its white body only (4.53:1 and 4.69:1); on a tinted ground they do not.
  The demo takes Bootstrap's body colours for that reason.
- Bootstrap's own border colour shows a control at about 1.3:1, below WCAG 1.4.11. The theme borders text boxes, lists, radio buttons,
  boxes and the signature pad with `--bs-tertiary-color`, which holds 3:1 in both colour modes (6.4p), so they stand a little darker
  than a site's own Bootstrap controls.
- **Revisit** when Bootstrap 6 changes its variables or classes: a `bootstrap6` theme would sit beside this one.
