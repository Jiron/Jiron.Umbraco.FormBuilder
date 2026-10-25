# 0057. The field types endpoint and availability

- Status: accepted; amends [ADR-0055](0055-field-type-framework.md) (a choice's label) and the management API's table
  in `docs/architecture.md` (the registries)
- Date: 2026-10-25
- Roadmap step: 5.1c (decided and built); 9.x builds the palette and the inspector on it, 7.2 makes the captcha
  placement depend on its providers

## Context

ADR-0055 gave every field type a declaration and a settings schema, and ADR-0056 the rules of each setting. The
designer (§4.7 "designer support") needs all of it from the server: the palette shows the types in their groups, the
inspector shows each type's settings with their editors and checks their rules as publishing does, and a type that
needs configuration the site lacks is greyed out with its reason (§4.3.2). Step 5.1c builds that endpoint, the
availability check and the English and German texts of the built-in types. Four questions were open:

1. **Who may read it.** `docs/architecture.md` listed "registries" with the form check and the preview under
   `design`. A permission policy without a resource checks the global grants (ADR-0049), but `design` is granted per
   folder and per form too (ADR-0013): a designer of one folder holds it there only, and a global check would refuse
   them the palette of the forms they may edit.
2. **How the texts reach the backoffice.** Names and labels are plain text or `#` keys of the backoffice's
   localisation (ADR-0055), and a choice had no label at all: a radio layout would read `cards`, a name part `first`.
3. **What availability looks like for a type,** and what happens when a type's check is slow or broken, given that
   a site's own check may read a database or call a service.
4. **How a test knows** that every text the built-in types use has an English and a German key.

## Options

1. **Who may read it.**
   - `design` on a form, as a route below the form (`form/{key}/field-types`): right for the designer, but the
     registry is the same for every form, and the entries, the exports, the filters and insights also show fields by
     type to users who hold `viewEntries` only.
   - `design` globally: refuses the designer of one folder.
   - **Any user of the Forms section:** the registry describes the code installed on the site (types, settings,
     defaults and rules), the same for everyone, and holds nothing about a form, an entry or a person. Chosen.
2. **Choice labels.**
   - A naming convention (`<setting's key>_<value>`): invisible, and a key that does not exist shows as a key.
   - .NET's `[Display(Name)]`: familiar, but its other properties (order, group, prompt) would mean nothing here.
   - **`[FormBuilderChoice(label)]` on each enum member,** the counterpart of `[FormBuilderSetting(label)]`, plain text
     or a `#` key; without it the label is the member's name as C# writes it (`Vertical`). Chosen.
3. **Availability.**
   - A property: cannot read anything that takes a call, such as a provider's keys in the backoffice's settings.
   - **A method on the type,** `CheckAvailabilityAsync(CancellationToken)`, available by default, which the endpoint
     runs for every type at once, each within a time limit. Chosen.
4. **The check of the texts.**
   - The .NET tests reading the TypeScript dictionaries with a pattern: fragile.
   - **A committed snapshot of the endpoint's answer** for the built-in types, `docs/api/field-types.json`, which an
     integration test compares with what the endpoint serves (written with `JFB_UPDATE_SNAPSHOTS=1`, as the OpenAPI
     snapshot is, ADR-0050), and which a client test reads: every `#` key in it is in both dictionaries. Chosen; the
     snapshot is also the reference of the built-in types for developers.

## Decision

### The endpoint

`GET /umbraco/jfb/management/api/v1/field-type` (`fieldTypeList` in the generated client), for any user of the Forms section
(`[FormBuilderSection]`). It answers:

- `paletteGroups` and `settingGroups`: the groups of the palette and of the inspector in the order they are shown,
  each with its alias and a `#` key for its name;
- `types`: every type of the registry in its order (the built-in ones in palette order, then the site's), each with
  - its alias, name, icon and palette group;
  - what it holds and how: `holdsValue`, `storage`, `ruleKind` (null when rules cannot compare it), `multipleSetting`,
    `onePerForm`, `serverOnlyDefault`;
  - the common properties it supports (`features`, such as `label`, `placeholder`, `places`) and the error kinds it
    raises (`messages`, each the name of a field's message);
  - its `settings` in the inspector's order (their `order` is that order, so it is not repeated): name, kind, item
    kind, choices with their labels, the settings of an object, the default as settings store it, label, description,
    editor, inspector group, merge tags, whether visitors see it, and its rules (ADR-0056);
  - `availability`: `isAvailable`, and when it is not, the `reason`.

Names and enum values are written in camel case, as definition documents write them. A text is plain text or a `#`
key, which the backoffice's localisation turns into the editor's language. `docs/architecture.md` now lists the
registries for any user of the section; the form check, the preview render and preview tickets keep `design`.

### Availability

`IFormBuilderFieldType.CheckAvailabilityAsync(CancellationToken)` is a default interface method that answers
`FieldTypeAvailability.Available`. A type that needs configuration the site lacks answers
`FieldTypeAvailability.NotConfigured(reason)`: what is missing, as a sentence written for editors ("No captcha
provider has its keys."), plain text or a `#` key, which the palette shows with "Not configured". `FormBuilderFieldTypeBase`
implements the method as a virtual one, so a type of the base class overrides it. The endpoint runs every type's check
at once:

- each within 5 seconds (through the site's `TimeProvider`); a check that takes longer is reported as not
  configured with `#jfbFieldTypeAvailability_checkFailed` ("its setup could not be checked"), and the log has a
  warning with the type's alias;
- a check that throws or answers null is reported the same way, and logged as an error with the exception;
- the request's cancellation is passed on, and cancels the answer.

Availability greys a type out in the palette. It does not change publishing or rendering: a field already on a form
stays, and what its type does without its configuration is the type's own concern. No built-in type needs
configuration yet; 7.2 makes the captcha placement not configured while no provider has its keys.

### The texts

The backoffice's dictionaries are `packages/backoffice/src/lang/en.ts` and `de.ts` (registered by two `localization`
manifests, `Jiron.FormBuilder.Localization.En` and `.De`; a regional culture falls back to them, §3.4), which merge one
file per area and language. The field types' area holds:

| Keys | For |
| --- | --- |
| `jfbFieldTypes_<alias>` | the 33 built-in types' names |
| `jfbFieldSettings_<type><Setting>` | the settings' labels, an object's settings as `<type><Object><Setting>` |
| `jfbFieldChoices_<set><Value>` | the choices' labels, one set per enum (`layoutCards`, `namePartFirst`) |
| `jfbFieldPalette_<group>`, `jfbFieldSettingGroups_<group>` | the groups of the palette and the inspector |
| `jfbFieldTypeAvailability_checkFailed` | the reason of a check that failed |

The words follow `docs/ux/copy.md`: the glossary, sentence case, Umbraco's own German terms, "Sie". The client test
holds both dictionaries to the same keys, each a text that is not blank, every `#` key of the snapshot to them, and
every key of the area to the snapshot, so none is left over.

## Consequences

- The palette, the inspector and the rules builder (Phases 9 and 10) read one endpoint, and the designer of one folder
  gets it like anyone else; a site's own type appears with its own texts, plain or from its own localisation.
- A change to a built-in type's declaration or settings changes the snapshot, so the review sees what the designer
  will get, and its texts are checked in both languages.
- A slow or broken availability check of a site's type costs the palette at most 5 seconds and never fails it.
- Revisit if a type's availability depends on the user or the form: the check would then take them, and the endpoint
  would move below the form.
