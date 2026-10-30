# 0109. Reading answers in the browser

- Status: accepted; ports [ADR-0058](0058-field-answers-and-values.md)'s cleaning, limits and readings of the built-in
  types to `packages/logic` (and corrects what its rules see of an answer that is unreadable or over its limits);
  extends [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) and amends
  [ADR-0085](0085-visitor-texts-and-consent-wordings-on-the-form-endpoint.md) (the form endpoint's answer gains
  `fields`, `formats` and `words`, and `mergeTags` gives its words and formats up to them); amends
  [ADR-0099](0099-visitor-error-messages.md) (a label of only spaces counts as none in the summary)
- Date: 2026-10-30
- Roadmap step: 6.4a (decided and built); 6.4b reads the page's controls through it, 6.4c's rules and checks take
  what it read, 6.4d gives answer recall and calculations what it read, 6.4h reads uploads into tokens before this
  stage sees them

## Context

Step 6.4's runtime decides in the browser what the server decides after a post: which fields are hidden and
required, which answers fail their checks, and the messages the visitor reads (ROADMAP 6.4b and 6.4c). `packages/logic`
already held the form engine, the checks, the messages and answer recall to Core's through the shared vectors
(ADR-0005), but each of them starts where Core's pipeline has already read the answers: the texts cleaned, held to the
longest answer the type takes, and turned by the type into values in the wire form (ADR-0058). Without that stage the
browser would compare `1e3` where the server compares `1000`, find a checkbox ticked where the server finds it
unticked, or check a name's parts the server has already joined.

The form endpoint's answer was also short of what the browser needs to decide a field:

1. **The kinds.** Rules compare a field by its kind (text, number, choice, choices, ...), and the kind follows the
   type's declaration and sometimes a setting (a dropdown with `multiple` is choices). A site's own type declares its
   kind on the server only.
2. **Required.** A rule may require only a field whose type declares `required` (ADR-0061).
3. **Whose type.** A site may register its own type under any alias; only the built-in type of an alias reads and
   checks as the browser's code does, and a site's own type shows "Check this answer" (ADR-0099).
4. **The formats and words.** Messages print numbers and dates with the culture's formats and show yes and no in the
   culture's words (ADR-0099), but the answer carried them only inside `mergeTags`, which is `null` on a form whose
   texts hold no tag.

## Options

1. **Where the browser's reading lives.**
   - In the runtime, beside the controls: nothing would hold it to Core's, and the readings are what rules compare.
   - **In `packages/logic`, held to Core by a vector suite** as the rules, checks and messages are. Chosen.
2. **How the browser knows a field's kinds and type.**
   - The browser works them out from the alias and a table of the built-in types: wrong for a site's own type, and
     for a site that puts its own type under a built-in alias.
   - The type's whole declaration in the answer: more than a page needs, and the same for every field of a type.
   - **The server says per field what it decides by:** the kind rules compare, a grid row's kind, whether it can be
     required, and whether its type is the built-in one of its alias. The browser's table of the built-in types then
     only gives each built-in type's reading, and a test holds it to the served field types. Chosen.
3. **The formats and words.**
   - Keep them in `mergeTags` and send that object for every form: it would no longer say whether any text renders.
   - **Give them at the answer's top, on every form,** and leave `mergeTags` to the texts. Chosen.

## Decision

### The reading stage

`packages/logic/src/reading.ts` holds the stage as Core runs it (`SubmissionReader.Clean`, then `FieldValues`'
`CheckLimits` and `Read`):

- `cleanAnswer` cleans every text and part name as rules clean them (ADR-0005), leaves out empty texts, nameless
  parts and parts left empty, and keeps parts whose names clean alike as one part; a type that reads its parts in
  order reads them in the order of their names, as Core keeps them.
- `readField` holds the cleaned answer to its type's limits (the longest text, the most texts, values and parts
  together, a part's name counted with the texts) and reads it into values: each a text in the wire form, the grid
  row it answers, and the text the entry stores in its place (a consent's wording hash). An answer over the limits is
  `maxLength` when a text is too long and the type declares that kind, `invalid` otherwise, and is not read; an answer
  the type cannot read is unreadable, which the pipeline reports as `invalid` once the rules leave the field visible.
  Either way **the rules get no value**, as in Core, where they read what the type read (ADR-0058's "compared as
  posted" corrected).
- The readings are ADR-0058's: the default by the type's kind (text and choices as they are, a checkbox's `true`, a
  number converted exactly to the wire form by `postedNumber`, dates and times in their wire forms only, two values
  unreadable for a type that takes one), and each type's own (radio buttons and checkboxes with "Other", a grid's
  rows, a ranking by order or by positions, yes or no, a consent with its wording's hash, a country of the list, a
  name's and an address's parts as one JSON value). `checkRows` gives a repeatable group's `rowCount` for its number
  of rows.
- `BUILT_IN_TYPES` names each built-in type with its storage, kind, `multiple` setting, whether it holds a value and
  whether a text too long is `maxLength`; `reading.test.ts` holds it to `docs/api/field-types.json`, the served
  declarations, no type more or fewer. `COUNTRY_CODES` (`countries.ts`) is ADR-0056's list, and Core's
  `CountryCodesTests` holds it to `BuiltInChecks.CountryCodes`.
- **A site's own type is not read in the browser.** `readField` answers `null` for a type that is not built in (an
  alias such as `constructor` too): such a type may read its answers its own way, so only the server reads and checks
  it, and the server's answer to the submission stands. The runtime gives the rules such a field's cleaned texts as
  they are, which is how Core's default reads the text and choice kinds most site types declare.

`tests/vectors/reading.json` (suite `reading`, 150 cases) holds both runtimes to one reading: every built-in type's
limits and readings, unreadable answers, numbers as browsers send them (`1e3`, `.5`, `007`, `-0`, 60 digits and no
more), a ranking's position with a trailing NUL, which .NET's parser takes, and repeatable groups' row counts. NUnit runs it against Core (`ReadingVector`), Vitest against
`packages/logic`, and the mutation check changes the stage in each runtime.

### The form endpoint's answer

The answer is `{alias, revision, culture, timeZone, open, form, fields, formats, words, mergeTags, consents}`:

- `fields`: by each field's key, a repeatable group's child fields too, `{kind?, rowKind?, canBeRequired, builtIn}`:
  the kind rules compare the field as (none for a type that holds no value, a grid, a name or an address), a grid
  row's kind (only for a type with rows), whether a rule may require it, and whether its type is the built-in one of
  its alias. A field whose type the site lacks has no kind and cannot be required, as the form engine's model reads
  it (`DefinitionLogic`).
- `formats`: the culture's formats of numbers and dates as the server writes them (ADR-0064), on every form.
- `words`: the culture's words for yes and no (`FieldDisplayWords`), on every form.
- `mergeTags` keeps `texts`, `values` and `recall` and is still `null` when no visitor text renders; its texts render
  with the answer's `formats` and `words`.

### Blank labels

A summary link puts a field's label first (ADR-0099); a label, or a row's legend, made only of characters that
cleaning trims (spaces, U+0085, U+FEFF and the other spaces .NET trims) counts as none in both runtimes, so the link
never starts with an empty name.

## Consequences

- The runtime (6.4b) reads the page's controls into answers as the no-JS post names them and passes them through this
  stage, so what the rules compare, the checks hold and the messages print is what the server will find.
- A change to a type's reading is a change to the vectors, which both runtimes then fail until they agree; a new
  built-in type fails `reading.test.ts` until the table names it.
- The answer grows by a small object per field and the culture's formats on every form; a form with no merge tag still
  answers `mergeTags: null`.
- A site's own type of a number or date kind reads differently in the browser (as posted) than on the server (in the
  wire form) when a visitor types a form other than the wire form; the server's answer corrects it after a send.
- **Revisit** if site types need to read in the browser: they would ship their reading as a module of their own.
