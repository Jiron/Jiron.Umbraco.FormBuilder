# 0055. The field type framework

- Status: accepted; amends [ADR-0035](0035-structural-validation-and-public-projection.md) (the traits come from
  the registry) and [ADR-0044](0044-form-service.md) (the service's field types)
- Date: 2026-10-25
- Roadmap step: 5.1a (decided and built); 5.1b, 5.1c and 5.1d build on it

## Context

Through Phase 4 the 33 built-in field types of §4.3.2 were one internal table, `FieldTypeCatalogue.BuiltIn`, which the
structural validation, the public projection and the field catalogue read (ADR-0035, ADR-0044). §4.9 promises sites
their own field types: a class with a settings schema, registered through `builder.JironFormBuilder()…`, whose
settings get a backoffice editor from attributes alone. Step 5.1 was too large for one step and became four (WORKLOG
2026-10-25); 5.1a builds the registry, the declarations and the settings schemas, and moves publishing onto them.
Four questions were open:

1. how a site registers, replaces and removes a type, whatever order its composers run in;
2. what a type declares, and which declarations contradict themselves;
3. how a settings class becomes a schema, and which values a setting may hold;
4. how the settings are read as JSON, given that a site's settings classes are not known when the package is built.

## Options

1. **Registration.**
   - A list in the service collection (`services.AddSingleton<IFormBuilderFieldType, …>()`): the order is the order
     of the registrations, and replacing or removing a built-in type means finding and removing its descriptor.
   - A list in `JironFormBuilder` configuration: types are code, so configuration can only name them, and a typo is a
     start-up failure far from its cause.
   - **Umbraco's ordered collection builder** (`OrderedCollectionBuilderBase`), as Umbraco's own extension lists do:
     `Append`, `Insert`, `InsertBefore`, `InsertAfter`, `Remove` and `Replace` by type, built once into a singleton
     collection. Chosen.
2. **Where the built-in types enter.** The core composer could append them, but then a site composer that runs
   before it would find an empty builder, and one that removes a built-in type would have to order itself after the
   core composer. **The builder's constructor appends them** instead, so whichever composer asks first gets a builder
   that already holds them. Chosen.
3. **Settings as JSON.** Source generation, as the definition model uses (ADR-0034), cannot reach a site's classes,
   and a context per type would make every site write one. **The reflection resolver** of System.Text.Json reads any
   class, with the metadata built once per class; settings are small and read once per field per publish or
   submission. Chosen; the options are strict in the same way as the definition reader's (below).

## Decision

### Registration

- `builder.JironFormBuilder().FieldTypes()` returns `FormBuilderFieldTypeCollectionBuilder`, which holds the
  built-in types in palette order from its start (`BuiltInFieldTypes.All`). A site appends its own, replaces one
  (`Replace<TextFieldType, MyTextFieldType>()`) or removes one (`Remove<SignatureFieldType>()`); composers may run
  before or after the core composer.
- The collection (`FormBuilderFieldTypeCollection`) and each type are singletons. `FieldTypeRegistry` (internal)
  builds over it once: the types in their order, the traits of each (`FieldTypeTraits`, ADR-0035), and look-ups by
  alias. The structural validation, the public projection, the field catalogue and the form service take the
  registry; nothing reads a static table any more.
- A type implements `IFormBuilderFieldType` (its declaration and its settings schema) or, the usual way, derives from
  `FormBuilderFieldTypeBase<TSettings>`, which reads the schema of its settings class when it is created.
  `NoFieldSettings` is the settings class of a type without settings.
- A field whose type the site removed is `unknown_type` at publish, as any unknown type is (ADR-0035).

### The declaration

`FieldTypeDeclaration` (a record) holds:

- the alias (the pattern of ADR-0002), a name (text or a `#` key of the backoffice's localisation), an Umbraco icon
  (`icon-…`) and a palette group (`FieldPaletteGroup`: text, numbers and dates, choices, people, files, structure,
  content, protection);
- whether it holds a value, how the answer is stored (`FieldStorageKind`, the field catalogue's column, ADR-0044),
  and how rules compare it (`FieldKind` of the shared logic, ADR-0005, now public; null for a name, an address, a
  repeatable group and a grid);
- the common properties it supports (`FieldFeatures`) and the error kinds it raises (`FieldMessageKinds`), both now
  public;
- `MultipleSetting`, `OnePerForm` and `ServerOnlyDefault` as ADR-0035 describes them.

**A declaration that contradicts itself is refused.** The registry checks every type and names each problem with
the type's alias and class:

- a missing declaration or schema, an alias outside the pattern, a blank name, an icon that is not an Umbraco icon
  name, and a group, storage, kind, feature or error kind that is not defined;
- a second type with an alias already taken, also in another letter case (a field names its type exactly, but
  "Rating" and "rating" would read as one in the palette and in a definition), naming both and how to replace the
  first;
- a type without a value that has storage, a rule kind, error kinds, a multiple setting, a server-only default, or any
  feature but show and hide rules; a type with a value that has no label;
- storage `None` other than exactly for a type with rows or child fields, and a type with both;
- required options or places without options; the required feature without the `required` error kind, or the other
  way round; the comparing checks (`matchesField`, `uniquePerForm`) without a rule kind; a server-only default without
  a default value;
- a multiple setting on a type that neither compares as a choice nor has rows, or without a boolean `multiple` setting.

The registry throws `InvalidOperationException` with every problem. At start-up, `FieldTypeStartupCheck` runs the
same checks with the other start-up checks (ADR-0042), so a broken site stops before it serves anything, and a type
whose settings class cannot be read (below) is reported there too.

### The settings schema

`FieldSettingsSchema` lists a type's settings in the order the inspector shows them: by group (`Essentials`,
`Validation`, `Appearance`, `Advanced`), then `Order`, then as the class declares them, a base class's first. Each
`FieldSettingDescriptor` holds the name, kind, item kind, choices, nested settings, default, label, description,
editor, group, order, whether it takes merge tags, and whether visitors see it.

- **Every public property is a setting** with `[FormBuilderSetting(label)]`, or is left out with `[JsonIgnore]`; a
  property with neither, or with both, is refused. The name is the property's in camel case or its
  `[JsonPropertyName]`, and passes the setting-name rule of ADR-0042.
- **Kinds follow from the type:** `bool` a yes or no; `int` and `long` a whole number; `string` a text, or a number,
  date or time when the attribute's `Kind` says so (numbers compared with answers are wire strings, ADR-0005);
  `LocalizedText` and `LocalizedHtml`; an enum a choice, by the names its JSON writes (camel case, or
  `[JsonStringEnumMemberName]`), in the order the enum declares them; `Guid` a key; `JsonElement` free JSON, which the
  type checks itself; `IReadOnlyList<T>` a list of whole numbers, texts, numbers, dates, times, choices or keys; and a
  class an object of settings, one level deep. Anything else is refused with the reason: a double or a date and time
  (whose JSON is not fixed), other collections, an object inside an object, a list of objects or of yes-or-no values,
  a `[Flags]` enum or one whose values have two names, and a `Kind` the type cannot be stored as.
- **The editor** is the kind's name in camel case (`boolean`, `localizedText`, `list`, `nested` and so on) unless the
  attribute names one; a key, a list of keys and free JSON have no editor of their own and must name one (the
  built-in types use `mediaPicker`, `dateBound` and `formula`). 5.1b serves the schema to the designer, and a
  `jfbSettingEditor` manifest can add an editor (15.1).
- **The default** is what a new instance holds, written as settings store it (ADR-0002): canonical, so `null`, `[]`
  and an object with nothing left have no default. A property that the class says cannot be null but that starts
  null is refused, since settings leave nulls out and it would stay null after every read.
- **Visitors see a setting** only when its attribute says `Public`, and only at the top: what visitors see of an
  object is the whole object. The traits' public settings, and so the projection, come from the schema.
- The settings class needs a public constructor without parameters; a constructor that throws, or two properties of
  one JSON name, are reported as the class cannot be written as JSON.

The reader throws `InvalidOperationException` with every problem; the base class adds the type's alias and class.

### Settings as JSON

`FieldSettingsJson.Options` (internal, frozen) reads and writes every settings class: camel-case names, enums as
camel-case strings and never as numbers, a property the class lacks refused, a property given twice refused, numbers
only as JSON numbers, nullable annotations respected (a `null` for a text that cannot be null is refused), nulls left
out when written, and the reflection resolver. The converter reads a choice in any letter case; the exact names are
the schema's, and 5.1b's settings check holds stored settings to them before they are read as the class.

### The built-in types

Each type of §4.3.2 is a public sealed class in `Core.FieldTypes.BuiltIn` with its declaration and settings class,
with the settings and defaults of ADR-0002's table. Names and labels are `#jfbFieldTypes_<alias>` and
`#jfbFieldSettings_<type><Setting>` keys, which 5.1b's English and German dictionaries fill. Where ADR-0002 gives no
default, this ADR chooses one:

- a name asks for and requires the first and last name;
- an address always asks for the street, the postcode and the city and requires them, and asks for the second street
  line, the region and the country only when the field says so;
- a file takes images and PDFs, one file;
- a grid's scale runs from 1 to 5, an image is medium, a scale starts at 0;
- an email field's allowed and blocked domains stay on the server (ADR-0035); every other built-in setting is public,
  because a browser needs it to show or check the field.

`FieldTypeSupportTests` restates §4.3.2 apart from the code, now against the registry: for each type, every common
property, check, rule and message it refuses or accepts, and its palette group, storage, rule kind, multiple setting,
one per form, server-only default, settings and public settings.

### What comes next

- **5.1b:** the rules of the attributes (ranges, lengths, allowed values, required settings) and each type's own check
  of its settings at publish, at paths under `…settings`; the field-types endpoint for the designer; availability
  ("not configured: …").
- **5.1c:** storage kinds mapped to `jfbEntryValue` columns, the parse and validate contract, the longest answer a type
  takes, and the change-type map.
- **5.1d:** designer locks of settings (ADR-0042), checked against the schema.

## Consequences

- A site adds a field type with a class and a composer, and the validation, projection and catalogue treat it as a
  built-in one; the designer's editor for it comes in 5.1b without JavaScript.
- The traits test against the registry, so a change to a built-in declaration fails `FieldTypeSupportTests` unless
  the restatement changes with it.
- Reflection metadata costs a little at start-up per settings class; settings classes are small and read once.
- Revisit if a settings shape needs a kind the reader refuses (a decimal as a JSON number, a list of objects): such a
  kind would be added to the reader, the schema and 5.1b's editors together, never read loosely.
