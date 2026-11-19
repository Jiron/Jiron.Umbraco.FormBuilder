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
| `Jiron.Umbraco.FormBuilder.Tests.Unit` | NUnit 3 and NSubstitute, no Umbraco host: the architecture tests (Core knows nothing about HTTP), the vector runner and the tables check for Core's shared logic (`Logic/`), the definition model and its JSON (`Definitions/`, below), the field types (`FieldTypes/`, below), the merge-tag service (`MergeTags/`, below), the host configuration with its start-up checks and IntelliSense schema (`Configuration/`, below), the backoffice settings (`Settings/`, below), the form service's parts (`Forms/`, below), the permission model (`Access/`, below), the management API's policies, access filter and answers (`ManagementApi/`, below), the public API's middleware and filter (`PublicApi/`, below), the website assets, `<jfb-assets>` and the parts of the form rendering (`Rendering/`, below), and the Testing project's own tests. |
| `Jiron.Umbraco.FormBuilder.Tests.Integration` | A real Umbraco (`Umbraco.Cms.Tests.Integration` 17.x) with Jiron Form Builder's composers: the boot test (each service of Core resolved, one per site, and each notification handler registered), the people factories against real services, the test-only endpoints on a real site, the schema and its migrations, the repositories (`Persistence/`, below), the settings service (`Settings/`, below), a site's own field types (`FieldTypes/`, below) and merge-tag sources (`MergeTags/`, below), the form services and the change pipeline they share (`Forms/`, below), the access service with the authorization policies (`Access/`, below), the management API (`ManagementApi/`, below), the public API (`PublicApi/`, below), and the website assets and the forms rendered on real pages (`Rendering/`, below). |
| `Jiron.Umbraco.FormBuilder.Testing` | Test-only code for the tests and the test hosts, never packed. |
| `Jiron.Umbraco.FormBuilder.Tests.AI` | The AI add-on on a real Umbraco with Umbraco.AI and the fake provider ([ADR-0018](../docs/decisions/0018-the-ai-add-on.md)): the spike (a description drafted into a form through Umbraco.AI's chat), Draft, Translate and Suggest through the management API with their refusals and permissions, Settings › AI and its audit, the health check, the OpenAPI snapshot `docs/api/jfb-ai.json`, the add-on's shape (its public surface, its audit words, its static fields), and the privacy test: entries with distinctive answers, none of them in any prompt. It links the integration project's harness; each test gets an Umbraco.AI SQLite file of its own. |
| `Jiron.Umbraco.FormBuilder.Testing.AI` | The fake Umbraco.AI provider (`jfb-fake`, ported from Tandem) and the set-up of its connection and chat profile, never packed. A host offers it only when `JironFormBuilderTesting:FakeAI` says so. |
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
`messages` (6.2g) holds the visitor messages ([ADR-0099](../docs/decisions/0099-visitor-error-messages.md)): a
case's field as the form endpoint shows it, the error's kind, the answers, the culture and the site's today,
expecting the text in English and German, and the error summary's entries after a label that ends in its own mark
or is made only of spaces. `reading` (6.4a) holds the reading stage
([ADR-0109](../docs/decisions/0109-reading-answers-in-the-browser.md)): a `read` case gives a field of a built-in
type with its settings and grid rows and an answer as posted (its texts and named parts), expecting the limit it
breaks or the values read (each a text, the row it answers and the text stored in its place) and whether it is
unreadable; a `rowCount` case gives a repeatable group's settings and its number of rows. `Logic/ReadingVector.cs`
runs both through Core's `SubmissionReader.Clean` and `FieldValues`. Beside it, `packages/logic/test/reading.test.ts`
holds the browser's table of the built-in types to `docs/api/field-types.json` and reads no site's own type, and
`FieldTypes/CountryCodesTests.cs` holds the browser's country codes (`countries.ts`, written as their letters two by
two) to Core's. `posts` (6.4b)
holds the reading of a page's post by its names ([ADR-0110](../docs/decisions/0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md)): a `post`
case gives the form's fields as the page names them (alias, type, settings, a repeatable group's children) and the
names and values in the order a browser posts them, expecting each field's answer by its alias (texts, and parts
in the order first posted), each group's rows, and the context and query values. `Logic/PostVector.cs` runs it
through the no-JS post's `FormPostReader`, `packages/logic/test/vectors.test.ts` through `readPost`, and
`packages/logic/test/posts.test.ts` holds what only the browser decides: a site's own type under a built-in alias,
and the most rows a group takes held to the served field types, and the query values' limits held to Core's
`FormSubmissions`. The form engine's model of a form (6.4c, [ADR-0111](../docs/decisions/0111-rules-on-the-page.md))
is held by a fixture rather than vectors, since it is made from a whole form: `Forms/LogicModelFixtureTests.cs`
writes `tests/fixtures/definitions/logic.json` (the form endpoint's answer and Core's `DefinitionLogic` model of the
full definition fixture and each demo form; `JFB_UPDATE_SNAPSHOTS=1` writes it and fails), and
`packages/logic/test/projection.test.ts` makes the same model from each answer with `logicFormOf`; the designer's move
check (9.3a, [ADR-0167](../docs/decisions/0167-the-form-workspace-and-the-editor-model.md)) runs the engine's own
ordering, and `packages/logic/test/cycles.test.ts` holds `hasCycle` to what `evaluateForm` refuses. The checks and
messages on the page (6.4d, [ADR-0112](../docs/decisions/0112-checks-and-messages-on-the-page.md)) are held to the
server's own pages: `Rendering/ErrorFixtureTests.cs` posts a form of every check the browser makes, in the default
theme and, with asterisks, in `bootstrap5`, and a form of two pages, as a browser without JavaScript does, in English
and in German, and writes each page the server renders again with its errors, with the page as it was first
rendered, to `tests/fixtures/rendering/errors-en.json` and `errors-de.json` (`JFB_UPDATE_SNAPSHOTS=1` writes them
and fails); `packages/runtime/src/checks.fixture.test.ts` checks each page as the runtime does and expects the
server's errors under the fields and the links of its summary, in its order, and their display (6.4e,
[ADR-0113](../docs/decisions/0113-errors-on-the-page.md)): taken away as the runtime takes them away, the page has
the first render's `aria-invalid` and `aria-describedby`; shown again as the runtime shows them, it is the
server's page, element for element. The calculations on the page (6.4f,
[ADR-0114](../docs/decisions/0114-calculations-on-the-page.md)) are held so too: `Rendering/CalculationFixtureTests.cs`
posts a form of calculations of every format and two sliders, in English and in German, and writes each page first
rendered and rendered again to `tests/fixtures/rendering/calculations-en.json` and `calculations-de.json`;
`packages/runtime/src/outputs.fixture.test.ts` blanks every result and slider text and shows them again as the
runtime does: the server's page, element for element. So are the texts that recall answers (6.4g,
[ADR-0115](../docs/decisions/0115-answer-recall-on-the-page.md)): `Rendering/RecallFixtureTests.cs` posts a form
with such a text in every place the views mark, twice, as a visitor without JavaScript answers what the rules show
only once the page comes back, in English and in German, and writes each page first rendered and rendered again to
`tests/fixtures/rendering/recall-en.json` and `recall-de.json`; `packages/runtime/src/recall.fixture.test.ts`
makes every marked text stale and renders them again as the runtime does: the server's page, element for element,
empty texts hidden and a progress step without its title showing the page's number. So is the headless element
(6.6e, [ADR-0128](../docs/decisions/0128-the-element-writes-the-servers-view-model.md)):
`Rendering/ElementFixtureTests.cs` asks the render endpoint and the element endpoint for the same first render of
each form of the step, in English and in German, and writes the answer (its keys fixed, its assets' addresses
without their content's version) with the server's markup to `tests/fixtures/rendering/element-en.json` and
`element-de.json`; `packages/element/src/markup.fixture.test.ts` writes each answer as the element does: the
server's markup, element for element and attribute for attribute, without what only a post without JavaScript uses.

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
| `PublicFormSchemaTests` | `schemas/jfb-public-form.v1.schema.json` is what the projection's records make through the visitor context's metadata (`JFB_UPDATE_SNAPSHOTS=1` writes it), a definition per record and no other property allowed; the full fixture's visitor form in two cultures and a hundred generated ones fit it (ADR-0125). `scripts/tests/generate-public-types.test.mjs` holds `jfb-public-form.v1.d.ts` to the types the schema makes, shows a changed schema changing them and refuses a schema they cannot say; `packages/runtime/src/projection-types.check.ts` fails the client's type check when the published form no longer fits the runtime's and the logic's types; `scripts/tests/pack.test.mjs` reads a package's entries and names a schema the main package lacks, which `scripts/pack.mjs` checks on every pack. |
| `UpgradeTests` | A synthetic older version read through the real reader, and the chain's checks. |
| `DefinitionValueTests` | Localised texts (a null culture refused as documented), default values, settings objects, instants and the string escaping; what the writer cannot write reported as what it is; the reader of what the site stored built once; both readers refusing text and bytes alike; a group nested 30 deep read in about the time of one; a string from outside the definition made well formed, each lone surrogate replaced. |
| `VisitorTextsTests` | The visitor texts of a visitor's form in the form's order with their owners and properties, a repeatable group's children after it; only the public text settings that take merge tags, nested ones by their path; no consent's text. |
| `ModelListTests` | No list of a read definition, its value types or its projection can be changed through a cast or through the list a record was built from, none takes a null item, and records compare their lists by their items, in order. |
| `StructureTests` | A document that publishes, and each publish refusal with its code and JSON path as one change to it (aliases that differ only in letter case and option values that fold to one text included); what publishes; that a save refusal comes first; that each one-per-form type counts on its own; and 500 generated definitions checked without throwing, every path naming a part of them. |
| `FieldTypeSupportTests` | §4.3.2 restated apart from the code, against the field type registry: each built-in type refuses exactly the common properties, checks, rules and messages it does not support, and declares the palette group, storage, rule kind, multiple setting, one per form, server-only default, personal data, settings and public settings written there; the one list of field messages that both checks use holds every message of the record with its own kind. |
| `RedirectUrlTests` | The redirects that could leave the editor's site, and those that stay on it. |
| `ProjectionTests` | 500 generated definitions with a marker in every private place project without it; the projection names only allowlisted properties; `fixtures/definitions/v1-full.public.json` is the reviewed projection of the fixture; which changes move `PublicHash` and which do not. |
| `DefinitionDiffTests` | The comparison of two versions ([ADR-0045](../docs/decisions/0045-versions-comparison-and-draft-cleanup.md)): texts per culture, a type's settings one by one, option items by value and grid rows by key with their order, moves to another parent and the fewest moves within one, rules whose order means nothing, settings groups by name, the order of the changes. Then 100 generated definitions, each changed by `DefinitionMutator` as editors change them: the comparison swapped mirrors it, names each element once, and each changed element's property changes rebuild its newer properties from its older ones. Also when a save changes the actions ([ADR-0051](../docs/decisions/0051-form-endpoints.md)): an action, or a rule of one added, changed or moved in or out, and nothing else. |
| `DefinitionCountsTests` | What a definition holds, as the import's check and the gallery say it (ADR-0166): its pages, its fields with a repeater's children, every rule (of pages, groups, fields, actions and outcomes, and the form-level checks), its outcomes and its actions. |
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
| `FieldTypeRegistryTests` | The collection builder holds the built-in types in palette order; a type appended, replaced or removed (a field of a removed type is `unknown_type` at publish), a custom type's field publishing with only its public settings projected; an alias taken twice named with how to replace the first, and each contradiction of a declaration refused naming the type, a type that keeps its default on the server and stores files and a unique check on yes or no, parts or files among them; the start-up check on a valid site and on two broken ones. |
| `FieldSettingsSchemaTests` | A settings class read into its schema: each property's kind and editor, a choice's values with their labels, defaults as settings store them, the order by group, order and declaration, the attribute's details and JSON names, public settings at the top only; every class or property that cannot be a setting refused with its reason (all reasons at once), the base class naming the type, and the strict JSON that reads settings. |
| `FieldSettingRulesTests` | The rules each validation attribute gives a setting, on lists' items too; the limits of a setting without them; every attribute that does not fit its setting refused with its reason (all at once); and a default that breaks its rules refused, quoting it. |
| `FieldSettingsChecksTests` | A field's settings held to its type's schema: each kind's values and wrong types, every bound, length, count and allowed value at its edge, values twice in a list as they compare, localised texts, required settings at the top and inside objects, each problem's code, path and message; the type's own check through the base class and the interface, its places, and a check that throws or cancels. |
| `FieldTypeAvailabilityTests` | Whether a type can be used: available by default through the interface and the base class, a type's own answer with its reason, and every check that throws, faults, answers null, cancels itself, blocks or takes longer than five seconds reported as not configured with the shared reason and logged, while the request's cancellation still cancels the answer. |
| `BuiltInSettingsTests` | The rules of every built-in type restated apart from the code and held at their edges, each type's own check through a publish, a file field held to the site's limit and blocked extensions (the defaults, and a site's own list in their place), the lists that start with items, and the country codes against .NET's regions. |
| `FieldPartsJsonTests` | A stored value with parts whose text holds a lone surrogate itself, in a text, in a name, or after an escaped high surrogate, giving no parts instead of throwing, as the website's reader gives none; a well-formed pair read (ADR-0083). |
| `FieldAnswersTests` | What a type receives and gives back: a field's texts and parts, with empty, nameless and repeated ones refused; the longest answer at its edges and none, a part's name counting in it when the part has texts; a value with its row and the text it stores; a reading that refuses a row the field lacks and keeps nothing once unreadable; a validation that keeps each error once and refuses kinds the type does not declare. |
| `FieldValuesTests` | The contract as the pipeline runs it, through a type on the interface alone (one text, choices, a `multiple` setting, no value), one with settings on the base class, and the built-in number: the limits with `maxLength` or `invalid`, the reading, and the order of `invalid`, `required` and the type's own errors; a type without limits, with an undeclared error kind, or reading a value its storage kind cannot hold refused by name, without the answer; what a check sees besides the answers (whether the field is required, today, and the file each upload token stands for, one for each value and only for a field that stores files); and the field's own checks after its type's, by index: a pattern on the whole of every value, one outside the safe subset checking nothing, values matching another field's as the field's kind compares them, and none without a readable value; and a number past the 15 digits a stored number keeps refused alone, with `digits` or, for a type without that kind, `invalid`, while text keeps every digit. |
| `PostedNumbersTests` | Numbers as posted (leading zeros, a point first, an exponent) converted exactly to the canonical wire form, those past the 15 digits a stored number keeps read for the check to refuse, and everything else refused: no number, other digits, or past the 60 digits a calculation reads. |
| `FieldTypeChangesTests` | Which types a field may become: every built-in type that holds a value of its own into every other, none of content, grids or repeatable groups; 25 built-in changes restated apart from the code with everything each keeps and loses; every rule by which a setting is kept, narrowed or lost (kinds, item kinds, required, bounds and their exclusivity, lengths, counts, allowed values, choices, nested objects, any JSON); the settings a change needs; the operators of each rule kind; a site's own type changing like the built-in ones; and a lock of the new type's setting taking it out of what is kept, narrowed and needed, named among the locked ones only where the change would have carried it ([ADR-0174](../docs/decisions/0174-changing-a-fields-type-in-the-designer.md)). |
| `FieldSettingLocksTests` | The host's designer locks of field settings: a value of every setting kind read as configuration gives it and written as settings store it, and every value its setting refuses, a site's own type's among them, each stopping the site with its key and what the setting takes, never the value; a type or a setting the site lacks, with the spelling suggested for another letter case; a required setting locked without a value; locks of an action type and of a shape the options refuse, and field types that cannot be a registry, left to their own checks; the site starting through the host; every field of the type given the locked value or its default, a repeatable group's child fields too, and a definition no lock changes kept as the same instance; and a draft save refused at the first locked setting in document order that holds another value, unless the value is the lock's, the default, left out or the one the stored draft held for the same field and type, compared as its kind compares. |
| `BuiltInAnswersTests` | The longest answer of every built-in type, restated from ADR-0058 apart from the code, and how they read: wire forms, choices with "Other", grids by row, rankings in order or by position (an option or a position given twice read for the check to refuse), a consent's tick with its wording's hash, names and addresses as one object of their parts in order, and a country only as its ISO code. |
| `BuiltInValidationTests` | The checks of the text, number and date types at their edges through each type's `Validate` (ADR-0062), the forms of email addresses, phone numbers and web addresses (ADR-0069), an email's confirmation and its domains, which only the server checks, and the types without checks of their own. |
| `BuiltInChoiceValidationTests` | The checks of the choice types at their edges: options compared exactly, from a list that could not give its options or none at all, the selections, the "Other" text, a grid's columns from its scale or its options and the rows it needs, a ranking's length and each option and position once, and a country's allowed list. |
| `HeldOptionsTests` | The options a check holds a field's answers to (ADR-0077): its own, or those its list or source resolved; for a field with a parent, those of the parent's answers (exactly compared, none for no answer or another field's) and those of every answer; none for a field without options or a source that resolved none; any answer for a source that could not give its options; 10,000 cascading options against 10,000 parent answers held in under a second (a set of the answers, ADR-0080); and a site's own type checking against the same set. |
| `BuiltInPeopleAndFileValidationTests` | The required parts of a name and an address as ADR-0056 reads them, and a file's count, extension (every extension of every group of file types) and size at their edges. |
| `BuiltInUploadTests` | The checks of an upload (ADR-0071) as the endpoint runs them: a signature's PNG header (every colour type and bit depth PNG allows, and each way a header is broken: its signature, chunk length and type, sizes of 0 and past what PNG allows, methods, CRC, a file that is no PNG, one too short), its size in pixels at three times its pad or the widest one, and its mebibyte under a host's larger or smaller limit, failing `invalid`; a file field's limit and `fileSize`, and its extensions at upload as its check of a submission takes them, its bytes left to the host; and the contract through a site's types: the host's limit and nothing found by default, a check of its own through its settings, each kind once and only declared kinds, and refusals of a field that stores no files, a head past 512 bytes, an empty name, a negative size and a type's limit below 1 byte. A refusal found outside the type is of a kind it declares, otherwise `invalid`. The headers carry a CRC computed with a table, pinned to two zlib computed. |
| `FieldHostLimitsTests` | The host's limits for files as a settings check sees them (ADR-0087): the defaults of a site that sets none, a site's own blocked extensions in place of the defaults, compared as written, and a limit below one byte refused. |
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
types, kept by a wrapper around the site's own validator (`Support/RecordingStartupValidator`), since the harness
starts the site before any test runs.

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
| `Csp/` | `TestCspMiddleware`, a strict site's Content Security Policy on every response, and `TestCspNonces`, the host's nonce provider (ADR-0121). |
| `Settings/` | `SampleSettings`, the settings tests' section (below), and `SettingsJson.Global`, a section's global row as the settings service writes it. |

A host turns these on in the `JironFormBuilderTesting` section; everything is off by default:

| Setting | Effect |
| --- | --- |
| `Endpoints` | Answers `/jfb-testing/status`, `/jfb-testing/clock`, `/jfb-testing/access` (each backoffice user's effective permissions, globally, at the root and in each root folder), `/jfb-testing/entries` (a form's stored entries), `/jfb-testing/entries-form` (a published form of job applications
with up to 2,000 entries sent through the pipeline, waiting for approval, and a webhook on approval to a given address;
optionally with places on one option, a unique name, a notice holding each entry's link and a forward address),
`/jfb-testing/audit` (the audit log's entries about one item),
`/jfb-testing/sent-expired` (a sent page's token again, expired a minute ago, ADR-0123) and `/jfb-testing/form-restriction` (a form's own restriction, which no screen sets yet, ADR-0166; `TestFormRestrictionEndpointTests`), only in Development and only for requests from this machine. |
| `Clock` | Runs the host on `TestClock`. |
| `StubPort` | Starts the stub on that port (5560 for the browser tests' host), only in Development, where calls to other hosts may then reach it on loopback at that port only (ADR-0076). |
| `StartupMarker` | Logs `Jiron Form Builder test host: indexes rebuilt` after each rebuild of Umbraco's indexes. |
| `Csp` | Gives every response, in Development only, `script-src 'self'` and nothing inline, from another host or in a style attribute; a request with `jfb-test-nonce=nonce` (or `=strict-dynamic`) gets a nonce of its own instead, which `<jfb-assets>` writes, and is never stored (`TestCspHostTests`; the strict-site spec). |
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
- the places on options (`OptionPlaceRepositoryTests`, ADR-0090): a count made with the places held then, apart
  from letter case, other fields, other forms and the entries that hold none (spam, rejected, drafts, test entries),
  kept by every write of an entry after it (a new entry, each state change into and out of the states that hold
  places, a change of its values away from the option and onto it, of another field's and of an entry that holds
  none, a delete of one in Spam and of several across two forms), each step checked against the count of the values;
  each repeater row and each choice of several counting once, and a move back counted on an option already full; the
  form's row updated as the first statement of every write that can change places; and the refusals of an option
  that cannot be counted;
- the unique answers (`UniqueAnswerRepositoryTests`, ADR-0091): a value taken when an entry that holds its answers
  has it at the field, apart from letter case and surrounding spaces, a number and a date by their columns and a
  longer number by its text, any repeater row, and none of spam, rejected, draft or test entries, other fields or
  other forms, with escaped wildcards matching only themselves; each write of an entry changing whether it holds the
  answer (each state change out of and back into the states that hold, an erased, restored and changed value, a delete
  of one and of several), and the unit's read leaving its own entry out; a key that may be cut compared by the whole
  texts in both reads; the unit's read locking the form's row as its first statement, then one statement per value;
  and no statement for no values, none held for an entry or a form that does not exist, and the refusals of a value
  that cannot be compared;
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
  first with their files, the uploads that wait for an entry, a batch's unsent mail, and a form's references;
- what the submission pipeline asks (`EntryLimitRepositoryTests`, ADR-0088): an entry limit counted inside the unit
  that stores the entry, over the entries that are no tests and are unverified, submitted or approved, a refused entry
  taking no number and one that does not count stored past the limit; and a revision read as the newest published
  version with it, never a draft's.

`Tests.Integration/Files/PhysicalFileStoreTests` runs the file store as a site gets it, in a folder under
`%TEMP%\jfb-files-*`: a file per id in its shard, read asynchronously from the stream, a delete during a download,
and saves that are cut off, cancelled, repeated or made while another save of the id writes, none of which leaves
anything behind or replaces a file, not even one that appears while the save writes. A partial file or a deleted
file's bytes that cannot be deleted are logged without their path and left for retention (ADR-0006, ADR-0041).

The uploads before a submission (ADR-0087) have unit tests in `Tests.Unit/Files/`: `FileKindsTests` holds every kind of
file to its first bytes (each extension of the table with its own kind and another's, ISO media brands major and
compatible, text with and without a NUL or a UTF-16 mark, a tar mark in its place), programs and scripts by their bytes,
the table to the file field's type groups, and the content types; `FileUploadStreamTests` the stream a store reads an
upload through: the head then the rest, counted to the byte at its limit and hashed, asynchronous only, a failure of the
request arriving as `FileUploadReadException` while a cancellation, a limit and a failure already told pass as they are,
and its exceptions with their standard arguments; `FileUploadsTests` the name cleaned for downloads (paths, control
characters, NFC, lone surrogates, trimming, 255 characters with the extension kept and no character cut in two), the
checks in their order with the field's kind of error, a site's own blocked extensions, and the store with fakes: stored
and recorded once nothing follows the file, and nothing left by a refusal, a cut-off, a form that went before or
meanwhile, a part after the file, a request past its limit after the file, a failure after the bytes were stored, a
store that stops early or a request that cannot be read, with bytes that cannot be deleted logged without the name.
A preview's file needs the form, not its publish, and is refused for a form in the trash or gone (ADR-0204).
`PublicApi/UploadTokensTests` holds the tokens to their form, field, file and 24 hours, reads a changed, cut, foreign
or too long one as nothing, and names a token's file only for its own form and field. `PublicApi/UploadRequestsTests` reads an upload's request where a test server does
not reach: the server's own limit becoming the field's while it lets it change, a form that goes before the file is
stored answering `404 form_not_found`, the server's own refusal of the size answering as the field's limit does and a
malformed one as a request it cannot take, a part after the file that cannot be read leaving nothing, and the answer's
model naming only its type.

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
- the key of an option's count (`OptionValueHashTests`): the SHA-256 of the value's UTF-8 bytes, checked against hashes
  computed apart from the code, exact about letter case, and refused for an empty value or a lone surrogate (ADR-0090);
- the row each storage kind stores a value as (`EntryValueColumnsTests`): numbers exact to 15 digits and text only
  past them, dates as their wall-clock values in UTC and text only before 1753, a file as the entry's file, a value with
  parts as its canonical object keyed by its parts' texts, and every value not in its kind's form refused; and what a
  unique answer compares by (ADR-0091): the typed column its row fills, or else the text whose key the row gets,
  refused for a kind that does not compare and for a value not in its kind's form;
- the query builder's statements and refusals (`EntryQuerySqlTests`), and the time budget of a read: a timeout that
  never runs again, a caller's cancellation, a read inside another (`FormBuilderDatabaseTests`);
- units inside units (`FormBuilderDatabaseTests`): an inner unit keeps the outer unit's cancellation and deadline and
  adds its own caller's, and a write inside a read is refused; the migration state read again after a busy read
  (`FormBuilderMigrationStateTests`);
- statement text on SQL Server, with names from `TestSqlNames`: the due list of action runs reads without lock hints
  and the claim with them (`ActionRunSqlTests`); the answers held against a unique one, read without lock hints while
  the answers are read and with them on every table inside the unit, by a number's or a date's column or by the text
  key in a binary collation with escaped wildcards, of the entries that count toward the limit only
  (`UniqueAnswerSqlTests`); and the numbering of a statement's arguments (`PersistenceHelperTests`);
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
case do not, even once the exact spelling is cached; the consent default's texts, refused as a definition refuses them;
the required-marker style, `optional` by default, never `inherit`; the button labels by culture, refused as a form's
own labels are, each without texts stored as none),
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
from the code, and the rows a preview's test entry adds for the fields only the draft has, without a first version,
and the fields it holds no value of, stored otherwise or under another parent (ADR-0204); the code of every refusal and conflict; the notifications' arguments (also of the folder service,
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
answers merge tags show. `SampleEntriesTests` holds the sample entry of previews and test runs (ADR-0187): each type's
answer as its own kind and checks choose it, default values first, a calculation worked out, the entry's number, time
and state, the site's setting locks; and the answers as a page posts them for the preview (ADR-0204), with each grid
row's first column and a signature's typed name. `ConsentWordingsTests` covers the wording: the culture's text with its fallback, a dictionary
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
`SubmissionReaderPrefillTests` reads prefill (6.5c, [ADR-0124](../docs/decisions/0124-prefill.md)): a field hidden
behind the signed-in member's value takes it whatever was posted, the visitor's answer without a member (and is
required of them), and the posted answer where the member's value fails the field's own check; a field shown with the
member's value stores what was posted; and a hidden field takes the member's value, then the context value, then its
default, never a posted one.
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
`SubmissionUniqueTests` names the answers a submission holds against the other entries (ADR-0091): each shown answer
of a field with a `uniquePerForm` check, with the indexes of its unique checks, its row and its value as entries
compare it (a number and a date by their columns, a text as its type stores it), and none for an unanswered or hidden
field, an answer that fails another of its checks, or a type that does not support the check.
`SubmissionHiddenDefaultsTests` renders a submission's hidden fields' defaults over one scope (ADR-0083): a later
default showing an earlier one's value, and a cascading source asked once to check its field's answer and once for
the label two defaults show; a label tag sending the source only the parent's answers the parent offers, never a
posted text; a parent of a site's type that keeps its value on the server, resolved between two defaults, giving the
later one the labels for its value; labels in a repeatable group's rows asked for each row's parent answer, a row's
posted text never among them, so a row whose parent is no option shows its value whatever the other rows answered; a
value the server resolves checked as it is resolved and not again once a later value changes its options, and a
default not among its options not stored; and only the answers made to be replaced changing, their hidden fields kept.
`SubmissionPartsTests` holds the parts of the submission pipeline that stand alone (ADR-0088): the digest that tells a
retry from other answers, the same for the same answers in any order and without the id and the tokens, another for
every change of them; a form's times taking entries from its opening up to but not at its closing; the entries counted
only for a limit and only while the times let the form take one; the states that count toward a limit; and the
results the pipeline gives.

`Tests.Integration/Entries/EntryAnswerStoreTests` publishes a form with every type through the form service, stores
an entry with uploads and a consent wording in SQLite, and reads every type back typed, each value as its type read it;
a wording is stored once for many entries, and an entry refused for an upload another entry took stores nothing.

The entries list (ROADMAP 11.1, ADR-0207): `Tests.Integration/Entries/EntryListTests` sends entries through the
pipeline and lists them: no state named leaves out drafts, Spam and test entries, and a draft is no state to ask for;
the Actions filter, the search by answers, numbers and references, Received's presets in the site's time zone, each
kind's field tests, the sorts, a row's title, unseen mark and cells, no column, filter or sort on a sensitive field,
pages of 50 with the first one counting, a cursor refused by its path, and a choice field offering its own options and
not those of another answer or a source. `EntryBulkTests` deletes for good with the files counted first, the site told
and the numbers audited 200 to an entry, and re-runs what the rules chose. `EntryUserSettingsTests` keeps a user's
columns and filter sets, and `EntryListBudgetTests` measures the first page of 100,000 entries against §4.12's 300 ms.
`ManagementApi/EntryListApiTests` holds the endpoints to the same with each permission. In the client,
`entries/entry-list-model.test.ts` holds what the view says (names, action status, chips with options by their labels,
the columns a filter fixes, the bulk actions that apply), `entries/entry-words.test.ts` its words in both languages,
`designer/entries/entries.view.test.ts` the view against a fake server, and `ui/undo-notification.element.test.ts` the
Undo toast ending with its window. In the browser, `specs/backoffice/entries-list.spec.ts` seeds 1,000 entries through
`/jfb-testing/entries-form`, pages, searches, reopens a filter set in a second browser, and undoes a bulk approval
inside its window, then lets another's window pass (`page.clock` in the browser, real time on the server, whose
clock every spec shares): that approval's webhook goes and the undone ones' never do. `e2e/support/shared-host.test.ts`
holds that no browser spec moves the host's clock.

The entry panel (ROADMAP 11.2, ADR-0208): `EntryAnswerStoreTests` opens an entry of every type through `EntryPanels`
and finds each value as its type reads it from what `EntryAnswerStore.ReadAsync` gives, a typed signature's name read
from its PNG, a consent's exact wording and which answers the panel edits. `Entries/EntryPanelTests` marks an entry
seen once, naming who opened it first; masks a sensitive answer until a reveal, which is audited without the value;
marks a field the live version lost; keeps notes their author's to change; asks before a move back overbooks an
option, its own or an option list's, or doubles a unique answer, and moves only when agreed; and edits answers, each
checked by its type, a choice moving its place, the old values on the timeline and masked there for a reader who may
not see them. `ManagementApi/EntryPanelApiTests` answers each persona by what it may do, keeps a note its author's,
answers an edit's problems by their path, forwards only to users who may view the form's entries and the site's
forward addresses, sensitive values only when chosen, and names an entry's form to its link only for who may view it.
In the client, `entries/entry-panel-model.test.ts` holds the bar of each state and user, the details line, sizes, the
timeline's order with Retry, and what a move back asks; `entries/entry-panel.element.test.ts` the panel against a fake
server (its header in each place, Reveal and Hide, files, the bar, Approve staying on the entry with Undo, a move back
asking first, Edit answers with its problems and its asks, Retry, notes, Delete… and Forward…);
`entries/entry-screen.test.ts` the entry's own screen and the Forward… sidebar; and `designer/entries/entries.view.test.ts`
the panel beside the list (narrowed rows, Enter, `j` with the panel following, `a` on the open entry, Esc, a deleted
entry's row, the panel covering a narrow view) and the bulk bar's move back asking first. In the browser,
`specs/backoffice/entry-panel.spec.ts` approves in the panel and undoes it, the audit log (`/jfb-testing/audit`) and
the timeline holding both; moves a rejected entry back onto a full option and a name another entry holds, which asks
and moves only once confirmed; forwards an entry to a team address that the pickup folder receives while the API
refuses a stranger's; and opens the `{{ entry.url }}` of a notice from the pickup folder, as a user who may view the
form and as one who may not. `/jfb-testing/entries-form` takes `places`, `unique`, `notice` and `forward` for these.

The Inbox and Spam (ROADMAP 11.1, ADR-0209): `Entries/EntryInboxTests` lists every form's entries through
`EntryLists`: the Inbox unseen first across the seam of its two phases in pages of 50, without the forms of a
restricted folder or the Recycle Bin, Spam or test entries; a sort the user picks replacing that order; the Form filter
offering one form's fields and columns, refusing them for several and a form out of reach by its path; Spam holding
Spam only; the badge's count stopping at its most; and Empty spam counting first, then deleting.
`ManagementApi/EntryInboxApiTests` holds the endpoints to the same with each persona: the viewer's Inbox without the
restricted folder's form while that folder's own group sees it, the forms named with what the user may do on each, a
user without grants answered with empty lists, opening an entry taking it off the badge, Empty spam deleting only
what the user may delete, a chosen form named on a page without its entries, and the view naming the forms of the
user's sets while leaving out a set of a trashed form. In the client, `entries/entry-list-model.test.ts` holds the Form
chip, a form's fields going once it is no longer the only one chosen, a set as each place applies it, actions weighed by
each entry's form and the Inbox's own order; `entries/entry-dialogs.test.ts` the State part offering each place's
states with the headline in each language, the column chooser's line without one form, and the Form filter's search,
ticks and limits; and `designer/entries/entries.view.test.ts` the Inbox and Spam against a fake server (the form
column, one form's fields and columns, a bulk action per form with one Undo, a move back asked once for several forms,
Spam without a state and with Not spam and Delete…, Empty spam… asking how many go and stay, each place's first use,
and the panel naming the form). The Inbox's badge: `entries/unseen-poll.test.ts` reads the count when heard, every
minute while the tab is on screen and at once when it comes back, waits at the gate before every read without piling
reads up, and stops with its last listener; `ui/idle-gate.test.ts` holds the rule that a call waits while the token has
run out and nobody used the backoffice since its last renewal, or once the session has ended until the next sign-in,
and the input of other tabs; and
`section/inbox-menu-item.element.test.ts` the badge up to "99+", the item's name with the count for screen readers and
the open item marked. In the browser, `specs/backoffice/inbox.spec.ts` holds the viewer's Inbox without the restricted
folder HR's form, in its rows, its Form filter and its badge, while the HR team sees it; Not spam on an entry whose
unique answer a visitor sent again meanwhile, which asks first and moves only once confirmed; and, with the page's clock
(`page.clock`), the badge's poll renewing the access token once after the page opened, then waiting while nobody uses
the backoffice, until Umbraco ends the session 20 minutes after that renewal and asks to sign in again, with no poll
since. The test server's clock is real, so at that point it is told to refuse the session's refresh token, as a server
20 minutes on does.

A move back across forms asks once before any form moves: `Entries/EntryPanelTests.cs` holds the check that reads
what a move back would ask and moves nothing, `ManagementApi/EntryStateApiTests.cs` the state change's `check`, and
`designer/entries/entries.view.test.ts` a Cancel that leaves every form as it was. `entries/unseen-poll.test.ts` also
holds a poll started again while the stopped one's read was under way, which reads once more for its new listener.

The summary email (ADR-0209): `Entries/EntryDigestTests.cs` moves the test clock (which each test sets to its own start,
since a fixture's clock carries over between its tests) past the site's 07:00 in Berlin and on Mondays, and holds one
summary per user and period however often the job runs, nothing before 07:00, nothing for a user who has not asked or
for a day without entries, no form the user may not view entries of, no answer in the mail, the links only with the
site's address, the weekly count of what waits for approval, a summary that still goes when the user saved the same
choice again just before the job ran, a change of how often that sends nothing for the days before it and never moves
the record back, and the test mail at once, marked as a test, with a German preview; `ManagementApi/EntryDigestApiTests.cs` each user's own choice with when and where it goes, the preview
that sends nothing and the test to the user's own address. `entries/entry-digest.modal.test.ts` holds the sidebar's
choices, its sentence by the time zone's place or UTC, the preview it asks for (off shows the daily one), saving, a
failed save, the test toast and a user without an address; and `specs/backoffice/inbox.spec.ts` the sidebar in the
browser from the Inbox's "…", its preview, Send test email, Save and Off, with axe.

The exports (ADR-0210): `Exports/EntryExportTests.cs` reads every format back from its file: a CSV round trip with
`ExportFiles.Csv`, which knows quotes and line breaks, the Excel workbook with `ExportFiles.XlsxAsync`, a test-only
reader that follows the workbook's own relationships, the JSON by alias, the printable view without scripts and the ZIP's
listing; a grid by row, a ranking by position and a repeatable group as lines, as a sheet and as rows; the formula guard;
sensitive fields only when asked for and allowed, a repeatable group's own and a sensitive group's too, and a field
only the catalogue marks, at the top or in a group; the list's
filter and the ticked entries, never another form's, a test entry or a draft though ticked; an unpublished form laid
out as it was last published, not as its draft; each reference and time received as the entry's own version and site
gave them; the audit and the announcement; a background export for its owner only until it expires, one restarted
once its lease ran out, the lease renewed on its timer while a page takes long, a store that times out counting as a
failed attempt, a host's stop leaving no file behind, three attempts and three exports under way per user; and 10,000
entries of 12 values as CSV within 5 s while 50 submissions arrive, each within 150 ms at p95, with one more going to
the background. `Exports/EntryExportWorkerTests.cs` keeps this server's own worker: a queued export is written at once,
and the job removes what expired and wakes the worker, which takes over an export whose lease ran out.
`ManagementApi/EntryExportApiTests.cs` holds the endpoints: the view with the user's last choices, which the entry
panel's Print leaves as they were, the streamed file and its headers, a background export that only its owner follows
and downloads through a link with its ticket, which no token, no other export and no changed ticket opens, no ticket
once its owner may no longer see the sensitive fields it holds, every ZIP of files in the background, and the refusals.
`Exports/ExportFormatTests.cs` holds the pieces that need no site, `ManagementApi/ExportDownloadTicketsTests.cs` the
ticket, and `ManagementApi/FormBuilderAccessFilterTests.cs` its declaration. In the client,
`entries/export-model.test.ts`, `entries/export-follow.test.ts`, `entries/export-api.test.ts` and
`entries/entry-export.modal.test.ts` hold the sidebar, the follower at the idle gate, its toasts and the link; in the
browser, `specs/backoffice/entry-export.spec.ts` ticks three entries, chooses Export selected… and finds only those in
the CSV file, exports a search as JSON, which the sidebar opens with next time, prints one entry from its panel, which
leaves that choice, and saves a ZIP of the files from its toast through the link, which carries no token.

Insights (ADR-0211): `Tests.Unit/Insights/InsightTests.cs` holds what needs no site: the buckets of the time to complete
and what a stored entry adds to its unit, the beacon's reader, which reads four members and skips every other, what a
beacon may count of the published version (a page it has, a field by its path, a row's child and a grid's row under
their field, a kind of error the form knows) and only while both switches are on, the fields a post without JavaScript
was refused at, the sums that wait (a day each, a limit, put back after a failed write), and a report built from daily
values: totals, groups by day, week from Monday and month, each beside its counterpart, the pages, the median bucket,
the fields with the kind that stops most, the website pages and the hours. `PublicApi/PublicFormInsightTests.cs` posts
beacons as `text/plain` and JSON and finds only what the version has once written, nothing of a value posted along,
`404` for a form not served and `413` past 1 KB; the markup's `data-jfb-insights` only while both switches are on and
the element's flag instead of an address; an entry's page, hour and time to complete in its unit and its duration; none
of them, nor the entry, when the unit fails right after the counts; and only `submissions` for a form whose insights
are off. `Rendering/FormPostTests.cs` counts the field a post without JavaScript was refused at.
`ManagementApi/InsightApiTests.cs` holds Privacy › Data collection's switch, a form's report with its comparison, its
pages and fields by name, its refusals and its default span, the site's table of forms and the boxes kept as tables. In
the client, `insights/insight-model.test.ts`, `designer/insights/insights.view.test.ts`, `insights/insight-glance.test.ts`,
`settings/privacy-settings.test.ts` and `insights/insight-words.test.ts` hold the view, the Overview's box, the screen and
the words, and `runtime/src/frame.test.ts` the beacons; in the browser, `specs/backoffice/insights.spec.ts` uses a form on a
headless page, finds its beacons arrive and fill its Insights view, a box kept as its table, and no beacon once the form's
switch in its Privacy group is off.

Answer summaries (ADR-0212): `Tests.Integration/Insights/AnswerSummaryTests.cs` sends entries to a form with a field of
every kind and finds each summary from the database, labelled as its type shows its values: options with the visitors'
own texts as one line, several choices, a country by its name, a ranking by its average position, a grid row by row,
a number's figures and a scale's groups and score, the free texts newest first, an email answered, a checkbox ticked and
a repeatable group's child by its rows; never a test entry, an entry in Spam (also where the filter names it), a
sensitive field, one kept only until the actions run, or a child of a group that is either; an older version's option
under its field, marked and named as that version named it; the span and the list's filter choosing the entries; the
free texts a page at a time, also across a group's rows at one instant; the visitors' own texts as one line where a list
gives the options or only an older version offered Other; a grid counted once per entry, with an older version's rows
and columns marked; and a scale's groups only where every version asked from 0 to 10. `AnswerSummaryBudgetTests.cs` summarises 100,000 entries within a second on
SQLite. `ManagementApi/InsightApiTests.cs` refuses the answers without `viewEntries`, an unknown form or field, a bad
state, a backwards span and a bad cursor, and the permission matrix holds both endpoints. In the client,
`insights/answer-model.test.ts` and `insights/insight-answers.element.test.ts` hold the boxes of every kind, their tables,
Show more and its focus, a page that comes too late or was asked for while another range loaded, a share far under 1%,
a grid's removed row, the empty, failed and busy states, and `designer/insights/insights.view.test.ts`
the switch, the filter's dialog and chips, the range read once the Overview shows and the tables kept; the testing host's
`POST /jfb-testing/answers-form` seeds a form with an answer of every kind, and in the browser
`specs/backoffice/insight-answers.spec.ts` opens its Answers as a user who may only view entries, finds each kind's numbers,
filters by an answer through the list's dialog, reads the free texts on, keeps the tables and opens an entry from its
text, with axe in both ways of showing.

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
| `PermissionMatrixTests` | Each endpoint's declaration against a table written out by hand; each endpoint as the outsider (`403 no_section`), as a persona who sees the folder or form but lacks the permission (`403 forbidden` naming the first missing one), as one who holds nothing on the restricted folder or its form and with a key that names nothing (`404`), and the section-wide ones as a user of the section without grants. Each test writes the endpoints it called, with their answers, to its output. The form picker's five endpoints are its one reviewed exception, each checked to carry `FormBuilderContentAccess` and left out of the outsider's refusal; `PickerApiTests` hold their access. |
| `PickerApiTests` | The form picker's endpoints (ADR-0013, ADR-0133, ADR-0135): the folders of given keys (for the settings) are those the user sees outside the trash, also one without a published form, in the order asked and each once (never a restricted one without a grant, one in the trash or below one, or a key of nothing; a group's grant adds a restricted one; an administrator sees all), and 101 keys `400`; an editor with the Content section and without the Forms section gets the published forms that are not restricted and the folders that lead to them (never a draft-only, trashed or restricted form, a restricted folder `404`, a folder with only drafts left out), by level, search and key, names and keys only; a group's grant on a restricted folder and form adds them; a data type's allowed folders (and below) and forms limit every answer, an allowed folder below another shows once, a folder outside `404`, a data type that is no picker's `404 data_type_not_found`; the themes are every theme in order and each is accepted by the picker's settings; a user with the Forms section only `403 forbidden` naming `content`, one with Settings only answered, none signed in `401`; `take=0`, no text and 101 keys `400`. `FormBuilderAccessFilterTests` (unit) check content access in each of the four sections in any letter case, without asking any policy, and its order: the sections, the migrations, the values. `FormRepositoryTests` hold the list's published-only, keys and scope filters and the folders that hold published forms. |
| `PendingMigrationsApiTests` | Every endpoint answers `503 migrations_pending` with `Retry-After` while the plan has not run (the site boots without it), and normally once it ran; the test writes each answer to its output. |
| `RefusalBodiesApiTests` | Each kind of refusal through Umbraco's pipeline, from a real call: `application/problem+json`, the status in the body, the code, a title for editors and what the kind adds (the problems, the permission, a handler's messages). |
| `TreeApiTests` | The tree and the trash as each persona sees them, folders first, with the user's permissions and `hasChildren` (only for what the user sees, in the folder's own state of the trash); paging across folders and forms, at the root and in the trash too; a folder in the trash holding what went with it; no key as the root; pages outside the limits. |
| `FolderApiTests` | Each folder endpoint's answer and change: the permission checked before the request's values, `design` on a move's new parent, `managePermissions` and a confirmation for a move that widens access, `design` and `deleteEntries` on everything a permanent delete removes (restricted folders and forms below it included), an emptied trash that keeps what the user may not delete, and a restore to the root (its parent in the trash) that needs what a move there needs. |
| `LibraryApiTests` | The form library's endpoints (ROADMAP 9.2, ADR-0166): a duplicate lands in its form's folder with or without actions and takes that folder's access, and actions need `manageActions` there; copied actions map their fields by alias and the answer lists what the target lacks, a target the user doesn't see `404` and one whose actions they may not manage `403`; the gallery lists Contact first with what it holds and its definition, and a form from it lands in the folder, `template_not_found` for a key of nothing; the import's check says what a file holds and what it can't bring (`theme_missing`, `page_missing`, paths into the file), the import gives the same notes, an unreadable file `400`, a file with actions `403` without `manageActions`; an export is the draft's file named by the alias, a missing version `404 version_not_found`; the field search finds published fields of forms the user sees, by label or alias in any letter case, never a draft's field, a hidden form's or one in the trash; a folder's list counts the last 30 days' entries the user may view (no spam, no test; a folder sums its forms and folders), says who changed each item, and refuses a folder the user doesn't see; the trash's list says when and by whom, a form's holdings count every entry and file, and the trash's what emptying would take for this user; the tree marks unseen entries for who may view them; an empty folder goes at once, one holding a folder or a trashed form is refused. |
| `FormApiTests` | Each form endpoint's answer and change: a search of what the user sees by name or alias, the form for anyone who sees it and the draft's document for `design`, a save of the document with its version (or none when nothing changed), problems with paths into the document and `409` for a stale version, `manageActions` for a save that changes the actions, publish and unpublish, rename, a move's new folder and its widened access, a restore to the root that needs what a move there needs, and a delete for good with `deleteEntries`. |
| `VersionApiTests` | The History of a form with four versions: the publishes or every version, newest first, with what changed and paged; a version with its document; a comparison with each property before and after; a restore into a new draft that needs `manageActions` when it changes the actions, after the concurrency check; pins and comments that leave the form's concurrency version alone; `404 version_not_found`. |
| `RestoreLandingApiTests` | A form's and a folder's restore whose folder comes back from the trash after the endpoint checked where it lands and before the write (`RestoreActionHook`, an action filter after every other, arms `HookedFolderRepository` to bring it back right after the action read it): `409 landing_changed`, and the form or folder stays in the trash where it was. |
| `FormBlockApiTests` | "Create a form block type for me" (ADR-0130): the element type with its mandatory picker property and the picker's data type created on the first call, nothing changed by a second (the same keys, the element type's date as it was, one data type); a content type with the alias and the data type with the key, both changed by an editor, left as they are; a user with `manageSettings` and the Forms section but not Umbraco's Settings section refused with `403 forbidden` naming the section and nothing created, and one with all three answered (both groups seeded in the test). `FormBuilderAccessFilterTests` (unit) checks a declared Umbraco section after the permissions, in any letter case. |
| `FormCacheEvictionTests` | Eviction with Umbraco's output cache on (ADR-0004, ADR-0132), through a server messenger that delivers cache messages on the test server (`LocalServerMessenger`, where the host's own delivers none): "publishing the form re-renders a cached page that picks it at once" (a new stamp and the version just published), while a cached page that picks another form, and one that names a form in its view, are kept; a rename, an unpublish and a move to the trash evict as a publish does, and a change to many forms evicts every page that uses a form; a page that uses no form gets no tag, one that does `jfb-form` and the form's own. `FormCacheTagsTests` (unit): a page whose forms cannot be read gets `jfb-form` and a logged warning; the eviction evicts each form's tag once, and `jfb-form` for a change to many forms or a refresh of all. |
| `FormReferenceApiTests` | "Used on" on a real site (ADR-0017, ADR-0131): a page that picks the form listed in the shape of Umbraco's document reference (`$type`, its document type's key, alias, name and icon, its variants), a draft's pick too, the published page kept while only its draft drops the pick, and gone once that is published; a form picked in a block of a block list listed under the list's own property; a page saved with a form deleted meanwhile, or one the library never held, writes no row; a page outside the user's content start nodes counted in `notShown`, never named, and paging by `skip` and `take`; a missing or unseen form `404`, `take=0` `400`; the rebuild (a part past its deadline stopping after a batch) finds a published page, a draft, a block and a page in the recycle bin and drops a gone page's row; the migrations ask for a rebuild and the reference job does it. `ReferenceRepositoryTests` hold the rows: each once, none to a missing form, a content item's rows replaced; refused aliases and cultures; the deletes by content and by form; the walk over documents in id order with the recycle bin and without blueprints or media, in parts; the delete of rows whose content has gone. `FormReferencesTests` (unit) cover the picker's references and the UDI type, the handler's writes, a failed write logged and the next item written, a cancelled one passed on, the rebuild's batches, deadline and unreadable request, and the job's conditions, period and logged failure. `MigrationPlanTests` hold step 4: a fresh install asks for the rebuild and a replay leaves a walk under way as it is. |
| `OpenApiSnapshotTests` | The document `jfb` against `docs/api/jfb.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), its paths and operation names, and schemas that are only its own models, never a framework type described by its properties. |
| `FieldTypeApiTests` | The field types for the designer ([ADR-0057](../docs/decisions/0057-field-types-endpoint-and-availability.md)) against `docs/api/field-types.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), served to a user of the section without grants; every type in the registry's order with the palette's and the inspector's groups, its declaration in camel case, its common properties and messages, its settings in the inspector's order with their kinds, choices and labels, nested settings, defaults and rules, and its availability; whether its fields may change type; and each type's changes from `field-type/{alias}/changes` as the map has them, a few held to `docs/api/field-type-changes.json`, a text that becomes a dropdown losing its placeholder and `contains` and needing options, and a type the site lacks `404 field_type_not_found` ([ADR-0174](../docs/decisions/0174-changing-a-fields-type-in-the-designer.md)). |
| `CanvasApiTests` | What the designer's canvas draws with ([ADR-0168](../docs/decisions/0168-the-canvas-draws-with-the-elements-views.md)) against `docs/api/canvas.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails, its stylesheets left out and held to the default theme's addresses instead), for a user of the section without grants: the words the views say with Yes and No, the default theme's classes, the stylesheets and the site's required marker; the words in German and French for those cultures and in the default language for one the site lacks; the marker after the general settings change. |
| `FieldSettingLockApiTests` | The host's designer locks through the API ([ADR-0060](../docs/decisions/0060-designer-locks-of-field-settings.md)): the field types mark a locked setting with its value and key and a hidden one without its value, which never leaves the server; a draft save with another value answers `409 setting_locked` with the setting's path and the lock's key, and the lock's own value saves; a change into a locked type neither carries the locked setting over nor needs it (checkboxes into a dropdown, a long text into a text, a number into a text with a hidden lock named only), and a lock of the type it leaves changes nothing. |
| `FormPreviewApiTests` | The preview (ADR-0203): the saved draft never published as a page shows it, with the base rules and the theme's stylesheet, the preview script before the runtime, a ticket that holds for the draft's version and how long it counts, the revision the draft would get, the page titled with the form's name as text, a form and buttons (a ranking's among them) with no address to post to, and `no-store`; German and dark where asked, the default language for one the site lacks, light for a theme without the dark scheme; a form for members shown to the test member, not its sign-in notice; a draft whose rules have errors `422 rules_invalid`; a new ticket for a version, `404 version_not_found` for one the form lacks. With 10.3f (ADR-0204): the sample answers as the pages' starting values (a text's label, the sample email, a signature's typed name) and `data-sample` for the script; a document or a ticket whose test entries run their actions needs `manageActions`, while `design` alone gets a plain one; a test entry the frame sent found by its submission id with its number, whether its actions ran and those waiting, by who may design the form, its actions run once by who may manage them (`204`, then `409 actions_ran`), and `404 test_entry_not_found` for any other submission. |

In the client, the preview's script (`packages/runtime/src/preview.test.ts`) lends the ticket to the public API's
requests alone, an upload's too, asks over the port the backoffice handed it, once for requests at once, and goes with
the ticket held when no answer comes; it sends a ticket handed over the port unasked from then on, says which
submission the public API took (from its answer's receipt; a refusal or an answer without one says nothing), says once
that the public API refused its ticket (`401 preview_ticket_invalid` alone, the page still reading the answer), makes a
sample file of the first kind a field accepts, and gives each file field of a document of sample answers its file once
the field has woken, none to one that does not wake in time. `visitor-preview/frame-host.test.ts` hands that port to
the frame's first document only, answers an ask once the last ticket is about to run out and one at a time, closes the
port when the frame goes on or the preview closes, tells of each submission the frame sent, by a UUID, once, and of ten
for one document, and of a refused ticket for its form only, and hands a switch of the test entries' actions over as a
ticket that its renewals keep, the latest switch deciding and the one that settles telling what the frame holds;
`preview.modal.test.ts` and `preview.action.test.ts` hold the sidebar (Fill with sample
answers and Start again, the note as the frame's ticket has it, for an entry waiting for its confirmation and for a
ticket refused, with Load again, Run
actions... after its confirmation, the switch for who may manage actions) and the
footer's Preview, which names the actions as the Actions view does; `preview-read.test.ts` what the preview reads, and
`ui/confirm.test.ts` a confirmation's list as text. `e2e/specs/backoffice/preview.spec.ts` opens the preview in Edge: the frame's opaque origin, a
challenge from `null` with the ticket, each width to the pixel, dark keeping the answers, German, and axe in the frame
as it stands, light and dark (`axeInFrame`).

`Tests.Unit/ManagementApi/` covers the same without a host: the access filter's order, refusals and fail-closed
cases, how it reads a key (`FormBuilderAccessFilterTests`); every answer to a refusal, the exception filter, the
declarations, the operation and schema names and the feature provider (`FormBuilderProblemsTests`); the models of the library
(`LibraryModelsTests`), of the versions, with the stable names of their kinds (`VersionModelsTests`), and of the field
types, with their names in camel case, an availability for every type and the lock of every locked setting, a hidden
one without its value, and each type's changes with the new type's locks (`FieldTypeModelsTests`); and the
policies and handlers (`FormBuilderAuthorizationTests`). In the client,
`packages/backoffice/scripts/generate-client.test.mjs` generates the client from the snapshot next to the committed
one and fails on any difference: after a change to the API, run the snapshot test with `JFB_UPDATE_SNAPSHOTS=1`, then
`npm run generate-client -w @jfb/backoffice`, and commit both. `packages/backoffice/src/hey-api.test.ts` shows that
the generated client starts from the backoffice's own HTTP client, whose settings win. `packages/backoffice/src/lang/lang.test.ts`
holds the English and German dictionaries to the same keys, each a text, to every `#` key of `docs/api/field-types.json`,
and the field types' areas to no key nothing serves, a function's text given sample values; it holds the form picker's
screens (`jfbFormPicker`) to exactly the keys their elements and manifests use (as a term, a `#` label or a `{#...}`
description), and a button a screen reader names more fully than it shows, the preview's Run actions… and a last
run's Details, to a name that starts with the words shown, in both languages (WCAG 2.5.3, ADR-0206). `bundle.manifests.test.ts` shows a `localization` manifest for each of the two languages loading its
dictionary, and the form picker's schema named after the server's property editor (read from
`FormPickerPropertyEditor.cs`) with its own UI as the default, its sidebar under the token's alias and the block label's
`jfbFormName`, each loading its module; and its settings (ADR-0135): the server's `ConfigurationField` names (read from
`FormPickerConfiguration.cs`) in its order, each with a label and help from the dictionaries, Umbraco's toggle for one
form or several (off by default, named as its label), the picker's own UI for the allowed folders and for the allowed
forms (`pick` set to each), the theme's own UI, Umbraco's document picker for the page after a send with one page at
most, and the two settings UIs registered without a schema, each loading its element.

**The form picker** (`packages/backoffice/src/picker`, Vitest in jsdom with Umbraco's packages transformed by Vite and
`src/test-setup.ts` adding what jsdom lacks: constraint validation on `ElementInternals`, `SVGAElement` and
`ResizeObserver`; ADR-0134, ADR-0135). Each test replaces the reads of `picker-data.ts` with hand-answered fakes and registers
the English dictionary (`src/testing.ts`):

| File | What it holds |
| --- | --- |
| `form-picker.test.ts` | the property: the picked form by name and alias, looked up with the data type's key from the content editor's property context (none outside it); several forms in their order with Choose, one form without it, Choose alone while empty; a form the picker cannot show in Umbraco's not-found look with its own Remove label; rows in their final shape while the names load; read-only with neither Remove nor Choose; Remove at once, the value written, the change event, and the focus asked of the next Remove and then of Choose (jsdom keeps no focus inside UUI's slotted buttons, so the review follows it in Edge); Choose opening the sidebar with the settings (several, limited, the data type) and the picks, and writing its answer without a second lookup; Close keeping the value; a failed lookup with Try again; a mandatory picker invalid while empty |
| `form-picker-modal.test.ts` | the sidebar, through a modal context as Umbraco's holds the value: the top level within the data type's limits, rows in their shape while it loads, the search focused; a folder opened in place (`aria-expanded`, its level nested and indented), loaded once, closed and opened again; one form at a time (`aria-pressed`, the check mark, Choose enabled, pressed again for none) and several in the order picked with the property's picks kept, Choose submitting; the search after its pause with the loader in the field, an older answer dropped, the count announced, a search that finds nothing with Clear search bringing the levels back and the focus to the field; Show more with the focus on the first form it brought; a failed level with Try again; an empty library told from a data type whose allowed forms are not published; no data type, and Close rejecting; the folder mode of the settings (ADR-0135): folders only, without the search, each with a chevron (`aria-expanded`, "Show the folders in ...") that opens its level and a row (`aria-pressed`, the check mark) that chooses it, several in the order chosen with the given folders kept, the empty top level and folder, Show more only while the pages hold folders (a level lists them first) with the focus on the first folder it brought, and its own failure text |
| `form-picker-allowed.test.ts` | the allowed folders and forms of the settings: the folders by name with the folder icon in the order chosen, read without any data type's limits, also one without a published form; one the user cannot see as "Folder not available" with its own Remove label; Choose alone while none is allowed; rows in their shape while the names load; Choose opening the sidebar in its folder mode for several with the chosen folders, and writing the keys it answers in order; Close keeping the value; Remove at once with the focus asked of the next Remove, and no value once none is left with the focus asked of Choose; read-only; a failed lookup with Try again; a value written by hand read as keys, each once; the allowed forms by name and alias through the forms' lookup, the sidebar opened for several forms, and their own failure text |
| `form-picker-theme.test.ts` | the theme of the settings: "The form's own theme" first, then the site's themes by label, read without a data type; the chosen theme chosen; a chosen theme the site lacks kept at the end as "(not on this site)"; a theme's alias written on change and no value for the form's own; read-only passed to the select; a box in the select's place while the themes load; a failed load with Try again |
| `form-name.ufm.test.ts` | the block label: the picked form's name without a data type's limits, several names joined as English joins a list with "(not available)" for one that is gone, the label following the block's value, nothing for no value or a failed lookup, and the component rendering its element with the token's alias when Umbraco calls `render` without the instance |
| `picked-keys.test.ts` | the stored value: keys in order, each once, in lower case; none for no value; written as the server reads them |

**The form library** (`packages/backoffice/src/library`, `tree` and `lang`; ROADMAP 9.2, [ADR-0166](../docs/decisions/0166-the-form-library.md)):
`stores.test.ts` drives the client stores with hand-answered calls: a list's first page, the next one without a row twice,
a failure and a load again, an older answer dropped after a newer one and the rows kept on screen meanwhile; the
gallery choosing its first template, searching names and descriptions in the user's language, a choice the search hides
giving way, the arrow keys wrapping at both ends; the import refusing a file over 5 MB unread, checking, importing,
and saying the server's reason or a missing permission. `rows.test.ts` holds the days, changes and entries of a row
(by culture), an import's notes said once each in file order, and the pages a form is on (five named, the rest
counted). The gallery's preview is the designer's (below). `tree/tree.test.ts` holds the tree's
items (the trashed ones under the Recycle Bin or their trashed folder, the restricted and unseen marks as flags, a form
without a published version muted) and the header search's results for forms and fields. `lang/library.test.ts` holds
the confirmations to the examples of `docs/ux/copy.md` and their counts of one, none and more names than are listed, in
English and German; `lang.test.ts` holds the `jfbLibrary` area to exactly the keys the library's code uses.

**The designer** (`packages/backoffice/src/designer`; ROADMAP 9.3b,
[ADR-0168](../docs/decisions/0168-the-canvas-draws-with-the-elements-views.md)): `preview/field-models.test.ts` builds the
view model of every field of the server's sink from its draft, writes it with the element's views and compares its
elements and classes with the server's markup (`designer-sink.json`, `element-en.json`); a field the server leaves out,
the designer leaves out too; and an upload and a signature are drawn as the runtime leaves them. `preview/preview.test.ts`
holds a draft's rich text to its allowlist (no attribute, nothing that runs or loads, text escaped), the canvas
endpoint's answer as a look with its words, and the read-only preview of the contact template for the eye only, its
fields said once for screen readers. `canvas/canvas.test.ts` holds a field on the canvas (the wrapper the views write,
inert, under a button that selects it and an insert point; Duplicate and Delete while selected; its markup and buttons
kept when drawn again; its label edited in place; a note for a field visitors don't see; a repeatable group's first
row with its own fields) and the canvas (steps, the page in the default theme, the groups' and pages' buttons, a new
form's and an empty page's sentences, an image's two notes, After sending without anything that runs, the widths and
the menu, and the requests it passes on; a label for screen readers only shown while edited, an edit that ends where no
label shows, a page's groups once it has more than one or a titled one, and the focus given to Add field for what it
doesn't draw). `palette/palette.test.ts` holds the palette's search, groups, recently used types, modes and the paste
dialog with its label and help; `new-elements.test.ts` new fields, groups and pages, aliases and the types pasted
questions suggest in English and German; `form-design.view.test.ts` the Design view through a stand-in workspace: picks
from the palette at the right place, refusals said, New page, labels in place, delete with Ctrl+Z and Ctrl+Y, Ctrl+D,
Delete and Esc, Add page, pasted questions, the width kept per browser, the media an image needs, and the width up to
which the palette opens over the canvas; the toast's Undo taking back its delete only while it is the last change, a
selection let go with the focus on Add field, a label edited in the canvas's culture, the canvas inert under the palette
with Esc and a click beside it giving the focus back, and a look that can't be read with Try again. `drawing.test.ts` finds no other drawing of a field: only the canvas and the
preview import the views, only the models and the canvas's label finder name a field's classes, and the gallery shows
a template with the preview.

**The designer's moves** (ROADMAP 9.3c, [ADR-0169](../docs/decisions/0169-the-designers-moves.md)):
`drag/drop-place.test.ts` holds where a point lands among boxes in reading order (columns too), the line that shows it
and the container under it; `drag/pointer-drag.test.ts` the drag itself: a click under 6 px, a drop that swallows its
click, Escape that goes no further, a lost pointer, one drag at a time, the main button only, and the autoscroll near
the edges and below the bottom, not above or beside; `canvas/canvas-drag.test.ts` the canvas's drags over a laid-out
page: a field between fields, a group among groups, a new field, a page among the steps with an upright line, a refusal
at the line asked once per place and said on the drop, a step's page end and the page shown after 600 ms, Escape, and
the settle (once, from where the copy was, none with reduced motion); `moves/moves.test.ts` a drop's edit, where an
element is, the ends of pages and groups, Move to…'s places with their refusals and the keys' names;
`moves/dialogs.test.ts` Move to…'s radio buttons by page with a refusal's reason, the focus on the first place, Move
only once chosen and Enter, and the shortcuts with Close only. `canvas/canvas.test.ts` adds each element's handle, a
refusal at a field, group or page, the order Up and Down go through and the focus on a handle or a step;
`palette/palette.test.ts` a type pressed to drag, beside the canvas only; `form-design.view.test.ts` drops with Undo,
a palette type dropped, refusals at the element without a toast, Alt+Up and Alt+Down across pages with the focus kept on
its control, Delete, Ctrl+D and F2 on the focused element with Undo giving the focus back, Up and Down, Move to… with
Cancel, and `?`. In the browser, `specs/backoffice/designer-moves.spec.ts` builds a form of three pages with the
keyboard only and asserts the focused element after every operation, moves fields, a group and a page with the mouse
with a refused drop and palette types, one on an empty page; and the `budgets` project, alone after every other one,
holds a form of 300 fields to 100 ms per interaction (Event Timing) and per frame (Long Animation Frames) in
`specs/budgets/designer.spec.ts`. What keeps it there (ADR-0176): `canvas/canvas-far.test.ts` holds a group far from
the view marked at its height and cleared when near, watched against the canvas's viewport each once and until gone;
`canvas/canvas-drag.test.ts` a far group never measured nor dropped into; `canvas/canvas.test.ts` the page's buttons
kept across a selection and its groups watched; and `form-design.view.test.ts` the workspace's scrollbar fade stopped
at the view.

**The inspector** (ROADMAP 9.4a, [ADR-0170](../docs/decisions/0170-the-inspector.md)):
`inspector/setting-values.test.ts` holds a setting's value with its default and lock, written back and left out where
it is the default, a text in each language and where it goes, and what is typed checked as the server checks it (whole
numbers, numbers in the wire form with their exclusive limits, times, date lists, lengths, counts, allowed values,
choices) with a date's limit read and written; `inspector/inspector-model.test.ts` the sections of the text, number,
date, heading and every other built-in type from the field types snapshot (common properties by feature, settings by
group, a hidden lock left out, only the plain editors), seven items before More options, a default of several answers,
and a field without a text in a language; `inspector/inspector.test.ts` the element: its heading, Essentials open and
the others as left, a closed section's one line, typing as one step per visit, a click as its own, a problem said once
the box is left with `aria-invalid` and `aria-describedby`, More options, a locked setting with its value and key and a
hidden one left out, nested ones too, the texts of another language with the default as placeholder and no placeholder
in the default language, the alias checked as the server
does with a way back, date limits, nested settings and choice lists, a group's and a page's items, and its rail and its
bar over a narrow canvas. `form-design.view.test.ts` adds the inspector beside the canvas, its changes as edits that
merge while typing, an alias that entries know changed after a question and kept as a former alias, its fold and Close,
the palette folded beside it up to 1024 px, the overlay with the focus on its heading, after a pick from the palette
over it too, Start a new page after this and Copy alias, the language switch, and the element's keys kept out of it
while Undo stays where the focus is; `canvas/canvas.test.ts` the element's "…" with its menu, Copy alias only with an
alias, the language switch with the chip of a field without a text in the language shown and none in the default's,
and the insert point right above the selected element as its "+" alone. In the browser, `specs/backoffice/designer-inspector.spec.ts` sets every setting
of the text, number and date types through the inspector and saves them, axe clean with each section open; folds the
palette at a 1024 px window; starts a new page from a field's "…" with the keyboard only; edits a German label with
the chip on a field without one; and changes an alias. `specs/backoffice/designer-locks.spec.ts` runs a host of its own
on 5535 whose configuration locks the text type's maximum length and hides its prefix, and finds the one read-only with
its value and key and the other gone (its dropdowns all take several options, three at most, for 9.4d's change). The integration tests `CanvasApiTests` and `LibraryApiTests` hold the site's
languages, the default first however Umbraco lists them, and a draft's `hasEntries`.

The option editor (ROADMAP 9.4b, [ADR-0172](../docs/decisions/0172-the-option-editor-and-the-option-sources.md)):
`inspector/option-values.test.ts` holds the option model: where options come from, a source changed with its parent
kept, a value from a label or the next free one, pasted lines with repeats folded as the server folds them and split
at every line break the server reads, cuts that never fall inside a character, the server's default limits, rows
pasted with keys of their own, keys for repeated values, a move, cards by the field's layout or the form's, and the
fields before one that it may depend on; `inspector/option-editor.test.ts` the element: the kinds the site serves in
"Options from", a library list with its count or kind, a standard list, a kind's settings at `options.config` with their
defaults, Depends on with Shown for, its answers cleared for a new parent and a gone one kept to untick, the own options
added, labelled, pasted and removed with what each says, the focus on what was moved, added, removed or given a new
value once the field comes back, Alt+Up and Alt+Down on a handle kept from the canvas, a press on a handle opening
its row's "Place in the list" (none for a list of one), the place chosen moving the row with its handle keeping the
focus and Escape or its own place moving nothing (ADR-0205), details (Stored as with its two
problems, places, a description and an image chosen in a stand-in media picker on cards), another language's own words,
and a grid's rows with Needs an answer said once. `inspector/inspector-model.test.ts` puts the options in Essentials and
a grid's rows before its columns, and counts their texts toward a field without a text in a language; `inspector/inspector.test.ts` hands the editor a parent now after the field and the
form's cards; `inspector/option-data.test.ts` keeps an answer by form, field, options and culture, but not one that
failed or found its source unavailable; `form-design.view.test.ts` asks the server once for the options of a field
that takes them from elsewhere and again once its changes pause, and the canvas shows what it answers, or nothing for
an unavailable source; and it reads the canvas's images once through Umbraco's imaging, a card showing its option's. `OptionSourceApiTests` holds the three endpoints: the kinds
with their settings and the standard lists against `docs/api/option-sources.json`, the library's lists by name with a
count, and a draft's options resolved in the culture asked (its own, a list, a standard list, the countries named, a
source the site lacks, a culture that is none) for designers only. `lang.test.ts` finds every `#` key those kinds and
sources are served with in English and German. In the browser, `specs/backoffice/designer-options.spec.ts` gives a
dropdown the demo's "Library branches" and another the months, depending on it, with the canvas showing what they
resolve in English and German; pastes a radio field's options, moves them by drag, with Alt+Up and with single presses
on a handle and its "Place in the list", no drag, refuses a value and a
place limit before keeping them; gives an option on the form's cards a description and the demo's picture through
Umbraco's media picker, which the canvas's card shows; and adds, pastes, marks, moves and removes a grid's rows and
columns, each saved and read back. The designer persona's group may pick from the whole media library for that
(`TestUserGroup.MediaRootAccess`, held by `TestPeopleTests`).

The inspector's richer editors (9.4c, ADR-0173): `inspector/merge-tags.test.ts` holds the place of each text (a
consent's wording its own), a tag put where the caret was over what is selected, and the paths grouped by source in the
server's order, a field by its label and the site's own sources under one heading, found by words or path in any letter
case; `inspector/merge-tag-picker.test.ts` the panel: the place's paths asked with the draft as it is, in the language
shown, for the element whose text it is, a search with nothing found said, the tag pressed handed on, a failed read
said, Escape giving the focus back, the panel closed when the focus leaves it, and an answer to an earlier opening
dropped; `inspector/rich-text.test.ts` its names, a tag at the caret handed on as done, a list turned on from its
button that says so, a link address the sanitiser wouldn't keep, or a "path" another site's (`//`, `/\`), refused and
one kept opening in a new tab with nothing else on it, nothing handed on for a visit without typing, a text set from
outside started afresh so Ctrl+Z can't bring back the last one, its editor back after a move, the toolbar one stop for
the keyboard with its arrow keys and Link saying its panel is open, an inline text as one line without lists or a
paragraph, and the form's text shown again after Undo; `inspector/inspector-model.test.ts` rich text, pickers and "Must
match a pattern" among the items drawn; `inspector/option-editor.test.ts` the content kind's root with Umbraco's
picker, its document types ticked from the site's (one the site no longer has kept to untick), a data type chosen from
the site's lists with none not to be chosen, and a site without any saying so; and `inspector/inspector.test.ts` a tag
put into a heading's text and a default value of its own place, for the element's own text, none without a form; a
consent's wording inline and a paragraph's not, another language's hint the default's words without markup; and a
field's pattern: the first rule written in its place among the checks, kept with its message while its box is emptied
and gone when the box is left empty, a pattern the matcher refuses said with "Try it" waiting, "Try it" answering as
the site would without a change sent and forgotten on another element, and the rule's message.
`MergeTagPathApiTests` holds `POST form/{key}/merge-tag-paths` to `docs/api/merge-tag-paths.json` for a form with
fields, a repeatable group's among them, and the Testing project's site source in `visitorText` and `consentText`, with
labels in German, a culture that is none read as the default texts, a visitor text offered only the answers before its
owner (none for the first field, not its own group's for a child, all for a key the form lacks), an unknown place and
an unreadable draft refused, and a viewer refused; `OptionSourceApiTests` serves the site's page types (no element
type) and its data types that hold values as the choices of the settings that name them, by name; `FieldTypeApiTests`
holds a consent's and a checkbox's text to the `inlineHtml` editor in `docs/api/field-types.json`; `lang.test.ts`
holds every built-in path to an English and a German text. In the browser, `specs/backoffice/designer-editors.spec.ts`
puts a field's tag into a heading through the picker, which offers the name before it and not the email address after
it, and writes a consent's wording as one line with the form's tag put in at the caret, bold words and a link, an
address the sanitiser wouldn't keep refused first; picks an image's picture with Umbraco's media picker, which the
canvas shows; tries a pattern in the mono font against answers in "Try it", after one the matcher refuses, before it is
kept with its message; and gives a dropdown the pages under the demo's home page, picked with Umbraco's content picker,
then only those of the legal pages' type, ticked from the site's, which the canvas lists; each is saved and read back.
The designer persona's group may browse the whole content tree for that (`TestUserGroup.ContentRootAccess`: the root
as its start node and Umbraco's Browse permission, held by `TestPeopleTests`).

Changing a field's type (9.4d, ADR-0174): `change-type/type-change.test.ts` holds, on the changes the server serves
(`docs/api/field-type-changes.json`), what a text with a placeholder, a pattern, its own message and a condition
using `contains` loses as a dropdown and what it needs, the field once changed with everything else as it was, the
settings both types take kept, a narrowed setting carried only where the new type's rules take its value, a lock of
the new type neither carried nor asked for (and named only where it changes the field's value), a required setting
asked for where the field has no value for it, being required taken away with its rules, the conditions on a field
found in every rule and named by what holds them (an operand naming the field none of them), and whether a setting
takes a value, kind by kind; `change-type/change-type.test.ts` the preview in words and the dialog: the types by
palette group in the browser's own select, a refused one not to be chosen and why, the preview of the type chosen,
Change type off until a type is chosen, and Enter; `form-design.view.test.ts` the dialog opened from the canvas with
each type's preview, the change made and said with Undo, Cancel leaving the field, a type the site lacks what it needs
for refused, and a read that fails said; `inspector/inspector.test.ts` Change type by the type's name, once in every
way of showing; and `canvas/canvas.test.ts` Change type... first in the element's "...", and none for content.
`form-workspace.test.ts` holds the read made once (`readOnce`): the same answer for every later ask, and a read that
found nothing made again. In the browser, `specs/backoffice/designer-types.spec.ts` adds a field of every built-in type
from the palette (the captcha placement, which the palette offers only with a provider's keys, from the API) and sets
its help text and one setting of its own in its inspector, axe clean with every section open, then saves and reads
each back; and changes a text field with a placeholder and a condition using `contains` into a dropdown through the
element's "...", after the inspector's Change type and Cancel gave the focus back, with the preview naming the
maximum length, the placeholder, the options it needs and the condition before the change, axe clean, the change
said, and the condition still there in the saved draft. `designer-locks.spec.ts` runs its host with every dropdown
taking several options, three at most: checkboxes that become a dropdown are told that the site sets the most options
instead, neither carried over nor asked for, and the saved field keeps only its fewest.

The rules builder (9.5a, ADR-0175): `rules/rule-draft.test.ts` holds a rule read and written back as it came (nested
condition groups, what else it held), condition groups added (a rule's own conditions becoming its first group) and
nested to the tenth level and no deeper, what a condition may look at (every field a visitor answers, by page, a
dropdown with several answers and a grid's rows with their kinds, the visit's parts with the language always kept for
a rule that has it, never the rule's element or, for show and hide, what it holds, nor a field the visitor never sees),
the operators each kind is offered (every one of the engine's offered for some kind, one a draft holds kept), what a
condition keeps when its question or operator changes (a list's answers that aren't options kept, a count or pattern
not carried into another shape), each reason a row can't be kept, and the first line's effects and clashes;
`rules/rule-words.test.ts` a rule in words, kind by kind, in English and German; `rules/rule-try.test.ts` Try it's
adapter run over every condition and rule case of `tests/vectors/rules.json` (D10), each of those that a draft can
hold read through the draft and its boxes as well, and the boxes and the verdict; `rules/rule-edits.test.ts` a rule
landed, changed, moved in one step, refused where an element would wait on itself, and taken off;
`rules/rule-builder.test.ts` the sidebar: its first line, each kind's value, another answer and a day counted from
today, condition groups and the focus after removing one, refusals said at the row and the first line (a broken rule
opening with them said), a value a condition doesn't take with its remove, a rule moved (and the effect a new element
takes), no condition group inside the tenth level, Try it (and none while a row is incomplete), the visit, Delete, and
the Design view told when the builder is shown and gone;
`rules/rule-data.test.ts` the member groups, read only for a user with the Members section; `rules/rule-names.test.ts`
the names rules are said with (a grid's row by the grid and the row), dates in the backoffice's language, the field
types' facts and a field's own options by their labels;
`inspector/inspector.test.ts` the Rules section's rules in words, a rule that is off said inside its line, Add a rule
beside a closed section's header, and the focus on a rule's line; `form-design.view.test.ts` the builder opened with
what it needs, its answer landed as one step (none for a rule left as it was), a rule moved with the selection, a
delete with Undo, Cancel, a new rule on the first effect the element has no rule for, and the builder opened once when
asked twice; `inspector/inspector-model.test.ts` Rules for a type that can only be required; and
`logic/test/operators.test.ts` the table the builder offers from. In the browser,
`specs/backoffice/designer-rules.spec.ts` builds a rule of groups three levels deep with a condition of every operator,
axe clean, and reads it back from the saved draft; and builds a "required when" rule comparing two fields and a query
value, refused at its empty row first, tried in Try it (required, not required, off), read back, and deleted from its
line with Undo in the toast.

Seeing rules, and validation rules (9.5c, ADR-0178): `rules/rule-list.test.ts` holds an element's rules with where each
is kept (a field's own, then the validation rules on it, a grid's row's on its grid), the pages with the elements that
have rules, the outcomes' (a message as plain text), the chips' words (none for a rule switched off) and a choice's own
options; `packages/logic/test/search.test.ts` (the rule search, moved into `@jfb/logic` in 9.5d) a conflict found by the engine for a hide rule, or a show rule that doesn't
hold, and a require rule (an option, one more than a number, the day around a date, a minute around a time, a day
counted from today, ticks, several choices, files, the visit, two answers compared), only with answers a visitor can
give (no option a choice lacks, a language of the site's, groups only signed in, no text standing for others where a
rule looks inside it), a text the rules don't name said as none of those they do, the answers that don't matter left
out together, and none where no answer makes both hold, a rule is off or the engine refuses it;
`rules/rules-overview.test.ts` the sidebar: the rules by page and After sending, a rule that is off marked, the
conflict on its row (none of the texts named, files by how many), a row's and an outcome's way out, Add rule asking what
the rule affects (only what can take a new rule, refused with nothing chosen, Cancel back to Add rule), and the empty
sentence; `rules/rule-builder.test.ts`, `rule-draft.test.ts`, `rule-edits.test.ts`, `rule-words.test.ts` and
`rule-try.test.ts` a validation rule as a rule that shows a message (offered only for a field whose answer rules compare
and the visitor gives, its own field looked at, the message asked for at its box and written in the language shown, Try
it's words, its landing among the form's validation rules, a grid's row's kept on its row, moved to and from an element
in one step), and a rule whose element can no longer have its effect opening with it shown and refused until changed;
`editor-model.test.ts` a delete taking the validation rules on what it deletes along and keeping those that only look
at it; `Tests.Unit/Definitions/StructureTests` a validation rule on a name, a repeatable group or a value kept on the
server refused; `canvas/canvas.test.ts` a chip per rule in use on a page, a group and a field, each describing the
element's button, none while it is selected; `inspector/inspector.test.ts` a field's validation rules among its rules;
and `form-design.view.test.ts` a validation rule landed, opened and deleted with Undo, and the overview's way to an
element, to the builder and to Settings. In the browser,
`specs/backoffice/designer-rules-overview.spec.ts` finds the chips on the elements rules affect and none on the selected
one, opens the overview from the canvas's "...", finds the conflict on its row (axe clean), follows a row to its
element, adds a rule from the overview after choosing what it affects, finds the empty overview's sentence, and builds a
validation rule with its field and message whose message the published form, shown through the element on a page of
the site's origin, shows at that field when the rule matches, and sends once it doesn't.

The form check on the server (9.5b, ADR-0177): `Tests.Unit/Forms/FormCheckTests` holds every text where merge tags
render with its place, context, owner and path (titles and descriptions, a field's texts, a default value and one kept
on the server, a consent's wording, a heading's and a paragraph's text, a test type's nested setting, an outcome's
message and redirect, the entry title; no action's template, which is its type's); the merge tags' errors and warnings
at their paths, and a redirect that recalls answers (a field's value or the entry's answers, not a field's label or the
entry's reference); a field visitors see without a label an error, content, a value kept on the server, a consent and a
calculation no visitor sees not; a captcha provider without its keys a warning, one with keys or one the site lacks
not; an email action on a form without an email field a warning, and on a form with one (a repeatable group's too) a
hint where a mail to the site's people names no field to reply to; an action type's own check, its refusals errors and
its warnings warnings at their settings, never run over settings its schema refuses; the check's errors (the publish checks' first), then warnings, then hints, at
most a hundred of each; and the form as the site runs it, a host's lock applied. The runtime tests that publish tags
their places refuse (`Support/RefusedTags.cs`) leave the merge-tag check out, as for a form published before it.
`Tests.Integration/ManagementApi/FormCheckApiTests` checks the unsaved draft through `POST form/{key}/check` (an error,
a warning and a test site's hint, each at its path; a duplicate alias and a rule on a removed field among its errors;
nothing saved; a definition it cannot read refused) and publishes
through the API: refused at the label's path, then live with a warning only. `EmailActionTests` and
`ContentActionTests` publish templates whose tags name nothing and find the form check's warnings at their settings,
and `ContentActionTests` find a repeatable group's field mapped, and a sensitive field the form lacks, refused by
their codes.

The checks of the form's rules (9.5d, ADR-0179): `tests/vectors/search.json` holds the rule search's cases, which the
Vitest runner runs through `search.ts` of `@jfb/logic` and the NUnit runner through Core's `RuleSearch`
(`Tests.Unit/Logic/SearchVector.cs`): conflicts by their hiding rule and answers (an option, a show rule not holding,
none for other answers, a rule off or refused, a number, a day, a minute, days from today, the visit, member groups
only signed in, two answers compared, a text the rules don't name, a text another looks inside, answers free together,
files by count, a group's and a page's rule; with a form, none for an answer it hides, its own field, a repeatable
group's child in its row or a grid's row with its grid, none where the engine refuses the form, and one for an answer it
shows; one tried next to 1.00001 that each runtime writes alike) and whether an element shows, never shows or can't be
told (an option the choice lacks, two options of one choice, a field not yet answered, hidden whatever the answer, a
language the site lacks, a day before today, a number only tested for being equal, files and none; between two numbers
or two minutes, a text looked inside, four ticks, at most two ticked, days from today, a refused rule, a repeatable
group's child, a query value, a day compared with another, a number too large to step from, more sets than tried, more
options than tried together, member groups tested for being empty). `packages/logic/test/search.test.ts` adds the
search's own cases and what a form hides for a set of answers (`formSight`: with what holds it, in its row, with its
grid; nothing it can tell for a refused form or one with calculations and no calculation language).
`Tests.Unit/Forms/LogicFormCheckTests` holds a require rule holding while its own, its group's, its page's or its
repeatable group's rule hides its field a warning at the rule, none where the form hides the answer that hides it, a
field required while shown or for other answers none; a page shown only by a later answer, grid row or repeatable
group's child, in a language the site lacks or for an answer no option is a warning, one an earlier answer shows, one a
later default value, prefill or calculation answers, one two rows of a repeatable group may answer, one a choice taking
any text may answer or one the answers tried cannot speak for none; and what the scope tries: a choice's own options
only (none for a library list, a cascading list or "Other"), a grid's for its rows, the site's languages, the site's day
(in UTC where its zone is unknown), a repeatable group's children as several answers. The designer's tests add the
overview saying no conflict for an answer the form hides, `ownOptionValues` knowing none for the same choices as the
server, and `logicOf` taking the rules model's kinds. `FormCheckApiTests` finds both warnings in the draft check and has
a publish refused through the API for conditions a change of type left.

The checks of the site and of what a form collects (9.5e, ADR-0180): `Tests.Unit/Forms/CollectedDataFormCheckTests`
holds personal data on a form without a consent field a hint at the first field that asks for it (a type that is
personal by nature, a site's own type that declares it, a repeatable group's child, an autocomplete token after its
section and kinds, the parts of a local number; none for a topic, a country or a count, or where a consent field sits
anywhere); a card number asked for a warning from its token, its label or placeholder in any culture (compounds and
accented words in each visitor language, "CVV", "Card number" as the whole label) or a pattern that takes sixteen
digits, plain, spaced or dashed, and refuses twelve, none for a membership card's number, a card type, a member number,
a phone pattern or a checkbox; a password from its token or a word in the visitor languages, never from a help text; a
label with a noncharacter read without throwing; a file field without a size a hint naming the site's limit, none where
the host locks the size; a required choice whose options all have places a warning (its own, required by a rule
switched on, a library list holding them, a cascading choice's), none with one unlimited, without a requirement, with
"Other", or for a list that is open or gone; a form that keeps no entries warning about its entry limit, one entry per
member, places (a library list's too) and unique answers, a field kept until the actions have run about its own, and a
form that keeps entries for days about none; and a last outcome's rule a warning unless it is switched off or matches
every answer. `Tests.Unit/Forms/SiteFormCheckTests` holds a theme the site lacks in code and in the library a warning,
a built-in one and a library one not; a redirect to a page in the tree but not published, or gone, a warning; content
and media that are gone in an image's setting, an option's image and an action type's page, folder, list of pages and
nested image;
member groups by key, and in the rules by key or exact name (a name that differs in letter case said with the site's
spelling) in nested groups too, the groups never asked for by a form that names none; every localised text with its
path; and a dictionary text without a text a warning at the culture's text for the languages whose visitors see it,
through a culture's parent and a language's fallback, read once per language, a setting's single text left to the
settings check, nothing read for a form without one. `FormCheckTests` adds a host's lock replacing a stored value a warning at the setting, for a field, a
repeatable group's child and an action, none for a value equal to the lock's or under a hidden lock, and a label's
merge tag of a source labels refuse an error. `FormCheckApiTests` finds these through the draft check, publishes such a
form, and has a publish refused for that label; `FormCopyServiceTests` keeps a built-in theme on import.

The form check in the designer (9.5f, ADR-0181): `FormCheckTests` holds a form without a field to fill in an error at
the end of its first page (a heading alone too), none with a field, a repeatable group's, one of a type the site lacks
or a form without pages. The designer's Vitest tests hold the server's paths read step by step (names dotted and
bracketed with their escapes, indexes, none it does not write) and each place's Fix it (`check/form-check.test.ts`: a
field's texts, switches, alias, options, messages, checks by kind and settings, nested ones too; a repeatable group's
child; an element's rule and a validation rule's field or grid; a group's and a page's items; the palette for a page or
group without fields and a form without pages; the Settings and Actions views; nothing for the whole form or a place
gone); when the check asks (`check/form-checker.test.ts`: at once for the form read, once after a run of changes, the
last ask's answer only, the same form not again, again after no answer, an ask that fails as none, nothing after a
reset or a stop); each problem's words (`check/check-words.test.ts`: the element's name, a merge tag's text by its
place, a setting by its label, a theme, a member group by name or key, a dictionary key, a type by where it is, a
page's number, kept limits by the form's storage, the server's sentence for the six codes whose words it has and one it
doesn't know, every own code said without a code or a gap); the sidebar (`check/form-check.modal.test.ts`), the
publish dialog (`publish.modal.test.ts`: no problems once, errors blocking with Fix it, warnings not, five of each,
Publish waiting for the answer and let through without one), the badge on the canvas, the inspector's item and its
first box (`inspector/item-focus.test.ts`) and the Design view's Fix it to a setting's box in a section opened for it,
the rules builder, the palette, the views and a Fix it from the publish dialog. `lang.test.ts` reads every code from the
server's sources and fails for one without the designer's sentence, a place among the server's words or among the codes
the check never sends. `e2e/specs/backoffice/designer-form-check.spec.ts` counts a form's problems on the badge, lists
them errors first with axe finding nothing on the list, follows Fix it to a label's box and to the rules builder, and
has a blank form's error open the palette, block Publish with Fix it, and a warning alone publish.

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
| `PublicApiPendingMigrationsTests` | Every endpoint (validate and receipt among them), and an address that names none, answers `503 migrations_pending` with `Retry-After: 10` while the plan has not run, an endpoint that takes a body sent one it takes and storing no file, and normally once it ran. |
| `PublicApiTimeLimitTests` | The time limit on the site's clock (`ManualTimers`): the request answers `504 timeout` once the test fires it, the read it waited for goes on, and the next request gets its answer from that one read; a limit that runs out after the action built its answer, before it is written, still lets the answer arrive (ADR-0193). |
| `PublicFormApiTests` | The form endpoint: a headless form by its key and its alias in any letter case; the same `404 form_not_found` for a form that is missing, unpublished, in the trash or not served to headless sites; the texts in the culture with the dictionary's items and rich text sanitised; an `ETag` that follows the answer, and `304` for a client that holds it; `open` from the opening time until the closing time; each field's kinds, whether it can be required and whether its type is built in (`fields`), and the culture's `formats` and `words` on a form whose texts hold no tag, whose `mergeTags` is null (ADR-0109). |
| `PublicFormCacheTests` | The published forms each server keeps: every change of the form service drops the form and tells the other servers (`RecordingServerMessenger`), a folder's trash or restore drops them all and its delete each form it removes, another server's instruction drops it here and so does a refresh of everything, misses that come together share one read, nothing is kept for a key or alias that names no form, and a published version this server cannot read is answered as missing, logged once until it changes (`LogCapture`). |
| `PublicFormSiteTests` | What the answer takes from the site: the time zone of the page's site or the global one, UTC with one warning for a stored zone this server cannot resolve, and of every settings section filled in full only the time zone, and in the visitor's form the site's required-marker style and the button labels the form leaves empty (a guard fails for a section or field the test does not fill). |
| `PublicFormLockTests` | A designer lock added after the publish: the locked value is served under another `ETag` than the answer without the lock, and a submission between two starts is held to the lock. |
| `PublicFormRecallTests` | Answer recall from the answer alone (ADR-0085): the texts that render, with the server's renderings for fixed answers on a page of a site, written with the answer to the reviewed fixture `fixtures/public-api/recall.json` (keys numbered in order, the culture's formats left out of the comparison; rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), which `packages/logic/test/recall-endpoint.test.ts` renders with `recallAnswers` and the answer's `formats` and `words` to the same texts; the fields' kinds on a form whose texts hold tags; the texts left out render as written; the recall form holds the owners, the recalled fields, a repeatable group whole and a field after the text, and names no field as sensitive. |
| `PublicFormConsentTests` | A consent's wording from a dictionary item, rendered and sanitised, with the hash of what it shows, and another hash once the item changes. |
| `PublicFormOptionsTests` | The options endpoint (ADR-0086) with sources of the site's own that note what they are asked for: a field's own options and a source's in the culture, a missing label as the value, a description and an image only where an option has them, the page as the request names it, a repeatable group's field; a parent answer the parent lacks giving the options of no answer under the same `ETag`, the source never seeing it, an answer cleaned and given once; three levels each held to its parent's held answers, every level by its key, a field outside the chain not read; a failing source and a field it filters `available: false`; `no-cache` and `304` for a weakly held tag; `404 form_not_found` as the form endpoint answers it but for a form not served headless, `404 not_found` for a field without options, `400 invalid_request` for a culture, a page or a `parent.` name it cannot read. |
| `PublicFormUploadTests` | The upload endpoint (ADR-0087) on a site whose limit for any file is 60 MiB and which blocks its own extensions, with a middleware that counts the bytes the server reads of each request: a file stored for its field with a row that waits for its entry, its hash and detected content type, the name cut to the device path's last segment, the bytes under a random id in its folder without the name's extension, and a token that names the row but not its bytes; a challenge first, a repeatable group's field, a signature and an archive taken; a 60 MiB file to a 50 MiB field cut off at its limit with `413 fileSize` before the client sent it all, nothing stored, recorded or buffered (a watch of `ASPNETCORE_TEMP`, proven by a buffering of its own, and under a quarter of the bytes read allocated); a file of exactly 50 MiB stored; a declared length past the field's limit or the largest upload refused before a byte is read; a renamed 20 MiB program, with its length and chunked, refused on its first bytes, and every other kind a field does not take with its kind of error (`fileType`, a signature's `invalid`), a type group's extension the site blocks among them; every request it cannot take refused with its code and where, closing the connection; publishing held to the site's limit and blocked extensions; an upload to a form that is closed or full refused with the code its submission would get, before a byte is read. |
| `PublicFormEntryTests` | The entries endpoint (ADR-0088): a submission stored in one unit with its event, its count, its action runs and its search text (a sensitive answer not searchable), answered with its receipt and outcome; an answer kept until the actions run not stored when none can run, and stored while one may still run (once the address is confirmed, on approval, on rejection while the entry waits for approval); an entry from a page published in English only storing no path for a Danish submission; a retry answered with the same receipt, also once the form is full or closed, and other answers under its id `409 duplicate_submission`; a revision the answers do not fit `409 form_changed`, an older one they fit read by the current fields; a closed form, a form for members, one in the trash and a submission without an answer (empty parts among them) refused with their codes, storing nothing; a consent posted with the hash of a wording that changed `409 form_changed`; a redirect that renders to another site not followed, the default message given and a warning logged by the outcome's key without the answer or the address; a redirect to a page leading to its address, and one stored to nowhere or to a page unpublished since not followed, each with a warning; a message from a dictionary item in the culture, rendered and sanitised; the default message when no outcome's rule applies; a message that goes on with its address rendered and its delay, `0` included, and `showAnswers` only when it shows them. |
| `PublicFormEntryStoreTests` | The endpoint against what the store holds a moment later or another way, through the site's entry, form and event repositories wrapped so a test answers one call as the store would (`Intercepted`): a submission stored a moment earlier answered as its retry, or `409 duplicate_submission` for other answers and once it is gone; the last place taken a moment earlier refused inside the unit with `403 entry_limit_reached`, the form's counter rolled back; an upload another entry took between the read and the unit failing at its field with `invalid`; a form trashed past the cache, or whose row is gone when the unit stores, `404 form_not_found`; a retry rendered again from the stored context, and with the default message for an entry stored without its event; an older revision this server cannot read `409 form_changed`; the pipeline called without a token reader taking no upload. |
| `PublicFormEntryAnswerTests` | How the endpoint reads, rules and checks the answers (ADR-0062, ADR-0088): each error where it is, with the check or the rule whose message shows (a grid's row and a repeatable group's row among them); an answer longer than its type takes failing at its field before any rule or pattern runs over it; a number with an exponent stored and compared as its value; a hidden field's unreadable answer failing nothing; a calculation too long for a number stored as text only; a rule's requirement on a name, the site's today in its time zone, an upload's name reaching the checks, and the site's clock closing the form. |
| `PublicFormEntryBodyTests` | The body (ADR-0012, ADR-0088), with a middleware that counts the bytes the server reads of each request: a lone surrogate, bytes that are not UTF-8, no JSON, an array, more than 64 levels, a revision missing or wrong, a member twice, an unknown member or alias, an answer of the wrong shape, rows for a field that holds none or an unknown alias, an unknown alias in a row and a value past its limit refused with where they are, never with `500`, storing nothing; another media type `415`; a declared length past 1 MiB refused before a byte is read and a chunked one just past it, each `413` with the connection closed; a body of exactly the limit stored, with its length and chunked; the server's own refusal of a body, past its size `413` and malformed `400` at `body`, each closing the connection. |
| `PublicFormPlacesTests` | The places on options (ADR-0090), with the site's entry repository wrapped (`Intercepted`): fifty submissions that all read the places left before any is stored get exactly the last ten, the other forty `422 validation_failed` with `full` at the field, storing nothing and taking no number, and the count equal to the entries' values; a rejected entry, one marked as spam and a deleted one giving their place back, one moved back taking it again over the limit and edits moving it, while a retry gets its receipt once the option is full; each repeater row and each choice of several taking a place, the choices past the places failing at their paths, the first rows getting the places left, among the other errors in the order of the form (a grid row's and a group's children row by row among them), every choice held and a field failing once, and an answer that fails taking no place; a library list's places counting as a field's own and changing with the list; places set after entries came in counting them; and validate answering `full` and taking nothing. |
| `PublicFormUniqueTests` | The unique answers (ADR-0091), with the site's entry repository wrapped (`Intercepted`): ten submissions with the same answer that all read the answers taken before any is stored store exactly one, the other nine `422 validation_failed` with the check's index at the field, storing nothing and taking no number; two at once that also want the last place fail at the place alone, the answer checked once the rest passes; an answer that differs only in letter case or surrounding spaces, and a number by its value, taken, failing each unique check of its field, while an answer that fails its pattern, a field without the check and an unanswered field hold nothing; a retry getting its receipt, and an entry marked as spam, rejected or deleted giving its answer back; a repeatable group's child failing at its row in the order of the form once the other answers pass, and one entry repeating an answer in two rows; a post with an empty required field answering alike whether its address is taken or free, the required error alone, storing nothing (6.8a); and validate passing an answer another entry holds while the other checks still run. |
| `PublicFormValidateTests` | The validate endpoint (ADR-0089): a page whose answers pass `204` with `no-store`, storing, counting and claiming nothing, so the upload it was checked with is still the submission's; a page's errors only at its own fields, by alias, a repeatable group's child and a grid row among them, required by a rule over an earlier page's answer and over a later one's; a page key that names no page `400 invalid_request` at `pageKey` for the current revision and `409 form_changed` for an older one, whose page is checked against the live version (a field it now asks for `form_changed`, a page gone since or added since checked by what is left); a form before its opening time, a full form, a form for members, in the trash, trashed past the cache or missing, a newer revision, a consent hash other than the wording's and an alias the form lacks refused with their codes, the consent's own hash passing, and a page without an answer no error. |
| `PublicServerKeyTests` | Server keys (ADR-0126): a key submits, in any letter case of the scheme, and the entry records its name as `jfbServerKey`, never a posted one; a request without the scheme is an ordinary one; a revoked, short, space-holding or lone-scheme key answers `401 server_key_invalid` with `WWW-Authenticate: Jfb-Key` on a post and a read and stores nothing; the receipt answers the key that submitted and `404 not_found` to another and to a request without a key, an entry no key made answering only a request without one, and another key's retry of the same submission is `409 duplicate_submission`; neither a key nor a hash reaches a log or an answer; a key changed while the site runs (a change token of the host's own, as a reload of appsettings gives one, since a value set on the site's configuration would reach the configuration the harness shares) takes effect only at the next start. `PublicServerKeyStartTests` boots a site whose key holds the key in place of its hash: the start-up checks refuse it, naming the setting and never the value. `ServerKeysTests` (unit) holds the header's reading, every configured hash compared whichever matched, a hash of another length never matching, the partition a signed request counts in, and the start-up check of names and hashes; `scripts/tests/server-key.test.mjs` the key `scripts/server-key.mjs` makes and its hash. |
| `PublicFormPreviewTests` | The preview frame's requests (ADR-0203): from the origin `null` without a ticket `403 origin_denied` as before, while a preflight from it may send `Authorization`; a ticket that is malformed, the scheme alone, another form's, expired (ten minutes to the second, on the test clock) or sent to the drafts, resuming one, the render or the form by its alias answers `401 preview_ticket_invalid` with `WWW-Authenticate: Jfb-Preview` and `Access-Control-Allow-Origin: null`; a ticket that holds reads the version it names, a draft never published that visitors get no challenge for, and not a save made after it, and its page check that version's aliases though another tab published a renamed field meanwhile; a published form's ticket reads the draft's options; a form for members takes the ticket as a test member, with no member and no puzzle. |
| `PublicFormReceiptTests` | The receipt endpoint (ADR-0089): the receipt and outcome of a submission id, byte for byte what the entries endpoint answered (a message with merge tags of the answers and the context, its address, delay and copy of the answers), for an id posted and one the server made, also once the form is full; an entry in Spam or rejected answered as any other, the outcome the same; an unknown id, another form's entry and a draft the same `404 not_found`, body for body; a form for members `403 member_required`, and a form in the trash, trashed past the cache, missing or named by no key `404 form_not_found`. |
| `PublicFormValidateReceiptBodyTests` | The bodies of validate and receipt (ADR-0012, ADR-0089), with the middleware that counts the bytes the server reads: a lone surrogate in an answer, a key or a challenge, bytes that are not UTF-8, a page key missing or no UUID, a submission id or a captcha token sent to validate, a revision missing, a submission id missing or null, answers or a challenge too long sent to receipt, an array and no JSON refused with where they are, never with `500`; for each endpoint another media type `415`, a declared length past its limit (1 MiB, 16 KiB) refused before a byte is read and a chunked one just past it, each `413` with the connection closed, and a body of exactly the limit answered. |
| `PublicFormTestEntryTests` | The test entries the preview frame sends (ADR-0204): a ticket's submission is a test entry that sets off nothing (no action queued, no verification mail, so it arrives as confirmed, no notification for Umbraco's webhooks) and counts nowhere (the statistics, the entry limit; places and unique answers leave out what `EntryLimits.Counts` leaves out, as the repository tests hold); with a ticket whose test entries run their actions it sets off what a visitor's entry does and still counts nowhere, and one waiting for its address to be confirmed says so; Run actions runs a test entry's actions once and finds no visitor's entry; on a form that stores no entries one whose actions wait is kept and one with none to wait for goes; the ticket stands in for the puzzle and the spam score (`PublicFormCaptchaTests`: for the captcha too); uploads and the receipt take the ticket, a draft's field taking a file, and once the form is published a visitor's receipt for the test entry's submission answers `404 not_found`; a field the draft stores otherwise than its published version keeps no value, and a catalogue row another submission added a moment before (a stale read, through a proxy of the repository) stops none. |
| `PublicOpenApiSnapshotTests` | The document `jfb-public` against `docs/api/jfb-public.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), its paths and operation names, its own schemas only, and for every operation and action the rate-limit policy, body limit and time limit with the frame's refusals. |

`Tests.Unit/PublicApi/` covers the frame without a host: the middleware only below the base path, the headers every
answer carries, the CORS answer for each kind of origin and method, a preview's ticket admitting the origin `null` alone, with whether its test entries run their actions, and the answers to what ends a request early
(`FormBuilderPublicApiMiddlewareTests`); the preview's tickets, a flag no ticket sets or the first layout read as nothing (`PreviewTicketsTests`); the filter with the body limit before a byte is read, the time limit and an answer built written past it
(ADR-0193), the pending migrations, the declarations of an action without any, the largest upload from the host's limits and the server's own limit set to the endpoint's (`FormBuilderPublicApiFilterTests`); and the body of the entries endpoint, of a page and of a receipt (`EntryBodiesTests`, ADR-0088, ADR-0089): every shape of answer by alias, parts as `alias.part`, a repeatable group's rows, consents, query and context values, the optional members left out or null, and every value it cannot read named once by its path (names that do not split, rows and texts under one alias, a consent's hash beside a value of its wording part among them), the challenge's and the token's limits, bytes that are not UTF-8 and a lone surrogate among them, in an answer, a name and a key; a page's body as a submission's with its page key and without a submission id or a captcha token, and a receipt's naming its submission id, each value it cannot read named by its path. Beside them,
`Configuration/PublicApiRulesTests` holds the base paths a host may choose, the origins as browsers write them and the
start-up check's messages; `Definitions/EditorHtmlTests` the one sanitiser; `Definitions/VisitorFormTests` the
visitor's form made from the full fixture, every text of it a string and nothing else changed;
`Forms/VisitorFormsTests` the site's required-marker style for `inherit` and its labels for the buttons a form does
not label itself (a stored label that is no localised text none), the culture, the dictionary read once per answer, a lone surrogate replaced, each consent's
wording with its hash (none for one that renders as nothing), `mergeTags` null when no text renders or with its
values and recall form when one does, and on every form the culture's formats and words and each field's kinds,
whether it can be required and whether its type is built in, a site's own type's and a child field's too; and
`Settings/SiteTimeZonesTests` the zone per site with the warning at most once an hour per site and zone, within its
bound.

## The rendering tests

`Tests.Unit/Rendering/` covers the website assets without a host, over files of the test's own
([ADR-0092](../docs/decisions/0092-serving-the-website-assets.md)), and the parts of the form rendering
([ADR-0093](../docs/decisions/0093-the-razor-rendering.md),
[ADR-0095](../docs/decisions/0095-visitor-texts-members-only-forms-and-the-pending-note.md)):

| Fixture | Holds |
| --- | --- |
| `WebsiteAssetsTests` | The catalogue: only the originals are names, each with its Brotli and gzip copies (a name ending in `.br` without its original is an original); no other spelling finds one (case, `%2F`, dot segments, doubled or back slashes, a leading or trailing slash); the media type by the extension as written; the fingerprint of the original, not of a copy; a name in a `chunks/` or `assets/` folder named by its content, a file named like those folders not; the copy for each `Accept-Encoding` (Brotli, then gzip, then the original; a quality of zero refuses, `*` stands for the codings not named, codings in any case) with its own entity tag, never a copy the build did not make; the address below the path base with `?v=`, and a missing asset's message saying to build the client. |
| `WebsiteAssetsMiddlewareTests` | The middleware: a request outside `/jfb/assets/` passes untouched; an unknown name, a copy's name, another spelling or a traversing path answers `404` with no body and `nosniff`, and is not passed on; the base path in any casing; another method `405` with `Allow: GET, HEAD`; each copy with its type, length, coding, `Vary`, entity tag, `nosniff`, CORS for any origin without credentials and `Cross-Origin-Resource-Policy`; `HEAD` without the body; a year's caching only under exactly one current fingerprint or for a name of content; `If-None-Match` with the copy's tag, its weak form, one of a list or `*` answering `304` without the body's headers. |
| `FormBuilderAssetsTagHelperTests` | `<jfb-assets>` run as Razor runs it: the module script under the fingerprint and the path base without the element itself, written once per request however often it is named (each request its own), never its children, the nonce of the attribute before the provider's, the provider's for this request without one, none when both are empty, and the nonce written as an attribute value; the tags a form adds by itself written only when the page writes them, so a tag written before them wins, and with the provider's nonce when they come first. |
| `FormBuilderRenderingServicesTests` | The registrations made twice add each service once, the members check per request and the rest as singletons, and one pipeline filter before Umbraco's routing. |
| `FormBuilderFormViewComponentTests` | The view component's result with the assets: the assets into the page's writer before the form, run synchronously or not, and refused without a context. |
| `FormViewsTests` | Which view writes each part over a view engine that knows the test's paths: the form's override, else the default view, for each part and control; a part without a view failing with the paths looked at; without a form, the default folder's view only, one it lacks naming its path; in Development, a site's own view whose header names an older version, or none, logged once, the current or a newer one not, the built-in views never read as files, and nothing read outside Development. |
| `FormRenderingTests` | What renders before a form is looked for: nothing and nothing asked without a reference; a value of another type logged once per type; nothing for a visitor, and no form read, while the migrations are pending. |
| `BackOfficeVisitorsTests` | Whether a request comes from a signed-in backoffice user: a backoffice ticket of the scheme's format, unexpired (one without an expiry too, one at its expiry not) and holding a backoffice identity; a large cookie in chunks joined; another key ring's ticket, no ticket, a cookie of another name, a member's identity and a scheme without a cookie name never. |
| `FormMembersTests` | Whom a form admits: anyone without asking who is signed in; a form for members any member, a visitor asked to sign in; a form for groups a member of one by name in any letter case, another member not, a visitor asked to sign in first; nobody for groups that are none or gone. |
| `RenderingPartsTests` | The model data's escapes, which read back as the same answer; the attribute helpers (a flag bare or nothing, a value encoded or nothing, a name required, the ids a description names with the missing ones left out); the instance ids of each request; a post's state found only for its form and instance; the package's words by culture, its parent or English, each with its placeholders and the German umlauts; and the form reference reading as its key. |

`Tests.Integration/Rendering/WebsiteAssetTests` serves the assets on a real site whose migration plan has not run:
every file the client build wrote under `src/Jiron.Umbraco.FormBuilder.Web/WebsiteAssets/` is in the assembly with
both copies, each copy decompresses to its original, each fingerprint is its original's, and each copy goes out with
the same bytes and every header, cached for a year under `?v=` and revalidated without it; an unknown, differently
cased, copy's or `%2F` name answers `404` without a page, as does a traversing path handed to the server as it is;
the base path in another casing serves the same file; `<jfb-assets>` with the site's services writes the address
that is served, once per request, with the nonce of the site's provider or none; and the composer adds the
middleware once.

`Tests.Integration/Rendering/RenderingTestBase` renders forms on real Umbraco pages: a route-hijacking controller
shows a view of the test project (`Views/RenderingTests/`, compiled into the test assembly, which joins MVC's
application parts) for each published page of a test page type, and a test controller signs a member in, or the
administrator into the backoffice (its cookie is secure, so those pages are asked for over `https`). A second site
answers on hosts of its own, for settings per site. The test server sends no cache messages, so the base rebuilds the navigation, publish status and URLs after publishing, and a
test refreshes the domain cache after assigning a culture.

| Fixture | Holds |
| --- | --- |
| `FormRenderingTests` | A form through `<jfb-render>` and through `Component.InvokeAsync` with the hooks of ADR-0015, the model data equal to the form endpoint's answer and the assets once; two forms under `jfb-1` and `jfb-2` with the assets before the first; a `<jfb-assets>` in the layout's head winning; no assets when turned off; a form's own override view in place of the default for that form only; a form with a file field posting `multipart/form-data`, its control from its own override folder; listed context values as the page's code gives them, an expression or text with one, under the name the form lists, in its letter case, and an unlisted or too long one left out and logged once each; a hidden field from the signed-in member never in the markup; a form named by key, reference, key text and padded alias, other values rendering nothing and a type logged once; a reference that names no form rendering nothing, the page going on, logged once per reference. |
| `FormRenderingAssetsOffTests` | A site with `Rendering:IncludeAssetsAutomatically` off (§4.8, ADR-0093): the site starts, a form through `<jfb-render>` or `Component.InvokeAsync` writes no assets, one whose placement asks for them (`assets="true"`, `assets = true`) writes them once before the form, and a `<jfb-assets>` in the layout's head still writes them (6.8a). |
| `FormPostTests` | The post without JavaScript (ADR-0100 to ADR-0103, `post*.cshtml`): a valid post stored as the runtime's post stores the same answers and redirected to its outcome; an invalid one shown again with its answers and errors; a taken unique answer told only to a post that would be stored, a taken and a free address answering alike beside an empty required field (6.8a); Next and Back through three pages beside another form; a changed consent unticked with a notice; each outcome; a sent token that names nothing; another origin's post `403` to a form for members and, while a member is signed in, to any form, a post without an `Origin` and an anonymous one passing (`.Prefill`, 6.8a); the surface action's auto-route; files (`.Files`): kept as a ticked box, too large or of the wrong kind at their field, past the drain allowance with a notice, and none stored for a form that filled up or closed meanwhile, whose notice the page shows (6.8a); and rows, moves, signatures, defaults, the summary and the copy in the other partial files, each below. |
| `FormRenderingViewTests` | The built-in views: the controls of the text, number and content types with their attributes, affixes and counters (a number with places or one that may go below zero keeping the full keyboard), groups as fieldsets or not, a default with a merge tag left empty, a sanitised text block, an image from the media library with its size, and nothing for an item gone or without a file, and the state the rules give the defaults (hidden and disabled, required, a hidden page with its fields and no step), with the progress, the asterisks and their note, and the model data; the notices of a form closed, closed with its own message and not open yet; a post's answers, errors, summary, notice, page and the form's own button labels, and its errors inline only without the summary, a form of one page without progress; the outcome after a post; a German site's culture with its model data and its required field unmarked under the "(optional)" style; and an Arabic site right to left with the English words. |
| `FormRenderingTextsTests` | Visitor texts with tags (ADR-0095): after a post, a label that recalls an earlier answer equal to what the merge-tag service renders for the same answers, with a page's title, a progress step, a legend, a help text with a sensitive answer masked, a placeholder and a heading recalling in their places, a hidden or later field recalling nothing and HTML escaped, an answer longer than its type takes recalling nothing and a help that renders as nothing leaving no help; on the first render the defaults recalled and the model data keeping the templates for the runtime; a site's `required` style and Send label on that site only, a form's own style and label winning, the endpoint's answer for the site's page equal to the model data, and the other site and the endpoint's answer without a page keeping the defaults. |
| `FormRenderingAccessTests` | Forms for members (ADR-0095): a visitor not signed in sees the notice in the page's culture (German), a form for anyone beside it renders; a signed-in member sees a form for members and one for their group, the groups' notice for other groups; a form whose group is gone admits no member, and a visitor is asked to sign in; a closed form for members says it is closed rather than asking to sign in. |
| `FormRenderingChoicesTests` | The choice controls (ADR-0096), every choice type empty, answered after a post, with errors and hidden by a rule (`choices.cshtml`): a radio in a list, a row and as cards (the form's layout, with an option's image and description), checkboxes with "Other", a dropdown with one answer and a list box, yes or no as buttons and as a switch, a scale, a rating, a grid with a required row, a ranking with its moves and the copy of the default button, a checkbox, a consent with the form endpoint's hash and a country; each with ADR-0015's hooks (a fieldset and legend, an id per option, the name repeated, the "Other" text, a grid row's names). Options from a library list, from code sources and below an answered parent (`options.cshtml`, a post's answer or the home country's default) equal to the `options` endpoint's answer for the same answers, and a source that fails saying so; one box that needs no answer unmarked, as the options form's own view (`_forms/renderingOptions`) sees it. Under the asterisk style (`rare.cshtml`): a grid on a scale with a required row's asterisk and a row without a label numbered, a required box's asterisk, a required switch unmarked as the form's own view (`_forms/renderingRare`) sees it, a parent a rule hides filtering nothing, an option without a label showing its value, and a radio's "Other" text not chosen beside an option; and the word "required" beside the asterisk for screen readers on a required group of boxes, a grid's required row of boxes and a required ranking, none on a required radio group or a grid's row of radio buttons (6.8c). After errors, a group's choices carry `aria-invalid` and the error's id after an option's description, never its fieldset, a ranking's fieldset names its error, and an error of one grid row marks that row's choices alone. |
| `FormRenderingInputsTests` | The part, date and output controls (ADR-0097), each type empty, answered after a post, with errors and hidden by a rule (`inputs.cshtml`, the site in Auckland at half past midnight): a date starting on the site's today with its window from today to two weeks on, a time and a date and time with their windows and steps, a slider starting in its middle with its unit, a calculation worked out for the slider, a name, an address whose parts it does not need are marked "(optional)", a file field with the extensions it takes, a signature typed instead, and the captcha's place in a hidden group; each with ADR-0015's hooks (a fieldset and legend for parts, an id per control and part, `<alias>.<part>` names, a label for every control). Two calculations (`sums.cshtml`) equal to what the entries endpoint stores for the same answers, an empty number counting as zero and an empty date leaving none. Under the asterisk style (`parts.cshtml`): a name's required parts in the type's order and an address's needed ones marked, a name that needs no answer requiring no part, a required name that needs every part marked once on its legend, the error on the needed parts that are empty or on every part when all are given, a date and time starting now on its step, a step that does not divide the day left out, a limit beyond the calendar setting none, a slider that shows no value, files of several kinds with their own extensions after their groups' and each once, and a calculation visitors do not see and a captcha's place without a captcha rendering nothing. The page's answers (`rules.cshtml`): a rule reading a grid row's answer, and a hidden field's posted answer, prefilled from the query or not, shown nowhere. A site's own type (`MoodFieldType`, `custom.cshtml`) rendering its control from its own view (`fields/acme.mood.cshtml`) with its settings, a prompt recalling an answer and one without tags as written, and its answer's part. |
| `FormRenderingRowsTests` | A repeatable group's rows (ADR-0098, `rows.cshtml`, `rowsde.cshtml`): a first render's least rows, each a fieldset numbered after its row label, with its fields' keys, paths, ids and names and their defaults, the group's columns, Add with its label and no Remove at the least, a group without a least starting with Add alone, and the default button first; a post's rows with their answers, Remove naming its row, no Add at the most, a calculation and rules that hide and require over each row's own answers, a row's options filtered by a parent in its row and by one outside the group (an answer for that parent posted in a row changing nothing), a row's error at its field and in the summary, and a recalled child listing the rows' answers but not where its row hides it; more rows than the most with the group's own error below them, linked to the rows' container, and no row state, and a post of more rows than any group may have showing that many; a hidden group with every row's controls and buttons disabled; a post without the group's rows showing its least; and on a German page under the optional style, the package's words and "(optional)" only on a group that needs no row. |
| `FormRenderingKitchenTests` | The kitchen sink (ADR-0015, `sink.cshtml`): a field of every built-in type and of a site's own type (`MoodFieldType`, whose view `fields/acme.mood.cshtml` follows the contract), each empty, answered after a post, with errors and hidden by a rule, with the same checks on each: the wrapper's classes, type, alias and hidden state, disabled controls when hidden, the control's id, its label or legend, its names, its help, and after an error the error, `aria-invalid` on the control or a group's own choices (a fieldset never, 6.8c) and the summary's link; what each answer shows. The repeatable group's row holds a field of every type a row may hold, each with the same checks by the row's ids and path, and its fields in order, a part's and an "Other" text's names, a few answers, a consent's wording named by the path, and its errors linked from the summary in the row's order. |
| `ErrorFixtureTests` | The server's errors the runtime is held to (ADR-0112, `errors.cshtml`, `errors-paged.cshtml`), on a site in English and on one in German: every required field left empty with one answer given, answers each check refuses (a hidden label, an editor's own message and a check's, a pattern, a match with another field and in a row, a number's range, a date's window from today, a phone, a web address, a text's longest, a grid row a validation rule refuses, a name in parts, a field a rule shows or requires, a validation rule's message, a radio group and a group of boxes left empty), nothing at all, and Next on the first page of two; each page rendered again written to `fixtures/rendering/errors-<language>.json`, the same on every run. |
| `CalculationFixtureTests` | The server's calculations and sliders the runtime is held to (ADR-0114, `calculations.cshtml`), on a site in English and on one in German: a prefix and its places, a suffix and no places, the default places, a calculation of a calculation, the days between two dates, a choice's number times a slider's, one a rule shows and one per row, and sliders with and without their unit and value; every calculation answered, an unreadable price, an empty date and a row's unreadable cost, and a price of nothing that a division fails on; each page first rendered and rendered again written to `fixtures/rendering/calculations-<language>.json`, the same on every run. |
| `ElementFixtureTests` | The server's first renders the element is held to (ADR-0128, `element.cshtml`), in English and in German: the kitchen sink of every built-in type in the default, `bootstrap5` and `unstyled` themes and with its trigger hiding every field; three pages with their steps, asterisks (the word "required" for a group of boxes, a grid's row of boxes and a ranking, 6.8c), a card group of two columns, affixes, a counter and the email again, in the default theme and `bootstrap5`, and with a bar; texts that recall answers; a closed form, one not yet open and one for members, each with its notice; each with the theme's classes, the post-only parts and the contract's names the element's tests read, written to `fixtures/rendering/element-<language>.json`, the same on every run. The sink's draft, with fixed keys, is written to `fixtures/rendering/designer-sink.json`: the designer's previews build their view models from it and are held to the sink's markup (ADR-0168). |
| `RenderEndpointTests` | `GET forms/{key}/render` (ADR-0104): the published form with the instance id asked for as the page shows it, in the page's culture, a form for members' notice for a visitor and the form for a member, no outcome even with a sent post's token, the theme it names else the form's (a theme the site lacks as the default), the refusals; and a form of sixty fields whose markup is over 64 KB written whole, the result buffered and written asynchronously. |
| `FormPickerSettingsTests` | The form picker's settings (ADR-0129, `picker-*.cshtml`): a single picker's form in the theme its settings name and a multiple one's forms in the editor's order, each in its own theme; the settings' page after a send reached by a post without JavaScript, at its top (the post's `#jfb-1` not carried there, ADR-0136), carried in the route token and nowhere a visitor can read it, where the same form placed by its alias shows its outcome; a page's value reported on save and never published for a form outside the allowed folders or not among the allowed forms, several in a single picker, a form the library lacks or holds in its trash and a value that is not keys, and accepted below an allowed folder, among the allowed forms, twice the same, several in a multiple picker and empty; a theme the site lacks and settings keys that are not keys refused with the data type. `FormPickerConfigurationTests` (unit) holds which forms a picker allows and how leniently it reads its values. |
| `FormPickerDeliveryApiTests` | The form picker in the Content Delivery API (ADR-0127), on a page type with the picker: a page that picks a headless form answers its `{ key, alias, name }`, and expanded the definition endpoint's `form` for that page, in English and, asked with `Accept-Language: de-DE`, in German; a form that is not headless, unpublished or deleted after the page picked it answers `null`, expanded or not; the picker's settings (ADR-0129) give the theme and the page after a send beside the form, and a multiple picker an array in the editor's order with `null` in the place of a form that is not headless, expanded too; the item route and the items route with two pages, expanded or not, leave no marker in their answers. `FormBuilderDeliveryApiMiddlewareTests` (unit) finds the markers by their exact bytes in the item answer and the items and query answers, each on its own page and a block's on the page holding the block, and none in what only looks like a marker (another property, another order, a key that is no key, a text `"true"`, a marker inside a text, an array, the settings' properties out of their places or of another kind), finds a marker's theme and page and a multiple picker's markers in order, and passes an answer without a marker, a text answer and any other path byte for byte, one outside ASCII too; `FormPickerValueConverterTests` (unit) converts only the picker's properties, reads the stored text as keys in any spelling a key has, in order and each once, and anything else as no form, gives Razor a `FormBuilderFormReference` (a list for a multiple picker) with the settings' theme and page and the Delivery API the marker (or a list) in the order the middleware reads, and caches each with the element. |
| `FormElementEndpointTests` | The element endpoint (ADR-0128): a thousand context names the form does not list dropped before the rendering, so none is logged or kept, while the listed one still renders (6.8a); a multi-page form's first render below the instance asked for, the first page current, the honeypot of the page's own markup and the definition endpoint's answer as the model data, escaped as the page's script holds it; a field of every built-in type and of a site's own type with its control's `kind` (a hidden field none, a divider no control), a row's fields with their own; no view path, the classes once and nothing only a post without JavaScript uses; a form for members shows a visitor its notice and a member the form; a closed form its notice; the culture, the theme (a theme the site lacks as default, the stylesheets in order) and the context values the form lists; `400` for each malformed parameter, `404` for a form the site lacks or that is not headless, `no-store`; every answer fits the element's schema. `ElementJsonTests` (unit) holds the shape (camel case, the classes once, the post-only parts left out, a control's kind first and a control of an unknown type refused, a rich text as its HTML, an enum's camel-case name, null as nothing), a kind for every control model, the schema in `packages/element/src/answer.schema.json` as the models' metadata makes it (`JFB_UPDATE_SNAPSHOTS=1` writes it), a control under a name of its own beside the same model written without one, and an answer that fits it; `scripts/tests/generate-public-types.test.mjs` holds `answer.d.ts` to the schema. |
| `FormRenderingPendingTests` | A site whose plan has not run: a page answers `200` with its own text and no form markup for a visitor, and exactly one note in the form's place, without the form's element or assets, for a signed-in backoffice user, in the page's culture (German). |

**The website runtime** (`packages/runtime`, Vitest in jsdom, [ADR-0110](../docs/decisions/0110-the-runtimes-frame-its-chunk-and-the-reading-of-the-page.md)):
`frame.test.ts` enhances every form of the contract once with one `jfb:ready` each, later forms through
`init(root)`, leaves a form of another contract version, without model data or with model data of another shape
to the post without JavaScript, and loads the chunk with the logic on a form's first `focusin`, `pointerdown`,
`input` or `change` only, once for the page, trying once when it cannot load; `page.test.ts` reads a page
named as the views name it through `FormData` and the post reader (a switch left off, a consent's wording, a
group's rows and their limits, a value the server resolves, a site's own type's cleaned texts, a disabled control
and a file left out) into the rules' answers; `rules.test.ts` decides the rules in the render's context (its clock
to the second, its time zone and culture, the query values the form lists) and shows them through the views' hooks:
a field, group or page shown or hidden with its controls enabled or disabled, `required` and the marks as the rules
require, the note while an asterisk shows, a row decided by its own answers, a shown field's answer decided again
at once, a form without rules or with rules the engine refuses left as rendered; `validation.test.ts` covers the
checks no post without JavaScript reaches (an answer over its type's limits alone, options a parent's answer
filters, files, calculations, values the server resolves and a site's own type left to the server, an editor's
empty message, Next's page with its rows, the empty submission's link past what the page hides, the words of a
culture's language loaded once); `errors.test.ts` shows errors as the visitor leaves a field they changed (none for
one passed through or for focus moving inside a field), says a new one in the live region, keeps it while the
visitor corrects and clears it on the input that fixes it; a refused send stops the post and shows every error, the
summary from the form's templates before the note with its focus, and `jfb:invalid` without an answer; the summary
is updated in place, goes once no error shows, and an empty submission's goes at the first answer; a field the rules
hide loses its error; without a summary the focus goes to the first field; a second press and Back are left alone;
a passing send goes on with its button, in a task of its own after the press's submit event (6.4f), as does one
whose errors are on a page the server has not shown; a summary
link moves the focus; the server's errors stay while their field is unchanged and go when it changes or the
rules hide it, and join only a send the page's own checks refuse; and a field the page leaves out (a member's own
value standing in) is the server's to check, the send refused for the other fields' errors alone (6.8b); a refused
group of choices marks its radio buttons, not its fieldset, after an option's description, and clears them once
answered (6.8c); `errors.offline.test.ts` sends as without JavaScript when the words cannot load; `outputs.test.ts`
shows each calculation's result as the visitor types, in its format, none for an unreadable answer, a failed
division or an empty date, nothing while the rules hide it, a row's in its row, written only when it changes, on a
form without rules too and never for a site's own type, said once the visitor pauses (fake timers, keystrokes closer
than the pause), in one line in the order of the page and never for the first pass (6.8b), and a slider's value
beside its thumb and in its `aria-valuetext`; `engine.test.ts` loads the calculation language only for a form with a calculation and the merge
tags only for a form with a text that recalls answers, each once for the page, and leaves a form whose chunk cannot
load to the post without JavaScript, fills a marked default in before the rules first decide and loads its filler
only for a form the server marked (6.4h); `defaults.test.ts` fills the marked fields once from the query values the
form allows and the endpoint's values, writes a refused tag as it stands and a default the endpoint does not list as
it is written, ticks and selects the matching choices, and never touches a control the visitor changed or a hidden
input, fills only the control named as the field (an "Other" text, a name's parts and an email's repeat stay empty,
6.8b), and fills a field prefilled from the query from its own query name before its default, a list for several
choices, nothing from a name the form does not allow or one an object inherits (6.5c); `recall.test.ts` renders a label, a help text, a placeholder, a heading, a
text block, a group's description and a later page's title as the visitor types, nothing of a field the rules hide
or of a row's child they hide in its row, a value escaped in a rich text, a text without a field tag and a field
after its text left as the server wrote them, and the error summary's link naming a label as it reads now, also
for a box that shows its own text, and none while it renders empty, a refused tag as it stands, and never a default
as a visitor text; `texts.test.ts` writes a text as text, hidden
while empty or showing its fallback, a placeholder as its attribute, a rich text by its texts and the attribute
values its elements already have, never a node or an `on…` attribute and not at all when its elements differ, a
recalled answer as it gains and loses line breaks with each break made anew (6.8b), and each element only when its
text changes; `jfb-runtime.test.ts` holds the global. In Edge,
`e2e/specs/website/runtime-frame.spec.ts` shows each demo form enhanced once with its `jfb:ready`, a form the page
puts in later enhanced through `init(root)`, the chunk loaded only once a form is touched (by focus, or by a change
without focus as autofill makes), or at once for a form with a default the page fills in, and no error in the
console; `e2e/specs/website/rules-on-the-page.spec.ts` shows
the feedback form's rules on the page in English and German, as the visitor answers; and
`e2e/specs/website/errors-on-the-page.spec.ts` shows, in English and German, the contact form's error as the
visitor leaves a field and its fix, a refused send with the server's errors for the same answers, the summary's
focus and links and `jfb:invalid`, axe clean with them shown, only the page's language file loading, and the
feedback form sent although a required field the rules hide has no answer; and
`e2e/specs/website/calculations-on-the-page.spec.ts` shows, in English and German, the room hire's cost following
the room and the hours, none for a room the visitor describes, the slider's value following its thumb, the
calculation language loaded once, axe clean, the cost the page showed stored with the entry, and the contact form
never loading the calculation language; and `e2e/specs/website/answer-recall-on-the-page.spec.ts` shows, in English
and German and signed in as a member, the workshop booking's question naming the visitor as they type, the notes'
help naming the workshop chosen, the closing text listing the guests and none once the rules hide them, the error
summary's link naming the question as it reads now, the merge tags loaded once, axe clean, and the contact form
never loading them; and `e2e/specs/website/defaults-on-the-page.spec.ts` shows, in English and German, the events
page's link opening the booking with its workshop chosen from the address before the visitor touches it, the help
naming it at once, the server's markup leaving the choice empty, axe clean, and a value no option has, or none,
choosing nothing; and `e2e/specs/website/headless-element.spec.ts` (6.6f,
[ADR-0128](../docs/decisions/0128-the-element-writes-the-servers-view-model.md)) shows the volunteer sign-up through
`<jfb-form>` on the static page on 5521, an origin the host's public API allows, in English and German: the theme's
stylesheets from the host, `jfb:ready` and `jfb:rendered`, the honeypot clipped to nothing and absent from the
accessibility tree, a rule showing a field, Next refusing the empty page with its errors and the summary focused and
the page where it was, no console error and axe clean; and the same page on an origin the API does not allow showing
nothing, with `jfb:error` and the console saying why (`e2e/support/static-site.test.ts` holds the page it serves);
`e2e/specs/website/keyboard-pass.spec.ts` (6.8e) shows, in every theme and scheme and in forced colours, every kind of
control changing its pixels as it takes the focus, with axe clean; Tab reaching each field in the page's order and
never the honeypot; the keyboard alone answering each choice, the switch and the slider, moving a ranking's option,
adding and removing a row and following a summary's link; Enter sending a form with JavaScript and without, and asking
for the next page on a form with a ranking; no slide or rise with reduced motion, which a recorder of the running
animations does find without it; and right to left, the stars, the switch and the Back arrow mirrored;
`e2e/specs/website/names-pass.spec.ts` (6.8e) reads Edge's accessibility tree for the role, name and description of
every kind of control and group, the required controls and `autocomplete` on the personal fields, and, with JavaScript
and without, each refused control invalid and described by its error and no group invalid;
`e2e/specs/website/focus-and-landing.spec.ts` (6.8d) shows, in the default and `bootstrap5` themes, light and dark,
each control a theme draws focused with a solid outline of 3:1 or more, every kind of control keeping an outline in
forced colours, a refused scale and rating drawn in the error's colour until answered, and an error summary's link
landing with the label below a sticky header in every theme, with JavaScript and without, on a desktop and a phone, a
site's taller header with its own margin and a focused summary too;
and `e2e/specs/website/strict-site.spec.ts` starts the demo as a second host on 5525, with the
output cache on and the Testing project's strict policy (ADR-0121), shows that an inline script the page did not
bring is refused and counted, runs the room hire form (the calculation language) from a page the cache serves and the
workshop booking (the merge tags) as a signed-in member, both with `script-src 'self'`, and both again from a page
with a nonce, with and without `'strict-dynamic'`, every chunk loaded, the error summary shown, no violation and axe
clean, and sends the contact form with no cookie set by any response; and `e2e/specs/website/journeys.spec.ts`
(6.5a, [ADR-0122](../docs/decisions/0122-the-summary-page.md)) takes, without and with JavaScript, the job
application through its three pages (Next refused while a page needs answers, Back keeping every answer, the steps
naming where the visitor is) and the specimen to its summary page (the first page's answers, "Not answered", a
calculation without "Change", a "Change" that opens the page at its field without a check, the changed answer listed
on the way back, the send from there), the job application's three pages again right to left, and the summary at
375 px with each answer under its question; axe is clean on every page of them. `Rendering/FormPostTests.Summary.cs`
holds the summary page without JavaScript to its rules on the server: the pages and fields the rules show, an
untitled page numbered, a sensitive answer masked, options by their labels, a repeatable group's rows, a file's
name, line breaks, the default Send first, a name of no field, and a send whose earlier pages' errors show in their
rows, linked to their "Change". `e2e/specs/website/copy-of-the-answers.spec.ts` (6.5b,
[ADR-0123](../docs/decisions/0123-the-copy-of-the-answers.md)) sends the job application without JavaScript and finds
the copy of its answers below the outcome, page by page, with "Not answered", a repeatable group's rows and no
"Change", in print media (`emulateMedia`) the answers in ink on white with the links gone, the same page again with
the outcome and a line in the answers' place, and a token the test host expired (`/jfb-testing/sent-expired`) showing
the form and nothing of the entry; with JavaScript the print button shows and prints, and the copy fits 375 px; axe is
clean on the copy. `Rendering/FormPostTests.Copy.cs` holds the copy to its rules on the server: the pages and fields
the rules showed, as the submitted event records what they hid, a title's merge tag from the entry's answers, a
sensitive answer masked, the page `no-store`, a HEAD that takes nothing, two views at once of which one shows the
answers, an expired token, the old marks deleted with a new one, and an outcome without the copy; `SentCopiesTests`
and `KeyValueRepositoryTests` hold its marks. `e2e/specs/website/prefill-on-the-page.spec.ts` (6.5c,
[ADR-0124](../docs/decisions/0124-prefill.md)) signs the demo member in and finds the contact form with their name in
its field, no email field, the contact page's topic chosen from its context value and "Where did you hear about us?"
from the allowed `?source=`, the server's markup holding neither the query value nor the member's address; the form
sends without the email field and the entry holds the member's address; a visitor who is not signed in answers the
email address, and a value under a name the form does not allow is left out; axe is clean.
`e2e/specs/website/form-picker-on-the-page.spec.ts` (6.7h, [ADR-0136](../docs/decisions/0136-the-form-picker-on-the-demo.md))
sends the volunteer sign-up from the form block of the demo's volunteer page with JavaScript, through both its pages,
and lands on the thanks page the block's picker names as its page after a send; signs in as the `editor` persona
(Umbraco's Editors group, no Forms section), who adds a form block to a page of their own, picks the newsletter in the
picker's sidebar, sees the block labelled with its name and publishes, after which a visitor's send goes to the same
thanks page; and, as the administrator, finds the thanks page in the picker's settings; axe is clean on the pages and
finds nothing inside the picker, its sidebar (both modes) and its settings.
`Rendering/FormPostTests.Prefill.cs` (`post-prefill.cshtml`) renders the same form for a visitor (cacheable, the
context value written in, the query prefill marked and empty) and a signed-in member (`no-store`, the name written in,
the email field left out), and proves the tamper case: a forged address posted for the hidden field is ignored on the
post without JavaScript and on the entries endpoint alike. `StructureTests` refuses a context value the form does not
list, a member name no merge tag can write, a hiding field in a repeatable group's rows or with a check against
another field or the other entries, and a page's rule or a validation rule's message on a hiding field. The size check holds every file of the runtime's build but its entry to
a content-hashed name, and `Client/scripts/scripts.test.mjs` shows the hash following a chunk's content.

**The headless element** (`packages/element`, Vitest in jsdom, [ADR-0128](../docs/decisions/0128-the-element-writes-the-servers-view-model.md)):
`markup.fixture.test.ts` holds the element's markup to the server's for every form of the element fixture (above);
`contract.fixture.test.ts` holds ADR-0015's contract on the element's output for the kitchen sinks, as
`FormRenderingKitchenTests` does on the server's: the form's attributes, its model data, templates, live region,
honeypot and navigation; each field's wrapper, its control's id and names below the instance (a row's with the row's
ids and path), its label or legend and its help; hidden fields hidden with their controls disabled; and the theme's
classes on each slot beside ours, and no other class; `jfb-element.test.ts` covers the element itself: the
endpoint's address from its attributes (a trailing slash, a key to encode, an empty culture, the context values), no
request without `api` and `form`, the next instance id the document has free and kept on each render, two elements
at once each with their own, the stylesheets and the runtime added once per document, the form shown once its
stylesheets loaded or failed or after a while, `jfb:rendered` with the form and the instance, an answer of another
contract, a refusal with and without a problem and an API it cannot reach each leaving it empty with one console error
and `jfb:error` with its code, a render again on an attribute change with the answers that come too late dropped
(also while the stylesheets load), the form kept when the element moves, a field of the site's own type framed and
said once per document, the form shown when the runtime cannot load, and no post as a page would make.

## Writing tests

- Name C# tests `Method_Situation_Expected` and assert with NUnit's constraint model.
- No sleeps to wait for state: poll with a deadline, as `TestMailbox.WaitForAsync` does, and use the fake clocks where
  time matters.
- Generate passwords with `TestPeople.NewPassword()`; test values that look like secrets carry the marker `JFB_FAKE`.
- Integration fixtures set their site's configuration on `InMemoryConfiguration`: in the constructor of an
  `UmbracoIntegrationTest`, before `base.Setup()` in an `UmbracoTestServerTestBase`. Never set a key on
  `builder.Config`: the test host chains one configuration into every fixture's site, so the key would reach every
  fixture that runs later, and `Support/SharedConfigurationTests` fails on it. To change the options while the site
  runs, register an `IOptionsChangeTokenSource` and an `IPostConfigureOptions` of the fixture's own, as
  `PublicServerKeyTests` does. Capture logs with `builder.Services.AddLogCapture()`.
- A test server fixture that calls the Management API derives from `Support/FormBuilderTestServerTestBase` and boots
  the runtime (`[UmbracoTest(Boot = true)]`): it runs Jiron Form Builder's migration plan after the host starts, and
  its `ConfigureHost` sets the configuration before the host is built.
- Every test's host goes once the test ends. Two things kept each test's host in memory until the machine paged and late
  tests' requests timed out: Umbraco's runtime listens to the process's unhandled exceptions and stays listening after
  its host is disposed (about 25 GB by the two-thousandth test); and the web host environment of Umbraco's test helper,
  one mock per fixture, answers every read of `ContentRootFileProvider` with a new provider and records it, while on
  Umbraco 18 every host watches one, so the watchers nobody disposes kept the mock and with it every host of the
  fixture (110 hosts and 3 GB after two minutes). `Support/UmbracoRuntimes.cs` releases both after every test,
  assembly-wide: the listeners, and the providers handed out during the test, disposed and forgotten.
  `Composition/TestHostReleaseTests` holds that a test's runtime listens alone and that a test's host holds its own
  providers alone.
- A test of the website's runtime or of the `<jfb-form>` element runs with its forms covered
  (`packages/runtime/src/testing-cover.ts`, through each project's set-up file): for every name of a form's prototype
  chain that could be an alias, the form answers the website's code with a control, as a browser does for a field of
  that name ([ADR-0260](../docs/decisions/0260-a-field-may-be-called-anything.md)). A test reads its forms as always;
  website code that reads a form's member plainly fails its suite. Stub a form's member with
  `stubMembers(form, { requestSubmit })`: a member set on the form itself is what a control of that name looks like,
  and the code reads past it. `testing-cover.test.ts` holds that the cover still answers.
- Read files of the repository in place through `RepositoryRoot.Path(...)`.
