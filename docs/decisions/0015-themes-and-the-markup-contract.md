# 0015. Themes and the markup contract

- Status: accepted; the functional base rules and theme resolution amended in 1.8; the default view folder, its views
  and the hooks they hold built in 6.2b (ADR-0093); the choice controls, one box's label and the copy of the default
  button decided in 6.2d (ADR-0096); the controls with parts, dates and outputs, the captcha's place and a site's own
  type's control in 6.2e (ADR-0097); a repeatable group's rows, their names and ids, and its Add and Remove buttons in
  6.2f (ADR-0098); the registry, a form's theme, the view order, the class map and the base rules built in 6.3a by
  [ADR-0105](0105-the-theme-registry-and-the-base-rules.md); the dark scheme's class on the form or on any ancestor,
  and each theme's stylesheet scoped to its forms, in 6.3b ([ADR-0106](0106-the-default-theme-ships.md)); markup that
  reads without a theme (a rating's numbers, a scale along a line, a grid's column heads, a ranking's arrows, the
  lists' roles) in 6.3c ([ADR-0107](0107-markup-that-reads-without-a-theme.md)); `bootstrap5`'s stylesheet, its class
  map's plain button and outcome, and the file input's theme classes in 6.3d
  ([ADR-0108](0108-the-bootstrap5-theme.md)); the hooks of a form with rules in 6.4c
  ([ADR-0111](0111-rules-on-the-page.md)); the runtime's templates in 6.4e ([ADR-0113](0113-errors-on-the-page.md))
- Date: 2026-10-23
- Amended in 6.4g by [ADR-0115](0115-answer-recall-on-the-page.md): the views mark each visitor text that holds merge
  tags with `data-jfb-text`, written however it renders and `hidden` while empty, and an empty heading or text block is
  hidden
- Roadmap step: 1.7b (decided); built in 2.3 (the visual design and its tokens), 6.2 (rendering), 6.3 (the themes),
  6.4 (the runtime), 6.6 (the headless element) and 12.2 (the theme editor)

## Context

D9 renders forms on the server with Razor, enhanced by a small framework-free runtime, and gives the headless path
"the same markup contract and the same runtime". §4.9 asks for theme views in Jiron Form Builder's own layout, fixed
here without mirroring any other product's conventions, and proposes `Views/JironFormBuilder/<theme>/` with
`Form.cshtml`, `Page.cshtml`, `Group.cshtml`, `Summary.cshtml`, `Outcome.cshtml` and `fields/<type>.cshtml`, plus
per-form overrides. Step 6.3 ships three themes: `default` (custom properties, an opt-in dark scheme, the error
summary, character counters, styled native controls, choice cards), `unstyled` (semantic markup and classes only) and
`bootstrap5` (Bootstrap 5 classes, Bootstrap itself not shipped), resolved per form, then theme, then default, with
versioned view contracts and right-to-left layouts. §4.14 makes view names, the contract and the custom properties
public surface, and each view names its contract version. §4.12 caps the default theme's CSS at 10 KB gzip; §4.13
puts every theme under WCAG 2.2 AA; 12.2 lets editors make library themes from tokens without code.

ADR-0004 fixed the parts the runtime needs from the markup: deterministic instance ids (`jfb-1`, `jfb-2`), field ids
`<instance>-<alias>`, `novalidate` with the constraint attributes in place, nothing per visitor in the markup, no
inline script or style. ADR-0002 fixed what the projection holds; ADR-0012 the honeypot; ADR-0014 answer recall.

## Options

1. **A set of views per theme.** Free to differ, but three themes would mean three copies of every field's markup,
   three places to fix an accessibility bug, and a runtime that must cope with three structures.
2. **One set of views, themed by a class map, a stylesheet and tokens,** with view overrides for the rare theme or
   site that needs other markup. The runtime and the headless element meet one structure.

## Decision

**Option 2.**

### Themes

- **A theme** is registered in code through `IFormBuilderTheme`: an alias, a display name, a stylesheet (or none), a
  class map, its token defaults, whether it offers the dark scheme, and its view folder.
- **Built in:** `default`; `unstyled` (no look and no extra classes, only the functional base rules below);
  `bootstrap5` (a class map onto Bootstrap's classes and a small stylesheet for what Bootstrap lacks, such as the
  error summary and choice cards; Bootstrap itself is the site's).
- **Library themes** (`jfbTheme`, 12.2) are a base theme plus tokens and custom CSS. They add no views of their own; a
  site may still give one a view folder.
- **The functional base rules** ship with every theme, `unstyled` included, in a small stylesheet that `<jfb-assets>`
  and the headless element `<jfb-form>` always include: the honeypot's container kept out of view, visually hidden
  text for screen readers, and `[hidden]` honoured. They need no JavaScript, and no theme can leave them out.
- **A form's theme** is its `appearance.theme` alias (ADR-0002), else the default theme of the General section for the
  form's site (ADR-0008), whose code default is `default`. A form picker can override it per placement (6.7). A
  theme's alias is fixed once it exists (its name can change); a form whose theme is gone renders with `default`, and
  the form check says so. The form row copies the published theme alias (`jfbForm.ThemeAlias`) for the library's "used
  by" list and its delete guard, which also counts the General defaults that name the theme.

### Views

```text
Views/JironFormBuilder/<theme>/
  Form.cshtml          the <form> element, the honeypot, the model data, the pages
  Page.cshtml          one page: its title, description and groups
  Group.cshtml         one group: a fieldset when it has a title, its columns
  Field.cshtml         the wrapper of every field: label or legend, help, the control, the error
  fields/<type>.cshtml the control of one field type (fields/email.cshtml, fields/radio.cshtml, ...)
  Navigation.cshtml    back, next and submit
  Progress.cshtml      the steps or the bar
  ErrorSummary.cshtml  the list of errors with links to the fields
  Summary.cshtml       "check your answers", and the copy of the answers after submitting
  Outcome.cshtml       the message after submitting
  Notice.cshtml        closed, not yet open, full, members only, "the form has changed"
  _forms/<formAlias>/  per-form overrides, with the same file names
```

- **Resolution,** for each view: the form's override in the theme's folder, then the theme's folder, then the base
  theme's folder (for a library theme), then `default`. The built-in views ship in the Razor class library under the
  same paths, so a site file at the same path overrides one view without copying the rest.
- **Custom field types** ship their own `fields/<type>.cshtml`, found the same way.
- **View models** (`FormViewModel`, `PageViewModel`, `GroupViewModel`, `FieldViewModel` and the per-type models) are
  public types in Web and part of the public surface (ADR-0010). They carry resolved texts, ids, names, current
  values, errors, the visibility each rule gives, and the theme's classes, so a view never computes anything.
- **The contract header:** every built-in view starts with `@* jfb-view: <Name> <version> *@` (for example
  `@* jfb-view: Field 1 *@`). The version goes up when the view's model or required hooks change; the upgrade notes list
  every view whose version changed, so a site with an override knows what to check (§4.14). In Development, the
  renderer logs a warning once per view when an override declares an older version.

### The markup contract

What the runtime, the headless element and site CSS may rely on, in every theme:

| Element | Contract |
| --- | --- |
| The form | `<form class="jfb-form jfb-theme-<alias>" id="<instance>" data-jfb-form="<key>" data-jfb-revision="<n>" data-jfb-api="<public API base>" data-jfb-contract="1" lang="<culture>" dir="ltr or rtl" method="post" novalidate>`; `action` and `enctype` as ADR-0004 sets them |
| The model data | one `<script type="application/json" data-jfb-model>` per form with what the runtime needs from the projection (rules, checks, recall templates, field kinds, culture patterns, ADR-0014); data, never executed, so a CSP needs nothing for it |
| The runtime's templates | one `<template data-jfb-templates>` per form with the error summary as the `ErrorSummary` view renders it for one empty link and an error paragraph with the error slot's classes, which the runtime copies to show errors in the theme's markup ([ADR-0113](0113-errors-on-the-page.md)); inert, so nothing in it is posted, read out or styled |
| A page | `<section class="jfb-page" data-jfb-page="<key>">`; pages other than the current one carry `hidden` |
| A group | `<fieldset class="jfb-group jfb-columns-<n>" data-jfb-group="<key>">` with a `<legend>` when titled, otherwise a `<div>` |
| A field | `<div class="jfb-field jfb-field-<type> jfb-span-<n>" data-jfb-field="<key>" data-jfb-alias="<alias>" data-jfb-type="<type>">` |
| Its parts | the control `id="<instance>-<alias>"` and `name="<alias>"`; the label `<label for>` (or a `<legend>` for groups of choices); help `id="<instance>-<alias>-help"`; the error `id="<instance>-<alias>-error" data-jfb-error`; the control's `aria-describedby` names the help and the error, and `aria-invalid="true"` marks an error |
| Names | several answers repeat the name; repeater rows are `<alias>[<row>].<child>`; parts of a name or address field are `<alias>.<part>` |
| Hidden by a rule | the element carries `hidden` and `data-jfb-hidden`; its controls are `disabled`, so they are never posted |
| A form with rules | the hooks the runtime shows them by ([ADR-0111](0111-rules-on-the-page.md)): `data-jfb-requires` on a control whose `required` follows its field; `data-jfb-mark="required"` or `"optional"` on a label's mark, the field's state it shows in, `hidden` in the other; `data-jfb-disabled` on a control that stays disabled when its field shows; `data-jfb-required-note` on the note that explains the asterisk, `hidden` while none shows |
| A text that recalls answers | the element that shows a visitor text holding merge tags carries `data-jfb-text="<owner key> <name>"` (`FormBuilderHtml.Text`, a label's text a `<span>` of its own through `FormBuilderHtml.Label`, a placeholder's hook on its control) and holds that text alone; it is written however the text renders, `hidden` while empty, a progress step with `data-jfb-fallback` instead; the runtime renders it again as the visitor answers ([ADR-0115](0115-answer-recall-on-the-page.md)) |
| The error summary | `<div class="jfb-error-summary" data-jfb-summary role="alert" tabindex="-1">` with a heading and one link per error to the control's id |
| Live messages | one `<div data-jfb-live aria-live="polite">` per form (places left, the changed form, saved drafts) |
| Navigation | `<div class="jfb-navigation">` with `<button type="submit" name="jfb-nav" value="back\|next\|submit" data-jfb-nav="…">` |
| The outcome | `<div class="jfb-outcome" data-jfb-outcome tabindex="-1">`, which receives focus |
| The honeypot | `<div class="jfb-hp" aria-hidden="true">` with its label and input (ADR-0012) |
| A consent field | its checkbox, plus `<input type="hidden" name="jfb-consent-<alias>" value="<hash>">` holding the `jfbConsentText` hash (SHA-256 of the culture and the wording shown), which is the same for every visitor (ADR-0009) |
| A members-only form | `<input type="hidden" name="jfb-challenge" value="…">` with the member-bound challenge (ADR-0012); an override must keep it |

- **State lives in attributes** (`hidden`, `disabled`, `aria-invalid`, `aria-current="step"`, `data-jfb-busy`), so
  themes style states without classes the runtime would have to manage.
- **Rules without JavaScript:** the server renders every element in the state its rules give for the answers it has
  (the defaults on the first render). A rule that depends on an answer on the same page takes effect when the page is
  posted: the server renders the page again with the element shown and, when it is required, asks for it with an
  ordinary error. With JavaScript, the runtime applies rules as the visitor types.
- **Classes** are `jfb-` plus a block name (`jfb-form`, `jfb-page`, `jfb-group`, `jfb-field`, `jfb-label`, `jfb-help`,
  `jfb-error`, `jfb-control`, `jfb-choices`, `jfb-choice`, `jfb-navigation`, `jfb-button`, `jfb-button-primary`,
  `jfb-progress`, `jfb-summary`, `jfb-outcome`, `jfb-notice`) and modifiers of the form `jfb-<block>-<variant>`
  (`jfb-field-email`, `jfb-span-2`, `jfb-choices-cards`). A theme's class map adds its own classes to these slots
  (`bootstrap5` adds `form-control` to `jfb-control`, `btn btn-primary` to `jfb-button-primary`); it never removes
  ours, so site CSS and the runtime work in every theme.
- **The headless element** `<jfb-form>` renders the same markup from the projection in the light DOM, so the themes,
  site CSS and the runtime behave as on a Razor page (6.6).
- **A contract test** renders a kitchen-sink form (every field type, every state) in every built-in theme and checks
  the hooks above; the same test runs against the element's output (6.3, 6.6).

### Styles and tokens

- **Scope:** every rule sits under `.jfb-form`; no theme styles the page, the body or another element outside a form,
  and no theme resets the site's styles.
- **Tokens** are CSS custom properties named `--jfb-<group>-<name>` (`--jfb-color-accent`, `--jfb-color-error`,
  `--jfb-space-3`, `--jfb-radius-control`, `--jfb-font-family`, `--jfb-focus-ring`). Phase 2 (2.3) fixes the set from
  its design; from the first prerelease the set is public surface, and a token is removed only at a major (ADR-0010).
  A site sets them on `.jfb-form` or on any ancestor.
- **Fonts:** system font stacks, or `inherit` for "use the site's font"; never a font from another host.
- **Logical properties only** (`margin-inline-start`, `inset-inline-end`), so `dir="rtl"` mirrors every layout; a lint
  check refuses physical `left` and `right` in the theme CSS (6.3). Directional icons are mirrored.
- **Dark scheme:** opt-in per theme (a class `jfb-scheme-dark`, or `jfb-scheme-auto` to follow `prefers-color-scheme`),
  never forced on a site.
- **Motion:** transitions only where they explain a change (a page step, an error appearing), and none under
  `prefers-reduced-motion`.
- **Custom CSS** of a library theme is scoped to the form root and passes through the sanitiser's CSS parser (§4.10).
- **Budgets and checks:** the default theme's CSS stays within 10 KB gzip (§4.12); every preset passes WCAG 2.2 AA
  contrast, focus visibility and target sizes, checked by axe and by contrast tests over the token values (6.3, 12.2).

### Email layouts

Email layouts (`jfbEmailLayout`) are not themes: mail clients need tables and inline styles, so they have their own
renderer (8.2). A layout may take its colours from a theme's tokens.

## Consequences

- One structure to make accessible, one for the runtime and the element to understand, and one place to fix a field's
  markup; themes differ by classes, styles and tokens.
- Sites override one view instead of copying a theme, and the contract header tells them when an upgrade changes what
  they copied.
- The class map lets `bootstrap5` fit Bootstrap sites without a second set of views; a theme that needs truly
  different markup still can, through its own view folder.
- The model data travels as an inert JSON block, so cached pages and strict CSPs keep working.
- §4.9's rendering bullet lists the view files, the resolution order and the contract header (WORKLOG).
- **Revisit** if a theme needs markup the class map cannot reach in more than a few views, or if browsers make
  scoped styles (`@scope`) reliable enough to replace the `.jfb-form` prefix.
