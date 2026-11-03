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

Six questions came up:

1. **Rich text:** Umbraco's own `umb-input-tiptap`, configured through its extension registry, or an editor of our own
   on the Tiptap that Umbraco ships.
2. **The tag picker's paths:** which place each box is, whose draft the paths come from, and who words them.
3. **Which boxes get the picker**, within the inspector's calm rules (IA 5.1, "say it once").
4. **The pickers:** Umbraco's input elements, or buttons of our own that open Umbraco's modals.
5. **The pattern:** a setting, or a Validation item; one pattern or a list; where the tester's sample lives.
6. **Rich text in a label:** a consent's and a checkbox's text sit in the visitor's `<label>`, which may hold only
   words, links and line breaks, never a paragraph or a list.

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
   a site's own paths show as written, under their source. Each path says its source and whether the site added it. The
   request names the element whose text it is, so a visitor text is offered only the answers of the fields before it,
   the ones its check accepts (ADR-0068).
3. **Which boxes.** A picker button beside every label, help and placeholder crowds Essentials for tags that are rare
   there; **the picker goes with the texts made for tags: every setting the type marks `supportsMergeTags` and a
   field's default value**, as "Insert merge tag" under its box. The place follows the text: a consent's wording is
   `consentText`, a default value `defaultValue`, every other setting `visitorText`. A label, help or placeholder still
   takes a typed tag, which the server checks as before; 15.x may revisit.
4. **Pickers.** **Umbraco's input elements** for content and media: `umb-input-document` for `contentPicker` (and the
   actions' `documentPicker`, one editor for both) and `umb-input-media` for `mediaPicker`. They show each item's
   name and icon and open Umbraco's own pickers, so an editor meets the pickers they know, within the start nodes and
   permissions Umbraco gives them. Umbraco's document type and data type pickers were tried and dropped: Umbraco lists
   those types only to users of its Settings section, which most form designers don't have, so the picker opened on "No
   items". **The site's document types a page may have (element types left out) and its data types that hold a list of
   values (dropdowns, checkbox lists, radio button lists) come with the setting as its choices** from the option sources
   endpoint, which any user of the Forms section may read: ticks for a list, a select for one. A type the site no longer
   has stays shown while it is picked, so it can be taken away. The names describe the site's structure, not its
   content.
5. **The pattern.** **A Validation item, "Must match a pattern"**, for types with the `patternCheck` feature: the
   pattern, its message, and a "Try it" box that says at once whether a sample matches, through `@jfb/logic`'s
   `compilePattern`, the browser's twin of the server's matcher (D10). A pattern outside the safe subset, or too long,
   is said under its box (publishing refuses it as before), and "Try it" waits until the matcher takes it. The sample is
   never saved and is forgotten when another element is selected. The item edits a field's first pattern rule, in the
   place it has among the field's checks; other pattern rules a draft brought from elsewhere stay as they are. An
   emptied box keeps its rule, message and all, while it is typed in, and removes it when it is left empty, so the next
   keys never edit the next rule.
6. **Inline text.** Unwrapping the editor's paragraph only when the site renders, or **an editor of its own kind,
   `inlineHtml`, which the type names for a text in a label**: `jfb-rich-text` then holds one paragraph, written
   without it, offers Bold, Italic and Link, and takes a line break with Shift+Enter. Chosen: the draft holds what the
   label shows, the canvas shows the same, and a type of a site's own names the same editor for such a text.

## Decision

- **`jfb-rich-text`** edits a `localizedHtml` setting in the language shown: a toolbar of Bold, Italic, Link, Bulleted
  list and Numbered list, one stop for the keyboard whose arrow keys move between its buttons (each list and mark a
  toggle with its state, Ctrl+B and Ctrl+I as usual; Link says whether its panel is open), a link edited in a small
  panel (an address with the schemes the sanitiser keeps, or a path on the site, which starts with one slash and never
  two or a backslash, and "Open in a new tab"), its own Undo while it has the focus, and its text sent as it is typed
  and once more when it loses the focus after typing, one step of the designer's history per visit; a visit without
  typing changes nothing. A text set from outside (the designer's Undo, another language, another field) starts a new
  editor, with nothing of the last text to undo. A link holds only its address, and a target where a new tab is asked;
  the server adds what such a link needs, as it does for any rich text (ADR-0084). Another language shows the default's
  words, without their markup, as its hint. An `inlineHtml` setting (a consent's and a checkbox's text) is one line.
- **`POST form/{key}/merge-tag-paths`** (`design` on the form): `{ place, culture, definition, owner }` answers the
  place's paths, each with its source, whether the site added it, and a field's label; a visitor text's only the answers
  of the fields before its owner. Held to `docs/api/merge-tag-paths.json`
  for a form with fields and the Testing project's site source in `visitorText` and `consentText`; the backoffice's
  dictionaries check holds every built-in path in it to an English and a German text.
- **The tag picker** ("Insert merge tag", copy.md) opens a popover under its box: a search box, then the paths by
  source (Fields by their labels first), each a button that inserts `{{ path }}` at the caret and gives the focus back to
  the box. Escape closes it and gives the focus back to its button, and the focus leaving it closes it, so it never
  covers the box the focus went to.
- **The pickers** are Umbraco's inputs for content and media, in a group named by the setting's label so their own
  buttons are said with it, a key or a list of keys as the setting's kind says; document types and data types are the
  site's, served as the setting's choices. A locked one shows how many it holds and the key that locks it (ADR-0060).
- **"Options from"** offers content and a data type, configured through their served schemas with these editors.
- **"Must match a pattern"** sits in Validation with the field's other checks; its summary is the pattern itself.

## Consequences

- The inspector draws every editor the field types and option sources serve; 10.x adds the actions' own (field and
  recipient maps, layouts).
- 10.2's email editor uses the same paths endpoint for `mail` and its own rich text on `jfb-rich-text`; its permission
  question (an actions editor without `design`) is 10.2's.
- A label, help or placeholder has no picker; typed tags there keep working and are checked as before.
- Tiptap comes from Umbraco's own bundle, so its version follows Umbraco's; nothing new ships.
- 10.2's content action, whose settings name document types too, serves the same choices for them (the step says so).
- A label that names a dictionary item (`#key`) is read for the paths in the culture asked, as for the options of
  ADR-0172; dictionary items are the site's own words for its visitors, and the endpoint needs `design` on the form.
