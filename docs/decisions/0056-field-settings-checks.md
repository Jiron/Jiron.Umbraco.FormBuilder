# 0056. The checks of a field's settings

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the "Other"
  option of radio buttons and checkboxes), [ADR-0035](0035-structural-validation-and-public-projection.md) (three
  publish codes) and [ADR-0055](0055-field-type-framework.md) (the defaults of three lists); amended in 5.3a by
  [ADR-0063](0063-the-calculation-language.md) (a formula of at most 1,000 characters)
- Date: 2026-10-25
- Roadmap step: 5.1b (decided and built); 5.1c serves the rules to the designer

## Context

ADR-0002 leaves a field's `settings` to its type: the save checks hold them to the shape of a settings object
(integers of at most 15 digits, no `null` in a list, nothing empty), and nothing else looks at them. ADR-0055 gave
each type a settings schema read from its settings class: the kind, the default, the editor of each setting. Step
5.1b (5.1 split in four, then 5.1b in two, WORKLOG 2026-10-25) makes publishing hold every field's settings to its
type, and five questions were open:

1. where a setting's rules (a range, a length, a count, the values a text may be, a value the field must give) are
   written, so that publishing, the designer and a site's own types read them alike;
2. what a setting keeps when its class says nothing, given that an `int` cannot hold every number settings store;
3. how a type checks what no rule can say, such as a minimum above the maximum, and where it reports;
4. which codes these problems have, given that ADR-0035 keeps the publish codes apart from the save codes;
5. what to do with settings that can never be stored: settings leave an empty list and an empty object out, so a
   list that starts with items can never be emptied, and an object whose settings are all left out is no object.

## Options

1. **Where the rules are written.**
   - In the `[FormBuilderSetting]` attribute (`Minimum = 1, Maximum = 10`): one attribute, but a vocabulary of its
     own that a .NET developer has to learn, and C# attributes cannot hold a decimal or a nullable number.
   - In code, in each type's own check: nothing to read for the designer, which would have to wait for the server to
     say that 11 is too many.
   - **.NET's validation attributes** (`System.ComponentModel.DataAnnotations`): `[Required]`, `[Range]`,
     `[StringLength]`, `[Length]` and `[AllowedValues]` are what every .NET developer already writes on a model, and
     their values are plain enough to serve as JSON. Chosen, read by the settings reader into the schema; the rest of
     that namespace (`[MinLength]`, `[RegularExpression]`, `[EmailAddress]`, a class derived from one of the five) is
     refused, because the designer could not run it as the server does.
2. **Where the own check reports.** Throwing an exception with a message loses the path and stops at the first
   problem; returning a list of paths and messages makes every type build them. **A small reporter**
   (`FieldSettingsCheck.Invalid(setting, message)`) takes the place in the settings as the setting's name, `.name`
   inside an object and `[index]` in a list, and builds the path. Chosen.
3. **Codes.** The save codes (`unknown_property`, `invalid_type`, `invalid_value`, `missing_property`,
   `limit_exceeded`) would say what is wrong, but ADR-0035 keeps the publish codes apart from them, and a test holds
   it to that: a code tells the backoffice whether a save or a publish refused. **Three publish codes of their own**
   tell the editor that the problem lies in the inspector's settings, which the path then names. Chosen.

## Decision

### The rules

`FieldSettingDescriptor.Rules` (`FieldSettingRules`, a public record) holds what the attributes on a setting's
property say:

| Attribute | Rule | Allowed on |
| --- | --- | --- |
| `[Required]` | The field gives the setting a value of its own: a text that is not blank, a localised text whose default text is not blank, a list with an item, an object, a key | A setting without a default (null or empty); not with `AllowEmptyStrings` |
| `[Range]` | The lowest and highest value, either exclusive, as text in the wire form of ADR-0005 | A whole number or a number, or a list of them (each item); bounds of at most 15 digits, integral for a whole number, within `int` for an `int`; a range some value keeps |
| `[StringLength]` | The fewest and most characters, counted in UTF-16 code units as browsers count them | A text, or a localised text or rich text in each culture, or a list of texts (each item) |
| `[Length]` | The fewest and most items | A list; a minimum above zero needs `[Required]` or a default of that many items |
| `[AllowedValues]` | The texts a text may be, compared exactly | A text, or a list of texts; each value a text that keeps the length |

Bounds come from `int` and `double` arguments or from text with a type (`[Range(typeof(long), "1",
"999999999999999")]`), and are written in the wire form. **A setting without an attribute keeps the limits of its
kind**: a text has at most 2,000 characters (each culture of a localised text too) and rich text 50,000, as the save
checks hold the definition's own texts; a list has at most 1,000 items; an `int` keeps the range of `int`, since a
larger number would not bind. These limits are in the rules too, so the designer reads them like any other.

**The default keeps the rules.** A setting that is not given takes its default, so the reader checks each default
against the setting's rules as a field's value would be checked, and refuses a class whose default breaks them,
quoting the default. A required setting has no default by definition, and an object of settings whose default lacks
a required setting is refused the same way.

Every refusal names the class, the property and the reason, all at once, as the reader's other refusals do
(ADR-0055).

### The check of a field's settings

Publishing (`DefinitionStructureChecks.CheckField`, ADR-0035) checks each field's settings against its type's
schema, before the type's own check and only for a type the site has:

- a setting the schema lacks, at the top or inside an object: `setting_unknown`;
- a required setting not given, or a required text that is blank (for a localised text, its default text):
  `setting_missing`; inside an object, only once the field gives the object;
- a value of the wrong JSON type; a number that is not in the wire form or has more than 15 digits; a date, time or
  key that is not one (a key in the lowercase form definitions write); a choice that is not one of the enum's names
  exactly; a localised text without its default text, with a name that is no culture, or with a culture twice in
  another letter case; a bound, length, count or allowed value broken; and a value that is in a list twice, as the
  values compare (`1.50` is `1.5`, `09:30:00` is `09:30`): `setting_invalid`.

Each problem has the path of the value it concerns: `…settings.domains.list[1]`, `…settings.prompt.de`,
`…settings.heading['']`. A missing setting is named where it goes, even when the field has no settings at all.
Free JSON (`JsonElement`) is the type's own to check.

### The type's own check

`IFormBuilderFieldType.CheckSettings(FieldSettingsCheck check)` is a default interface method that finds nothing.
`FormBuilderFieldTypeBase<TSettings>` implements it by reading the settings as its class (`ReadSettings`, a new
instance for none) and calling `protected virtual CheckSettings(TSettings settings, FieldSettingsCheck check)`.

- It runs only when the settings keep the schema, so it reads a class that binds and values that keep their rules.
- `check.Field` is the whole field, so a check may compare a setting with the field's own options (a minimum of
  choices above the options there are).
- `check.Invalid("domains.list", "…")` reports `setting_invalid` at that place under the field's settings; a place
  written another way, or a blank message, throws `ArgumentException`.
- An exception from the check is reported as `setting_invalid` at the field's settings with its message, so a broken
  type never stops a publish from listing what else it found; cancellation is passed on.

### The built-in types

Their rules and own checks, besides the limits every setting keeps:

| Type | Rules | Own check |
| --- | --- | --- |
| `text` | `minLength` 0 to 4,000, `maxLength` 1 to 4,000; prefix and suffix 50 characters | the fewest characters at most the most |
| `textarea` | `rows` 1 to 50; `minLength` 0 to 100,000, `maxLength` 1 to 100,000; words 1 to 20,000 | the fewest at most the most, for characters and for words |
| `email` | the confirm label 200 characters; each domain 253 | each domain a host name of two labels or more in lowercase (IDNA); `any` lists none, `allow` and `block` at least one, at `domains.list` |
| `phone` | | the default country a country code |
| `number` | `decimals` 0 to 15; prefix and suffix 50 | minimum at most the maximum; a step above 0; minimum, maximum and step with no more places than `decimals` |
| `date`, `datetime` | `stepMinutes` 1 to 720 (`datetime`) | each limit a date or `{ "today": "+2d" }`; the earliest at most the latest when both are dates |
| `time` | `stepMinutes` 1 to 720 | the earliest at most the latest |
| `rating` | `count` 3 to 10; labels 100 | |
| `scale` | `from` 0 to 1, `to` 5 to 10; labels 100 | |
| `slider` | the unit 50 | the minimum below the maximum; a step above 0 and at most the distance between them |
| `calculation` | `formula` required, 1,000; `format.decimals` 0 to 15; prefix and suffix 50 | the formula itself, against the form (ADR-0063) |
| `dropdown` | the prompt 200; choices 1 to 1,000 | choices only with `multiple`; the fewest at most the most and at most the field's own options |
| `radio` | the Other label 200 | the Other label only with Other |
| `checkboxes` | choices 1 to 1,000; the Other label 200 | the fewest at most the most and at most the own options (one more with Other); the Other label only with Other |
| `grid` | `scale.from` 0 to 1, `scale.to` 2 to 10 | columns from the options or from a scale, never both and never neither, at `scale` |
| `ranking` | `top` 1 to 1,000 | `top` at most the field's own options |
| `checkbox` | the text 20,000 | |
| `yesno` | labels 100 | |
| `consent` | the text required, 20,000 | |
| `country` | | each country a code; the countries shown first and the one chosen at the start among those allowed, when some are |
| `name` | | the required parts among those asked for |
| `address` | | street, postcode and city never in `parts` (they are always asked for); the required parts among those asked for |
| `file` | each extension 10 characters; `maxBytes` 1 to 999,999,999,999,999; `maxFiles` 1 to 20 | each extension lowercase letters and digits without the point |
| `signature` | `width` 200 to 1,200, `height` 100 to 600 | |
| `repeater` | `minRows` 0 to 100, `maxRows` 1 to 100; labels 100 | the fewest rows at most the most |
| `heading` | the text required, 300; `level` 2 to 4 | |
| `paragraph` | the HTML required | |
| `image` | the media and the alternative text required, the text 300 | |

A country is one of the 249 ISO 3166-1 alpha-2 codes or XK (Kosovo), in capitals; a test holds the list to .NET's
regions, all of which it knows except Western Sahara (EH), and every region of .NET's cultures to the list, apart
from the exceptionally reserved codes such as EU. The file type's check against the host's blocked extensions and
size limit comes with the uploads (6.1), and the captcha placement's availability with the providers (7.2).

### Settings that can be stored in every state

- **Lists start empty and say what empty means**, unless an empty list would mean nothing: a file field without
  types or extensions takes images and PDFs (until now it started with both, which could never be taken away to
  allow only `dwg`); a required name with no required parts requires every part it asks for; a required address
  with none requires the street, the postcode and the city. ADR-0055's defaults are amended to these; the checks of
  the answers read them so (5.2, ADR-0058). A web address's schemes and a name's parts keep their defaults, since a field without either
  is no field, and a test holds every other list to starting empty.
- **The "Other" option is a yes or no and a label**: `other` (false) and `otherLabel` (the type's own text when not
  given) on radio buttons and checkboxes, instead of ADR-0002's `{ "label" }` object, which settings would leave out
  whenever the editor kept the type's own label, so the option could never be switched on that way. A label without
  the option is refused (said once, ADR-0002's "say it once").

## Consequences

- A field whose settings do not fit its type can no longer be published; a draft keeps it, and the form check lists
  every problem with its path, as it does the other publish codes.
- A site's type gets the same checks from attributes it already knows, and an own check through one override; the
  designer (5.1c) checks the rules in the editor as the server does, so most problems never reach a publish.
- Every rule of the built-in types is restated in `BuiltInSettingsTests` apart from the code and held at its edges.
- The checks cost one pass over each field's settings at publish, which a 2 MB definition bounds.
- Revisit if a setting needs a rule the five attributes cannot say and that the designer should run: such a rule
  would be added to `FieldSettingRules`, the reader, the checks and 5.1c's editors together.
