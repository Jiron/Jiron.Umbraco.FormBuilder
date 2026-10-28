# Tests

How Jiron Form Builder is tested is decided in [ADR-0011](../docs/decisions/0011-testing-strategy.md); the Testing
project is described in [ADR-0029](../docs/decisions/0029-testing-project.md), the shared vectors in
[ADR-0005](../docs/decisions/0005-shared-logic-and-test-vectors.md) and
[ADR-0030](../docs/decisions/0030-porting-the-shared-logic.md).

## Running them

```
dotnet test Jiron.Umbraco.FormBuilder.slnx --blame-hang-timeout 10m
```

The hang timeout names a test that hangs instead of stalling the run. The integration tests use SQLite and need nothing
installed; Umbraco works in `Tests.Integration/TEMP/`, which git ignores.

Every phase review measures the line coverage of Core, which must be 85% or more (ROADMAP §4.12 and the phase-review
gate):

```
node scripts/coverage.mjs --run
```

It empties `.local/coverage`, runs the .NET tests in Release with Microsoft's code coverage collector over Core and
Web only (`tests/coverage.runsettings`), which instruments in memory and rewrites no file, so Smart App Control lets
the assemblies load (ADR-0079), merges the reports of both test projects (a line counts once, as covered when any
report hit it) and prints each assembly's coverage. Without `--run` it reads the reports already there.

The browser tests start a fresh demo host of their own and run in Edge (Playwright's Chromium in CI):

```
cd tests/e2e
npm ci
npx playwright test --project=smoke
```

## The projects

| Project | What it holds |
| --- | --- |
| `Jiron.Umbraco.FormBuilder.Tests.Unit` | NUnit 3 and NSubstitute, no Umbraco host: the architecture tests (Core knows nothing about HTTP), the vector runner and the tables check for Core's shared logic (`Logic/`), the definition model and its JSON (`Definitions/`, below), the field types (`FieldTypes/`, below), the merge-tag service (`MergeTags/`, below), the host configuration with its start-up checks and IntelliSense schema (`Configuration/`, below), the backoffice settings (`Settings/`, below), the form service's parts (`Forms/`, below), the permission model (`Access/`, below), the management API's policies, access filter and answers (`ManagementApi/`, below), the public API's middleware and filter (`PublicApi/`, below), and the Testing project's own tests. |
| `Jiron.Umbraco.FormBuilder.Tests.Integration` | A real Umbraco (`Umbraco.Cms.Tests.Integration` 17.x) with Jiron Form Builder's composers: the boot test (each service of Core resolved, one per site, and each notification handler registered), the people factories against real services, the test-only endpoints on a real site, the schema and its migrations, the repositories (`Persistence/`, below), the settings service (`Settings/`, below), a site's own field types (`FieldTypes/`, below) and merge-tag sources (`MergeTags/`, below), the form services and the change pipeline they share (`Forms/`, below), the access service with the authorization policies (`Access/`, below), the management API (`ManagementApi/`, below) and the public API (`PublicApi/`, below). |
| `Jiron.Umbraco.FormBuilder.Testing` | Test-only code for the tests and the test hosts, never packed. |
| `vectors/` | The shared vectors that Core and `packages/logic` both run (below). |
| `e2e/` | The browser tests: Playwright against a fresh demo host on 5520 ([e2e/README.md](e2e/README.md), [ADR-0031](../docs/decisions/0031-browser-harness.md)). |

## The host configuration

`Configuration/` checks the `JironFormBuilder` options as a site starts them (ADR-0042): `HostOptions` binds a
configuration, runs the validator and builds the services, and `HostOptions.Start` runs what the host runs before it
serves anything (`IStartupValidator`). A site that must not start is also run as a real process: the browser tests'
`startup` project ([e2e/README.md](e2e/README.md)).

`AppSettingsSchemaTests` generates the IntelliSense schema from the options and Core's XML docs and fails when
`src/Jiron.Umbraco.FormBuilder.Core/appsettings-schema.Jiron.Umbraco.FormBuilder.json` is stale; run the unit tests
with `JFB_UPDATE_SCHEMA=1` to rewrite it. Every `$ref` of the committed file must resolve within the file.

## The shared vectors

Rules, validation, merge tags and calculations exist in C# and in TypeScript, and both must give the same answer
(D10). Each module in `vectors/suites/` holds cases in the `jfb-vectors/1` format, and `vectors/build.mjs` writes it to
`vectors/<suite>.json` as plain ASCII, with special characters built from code points:

```
node tests/vectors/build.mjs
node tests/vectors/build.mjs --check
```

Both runners read every `vectors/*.json` in place, one test case per vector: `Logic/VectorTests.cs` in the unit tests
(a case is named `Vector(<suite>/<id>)`) and `packages/logic/test/vectors.test.ts` in the client's `npm test`. A case
that passes in one runtime and fails in the other is a bug in one of them. `spike` holds the 163 cases of 1.5, `port`
those of the port (3.4b, 4.8f), and `rules` (5.2a) every operator against every kind of answer and whole forms run by
the form engine ([ADR-0061](../docs/decisions/0061-the-form-rules-engine.md)): a case's `form` with its `values` and the
`rows` of its repeatable groups, expecting the `FormState`. `validation` (5.2b, 5.2c, 5.4a) holds the built-in types'
checks at their edges, the forms of email addresses, phone numbers and web addresses
([ADR-0069](../docs/decisions/0069-the-checks-from-text-to-country.md)) and a field's own `pattern` and `matchesField`
checks ([ADR-0062](../docs/decisions/0062-the-built-in-types-checks.md)): a case's `field` with its effective settings,
its options, each a value or a value with the `parentValues` it belongs to, and the `parent` whose answers filter
them, `fromSource` for options a list or another source resolved or, without them, for a source that could not give
any ([ADR-0077](../docs/decisions/0077-answers-and-labels-from-resolved-options.md)), its rows (each an `id`, and
`required` when it is marked so) and its checks, and its `answers`
(values, parts and files), with the other fields' `values` by id, expecting its `errors` and the indexes of its
failing `checks`. An email's domains are the server's alone, so `FieldTypes/BuiltInValidationTests.cs` covers them;
it, `BuiltInChoiceValidationTests.cs` and `BuiltInPeopleAndFileValidationTests.cs` restate every check at its edges
through each type's `Validate`. `calculations` (5.3a) holds the calculation language
([ADR-0063](../docs/decisions/0063-the-calculation-language.md)): a case's `formula` with its `values`, expecting its
result, null without a value, or the mistake's code with the position where it starts, and whole forms with
calculation fields, whose results every `form` case expects in `calculations`. `merge-tags` (5.3b) holds the merge-tag
engine ([ADR-0064](../docs/decisions/0064-the-merge-tag-engine.md)): a case's `template` with its context and its
values by source, rendered with the context's culture from the file's `cultures`, expecting the `text` and the
`problems` with their positions; the `place` cases put one tag after each piece of markup, address or JSON that
decides whether a tag may stand there. Its `recall` cases (5.3f) render a visitor text with answer recall
([ADR-0068](../docs/decisions/0068-answer-recall.md)): a form and answers of the file's defaults by name, the page,
group or field whose text it is, the fields the rules hid, the text and its context, expecting the `text` and the
`problems`; since 5.5h they hold the `field` source without an alias and values with parts that Core never writes but
a client may store (a name twice, names like numbers, JSON whitespace and escapes, a lone surrogate, an empty object,
an array; [ADR-0083](../docs/decisions/0083-merge-tags-and-the-two-runtimes-after-the-phase-5-review.md)).
`Logic/RecallVector.cs` first holds the form to what the browser is given (each field's kind, parts, rows
and recall mode are its type's, `builtIn` names the built-in types, countries are named and yes and no are worded as
the server does) and renders over a site with its own types, whose displays the visitor texts must not use.

**The mutation check** proves the vectors. Each mutant in `vectors/mutants.mjs` changes one piece of the logic in one
runtime, that runtime's vector runner runs, and the file is put back; the check fails on a mutant that nothing catches,
unless it is written down there as an equivalent change with its reason:

```
node tests/vectors/mutants.mjs                 # every mutant: seconds each in TypeScript, a build each in C#
node tests/vectors/mutants.mjs --runtime ts    # one runtime: ts or cs
node tests/vectors/mutants.mjs --name form     # the mutants whose name contains the text
node tests/vectors/mutants.mjs --check         # only that each mutant's code is there once (the scripts' tests run it)
```

Run it again after changing the logic. A survivor needs a vector in `vectors/suites/`, or a reason why no input can
tell the change apart. Lone surrogates are the one thing no vector can hold, since .NET's JSON reader refuses them, so
`packages/logic/test/text.test.ts` and `Logic/TextSemanticsTests.cs` cover them, for the merge tags
`packages/logic/test/templates.test.ts` and `Logic/TemplateInjectionTests.cs`, in every context whose output must be
well formed, and for a stored value with parts `packages/logic/test/parts.test.ts` and
`FieldTypes/FieldPartsJsonTests.cs`, a raw one giving no parts in both. `Definitions/DefinitionLogicTests.cs`
shows what the engine's model holds for a definition, and what the engine decides from it.

**What long answers cost** is checked in both runtimes too, by `Logic/LogicCostTests.cs` and
`packages/logic/test/cost.test.ts`: rules over a number answer of 10,000 digits take under 50 ms (and in Core
allocate less than one copy of it, so no number, key or cleaned copy is built), a text condition against 20 operands
folds its answer once, an evaluation compiles each pattern once (Core shows both by what it allocates, TypeScript by
counting the calls), and a pattern's run keeps one set of buffers, which Core shows by what it allocates and TypeScript
by its time over ten times the answer. What numbers compare as lives in the shared vectors, not in these tests. `Logic/TextSemanticsTests.cs` shows
that cleaning returns a text it leaves alone as it is, and puts each replacement where it belongs.

`Logic/FormulaFuzzTests.cs` and `packages/logic/test/formulas.test.ts` run 10,000 random formulas each, from a seeded
generator: built from the grammar, broken by chance and worked out over random answers, each gives a result or a
mistake, nothing else, in at most 50 ms (the fastest of three runs), and together they reach every outcome.
`Logic/TemplateFuzzTests.cs` and `packages/logic/test/templates.test.ts` do the same for 10,000 random templates in
every context and culture: none throws, each renders in at most 50 ms within the output limit, the problems come in
the order of the template, and together they reach every problem, each at least 20 times. `Logic/TemplateInjectionTests.cs`
and the same TypeScript file put hostile values through every filter: where a tag may stand they add no element,
attribute, header line, address part or JSON token, and where it may not the template renders as it is written.

**The tables check** compares the two runtimes' Unicode data. `npm run tables` in `packages/logic` writes what
TypeScript's clean, fold, NFC recomposition and the merge tags' `upper` and `lower` do to every code point into
`vectors/tables/typescript.json`; a Vitest
test fails when the file no longer matches the running Node, and `Logic/UnicodeTablesTests.cs` compares Core with it,
listing the known gaps exactly.

## The definition tests

`Tests.Unit/Definitions/` tests the definition model, its reader and writer, the schema and the upgraders
([ADR-0034](../docs/decisions/0034-definition-model-reading-and-writing.md)), and publishing's structural validation
and the public projection ([ADR-0035](../docs/decisions/0035-structural-validation-and-public-projection.md)):

| Tests | What they show |
| --- | --- |
| `RoundTripTests` | 500 definitions from `DefinitionGenerator` (one per seed) write, read and write to the same bytes and model, and between them set every property of the model. |
| `CanonicalFormTests` | `fixtures/definitions/v1-full.json`, written by hand with every property, reads and writes byte for byte, and its hash. |
| `RefusalTests` | Every save refusal with its code and JSON path, each one change to a valid document. |
| `SaveCheckTests` | Every enum property of the full fixture set to a value its enum does not define, and every required value set missing, is refused at its path and by the writer, and the fixture reaches every such property of the model; a rule nested 100,000 groups deep is too deep at `$`, and at every depth what the checks pass reads back. |
| `RuleItemReadTests` | An error inside a rule item, and inside groups inside groups, named at its full path. |
| `DefinitionSchemaTests` | `schemas/jfb-form.v1.schema.json` validates the generated definitions and the fixture, names the same objects, properties, required ones and words as the model (`ModelProperties`), and refuses the refusals a schema can say. |
| `UpgradeTests` | A synthetic older version read through the real reader, and the chain's checks. |
| `DefinitionValueTests` | Localised texts (a null culture refused as documented), default values, settings objects, instants and the string escaping; what the writer cannot write reported as what it is; the reader of what the site stored built once; both readers refusing text and bytes alike; a group nested 30 deep read in about the time of one; a string from outside the definition made well formed, each lone surrogate replaced. |
| `VisitorTextsTests` | The visitor texts of a visitor's form in the form's order with their owners and properties, a repeatable group's children after it; only the public text settings that take merge tags, nested ones by their path; no consent's text. |
| `ModelListTests` | No list of a read definition, its value types or its projection can be changed through a cast or through the list a record was built from, none takes a null item, and records compare their lists by their items, in order. |
| `StructureTests` | A document that publishes, and each publish refusal with its code and JSON path as one change to it (aliases that differ only in letter case and option values that fold to one text included); what publishes; that a save refusal comes first; that each one-per-form type counts on its own; and 500 generated definitions checked without throwing, every path naming a part of them. |
| `FieldTypeSupportTests` | §4.3.2 restated apart from the code, against the field type registry: each built-in type refuses exactly the common properties, checks, rules and messages it does not support, and declares the palette group, storage, rule kind, multiple setting, one per form, server-only default, settings and public settings written there; the one list of field messages that both checks use holds every message of the record with its own kind. |
| `RedirectUrlTests` | The redirects that could leave the editor's site, and those that stay on it. |
| `ProjectionTests` | 500 generated definitions with a marker in every private place project without it; the projection names only allowlisted properties; `fixtures/definitions/v1-full.public.json` is the reviewed projection of the fixture; which changes move `PublicHash` and which do not. |
| `DefinitionDiffTests` | The comparison of two versions ([ADR-0045](../docs/decisions/0045-versions-comparison-and-draft-cleanup.md)): texts per culture, a type's settings one by one, option items by value and grid rows by key with their order, moves to another parent and the fewest moves within one, rules whose order means nothing, settings groups by name, the order of the changes. Then 100 generated definitions, each changed by `DefinitionMutator` as editors change them: the comparison swapped mirrors it, names each element once, and each changed element's property changes rebuild its newer properties from its older ones. Also when a save changes the actions ([ADR-0051](../docs/decisions/0051-form-endpoints.md)): an action, or a rule of one added, changed or moved in or out, and nothing else. |
| `DefinitionKeysTests` | The element keys of a definition ([ADR-0046](../docs/decisions/0046-copies-templates-and-import.md)), the same as the model's by reflection over the fixture and 500 generated definitions; renewing them and mapping them back gives the same bytes; every reference follows, in rules, checks, the verification field and settings (as values, property names, in any letter case and in lists), but not a key inside a longer text, another format or the key of something outside the form. |
| `ActionCopiesTests` | Copied actions: new keys, fields mapped by alias in any letter case, grid rows by a single matching label, what is not mapped kept and reported with its path, the merge tags in the settings that name a field the target lacks reported with the path of their text and where they start (and those it knows, by alias or former alias, of other sources or not well formed, not), and the target's limits. |
| `FormExportFormatTests` | The export envelope: a round trip, two-space indentation with line feeds and ASCII escapes, `schemas/jfb-form-export.v1.schema.json` (which refuses what the reader refuses, unless a schema cannot say it), the format checked first, every refusal with its path, the definition measured in its canonical text, an older definition upgraded, and a file with thousands of bad option lists answering its first 100 problems. |

A new property of the model needs the record, the schema, the generator and the fixture: until all four agree, the
coverage, schema and fixture tests fail.

## The field type tests

`Tests.Unit/FieldTypes/` covers the field type framework without a host
([ADR-0055](../docs/decisions/0055-field-type-framework.md), [ADR-0056](../docs/decisions/0056-field-settings-checks.md),
[ADR-0058](../docs/decisions/0058-field-answers-and-values.md), [ADR-0059](../docs/decisions/0059-changing-field-types.md),
[ADR-0060](../docs/decisions/0060-designer-locks-of-field-settings.md)):

| Tests | What they show |
| --- | --- |
| `FieldTypeRegistryTests` | The collection builder holds the built-in types in palette order; a type appended, replaced or removed (a field of a removed type is `unknown_type` at publish), a custom type's field publishing with only its public settings projected; an alias taken twice named with how to replace the first, and each contradiction of a declaration refused naming the type, a type that keeps its default on the server and stores files among them; the start-up check on a valid site and on two broken ones. |
| `FieldSettingsSchemaTests` | A settings class read into its schema: each property's kind and editor, a choice's values with their labels, defaults as settings store them, the order by group, order and declaration, the attribute's details and JSON names, public settings at the top only; every class or property that cannot be a setting refused with its reason (all reasons at once), the base class naming the type, and the strict JSON that reads settings. |
| `FieldSettingRulesTests` | The rules each validation attribute gives a setting, on lists' items too; the limits of a setting without them; every attribute that does not fit its setting refused with its reason (all at once); and a default that breaks its rules refused, quoting it. |
| `FieldSettingsChecksTests` | A field's settings held to its type's schema: each kind's values and wrong types, every bound, length, count and allowed value at its edge, values twice in a list as they compare, localised texts, required settings at the top and inside objects, each problem's code, path and message; the type's own check through the base class and the interface, its places, and a check that throws or cancels. |
| `FieldTypeAvailabilityTests` | Whether a type can be used: available by default through the interface and the base class, a type's own answer with its reason, and every check that throws, faults, answers null, cancels itself, blocks or takes longer than five seconds reported as not configured with the shared reason and logged, while the request's cancellation still cancels the answer. |
| `BuiltInSettingsTests` | The rules of every built-in type restated apart from the code and held at their edges, each type's own check through a publish, the lists that start with items, and the country codes against .NET's regions. |
| `FieldPartsJsonTests` | A stored value with parts whose text holds a lone surrogate itself, in a text, in a name, or after an escaped high surrogate, giving no parts instead of throwing, as the website's reader gives none; a well-formed pair read (ADR-0083). |
| `FieldAnswersTests` | What a type receives and gives back: a field's texts and parts, with empty, nameless and repeated ones refused; the longest answer at its edges and none, a part's name counting in it when the part has texts; a value with its row and the text it stores; a reading that refuses a row the field lacks and keeps nothing once unreadable; a validation that keeps each error once and refuses kinds the type does not declare. |
| `FieldValuesTests` | The contract as the pipeline runs it, through a type on the interface alone (one text, choices, a `multiple` setting, no value), one with settings on the base class, and the built-in number: the limits with `maxLength` or `invalid`, the reading, and the order of `invalid`, `required` and the type's own errors; a type without limits, with an undeclared error kind, or reading a value its storage kind cannot hold refused by name, without the answer; what a check sees besides the answers (whether the field is required, today, and the file each upload token stands for, one for each value and only for a field that stores files); and the field's own checks after its type's, by index: a pattern on the whole of every value, one outside the safe subset checking nothing, values matching another field's as the field's kind compares them, and none without a readable value; and a number past the 15 digits a stored number keeps refused alone, with `digits` or, for a type without that kind, `invalid`, while text keeps every digit. |
| `PostedNumbersTests` | Numbers as posted (leading zeros, a point first, an exponent) converted exactly to the canonical wire form, those past the 15 digits a stored number keeps read for the check to refuse, and everything else refused: no number, other digits, or past the 60 digits a calculation reads. |
| `FieldTypeChangesTests` | Which types a field may become: every built-in type that holds a value of its own into every other, none of content, grids or repeatable groups; 25 built-in changes restated apart from the code with everything each keeps and loses; every rule by which a setting is kept, narrowed or lost (kinds, item kinds, required, bounds and their exclusivity, lengths, counts, allowed values, choices, nested objects, any JSON); the settings a change needs; the operators of each rule kind; and a site's own type changing like the built-in ones. |
| `FieldSettingLocksTests` | The host's designer locks of field settings: a value of every setting kind read as configuration gives it and written as settings store it, and every value its setting refuses, a site's own type's among them, each stopping the site with its key and what the setting takes, never the value; a type or a setting the site lacks, with the spelling suggested for another letter case; a required setting locked without a value; locks of an action type and of a shape the options refuse, and field types that cannot be a registry, left to their own checks; the site starting through the host; every field of the type given the locked value or its default, a repeatable group's child fields too, and a definition no lock changes kept as the same instance; and a draft save refused at the first locked setting in document order that holds another value, unless the value is the lock's, the default, left out or the one the stored draft held for the same field and type, compared as its kind compares. |
| `BuiltInAnswersTests` | The longest answer of every built-in type, restated from ADR-0058 apart from the code, and how they read: wire forms, choices with "Other", grids by row, rankings in order or by position (an option or a position given twice read for the check to refuse), a consent's tick with its wording's hash, names and addresses as one object of their parts in order, and a country only as its ISO code. |
| `BuiltInValidationTests` | The checks of the text, number and date types at their edges through each type's `Validate` (ADR-0062), the forms of email addresses, phone numbers and web addresses (ADR-0069), an email's confirmation and its domains, which only the server checks, and the types without checks of their own. |
| `BuiltInChoiceValidationTests` | The checks of the choice types at their edges: options compared exactly, from a list that could not give its options or none at all, the selections, the "Other" text, a grid's columns from its scale or its options and the rows it needs, a ranking's length and each option and position once, and a country's allowed list. |
| `HeldOptionsTests` | The options a check holds a field's answers to (ADR-0077): its own, or those its list or source resolved; for a field with a parent, those of the parent's answers (exactly compared, none for no answer or another field's) and those of every answer; none for a field without options or a source that resolved none; any answer for a source that could not give its options; 10,000 cascading options against 10,000 parent answers held in under a second (a set of the answers, ADR-0080); and a site's own type checking against the same set. |
| `BuiltInPeopleAndFileValidationTests` | The required parts of a name and an address as ADR-0056 reads them, and a file's count, extension (every extension of every group of file types) and size at their edges. |
| `BuiltInUploadTests` | The checks of an upload (ADR-0071) as the endpoint runs them: a signature's PNG header (every colour type and bit depth PNG allows, and each way a header is broken: its signature, chunk length and type, sizes of 0 and past what PNG allows, methods, CRC, a file that is no PNG, one too short), its size in pixels at three times its pad or the widest one, and its mebibyte under a host's larger or smaller limit, failing `invalid`; a file field's limit and `fileSize`, and its extensions at upload as its check of a submission takes them, its bytes left to the host; and the contract through a site's types: the host's limit and nothing found by default, a check of its own through its settings, each kind once and only declared kinds, and refusals of a field that stores no files, a head past 512 bytes, an empty name, a negative size and a type's limit below 1 byte. The headers carry a CRC computed with a table, pinned to two zlib computed. |
| `BuiltInRowsTests` | A repeatable group's number of rows held to `minRows` and `maxRows` at their edges with `rowCount` (ADR-0071), its most rows as its settings' limit, a site's type with child fields finding nothing unless it checks, and the check refused for a type without child fields and for a negative count. |
| `BuiltInWithoutAnswersTests` | The calculation, the repeatable group, the content types and the captcha placement ignoring what is posted for them (a number, a text far past any limit, a thousand texts, a text twice, parts, nothing): neither limited, nor read, nor checked; and these are exactly the built-in types without answer limits. |
| `FieldDisplayTests` | What a type shows its values as in merge tags (ADR-0066): country names in the culture's language or by their code, the words of yes and no by culture, numbers rounded and padded to clamped places, dates and times in the culture's formats, option labels or the value, empty texts showing nothing, files one for each value, prefixes and suffixes, a hidden value and an unseen calculation left out of the list of the answers only, a type on the interface alone showing as the defaults do, and the answers merge tags read refusing what contradicts itself. |

`TestFieldTypes` (in the Testing project) gives the unit tests the registry of the built-in types, or of a builder a
test changes, as a site's container builds it.

`Tests.Integration/FieldTypes/` boots a real site whose composers change the field types: one adds a colour field
before the core composer, one removes the signature after it. Both are found like any site's composers and do nothing
unless the site sets `JironFormBuilderTests:SiteFieldTypes`, so every other test's site keeps the built-in types.
`SiteFieldTypeTests` shows the registry, start-up, publishing, the field catalogue and the public revision with the
site's types; `SiteFieldTypeApiTests` shows the backoffice's field types on that site: the colour type last, not
configured with its reason while the site lacks its swatches, its plain labels, and no signature.

## The merge-tag tests

`Tests.Unit/MergeTags/` covers the merge-tag service, its places and its sources without a host ([ADR-0065](../docs/decisions/0065-the-merge-tag-service-and-its-sources.md)), over
`MergeTagTestSite`: the service of a site with its own sources and filters, a time zone, a clock and field types, and a
logger that keeps what it is given. `MergeTagAnswerForms` makes forms of any fields and their answers
([ADR-0066](../docs/decisions/0066-answers-as-merge-tags.md)). `TestUmbraco` stands in for the Umbraco services the
Umbraco sources read ([ADR-0067](../docs/decisions/0067-the-umbraco-sources-and-the-base-url.md)): published pages with
names and properties per culture, their ancestors, the addresses Umbraco gives them, domains, languages with their
fallbacks, dictionary items and members with their types; it counts the dictionary's reads and keeps the current
address each URL was asked with.

| Tests | What they show |
| --- | --- |
| `BuiltInMergeTagSourcesTests` | `form`, `entry`, `query`, `context` and `date`: each value escaped by its context, the entry empty before it is submitted, its date and the reference's year in the site's time zone (UTC when the zone is unknown) and the culture's format, query values only for the names the form allows and as spelt, context values passed or listed, the clock's time, each path asked for once; and `entry.reference` with and without a format, prefix and year, padded to digits clamped to 1–10. |
| `MergeTagPlacesTests` | For each place that limits sources, a built-in one it refuses and, for a site's, one per visitor, any in consent texts and entry titles, and one outside its own places, each written as is with `source_not_for_place` (rendered and checked); the sources each place takes; a site's filter refused where the browser renders too and taken elsewhere; every context a place lacks refused; and the picker's paths per place. |
| `SiteMergeTagExtensionsTests` | A site's source and filter through their interfaces: the request each gets, values escaped, lists, unknown paths; a source or filter that throws rendering empty and logged with its type and stack but not its message, a cancelled rendering cancelling; arguments refused at the filter; the check's warnings before any value; a taken name left out so the built-in one stays; and the start-up check naming each name that is taken or outside the grammar, and a source the container cannot create, by the exception's type and message, one whose constructor throws among them; a source whose name or traits throw, or a filter whose name throws, named with the exception's type, stopping the start and left out of a running registry; and a source's traits read once, its places copied, across the start-up check and the registry too, by the check before any registry is made; the usable side of shared names, the first of each; and a collection the container cannot create stopping the start and leaving the other's sources or filters usable (ADR-0083). |
| `FieldMergeTagSourceTests` | For every built-in type that holds a value (a test fails when one has no case), the answer as its type shows it, its values and its label; answers in the culture (option labels, numbers, dates, countries, the words of yes and no); the wire number or date behind a text for the filters; lists whatever the count; a value's parts; a repeatable group's child as a list across its rows and the group as its rows; former aliases after current ones; what the rules hid empty; unknown paths when checked and rendered; the picker's paths; and a type whose display throws, logged without its message and shown as the default would. |
| `AnswerRecallTests` | The form check of a visitor text refusing the field itself and a later one with `field_after_text` at their paths, and taking every field for a text after every field and in the other places; a former alias unknown in a visitor text and found in mail; a site type's own display in mail and its rule kind's default in a visitor text; a hidden field's value in mail and only its label in a visitor text; and the `field` source without an alias, an unknown path rendered empty and checked as one in every place (ADR-0083). |
| `EntryAnswersTests` | `entry.answers` as lines and as an escaped table, through a filter as text, in JSON; what the rules hid, unanswered fields, hidden values and unseen calculations left out; fields not for mail left out of mail only; and a sensitive field's tag and line in every place, named by the action to include it or not. |
| `PageMergeTagSourceTests` | `page.name` in the culture; `page.url` as Umbraco gives it with the base URL as the current address, never the request, and empty without a base URL; a property in the culture as its kind shows; `page.up.<alias>` the page's own value or its nearest ancestor's that has one; every path empty without a page or for one that is gone, with no problem; other paths unknown when checked and rendered; and any property alias known to the check without a page. |
| `SiteMergeTagSourceTests` | `site.name` as the root's name in the culture; `site.url` and `entry.url` from the base URL setting, then the site's first `https` domain, then Umbraco's application URL, and empty without one; the entry's link empty before the visitor submits and written as text in an HTML mail. |
| `MemberMergeTagSourceTests` | The member's name, email and a property they may see; values escaped by the place's context; a property the member may not see, a sensitive one or one their type lacks empty; every path empty without a member or for one who is gone; deeper paths unknown, and the places whose pages may be cached refusing `member`. |
| `DictionaryMergeTagSourceTests` | An item in the culture, its parent cultures, Umbraco's fallback languages and the default language, in that order; fallbacks in a circle ending at the default language; an item without a text in any language tried empty and a missing item `unknown_path`; the text escaped by the context and taken by consent texts; any key known to the check, the source's name alone not. |
| `DictionaryAnswersTests` | Labels, option labels and grid rows that name an item (`#key`) showing its text in the culture in `field.*` and in `entry.answers` as lines and in the table; the dictionary read once per rendering and not at all for a form that names no item; and a text naming an item only when it is the key alone. |
| `ListAnswersTests` | A choice from a library list showing the list's labels in the entry's culture (with its fallback and a dictionary item) in `field.*` and `entry.answers`, its `.value` the value (ADR-0077); a value the list no longer offers, a list the site lacks and an option without a label showing the value; a grid's columns from a list showing its labels; a cascading code source asked once for a rendering, on the scope's page, for each row's parent answer, so two rows whose parents' sources share a value each show their own label, a parent outside the child's group, at the top or in another group, asked for all its answers once, and a chain through a row and out of it asking each parent as it lies; a parent the rules hid, by its own rule or its repeatable group's, giving none, and one hidden in a row having no answer there; a parent from a source passing on only the options it resolved; a parent whose options are unknown leaving its child's values as they are, asking nothing; a chain of parents leading back cut where it meets itself; and a choice the rules hid or left unanswered asking nothing; and a source's labels read only for the tags that show its field's answers, once for all of them in a rendering, and not at all for a value or a label; and two renderings that read one scope's labels at once both showing the options kept first, which later tags read without asking, on timers that never fire on their own (ADR-0083). |
| `MergeTagPropertyValuesTests` | Content and member values as the tags show them: texts, numbers, dates and times in the culture's formats with the wire form behind, yes and no in the culture's words, rich text as its text, several values as a list and other kinds as nothing, a content item's name in the culture or its invariant one, and a value only where Umbraco says the property has one. |
| `DotNetCultureFormatsTests` | For every culture .NET knows, dates, times, numbers and the culture's own amounts printed with the written formats as .NET prints them, the cultures whose patterns leave the subset listed from the data with the invariant culture's pattern taken; the medium date derived from the long one; the separators escaped; the Gregorian calendar for a culture with another; the region's currency; and a name .NET does not know. |
| `TemplateValuesTests` | What a visitor's browser renders a template with (ADR-0085): whether it renders other than as it is written (a tag the place takes, or a limit that ends the output; never its own text or refused tags alone), each path's value but answer recall's in a visitor text, resolved once for a path named twice, every field a recall tag names, refused or not, and in mail the fields' values themselves. |
| `MergeTagRecallFormsTests` | The recall form cut down to the owners and the recalled fields with what holds them, an owner with its place alone; a repeatable group whole with any of its fields and masked when sensitive; own options, a list's for no parent, none for an unavailable list or a cascading field, every country named in the culture; rows, parts, `none` for a value kept on the server and a null kind for a field that holds none. |
| `CultureFormatsJsonTests` | Every culture of `merge-tags.json` written exactly as the vectors write it, and formats from .NET read back as they were written. |

`Tests.Integration/MergeTags/` boots a real site whose composers add two sources and a filter through
`builder.JironFormBuilder()`, one before the core composer and one after it, switched on by
`JironFormBuilderTests:SiteMergeTags`: the site starts, and the service renders and offers them beside the built-in
ones; with the site's colour type switched on too, answers show as their types write them, the site's own type's too.

`UmbracoMergeTagSourceTests` renders the Umbraco sources over real content, with a fresh database per test: a library
in English and German under two domains, with a page type, a German language that falls back to English, dictionary
items and a member type with a property the member may see and one they may not. A mail rendered without a request
gets the page's name, German address, property and ancestor's property, the site's name and address, dictionary texts
with their fallback, the member's name, email and visible property (the hidden one empty), the entry's link and an
answer whose label names a dictionary item; the site's own base URL, saved through the settings service, comes before
its domains while a page under a domain keeps the domain's address; and during a request the addresses are the site's,
not the request's host. Content changes reach Umbraco's navigation and URLs through the cache refreshers on a local
server messenger, as on a site with one server.

## The option list tests

`Tests.Unit/OptionLists/` covers option lists and sources without a host
([ADR-0073](../docs/decisions/0073-option-lists-and-sources.md),
[ADR-0075](../docs/decisions/0075-content-and-data-type-lists.md),
[ADR-0076](../docs/decisions/0076-url-and-file-lists.md)), over `OptionListTestSite`: one server with the site's
sources, the library's lists in memory (`MemoryOptionLists`, which counts reads and gives the database's refusals), a
real runtime cache, the distributed cache over a messenger that records whom it told, `TestUmbraco`'s languages,
dictionary, pages and tree, substitutes for public access, document types and data types, the calls to other hosts as
`OptionListTestHosts` gives them (the allowed hosts, `http`, a transport that by default resolves no name, and a
clock), and loggers that keep what is logged. `TestOptionSource` is a source whose options a test gives, which may
throw or wait, and keeps the requests it got; `ScriptedTransport` (in `Outbound/`) answers URL lists' fetches.

| Tests | What they show |
| --- | --- |
| `StandardOptionSourcesTests` | The months 1 to 12 named in English, German and French with a capital first letter; the weekdays in ISO numbering from the culture's first day (Sunday in the United States, Monday in Britain and Germany); the two scales and "yes, no, not sure" in English and German, a regional culture taking its language's words and any other language English's; and the five lists' names, labels and order. |
| `OptionKindsTests` | Every kind with where it may be configured, how it keeps its options and the shortest `cacheSeconds` it takes, a kind of a later version unknown, the settings of URL lists and none of file lists, and the settings and editors of content and data type lists; a manual list taking no configuration; the countries' codes known and each once; the countries named and ordered in English and German, all of them without any; a code list naming a source the site has, by its exact name; and its source's options for the request, a source that is gone a failure. |
| `OptionSourceRegistryTests` | The collection builder holding the standard lists from its start; the registry finding a source by its exact name and leaving out what start-up refuses; the problems naming each source whose name is taken or outside the grammar (64 characters at most); and the start-up check passing a site's own source and refusing a taken name. |
| `OptionServiceTests` | A field's own options in the culture with the text's fallback and dictionary items; a manual list kept until it changes, its new options showing at once and every server told; a list the site lacks or that no longer reads unavailable and logged once; a code source filtering a cascading field by the parent's answers and hearing them only then; a source that throws, synchronously or not, unavailable and logged without its message; one that takes longer than 5 seconds unavailable, whether it heeds its token or not, and a cancelled request cancelled; what a source gives cleaned (blank, too long, unclean and repeated values, a blank label, more than 10,000 options) and logged as a count; the countries on a field; a source of a later version and an upload unavailable; a source list kept for its time per culture, page and parent answer, dropped by a change, a failure never kept; a stored list of another kind with a time kept for none; a source list asked each time for two parent answers or more while one answer and none are kept; parent answers no option could have (blank, padded, longer than an option value, repeated, more than 10,000) never reaching a source; a list of a kind this version does not know unavailable and logged once; and the cache refresher dropping one list, or every list for a change it cannot read. |
| `ContentOptionKindTests` | The key of kept content options naming every setting, the root and the culture but not the document types' order; the value and the label as a property's alias at its edges (255 characters, a leading digit, a space, a dot) or their words, a key being no label; at most 10,001 pages read; a property that holds several items, or a list of one, a label but no value, a yes or no in the culture's words, and no culture read in the default language; and a root that is gone failing, logged without values. |
| `UrlOptionKindTests` | A URL list's options at the list path of a JSON answer with each value and label at their paths, by default and through nested, quoted and indexed parts; numbers and booleans as the answer writes them, anything else left out and counted; the first 10,000 of more; a byte order mark skipped; an answer with no list at the path, or nested deeper than 64, unavailable and logged with its reason; an address that fails without a copy before, or on a host or scheme the site no longer calls, unavailable and never fetched, logged without the address; a list's options kept for its `cacheSeconds`, one copy for every culture and every field of the configuration, a field's own source for 10 minutes, a stored list with less than a minute for one; the last copy in use while the address fails, fetched again a minute later; one fetch for callers that ask at once, one that stops waiting stopping no other; and the configuration's checks at their paths, the site's hosts and `http`, and the keep time of 60 seconds to 30 days; an address that fails with no copy called again only after a minute, a field of the same address calling nothing meanwhile, each failure logged once; and an address on a port the site's entry does not name refused with the entry that allows it, for a host name and an IPv6 address. |
| `OptionJsonPathTests` | The paths a URL list takes and those it refuses at their edges (an index of seven digits, quoted names with their two escapes, 256 characters); where each leads in an answer, or that it leads nowhere; and how a value there reads as a value or a label. |
| `OptionFileReaderTests` | An upload's lines in CSV with a comma or, as its first line says, a semicolon, and in TXT with a bar; quoted parts with the separator and doubled quotes, spaces around parts left out; a quote left open and text after a quoted part refused at their lines; a byte order mark skipped and any other encoding refused; empty lines skipped but counted; a header on the first line only; a file with no options refused; a repeated value naming both lines; 10,000 options and no more; a value of 200 characters and a line break refused, once per line; more than two parts refused but empty columns after them left out; 4 MiB at most; a file's format by its name; and a file read becoming a file list's items, offered in order. |
| `OptionListServiceTests` | A list's alias, name and time within their bounds, `cacheSeconds` for code and URL lists only; its configuration held to its kind at its path; its items read and checked as a field's own options, for a manual or file list only, at most 10,000; a create, an update and a delete each telling every server, the database's refusals as problems, a change made meanwhile dropping the kept copy; and a missing list kept as missing until it is created. |
| `OptionSourcePublishCheckTests` | A field's source configuration held to its kind at `options.config`, an address on a host the site does not call at `options.config.url` and an upload refused at `options.source`, in every page, group and repeater, while own options and a library list are left to the structural validation. |
| `CascadingOptionsTests` | A field's options for a request's answers up its chain of parents (ADR-0086): a parent's answers cleaned, held to its own options and given once each in their order; a parent from a source offering what it resolves for its own held answers, on every level; a parent without answers not asked for; one whose options are unknown making the field's unknown without asking for it; a parent outside the form or without options, and a chain that leads back, offering nothing; a repeatable group's field with its parent outside the group. |

`Tests.Integration/OptionLists/OptionListSiteTests` publishes a form through the form service whose fields name a
source the site lacks and an unknown country, refused at their paths, then publishes it once fixed; changes a manual
list through SQLite, its new options showing at once and each change told to the other servers; drops this server's
copy of a list on another server's cache instruction read from its JSON; reads a row of a kind this version does not
know as an unreadable list, by key and in the library's page, with no options; and checks the site's registrations:
the refresher, the service, the standard lists and the publish checks in their order.

`Tests.Integration/OptionLists/SiteOptionSourceTests` boots a site whose composers change the sources through
`builder.JironFormBuilder().OptionSources()`, one before the core composer and one after it: a source added last, the
months replaced by the site's own in their place and under their name, and "yes, no, not sure" removed, which a field
then finds unavailable; the site starts. `SiteOptionSourceTakenNameTests` boots one whose composer adds a source under
the weekdays' name: the start-up validation the host runs as it starts refuses it with a message that names both
types, kept by a wrapper around the site's own validator, since the harness starts the site before any test runs.

`Tests.Integration/OptionLists/ContentOptionListTests` reads a real site's content in English and German: an events
page with a concert in both languages, a workshop in English only, a draft, a members' night behind public access and
an archive folder with an old fair, and a dropdown of sizes. It shows each content setting (a picked root and the page
the form is on, the document type filter, descendants through a folder, values and labels from properties, numbers
in their wire form, the order by label in German) with the draft and the members' page left out; a dropdown's, a
checkbox list's and a radio button list's values, and a deleted data type failing; a published page, an unpublished
one, a newly protected one and a saved data type showing at once for a field's source and a library list; each of
Umbraco's refresher notifications, as every server publishes them, dropping what this server kept; and saving a list
and publishing a form refused at the paths of a page, a document type, properties for the value and the label and a
data type the site lacks, a page in the recycle bin, a textstring that is no list of values, and a content list's
`cacheSeconds`, while an alias in another case passes. Umbraco's own handlers tell its cache
refreshers of the changes, and a messenger runs them on this server at once.

`Tests.Integration/OptionLists/UrlOptionListTests` runs a real site in Development with the Testing assembly's stub on
a free port and a host configuration that calls 127.0.0.1 over `http`: a URL list saved through the list service
offers what the stub answers, asked for once in two cultures as it is kept, with the site's `Accept` and `User-Agent`;
and a list on a host the configuration does not name is refused and never called. `UrlOptionListProductionTests` is
the same site outside Development with a stub of its own running: the Testing assembly leaves the site's transport in
place, which refuses loopback, so the list offers nothing and the stub is never called.

## The outbound tests

`Tests.Unit/Outbound/` covers calls to other hosts ([ADR-0076](../docs/decisions/0076-url-and-file-lists.md), §4.10).
`OutboundHttpTests` runs against a stub on a port the system chooses, through the transport the Testing assembly
registers (a test's name resolves to loopback, and only the stub's endpoint is reached), and `ScriptedTransport`
answers what no socket shows: an `https` address, a body whose length is not declared.

| Tests | What they show |
| --- | --- |
| `PublicAddressesTests` | Globally reachable IPv4 and IPv6 addresses, each refused range at its edges, and an IPv6 address that carries an IPv4 one (mapped, NAT64, 6to4) judged by it. |
| `OutboundHostsTests` | A host compared in lowercase and its ASCII form, an IPv6 address without its brackets; anything but one host refused, a host name or IPv4 address in brackets among them; an entry read as a host and the port it names, an IPv6 address with a port in brackets and one without them a host alone; anything but one host and one port from 1 to 65535 refused; none allowed and `http` off by default; entries with ports passing beside the same host without; and start-up refusing a value that is no host and a host and port named twice, naming the keys. |
| `OutboundHttpTests` | An allowed host called with a `GET` for the media type as Jiron Form Builder; `https` only unless the host allows `http`; a host not allowed, an address with a user name and a site that names no hosts refused before anything is sent; a name that resolves to a loopback, private, link-local or unique local address, or to one beside a public one, refused before anything connects; the site's transport refusing loopback by name and as an address, and being the one registered; the test transport reaching the stub and no other loopback address or port; redirects to the same host followed three times and no more, to another host refused even when it is allowed, from `https` to `http` refused; an answer over the cap refused whether it declares its length or not, and one that declares it before its body is read; one too slow timed out and a caller that stops waiting cancelled; a status that is no success refused with it; a host that cannot be reached; a host called on its scheme's standard port or one its entry names, on either scheme, `http` on port 443 refused, and nothing sent to a port not allowed; a redirect to another port refused unless an entry names it; a body that breaks off or does not decompress a failure with its reason, a gzip, zlib or Brotli body through the real transport among them, and a stream disposed while it is read staying what it is unless the time limit passed, when it timed out; and failures that name their reason, never the address. |

## The Testing project

| Folder | What it gives |
| --- | --- |
| `Time/` | `TestTimeProvider` (a clock moved by hand), `ManualTimers` (timers that fire when told) and `TestClock` (the real time plus an offset, for test hosts). |
| `People/` | `TestPeople` creates user groups, users, member groups and members, and `NewPassword()` makes passwords per run; `TestAccess` gives a group its global grants and creates a restricted folder for it; `TestPeopleSeeder` creates a host's configured people, grants and restricted folders at start-up. |
| `Mail/` | `TestMailbox` reads and waits for the `.eml` files in a host's mail pickup folder. |
| `Stub/` | `StubServer`, the local HTTP stub: webhooks, captcha verification (`StubCaptcha` tokens) and scripted answers (a redirect with its `Location`), with every call recorded. |
| `Endpoints/` | The test-only endpoints under `/jfb-testing/`. |
| `Startup/` | `StartupMarkerPopulator`, which logs a line once Umbraco has rebuilt its indexes, for the browser tests' start-up wait. |
| `Settings/` | `SampleSettings`, the settings tests' section (below), and `SettingsJson.Global`, a section's global row as the settings service writes it. |

A host turns these on in the `JironFormBuilderTesting` section; everything is off by default:

| Setting | Effect |
| --- | --- |
| `Endpoints` | Answers `/jfb-testing/status`, `/jfb-testing/clock` and `/jfb-testing/access` (each backoffice user's effective permissions, globally, at the root and in each root folder), only in Development and only for requests from this machine. |
| `Clock` | Runs the host on `TestClock`. |
| `StubPort` | Starts the stub on that port (5560 for the browser tests' host), only in Development, where calls to other hosts may then reach it on loopback at that port only (ADR-0076). |
| `StartupMarker` | Logs `Jiron Form Builder test host: indexes rebuilt` after each rebuild of Umbraco's indexes. |
| `UserGroups`, `Users`, `Members` | The people the host creates at start-up, each group with its global `Permissions`; their passwords come from the environment only. |
| `RestrictedFolders` | Root folders the host restricts at start-up, each to one group (`Name`, `Group`, `Permissions`); a folder of that name in the trash is left there and a new one made. |

## The schema tests

`Tests.Integration/Persistence/` runs the migration plan on a fresh database per test and reads the schema back with
queries of its own (`SchemaInspector`), comparing it with `ExpectedSchemaV1`, which is written out by hand from §4.5
and [ADR-0033](../docs/decisions/0033-schema-v1.md). The same tests run against SQL Server in the `sqlserver` CI job.
Two of them compare with files under review, and rewrite them when `JFB_UPDATE_SNAPSHOTS=1` is set (and then fail, so
the diff gets read and committed; the API documents are more such files, below):

- `SchemaV1DdlTests`: the SQL Server statements of step 1, rendered without a server, in
  `Persistence/schema-v1.sqlserver.sql`;
- `DatabaseDocsTests`: the diagram in [docs/database.md](../docs/database.md).

Step 1 itself is frozen ([ADR-0054](../docs/decisions/0054-schema-freeze.md)): `Tests.Unit/Persistence/SchemaV1FreezeTests`
describes its snapshot DTOs and filtered indexes from their declarations and compares the SHA-256 of that description
with a constant, which no switch rewrites. Its other tests show that every kind of declaration that shapes a table
changes the description, and that the class names and the layout of the source do not. A schema change is a new step.

## The repository tests

`Tests.Integration/Persistence/Repositories/` migrates a fresh database per test, with a clock that stands still
(`RepositoryTestBase`). The tests cover:

- each repository's reads and writes, and every refusal, with nothing written (for entries, the number too), the text
  keys of values with parts made of what their parts read included. A
  refusal must come from the statement's own condition: `AssertRefused` fails when the unique index refused instead;
- the unique index as the backstop: `CommandRewriter` (in `StatementInterceptors`) takes the condition out of one
  statement, so the test shows the index's refusal classified as the condition's would be;
- writers at the same moment: of ten saves from one version, exactly one gets through; fifty entries stored at once get
  the numbers 1 to 50, each once, and ten retries of one submission store one entry;
- what option lists, themes and email layouts share, written once in `AliasedLibraryRepositoryTests` and run for each
  of the three;
- units of work that commit or roll back together;
- the reruns after SQLite's real busy error;
- the entries query builder (`EntryQueryRepositoryTests`): every filter kind with the 1.3 spike's numbers and dates,
  text compared as rules compare it (`ß` and `ss` apart, texts longer than their key), the sorts and their keyset
  pages, the counts, and a list past its time budget while a submission comes in;
- SQLite's plans of every shape the query builder writes, with and without planner statistics: no scan of entries
  or values, and no temporary b-tree for a sort by a value (`EntryQueryPlanTests`), with a walk stripped of its
  unary plus that the check must catch;
- SQLite's plans of the statements the indexes of 4.8a serve (`IndexPlanTests`): the statements a repository really
  issues, recorded by `StatementRecorder` (in `StatementInterceptors`) and explained with their values, each naming
  its index and scanning nothing: an entry's files and the uploads that wait for one, one form's daily statistics
  and their delete, retention by day, and a content item's references; a create's checks of its alias and its
  key, each a search of its own unique index, never one search over both (4.8b); and one form's uploads that wait
  for an entry, for its delete, sought on both columns of the index that leads with the form (4.8g);
- stored names: a state written in another letter case is refused when read, as a row something else wrote, and
  `Tests.Unit/Persistence/StoredNamesTests` shows every stored enum written as the camel case of its names, read back
  only from that text, and fitting its column; a scan state or an option list kind in another letter case is
  refused as well, since every stored name is a camelCase word (4.8g);
- the outbox (`ActionRunRepositoryTests`, `EmailRepositoryTests`): every state change, a claim once (ten claims at
  once, which SQLite's gate takes in turn; the two-processor test is 8.1's), a token of each claim's own, outcomes
  only with the claim's token and from the states they need, the order of a batch and "stop the rest" within it,
  leases that run out and the late finisher, retries from the log, undo, the purge and an entry's deletion
  (ADR-0007, ADR-0040);
- the audit log (`AuditRepositoryTests`): entries that commit and roll back with their change and come back as
  stored, the filters and pages newest first, a view recorded once per window (also twenty at once, and again after
  a unit that rolled back), the purge oldest first, a filtered list past its time budget, a budgeted query stopped
  through its unit answered as cancelled, one inside another that keeps its own caller's token, and an entry full of
  personal data through every kind of action about entries, after which `Support/AuditLogSearch` finds none of its
  values anywhere in the log (ADR-0041);
- the audit log's search itself: it finds a value in a column, in the details and escaped, ignoring case, and never
  inside a key;
- consent wordings (`ConsentTextRepositoryTests`): stored once under the hash of their culture and text, first seen
  when first stored, and a writer at the same moment answered by the stored wording;
- daily statistics (`DailyStatRepositoryTests`): counts by day, metric and dimension, 400 increments and 100 batches
  at once from two repositories while fields are added to the form, a day's first counts at once, a form that does
  not exist (also with values of it left), counts racing their form's delete, the caller's unit, a form's delete and
  the purge a form and a day at a time (ADR-0041). SQLite runs one write at a time, so only the SQL Server run in CI
  shows that the parallel ones never deadlock;
- the trash and the delete (`TrashRepositoryTests`, ADR-0047): the forms of a folder's tree going to the trash with
  its key and coming back with it, a form held in the trash at its version, the entries of a form lowest number
  first with their files, the uploads that wait for an entry, a batch's unsent mail, and a form's references.

`Tests.Integration/Files/PhysicalFileStoreTests` runs the file store as a site gets it, in a folder under
`%TEMP%\jfb-files-*`: a file per id in its shard, read asynchronously from the stream, a delete during a download,
and saves that are cut off, cancelled, repeated or made while another save of the id writes, none of which leaves
anything behind or replaces a file, not even one that appears while the save writes. A partial file or a deleted
file's bytes that cannot be deleted are logged without their path and left for retention (ADR-0006, ADR-0041).

The unit tests (`Tests.Unit/Persistence/`) cover what a database cannot be made to do on cue:

- a commit that fails on its way, a lost connection, and the waits between attempts;
- the arguments each repository refuses, and how the outbox cuts and stores errors and attachments;
- the one trash check, which the services and the repositories share, with the refusal of a restore whose landing
  changed (`LibraryTrashTests`), and where forms and folders go and land (`Tests.Unit/Forms/LibraryFoldersTests`);
- every update of a row that keeps who changed it last requires that user, in its record and in every repository
  method (`RepositoryUserTests`);
- the file store's root (`Tests.Unit/Files/FileStoreRootTests`), with a site whose files would lie in its web root
  failing its start-up validation, a store of the site's own, registered before the package or after it, in the
  default's place, device paths and streams refused, and real junctions or symbolic links followed into, around and
  out of the web root (`FileStoreRootLinkTests`, in `%TEMP%\jfb-links-*`); and audit details that take no text, down
  to every method and field of the type (`Tests.Unit/Audit/AuditDetailsTests`);
- the default folder on more than one server (`Tests.Unit/Files/FileStoreLoadBalancingCheckTests`): an error naming
  the setting for a subscriber or beside other active servers, none for a single server (the elected publisher of
  one included, without reading the registrations), a root the host set however it is written, a store of the site's
  own or an Umbraco that does not run, and registrations that cannot be read only as a warning;
- the entry text key against the rule vectors of `tests/vectors`: two texts share a key exactly when an `equals`
  vector says they match (`EntryTextKeyTests`);
- the row each storage kind stores a value as (`EntryValueColumnsTests`): numbers exact to 15 digits and text only
  past them, dates as their wall-clock values in UTC and text only before 1753, a file as the entry's file, a value with
  parts as its canonical object keyed by its parts' texts, and every value not in its kind's form refused;
- the query builder's statements and refusals (`EntryQuerySqlTests`), and the time budget of a read: a timeout that
  never runs again, a caller's cancellation, a read inside another (`FormBuilderDatabaseTests`);
- units inside units (`FormBuilderDatabaseTests`): an inner unit keeps the outer unit's cancellation and deadline and
  adds its own caller's, and a write inside a read is refused; the migration state read again after a busy read
  (`FormBuilderMigrationStateTests`);
- statement text on SQL Server, with names from `TestSqlNames`: the due list of action runs reads without lock hints
  and the claim with them (`ActionRunSqlTests`); and the numbering of a statement's arguments (`PersistenceHelperTests`);
- the product rows against the migration's snapshots.

`Architecture/CoreDatabaseConnectionTests` reads Core's IL (through `IlReader`) and fails on any connection of Core's
own (ADR-0036). `Architecture/StoredEnumTests` reads the IL and the source of Core's persistence and fails on an enum
parsed, turned into text, boxed or named with `nameof` anywhere but `StoredNames` and its table of names (ADR-0033,
4.8g); it reads local functions and lambdas too, and leaves out only what the compiler writes for a record (4.8i).
`Architecture/StaticDataTests` reads every static field of Core and Web and fails on one that is not read-only or that
holds anything but what cannot change, all the way down: values, frozen and immutable collections, read-only
serializer options and contexts, reflection's members, and the package's own types with read-only fields (ROADMAP
4.8f, 4.8h). A lazy value not made yet is judged by what its type can hold, without making it (4.8i). A test of its
own samples shows it finds a concurrent dictionary, options that can change, a list behind a read-only view and a
lazy list.
`Architecture/QueryBuilderReadsTests` reads the query builder's source and fails on any database call but its one
budgeted read, or on SQL that changes rows (ADR-0039).
`Architecture/SqlGuardTests` reads the repositories' source and fails on a guard without a locking read (ADR-0038):
the hints are empty on SQLite, so no database test here can tell. The few checks that read without a lock on purpose
are listed there, each with its reason.
`Architecture/UmbracoFloorTests` fails when the unit tests run on an Umbraco before 17.6.0, whose runtime cache can
keep a value it was told to clear (ADR-0074): NuGet resolves the packages' range to its lowest version, so a range
lowered below the floor shows here.

## The settings tests

`Tests.Unit/Settings/` covers the backoffice sections without a host (ADR-0043): a section's JSON (camelCase, enums
as text, nulls left out, unknown properties dropped), each migration step and a missing or doubled one, how a site row
combines with the global row, the changed field paths, the host rules of every kind and a save's lock check, every
validator and default (`general`'s time zone: IANA ids pass; Windows names, offsets, untrimmed text and another letter
case do not, even once the exact spelling is cached; the consent default's texts, refused as a definition refuses them),
the cache keys and what a refresher instruction clears. `SiteBaseUrlTests` covers the base URL (ADR-0067): the
setting first and asked for the site, then the root's `https` domains in their order (wildcards and `http` skipped),
the nearest domain below a root without one and none of another site, then Umbraco's application URL as its origin,
none otherwise; without a site only the global setting and the application URL; the origins the setting takes and the
ones a domain gives; and the setting's check, which stores an origin as its origin, white space as the default and
refuses anything after the host and port.
`Testing/Settings/SampleSettings` is a section with every kind of field (texts, a number, an enum, a nested object, a
list, a `[GlobalOnly]` field, one stored under a name of its own) and what is no field (a worked-out value, an
indexer), two migrations, a validator that normalises and checks across fields, and a host rule whose locks a test
sets.

`Tests.Integration/Settings/` runs the settings service on a real site, with a fresh database per test: saves
globally and per site with their versions, stale saves, audit entries and notifications (a failing handler too); old
rows migrated and rewritten; unreadable rows; host locks of every kind and a whole locked section
(`LockedSectionTests`); a save refused inside a caller's unit of work or an Umbraco scope; and the cache with its
loading token and the refresher, also as the JSON instruction other servers get. `Tests.Unit/RuntimeCacheLoadsTests`
covers the load that the settings and the access snapshot share: misses that come together read once, a change
during the load or as it ends keeps it from being kept, a failed load (also one that throws before its first await)
is cleared, a caller that stops waiting leaves the load to the others, and the load runs outside its first caller's
unit, cancellation and async-local state (4.8g). Umbraco's test host runs without
caches (`NoAppCache`), so `SettingsServiceTests` gives its site a runtime cache, as a real site has, and a server
messenger that records the instructions a save sends instead of delivering them (`RecordingServerMessenger`).

## The form service tests

`Tests.Unit/Forms/` covers the parts of the form service without a host (ADR-0044): aliases from names (accents,
spelled-out letters, camelCase, a leading digit, names without Latin letters, the length) and the lowest free number
in any letter case, the alias cut so its number keeps it within the longest alias; the field catalogue of a definition
and what a publish adds, updates, brings back and ends, with the storage kind of every built-in type written out apart
from the code; the code of every refusal and conflict; the notifications' arguments (also of the folder service,
`FolderNotificationTests`) and the structural publish check; the name rule of forms and folders (`LibraryNamesTests`);
and the built-in templates (`BuiltInFormTemplatesTests`: one per file, each reading with its key, publishing and
having English and German texts) with the package version an export names. `FormImportListsTests` plans the option
lists of an import (ADR-0078) over the library in memory: a list the file carries for a user who may manage the
library, with its key, name, kind and contents; the first free alias past the site's aliases in any letter case and
those the same import takes, one from the name for an alias that is none, cut for its number; a URL list on another
host, a value given twice and a kind the site lacks left missing and logged by code and path, never the configuration,
an item or the file's kind; a user who may not, a key that names no user and no user creating none, and nobody looked
up while nothing is to be created; the lists written and forgotten by every server only once committed, and a list
taken since the plan refused as taken.

`Tests.Integration/Forms/` runs the service on a real site, with a fresh database per test: a create from blank with
and without the consent default (and with a stored default a definition would refuse), aliases and their races,
folders outside the trash, draft saves that change something or nothing, stale changes and a change between the
checks and the write, publishes with the catalogue, the theme and the public revision, every publish check at once,
settings that break their type's rules refused at their paths and published once put right, unpublish, rename and move, the trash, cancelling and failing handlers, the order of the notifications, the audit
entries, and a change refused inside a unit of work or an Umbraco scope. `FormNotificationRecorder` records every
notification and whether a unit was open, and cancels, fails or changes the form when a test says so;
`TestPublishCheck` is a second publish check whose problems a test sets. `FormServiceLimitTests` lowers the pages a
definition may have, so a form saved before still opens and its save returns `limit_exceeded`, as do a restore of a
version above the limit, a duplicate of it, copied actions into it, a saved template and an import above it.
`FormImportListTests` imports files that carry option lists (ADR-0078) for the personas of ADR-0013: a library
manager gets a manual list, one whose alias another list has (numbered) and a URL list on a host the site calls, with
their keys and contents, offered at once, counted in the audit entry and used by key on a second import; an entry
handler, a key that names no user and no user get the form and `option_list_missing`, and no list; a URL list on a
host the site does not call and an uploaded list with a value twice are reported and not created; and the lists are
written with the form or not at all: none for a cancelled import, the rooms list rolled back and created again when
another change takes the meals list's key at the same moment, and the import refused with no form and no list after
three such races; and an import whose unit the database runs again after a lock conflict, another server taking the
form's alias in that second attempt, takes the next free alias with one form and one list.
`FieldSettingLockServiceTests` gives the site two designer locks of the text type: a form saved before them runs and
is checked at publish with the locked values while its store keeps its own, a draft save refuses another value of a
locked setting (a hidden one too) and takes the lock's value or the one the draft held, the published form is none for
a missing, an unpublished and a trashed form, and the site resolves the locks and checks them when it starts.

`Tests.Integration/FormBuilderChangesTests` runs the change pipeline every service of Core shares (ADR-0053) on a real
site: a change refused inside a unit of work or a scope with its service's reason, and by the pipeline itself when it
skipped that check, writing nothing; a write that commits with its audit
entries in the order given (none for a write that found nothing), a write whose entry cannot be made leaving nothing,
a cancelling handler's messages, and a handler that fails after the commit logged as the service's with what changed.
The three parts of the form service (the lifecycle with the trash, `IFormBuilderFormVersionService` and
`IFormBuilderFormExchangeService`) are tested below, each with what it does.

`FormCopyServiceTests` covers copies, templates, imports and exports (ADR-0046) on a form designed with every kind of
reference: a duplicate whose rules, checks, verification field and action settings name its own elements, with and
without actions; copied actions mapped by alias with the unmapped ones reported and refused at publish, and a copied subject's merge
tag that names a field the target lacks in the report with its path; forms from the
Contact template and from saved templates, with the consent default; an export of the draft or a version with its
option lists that imports as a new form, a theme the site lacks, option lists by key, by alias or missing; and every
refusal, notification and audit entry (an import's notes named once per code, with their counts), a reimport of the
longest alias numbered within it, and each operation refused inside a unit of work or a scope.

`FormVersionServiceTests` covers a form's versions (ADR-0045) on a history of six versions: the History of publishes
or of every version with what changed into each, paged, one version, two compared either way, a restore that creates a
new version (or writes nothing when the draft holds it already) with its notifications and audit entry, pins and
comments that leave the form's row alone, and every refusal. `Tests.Integration/Retention/` runs the draft-version
cleanup by the history settings, with the versions it must keep, its rounds, its deadline and its audit entries, and the
retention job that runs it; `Tests.Unit/Retention/` covers the job's own rules without a host.

`FormTrashServiceTests` covers the trash and the permanent delete of a form (ADR-0047): a trash and a restore into its
folder or, when that is in the trash, the root, refused with `landing_changed` when that changed after the check (the
repositories refuse it too); stale changes; a delete that removes the entries with their values, files, events and
notes, the versions, catalogue, grants, references and daily statistics, unsent mail and the bytes of the files (with
`Support/MemoryFileStore` as the site's file store), keeping sent mail and other forms; a delete recorded as begun
(`form.deleting`) with its first batch that removes something, one that stops after its first batch and finishes when
run again, bytes that cannot be deleted, a late entry and entries that keep arriving until `in_use`.
`FolderServiceTests` covers the folder service on the same rules: create, rename and move with every refusal, a trash
and a restore that take exactly what went (a folder and a form trashed before stay), a subfolder restored on its own,
a restore refused when where it lands changed after the check, the delete of a folder's tree, emptying the trash, and
that none of these publishes notifications of the forms it takes.

## The entry tests

`Tests.Unit/Entries/` covers how an entry's answers are stored and read back (ADR-0070) on `EntryTestForm`, a form with
a field of every built-in type that holds a value, its catalogue as a publish stores it, and answers read by the types
as the pipeline reads them. `EntryValueWriterTests` gives the rows of every type at its edges: the longest texts,
numbers exact to 15 digits and text only past them, dates where both databases keep them and text only outside,
choices in their order with the "Other" text in its place, a ranking in rank order with its gaps closed, a grid's
columns at its rows, values of parts, files with their uploads, a consent's hash with its wording once, and a
repeatable group's rows; and every refusal, by name and without a value. `EntryValueReaderTests` reads them back typed
from their text (long numbers, dates before 1753, times, a grid by its rows, a group by row, a file whose row is gone,
a consent without its stored wording), a text its kind cannot read and a type the site lost by storage alone, and the
answers merge tags show. `ConsentWordingsTests` covers the wording: the culture's text with its fallback, a dictionary
item, merge tags rendered as consent texts render them, the result sanitised and made well formed before it is hashed,
and none for a field that shows no text or nothing safe.

`SubmissionReaderTests` reads a whole submission (ADR-0072) of a form with a field of each kind the step treats on its
own: a repeatable group's rows counted with too few and too many (a thousand rows over the limit neither read nor
checked) and an empty row that counts but is not stored, a child's errors and its `matchesField` check in its own
row, a calculation's posted value replaced by the server's (a row's too), a hidden field's member value resolved on
submit whatever was posted (and absent from the public projection) with the action whose rule looks at it, one too
long for its type left out and logged by alias, a query prefill and its default, what the rules hide (a field, a
group's rows) neither checked nor stored, required and own checks, a form-level validation rule's message, files by
their upload tokens, parts cleaned, and keys, types and time zones the server does not know.
`SubmissionReaderPostTests` reads what a client can post but the browser's runtime never does (ADR-0080): one upload
named in two rows (stored at the first, `invalid` at the later) and twice in one answer, by one token and by two, an
upload an entry holds `invalid`, a token two fields name `invalid` at the field whose upload it is not and kept by the
other, a ranking's part names cleaned where its options are unknown, one longer than an option value refused before
it is read and two that clean to one, a query value too long for its hidden field, or one of two, counting as none
(its default stored, logged by alias as a value its type cannot take, never the value), in a repeatable group's row
too, an answer that fails after one of its tokens stood taking nothing so a later value gets the upload, a query value
a site's server-side number cannot read or whose check it fails counting as none, with or without a default, the
action's rule seeing what was taken, a default it cannot read, that fails its check or is longer than it takes not
stored, and a required one given no value its type takes `required`, and a checkbox's `false` and `no` read as
unticked.
`SubmissionParentAnswersTests` shows the parent answers a cascading field's source sees (ADR-0081): a text the parent
lacks reaching neither the source nor a library list's cache key, the parent's answers in their order and a repeated
one refused, a parent whose options are unknown leaving its child's unknown (each logged once, never the answer), and a
parent from a source passing only the options it resolved for its own parent, and a parent in a repeatable group
giving each row's child the options for that row's answer, asked once for each answer.
`SubmissionOptionsTests` holds a submission's choices to the options they offer (ADR-0077): a choice from a library
list refusing what the list lacks and storing what it has, a cascading choice taking its parent's answer's options and
those of every answer, a cascading code source asked in the submission's culture and page for the parent's answers, a
repeatable group's child held to the parent in its own row, a source asked once per field and parent answers and not
at all for a choice without an answer or hidden by the rules, and a list the site lacks or a source that throws taking
the answer as given, logged by the field's alias without it.
`SubmissionHiddenDefaultsTests` renders a submission's hidden fields' defaults over one scope (ADR-0083): a later
default showing an earlier one's value, and a cascading source asked once to check its field's answer and once for
the label two defaults show; a label tag sending the source only the parent's answers the parent offers, never a
posted text; a parent of a site's type that keeps its value on the server, resolved between two defaults, giving the
later one the labels for its value; labels in a repeatable group's rows asked for each row's parent answer, a row's
posted text never among them, so a row whose parent is no option shows its value whatever the other rows answered; a
value the server resolves checked as it is resolved and not again once a later value changes its options, and a
default not among its options not stored; and only the answers made to be replaced changing, their hidden fields kept.

`Tests.Integration/Entries/EntryAnswerStoreTests` publishes a form with every type through the form service, stores
an entry with uploads and a consent wording in SQLite, and reads every type back typed, each value as its type read it;
a wording is stored once for many entries, and an entry refused for an upload another entry took stores nothing.

## The access tests

`Tests.Unit/Access/` covers the permission model without a host (ADR-0013, ADR-0048): the flags' names in any letter
case, what each needs and where each may be granted (`PermissionRulesTests`); how a user is read, the administrators by
Umbraco's alias and the Forms section matched exactly (`AccessUserTests`); and the evaluation on a tree with restricted
folders, a ring and unknown folders, globally, per folder and per form, the lists' scope and visibility, and what a move
gives each group (`AccessSnapshotTests`). `Tests.Unit/Persistence/AccessSqlTests` covers the lists' clause and its
limit of groups.

`Tests.Integration/Access/` runs the access service on a real site, with a fresh database per test. `AccessPersonas`
creates the personas of ADR-0013, one group and one user each, with their global grants, and `AccessListTests` lists
forms and folders as each of them over a library with a restricted folder for a separate group, a restricted form and
a form in the trash, paged and counted. `AccessServiceTests` changes grants and restrictions with their audit entries,
refusals and stale versions, deletes a user group through Umbraco, and shows that the cached evaluation follows every
change of access and of the tree, while a folder's rename keeps it, and that a change, a deleted group's grants
included, is refused inside a caller's unit of work: it gives the site a runtime cache, the recording server
messenger, and `HookedPermissionRepository`, which lets another server change a grant while the snapshot loads or a
change writes. `AccessLockTests` locks the section, and `HostGrantsBootTests` boots a site whose host sets the global
grants.

The authorization policies ([ADR-0049](../docs/decisions/0049-authorization-policies.md)) are tested twice.
`Tests.Unit/ManagementApi/FormBuilderAuthorizationTests` covers their names, scheme and requirements, the resources
and denials, and both handlers against a substitute access service: the section first, then what is held, and a
folder or form the user cannot see answering as not found. `Tests.Integration/Access/AccessPolicyTests` signs each
persona in through Umbraco as an API user with a client secret (`Support/BackOfficeApiUsers`) and has
`AccessProbeController`, a controller of the test assembly, authorize every policy globally, at the root, on folders
(the restricted one, and one inside it that another group holds on its own) and on forms, against a table written
out by hand; it also calls actions guarded by `[Authorize(Policy = ...)]` without a token and as personas with and
without the section or the permission. `Testing/TestPeopleTests` seeds the grants and restricted folders, again,
next to a folder of that name in the trash or inside another, for one of Umbraco's groups alone and with broken
settings, and `Testing/TestAccessEndpointTests` reads them back through `/jfb-testing/access`.

## The management API tests

`Tests.Integration/ManagementApi/` calls the management API on a real site as the personas of ADR-0013, each an API
user signed in through Umbraco's token endpoint on the first call (`ManagementApiTestBase`, `ManagementApiCaller`)
([ADR-0050](../docs/decisions/0050-management-api-base.md)). `ManagementApiEndpoints` lists every endpoint through
the API explorer and fills its route, query and body by type, so the permission check, not routing, answers; the
tests that use it call a new endpoint by themselves:

| Tests | What they show |
| --- | --- |
| `PermissionMatrixTests` | Each endpoint's declaration against a table written out by hand; each endpoint as the outsider (`403 no_section`), as a persona who sees the folder or form but lacks the permission (`403 forbidden` naming the first missing one), as one who holds nothing on the restricted folder or its form and with a key that names nothing (`404`), and the section-wide ones as a user of the section without grants. Each test writes the endpoints it called, with their answers, to its output. |
| `PendingMigrationsApiTests` | Every endpoint answers `503 migrations_pending` with `Retry-After` while the plan has not run (the site boots without it), and normally once it ran; the test writes each answer to its output. |
| `RefusalBodiesApiTests` | Each kind of refusal through Umbraco's pipeline, from a real call: `application/problem+json`, the status in the body, the code, a title for editors and what the kind adds (the problems, the permission, a handler's messages). |
| `TreeApiTests` | The tree and the trash as each persona sees them, folders first, with the user's permissions and `hasChildren` (only for what the user sees, in the folder's own state of the trash); paging across folders and forms, at the root and in the trash too; a folder in the trash holding what went with it; no key as the root; pages outside the limits. |
| `FolderApiTests` | Each folder endpoint's answer and change: the permission checked before the request's values, `design` on a move's new parent, `managePermissions` and a confirmation for a move that widens access, `design` and `deleteEntries` on everything a permanent delete removes (restricted folders and forms below it included), an emptied trash that keeps what the user may not delete, and a restore to the root (its parent in the trash) that needs what a move there needs. |
| `FormApiTests` | Each form endpoint's answer and change: a search of what the user sees by name or alias, the form for anyone who sees it and the draft's document for `design`, a save of the document with its version (or none when nothing changed), problems with paths into the document and `409` for a stale version, `manageActions` for a save that changes the actions, publish and unpublish, rename, a move's new folder and its widened access, a restore to the root that needs what a move there needs, and a delete for good with `deleteEntries`. |
| `VersionApiTests` | The History of a form with four versions: the publishes or every version, newest first, with what changed and paged; a version with its document; a comparison with each property before and after; a restore into a new draft that needs `manageActions` when it changes the actions, after the concurrency check; pins and comments that leave the form's concurrency version alone; `404 version_not_found`. |
| `RestoreLandingApiTests` | A form's and a folder's restore whose folder comes back from the trash after the endpoint checked where it lands and before the write (`RestoreActionHook`, an action filter after every other, arms `HookedFolderRepository` to bring it back right after the action read it): `409 landing_changed`, and the form or folder stays in the trash where it was. |
| `OpenApiSnapshotTests` | The document `jfb` against `docs/api/jfb.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), its paths and operation names, and schemas that are only its own models, never a framework type described by its properties. |
| `FieldTypeApiTests` | The field types for the designer ([ADR-0057](../docs/decisions/0057-field-types-endpoint-and-availability.md)) against `docs/api/field-types.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), served to a user of the section without grants; every type in the registry's order with the palette's and the inspector's groups, its declaration in camel case, its common properties and messages, its settings in the inspector's order with their kinds, choices and labels, nested settings, defaults and rules, and its availability. |
| `FieldSettingLockApiTests` | The host's designer locks through the API ([ADR-0060](../docs/decisions/0060-designer-locks-of-field-settings.md)): the field types mark a locked setting with its value and key and a hidden one without its value, which never leaves the server; a draft save with another value answers `409 setting_locked` with the setting's path and the lock's key, and the lock's own value saves. |

`Tests.Unit/ManagementApi/` covers the same without a host: the access filter's order, refusals and fail-closed
cases, how it reads a key (`FormBuilderAccessFilterTests`); every answer to a refusal, the exception filter, the
declarations, the operation and schema names and the feature provider (`FormBuilderProblemsTests`); the models of the library
(`LibraryModelsTests`), of the versions, with the stable names of their kinds (`VersionModelsTests`), and of the field
types, with their names in camel case, an availability for every type and the lock of every locked setting, a hidden
one without its value (`FieldTypeModelsTests`); and the
policies and handlers (`FormBuilderAuthorizationTests`). In the client,
`packages/backoffice/scripts/generate-client.test.mjs` generates the client from the snapshot next to the committed
one and fails on any difference: after a change to the API, run the snapshot test with `JFB_UPDATE_SNAPSHOTS=1`, then
`npm run generate-client -w @jfb/backoffice`, and commit both. `packages/backoffice/src/hey-api.test.ts` shows that
the generated client starts from the backoffice's own HTTP client, whose settings win. `packages/backoffice/src/lang/lang.test.ts`
holds the English and German dictionaries to the same keys, each a text, to every `#` key of `docs/api/field-types.json`,
and the field types' areas to no key nothing serves; `bundle.manifests.test.ts` shows a `localization` manifest for
each of the two languages loading its dictionary.

## The public API tests

`Tests.Integration/PublicApi/` calls the public API anonymously on a real site that boots its runtime, with forms
published through the form service (`PublicApiTestBase`) ([ADR-0084](../docs/decisions/0084-the-public-apis-frame-and-the-form-endpoint.md)).
The base puts `WatchedForms` in front of the site's form service: it passes every call on and lets a test count the
reads of published forms, make them fail, or hold them at a `ReadGate` until the test opens it. `PublicApiEndpoints`
lists every endpoint through the API explorer and fills its route, so the tests that use it call a new endpoint by
themselves:

| Tests | What they show |
| --- | --- |
| `PublicApiFrameTests` | What every request meets: CORS for an allowed origin without credentials, nothing readable and `403 origin_denied` for a post or a preflight from another, the site's own pages and requests without an origin passing, `no-store` and `Vary: Origin`; `404 not_found` for an address that names no endpoint, whatever its method; `413 payload_too_large` with the connection closed for a body to an endpoint that takes none; `500 server_error` for a failing read, logged with the endpoint's name and none of the request's values. |
| `PublicApiBasePathTests` | A base path of the host's own: every endpoint and the catch-all move there, the frame matches it in any letter case, and every route starts with it. |
| `PublicApiPendingMigrationsTests` | Every endpoint, and an address that names none, answers `503 migrations_pending` with `Retry-After: 10` while the plan has not run, and normally once it ran. |
| `PublicApiTimeLimitTests` | The time limit on the site's clock (`ManualTimers`): the request answers `504 timeout` once the test fires it, the read it waited for goes on, and the next request gets its answer from that one read. |
| `PublicFormApiTests` | The form endpoint: a headless form by its key and its alias in any letter case; the same `404 form_not_found` for a form that is missing, unpublished, in the trash or not served to headless sites; the texts in the culture with the dictionary's items and rich text sanitised; an `ETag` that follows the answer, and `304` for a client that holds it; `open` from the opening time until the closing time. |
| `PublicFormCacheTests` | The published forms each server keeps: every change of the form service drops the form and tells the other servers (`RecordingServerMessenger`), a folder's trash or restore drops them all and its delete each form it removes, another server's instruction drops it here and so does a refresh of everything, misses that come together share one read, nothing is kept for a key or alias that names no form, and a published version this server cannot read is answered as missing, logged once until it changes (`LogCapture`). |
| `PublicFormSiteTests` | What the answer takes from the site: the time zone of the page's site or the global one, UTC with one warning for a stored zone this server cannot resolve, and of every settings section filled in full only the time zone (a guard fails for a section or field the test does not fill). |
| `PublicFormLockTests` | A designer lock added after the publish: the locked value is served under another `ETag` than the answer without the lock. |
| `PublicFormRecallTests` | Answer recall from the answer alone (ADR-0085): the texts that render, with the server's renderings for fixed answers on a page of a site, written with the answer to the reviewed fixture `fixtures/public-api/recall.json` (keys numbered in order, the culture's formats left out of the comparison; rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), which `packages/logic/test/recall-endpoint.test.ts` renders with `recallAnswers` to the same texts; the texts left out render as written; the recall form holds the owners, the recalled fields, a repeatable group whole and a field after the text, and names no field as sensitive. |
| `PublicFormConsentTests` | A consent's wording from a dictionary item, rendered and sanitised, with the hash of what it shows, and another hash once the item changes. |
| `PublicFormOptionsTests` | The options endpoint (ADR-0086) with sources of the site's own that note what they are asked for: a field's own options and a source's in the culture, a missing label as the value, a description and an image only where an option has them, the page as the request names it, a repeatable group's field; a parent answer the parent lacks giving the options of no answer under the same `ETag`, the source never seeing it, an answer cleaned and given once; three levels each held to its parent's held answers, every level by its key, a field outside the chain not read; a failing source and a field it filters `available: false`; `no-cache` and `304` for a weakly held tag; `404 form_not_found` as the form endpoint answers it but for a form not served headless, `404 not_found` for a field without options, `400 invalid_request` for a culture, a page or a `parent.` name it cannot read. |
| `PublicOpenApiSnapshotTests` | The document `jfb-public` against `docs/api/jfb-public.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), its paths and operation names, its own schemas only, and for every operation and action the rate-limit policy, body limit and time limit with the frame's refusals. |

`Tests.Unit/PublicApi/` covers the frame without a host: the middleware only below the base path, the headers every
answer carries, the CORS answer for each kind of origin and method and the answers to what ends a request early
(`FormBuilderPublicApiMiddlewareTests`); the filter with the body limit before a byte is read, the time limit, the
pending migrations and the declarations of an action without any (`FormBuilderPublicApiFilterTests`). Beside them,
`Configuration/PublicApiRulesTests` holds the base paths a host may choose, the origins as browsers write them and the
start-up check's messages; `Definitions/EditorHtmlTests` the one sanitiser; `Definitions/VisitorFormTests` the
visitor's form made from the full fixture, every text of it a string and nothing else changed;
`Forms/VisitorFormsTests` the culture, the dictionary read once per answer, a lone surrogate replaced, each consent's
wording with its hash (none for one that renders as nothing), and `mergeTags` null when no text renders or with its
values, recall form, words and formats when one does; and
`Settings/SiteTimeZonesTests` the zone per site with the warning at most once an hour per site and zone, within its
bound.

## Writing tests

- Name C# tests `Method_Situation_Expected` and assert with NUnit's constraint model.
- No sleeps to wait for state: poll with a deadline, as `TestMailbox.WaitForAsync` does, and use the fake clocks where
  time matters.
- Generate passwords with `TestPeople.NewPassword()`; test values that look like secrets carry the marker `JFB_FAKE`.
- Integration fixtures set their site's configuration on `InMemoryConfiguration`: in the constructor of an
  `UmbracoIntegrationTest`, before `base.Setup()` in an `UmbracoTestServerTestBase`. Never set a key on
  `builder.Config`: the test host chains one configuration into every fixture's site, so the key would reach every
  fixture that runs later, and `Support/SharedConfigurationTests` fails on it. Capture logs with
  `builder.Services.AddLogCapture()`.
- A test server fixture that calls the Management API derives from `Support/FormBuilderTestServerTestBase` and boots
  the runtime (`[UmbracoTest(Boot = true)]`): it runs Jiron Form Builder's migration plan after the host starts, and
  its `ConfigureHost` sets the configuration before the host is built.
- Read files of the repository in place through `RepositoryRoot.Path(...)`.
