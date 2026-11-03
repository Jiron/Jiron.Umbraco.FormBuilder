# 0172. The option editor and the option sources in the inspector

- Status: accepted; builds on [ADR-0073](0073-option-lists-and-sources.md), [ADR-0075](0075-content-and-data-type-lists.md),
  [ADR-0076](0076-url-and-file-lists.md), [ADR-0016](0016-designer-interaction.md) and [ADR-0170](0170-the-inspector.md)
- Date: 2026-11-03
- Roadmap step: 9.4b (decided and built); 9.4c adds the content and data type kinds with its pickers; 12.1 builds the
  library's own editors for option lists

## Context

A choice field takes its options from itself, from a library option list, or from a kind configured on the field
(ADR-0073). The server resolves all of them, checks a source's configuration when a form is published, and reads each
kind's configuration into a settings schema, but nothing serves those schemas or the site's code sources, nothing lists
the library's lists for a picker, and the canvas draws only a field's own options. The inspector of 9.4a draws plain
settings and leaves options out. Step 9.4b gives the inspector its option editor and its source picker.

Four questions came up:

1. **What the endpoint serves.** Every kind, or those a field may configure; and how the picker learns the library's
   lists, which no endpoint lists yet.
2. **How the canvas shows options from elsewhere.** A list, a code source or a configured kind resolves on the server,
   in a culture, and a draft is not published.
3. **The kinds whose settings need pickers.** Content (a root page, document types) and a data type are chosen with
   Umbraco's pickers, which the inspector's generic renderer gets in 9.4c.
4. **Editing the options themselves** within the inspector's calm rules (IA 5.1): reordering, pasting, the details an
   option may have, a grid's rows.

## Options

1. **The kinds:** serve all seven kinds, and let the client leave out what a field can't use; or **serve the five a
   field may configure** (`content`, `dataType`, `url`, `countries`, `code`), as the publish check allows them, with
   the code sources' names and labels. Chosen: a field never picks `manual` or `file`.
2. **The library's lists:** a list with each list's options resolved, so every list says how many it offers (a JSON
   address can take seconds, and one slow list would hold the whole picker back); or **each list's name, kind and, for
   a list that holds its options, their number**, while a list that resolves them says its kind. Chosen; the canvas
   shows what a chosen list resolves.
3. **The canvas:** resolve in the browser (it can't reach content, data types or code sources); or **a resolve endpoint
   for a field's options as the draft holds them**, in the canvas's culture. Chosen, as `POST form/{key}/options`, and
   it needs `design` on the form: a configured JSON address makes the server call it, which only someone who may
   design the form may ask for.
4. **Content and data type kinds now:** draw them without their pickers (a content list with no root, a data type list
   that can't name its data type); or **offer them once 9.4c brings the pickers**. Chosen; the endpoint serves their
   schemas now and their texts join the dictionaries now, so 9.4c adds editors only.

## Decision

- **`GET option-source`** (any user of the section, as the field types are): the kinds a field may configure, in the
  enum's order, each with its settings schema as the field types endpoint serves a type's (labels and descriptions as
  `#jfbOptionListSettings_*` and `#jfbOptionListChoices_*` keys), and the site's code sources in the registry's order,
  each with its name and its label (`#jfbOptionSources_*` for the standard lists). Its answer is held to
  `docs/api/option-sources.json`, and the backoffice's dictionaries check holds every `#` key in it to an English and a
  German text, as for the field and action types.
- **`GET option-list`** (any user of the section): the library's lists by name, each with its key, alias, kind and, for
  a manual or uploaded list, how many options it holds.
- **`POST form/{key}/options`** (`design` on the form): a field's options as the draft holds them, resolved in a culture
  through the option service, with whether the source was available. A cascading field's parent is left out, so the
  canvas shows every option a designer wrote or a source gives, not those of one answer.
- **The source picker**, in Essentials as IA 5.1 places it: "Options from" offers this field, an option list, a
  standard list (the code sources), the country list and a JSON address; content and a data type join with 9.4c.
  Changing the source keeps only what the new one holds (the publish rules: a list has no items or configuration, a
  kind no items), and Undo brings the rest back. A kind's configuration is drawn by the generic renderer over its
  served schema, at `options.config`. "Depends on" picks a parent among the choice fields before it in reading order,
  which can't make a cycle, and an own option then says which of the parent's answers it is shown for.
- **The option editor** lists a field's own options, each as a row with its handle, its label in the language shown
  and a button for its details: the value (taken from the label when it is added, as visitors' answers store it),
  a place limit for types that count places, a description and an image for a radio or checkbox field shown as cards,
  by its own layout or the form's (the image through Umbraco's media picker), and the parent's answers it is shown for;
  a new parent, or none, clears those answers. A row moves by its
  handle with the pointer (the backoffice's drag of ADR-0016, with the insertion line) and with Alt+Up and Alt+Down,
  which says where it went. "Paste options…" takes one option a line and adds those whose values are new, saying how
  many were already there. A grid edits its rows the same way (a statement and whether it needs an answer) and its
  columns as its options or its scale.

## Consequences

- 9.4c adds the content picker, the document type picker and the data type picker to the generic renderer, and with
  them the content and data type kinds in "Options from"; its text says so.
- The canvas asks the server once per field and source as the draft holds it, and again only when it changes or the
  language does, once the changes pause for 400 ms, so a JSON address typed key by key is fetched only once it is
  typed; a source that is unavailable, or configured only in part, shows no options until it gives some, and such an
  answer is not kept, so the next look asks again.
- The canvas reads its images (a card's, an image field's) through Umbraco's imaging, as the media picker's thumbnails
  are read: Umbraco's media URLs need its Media section, which a form designer may not have. A user with neither the
  Content nor the Media section sees no images on the canvas; the site shows them.
- A paste stops at the server's default limits, 1,000 options of a field's own and 300 rows of a grid; a host that sets
  others hears of them from the save, as the client is not told the host's limits.
- 12.1's library screens add creating and editing lists; the picker's "Create an option list…" (IA 5.1) comes with
  them.
- Revisit the counts if sites keep hundreds of lists that resolve, or if a picker needs to search them.
