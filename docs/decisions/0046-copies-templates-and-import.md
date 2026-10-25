# 0046. Copies, templates, import and export

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (how references are
  found, the envelope's limits and layout, `unsupported_format`, when an import creates option lists) and
  [ADR-0044](0044-form-service.md) (the new-form defaults apply to forms from a template); the operations are
  `IFormBuilderFormExchangeService`'s since [ADR-0053](0053-form-service-in-parts.md) in 4.8d
- Date: 2026-10-24
- Roadmap step: 4.5c (decided and built); 4.7 and 9.x map it to the API and the tree, 5.3, 5.5, 8.1 and 8.3 extend it,
  12.3 adds the other templates and "Save as template", 12.5 the import that replaces a form and the mirror files

## Context

ADR-0002 settles the export envelope (`jfb-form-export/1`), that an import makes a new form with new keys, that a
duplicate gives every element a new key and remaps every reference, that copying actions maps the fields they name by
alias and lists the ones it cannot map, and that built-in templates ship as embedded envelopes. ADR-0044 put every
change to a form behind one service with its own unit, checks, notifications and audit entries. The Import sidebar
(IA §4) adds that a theme the site lacks gives way to the default theme, that a page the site lacks is chosen before
publishing, and that an action whose address the site lacks arrives switched off. Open questions:

- **Where references are.** Rules, checks, cascading options and the verification field name elements by key, and so
  will the settings of field and action types (8.x: "reply to a field", a recipient map, a field-to-property map),
  which the model keeps as opaque settings objects. A remap that knows only today's properties misses tomorrow's.
- **What a duplicate copies:** the draft, or what visitors get; and the form's grants and restriction (ADR-0013).
- **Fields that match by alias.** Copying actions to another form maps a reference to the source's field `email` to
  the target's field `email`. Grid rows have keys but no aliases. A reference that finds nothing has to go somewhere.
- **Option lists.** An envelope carries the library lists the form uses so that an import can reuse or create them,
  but the option list kinds, their checks (URL lists must refuse other hosts, 5.5) and the library's permission come
  later. A list created now from a file would hold configuration nothing has checked.
- **The envelope as a file.** People read, diff and edit exports; a 2 MB definition written with indentation is larger
  than 2 MB.
- **The new-form defaults** (the consent field) apply to a form from blank (ADR-0044). A site that wants consent on
  every new form also wants it on a form made from a template.

## Options

**Finding references:**

1. **Typed remaps,** one per property that holds a key. Each new property that names an element needs its own line,
   and a settings object is out of reach.
2. **A walk over the canonical JSON:** the element keys are collected from the model, and every string value and
   property name anywhere in the document that is exactly one of them, in the `D` format, is replaced; the result is
   read back through the model. Settings that name an element by key are found without knowing them.

**What a duplicate copies:**

1. **The published version.** A form never published could not be duplicated, and the editor's work since the last
   publish would be lost.
2. **The draft,** which is what the designer shows; grants and the restriction stay with the source.

**A reference that finds nothing when actions are copied:**

1. **Removing the condition or the setting.** The copy then does something else than the original without a word.
2. **Switching the action off.** It still has to be repaired, and the switch hides why.
3. **Keeping the reference and reporting it.** The draft keeps it, as a draft keeps every problem (ADR-0002);
   publishing refuses it with `unknown_field` until it is fixed, so it never runs half-mapped.

**Option lists in an import:**

1. **Create every list the site lacks now.** Configuration from a file would reach the library unchecked, past the
   permission that guards it.
2. **Reuse what matches now, report the rest, and create lists in 5.5,** when the kinds and their checks exist.

**The new-form defaults:**

1. **Blank forms only.** A form from a template would start without the site's consent field.
2. **Blank forms and forms from a template,** which are new designs; a duplicate and an import copy a design someone
   already made and stay as they are.

## Decision

Option 2 in each case, except option 3 for references that find nothing. In detail:

### Keys and references

- `DefinitionKeys` (`Core.Definitions.Exchange`) lists a definition's element keys from the model: the definition, its
  pages, groups and fields (a repeatable group's children included), grid rows, the rules of pages, groups, fields,
  actions and outcomes, form-level validation rules, actions and outcomes.
- **Renewing** gives each of them a new key and replaces every string value and every property name of the canonical
  JSON that is one of them, in the `D` format in any letter case; then the document is read back through the model.
  Keys of other things (option lists, content, media, member groups) stay, because they are not element keys.
- **The convention for types** (5.1, 8.1): a field or action type's setting names an element by its key as a whole
  string or a whole property name, so that copies find it. A key inside a longer string is not found.

### Duplicate

`DuplicateAsync(key, request)` copies the form's draft, read as stored and upgraded (ADR-0042), into a new form:

- with its actions or without them (`IncludeActions`, which the caller always chooses);
- every key renewed, so the copy's rules, checks, cascading options, verification field and settings name the copy's
  own elements;
- a name and a folder from the request, and an alias from the name unless the request gives one, as a create does;
- unpublished, without entries, versions, grants or the restriction: the copy takes its folder's access, as a new
  form does, so its creator can open it (a restricted copy without grants would be for administrators only);
- refused for a form in the trash (`trashed`), for a draft that cannot be read (its read problems), and with
  `limit_exceeded` when the draft is above a lowered limit.

### Copy actions to another form

`CopyActionsAsync(request)` appends the source draft's actions (all, or those named, in the source's order) to the
target's draft, as a draft save of the target with its concurrency version:

- each copied action and its rules get new keys; a reference from one copied action to another follows it;
- **a reference to a source field** maps to the target field with the same alias in any letter case (aliases are
  unique that way); **a grid row** maps to the row of the mapped grid whose default label is the same, when exactly
  one is;
- **anything else of the source** (a field the target lacks, a row without a match, a page) is kept as it is and
  reported: each copied action lists its unmapped references with their path in the target's draft, the source key
  and the field's alias. Publishing refuses them (`unknown_field`) until the editor fixes them;
- references to content, media and other library items are kept;
- nothing to copy writes nothing; an unknown action key is refused with `invalid_value` at `actionKeys[n]`; a source
  or target in the trash with `trashed`; a stale target with `concurrency_conflict`.

Merge tags name fields by alias already and are copied as they are; 5.3 adds the tags that name a field the target
lacks to the report.

### Templates

- **Built-in templates** are envelopes embedded in Core (`Forms/Templates/*.json`), each with a fixed key that is its
  envelope's form key. Their texts come in English (the default text) and German (`de`). 4.5c ships **Contact**: a
  name, an email address and a message, all required, and the built-in thank-you message; 12.3 adds the others.
- `CreateFromTemplateAsync(request)` finds the key among the built-in templates first, then among the saved templates
  of kind `Form` (12.3 saves them), renews every key, and creates the form with the template's name unless the
  request gives one. A key that names no form template gives null.
- **The new-form defaults apply** (this amends ADR-0044): while the consent default is set, a form from a template
  gets the consent field at the end of its last group, unless it has a consent field or a field with the alias
  `consent` already. 8.3 applies the notify-the-creator default the same way.

### Export

`ExportAsync(key, version)` writes the envelope of the draft, or of the version named:

- `format`, `packageVersion` (the Core assembly's version without build metadata), `exportedUtc`, `form` (key,
  alias, name), `definition` (the canonical form's properties) and `optionLists`: each library list the definition
  names by key that the site has, with its key, alias, name, kind, configuration, items and cache time;
- indented by two spaces, with every character outside ASCII escaped, so the file reads and diffs well and survives
  any editor; the file name is `<alias>.json`;
- a form in the trash can be exported, since nothing changes; a stored version that cannot be read is refused with its
  read problems.

### Import

`ImportAsync(request)` reads an envelope strictly and creates a new form from it, always with new keys (12.5 adds
importing as a new draft of an existing form):

- **The envelope** is at most four times the definition limit (8 MB by default), so indentation and option lists fit.
  Its `format` is checked first: another value, such as `jfb-form-export/2` from a newer version, is refused with the
  new code `unsupported_format`. Then the properties, as strictly as a definition: `unknown_property`,
  `duplicate_property`, `missing_property`, `invalid_type`, `invalid_value` and `duplicate_key` (two option lists with
  one key). The definition's key must be the form's.
- **The definition** is measured without its indentation against the site's definition limit and read like every
  save: an older `schemaVersion` is upgraded, a newer one refused with `unsupported_schema_version`. Its problems keep
  their paths below `$.definition`.
- **Name and alias:** the request's, or the envelope's name and its alias (or one from the name when the envelope's is
  not valid), with the next free number when another form has it (`contact2`).
- **A theme the site lacks** is cleared, so the form uses the default theme, and the import says so (`theme_missing`).
- **Option lists:** a list the site has by key is used as it is. Otherwise, when the envelope carries it and the site
  has a list with the same alias in any letter case and the same kind, configuration and items, the fields are moved
  to that list (`option_list_reused`). Otherwise the fields keep the key and the import says so
  (`option_list_missing`); 5.5 creates such lists through the kinds' checks, only for a user who may manage the
  library.
- **Content, media and member groups** are kept by key, and the form check (9.5b) shows the ones the site lacks.
  **Actions** arrive as they are; 8.1 switches off an action whose site configuration (such as a Slack address) this
  site lacks.
- The result is the new form and these notes, each with its code, the path in the envelope and the alias it is about.

### For every operation

- Every create (from a template, an import, a duplicate) follows ADR-0044: the checks, `FormSavingNotification`
  (`IsNew`), one unit with the form, its version 1 and the audit entry, then `FormSavedNotification`. Both carry
  `CreatedFrom` (`Blank`, `Template`, `Import`, `Duplicate`) and `SourceKey` (the template, the source form, or the
  form key in the envelope). Copying actions is a draft save of the target whose notifications carry
  `ActionsCopiedFrom`.
- Every result is held to the site's limits with the save checks before anything is announced.
- **Audit actions** (details name keys, aliases, numbers and codes, never texts): `form.createdFromTemplate`
  (the template, whether it is built in), `form.imported` (the envelope's form key and alias, its package version when
  it is an identifier, the note codes), `form.duplicated` (the source, its draft's number, whether actions came
  along) and `form.actionsCopied` on the target (the source, the number of actions and of unmapped references), each
  with the alias, version and folder a create records.
- **No new statements:** these operations use the repositories' reads and writes of 4.3a and 4.3b, so the guard rule
  of ADR-0038 holds as `SqlGuardTests` checks it.
- `schemas/jfb-form-export.v1.schema.json` describes the envelope, with its definition by reference to the form schema.

## Consequences

- A setting that a later step adds, which names a field by key, is remapped by every copy without a change here.
- Copying actions never makes an action do something other than the original without saying so, and a half-mapped
  action cannot reach visitors.
- Exports are readable files, and an exported form imports into the same version and every later one.
- An import cannot put unchecked option list configuration into the library; until 5.5 it reports missing lists
  instead of creating them.
- Roadmap: 5.3 reports merge tags in copied actions that name a field the target lacks; 5.5 creates the missing
  option lists of an import; 8.1 switches off an imported action whose site configuration is missing; 8.3 applies the
  notify-the-creator default to forms from a template; 9.x's Duplicate dialog says when the source is restricted
  (WORKLOG).
- **Revisit** if types need references inside longer strings, if editors want to choose a target field for an
  unmapped reference while copying, or if sites exchange forms that must keep their keys (then 12.5's replace import is
  the place).
