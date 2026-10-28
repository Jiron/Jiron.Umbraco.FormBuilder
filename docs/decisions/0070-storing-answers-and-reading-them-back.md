# 0070. Storing answers and reading them back

- Status: accepted; the values the server works out (calculations, hidden fields) and a repeatable group's rows,
  closed up, come from [ADR-0072](0072-a-submissions-answers.md) in 5.4d; the consent wording sanitised before it is
  hashed and stored amended by [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md) in 6.1b
- Date: 2026-10-26
- Roadmap step: 5.4b (decided and built); 6.1 calls it from the pipeline, 6.2 renders the consent wording into the
  markup, 5.4d adds the values the server works out (calculations, hidden fields)

## Context

Each field type reads a visitor's answers into values in the wire form (ADR-0058), and `EntryValueColumns` (5.1d)
makes the row one value is stored as. The entry repository stores an entry with its rows and takes its uploads
(ADR-0038). What is still open is the step between the two: which row each value of a whole entry goes to, and how a
stored entry becomes typed answers again.

1. **Indexes.** A row of `jfbEntryValue` has a field, a `rowIndex` and a `valueIndex`. Multi-value answers, a grid's
   rows (each its own field in the catalogue, ADR-0044), a ranking's positions and a repeatable group's rows all need
   a rule.
2. **Files.** An upload token names a file of `jfbEntryFile` (ADR-0006). Which uploads may an entry take?
3. **Consent.** A ticked consent stores the hash of the wording shown (ADR-0009), and `jfbConsentText` keeps the
   wording once. Which text is the wording, and when is it stored?
4. **Reading back.** Exports, the entries screen, actions and mail need an entry's answers typed: a number as a
   number, a date as a date, a file as its file, the parts of a name.

## Options

1. **A ranking's positions.**
   - The posted position numbers as `valueIndex`: gaps (`1`, `3`) would mean nothing a reader could use, and the
     browser posts no numbers at all.
   - **The rank order:** the values come in rank order from reading, so `valueIndex` is the position from 0, gaps
     closed. Chosen.
2. **Which typed value reading back gives.**
   - The typed columns: `number` is a double, null past 15 digits, and `date` is null before 1753 (ADR-0058).
   - **The stored text, read again in its wire form,** typed by the storage kind and the rule kind the type
     declares: what is read back is exactly what was stored. Chosen.
3. **The consent wording.**
   - The wording as the editor wrote it: a merge tag or a dictionary item would then differ from what the visitor read.
   - **The wording as shown:** the text in the entry's culture, a dictionary item resolved, its merge tags rendered
     where consent texts render them. Chosen.

## Decision

### Storing

`EntryFieldAnswer` is what the pipeline hands over for one field in one row: the field's key, the values its type
read (ADR-0058) and, for a repeatable group's child, the row, from 0; with a file field's values come the uploads
their tokens name, resolved by the pipeline (6.1). `EntryValueWriter` (internal) turns the answers of an entry into
its rows, over the version the entry is submitted with and the form's field catalogue:

- each value goes to its field's catalogue row with the column its storage kind fills (5.1d); `valueIndex` is its
  position among the field's values, so a multi-value answer, an "Other" text among the options (in the place reading
  gave it) and a ranking in rank order each keep their order; `rowIndex` is the repeatable group's row, 0 elsewhere;
- a grid's values go to their rows' catalogue rows, each row's values from `valueIndex` 0; the grid's own row and the
  repeatable group's own row store nothing;
- a file value takes the upload its token names: an upload of the entry's form and of that field, not taken by any
  entry and named once. The repository takes the uploads in the entry's unit only while no entry has them (ADR-0038),
  so two submissions never share one;
- a ticked consent stores its wording's hash and needs the wording shown with the entry: one whose hash is the stored
  one, or the answer is refused. The pipeline compares the posted hash with the current wording before (6.1, answered
  as `form_changed`), so a refusal here means the pipeline is wrong, not the visitor.

The writer refuses, by name and without any value (messages end up in logs), an answer of a field the version lacks,
two answers of one field in one row, a row outside a repeatable group, a field or a grid row missing from the
catalogue or kept there under another grid, and an upload of another form, field or entry.

`EntryAnswerStore.CreateAsync` stores the entry, its rows, its uploads and the consent wordings it needs in one unit
of work, the form's counter first (ADR-0038); a wording is stored once, under its hash, insert-once.

### The consent wording

`ConsentWordings` gives the wording a consent field shows: its `text` in the entry's culture with the usual fallback,
a dictionary item (`#key`) resolved through Umbraco's dictionary, and its merge tags rendered in the consent-text
place (HTML; only `form`, `page`, `site` and `dictionary`, ADR-0014), so it is the same for every visitor and holds no
personal data. The hash is `ConsentTextHash` over the entry's culture and that text (ADR-0038). 6.2 renders the same
wording through the same service into the markup and the projection, with §4.10's sanitising added there, so the hash a
form posts and the hash the server stores always name one text. A site's own type that stores another text than its
value names its own meaning; only the built-in consent has a wording.

### Reading back

`EntryAnswerStore.ReadAsync` reads an entry's rows, uploads, catalogue and wordings into `EntryAnswers`: one
`EntryAnswer` per field and row, its values in `valueIndex` order (a grid's by its rows in the catalogue's order, each
value with its row), each typed from its stored text:

| Storage, rule kind | Typed value |
| --- | --- |
| `number` | `decimal`, when a stored number keeps it exactly (15 digits); a calculation's longer result only as its text |
| `date` | `DateOnly`, or `DateTime` (wall-clock, unspecified kind) for a date with a time |
| `text`, time | `TimeOnly` |
| `bool` | `bool` |
| `file` | the entry's file |
| `json` | the parts, in the type's order |
| `text`, the built-in consent | `true`, with the wording: culture and text |
| any other | the text |

Every value also gives back the `FieldValue` rules and merge tags read (a consent's `true` with its hash), and
`ForMergeTags` makes the answers merge tags show, files included, so actions and mail (Phase 8) render a stored entry
as they render a submission. A field whose type the site no longer has reads by its storage kind alone.

## Consequences

- The pipeline (6.1) only reads, validates and hands over; the rows' shape is decided once, here.
- A stored entry's answers read back the same in every consumer, and a value never reads back rounded.
- A ranking's posted gaps are not kept; its order is.
- 5.4d's calculations and hidden fields store through the same writer, with the values the server works out.
- **Revisit** if a site's own type needs a wording of its own, or if a reader needs a stored value that no wire form
  can say.
