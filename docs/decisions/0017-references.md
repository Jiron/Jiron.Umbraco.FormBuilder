# 0017. References

- Status: accepted; the rebuild runs as a job that the plan's step 4 asks for, and the endpoint's answer is set out, in
  [ADR-0131](0131-keeping-used-on-current.md)
- Date: 2026-10-22
- Roadmap step: 1.2b (decided); built in 6.7 and used in 9.2

## Context

Editors need to know where a form is used ("used on") before they trash or delete it (9.2), and the form
workspace should list those pages. §3.4 left open whether Umbraco's own reference tracking can hold a custom
entity such as a form; §4.5 reserves a `jfbReference` table for the case that it cannot; §4.7 has a references
endpoint.

What the sandbox showed on 17.7.1 ([research](../research/umbraco-backoffice.md), "References"):

- An `IDataValueReferenceFactory` for the form picker, returning `UmbracoEntityReference(new GuidUdi(<type>, id))`
  with the UDI type registered through `UdiParser.RegisterUdiType`, is called by Umbraco on every content save.
- **Umbraco keeps only references it can map to a node id.** It stores references as relations between
  `umbracoNode` rows; for our form it logged at Debug level that the reference has no node id and skipped it. The
  relation service works with integer node ids, and the management API's "referenced by" endpoints cover only
  documents, media, members and data types.
- `DataValueReferenceFactoryCollection` walks into block editors: a form picked inside a Block List item was found
  both by `GetAllReferences(properties, editors)` and, per property, by `GetReferences(editor, values)`. Both
  exist in 17.5.0; the overload of `GetAllReferences` with a third argument is newer and is not used.
- The backoffice's info app kind `entityReferences` takes any repository implementing
  `UmbEntityReferenceRepository`. With our own endpoint returning Umbraco's document reference model
  (`$type: "DocumentReferenceResponseModel"`), the form's Info view showed Umbraco's own "Referenced by" box with
  both pages, the document-type icons and a Draft badge. The kind exists in 17.5.0.

## Options

1. **Umbraco relations only** (a custom relation type through `IRelationService`). Relations need node ids on
   both ends; a form would need an `umbracoNode` row, which misuses Umbraco's tables and couples the package to
   their internals.
2. **Our own index, filled from Umbraco's reference walk, shown with Umbraco's info app.** Accurate, block-aware,
   cheap to read, and it looks exactly like Umbraco's own references.
3. **Scan the content on demand** when someone asks. No extra table, but every request reads every page's
   property values: seconds on a large site, and impossible inside a delete confirmation.
4. **Make forms content nodes.** Gains relations for free, at the price of the whole data model (D-level
   decisions on storage, versions and permissions) for one feature.

## Decision

Option 2.

- **Reporting:** the form picker's data editor has an `IDataValueReferenceFactory`, and its `IDataValueReference`
  returns one `UmbracoEntityReference` per picked form with the UDI type `jfb-form`, registered once at start-up.
  Any editor that stores a form id reports it the same way, so the walk also finds forms in blocks and nested
  content.
- **The index:** the `jfbReference` table of §4.5 (FormId, ContentKey, PropertyAlias, Culture, UpdatedUtc). For each
  property of a content item, `DataValueReferenceFactoryCollection.GetReferences(editor, values)` runs over the
  edited and the published value of each culture, and one row is written per form, property and culture. A form
  used only in a draft counts: publishing that draft would break the page.
- **Keeping it current:** notification handlers for content saved, published, unpublished, moved to the recycle
  bin, restored, copied and deleted replace the rows of the affected items in one scope. The handler runs on the
  server that saved the content, and the table is shared, so load-balanced sites need nothing more.
- **Rebuilding:** a full rebuild pages through all content. It runs in the package migration that creates the
  table, and as an action on Jiron Form Builder's health check, for sites where content changed while the package
  was disabled or where a property editor changed.
- **Reading:** `GET /umbraco/jfb/management/api/v1/form/{id}/referenced-by?skip&take` (§4.7) returns Umbraco's
  document reference model. Names, icons and the published state come from Umbraco at request time, so a renamed
  page needs no re-index. Pages outside the user's content start nodes are not named: the response counts them,
  and the Info view adds one line saying how many more pages use the form.
- **Showing:** the form workspace's Info view uses the `entityReferences` info-app kind with our repository, so the
  list looks and behaves like Umbraco's own. Trash and delete confirmations (9.2) name the pages from the same
  endpoint.

## Consequences

- One table, a handful of handlers and a rebuild to maintain; the handlers' cost is one reference walk per saved
  item, which Umbraco already does for its own relations.
- References from code (a form id in a Razor view or a controller) and from outside content are not tracked. The
  UI says "used on pages", and the docs say what is not covered.
- Umbraco writes a Debug log line for each skipped form reference. It is harmless and invisible at the default
  log level.
- Revisit when Umbraco can store references to entities without a node id: then a migration moves the rows into
  Umbraco's relations and drops `jfbReference`.
