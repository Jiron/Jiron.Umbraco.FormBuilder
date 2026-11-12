# 0215. Library themes and email layouts

- Status: accepted
- Date: 2026-11-08
- Roadmap step: 12.1, its part 12.2 (ADR-0197); builds on [ADR-0015](0015-themes-and-the-markup-contract.md) and
  [ADR-0105](0105-the-theme-registry-and-the-base-rules.md) (themes in code, their tokens),
  [ADR-0106](0106-the-default-theme-ships.md) (the default theme and its dark scheme),
  [ADR-0037](0037-library-and-permission-repositories.md) (the library's rows), [ADR-0214](0214-the-option-list-library.md)
  (the library's screens and `jfbFormLibraryUse`) and [ADR-0159](0159-mail-the-queue-the-layouts-and-the-limits.md)
  (the email layouts); IA 7,
  `docs/ux/wireframes/theme-editor.html` and `email-layout.html`

## Context

`jfbTheme` and `jfbEmailLayout` exist since the schema's first version, and a form can name a theme of the library, but
nothing makes one: there are no screens and no endpoints, and the website renders a library theme's alias as the
default theme, since the registry knows only themes in code. Email actions already send in a layout of the library
(`EmailLayoutDesign`: a look, a logo, light and dark colours, a header and a footer per language). Part 12.2 asks for
the theme editor (the tokens a base theme declares with their defaults, dark colours only for a base theme with a dark
scheme, type, spacing, corners, borders, the focus ring and the button style), a kitchen-sink preview with contrast
checks, sanitised custom CSS, import and export, and the email layout editor with a logo, colours, a header and footer
per language, a preview in light and dark, and a test send.

## Decisions

1. **A library theme is a layer over a theme in code.** Its row names its base theme (`baseTheme`), and its `tokens`
   hold what it changes: `{ "light": { "--jfb-…": "…" }, "dark": { … } }`, each token one the base theme declares
   (`IFormBuilderTheme.Tokens`; dark only for a base theme whose `DarkScheme` is true, and only its colours), each value
   one declaration without an address, as the registry holds a theme's own tokens. `customCss` is the editor's text;
   what the website serves is the sanitised copy (5).
   Amended by [ADR-0236](0236-what-the-security-review-changed.md): a value keeps to an allowlist grammar rather than a
   blocklist, so an `image-set()` or an escaped `url(` is refused too.
2. **The website renders a library theme** as its base theme with the layer on top: the form carries both classes
   (`jfb-theme-<base> jfb-theme-<alias>`), takes the base theme's class map and views, and its page loads the base
   theme's stylesheet and then the layer's, `/jfb/assets/themes/library/<alias>.css?v=<version>`, served by the
   website's assets middleware beside the stylesheets in code: immutable for a year under the theme's current version,
   `no-cache` under any other, with an ETag. The layer holds the tokens on `.jfb-form.jfb-theme-<alias>`, the dark
   ones under the dark scheme's own selectors (`jfb-scheme-dark` always, `jfb-scheme-auto` when the visitor's system
   asks, as the default theme's), and the sanitised custom CSS. In the dark scheme it writes every colour changed in
   either scheme, the dark value or else the base theme's dark default, so a light-only change never shows through on a
   dark surface. The layer is cached per alias on every server and dropped through a cache refresher when the theme
   changes, so a page's stylesheet is never older than its version. A library theme whose base the site no longer has
   renders as the default theme, as a missing theme does today; an alias names the theme in any letter case.
3. **`IFormBuilderTheme.DarkTokens`**, a member with a default (none): the colours a theme takes in the dark scheme,
   which the editor offers as the dark colours' defaults. The default theme declares one for each of its colours, and its
   `Tokens` gain `--jfb-font-family` and `--jfb-font-size`, whose default `inherit` is the site's font and size, and
   the button's own colours, `--jfb-color-button`, `--jfb-color-on-button` and `--jfb-color-button-hover`, which
   default to the accent, the text on it and the accent's strong shade, so a theme can outline its buttons or give them
   a colour of their own without touching links and checked controls.
4. **The editor** is the library's workspace, as an option list's (ADR-0214): the name and alias in the header with "Used
   by N forms", Save, and Duplicate, Export and Delete… in its "…"; Import a theme… in the list's "…". Its one view is the
   settings beside a kitchen-sink form (under them below 900 px), the settings in collapsible sections, each closed one
   saying in one line what it holds:
   - **Colours,** light and dark side by side, each a colour box with its hex value and its contrast check under it:
     text and errors 4.5:1 on the surface, muted text 4.5:1, control borders and the focus ring 3:1 (WCAG 1.4.11), the
     accent 3:1 and the text on it 4.5:1 on the accent, dividers decoration only. A check that fails says the nearest
     colour of the same hue that passes. Dark colours show only for a base theme with a dark scheme.
   - **Type:** the font (the site's, a system sans, serif or monospace stack), the size (the site's or 14 to 24 px) and
     the scale (compact, default, generous), which sets the three steps.
   - **Spacing and corners:** compact, default or roomy (the seven spaces at 0.75, 1 or 1.25 of the 4 px grid), and the
     corner radius (0 to 24 px).
   - **Buttons and focus:** filled or outlined buttons (the button's colours of 3: outlined is the surface with the
     accent's text), and the focus ring's width (2 to 4 px).
   - **Other tokens:** every other token the base theme declares, by name with its default as the placeholder — a site's
     own base theme's tokens show here, nothing of them hidden.
   - **More options:** the custom CSS, with what the sanitiser removed said under it.
   The preview is the website's own rendering of a sample form with every common kind of field, help, a placeholder, an
   error with its summary and both buttons (`GET theme/sample`: the second page of two in the base theme's views and
   class map, under the preview's alias, in the user's language), the tokens applied as the layer applies them, in
   light or dark. The page links the base rules and the base theme's stylesheet, holds an empty style element for the
   layer and no script, and its form has nowhere to post to; the editor shows it in a frame with scripts off and, as
   the theme changes, writes only the layer `POST theme/preview` gives into that element, so the page never reloads.
5. **Custom CSS** goes through AngleSharp's CSS parser (the HTML sanitiser's own, ROADMAP §4.10): style rules have every
   selector put under `.jfb-form.jfb-theme-<alias>` (`:root`, `html` and `body` become the scope itself);
   `@media`, `@supports`, `@container` and `@keyframes` stay with their rules; `@import` and every other at-rule
   go, and so do nested rules, which older browsers would read unscoped; a declaration goes when its value calls
   `expression()`, names `behavior` or `-moz-binding`, holds `javascript:` or `vbscript:`, or holds a `url()` or
   `image-set()` that is not a path on the site or a `data:` image; an `@font-face` goes when a source is on another
   host. Values stay exactly as written (the parser's own shorthand expansion is not used). At most 64 KB. What went is
   said in the editor, by kind (import, at-rule, nested, address, script, font); the stored text stays as typed, and the
   website serves the sanitised copy only.
   Amended by [ADR-0236](0236-what-the-security-review-changed.md): a selector of what stands beside the form goes
   (outside), and so does a declaration with an escape outside quotes (escape); an address with an escape is none of the
   site's.
6. **Export and import** are one JSON document of the theme's base, tokens and custom CSS (`jfb-theme`, version 1),
   with its name; an import makes a new theme with a free alias, refusing a base the site lacks and tokens it does not
   declare, each problem at its path.
7. **"Used by"** reuses `jfbFormLibraryUse` (ADR-0214) with two more kinds: `theme`, a form whose settings name the
   theme, keyed by a name-based UUID of its lower-case alias (RFC 9562 version 8 over SHA-256 in a namespace of its own),
   so a form's rows need no read of the theme, and a library theme is created under that same key; and
   `emailLayout`, an email action that names the layout, keyed by the layout's key and counting actions. Migration step
   13 (`jfb-013-theme-and-layout-uses`) fills both from the stored versions with a frozen reading, held to the model by
   a unit test. A theme or layout is not deleted while a form uses it, nor while it is the site's default (Settings ›
   General's theme, Settings › Email's layout); the dialog says which.
8. **The email layout editor,** a workspace of the same kind: the look (card or letter), the logo from Umbraco's media
   picker (the media's key kept with its address, which the save reads from the media, so an email needs no read of it),
   its alternative text and the header and footer per language, the colours (accent, page, surface, text, muted text,
   dividers) for light, the dark ones worked out as the renderer does unless set, and beside them the preview of a sample
   entry in light and dark through the email renderer (in each of the site's languages), with **Send test email**, which
   saves the layout first and then sends the saved layout to the signed-in user's own address, through the outbox as the
   Email settings' test does. Sending only what is saved keeps the test mail what the forms will send, and the user's own
   address keeps the button from mailing anyone else.
9. **Recorded:** `theme.created`, `theme.saved`, `theme.deleted`, `emailLayout.created`, `emailLayout.saved`,
   `emailLayout.deleted` and `emailLayout.testSent`, each with the alias.

## Consequences

- A library theme looks like its base theme plus what it changed; a base theme's new tokens show in the editor at once.
- The website loads one stylesheet more for a library theme, cached with its version.
- The theme and layout rows' delete guard and count read the same table as option lists'.
- **Revisit** if sites need a library theme based on another library theme, or a layout with images besides the logo.
