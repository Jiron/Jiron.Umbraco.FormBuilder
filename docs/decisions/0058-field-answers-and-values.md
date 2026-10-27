# 0058. Field answers and values

- Status: accepted; amends [ADR-0055](0055-field-type-framework.md) (what a type declares) and the entry values of
  [ADR-0038](0038-entry-repositories.md) (the key of a value with parts); what a type's check sees (whether the
  field is required, and today) amended by [ADR-0062](0062-the-built-in-types-checks.md) in 5.2b, and the files a
  check sees, a file field's most texts and a country's reading in 5.2c; how merge tags show the values a type read,
  written by the type, added by [ADR-0066](0066-answers-as-merge-tags.md) in 5.3d; a number read up to 60 digits and a
  ranking's repeated option or position read, for the checks to refuse, amended by
  [ADR-0069](0069-the-checks-from-text-to-country.md) in 5.4a; a type's limit and check of its uploads and its check
  of a field's rows added by [ADR-0071](0071-the-advanced-types-own-checks.md) in 5.4c; a checkbox's longest answer
  (5, for `false`) and part names held to the longest text amended by
  [ADR-0080](0080-reading-a-submission-after-the-phase-5-review.md) in 5.5f
- Date: 2026-10-25
- Roadmap step: 5.1d (decided and built); 5.2 gives the built-in types their checks, 6.1 runs the pipeline on them,
  6.2 posts the answers without JavaScript

## Context

ADR-0055 gave every field type a declaration with its storage kind, and ADR-0003 the typed columns of `jfbEntryValue`.
What was still open is the step between a visitor's answer and a stored value (§4.3.2: "server validation and
parsing"), and the ceiling that 4.8f put on answers before the rules run (ADR-0030):

1. **What a type receives.** Most answers are one text or a list of texts (ADR-0005's wire forms), but a name and an
   address have parts, a grid answers each of its rows, an email may be typed twice, radio buttons and checkboxes may
   carry an "Other" text, and a consent carries the hash of the wording it showed (ADR-0009).
2. **The longest answer a type takes.** No rule, pattern or comparison may run over more text than a field takes, so
   the pipeline holds every answer to a ceiling before the rules run (6.1). What is the ceiling, and what does a
   visitor see when an answer is over it?
3. **Reading and checking.** Which part turns the posted texts into values (a number posted by a browser's own number
   input may use an exponent), which part holds them to the field's settings, and in which order the pipeline runs
   them, given that rules decide which fields are visible and rules compare wire forms.
4. **The columns.** How each storage kind fills `text`, `textKey`, `number`, `date`, `bool` and `fileId`.

## Options

1. **The answers.**
   - One JSON value per field (an object for a name, an array for checkboxes): every client would build JSON, and a
     post without JavaScript sends flat names.
   - **A field's texts and its parts:** the texts in order, and named parts, each a list of texts. A form without
     JavaScript posts `<alias>` and `<alias>.<part>` as they are; the JSON body of the public API carries the same.
     Chosen.
2. **The ceiling.**
   - The field's own limit (a text field's `maxLength`): exact, but it duplicates the validation of lengths that 5.2
     builds in both runtimes, and every type would need settings to know it.
   - **The type's own ceiling,** the most any field of the type can take: a text field takes at most 4,000 code units
     because its `maxLength` goes no higher; a field's own `maxLength` stays its validation, with its own message.
     A ceiling on the length of each text and on the number of texts, since a choice's answers are short and many.
     Chosen.
3. **Reading and checking.**
   - One method that reads and checks: the pipeline could not compare wire forms in rules before it knows which
     fields are hidden, and a hidden field must never fail.
   - **Two steps:** `Read` turns the texts into values in the wire form or finds them unreadable, and `Validate`
     holds readable values to the field's settings. The pipeline reads before the rules (rules compare what was read)
     and reports a field's errors only once the rules leave it visible. Chosen.
4. **A value with parts** in the columns.
   - One row per part (`valueIndex` as the part): every reader would need the type's list of parts to know which row
     is the street.
   - **One row with a JSON object,** as the storage kind's name says, whose key is made of the parts' texts in the
     type's order, so the entries screen sorts and filters names and addresses by what they read. Chosen.

## Decision

### Answers

`FieldAnswers` holds what a visitor gave one field: `Values`, the texts in order (one per chosen option, file token or
ranked option, in rank order), and `Parts`, each a name and its texts. The pipeline cleans every text first as rules
clean them (ADR-0005) and leaves out empty ones, so `FieldAnswers` refuses a null or empty text. The built-in parts are:

| Type | Parts |
| --- | --- |
| `name` | `title`, `first`, `middle`, `last` |
| `address` | `street`, `street2`, `postcode`, `city`, `region`, `country` |
| `grid` | each row by its key in the `D` format, holding the chosen columns' values |
| `email` | `confirm`, the address typed again |
| `radio`, `checkboxes` | `other`, the visitor's own text |
| `consent` | `wording`, the hash of the wording shown (`consents` in the public API, ADR-0009) |

A form without JavaScript names its inputs `<alias>` and `<alias>.<part>` (6.2); a repeatable group's rows are the
pipeline's to split (6.1), so a type always reads one row.

### The longest answer

`IFormBuilderFieldType.AnswerLimits(field)` gives a `FieldAnswerLimits`: the most UTF-16 code units in any one text,
and the most texts in all, values and parts together. `FieldAnswerLimits.None` says the type takes no answer: the
pipeline ignores what is posted for it (a calculation, which the server recomputes; a repeatable group, whose child
fields take its answers; content and the captcha placement). The pipeline checks the limits before the rules run
(6.1); an answer over them fails at its field with `maxLength` when a text is too long and the type declares that
error kind, and with `invalid` otherwise. By default a type takes one text of 2,000 code units, or as many as a field
can have options (10,000, the ceiling of ADR-0042) when it takes several, and the readers of numbers and dates take
what their wire forms need. The texts a field takes follow its settings; the built-in types take:

| Type | Longest text | Most texts |
| --- | --- | --- |
| `text` | 4,000 | 1 |
| `textarea` | 100,000 | 1 |
| `email` | 254 | 1, or 2 with `confirm` |
| `phone` | 64 | 1 |
| `url` | 2,048 | 1 |
| `number`, `rating`, `scale`, `slider` | 64 | 1 |
| `date` | 10 | 1 |
| `time` | 8 | 1 |
| `datetime` | 19 | 1 |
| `dropdown` | 200 (an option's value) | 1, or 10,000 with `multiple` |
| `radio` | 200, or 2,000 with `other` | 1, or 2 with `other` |
| `checkboxes` | 200, or 2,000 with `other` | 10,000, or 10,001 with `other` |
| `grid` | 200 | 1 for each of its rows, or 10,000 each with `multiple` |
| `ranking` | 200 | 10,000 |
| `checkbox` | 4 (`true`) | 1 |
| `yesno` | 3 (`yes`, `no`) | 1 |
| `consent` | 64 (the wording's SHA-256 in hex) | 2 |
| `country` | 2 | 1 |
| `name` | 200 | 4 |
| `address` | 200 | 6 |
| `file` | 1,024 (an upload token) | 20, the most any file field takes: its check reports more than `maxFiles` (ADR-0062) |
| `signature` | 1,024 | 1 |
| `hidden` | 2,000 | 1 |
| `calculation`, `repeater`, `heading`, `paragraph`, `divider`, `image`, `captcha` | none | none |

### Reading

`IFormBuilderFieldType.Read(FieldReading)` turns the answers into values (`FieldValue`: a text in the wire form, for a
grid the row it answers, and the text the entry stores where it is not the value) or calls `Invalid()`. The default
reads by the type's rule kind: text, choices and file tokens as they are; a checkbox's `true` (anything else leaves it
unticked); a number in the wire form or in the form a browser's number input posts (leading zeros, a point with no digit
before it, an exponent), converted exactly to the wire form, unreadable past the 60 digits a calculation reads and refused
past 15 with `digits` (ADR-0005, ADR-0069); dates, times and
dates with times in their wire forms only. A type that takes one value (all but choices that take several, files, and a
choice type whose `multiple` setting is on) finds two unreadable. Types with parts read them: a name and an address
become one value, a JSON object of their parts in the type's order with empty parts left out and parts the field does
not ask for ignored; a grid gives each row's columns as that row's values; the "Other" text counts when the field offers
it, after the chosen options on checkboxes and in place of a chosen option on radio buttons; a ticked consent's value is
`true`, which rules compare as a ticked box, and the entry stores the wording's hash in its place
(`FieldValue.StoredText`), while a tick without a wording's hash is unreadable. A country reads only as an ISO 3166
alpha-2 code of ADR-0056's list; any other text is unreadable (ADR-0062).

### Checking

`IFormBuilderFieldType.Validate(FieldValidation)` holds readable values to the field's settings and reports each error
with `Fail(kind)`: one of the error kinds the type declares, or `invalid`, which every type may raise and which shows
the type's default text where it has no message of its own. It finds nothing by default; 5.2 gives the built-in types
the checks of their settings (lengths, words, ranges, steps, date windows, selections, file rules, the confirmation of
an email, the options a choice offers), built in both runtimes. `required` is the pipeline's: a field without values is
required when its own setting or a rule says so, and its type's `Validate` runs only on values. Besides the field,
its answers and its values, `FieldValidation` tells the check whether the field is required (`IsRequired`, by its own
setting or a rule) and the site's date (`Today`), which the pipeline takes once per submission, and, for a field that
stores files, the name and size of the file each upload token stands for (`Files`, ADR-0062).
`FormBuilderFieldTypeBase<TSettings>` gives all three methods the field's settings as its class, as it does for the
check of settings (ADR-0056). `FieldValues` (internal) runs them for the pipeline and refuses a type that breaks the
contract: an error kind it does not declare, a grid row the field does not have, or a value its storage kind cannot hold
(a number or a date that is not in its wire form, a `bool` that is neither `true` nor `false`, a `json` value that is no
object of texts, or a value without a row of a type whose parts hold its values).

The pipeline's order (6.1): the limits of every answer; each field read by its type; the rules over what was read (an
unreadable answer is compared as posted, and an invalid value never matches, ADR-0005); hidden fields stripped; then,
for each visible field, a read that failed as `invalid`, a missing required value, or its type's `Validate`.

### The columns

`EntryValueColumns` (internal) makes the row a value is stored as, by the field's storage kind:

| Storage | `text` | Typed column |
| --- | --- | --- |
| `text`, `longText` | the value, or the text it stores instead | none; `textKey` from the text |
| `number` | the wire form | `number`, a double, when the value has at most 15 digits (a calculation's longer result stays text only, ADR-0005) |
| `date` | the wire form | `date`: the date, or the date and time, as the wall-clock value it is, written as UTC (ADR-0003), from 1753 on, where SQL Server's `DATETIME` starts (an earlier one stays text only) |
| `bool` | `true` or `false` | `bool` |
| `file` | the file's key | `fileId`, the entry's file the upload became |
| `json` | the JSON object, written canonically | none; `textKey` from the parts' texts, joined by spaces in the type's order |
| `none` | refused: the parts hold the values (a grid's rows are text in the catalogue) | |

`NewEntryValue.KeyText` carries the text a key is made of when it is not the stored text; only a value that is text
only may have one.

## Consequences

- The pipeline (6.1), the no-JS post (6.2) and the runtime (6.4) share one shape of answers, and a site's own type
  reads and checks its answers through the same three methods.
- An answer's cost is bounded per type before any rule reads it, whatever the field's own limits.
- The entries screen (11.x) sorts and filters names and addresses by what they read; a text filter that compares the
  whole stored text of a long value does not apply to them, since their stored text is JSON.
- The built-in types read their answers now and check them in 5.2; until then a published form takes any readable
  value, which no visitor can submit before 6.1 anyway.
- Revisit the ceilings if a type's settings ever allow more than its ceiling, and the number reader if browsers post
  forms the conversion does not take.
