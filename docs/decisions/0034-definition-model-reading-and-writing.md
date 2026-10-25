# 0034. The definition model in code: reading, writing, the schema and upgraders

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the save codes,
  properties a type does not support, the outcome limit); amended in 4.4a (stored definitions are read with the
  highest limits, [ADR-0042](0042-host-configuration-and-start-up-checks.md)); amended in 4.8e (one limit of 100
  problems for every reader and check; the lists are read-only, and records compare them by their items); amended in
  4.8f (five C# names, one opening for both readers, what the writer refuses, nested groups read once)
- Date: 2026-10-23
- Roadmap step: 4.2a (decided and built); 4.2b builds the structural validation on it, 4.4 binds its limits, 4.5
  calls it from the form service

## Context

Step 4.2a turns ADR-0002's document into code: the model, the strict reader, the canonical writer with
`DefinitionHash`, the JSON schema and the upgraders. ADR-0002 fixes the shape; building it raised questions the ADR
leaves open:

- **What is public.** The records are what field types, actions and later extension points see. The reader and the
  writer are what the form service (4.5) calls; nothing outside the package needs them yet.
- **How strict reading reports.** System.Text.Json stops at the first problem, has no codes, and turns a missing
  property into `null` or a default without a word. Its source generator also drops the initialisers of init-only
  properties when it reads a document that leaves them out, so a missing list would arrive as `null`.
- **Codes.** ADR-0002 lists `duplicate_key` both for a property named twice in one object and for two elements that
  share a key, and has no code for a required property that is missing.
- **Null and empty.** The canonical form leaves out `null`, `[]` and `{}`; a reader that refused them would refuse
  other tools' output for nothing.
- **Numbers and instants.** JSON allows `2.0`, `2e0` and integers no JavaScript runtime holds exactly; ISO 8601 has
  many spellings of one instant, and a `DateTime` carries a kind and ticks below a second.
- **Texts.** In which unit a length counts, what a line break is, and what happens to a lone surrogate, which
  System.Text.Json writes as U+FFFD.
- **Booleans and unsupported properties.** The canonical form always writes booleans and numbers, so every field
  carries `hideLabel` and `required`, while ADR-0002 says a property its type does not support must be absent.
- **Limits.** §4.3.1 limits pages, groups, fields, options, actions and rules, but not outcomes, which a form can also
  hold without end.
- **The canonical bytes.** `DefinitionHash` is compared across servers, versions and package releases, so the bytes
  must not depend on a serializer's escaping defaults, which differ between settings and may change between releases.
- **The schema.** Which draft, which identifier, what a schema can say at all, and which validator the tests use under
  CLAUDE.md rule 4: the JsonSchema.Net 9 releases ship under a maintenance fee agreement, which is not a permissive
  licence.
- **Upgraders** with only one schema version to test against.

## Options

**Strict reading:**

1. **System.Text.Json alone**, with unmapped members disallowed and required members checked: one error per document,
   its own messages, no codes, and the init-only defaults lost.
2. **A JSON-schema validator at run time:** a dependency in shipped code, output that has to be mapped to codes, and
   the save checks (keys, limits) are still needed.
3. **A shape reader over the source-generated metadata:** it walks the document against the same metadata the
   serializer uses, records every problem with a code and a JSON path, fills in defaults, and hands a clean document to
   the serializer. The model is described once.

**The canonical writer:**

1. **System.Text.Json's writer** with a relaxed encoder: it still escapes some characters, and its choices are not
   ours to keep stable.
2. **A small writer of our own** over the parsed document, escaping strings as RFC 8785 (the JSON Canonicalization
   Scheme) does.

**The schema validator for the tests:** JsonSchema.Net 8.0.5, the last MIT release, which reads draft 2020-12; a
newer release under its fee agreement (refused by rule 4); or a validator of our own (a large piece of code that
would itself need tests).

## Decision

Option 3 for reading, option 2 for writing, JsonSchema.Net 8.0.5 as a test-only dependency. In detail:

### The model

- **Public immutable records** in `Jiron.Umbraco.FormBuilder.Core.Definitions`: `FormDefinition`, `FormSettings` and
  its six groups, `FormPage`, `FormGroup`, `FormField` with `FieldMessages`, `FieldPrefill`, `FieldOptions`,
  `FieldOption`, `GridRow` and the checks (`PatternCheck`, `MatchesFieldCheck`, `UniquePerFormCheck`), `FormRule` with
  `RuleCondition` and `RuleGroup`, `FormValidationRule`, `FormAction`, `FormOutcome` and `OutcomeRedirect`, and the
  enums. Property names, defaults and enum words are ADR-0002's.
- **Lists** (amended in 4.8e): every list that a record, a value type or the public projection holds is the model's
  own read-only list (internal), which implements no collection interface that adds, removes or replaces items, so no
  cast changes it. A record's `init` accessor copies the list it is given, the reader's lists included, so the list a
  caller built a record from cannot change it later either.
- **Records compare their lists by their items, in order** (amended in 4.8e), each item by its own equality, and hash
  them alike, so two reads of one document are equal, as the value types always were. The other choice, comparing
  lists by reference and writing that down, would let a public record's `Equals` say that two equal definitions
  differ, a trap for every extension point that sees them. A comparison walks the whole record and costs time in
  proportion to its size; nothing in the package keys a dictionary by a record, and stored versions are still compared
  by `DefinitionHash`.
- **Value types with their own rules:** `LocalizedText` and `LocalizedHtml` (validated and sorted when built: a
  default text, culture names, no culture twice in any letter case; `Resolve` falls back through the parent cultures
  to the default); `FieldDefaultValue` (one text or a list); `ConditionOperand` (a text, another answer, a date
  relative to today, or a list of those, which holds at least one item and no list and checks that when it is built);
  `SettingsObject` (a type's settings as canonical JSON, bound to the type's settings class with `Bind`).
- **An object that may be absent always writes a property** (a boolean, a number, a word or a required one), because
  the canonical form leaves out `{}` and such an object would read back as nothing. `FormField.Messages` is therefore
  never null but empty by default, as ADR-0002 has it, and a test checks every object that may be null or a list item.
- **`DefinitionLimits`** is a public record with ADR-0002's limits as defaults; 4.4 binds it from
  `JironFormBuilder:Limits:Definitions`.
- **Internal:** the serializer (`FormDefinitionSerializer`), the shape reader, the save checks and the upgraders. The
  document format is the public contract (ADR-0002, the schema); the form service of 4.5 is the way in. They become
  public only when an extension point needs them, with an ADR.

### Reading

A read runs these steps and stops at the first that fails, reporting at most 100 problems, each with a code, a JSON
path (`$.pages[0].title['']`) and a message:

1. the size (the site's limit, 2 MiB by default, counted in UTF-8 bytes; `ReadStored` takes the highest limits a host
   may set instead, for what the site stored itself, so a lowered limit never makes a stored form unreadable, ADR-0042);
2. UTF-8 that is well formed, and JSON that parses with a depth of at most 64 (a scan tells a document that is only
   too deep from one that is not JSON, however deep it nests);
3. a root object whose property names can all be read (an escaped lone surrogate in a name is `invalid_value` at its
   object, since .NET cannot read such a name);
4. a `schemaVersion` the site reads;
5. for an older version, a check for properties named twice, then the upgraders;
6. the shape, against the source-generated metadata;
7. System.Text.Json reads the cleaned document;
8. the save checks on the model, which a definition built in code also runs before it is written.

Reading takes time in proportion to the document's size: a path becomes text only when a problem names it, so a long
property name over a long array costs no more than its bytes.

**One limit for every reader and check** (amended in 4.8e): the shape reader, the save checks, the structural checks
(ADR-0035), the publish checks together and the export file's reader (ADR-0046) collect their problems through one
collection with one limit of 100. Past it a problem is counted, so a step still knows that it failed, but not kept,
and the export file's reader stops reading.

| Code | Meaning |
| --- | --- |
| `invalid_json` | Not UTF-8, not JSON, or not one JSON value |
| `unknown_property` | A property the object does not have |
| `missing_property` | A required property is absent (new) |
| `duplicate_property` | A property named twice in one object (new) |
| `invalid_type` | A value of the wrong JSON type, a number with a fraction or an exponent where an integer belongs, an enum as a number |
| `invalid_value` | The right type, but a value that is not allowed: an unknown word, a key that is not a lowercase GUID or is the empty one, an instant in another form, a culture name, a missing default text, a line break, a number out of range |
| `duplicate_key` | Two elements share a key (now only this) |
| `unsupported_schema_version` | Older than the oldest the upgraders read, or newer than the site |
| `limit_exceeded` | The size, the depth, a text's length, or an element limit |

- **Null means absent, and so do `[]` and `{}`.** A missing property takes its default, including the defaults of
  init-only properties, which the shape reader fills in. Inside a list, `null` and an empty object are refused; an
  empty list operand is refused (it would match nothing).
- **Integers only:** no fraction and no exponent, within `int` for the model's numbers. Inside settings objects, at
  most 15 digits, which every JavaScript runtime holds exactly.
- **Instants** are written `YYYY-MM-DDTHH:mm:ssZ`. A `DateTime` that is not UTC or has a fraction of a second is
  refused by the save checks, and the writer refuses it too.
- **Texts:** lengths count UTF-16 code units, as .NET's and JavaScript's `length` do. Line breaks are CR, LF, NEL,
  U+2028 and U+2029. A lone surrogate is refused (`invalid_json` in the raw text, `invalid_value` once escaped).
  Culture names, keys and dictionary keys must match to the very end of the text, so a trailing newline is refused
  (the .NET patterns end with `\z`, since `$` also matches before a final newline).
- **Ranges:** columns 1 to 4; a span from 1 to its group's columns (a repeater's children use the group's); `storeDays`
  1 to 36,500, required when `store` is `days` and absent otherwise; verification 1 to 90 days; resume 1 to 365 days;
  reference digits 1 to 10; `maxEntries` at least 1; places 0 to 1,000,000; retention 1 to 36,500 days; a redirect's
  delay 0 to 600 seconds. Text limits are ADR-0002's (500 for a line, 2,000 for plain text, 50,000 for rich text),
  plus 1 to 200 for an option value, 128 for a type or provider alias, 64 for a theme alias, and 2,000 for a redirect
  URL, a pattern or an operand.
- **Element limits:** ADR-0002's, plus **50 outcomes** per form. Fields count a repeater's children and a grid's
  rows; rules count the form-level validation rules.
- **A property a field type does not support keeps its default** (amending ADR-0002's "must be absent"): booleans
  and numbers are always written, so absence cannot be the test. 4.2b refuses a value other than the default.

### Writing

- **The canonical form:** `schemaVersion` first, then the model's declaration order; the cultures of a text and the
  properties of a settings object sorted ordinally (`""` first); `null`, `[]` and `{}` left out at every depth,
  settings included; booleans and numbers always written; no whitespace.
- **Strings are escaped as RFC 8785 escapes them:** `"` and `\`, the short escapes `\b`, `\f`, `\n`, `\r`, `\t`, and
  every other character below U+0020 as `\u00xx` in lowercase. Everything else is written as UTF-8, including `<`,
  `>`, `&`, `'`, U+007F and U+2028.
- **`DefinitionHash`** is the SHA-256 of the canonical bytes in lowercase hex. The writer throws for what it cannot
  write faithfully (a lone surrogate, an instant it would change) or what no reader would read (a document nested
  deeper than 64 levels), so a hash is never taken of a changed or unreadable document.
- **`CheckSave`** runs the save checks and then, when they pass, measures the canonical form: deeper than 64 levels or
  larger than the size limit is `limit_exceeded` at `$`. A definition built in code thus meets the limits of one that
  is read. System.Text.Json itself may nest up to 128 levels while it reads and writes the model, because it meets the
  empty lists the canonical form leaves out one level below the deepest object.

### The schema

- `schemas/jfb-form.v1.schema.json`, JSON Schema draft 2020-12, with the identifier
  `urn:jiron:form-builder:schema:jfb-form:v1`: a name, not an address, so no tool fetches it and no host is promised.
  One definition per model object, named like the record, with `additionalProperties: false`; the enums; the ranges
  and text limits above; `null` allowed wherever the reader takes it as absent, a culture's text included (once any
  culture has a text, the default `""` is a text too); a nested group has its `match` or at least one condition.
- **Patterns mean the same in every dialect:** none relies on `$`, which in .NET and PCRE also matches before a final
  newline. Keys and instants have a fixed length, and single-line texts refuse a line break with `not`.
- **What a schema cannot say** stays with the reader, and the schema's description names it: element keys that
  repeat, a property named twice, a span wider than its group, a culture twice in two letter cases, a date that does
  not exist, the spelling of a number, and the site's element limits. Lengths in a schema count code points, so the
  site is stricter for characters outside the Basic Multilingual Plane.
- **The tests** (JsonSchema.Net 8.0.5, test-only, MIT, with its dependencies JsonPointer.Net, Json.More.Net and
  Humanizer.Core, all MIT): generated definitions and the full fixture validate; the schema and the model describe the
  same objects with the same properties, required ones, references and words; the schema refuses the reader's
  refusals except the named ones, and accepts exactly those. The version is pinned and Dependabot ignores 9.0 and
  later. Nothing of it ships.

### Upgraders

- `IFormDefinitionUpgrader` (internal) reads one schema version as a `JsonObject` and returns the next. The chain is
  checked when it is built: one upgrader per version from the oldest to the current, no gap, no twin, none past the
  current version.
- Any exception from an upgrader becomes `invalid_value` at `$`, since an upgrader meets old documents nobody has seen
  yet, and a result that nests deeper than 64 levels is `limit_exceeded` at `$`. The upgraded document then goes
  through the strict reader like any other.
- With v1 the only version, the tests use a synthetic version 0 and a synthetic chain to version 3 through the real
  reader. A custom field type upgrades its own settings (ADR-0002).

### Amended in 4.8f

- **C# names before the model is public, with the JSON unchanged:** `RuleCondition.Operator` (`op`),
  `FieldOptions.OptionList` (`list`), `FormAvailabilitySettings.Access` (`members`),
  `EntryReferenceFormat.IncludeYear` (`year`), and `FormPrivacySettings.IP` with `IPHandling` (`ip`; the schema's
  definition is named `IPHandling` too). The full fixture still reads and writes byte for byte.
- **One opening for both readers:** `StrictText` turns text into strict UTF-8 within the reader's limit, refusing a
  lone surrogate, and checks that bytes are within it and valid UTF-8 throughout; the definition reader and the export
  file's reader (ADR-0046) take these steps from it, and the canonical writer its encoding.
- **What the writer refuses is reported as what it is:** a value the canonical form cannot carry is `invalid_value`
  at `$` with the writer's message, and a document deeper than a reader reads is `limit_exceeded`. The converters
  refuse what they cannot write with `ArgumentException`, so a `JsonException` while writing can only be
  System.Text.Json's own depth limit; before, every refusal of the writer was reported as too deep.
- **One home each:** the hash of canonical bytes is `CanonicalJson.Hash`, beside the writer; what publishing refuses
  is the publish check's (ADR-0035), not the serializer's; the reader of what the site stored is built with the
  serializer, not when first asked for; the static tables of Core are frozen, and a test finds any that is not.
- **Nested groups are read once:** the rule item reader looks at a copy of the reader for `match` or `conditions` and
  reads the item with the model's own converter, so a group nested many levels deep costs what its text costs, not
  that once per level.

## Consequences

- One description of the model, the source-generated metadata, drives the reader and the tests. A new property needs
  the record, the schema, the generator and the fixture; the tests fail until all four agree.
- The designer and the import get the problems of a document together (up to 100), each with a code to translate
  and a path to point at.
- The hash does not move with a .NET release, because the bytes are ours.
- The reader is more code than System.Text.Json alone, and the schema is kept by hand; the cross-check keeps it
  honest.
- ADR-0002 is amended (status line and the passages above); §4.3.1 gains the outcome limit; ROADMAP 4.2 is split into
  4.2a and 4.2b (WORKLOG).
- **Revisit** if the schema can be generated from the metadata more simply than it is kept, if the validator gets a
  security advisory with no MIT fix (the tests stay and the validator changes), or if an extension point needs the
  reader in public.
