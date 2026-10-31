# 0093. The Razor rendering

- Status: accepted; builds the rendering of [ADR-0004](0004-rendering-and-submission-paths.md) and the views and the
  markup contract of [ADR-0015](0015-themes-and-the-markup-contract.md) for the default view folder, and amends how
  the automatic inclusion of [ADR-0092](0092-serving-the-website-assets.md) counts "once"; the visitor texts with
  tags, the site's required-marker style and labels, members-only forms and the note while the migrations are pending
  decided in 6.2c by [ADR-0095](0095-visitor-texts-members-only-forms-and-the-pending-note.md); the choice types'
  controls and their options in 6.2d by [ADR-0096](0096-the-choice-controls.md); the controls with parts, dates and
  outputs, and the page's answers read as the pipeline reads them, in 6.2e by
  [ADR-0097](0097-the-part-date-and-output-controls.md); a repeatable group's rows, their rules and the post state's
  rows in 6.2f by [ADR-0098](0098-the-repeaters-rows.md); the hidden inputs a post carries, the page a post moves to
  and the page after a sent post in 6.2h by [ADR-0100](0100-the-post-without-javascript.md); the `theme` attribute, the
  views of a form's theme and the classes they write in 6.3a by [ADR-0105](0105-the-theme-registry-and-the-base-rules.md)
- Date: 2026-10-28; amended 2026-10-31 (6.8a: the host's default for a placement's assets)
- Amended in 6.4g by [ADR-0115](0115-answer-recall-on-the-page.md): a visitor text that holds merge tags carries its
  hook, and one that renders empty keeps its element, hidden
- Amended in 6.4h by [ADR-0118](0118-defaults-with-merge-tags-on-the-page.md): a field whose controls start from a
  default that holds merge tags carries `data-jfb-default`, on a first render and in a row just added, for the page to
  fill in
- Roadmap step: 6.2b (decided and built); 6.2c the visitor texts with merge tags, members-only forms and the note
  while the migrations are pending (ADR-0095), 6.2d the choice controls (ADR-0096), 6.2e the part, date and output
  controls (ADR-0097), 6.2f the repeater's rows (ADR-0098),
  6.2g the visitor messages, 6.2h the no-JS post, 6.2i its rows and moves, 6.3 the themes, 6.7 the form picker

## Context

ADR-0004 renders a form through one view component, called by the `<jfb-render>` tag helper and by
`Component.InvokeAsync`, with deterministic instance ids and nothing per visitor in the markup. ADR-0015 fixes the
views, their contract headers, a form's override folder, public view models that carry everything resolved, and the
markup contract that the runtime, the headless element and site CSS rely on. Section 4.9 names the tag helper
`<jfb-render form="alias|key" theme="…" context-…="…">` and the component `FormBuilderForm`. The form endpoint (6.1a,
6.1b) already answers what the runtime needs: the projection in the culture, the visitor texts' data and the consents'
hashes. The website specimen (`docs/ux/website/`) shows the markup the default theme styles.

Open were: how a page names a form; what renders when it cannot; where the page and the culture come from; what the
model data is; how a view finds the next view; what state a first render shows; which texts the package brings; how
`<jfb-render>`'s automatic assets and a `<jfb-assets>` in the layout's head agree, since Razor runs a view before its
layout; and what a hidden field and the `context-…` values put into the page.

## Options

**The model data:**

1. **A model of its own for the runtime.** Two shapes of one form, one for Razor pages and one for headless sites,
   and two places to keep in step.
2. **The form endpoint's answer, as it is.** One model for both paths; the runtime of 6.4 reads the same JSON whether
   it came in the page or from `GET forms/{keyOrAlias}`. Chosen.

**How a view finds the next view:**

1. **By name, from inside the view** (`Html.PartialAsync("Field")`), resolved by a helper at each call. Every view
   would compute paths, and an override could not see what was resolved.
2. **Resolved once, carried by the model** (`Model.Views.Field`, `field.ControlView`). A view only writes. Chosen.

**The automatic assets and `<jfb-assets>`:**

1. **Whoever runs first writes** (ADR-0092 as written). Razor runs the body before the layout, so a form in the body
   always wins and a `<jfb-assets>` in the layout's head writes nothing: the tags land in the body and the head's tag
   is useless.
2. **The form's inclusion decides when the page is written,** not when it runs. Chosen, below.

## Decision

- **Naming a form:** `<jfb-render form="…">` and `@await Component.InvokeAsync("FormBuilderForm", new { form = … })`
  take a key (a `Guid`, or its text with hyphens), an alias in any letter case, or a `FormBuilderFormReference`, the
  value the form picker gives (6.7), which reads as its key, so `form="@reference"` names the form too.
  `assets="false"` (`assets: false`) leaves the assets out and `assets="true"` asks for them; a placement that says
  neither follows the host's `Rendering:IncludeAssetsAutomatically` (§4.8, true unless the site turns it off, for a
  site that loads the assets itself; 6.8a). `context-<name>="…"` (`context: dictionary`) passes context values. The
  tag helper calls the view component, so the two are one path.
  The theme attribute comes with the themes (6.3) and the picker's placement (6.7).
- **What renders:**
  - a published form: the Form view;
  - a form that takes no entry now (ADR-0088): the Notice view in its place. A form not open yet shows the package's
    heading with the opening time in the site's time zone (the culture's full date and short time) and its text; a
    form closed or full shows its closed message as the whole notice, else the package's heading and text;
  - nothing, never a broken page, for a reference that names no published form (missing, unpublished, trashed),
    logged once per reference and process; for a value of another type, logged once per type; for an empty
    reference; and while the migrations are pending, where a signed-in backoffice user sees a note (ADR-0095).
- **Instance ids** are `jfb-1`, `jfb-2`, … in the order the forms render in a request. A field's control is
  `<instance>-<alias>` (a field without an alias takes its key), and the parts that refer to it add `-help`, `-error`,
  `-prefix`, `-suffix`, `-count` or `-confirm`; a group's description is `<instance>-<group key>-help`, the progress
  text `<instance>-progress` and the summary's heading `<instance>-summary`.
- **The page and the culture** come from Umbraco's published request: its content (whose site's settings apply) and
  its culture. Outside an Umbraco page there is no content, and the culture is the request's UI culture. Either is held
  to the site's languages as the public API holds it (ADR-0084), so the form, its model data and `lang` agree; `dir` is
  `rtl` for a right-to-left culture.
- **The form element** follows ADR-0015 with `action="<path base><path>#<instance>"` (no query string; the no-JS post
  of 6.2h decides what travels with it), `enctype="multipart/form-data"` only for a form with a field whose type
  stores files, `data-jfb-api` the public API's base below the path base, and `data-jfb-contract="1"`.
- **The model data** is the form endpoint's answer for the same culture and page, made by the same code, inside
  `<script type="application/json" data-jfb-model>`. Every `<`, `>`, `&`, U+2028 and U+2029 is written as a `\u`
  escape, which JSON allows only inside strings and reads back as the same character, so no text can end the element
  and the data parsed equals the endpoint's answer.
- **The views** ship in the Razor class library under `Views/JironFormBuilder/default/`: Form, Page, Group, Field,
  Navigation, Progress, ErrorSummary, Outcome, Notice and `fields/<type>`, each starting with
  `@* jfb-view: <Name> 1 *@`. For each view a form looks in `_forms/<alias>/` first, then in the folder (the themes'
  order comes with 6.3). MVC's view engine answers whether a path exists and keeps its answers, dropping them when a
  view changes under runtime compilation, so the package keeps no cache of its own. The model carries every resolved
  path, so a view never looks one up. A field type without a control view fails the render with the paths it looked
  at, as a missing partial does in MVC; a field whose type the site lacks renders nothing, as a hidden field does. A
  site's own views take the site's `_ViewImports.cshtml` and its tag helpers; the built-in views take none, so their
  markup is exactly as written.
- **Attributes:** Razor leaves out an attribute whose value is null or false but always writes a `data-*` attribute
  (false as `False`), so the views write the contract's flags and optional `data-jfb-*` values with
  `FormBuilderHtml.Flag` and `FormBuilderHtml.Attribute`, which are public for overrides. Razor hands `<jfb-render>`
  an attribute's value as the page's code gives it, an expression or text with one alike, and text written in the
  page as it is written; the tag helper takes each as it comes, as MVC's tag helpers do.
- **Contract versions:** the built-in views are version 1. In Development, a view found in the site's own folder
  (its source file exists in the content root) whose header names an older version, or none, is logged once per view.
- **View models** are public types in `Jiron.Umbraco.FormBuilder.Web.Rendering.Models` (ADR-0010): the form, its pages,
  groups and fields, the navigation, the progress, the error summary, the outcome, the notice, and a control model per
  built-in type that has settings to show (text, which email, phone and url share and which carries the repeated
  email's input; text area; number; heading; text block; image). They hold texts in the culture, ids, names, values,
  errors, states and classes; a rich text is sanitised HTML (section 4.10).
- **Controls:**
  - no `maxlength` or `minlength`: a browser cuts a pasted answer at `maxlength` without a word, so the counter says
    the limit ("Up to 40 characters") and the submission checks it;
  - a number with decimal places is `type="text" inputmode="decimal"`, since a number input reads a decimal comma
    differently from browser to browser; a number that may be below zero takes no `inputmode`, since some phones'
    number keyboards have no minus sign; `min`, `max` and `step` go only on `type="number"`;
  - the repeated email's input is named `<alias>.confirm`;
  - an image renders only for a media item with a URL, with its width and height, `loading="lazy"` and
    `decoding="async"`;
  - every label is a `<label>`; the types that group options take `<fieldset>` and `<legend>`, and one box its own
    label (ADR-0096); a name, an address and a signature take `<fieldset>` and `<legend>` with a label per part
    (ADR-0097).
- **The first render's state:** the rules (ADR-0061) run over the default values that hold no merge tag, with the
  site's clock and time zone, no member and no query value, so the markup is the same for every visitor. A default
  with tags is left for the browser to fill (`MergeTagPlace.DefaultValue`). What the rules hide carries `hidden` and
  `data-jfb-hidden`, its controls `disabled`; what a hidden page or group holds is hidden too and carries the same.
  What a rule requires carries `required`. The current page is the first page the rules show; the others carry
  `hidden`, and a page the rules hide is no step of the progress.
- **Answers, errors, notices and outcomes after a post** reach the component through `HttpContext.Items`, keyed by the
  form's key and the instance, as ADR-0004 decides; 6.2h's surface action fills them. The post's page shows when the
  rules show it, else the first page they show. Errors render inline, each with `aria-invalid` and its message in
  `aria-describedby`, and, while the form's validation summary is on, in the error summary with a link per error.
- **The package's words** (Back, Next, Send, "(optional)", the summary's heading, "Step 1 of 3", the notices, the
  repeated email's label) come in English and German, by the culture and then its parent, else English; section 14.2
  adds five languages. A form's own labels and messages win, then the site's labels (ADR-0095).
- **The required marker:** `optional` adds "(optional)" to each field that holds a value and is not required;
  `required` marks required fields with an asterisk hidden from assistive technology (the `required` attribute says
  it), and says once, above the first page, what the asterisk means; `none` marks nothing. `inherit` takes the General
  section's style, by default `optional` (ADR-0095).
- **The automatic assets** (amends ADR-0092): the form's inclusion is content that decides when the page is written,
  not when the form renders. `<jfb-assets>` writes its tags as it runs and marks the request; the form's inclusion,
  when the page is written, writes the tags only if nothing wrote them yet. Razor runs a view, then its layout, and
  writes the layout's head before the view's body, so a `<jfb-assets>` anywhere on the page wins and the tags appear
  once, where it stands; without one, they appear once, before the first form.
- **A hidden field renders no markup.** Its value is resolved when the entry is submitted, from its default, the
  member or a context value (ADR-0014), or taken from the query values the form lists, which travel with the post
  (ADR-0088). So no member value can reach a cached page.
- **Context values:** `context-<name>` for a name the form lists (`developer.contextValues`) renders
  `<input type="hidden" name="jfb-context-<name>" value="…">` in the form, for the runtime (6.4) and the no-JS post
  (6.2h) to send. A name the form does not list, or a value longer than 2,000 characters (ADR-0088), renders nothing
  and is logged. The values are part of the page's markup and the same for every visitor of the page; the docs say
  that per-visitor values belong elsewhere.
- **Left to their steps:** the honeypot and the member-bound challenge (7.1; the honeypot's markup came with the base
  rules in 6.3a, ADR-0105), the route token (6.2h), a theme's classes and stylesheet (6.3), and the controls of the
  other types (6.2d, 6.2e, 6.2f).

## Consequences

- A Razor page and a headless site read one model, so the runtime cannot meet two shapes of one form.
- A form can be put into any template, block or partial without knowing where the layout puts its assets.
- The views are public surface with versioned headers: a change to a view's model or hooks raises its version and the
  upgrade notes name it (section 4.14).
- The integration tests render pages through the full Umbraco pipeline: the test project compiles its own views, and a
  route-hijacking controller shows them for a test page type.
- **Revisit** when Umbraco offers its own way to put a package's tags into a page's head.
