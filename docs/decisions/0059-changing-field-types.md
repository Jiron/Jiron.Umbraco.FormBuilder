# 0059. Changing a field's type

- Status: accepted; builds on [ADR-0055](0055-field-type-framework.md) (what a type declares) and
  [ADR-0056](0056-field-settings-checks.md) (the rules of settings)
- Date: 2026-10-25
- Roadmap step: 5.1e (decided and built); 9.4 serves the map and previews a change in the inspector

## Context

The designer offers "Change type…" on a selected field (`docs/ux/information-architecture.md`, the canvas), and the
inspector of 9.4 changes a field's type "to compatible types only, with a preview of what is lost". A pasted list of
questions becomes text fields "ready to change type" (9.3b), so a text field has to be able to become a date, a
dropdown, a number or a file. A site's own types take part like the built-in ones (ADR-0055), so the map is computed
from what each type declares, never written as a table.

A change keeps the field's key, alias, label, help and everything both types share. What it can lose:

1. **Settings**, which belong to the type: the text's `maxLength` means nothing to a dropdown, and a long text's
   `maxLength` of 50,000 is more than a text takes.
2. **Common properties** the target does not support: a placeholder, options, places, a default value, a prefill,
   autocomplete, the checks no type owns, custom messages for error kinds the target does not have.
3. **Rules:** conditions that name the field compare its answers by its rule kind (ADR-0005), and an operator such as
   `contains` takes text only.
4. **Stored answers:** entries keep the version they were submitted with (D5), but the entries screen reads the
   field's current storage kind from the catalogue (ADR-0044), and older answers stay in the columns they were stored
   in (ADR-0058).

## Options

1. **Which changes the designer offers.**
   - Families of similar types (texts among texts, choices among choices): a pasted text field could never become a
     date or a dropdown.
   - **Any type that holds a value of its own into any other**, with a preview of what goes. Content holds no value,
     and a grid or a repeatable group keeps its values in its rows or child fields, which no other type has, so they
     do not change. Chosen.
2. **How settings carry over.**
   - By name only: a setting of the same name and another kind would carry a value the target cannot read.
   - **By name and kind, rule by rule:** a setting is kept when the target's rules take every value the source's take,
     narrowed when they take fewer (a value outside them is not carried over), and lost otherwise; the target's
     required settings that the change does not carry over are needed before the field publishes. Chosen.
3. **Where a field's preview is worked out.**
   - An endpoint per preview: a round trip for every type the editor looks at, for a draft that is only in the
     browser.
   - **The map served with the field types (9.4) and applied by the designer to the field in the draft:** the settings
     it has values for, the properties it uses and the conditions that name it. The map stays the one source of what
     a change loses. Chosen.

## Decision

`FieldTypeChanges` (internal, in `Core/FieldTypes`) works out the map from the registry. A field may change from one
type to another when the two differ, both hold a value of their own and neither stores `none`. Each change
(`FieldTypeChange`) says:

| Part | What it holds |
| --- | --- |
| `KeptSettings` | the source's settings the target has with the same name and kind, whose rules take every value the source's take |
| `NarrowedSettings` | settings of the same name and kind whose rules in the target take fewer values: a value outside them is not carried over |
| `LostSettings` | the source's other settings |
| `NeededSettings` | the target's required settings that the change does not carry over, which the field needs before it publishes |
| `LostProperties` | the common properties the source supports and the target does not (`needsOptions` is a demand, not a property) |
| `NeedsOptions` | whether the target cannot be answered without options and the source has none |
| `LostMessages` | the error kinds the source declares and the target does not: their custom messages go |
| `LostOperators` | the operators the source's rule kind takes and the target's does not; every one when the target has no rule kind |
| `ComparesAnew` | whether both have a rule kind and they differ, so the operators that stay compare answers as the target's kind |
| `KeepsColumns` | whether both store in the same columns (`text` and `longText` share them), so older answers filter and sort as before |

Settings are compared as follows. A setting the target lacks, or has with another kind or item kind, is lost. Of the
rest, a setting is narrowed when the target requires it and the source does not, when the target's lowest value, length
or count is higher, its highest lower (an exclusive bound counts as narrower than the same inclusive one), when its
allowed values or its choices leave out one the source takes, when a nested object keeps only some of its settings, or
when it is any JSON that the target checks itself; a nested object that keeps none of its settings, or a choice that
keeps none of its values, is lost. Lists are in the source schema's order, needed settings in the target's, and
operators in ordinal order.

## Consequences

- 9.4 serves each type's changes with the field types and shows the preview before the change: the field's values of
  lost and narrowed settings, the properties it uses that go, the conditions naming it whose operator goes, and what it
  needs. Making the change drops what is lost and leaves the rest to publishing, which refuses a condition with an
  operator the target does not take, a merge tag or calculation that no longer fits (5.3), and a combination of
  settings that the target's own check refuses (ADR-0056).
- The entries of earlier versions keep their answers as they were stored. When the columns differ, filters and sorts
  by the new kind's column pass older answers by, and they show as their text.
- A site's own type takes part through its declaration and settings schema alone.
- Revisit the offered changes if editors need to turn a grid or a repeatable group into something else; that would
  need a way to carry over rows or child fields.
