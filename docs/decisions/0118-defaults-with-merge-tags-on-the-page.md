# 0118. Defaults with merge tags on the page

- Status: accepted; builds on [ADR-0093](0093-the-razor-rendering.md) (the server leaves a default with tags for the
  browser to fill), [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md) (what the form endpoint
  gives the browser for its merge tags) and [ADR-0115](0115-answer-recall-on-the-page.md) (the merge tags' chunk);
  amends ADR-0085 (the endpoint lists the default values that render and each text's refused tags), ADR-0093 and
  [ADR-0015](0015-themes-and-the-markup-contract.md) (`data-jfb-default` on a field that starts from such a default),
  [ADR-0110](0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md) (the frame loads a marked form's
  logic at once) and ADR-0115 (a refused tag is written as it stands on the page too, and the chunk holds the
  defaults' filler)
- Date: 2026-10-30; amended 2026-10-31 (6.8b: only the control named as the field takes the default)
- Roadmap step: 6.4h (decided and built); 6.4o adds rows in place, whose new rows start from their defaults too; 6.5
  builds prefill, which wins over a default

## Context

A field's default value may hold merge tags of the sources that are the same for every visitor of a page (`form`,
`page`, `site`, `date`, `dictionary`) and the query values the form allows (`query.<name>`, `developer.queryValues`),
as `MergeTagPlace.DefaultValue` lists them. The page may be cached, so the server writes no such default into the
markup: ADR-0093 leaves the control empty for the browser to fill. Nothing filled it yet, so a link that should open a
booking with its workshop chosen opened it empty.

Building it showed a gap in 6.4g's answer recall too: a tag of a source a text's place refuses (`{{ member.name }}` in
a label) is written as it stands by the server, but the browser cannot tell it from a tag no source knows, which
renders empty, so a label with such a tag and a recalled answer read differently on the page. Nothing refuses
publishing such a form before 9.5b's form check.

Open were:

1. which defaults the page fills in, and where;
2. what the browser renders a default with, and which values are the page's own;
3. when it fills them in, given that the form's logic loads on its first interaction;
4. what it does with a control the visitor has changed already;
5. how the browser learns which tags a place refuses;
6. how the runtime's budget (section 4.12) holds the filler.

## Options

1. **Which defaults.**
   - Every field whose model has a default with `{{`: the page would also fill a field after a post, whose answers
     stand, and could not tell a first render from a page sent back.
   - **The fields the server marks: `data-jfb-default` on the field's element where its controls start from a
     default that holds a tag,** on a first render and in a row the visitor just added or the group's least added,
     never after a post. Chosen: the server decides it as it decides each control's start (`RowsOf`, `StartOf`).
2. **The values.**
   - All from the endpoint: a cached page's answer would hold the address it was cached for.
   - All from the page: the page knows no site name, page name or dictionary item.
   - **The query values from the page's own address, as the rules read them (the names the form allows, a page sent
     back carrying its own), everything else from the endpoint,** as visitor texts take theirs. Chosen.
   - `date.now` from the page's clock was weighed: it would keep a cached page's date current, but the culture's date
     and time in the site's zone needs a second formatter, about 0.25 KB, and the server's own "default to today"
     and the visitor texts' `date.now` print the render's date into a cached page already. So a default's `date.now`
     is the endpoint's, one rule for the page. **Revisit** with the output cache's lifetimes.
3. **When.**
   - On the first interaction, with the rest of the logic: the default would arrive as the visitor starts typing.
   - **At once, for a form with a marked field:** the frame loads its logic as it enhances it, and the defaults are
     filled before the rules first decide, so rules, calculations and recalled texts start from them. Chosen.
4. **A changed control.** The visitor may type or choose before the chunk arrives. A field none of whose controls
   the visitor changed from what the server wrote (`defaultValue`, `defaultChecked`, the `defaultSelected` option, or
   the first) is filled; any other is left. Chosen over filling regardless, which would overwrite an answer.
5. **Refused tags.**
   - The browser keeps each place's built-in sources: a site's own source is refused by its traits, which the
     browser does not know.
   - **The endpoint lists, per text, the paths its place refuses for their source (`refused`), which the browser
     writes as they stand.** Chosen; `ValuesAsync` collects them as the path check refuses them.
6. **The budget.** The filler as a chunk of its own made the template engine a third chunk that two features share,
   a file and its banner more (41.43 KB of 40). **The filler is loaded through the merge tags' chunk**
   (`import('./recall')`, which re-exports it), so recall, the template engine and the filler stay one chunk without
   a manual group, whose helper chunk would cost a file too; with the terser pass of
   [ADR-0119](0119-a-terser-pass-for-the-website-runtime.md), the runtime with every feature is 39.89 KB.

## Decision

### The server

- **The form endpoint** (`mergeTags.texts`) lists each field whose default value renders other than as it is written,
  as `{owner, text: "defaultValue", context: "text"}`: one entry for a list's every text. Its values join
  `values.text`, but a `query.*` path's, which the page gives (`ValuesAsync` leaves them out in a default value as it
  leaves answer recall's out in a visitor text). A default that only holds tags its place refuses renders as it is
  written and is not listed. `mergeTags` is no longer `null` for a form whose only tags are in defaults.
- **Every text entry** that names a tag its place refuses for its source carries `refused`: those paths, each once, in
  the template's order. The recall form names no field for a default, so a form whose only tags are in defaults never
  loads answer recall.
- **The markup.** A field whose controls start from a default with a tag carries `data-jfb-default` on its element
  (`FieldViewModel.FillsDefault`), and its controls are written empty, as before.

### The runtime

- **The frame** loads a form's logic as it enhances it when the form holds a `[data-jfb-default]`, any other on its
  first interaction as before.
- **The filler** (`defaults.ts`, in the merge tags' chunk) runs once in `attach`, before the rules first decide: for
  each marked field it renders each text of its default (the template as written when the endpoint does not list it),
  with the query values of `contextOf` (one text, a list of several, or the empty text, as the server's source gives
  them), the endpoint's other values, and a refused path written as it stands; drops the mark; and, unless the
  visitor changed one of its controls, fills only the controls named as the field (a row's by its row's path), as the
  server does: a choice's "Other" text, a name's or an address's parts and an email's repeat stay empty (6.8b). It
  writes the first text into a text control, ticks the boxes and radio buttons
  whose values it holds, and selects the matching options, leaving a choice whose rendering no option has as it was
  and never touching a hidden input. A form whose filler cannot load is left to the post without JavaScript.
- **Answer recall** writes a visitor text's refused tags as they stand, as the server writes them, and never takes a
  default for a visitor text.

### Design (ADR-0019, ADR-0020)

- **`design-taste-frontend`:** reading this as a booking reached from a link, for any visitor, trust-first, calm;
  dials 3 / 2 / 5. What applies passed its pre-flight: no motion on a value the page sets as it shows, the layout the
  same with the default and without, link texts that say where each goes.
- **`impeccable` (Operate, `harden`):** a value from the address is held to the form's allowed names and the field's
  options, never markup; a visitor's own choice is never overwritten; a page without the query chooses nothing. The
  demo's events page links each workshop to the booking with it chosen ("Book a place at Bike repair basics"), each
  link's text its own. The bundled detector finds nothing in the changed view and runtime files.
- **`emil-design-eng`:**

| Before | After | Why |
| --- | --- | --- |
| The default could be filled on the first interaction | It is there as the form shows | A value that arrives while the visitor types reads as the page fighting them |
| The filled choice could animate into place | It is simply checked | It is the page's starting state, not an event |
| A control the visitor changed could take the default | It keeps the visitor's answer | Their input always wins |
| A refused tag rendered empty on the page | It reads as the server writes it | One text on the page and after a post |

### The proof

`TemplateValuesTests` hold `ValuesAsync`'s refused paths and a default's query values to the server's rules;
`PublicFormRecallTests` holds the endpoint's answer for defaults (listed or not, a list, a refused tag, no query
value) and the recall fixture's text with a refused tag beside a recalled one, which `recall-endpoint.test.ts`
renders as the server does; `FormPostTests.Defaults` holds the mark on a first render and in a row just added, never
after a post. `defaults.test.ts`, `engine.test.ts`, `frame.test.ts` and `recall.test.ts` cover the runtime's part, and
`defaults-on-the-page.spec.ts` the demo in Edge.

## Consequences

- A link can open a form with its choices made, on a cached page too, as long as the form allows the query value.
- A page without JavaScript shows such a default empty, as ADR-0093 decided.
- Rows added in place (6.4o) start from their defaults through the same filler; prefill (6.5) comes before a default.
- Every visitor text that holds a refused tag now reads the same on the page and after a post.
