# 0232. The site of a request, an entry's site and a deleted root's settings

- Status: accepted; completes [ADR-0008](0008-settings-model.md) (the site of a request without content) and
  [ADR-0043](0043-backoffice-settings-sections.md) (a deleted root's rows), and keeps
  [ADR-0147](0147-the-site-that-protects-a-form.md) as it is
- Date: 2026-11-11
- Roadmap step: 14.1 (part 14.4, multi-site)

## Context

Settings are global with per-site overrides (ADR-0008, ADR-0043): `general` (theme, required marker, button labels,
texts, base URL, time zone) and `email` (sender, layout, team and forward addresses) field by field, `protection` whole.
A request's site has been the root of the content it names: the rendered page, a post's page, or `contentKey` in a
public call. Three things were left to this step:

1. **A request that names no page.** A headless site calls the public API without `contentKey`; ADR-0008 decided its site
   is the root whose Umbraco domain matches the request's host, which never becomes a URL, and the shared values when
   nothing matches. Nothing read the host yet.
2. **An entry outlives its request.** Its actions, notices, forwards and edits run later, and found the site again from
   the entry's page. An entry sent without a page, or whose page was deleted since, got the shared values: its mail came
   from the shared sender with links to the shared address, not its site's.
3. **A root deleted for good** left its `jfbSetting` rows behind. They were never read for pages again, but they still
   counted in the strictest protection of ADR-0147 and in the sites' lists.

A host is the visitor's choice, as a page is (ADR-0147): whatever reads it must not let a request loosen anything.

## Decision

1. **The site a host names** (`SiteHosts`). For a request that names no page, its site is the root of the content an
   Umbraco domain on the request's host is assigned to:
   - only a domain with a host counts: `/en` names none, so a single-host site with language paths keeps the shared
     values for headless calls, as before;
   - hosts compare in their ASCII form, in any letter case; a domain that names a port counts on that port alone, one
     without on every port;
   - a host whose domains lead to two sites (`example.com/a` on one root, `example.com/b` on another) names neither, so
     a request never gets a site by the order the domains were added in;
   - a domain on content in the Recycle Bin, or whose content is gone, names nothing;
   - a page the request names always comes first, whatever its host.
   The public definition, options, render and element endpoints, the submission pipeline (entries, page checks, drafts),
   the post without JavaScript and the Delivery API's expanded form read it, through `FormPages.PageAsync(content, host)`.
   The texts, labels, theme, time zone and mail of a headless call on a site's domain are then that site's.
2. **What protects a form never reads the host.** `SubmissionProtection.SettingsOfAsync` keeps ADR-0147: a page's site
   counts only when that page holds the form; a request without one, whatever its host, gets the strictest of the shared
   values and every site's. A host-named site has no page, so it can never choose the protection. A test sends a lax
   site's host and still gets the strict site's puzzle.
3. **An entry keeps its site** (`jfbEntry.siteKey`, migration step 15, `AddEntrySiteV1`): the root of its page, or of the
   site its host named. Its later reads (`FormPages.EntryPageAsync`) take the page's site while the page lives, as
   before, and the kept site once the page is gone or when it never named one; so its notices, forwards, edits and the
   mail its actions send follow the site it came from. Entries from before step 15, and those from a host of no site, keep
   none and get the shared values, as they did. Content that a cache still serves but the tree no longer holds counts as
   no page, never as a site of its own.
4. **A root deleted for good takes its settings with it** (`SiteSettingsRemoval`, on Umbraco's `ContentDeletedNotification`,
   which fires for an item emptied from the Recycle Bin and for one deleted at once): every row of its scope goes in one
   unit of work with a `settings.removed` audit entry per section, with the user who deleted it or none when Umbraco did;
   every server forgets the site's copies and the sections' site lists, through the settings cache refresher. A root in
   the Recycle Bin keeps its rows, since it may come back. Content that was no site removes nothing. A removal that fails
   is logged and never undoes the deletion.
5. **The base URL keeps ADR-0067's order:** a site's own value, then the shared value, then the site's first `https`
   domain, then Umbraco's application URL. A shared base URL is a choice the site's settings screen shows as inherited,
   so a site with a domain of its own and a shared value set follows the shared value until it sets its own or the shared
   value is cleared; without a shared value, its links follow its domain.

## Consequences

- A headless site on its own domain needs no `contentKey` to get its site's words, time zone, theme and mail, and its
  entries keep that site for every later mail.
- A request still cannot pick a laxer protection by choosing its host or its page.
- The settings table holds no rows for roots that are gone, so the strictest protection reads only sites that exist or
  may come back from the Recycle Bin.
- The backoffice's own defaults (the designer's default theme, the email layout preview's sender) stay the shared values:
  a form can be placed on several sites, so no one site's values belong to it there.
- Tests: `SiteHostsTests` (the host and domain rules), `MultiSiteTests` (two roots on `site-a.localhost` and
  `site-b.localhost`: a call by host, a host of two sites, an entry's site and mail after its page is gone, protection
  that never follows the host, and the rows of a deleted root), the migration's tests, and the Playwright spec
  `multi-site.spec.ts` on a host of its own with both roots.
