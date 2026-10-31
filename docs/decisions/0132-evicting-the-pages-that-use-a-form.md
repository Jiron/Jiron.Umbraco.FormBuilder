# 0132. Evicting the pages that use a form

- Status: accepted; extends [ADR-0004](0004-rendering-and-submission-paths.md) (what evicts a form's pages, and on
  which servers)
- Date: 2026-10-31
- Roadmap step: 6.7d (decided and built)

## Context

ADR-0004 tags the pages that show a form with `jfb-form-<key>` from the "used on" index and evicts that tag when the
form is published, so a page behind Umbraco's website output cache shows the new version at once. Building it raised
three questions:

- **Which changes evict.** A publish changes what visitors see, but so do an unpublish (the page then shows nothing
  for the form), a rename (the form's name can be in its markup), a move to or from the trash and a delete.
- **Where the eviction runs.** Umbraco's output cache is in each server's memory; a form changes on one server.
- **What a tag provider may do.** Umbraco asks its tag providers synchronously when it stores a page, and a page must
  never fail to render because a tag could not be read.

## Options

- **What evicts:** (a) the publish only; (b) every change after which the website drops what it keeps of the form.
  (b) is chosen: the form cache already names exactly those changes (ADR-0084), so the pages follow what the website
  serves.
- **Where:** (a) a handler on the form's published notification, which runs on the server that published only;
  (b) a handler on the form cache refresher's notification, which runs on every server. (b) is chosen.

## Decision

- `FormCacheTags` (an `IWebsiteOutputCacheTagProvider`) tags a page that uses forms with `jfb-form` and
  `jfb-form-<key>` for each form the index names for it, read synchronously in one query (`PageForms`); a page that
  uses none gets no tag. When the read fails, the page gets `jfb-form` only and the failure is logged as a warning;
  the page renders and is cached as Umbraco decides.
- `FormCacheEviction` handles `FormBuilderFormCacheRefresherNotification`, which each server publishes when it runs
  the form cache refresher: a change to a form evicts `jfb-form-<key>`, a change to many forms (a folder's) or a
  message it cannot read evicts `jfb-form`. Umbraco registers its cache manager only while the output cache is on,
  so the handler asks for it when it runs and does nothing without it; a site without the cache starts as before.
- The integration tests run the refresher through a server messenger that delivers on the test server, as a site of
  one server does; the test host's own messenger delivers no cache message.

## Consequences

- A page the index does not know (a form named in a template or a view) stays cached until its duration ends;
  `form_changed` covers it (ADR-0004).
- A draft-only pick tags a page too, so a publish may evict a page that does not show the form yet: an extra render,
  never a stale one.
- The other servers evict when they run the refresher, usually within seconds, as they drop the form itself.
