# 0105. The theme registry, view resolution and the base rules

- Status: accepted; builds [ADR-0015](0015-themes-and-the-markup-contract.md)'s themes (the registry, a form's
  theme, the view order, the class map and the functional base rules) and the honeypot's markup of
  [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md), and extends
  [ADR-0092](0092-serving-the-website-assets.md) (the stylesheets of the themes a page uses, each once),
  [ADR-0093](0093-the-razor-rendering.md) (the `theme` attribute) and
  [ADR-0104](0104-the-output-cache-rules-and-the-render-endpoint.md) (the render endpoint's `theme`); the honeypot's
  base rule clipped where it stands instead of moved off the page by [ADR-0106](0106-the-default-theme-ships.md) in 6.3b
- Date: 2026-10-29
- Roadmap step: 6.3a (decided and built); 6.3b gives `default` its stylesheet and tokens, 6.3c `bootstrap5` its
  stylesheet and `unstyled` its review, 6.6 the headless element's base rules, 6.7 the form picker's placement, 12.2
  the library themes, 12.4a the General setting's screen

## Context

ADR-0015 decided that every theme shares one set of views, themed by a class map, a stylesheet and `--jfb-*` tokens;
that a theme is registered in code through `IFormBuilderTheme`; that a form's theme is its `appearance.theme`, else the
site's default theme of the General section, else `default`; that each view resolves from the form's override in the
theme's folder, the theme's folder, a library theme's base folder and `default`; and that functional base rules ship
with every theme. ADR-0093 left the `theme` attribute of `<jfb-render>` to the themes, and ADR-0092 said that from the
themes on `<jfb-assets>` writes the stylesheets too, once per request.

Step 6.3a builds those mechanics; the looks come after it (`default` in 6.3b, `unstyled` and `bootstrap5` in 6.3c).
Still open were:

- **where the registry lives** and how a site adds or changes a theme;
- **which slots** the class map has, and how a view gets a theme's classes without computing them (ADR-0015 asks that
  views compute nothing);
- **how stylesheets are written** when two forms on one page use two themes, given that Razor runs a view before its
  layout and a `<jfb-assets>` in the layout's head runs after the view's forms but before a form the layout itself
  renders (ADR-0092);
- **what the General setting holds** when a theme it names is removed from code;
- **what the base rules hold** besides ADR-0015's three: 6.2i (ADR-0101) asked every theme for a scroll margin where a
  press without JavaScript lands, and 6.2k for a group that shrinks to a phone's width;
- **the honeypot** the base rules keep out of view: ADR-0093 left its markup to 7.1 with its scoring, so no page had
  one, yet 6.3a and 6.3c check in Edge that every theme keeps it out of view and out of the accessibility tree.

## Options

**Where the registry lives:**

1. **Web**, beside the views. The settings and the backoffice, which name themes, would have to reach into the website
   layer.
2. **Core**, as an ordered collection like the field types (ADR-0055), reached through
   `builder.JironFormBuilder().Themes()`. A theme is data (names, a path, classes, tokens), so Core needs nothing of the
   web to hold it.

**How a view gets a theme's classes:**

1. A view helper that looks a slot up as the view runs: every view would compute its classes.
2. **The view models carry them**: one object per form with a property per slot, each property our classes and the
   theme's together, so a view writes `class="@Model.Classes.Control"`.

**How the stylesheets are written:**

1. Every registered theme's stylesheet on every page: simple, and every page loads themes it never shows.
2. Only the site's default theme's: a form with another theme would render unstyled.
3. **Each theme a page uses, once**, decided when the page is written, so every form rendered by then counts.

## Decision

Option 2, option 2 and option 3.

- **`IFormBuilderTheme`** (Core, public): `Name` (the alias, as an option source names itself), `Label` (what the
  backoffice shows), `Stylesheet`, `Classes` (slot to classes), `Tokens` (token to default value), `DarkScheme`
  (whether it offers `jfb-scheme-dark` and `jfb-scheme-auto`) and `ViewFolder`. The
  collection holds the built-in themes from the moment it is made, so a composer appends, replaces or removes one
  whenever it runs. The registry checks them once, when it is made, and refuses to start with a message for each
  problem:
  - an alias that is not lower-case letters, digits and hyphens, starting with a letter, at most 64 characters, or that
    another theme has;
  - no theme `default`, which every other resolution falls back to;
  - a stylesheet that is not a local path (one leading `/`, no second one, only a URL path's characters, so no scheme,
    query or fragment): nothing loads from another host on a visitor's page (CLAUDE.md rule 4);
  - a stylesheet below `/jfb/assets/` that is none of the website assets the Web assembly holds (a check of the Web
    package beside the registry's): nothing else is served there;
  - a slot that is not one of the list below, a class that is not a class name (visible ASCII without quotes, angle
    brackets or ampersands, single spaces between), a token that is not `--jfb-*` or whose value is more than one
    declaration's or holds an address, or a view folder that is not a folder name below `Views/JironFormBuilder/`.
- **The built-in themes:** `default` (its view folder `default`; the dark scheme; its stylesheet and tokens come with
  its look in 6.3b), `unstyled` (no stylesheet, no classes, no tokens, no dark scheme) and `bootstrap5` (its class map
  now, onto Bootstrap 5's own classes; its small stylesheet for what Bootstrap lacks comes in 6.3c). Each built-in
  theme's view folder is its alias; only `default` ships views, so the others render the default views with their
  classes, and a site may still give them views of its own.
- **A form's theme**, first that is set: the placement's `theme` (`<jfb-render theme="…">`, the view component's
  `theme`, the render endpoint's `theme`, which is an alias, none when blank, and `400 invalid_request` for anything
  else, and the form picker's placement in 6.7), the form's `appearance.theme`, the General section's `theme` for the
  form's site, then `default`. The visitor form carries the site's theme in place of an absent one, as it carries the
  site's button labels (ADR-0095), so the headless element (6.6) sees the same theme. A name the registry lacks
  renders with `default` and is logged once per name; library themes (12.2) join the lookup before that fallback.
- **The General setting `theme`:** a theme's alias of at most 64 characters on one line, or none (the code default).
  It is not held to the registry: a theme removed from code leaves the setting valid and the site's forms on `default`,
  as a form's own theme does (ADR-0015). The settings screen (12.4a) offers the registered themes.
- **Views**, for each view: `<theme folder>/_forms/<form alias>/`, then `<theme folder>/`, then `default/` (a library
  theme's base folder joins before `default` in 12.2). For `default` the first two are its own.
- **The class map's slots** (`FormBuilderThemeSlots`), each with our classes, which a theme's classes join and never
  replace: `form` (`jfb-form`), `page`, `group`, `field`, `label`, `help`, `error`, `control` (inputs and text areas),
  `select` (`jfb-control` on a dropdown), `choices`, `choice`, `choice-input` and `choice-label` (a choice's input and
  label, which have no class of ours), `switch`, `range` (a slider's input), `affix` and `affix-text` (a prefix or
  suffix and its text), `counter`, `navigation`, `button`, `button-primary`, `button-link`, `progress`,
  `error-summary`, `notice` and `outcome`. The view models carry one `ThemeClassesModel` per form, with a property per
  slot; a slot with no class of ours and none of the theme's gives none, so the attribute is left out. The form also
  carries `jfb-theme-<alias>`.
- **The functional base rules** (`/jfb/assets/themes/base.css`), every rule under `.jfb-form`, written for every form
  whatever its theme: `[hidden]` honoured; visually hidden text; the live region heard and not seen; the honeypot out
  of view (`aria-hidden` in the markup keeps it out of the accessibility tree, ADR-0012); a block-start scroll margin
  on `.jfb-row`, `.jfb-repeater` and `.jfb-ranking > li`, where a press without JavaScript lands (ADR-0101), of
  `--jfb-scroll-margin` (6rem unless a site with a taller sticky header sets more); and a form's fieldsets free to
  shrink below their widest control, with every control, a select among them, held to its field's width, so no
  control widens a phone's page.
- **Assets on a page** (each tag with the nonce, as ADR-0092 says):
  - The runtime's module script: the first `<jfb-assets>` to run claims it, so it wins wherever it is, and a form's
    own tags write it when the page is written unless it was claimed or written before (unchanged).
  - The stylesheets, the base rules first and then each theme's: decided when the page is written, in the page's
    order, each once. The `<jfb-assets>` that claimed the script writes every stylesheet the page has not written
    before it: the base rules' and those of every theme the page's forms use, in the order the forms rendered, a form
    the layout renders after it included. A form's own tags write the base rules and its theme's stylesheet unless
    the page wrote them before. A form whose own tags are turned off is still covered by a `<jfb-assets>` anywhere on
    its page. The base rules go with the stylesheets rather than with the script, so a form before a `<jfb-assets>`
    lower on the page never shows its honeypot or its hidden parts before they load.
  - A stylesheet below `/jfb/assets/` is written under its fingerprint (`?v=`); a site's own is written below the path
    base as it is. Two themes that name one stylesheet write it once.
  - Without `<jfb-assets>`, the tags stand before the first form that needs them; a stylesheet in the body is valid,
    but the rendering docs (Phase 17) recommend `<jfb-assets>` in the head, where nothing shows before its styles.
- **The honeypot's markup comes now**, as ADR-0012 and ADR-0015 describe it: `Form.cshtml` writes, after every
  field and before the live region, `<div class="jfb-hp" aria-hidden="true">` with a label that asks to leave the
  field empty and a text input with `tabindex="-1"` and `autocomplete="off"`. Its name comes from the form's key and
  public revision (a SHA-256 of both: a digit, three hex digits, a hyphen and four more, such as `7f3a-9c1d`), so it
  is the same in every cached copy of one revision and different in the next, is no field's name (an alias holds no
  hyphen) and never starts with `jfb-`; its id is the instance's followed by the name, which no field's id can be,
  since an alias starts with a letter. The name reads as no word, so a browser's autofill never fills it for a real
  visitor. The post reader leaves the name out, as it leaves out every name that is not a field's (ADR-0100); 7.1
  reads it and scores it, and the runtime (6.4) and the headless element (6.6) carry it as 7.1 says.
- **The render endpoint's theme** is taken as the page took it, but a name the site lacks becomes `default` before the
  registry sees it, so a name from a request is neither logged nor kept: the registry logs a missing name once and
  remembers it, which is bounded for the names forms, settings and placements hold, and would not be for names a
  visitor makes up.

## Consequences

- A site adds a theme from a composer (`builder.JironFormBuilder().Themes().Append<AcmeTheme>()`), and a theme of its
  own whose stylesheet is not local never starts.
- One set of views serves every theme; `bootstrap5` gets Bootstrap's classes through the class map before it has a
  stylesheet of its own, and a site's override that ignores `Model.Classes` still renders, without the theme's classes.
- Every built-in view writes its classes from `Model.Classes`, so the default theme's markup keeps every class it had,
  with `jfb-theme-default` on the form, and a theme's classes reach every slot.
- Every form carries its honeypot from now on, in every cached copy alike; until 7.1 scores it, a bot that fills it
  is not yet caught by it.
- A page loads the base rules, the runtime and the stylesheets of the themes it shows, each once; a page without a
  form that names `<jfb-assets>` still loads the base rules and the runtime, as it loaded the runtime before.
- The themes package of the client (`packages/themes`) builds the stylesheets into `WebsiteAssets/themes/`, minified
  with Lightning CSS for the browsers the themes are written for, so logical properties stay as written; the size
  check holds each to the theme budget (10 KB gzip) and fails when they were not built.
- **Revisit** if a theme needs markup the class map cannot reach in more than a few views (ADR-0015), or if browsers
  make `@scope` reliable enough to scope the base rules without the `.jfb-form` prefix.
