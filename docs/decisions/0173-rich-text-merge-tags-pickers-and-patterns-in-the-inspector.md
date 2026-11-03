# 0173. Rich text, merge tags, pickers and patterns in the inspector

- Status: accepted; builds on [ADR-0065](0065-the-merge-tag-service-and-its-sources.md), [ADR-0066](0066-answers-as-merge-tags.md),
  [ADR-0005](0005-shared-logic-and-test-vectors.md), [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md),
  [ADR-0170](0170-the-inspector.md) and [ADR-0172](0172-the-option-editor-and-the-option-sources.md)
- Date: 2026-11-03
- Roadmap step: 9.4c

## Context

The inspector of 9.4a draws a type's plain settings and leaves out four kinds the field types and option sources
serve: rich text (`localizedHtml`: a consent's wording, a paragraph, a checkbox's text), Umbraco items (`mediaPicker`
for an image, `contentPicker`, `documentTypePicker` and `dataTypePicker` for the content and data type option kinds),
merge tags in the texts that take them (`supportsMergeTags`, and a field's default value, ADR-0118), and a field's
pattern check (`patternCheck`, a `pattern` rule in its `validation`, ADR-0005). The merge tag service already knows
which paths each place takes (`IFormBuilderMergeTagService.Paths`), but nothing serves them.

Five questions came up:

1. **Rich text:** Umbraco's own `umb-input-tiptap`, configured through its extension registry, or an editor of our own
   on the Tiptap that Umbraco ships.
2. **The tag picker's paths:** which place each box is, whose draft the paths come from, and who words them.
3. **Which boxes get the picker**, within the inspector's calm rules (IA 5.1, "say it once").
4. **The pickers:** Umbraco's input elements, or buttons of our own that open Umbraco's modals.
5. **The pattern:** a setting, or a Validation item; one pattern or a list; where the tester's sample lives.

## Options

1. **Rich text.** `umb-input-tiptap` brings Umbraco's toolbar and look, but it is configured by extension aliases meant
   for Umbraco's own property editor, it keeps its editor private, so a merge tag could only be inserted through a
   private member, and its extensions allow images, tables and styles the server's sanitiser removes (ADR-0084, so an
   editor would write what visitors never see). **An editor of our own, `jfb-rich-text`, on the Tiptap that
   `@umbraco-cms/backoffice/tiptap` exports**: no new dependency, exactly the marks and nodes the sanitiser keeps that a
   consent or a paragraph needs (bold, italic, links, bulleted and numbered lists, paragraphs and line breaks), a public
   way to insert a tag, and UUI's look. Chosen.
2. **Paths.** `GET` the saved draft's paths (a field added since the last save is missing), or **`POST
   form/{key}/merge-tag-paths` with the draft the designer holds and the place**, for users with `design` on the form,
   as `POST form/{key}/options` does (ADR-0172). Chosen. The server labels a field's path with the field's label in the
   culture asked (a `#` key through the dictionary); the backoffice words the built-in paths from its dictionaries, and
   a site's own paths show as written, under their source. Each path says its source and whether the site added it.
3. **Which boxes.** A picker button beside every label, help and placeholder crowds Essentials for tags that are rare
   there; **the picker goes with the texts made for tags: every setting the type marks `supportsMergeTags` and a
   field's default value**, as "Insert merge tag" under its box. The place follows the text: a consent's wording is
   `consentText`, a default value `defaultValue`, every other setting `visitorText`. A label, help or placeholder still
   takes a typed tag, which the server checks as before; 15.x may revisit.
4. **Pickers.** **Umbraco's input elements**: `umb-input-document` for `contentPicker` (and the actions' `documentPicker`,
   one editor for both), `umb-input-media` for `mediaPicker`, `umb-input-document-type` for `documentTypePicker`,
   `umb-data-type-input` for `dataTypePicker`. They show each item's name and icon, open Umbraco's own pickers, and read
   only when a lock holds the setting, so an editor meets the pickers they know. Chosen; buttons of our own would
   redraw what Umbraco already draws.
5. **The pattern.** **A Validation item, "Must match a pattern"**, for types with the `patternCheck` feature: the
   pattern, its message, and a "Try it" box that says at once whether a sample matches, through `@jfb/logic`'s
   `compilePattern`, the browser's twin of the server's matcher (D10). A pattern outside the safe subset, or too long,
   is said under its box (publishing refuses it as before), and "Try it" waits until the matcher takes it. The sample is
   never saved and is forgotten when another element is selected. The item edits a field's first pattern rule, in the
   place it has among the field's checks; other pattern rules a draft brought from elsewhere stay as they are, and an
   emptied pattern removes its rule.

## Decision

- **`jfb-rich-text`** edits a `localizedHtml` setting in the language shown: a toolbar of Bold, Italic, Link, Bulleted
  list and Numbered list (each a toggle with its state, Ctrl+B and Ctrl+I as usual), a link edited in a small popover
  (an address with the schemes the sanitiser keeps, or a path on the site, and "Open in a new tab"), its own Undo while
  it has the focus, and a change sent when it loses it or pauses, one step of the designer's history per visit.
- **`POST form/{key}/merge-tag-paths`** (`design` on the form): `{ place, culture, definition }` answers the place's
  paths, each with its source, whether the site added it, and a field's label. Held to `docs/api/merge-tag-paths.json`
  for a form with fields and the Testing project's site source in `visitorText` and `consentText`; the backoffice's
  dictionaries check holds every built-in path in it to an English and a German text.
- **The tag picker** ("Insert merge tag", copy.md) opens a popover under its box: a search box, then the paths by
  source (Fields by their labels first), each a button that inserts `{{ path }}` at the caret and gives the focus back to
  the box. Escape closes it and gives the focus back to its button.
- **The pickers** are Umbraco's inputs, a key or a list of keys as the setting's kind says; a locked one reads only.
- **"Options from"** offers content and a data type, configured through their served schemas with these pickers.
- **"Must match a pattern"** sits in Validation with the field's other checks; its summary is the pattern itself.

## Consequences

- The inspector draws every editor the field types and option sources serve; 10.x adds the actions' own (field and
  recipient maps, layouts).
- 10.2's email editor uses the same paths endpoint for `mail` and its own rich text on `jfb-rich-text`; its permission
  question (an actions editor without `design`) is 10.2's.
- A label, help or placeholder has no picker; typed tags there keep working and are checked as before.
- Tiptap comes from Umbraco's own bundle, so its version follows Umbraco's; nothing new ships.
