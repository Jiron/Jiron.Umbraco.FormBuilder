# 0062. The built-in types' checks

- Status: accepted; amends [ADR-0058](0058-field-answers-and-values.md) (what a type's check sees),
  [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the message kind `confirmation`, and the
  settings in the public projection) and [ADR-0055](0055-field-type-framework.md) (the email type's message kinds);
  the forms of an email address, a phone number and a web address, a grid's required rows, a ranking's positions and
  a number's stored digits added by [ADR-0069](0069-the-checks-from-text-to-country.md) in 5.4a; the options of a list
  or another source and cascading options held in both runtimes by [ADR-0077](0077-answers-and-labels-from-resolved-options.md)
  in 5.5d
- Date: 2026-10-26
- Roadmap step: 5.2b (decided; builds the contract and the text, number and date types), 5.2c (the choice, people and
  file types, `pattern` and `matchesField`); 5.4a adds the grid's required rows and a ranking's positions, 5.5 the
  options of lists and other sources, 6.1 runs the checks in the pipeline, 6.4 in the visitor's browser

## Context

ADR-0058 split a type's work on an answer into `Read`, which turns the answers into values, and `Validate`, which holds
readable values to the field's settings; by default `Validate` finds nothing. Step 5.2 gives the built-in types their
checks in both runtimes (D10): lengths and words, ranges and steps, date windows, selections and the options a choice
offers, the required parts of a name and an address, file rules and an email's confirmation, together with the checks
of a field's `validation` list that need no other entry (`pattern`, `matchesField`). Planning them showed six open
questions:

1. **What a check needs that `FieldValidation` lacks.** A required name with no required parts requires every part it
   asks for (ADR-0056), and a rule can make a field required; a date window can be relative to today; a file's type and
   size are not in its upload token; `matchesField` needs another field's answers.
2. **Which settings the browser sees.** The public projection carries the public settings the editor gave, without the
   type's defaults: a text field without `maxLength` holds 255 characters on the server and an unknown number in the
   browser.
3. **How each check counts and compares:** lengths, words, steps, relative bounds, the "Other" text, file extensions.
4. **What a mismatched confirmation says.** The email type declares `required` and `invalid`, and `invalid` reads "not
   a valid email address", which is wrong for two valid addresses that differ.
5. **What is shared and what is the server's.** An email field's domains are server-only settings (ADR-0056).
6. **How big the step is.** Twenty-one types with checks, two runtimes, the vectors, the mutation check and a unit test
   per type.

## Options

1. **What a check sees.**
   - Each type looks things up itself (the clock, the upload store): types would need services, and the vectors could
     not give them.
   - **The pipeline hands them over:** `FieldValidation` gains whether the field is required (by its setting or a
     rule), today's date in the site's time zone and, for a field that stores files, the files its tokens stand for.
     `matchesField` is no type's check, so it runs beside the types with the form's other values. Chosen.
2. **The settings in the browser.**
   - A table of the built-in types' defaults in `packages/logic`, held to the server's by a test: two sources kept
     alike, and a site's own type would need its own.
   - **The projection writes each public setting's effective value:** the field's own, or else its type's default.
     The browser's checks, the headless renderer (6.x) and the server then read the same values from one source.
     Chosen; a form's public revision changes once, which no published site has yet.
3. **The confirmation's message.**
   - `invalid`: one message for two different problems.
   - `matchesField`: that is a check of the `validation` list with its own message, not a kind of the type.
   - **A message kind of its own, `confirmation`:** the address typed again is missing or another one. Chosen.
4. **Server-only checks.** An email field's domains never reach the browser, so it accepts what the server then refuses
   with `invalid`, as it does for a device whose clock is wrong (ADR-0005). The check is Core's alone and is tested by
   unit tests, since a shared vector would fail in the browser by design.
5. **The size.** One step would carry about twice what any step of Phase 5 has. **Two steps,** each with all of 5.2b's
   checks for its types: 5.2b builds the contract, the projection's settings, the TypeScript module and the checks of
   the text, number and date types; 5.2c the choice, people and file types and the field's `pattern` and
   `matchesField` checks.

## Decision

### Where the checks run

- **Core:** each built-in type's `Validate` (ADR-0058), through `FieldValues.Check`, which runs only on readable values
  of a visible field and reports `required` for one without values. The field's `pattern` and `matchesField` checks
  run after it, for a field with values, from 5.2c.
- **TypeScript:** `checkField` in `packages/logic/src/checks.ts` runs the same checks over the same answers for the
  built-in types and returns the same errors; a type it does not know has none, and the server decides.
- **The answer:** the error kinds in the order below, each at most once, and from 5.2c the indexes of the field's checks
  that fail, in their order: `{ "errors": [...], "checks": [...] }`.

### What a check sees

`FieldValidation` holds, besides the field, the answers and the values (ADR-0058):

- `IsRequired`: whether the field is required, by its own setting or by a rule;
- `Today`: the date in the site's time zone, which the pipeline takes once per submission;
- from 5.2c, `Files`: for a field that stores files, the name and size of the file each upload token stands for, in
  order, which the pipeline takes from the upload store (6.1). A field that stores files without one file for each
  value, or another field with files, is the pipeline's mistake, and `FieldValues.Check` refuses it.

`checkField` takes the same: the field (its type, its rule kind, whether it is required, its effective settings, its
own options' values, its rows and its own checks), its answers (values, parts, files), the context of ADR-0005 (whose
instant and time zone give today) and, from 5.2c, the values of the form's other fields by id, as the rules see them.
The browser has no reading of its own until 6.4, so `checkField` reads what its checks need as Core reads answers it
can read (5.2c): the "Other" text in place of an option on radio buttons, and after the options on checkboxes unless
it repeats one; each grid row's values; the parts a name or an address asks for. A field whose options come from a
list or another source has those it resolved as its `options` since 5.5d, filtered by its `parent`'s answers, and
none when the source could not give them (ADR-0077).

### The settings

The public projection writes, for each field, every public setting of its type with its effective value: the field's
own when it has one, otherwise the type's default, after the host's locks (ADR-0060). A setting without a default and
without a value stays out. `checkField` reads the settings as given and treats a missing one as no limit, so a form
projected by an older version is checked leniently and the server still decides.

### How the checks count and compare

- **Lengths** are UTF-16 code units of the cleaned value, as browsers count `maxlength`; the pipeline cleans every
  answer first (ADR-0058).
- **Words** are the runs of characters between the whitespace that cleaning trims (ADR-0005).
- **Numbers** compare as exact decimals. A step counts from the minimum, or from 0 without one; a number field without
  a step takes values with at most `decimals` places, which is the step 10 to the power of minus `decimals`.
- **Times** compare to the second. A time's step counts from its earliest time, or from midnight without one; a date
  and time's step counts from midnight of its own day; with a step, a value with seconds other than 0 is off the step.
- **Date windows** are inclusive. A bound is a date or `{ "today": offset }` from the site's today (ADR-0005); a bound
  that falls outside the years 1 to 9999 lets no date through. Weekdays are Monday to Friday. A date and time's window
  is that of its date.
- **The confirmation** of an email address compares as rules compare text: cleaned and folded (ADR-0005).
- **Choices** (5.2c) compare option values exactly, from the field's own options; the answers' own values must be
  options, and the "Other" text is the visitor's own when the field offers it, counting as one selection. A choice
  whose options come from a list or another source has them checked by 5.5, its selections still counted; a field
  without options, which only a draft holds, takes none. A dropdown counts selections only with `multiple`. A grid's
  columns are its scale's whole numbers when it has a scale, otherwise its options, so a draft with both checks
  against the scale. A ranking ranks as many as `top`, or all its options when `top` is missing or exceeds them;
  reading already refuses an option given twice.
- **Required parts** (5.2c) hold once any part is given: the parts `requiredParts` lists that the field asks for, or,
  with none listed, every part a required name asks for and the street, the postcode and the city of a required
  address. A part the field does not ask for is no answer.
- **A country** (5.2c) is read only as an ISO 3166 alpha-2 code of ADR-0056's list, so Core's reading finds any other
  text unreadable (amending ADR-0058); the browser's list of countries offers no other.
- **File extensions** (5.2c) are the text after a name's last point, in ASCII lowercase. A file field takes the
  extensions of its type groups and its own extensions, or images and PDF when it lists neither:

| Group | Extensions |
| --- | --- |
| `images` | `jpg`, `jpeg`, `png`, `gif`, `webp`, `avif`, `heic`, `heif` |
| `pdf` | `pdf` |
| `office` | `doc`, `docx`, `xls`, `xlsx`, `ppt`, `pptx`, `odt`, `ods`, `odp`, `rtf` |
| `text` | `txt`, `csv` |
| `archives` | `zip`, `7z`, `rar`, `tar`, `gz` |

  SVG is no image here, because it can carry script; a site that needs it lists it as its own extension. The host's
  blocked extensions and size limit, and the content check, come with the uploads (6.1, 7.x). A file field takes up
  to 20 upload tokens before its type reads them, the most any file field takes, so that more than `maxFiles` is
  reported as `fileCount` and not as `invalid` (amending ADR-0058's limits).
- **`matchesField`** (5.2c) holds when both fields have the same number of values and each value of the field equals
  one of the other's, compared as the field's rule kind compares an operand (ADR-0005); a hidden other field has no
  values. **`pattern`** (5.2c) holds when every value matches the whole pattern (ADR-0005); a pattern outside the safe
  subset, which publishing refuses, checks nothing, so a draft's preview does not fail on it. `uniquePerForm` is the
  submission's (6.1): it never fails here, and keeps its place in the indexes of the checks.

### The checks of each type

In the order they are reported:

| Type | Checks | Kinds |
| --- | --- | --- |
| `text` | the value's length within `minLength` and `maxLength` | `minLength`, `maxLength` |
| `textarea` | the length, then the words within `minWords` and `maxWords` | `minLength`, `maxLength`, `minWords`, `maxWords` |
| `email` | its form (ADR-0069); with `confirm`, the address typed again and the same; then, on the server only, the domain allowed or not blocked | `invalid`, `confirmation` |
| `phone` (5.4a) | its lenient international form (ADR-0069) | `invalid` |
| `url` | the scheme, before the first colon in ASCII lowercase, among the allowed ones; then its form (ADR-0069) | `invalid` |
| `number` | at most 15 digits in all and after the point, alone (5.4a, ADR-0069); at least `min`, at most `max`, on the step | `digits`, `min`, `max`, `step` |
| `slider` | at least `min`, at most `max`, on the step from `min` | `min`, `max`, `step` |
| `rating` | a whole number from 1 to `count` | `invalid` |
| `scale` | a whole number from `from` to `to` | `invalid` |
| `date` | within `min` and `max`, a weekday when `weekdaysOnly`, not an excluded date | `dateWindow` |
| `time` | at least `min`, at most `max`, on the step of `stepMinutes` | `min`, `max`, `step` |
| `datetime` | its date within the window as for `date`, then on the step of `stepMinutes` | `dateWindow`, `step` |
| `dropdown` (5.2c) | each value an option; with `multiple`, the count within `minSelections` and `maxSelections` | `invalid`, `selections` |
| `radio` (5.2c) | the value an option, or the "Other" text | `invalid` |
| `checkboxes` (5.2c) | each value an option; the count within the selections | `invalid`, `selections` |
| `grid` (5.2c) | each row's values among its columns: the options, or the scale's whole numbers; then the rows it needs answered (5.4a, ADR-0069) | `invalid`, `required` |
| `ranking` (5.2c) | each option once and each position once (5.4a, ADR-0069); each value an option, as many as `top` or as all the options | `invalid` |
| `country` (5.2c) | among `allowed`, when it lists some | `invalid` |
| `name` (5.2c) | the required parts: `requiredParts`, or every part asked for when the field is required and lists none | `required` |
| `address` (5.2c) | the required parts: `requiredParts`, or the street, the postcode and the city when the field is required and lists none | `required` |
| `file` (5.2c) | at most `maxFiles` files; each file's extension taken; each file at most `maxBytes` | `fileCount`, `fileType`, `fileSize` |

The other built-in types (`checkbox`, `yesno`, `consent`, `signature`, `hidden`, `calculation`, `repeater`
and the content types) have no check of their settings: their reading, the pipeline or a later step decides what they
take. A choice whose options come from a list or another source has its options checked by 5.5, which resolves them,
together with cascading options; the grid's required rows and a ranking's positions come with 5.4a (ADR-0069), in both runtimes
through the same vectors.

### The vectors

`tests/vectors/validation.json`, from `tests/vectors/suites/validation.mjs`, adds the runnable `field` to the
`jfb-vectors/1` format (ADR-0005): the field (`type`, `kind`, `required`, `settings`, `options`, `rows`, `checks`) with
its `answers` (`values`, `parts`, `files`), the other fields' `values` by id, and the expected errors and failing
checks. `options` are the values of the field's own options; since 5.5d an option may be `{ "value", "parentValues" }`,
`parent` names the field whose answers filter them, and `fromSource` marks options a list or another source resolved,
or, without `options`, a source that could not give any (ADR-0077). Ids are keys in the `D` format, as Core's model has them. Both runners check that a case's field gives
every public setting its type has a default for, an object's settings each in turn, as the projection writes them, so
no vector leans on a default; that its kind is its type's; that it has options and rows only when its type has them;
that a field that stores files has one file for each value; and that its answers are cleaned as the pipeline cleans
them. The Core runner also checks that they are readable and that the values posted read as they are, because reading
is Core's alone until the browser's runtime (6.4): a choice's "Other" text, a grid's rows and the parts of a name or
an address come as parts. 5.2a's mutation check covers `checks.ts` as it covers the other modules.

## Consequences

- The pipeline (6.1) gives each visible field its requiredness, today and its files, and the visitor's browser (6.4)
  checks the same answers against the same settings, so the two agree on every message except the server's own: the
  email domains, the uploads' host limits and content, and `uniquePerForm`.
- A site's own type checks its values in `Validate` with the same help; the browser leaves it to the server.
- An editor writes a custom text for a mismatched confirmation under `confirmation`.
- The public projection grows by the defaults of public settings, once per field; the headless renderer gets the values
  it renders.
- **Revisit** the extensions of the type groups when browsers or devices change what they save, and the confirmation's
  comparison if forms need an exact one.
