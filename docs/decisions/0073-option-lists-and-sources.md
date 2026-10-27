# 0073. Option lists and sources

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (a field takes no
  upload as its source) and [ADR-0033](0033-schema-v1.md) (an option list's kind is an enum); builds on
  [ADR-0055](0055-field-type-framework.md) and [ADR-0056](0056-field-settings-checks.md) (settings classes and their
  checks)
- Date: 2026-10-27
- Roadmap step: 5.5a (decided and built); 5.5b adds content and data type lists, 5.5c URL and file lists, 5.5d holds
  answers to the resolved options, 5.5e imports lists, 6.1 serves `GET forms/{key}/options/{fieldKey}`, 6.2 renders
  the options, 12.1 builds the library's editors

## Context

A choice field takes its options from itself or from a source (ADR-0002): a library option list (`jfbOptionList`,
ADR-0037), or a kind configured on the field with the configuration a library list of that kind has. Until now the
kinds were free words, a source's configuration was never checked, and nothing resolved options from anywhere but the
field's own items: both runtimes skip the check of such a choice's answers (ADR-0062), and merge tags show the stored
value (ADR-0066). Before the content, URL and file kinds, the step needs the model every kind shares:

1. **The kinds** and where each may be configured.
2. **Configuration:** its shape, its checks and the editors the library (12.1) and the inspector (9.x) build for it.
3. **Resolving:** what a source gives, in which culture, filtered how for a cascading field, and what a failure is.
4. **Code sources and the standard lists** (the roadmap's "presets"): months, weekdays, two scales and "yes, no, not
   sure".
5. **Keeping lists:** a list that changes shows at once, on every server.

## Options

1. **A kind's configuration.**
   - A JSON shape per kind with its own reader: every kind would bring its own checks, codes and editor.
   - **A settings class per kind, read into a settings schema as a field type's settings are** (ADR-0055), held to
     it by the same checks and codes (ADR-0056), then the kind's own check: one way to describe, check and edit
     configuration. Chosen.
2. **The standard lists.**
   - Templates that copy fixed options into a new list: the month names would be frozen in the editor's languages.
   - **Built-in code sources,** whose labels come from culture data and a language table in the visitor's culture,
     and which a site may replace or remove as it does a field type. Chosen. The backoffice calls them standard lists,
     because "preset" names an action's presets (`docs/ux/copy.md`).
3. **Labels.**
   - Every culture's labels from every source: a content source would read every variant for every request.
   - **The labels of one culture,** the one asked for: own items and manual lists by the text's fallback (ADR-0002)
     with dictionary items (`#key`) read in that culture, a source in its own way. Chosen.
4. **A cascading field's options.**
   - Only the source filters: a manual list or a field's own items could not cascade.
   - **The parent's answers go to the source, and an option that names parent values belongs only to them:** an option
     without parent values belongs to every answer (a "Not listed" option), and with no answer the field offers only
     those. Chosen.
5. **A failing source.**
   - An exception: a broken source would stop the page or the submission.
   - **No options, marked unavailable, and a log line without values.** Chosen; what an unavailable source means for
     a submission's answers is 5.5d's to decide.

## Decision

- **The kinds** (`OptionListKind`, stored as its camelCase name like every enum, ADR-0033): `manual`, `content`,
  `dataType`, `url`, `file`, `countries`, `code`. A library list has a kind, its configuration, its items (a manual or
  uploaded list's) and `cacheSeconds`. A field's source (`options.source`) is its own `items`, a `list`, or one of the
  kinds `content`, `dataType`, `url`, `countries` and `code` with `config`. An upload is a library list's or becomes a
  field's own options in the designer, so publishing refuses `file` on a field (`options_invalid`).
- **Configuration** is a settings class per kind with `[FormBuilderSetting]`, read into a settings schema (the kind's
  `Schema`, which the backoffice's editors read), checked with `setting_unknown`, `setting_missing` and
  `setting_invalid` at its path, then by the kind's own check:
  - **manual:** none; its items are read as a field's own items, with the same shape checks, codes and paths (at
    `$.items`) and the same save checks (lengths, values cleaned as answers are and unique once folded, places from
    0 to 1,000,000, parent values), at most 10,000 (`MaxOptions`);
  - **countries:** `countries`, the ISO 3166 codes offered (all without any), each known and once;
  - **code:** `source`, the name of a registered option source;
  - the other kinds come with 5.5b and 5.5c; until then a kind the site lacks is refused as such.
- **Publishing** checks a field's source configuration through its kind (`OptionSourcePublishCheck`, after the
  structural validation), at `…options.config`. A library list is checked when it is saved (`OptionListService`).
- **`IFormBuilderOptionSource`** (§4.9): a `Name` (what §4.9 calls its alias: a lowercase ASCII letter, then letters
  and digits, at most 64; start-up refuses one that is taken or outside the grammar, naming the type), a `Label` for
  the backoffice (plain text or a `#` key), and `GetOptionsAsync` with the culture, the parent's answers and the
  current page; a source that takes longer than 5 seconds counts as failed. Registered through
  `builder.JironFormBuilder().OptionSources()`, which holds the standard lists from the moment it is made; each source
  is a singleton.
- **The standard lists**, each value the same in every language:
  - `months`: `1` to `12`, the names from .NET's culture data over the Gregorian calendar;
  - `weekdays`: `1` (Monday) to `7` (Sunday) as ISO 8601 numbers them, from the culture's first day of the week, the
    names from culture data;
  - `agreement`: `1` (strongly disagree) to `5` (strongly agree);
  - `satisfaction`: `1` (very dissatisfied) to `5` (very satisfied);
  - `yesNoNotSure`: `yes`, `no`, `notSure`.

  A name from culture data starts with a capital letter, as an option's label starts a line. The scales' words come
  from a language table (`OptionLists/lang/`) in English and German (14.2 adds the other five); a culture without its
  own words takes its more general culture's, then English.
- **`IFormBuilderOptionService`** gives a field's options (`GetAsync`) or a library list's (`GetListAsync`) for a
  context (the culture, the parent's answers, the current page) as `FormBuilderResolvedOptions`: the options in
  order, each with its value, label, description, image, places and parent values, and whether the source was
  available.
  - **Cleaning what a source gives:** an option whose value is blank, longer than 200 characters, changed by cleaning
    (ADR-0005) or equal to an earlier one once folded is left out; a blank label shows the value; past 10,000 options
    the rest is left out. Each is logged as a count, never with values.
  - **Unavailable:** a list the site lacks, a kind or a source the site lacks, a stored list whose kind, configuration
    or items no longer read (logged once while it is kept), a source that throws (logged with its kind, the field or
    list, and the exception's type and stack, never its message, ADR-0009), or one that takes longer than 5 seconds
    (logged as such; the wait ends then even for a source that does not heed its token). An unavailable source gives
    no options.
- **Keeping lists:** a library list's row is kept in the runtime cache, read once per server while it is missing
  (ADR-0043), until it changes. `OptionListService` creates, changes and deletes lists through their kind's checks,
  and the option list cache refresher drops the row on every server. A source list's options (any kind but manual)
  are kept for its `cacheSeconds` per culture, parent answers in any order and page, and dropped with the row; `0`
  keeps none, and a source that fails is never kept.

## Consequences

- ADR-0002's sources hold one shape per kind, checked when a form is published and a list is saved; ADR-0033's free
  kind word is an enum, and rows with any other word are refused as rows something else wrote.
- 5.5b and 5.5c add their kinds as settings classes with their checks and caches; 5.5d holds answers to the resolved
  options in both runtimes; 5.5e creates imported lists through `OptionListService`; 6.1 and 6.2 resolve options for
  the endpoint and the markup; 9.x and 12.1 build the inspector's and the library's editors from the kinds' schemas,
  and 12.1's endpoints serve the schemas and the sources' labels, whose `#` keys join the backoffice's English and
  German dictionaries with them.
- A site's own source is an extension point with a sample in 15.x.
- **Revisit** if a source needs settings of its own beyond its name, or if labels must be resolved in several
  cultures at once.
