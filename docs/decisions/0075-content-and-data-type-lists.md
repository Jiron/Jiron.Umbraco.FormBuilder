# 0075. Content and data type lists

- Status: accepted; amends [ADR-0073](0073-option-lists-and-sources.md) (what a configuration names on the site is
  checked when a list is saved and a form is published, never when a stored list is read; `cacheSeconds` belongs to
  the kinds whose options change without the site saying so)
- Date: 2026-10-27
- Roadmap step: 5.5b (decided and built); 5.5c's URL kind keeps its options for `cacheSeconds`, 5.5d holds answers to
  them, 9.x and 12.1 build the editors (a content picker, a document type picker, a data type picker)

## Context

5.5b brings a list's options from the site: from content pages and from a data type's values. ADR-0073 left open:

1. **Content's configuration:** where the pages start, which pages, what an option's value and label are, and in
   which order.
2. **Which pages a visitor may be offered:** published ones, in the visitor's language, and none that needs a member
   to sign in.
3. **What a configuration names on the site:** a page, document types, properties, a data type. The site changes
   them on its own, and the kind's own check runs whenever a stored list is read, which keeps the list until the list
   itself changes.
4. **Keeping the options:** a content list may walk thousands of pages, and a change an editor publishes must show
   at once, on every server.

## Options

1. **What a configuration names on the site.**
   - Checked with the kind's own check, also when a stored list is read: a list whose page went to the recycle bin
     would stay unreadable after the page comes back, until someone saves the list again.
   - **Checked in a check of its own when a list is saved and a form is published,** and when options are resolved a
     page or data type that is gone makes the source fail (no options, logged). Chosen.
2. **Keeping content options.**
   - For the list's `cacheSeconds`, as a code list's: a published change would show only after it, and a field's own
     source would keep nothing.
   - Read on every request: a list of every page below the home walks the tree and converts properties per page view.
   - **Kept until the site changes:** per configuration, root and culture, and dropped on every server when Umbraco's
     own cache refreshers tell of a change to content, document types, data types, public access or languages.
     Chosen; `cacheSeconds` then means nothing for these lists.
3. **An option's value.**
   - The page's name: readable, but two pages may share one, and a renamed page changes what stored answers mean.
   - **The page's key by default, or its name, or a property's value;** the label the page's name by default or a
     property's value. Chosen.
4. **`cacheSeconds` on a list that does not use it.**
   - Ignored: a setting that does nothing would be stored and shown.
   - **Refused unless it is 0,** for every kind that keeps nothing for a time (manual, content, data type, countries).
     Chosen.

## Decision

- **Content** (`content`, `ContentOptionListSettings`):
  - `root`: the page whose pages are offered; without one, the page the form is on, and with no such page there are no
    options;
  - `descendants`: every page below the root, depth first, instead of its children only;
  - `documentTypes`: the document types offered, by key; with `descendants`, the pages below a page of another type
    are still read;
  - `valueFrom`: `key` (by default), `name` or a property's alias; `labelFrom`: `name` (by default) or a property's
    alias, the page's name when the property has no text;
  - `order`: `tree`, as the content tree shows the pages, or `label`, by the labels in the culture's order.
- **A property's value** reads as merge tags show it (ADR-0067): as a value, a number or a date in its wire form and
  text as it is; a property that holds several items, or one of a kind without text, gives no value, which leaves the
  page out. As a label, every item's text, joined with a comma.
- **Which pages:** as published, never a draft, even in a preview; with a name in the culture (the default language
  for none); below pages that are on the site in that culture; and not behind public access, on the page or above it.
  At most one page more than a list offers (10,001) is read.
- **Data type** (`dataType`, `DataTypeOptionListSettings`): `dataType`, a dropdown, checkbox list or radio button list,
  required; its values in its order, each value its own label in every culture.
- **Checks.** The kind's own check, on every read: `valueFrom` and `labelFrom` are their words or an alias as Umbraco
  writes one (a letter or `_`, then letters, digits and `_`, at most 255), and a key is no label. What it names on the
  site (`OptionKind.CheckOnSiteAsync`, when a list is saved and a form is published): the root is a page outside the
  recycle bin, each document type exists and has every property named (compositions included), and the data type
  exists and is one of the three editors. `OptionListService.CheckAsync` and `OptionSourcePublishCheck` run it.
- **Keeping.** Each server keeps content options under `Jiron.FormBuilder.OptionSource.content.` (a hash of the
  configuration, the root and the culture) and data type options under `Jiron.FormBuilder.OptionSource.dataType.`
  (the data type's key), read once per server through the runtime cache's loads (ADR-0043), for 12 hours at most.
  `OptionSiteCacheHandler` drops content options on Umbraco's refresher notifications for content, document types,
  data types, public access and languages, and data type options on those for data types; Umbraco publishes them on
  each server once its refreshers ran there. A source that fails keeps nothing. Fields' sources and library lists
  share what is kept.
- **`cacheSeconds`** is for code lists (and 5.5c's URL lists): any other list with another value is refused at
  `$.cacheSeconds` (`invalid_value`), and a stored one is not kept for a time.

## Consequences

- ADR-0073's kinds gain a check of what they name on the site, async, and `OptionListService.Check` becomes
  `CheckAsync`; a stored list is held to its schema and own check only.
- 12.1 offers `cacheSeconds` only for code and URL lists, and the editors of `root`, `documentTypes` and `dataType` are
  pickers; their `#` keys join the backoffice's dictionaries with them.
- 5.5d holds an answer to the options a page or data type gives now: an answer whose page was unpublished since is
  refused like any answer a list lacks.
- **Revisit** if a list should offer pages only signed-in members may see, or cascade by the parent field's chosen page
  as its root.
