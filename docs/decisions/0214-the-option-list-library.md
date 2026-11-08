# 0214. The option list library

- Status: accepted
- Date: 2026-11-08
- Roadmap step: 12.1, its part 12.1 (ADR-0197); builds on [ADR-0073](0073-option-lists-and-sources.md) (kinds,
  configuration, resolving), [ADR-0075](0075-content-and-data-type-lists.md), [ADR-0076](0076-url-and-file-lists.md)
  (URL and file lists), [ADR-0037](0037-library-and-permission-repositories.md) (the library's rows and deletes),
  [ADR-0172](0172-the-option-editor-and-the-option-sources.md) and
  [ADR-0173](0173-rich-text-merge-tags-pickers-and-patterns-in-the-inspector.md) (the
  inspector's option editor and the kinds' served schemas); IA 7, `docs/ux/wireframes/option-list.html`

## Context

Library › Option lists has shown the coming screen since 9.1. Step 12.1 builds it: a list of the lists, an editor per
kind with a live preview of the options, CSV and TXT uploads, the time a list keeps what it fetches, and "used by". Its
check is Playwright creating a list of every kind through the screens, a URL list against the test site's stub, and axe
on the editor. The kinds, their configuration and their checks exist since 5.5 (`OptionListService`); the inspector's
option editor (9.4b) already draws every kind's configuration from the kinds' served schemas. What is new:

1. **"Used by".** Definitions name a list by key inside their JSON (ADR-0037). Nothing says which forms use a list, so
   neither the list's count nor a delete that would leave fields without options can be told.
2. **The screens:** where a list is created, edited and deleted, and how the editor per kind is built without drawing
   the kinds a second time.
3. **The preview:** the options of contents not saved yet, for every kind, the URL one included.
4. **Uploads,** the keep time, and Fetch now for an address whose answer changed.
5. **Who may do it,** and what is recorded.

The owner answers no questions during the run (CLAUDE.md rule 8), so each choice below is this record's.

## Options

1. **"Used by".**
   - Search the definitions when asked: a `LIKE` over every form's current versions for each list. Correct, but the
     list screen would scan every definition once per list, and a delete's check could not be one statement.
   - Keep a count on the list: every save would need to know what the version before used.
   - **A table of what each form's draft and live version use, written with the version** by the repository that
     writes it, in the same transaction, and filled for the forms there are by its migration. Chosen: one seek per list,
     a delete guard in the delete's own statement, and nothing that can drift, as no version is written another way.
2. **The editor.**
   - A page of our own with its own save, as Settings › General: no "unsaved changes" guard, and Umbraco's own
     patterns (the header's name, the footer's Save, the "…" of entity actions) drawn again.
   - **Umbraco's named detail workspace,** as the form workspace (9.3a): create and edit routes, the name in the header,
     Save in the footer, the entity actions in its "…", and leaving with unsaved changes asks. Chosen.
3. **The kinds' editors.**
   - A form per kind in the library: every kind's settings drawn twice, which the roadmap rules out ("the same texts").
   - **The inspector's option editor with its source fixed:** the kind's configuration by the generic renderer over the
     served schemas, a code list's source from the served names, and a manual list's items with every one of 9.4's
     moves. Chosen; the editor gains one switch.
4. **Deleting a list a form uses.**
   - Delete it, and let those fields offer nothing until someone notices: a published form loses its options at once.
   - Delete it and change the forms: a library screen would edit forms without their editors.
   - **Refuse while any form's draft or live version uses it,** a form in the recycle bin's included as it can come
     back, and name the forms in the dialog. Chosen, as a theme does (ADR-0037).

## Decisions

1. **`jfbFormLibraryUse`** (migration step 12, `jfb-012-form-library-uses`): per form, version (`draft` or `published`),
   kind of item (`optionList` so far) and key, with how many of the version's fields use it; primary key over all four,
   an index on the item, and the form's foreign key with cascade. A version document carries what its definition uses
   (`FormLibraryUses`, every field with a list source, the children of repeatable groups included); creating a form,
   saving its draft and publishing replace that version's rows in the write's own transaction, taking a form offline
   removes its live rows, and deleting it for good removes them all. The migration fills the table from the forms' draft
   and live versions with its own frozen reading of the stored JSON, which a unit test holds to the model's on the full
   fixture and on generated definitions.
2. **The guard:** an option list's delete is one statement with `NOT EXISTS` over the table, read with a lock on SQL
   Server, refused as `409 in_use`. A form that starts to use a list at the moment it is deleted is the form check's to
   report, as for themes.
3. **The endpoints,** all `manageLibrary`, which is granted for the whole section: `GET option-list/library` (a page of
   the lists by name, each with its kind, how many options a list that holds them has and how many forms use it), `GET
   option-list/editor` (the site's languages and what each kind keeps), `GET`, `POST`, `PUT`, `DELETE option-list/{key}`
   (changes against the version read, every problem at its path), `POST option-list/{key}/duplicate` (the name given and
   the first free alias after the list's own: `topics2`, `topics3`), `GET option-list/{key}/used-by` (the forms the user
   may open by name, with whether the draft, the live version or both use it and whether it is in the recycle bin; the
   others counted, never named), `POST option-list/preview`, `POST option-list/read-file`, `GET
   option-list/{key}/fetched` and `POST option-list/{key}/fetch`. The inspector's `GET option-list` stays open to every
   user of the section.
4. **The list screen:** Library › Option lists lists Name, Kind and Used by ("no forms", "1 form", "3 forms"), by name,
   with Load more; a row opens the list. Its footer's one action is **Create option list**, which asks the kind first in
   Umbraco's create dialog: Fixed options, Pages, A data type's values, A JSON address, An upload, Countries and A
   standard list, each with one line on where its options come from. Choosing one opens the new list's workspace. With no
   list, the screen says what lists are for in one sentence and offers the same button.
5. **The workspace** (`jfb-option-list`): the back arrow to the list, the name with its alias beside it in Umbraco's
   name-and-alias box, the alias made from the name until it is unlocked and held to a field alias's rule, then **Used
   by N forms**, a button that opens the forms as links (a form only in its draft says so, one in the recycle bin too,
   and the forms the user may not open are one line), and the "…" of Umbraco's entity actions: Duplicate, then
   Delete…. **Save** is the footer's primary. A new list has neither the button nor the "…" until it is saved.
   Delete… asks first, as a list goes for good; a list a form uses is not deleted, and Umbraco's information dialog
   names the forms instead, those the user may not open counted.
6. **The editor per kind,** the workspace's one view: a box headed by the kind's name, holding the inspector's option
   editor with its source fixed to the list's kind, so every kind reads and behaves as it does in the inspector:
   - **Fixed options:** the option rows, Add option and Paste options…, each option's details (what entries store, a
     place limit, a description and an image); a list serves fields of every choice type, so all of them are offered.
     Parent values an imported list holds are kept as they are; cascading is the field's (ADR-0073).
   - **An upload:** how many options the last file gave and **Upload a CSV or TXT file…** (Upload another file… once it
     has some), which a new list needs before it is saved; Fixed options has the same button under its rows, whose file
     replaces the options with an Undo. A file over the site's limit is refused before it is sent, and a line the server
     cannot read is said under the button.
   - **Pages, a data type, a JSON address, countries, a standard list:** the kind's settings by the generic renderer, with
     the site's document and data types as choices, and the standard lists by their names.
   - **The keep time** of a JSON address and a standard list, under its settings: Keep fetched options for (1 minute to
     30 days, an hour for a new JSON address) or Keep the options for (Don't keep, the default, to 30 days), with what it
     means in one line. A saved JSON address says once when this server last fetched it and how many options it gave,
     with **Fetch now**, which every server forgets its copy for and this one fetches again at once.
7. **The preview** beside the editor (under it below a 900 px workspace): the options of the contents as they are, saved
   or not, in the language chosen when the site has more than one (one switch drives the editor's texts and the
   preview), as a dropdown and as radio buttons, the first eight and then how many more; it follows each change after a
   pause of 400 ms. Contents a save would refuse say why in one line; a source that gives nothing says so; a page list
   that takes the page the form is on says its options show on that page.
8. **Recorded:** `optionList.created` (a duplicate's with what it copied), `optionList.saved`, `optionList.deleted` and
   `optionList.fetched`, each with the list's alias and kind.

## Consequences

- The library's count and delete guard read one table that every version write keeps current; a new kind of item (email
  layouts in this step's part 12.2) adds its kind and its rows, and a backfill of its own.
- The inspector's option editor serves both screens; a change to a kind's settings shows in both.
- A list whose forms are gone can be deleted at once; one that a form in the recycle bin uses waits for the form to be
  restored and changed, or deleted for good.
- **Revisit** if sites keep hundreds of forms in the recycle bin that block deletes, or if lists need to cascade from the
  library itself.
