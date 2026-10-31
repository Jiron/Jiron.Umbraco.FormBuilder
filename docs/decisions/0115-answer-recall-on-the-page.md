# 0115. Answer recall on the page

- Status: accepted; builds on [ADR-0068](0068-answer-recall.md) (what a visitor text recalls, and `recallAnswers`),
  [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md) (what the form endpoint gives the browser
  for its visitor texts) and [ADR-0109](0109-reading-answers-in-the-browser.md) (the answers as Core's types read
  them); amends [ADR-0095](0095-visitor-texts-members-only-forms-and-the-pending-note.md) (a visitor text with tags that
  renders empty keeps its element, hidden), [ADR-0093](0093-the-razor-rendering.md) and
  [ADR-0015](0015-themes-and-the-markup-contract.md) (the views write `data-jfb-text` on such a text, and an empty
  heading or text block is hidden), [ADR-0110](0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md) (the
  merge tags leave the form chunk), [ADR-0114](0114-calculations-on-the-page.md) (the rules' pass also renders the
  texts, on a form with neither rules nor calculations too) and [ADR-0112](0112-checks-and-messages-on-the-page.md)
  (a summary link names a label as it recalls the answers now)
- Amended in 6.4h by [ADR-0118](0118-defaults-with-merge-tags-on-the-page.md): a tag of a source the text's place
  refuses is written as it stands on the page too, as the endpoint lists it, and the merge tags' chunk holds the
  defaults' filler
- Date: 2026-10-30; amended 2026-10-31 (6.8b: a recalled answer's line breaks follow it)
- Roadmap step: 6.4g (decided and built); 6.4h fills defaults with merge tags, 6.4o adds and removes rows in place,
  6.5 moves through pages in the browser

## Context

A visitor text may recall earlier answers: "Thanks, {{ field.name.first }}." as a label, a help text that names the
workshop chosen above it, a text block that lists the guests (ADR-0068). The server renders such a text for the answers
a page holds, on a first render and after a post (ADR-0095), and the form endpoint gives the browser each text's
template, the values of its other tags and a cut-down form to recall from (ADR-0085). Nothing in the browser used them
yet, so a label kept the answers of the last render while the visitor typed new ones, and the error summary named a
field by its template's text, tags and all, when the field's label was not on the page.

Open were:

1. how the runtime finds the element that shows a text, and what it writes there;
2. what a text that renders empty leaves on the page, which the server now leaves out;
3. how the merge tags reach the page, since `template.ts` and `recall.ts` are a quarter of the logic package, and few
   forms have such a text;
4. when the texts render, and over which answers;
5. which label the error summary names.

## Options

1. **Finding a text's element.**
   - By the owner's element (`[data-jfb-field]` and the like) and the view's structure (a label's first text, the help's
     id): every theme's views would have to keep the default theme's structure, and a progress step stands outside its
     page.
   - **A hook on the element that shows the text, with the text's owner and name, written only for a text with tags.**
     A theme's view keeps it with one helper; an element that shows something besides the text gives the text a
     `<span>` of its own. Chosen.
2. **An empty text.**
   - Leave its element out, as the server does: the runtime would have to know where each element goes, in every theme.
   - **Keep the element, `hidden` while the text is empty**, on the server and in the browser. Chosen. A group whose
     title has tags is a fieldset with its legend however the title renders, and its description is named by
     `aria-describedby` even while hidden, so the page's structure never changes with the answers.
3. **The merge tags' code.**
   - In the form chunk: every form would pay for it, and the runtime's 40 KB budget for every feature (section 4.12)
     would not hold it beside the others.
   - **Their own chunk**, as the calculation language (ADR-0114): `packages/logic` exports `@jfb/logic/template` and
     `@jfb/logic/recall`, and its index only their types, so nothing the form chunk imports reaches them. Chosen. The
     index's re-exports had kept part of `template.ts` in the form chunk though nothing called it; that weight leaves
     with the move.
4. **When and over what.**
   - On `blur`, or a debounced timer: the label of the next field would lag behind what the visitor sees typed.
   - **In the rules' pass,** on every `input` and `change` in one microtask (ADR-0111, ADR-0114), over the reading the
     rules decided and the state they gave, as the server renders a page over its read answers and its rules' state.
     Chosen.
5. **The summary's label.**
   - The label as the page shows it, as 6.4d reads it: a box with a text of its own does not show its field's label,
     and the model's label is the template.
   - **The label as it recalls the answers now,** then the page's, then the model's. Chosen.

## Decision

### The markup

- **The hook.** Every element that shows a visitor text with tags (ADR-0095's list: a page's and a group's title and
  description, a field's label, help and placeholder, a text setting of its type) carries `data-jfb-text="<owner key>
  <name>"`, such as `data-jfb-text="d0000000-0000-4000-8000-000000000207 label"`, where the name is the text's in the
  form endpoint's `mergeTags.texts`. The element holds the text and nothing else: a label's text is a `<span>` before
  its mark, in a `<label>`, a `<legend>` and a box's own label alike; a placeholder's hook is on its control, whose
  attribute the runtime writes. A progress step that shows a page's title carries the hook too, with
  `data-jfb-fallback` holding what the step shows without a title ("Page 2").
- **An empty text.** Such an element is written however the text renders, `hidden` while it renders empty, and a step
  shows its fallback instead. A heading or a text block that renders empty is hidden as well, with tags or without, so
  the page never holds an empty heading.
- **For views.** The view models carry `Recalling`, the names of their texts that hold tags;
  `FormBuilderHtml.Text(model, name)` writes the hook, `FormBuilderHtml.Label(field)` a field's label with its hook,
  and `FormBuilderHtml.Text(step)` a step's. A site's own type's view writes
  `FormBuilderHtml.Text(Model, "settings.<path>")` on the element that shows such a setting, and the runtime updates
  it. A view without the hooks keeps working: the server's render stands until the next post.

### The runtime

- **The chunk.** A form whose recall form names a field (ADR-0085: only a field a text recalls carries its alias there)
  loads `chunks/recall-<hash>.js` before it is enhanced, once for the page: the runtime's renderer with the template
  engine and answer recall. Any other form, and a form without `mergeTags`, never loads it. A form whose chunk cannot
  load is left to the post without JavaScript, as one whose calculation language cannot (ADR-0114).
- **The texts.** The renderer parses each text of `mergeTags.texts` once, from the endpoint form's property its name
  says, in its context and with the path check of its owner, and keeps only those with a `field.*` tag; the others never
  change. On each pass it renders them with `recallAnswers` over the reading (each field's values as its type read them,
  a site's own type's cleaned texts, a repeatable group's rows while their number keeps its limits, a row's child the
  rules hide in that row left out), the rules' hidden elements, the culture's words and formats and the endpoint's
  values for the other tags.
- **On the page.** Each hook gets its text when it changes, and `hidden` follows whether it is empty. A plain text is
  written as text. A rich text never becomes markup from a string (section 4.10): its rendering is parsed in an inert
  `<template>`, where nothing runs or loads, and only its texts and the values of the attributes the page's elements
  already have (never an `on…` one) are copied into the page's elements, never a node. A rendering differs from the
  server's markup only there, since the template is the editor's sanitised text and every value is escaped for HTML
  (ADR-0064, ADR-0085), and in the line breaks an answer recalled through `| html` brings (6.8b): a `<br>` is the one
  element a rendering may add or drop, made anew and bare, never taken from it, so the text follows an answer that
  gains or loses a line; one whose other elements differ is left as the page has it. This is the second reviewed use of an
  inert `<template>` in website code, after form_changed's (ADR-0004), and the lint names both. The rules' pass, now
  also on a form with neither rules nor calculations, renders the texts after the results, so a calculation a text
  recalls is the one shown.
- **The summary.** A link names a field by its label as the texts render it for the answers it checked, and an empty
  one as none; a field whose label holds no tag keeps 6.4d's label as the page shows it.

### Design (ADR-0019, ADR-0020)

- **`design-taste-frontend`:** reading this as words inside public website forms that follow the visitor's own
  answers, for any visitor, trust-first, calm and restrained, on the themes' own styles; dials 3 / 2 / 5. What applies
  passed its pre-flight: no motion on text the keyboard changes, the page's structure the same whatever the answers,
  no dashes as decoration in the demo's copy.
- **`impeccable` (Operate, `harden`):** a recalled answer is escaped and never markup; an empty text hides its element
  instead of leaving an empty legend or a blank line; a long answer wraps inside its label. Looked at in Edge on the
  demo's workshop booking at 375 px in English and 1440 px in German, first rendered, then with a long name
  ("Anna-Katharina Müller-Lüdenscheidt" in German), a workshop and a guest, then sent: the question names the visitor
  and wraps where the name is long, the help names the workshop, the closing text lists the guests and says "none"
  once they are hidden again, and the page the server sends back says the same. (The send itself is refused for a
  member until 14.3 builds members' submissions, ADR-0095.) The bundled detector finds nothing in the changed views
  and runtime files.
- **`emil-design-eng`:**

| Before | After | Why |
| --- | --- | --- |
| A recalled text could fade or slide to its new words | It changes at once | A text the keyboard drives changes with every key; motion there is delay |
| A label's words and its mark were one run of text | The words have a `<span>` of their own before the mark | The runtime rewrites the words, never the mark, so "(optional)" or the asterisk stays put |
| An empty text left an empty legend, heading or line | Its element is `hidden` while the text is empty | Nothing on the page that says nothing, and no blank gap where it was |
| Every answer rewrote every text | Only a text that differs is written | Assistive technology and the page's own observers see real changes only |
| A changed label could be announced | No live region | The visitor made the change and sees it; announcing each key would talk over them |

### The proof

`Rendering/RecallFixtureTests` posts a form of recalled texts of every kind without JavaScript, in English and German,
and writes each page first rendered and rendered again to `tests/fixtures/rendering/recall-<language>.json`;
`recall.fixture.test.ts` makes every hooked text stale and renders them again as the runtime does: the server's page,
element for element, empty and hidden ones included.

## Consequences

- A label, a help text or a text block follows the answers as the visitor types, and says what the server says after
  a post; a page without JavaScript is unchanged but for an empty text's hidden element.
- A theme's own views add the hook to keep their texts live; the built-in views' contract stays at version 1, as
  nothing has been released (ADR-0096).
- Rows added and removed in place (6.4o) and pages moved in the browser (6.5) bring texts on other rows and pages
  that the reading already covers.
- **Revisit** if a site's own type needs its texts rendered in a place no hook can mark, such as an attribute other
  than a placeholder.
