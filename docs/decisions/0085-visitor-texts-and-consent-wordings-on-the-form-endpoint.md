# 0085. Visitor texts and consent wordings on the form endpoint

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (a visitor text's
  values come beside its template, not in it), [ADR-0068](0068-answer-recall.md) (what the browser is given for
  recall, and the text blocks' texts take merge tags) and [ADR-0070](0070-storing-answers-and-reading-them-back.md)
  (the consent wording is sanitised before it is hashed and stored); extends
  [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (the form endpoint's answer)
- Date: 2026-10-28
- Roadmap step: 6.1b (decided and built); 6.1c's `options` endpoint gives a cascading field's options, 6.2 renders
  the same texts on the server, 6.4 renders them in the browser as the visitor types, 6.1e compares a posted
  consent's hash

## Context

A visitor text (a label, a help text, a placeholder, a page's or a group's title or description, a text block) may
hold merge tags (ADR-0014). Its `field.*` tags recall the answers given so far, which only the browser knows before a
submission; every other tag names what is the same for every visitor of the page (`form`, `page`, `site`, `date`,
`dictionary`). ADR-0068 decided that the browser renders such a text with `renderTemplate`, recall's values coming
from `recallAnswers` and the rest from the server, and that it gets a `RecallForm` with what recall needs. ADR-0064
lets neither runtime use culture data of its own, so the browser needs the culture's formats too. A consent's wording
is rendered whole on the server (ADR-0070) and the form posts its hash (ADR-0009), so the browser must be given both.

Open were: which texts are visitor texts; how the browser learns which hold tags and what their other tags are worth;
how much of the form recall needs; and whether the wording a consent shows, its hash and what the entry stores are one
text, given that rich text passes the sanitiser on its way out (ADR-0084).

## Options

1. **Which texts.** Every text of the projection would make an option's label or a validation message a template,
   which nothing offers editors. The texts ADR-0068 names, each with its owner, are chosen: a page's and a group's
   title and description, a field's label, help and placeholder, and each public text setting its type marks as
   taking merge tags (`SupportsMergeTags`), except a consent's text, which is its wording. The built-in heading's text
   and paragraph's text take them from now on, as ADR-0068's text blocks.
2. **The values.** Resolving the tags into the text (ADR-0002's wording) would need the engine's escaping and its
   filters run backwards on a template; sending the values beside it lets both runtimes render the same template with
   the same engine. Chosen. A value per text repeats a `page.name` in every label; one table per context (a site's
   own source may answer HTML and plain text differently) is chosen.
3. **How much of the form.** The whole `RecallForm` repeats every label and option of the form; recall needs the
   order of the elements a text names or is owned by, and the display of the fields it recalls. The cut-down form is
   chosen: the pages, groups and fields that own a text with tags or are recalled, with their holders, in the form's
   order, and the display data only for the recalled fields (a repeatable group whole when one of its fields is).
4. **The consent wording.** Hashing the wording before the sanitiser and storing it so leaves the entry holding a
   text that differs from what the visitor read whenever the sanitiser changed something; sanitising it in
   `ConsentWordings`, before it is hashed, makes the shown text, the hash and the stored text one. Chosen.

## Decision

### Visitor texts

The visitor texts of a form, each with its owner, are those of option 1, collected from the visitor's form, so after
the culture's text and a dictionary item are read (an item may bring tags of its own) and after the sanitiser, each
parsed in its context: rich text as HTML, plain text as text. Both runtimes render the sanitised template; 6.2 renders
the visitor's form's text on the server too. A text's own text and a tag the place refuses (a `member.*` in a visitor
text, a field that does not come before the text) are written as they stand in both runtimes, so a text renders other
than as it is written only when it holds a tag the place takes, or holds more tags or filters than a text renders and
its output ends there. The answer names only the texts that render.

`IFormBuilderMergeTagService.ValuesAsync` gives what a browser renders a template with: whether it renders other than
as it is written, the values of every path but recall's (`field.*` in a visitor text), each resolved once as
`RenderAsync` resolves it, and the fields its recall tags name, refused or not. A refused tag gets no value; the
browser refuses a field after the text by the field's place in the form, as the server does, so it is given that
field too, and a repeatable group's child is named through its group, so the group comes with it.

### The answer

The form endpoint's answer gains `mergeTags`, `null` when no visitor text renders:

- `texts`: each visitor text that renders, as `{owner, text, context}`: its owner's key, the property it is
  (`title`, `description`, `label`, `help`, `placeholder`, or `settings.` and the setting's path) and `html` or
  `text`. The template is that property of the visitor's form.
- `values`: `{html: {}, text: {}}`, each path's value for the texts of that context, as the vectors write a source's
  value: a string, an object with its `text` and the `number` or `date` behind it, or a list of these.
- `recall`: the cut-down `RecallForm` of option 3, held to the rules the vectors' runner holds (a field holds a value,
  is built in, has parts, has a kind and shows nothing only as its type says; `masked` for a sensitive field or one in
  a sensitive group; a country's and an address's countries named as the server names them; every field a text names
  with its alias). A field's options are its own, or those its list or source gives for no parent answer in the
  culture and on the page; a cascading field's depend on its parent's answers, which the browser asks the `options`
  endpoint for (6.1c) and adds (6.4).
- `words`: the culture's words for yes and no (`FieldDisplayWords`).
- `culture`: the culture's formats as the server writes them (ADR-0064), in the vectors' shape.

### Consent wordings

`ConsentWordings` passes the rendered wording through the one sanitiser before it hashes it, so the text the visitor
reads, the hash the form posts and the text the entry stores are one. The visitor's form shows a consent's `text` as
that wording, and the answer's `consents` gives each consent field's hash by its key, `ConsentTextHash` over the
answer's culture and the wording. A consent whose wording renders as nothing gets no hash.

## Consequences

- A label such as "Thanks, {{ field.name.first }}, from {{ site.name }}." renders in the browser from the answer alone,
  and the test of ADR-0068's promise runs the browser's code on the server's answer (6.1b's recall test).
- The answer grows with the texts that render and the fields they name, never with the rest of the form.
- A visitor text that names `date.now` is given the time of the answer, as a page the server renders is given the time
  it renders; each answer then differs, so its `ETag` matches no earlier one.
- A site's own merge-tag service implements `ValuesAsync` too.
- The editors offer merge tags in a heading's and a paragraph's text, and the field types' document shows it.
- **Revisit** if a site's own source must answer per text owner in a visitor text: the values are per page and
  context.
