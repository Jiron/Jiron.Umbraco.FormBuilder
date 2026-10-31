# 0127. The form picker in the Content Delivery API

- Status: accepted; builds the form picker's server side of [ADR-0017](0017-references.md)'s picker and
  the Delivery API output of section 4.9
- Date: 2026-10-31
- Roadmap step: 6.6c (decided and built); 6.7 builds the data type's settings, the backoffice picker, blocks and
  "used on"

## Context

A page picks a form with the form picker, a property editor. A headless site reads the page through Umbraco's
Content Delivery API and needs to know which form the page picked: `{ key, alias, name }`, and, when it asks for the
property's expansion (`expand=properties[form]`), the form itself as the definition endpoint answers it, in the
request's culture, so it can render the form without a second call. A form that is not published, or that does not
allow headless use (`developer.headless`), gives nothing.

Umbraco 17's Delivery API reads a property through `IDeliveryApiPropertyValueConverter`, whose methods are
synchronous. The form a page names is not in Umbraco's content cache: it is read through the package's published form
cache, which loads from the database asynchronously, and the visitor form of a culture needs the site's settings,
the dictionary items its texts name and its consents' wordings, all asynchronous. The quality bar forbids
sync-over-async.

## Options

1. **Block on the async services inside the converter.** Sync-over-async, refused by the quality bar; it also ties a
   thread for every database load.
2. **An in-memory directory of published forms, kept current by notifications,** read synchronously. The summary
   would fit; the visitor form per culture would need every site setting, dictionary item and wording cached and
   invalidated as well, a second copy of what the definition endpoint already computes.
3. **A marker the converter writes, resolved after the Delivery API's answer is made.** The converter writes, without
   any lookup, `{ "jfbForm": "<key>", "expand": true|false }`; a middleware in front of the Delivery API's routes reads
   each JSON answer, resolves every marker asynchronously through the same published form cache and the same visitor
   form the definition endpoint answers with, in the culture the Delivery API serves (`Accept-Language`), and writes
   `{ key, alias, name }` (and `form` when expanded), or `null`. Chosen.

## Decision

- **The property editor** `Jiron.FormBuilder.FormPicker` stores the picked form's key as text. Its Razor value
  converter gives `FormBuilderFormReference` for a key it can read, which `<jfb-render form="@...">` renders as any
  key (a form that is gone renders nothing, as it does there).
- **The Delivery API converter** writes the marker for a key, `null` for no key.
- **The middleware** runs only on the Delivery API's paths and JSON answers, reads the answer once, replaces each
  marker, and leaves every other byte as Umbraco wrote it; an answer without a marker passes untouched. A marker of a
  form that is missing, trashed, not published or not headless becomes `null`, so a headless site cannot tell those
  apart. The expanded `form` is byte for byte the `form` of the definition endpoint's answer for that culture and
  the page the marker is on (its `contentKey`): the nearest item around the marker that has a route, so a block's
  element counts as the page that holds it. Each distinct form, expansion and page is worked out once per answer.
- **Never a marker out:** a test asks the item and items routes and finds no `jfbForm` left, and the finder's unit
  test reads the answers of the item, items and query routes, the query route's from the search index, which the test
  host leaves empty.
- **The converters' cache levels** are `Element`: the marker holds only the key and whether it is expanded, so a page
  cached with it still resolves the form as it is now, renamed, unpublished or trashed.

## Consequences

- The Delivery API's answer for a page that picks a form costs one cached lookup per picked form, and an expansion
  the definition endpoint's work, once per form and page in the answer.
- Every Delivery API answer is held in memory until it is complete, so the middleware can replace markers; one
  without a marker is written as it came, with its own headers.
- 6.7 adds the data type's settings (one form or several, allowed folders, a theme and a redirect), whose values the
  converters then read; several forms become a list of the same objects.
