# 0104. The output cache rules and the render endpoint

- Status: accepted; builds [ADR-0004](0004-rendering-and-submission-paths.md)'s output cache rules and its `render`
  endpoint, and adds a second way of keeping a response out of Umbraco's cache to its `no-store`; keeps the renders
  [ADR-0095](0095-visitor-texts-members-only-forms-and-the-pending-note.md) calls per visitor out of the cache; the
  render endpoint's `theme` added in 6.3a by [ADR-0105](0105-the-theme-registry-and-the-base-rules.md)
- Date: 2026-10-29
- Roadmap step: 6.2l (decided and built); 6.4's runtime calls `render` after `form_changed`, 6.7 tags and evicts the
  pages that show a form, 14.3 adds the frame headers of a members-only page and of `render`

## Context

ADR-0004 lets a page with a form sit in Umbraco's website output cache or behind a CDN, because an anonymous form's
markup holds nothing per visitor. Some renders are per visitor all the same: the outcome after a post without
JavaScript (`?jfb-sent=`), the page a post renders again, a members-only form (its notice or its form, by who is signed
in, ADR-0095), and, while the migrations are pending, a page whose form shows a note to a backoffice user and nothing
to anyone else. ADR-0004 rules what keeps them out (`no-store`, a vary rule for `jfb-sent`, never a request filter);
6.2h already marked every response of the surface action. The spike (docs/research/spike-rendering.md, section 2)
found that Umbraco's cache stores whatever lacks `no-store` and `Set-Cookie`, whatever the method, and that its key
ignores the query string.

Two things the spike did not settle:

- **A header can come too late.** `Cache-Control` can only change before the response starts. A page that flushes
  its head early (some sites do, for speed) has started by the time the form renders, and the header would then
  throw or do nothing. Umbraco's cache is ASP.NET Core output caching, which offers its own switch for this:
  `IOutputCacheFeature.Context.AllowCacheStorage`, read when the response is complete.
- **Who counts as signed in.** Umbraco's default request filter leaves a signed-in member's request out of the cache,
  but a backoffice user is no member on a website page, and a site may replace the filter with one that caches
  members too.

The `render` endpoint of §4.6 must return what the page would show at that place, so that the runtime can swap the
published form in after `form_changed` (6.4) on a page that came from a cache.

## Options

**How a render keeps its page out of a cache:**

1. **`Cache-Control: no-store` only** (ADR-0004 as written). Every cache and browser honours it, but it cannot be set
   once the response has started.
2. **The output cache's own switch only.** It works however late, but only for Umbraco's own cache; a CDN or a browser
   never sees it.
3. **Both.** The header whenever it can still be set, again as the response starts (so a site's middleware that
   writes caching headers later cannot undo it), and the switch whenever the request runs under output caching.

**Which renders are per visitor:**

1. **Only what ADR-0004 lists by name** (answers, errors, an outcome). A members-only form for a visitor shows the
   same sign-in notice to everyone, so it could be cached; but the next request may be a member's, and a site's own
   filter decides whether a member's request reads the cache.
2. **Every render that depends on who asks or how:** a request other than `GET` or `HEAD`, a request that carries
   `jfb-sent` in any form, any members-only form for anyone, and any form while the migrations are pending for
   anyone.

**How `render` reaches the page's markup:**

1. **A second renderer for the endpoint.** Two paths that must stay alike; ADR-0004 asks for one.
2. **The view component, told where it renders.** The endpoint hands the component the page's instance id, path,
   content and culture, in place of the request's, and the component renders as on the page.

## Decision

Option 3 for keeping a page out, option 2 for what is per visitor, option 2 for `render`.

- **One way to say `no-store`** (`PageCaching.NoStore`): it sets `Cache-Control: no-store` while the response has not
  started and again as it starts, removing any `Expires`, and sets `AllowCacheStorage` to false on the request's
  output cache feature when there is one. The surface action, the reader of a post's files before routing, the view
  component and the vary rule all use it.
- **What the view component marks `no-store`:**
  - any render on a request other than `GET` or `HEAD` (a post to the page by anything, not only the surface action);
  - any render on a request whose query holds `jfb-sent`, in any letter case, with any value or none;
  - a members-only form (access other than "anyone"), for every visitor: the notice for one who is not signed in or not
    admitted, and the form for a member it admits;
  - while the migrations are pending, every page with a form, for a visitor (who sees nothing) and for a backoffice user
    (who sees the note): the two pages differ, so neither may answer for the other.
  - Nothing else: an anonymous form on a `GET` renders the same for everyone and stays cacheable; a reference that
    names no form renders nothing for anyone.
  - A page with a members-only form is therefore never cached, for anyone. A cache entry per member would need a vary
    rule on Umbraco's login cookie, and the render costs a fraction of a millisecond (ADR-0004).
- **The vary rule** (`SentVaryBy`, an `IWebsiteOutputCacheVaryByProvider`, added to Umbraco's collection once): on a
  request whose query holds `jfb-sent` it adds that name to the rule's query keys, keeping the site's own, and marks the
  response `no-store`; on any other request it does nothing. Jiron Form Builder never registers an
  `IWebsiteOutputCacheRequestFilter`, so the site's filter, Umbraco's or its own, stays in charge of everything else.
- **`GET forms/{key}/render?instance=&page=&contentKey=&culture=`** (with `theme` from 6.3a and `placement`, the page's token of ADR-0129, from 6.4j)**:**
  - `key` is the form's key only, never an alias: the endpoint renders a form whose key the page already holds. It
    renders every published form not in the trash, as the `options` endpoint answers it; the headless setting governs
    the JSON projection only, since a page shows the form's markup anyway.
  - `instance` (required) is an instance id as a page gives it: `jfb-` and a whole number from 1, without a leading
    zero, at most 16 characters. The ids a post names are held to the same rule.
  - `page` (required) is a local path as the browser's `location.pathname` gives it: one leading `/`, then only the
    characters of a URL path (no query, no fragment, no backslash, no `//` at the start), every `%` followed by two hex
    digits, at most 2,048 characters. It becomes the form's action as given, with the file marker and the fragment the
    page adds.
  - `contentKey` and `culture` are read as the form endpoint reads them (the site's settings and languages).
  - The controller puts the target (instance, page, content, culture) into the request's items and returns the view
    component without its assets, rendered with the page's culture as the current culture and UI culture, as Umbraco
    sets them on a page, so a site's own views (one that reads the dictionary, say) render as they do there.
    ASP.NET Core's query-string culture provider would often read the `culture` parameter already, but a site may drop
    it. `FormRendering` takes the target in place of the request's page, culture and next instance
    id, and shows no outcome: a `jfb-sent` on the request is ignored. The answer is `200` `text/html` with the form's
    markup only, `no-store` like every answer of the public API but the form and the options.
  - A members-only form follows the member signed in on the request, whose login cookie the runtime's same-origin
    request carries; the frame headers ADR-0004 gives such a page and `render` come with members' submissions in 14.3.
  - The endpoint takes no context values: the runtime carries the page's `jfb-context-*` inputs across with the answers
    (6.4).
  - Rate-limit policy `read`, no body, 20 seconds (a field's options may come from a source); `400 invalid_request`
    naming the parameter, `404 form_not_found` for a key that names no published form, and `503` while the migrations
    are pending, from the frame.

## Consequences

- Umbraco's cache cannot store a per-visitor render even when the page started its response before the form, and a
  CDN sees `no-store` whenever the header could still be written. A page that flushed early and sits behind a CDN that
  caches by its own rules is the site's to exclude; the rendering docs (Phase 17) say so.
- The tests run with Umbraco's output cache on and a request filter that caches signed-in requests too, so the
  members-only and pending checks prove the package's own rules rather than Umbraco's default filter; `render`'s tests
  run on a site that reads no culture from the query string, so the culture it renders in is its own doing.
- 6.4's runtime calls `render` with the form's instance and culture, `location.pathname` and the page's content
  key, and swaps the answer in (ADR-0004).
- 6.7 adds the tag provider and eviction on publish; 14.3 the frame headers.
- **Revisit** if Umbraco's cache starts to vary by query string, to honour the method, or to read a signal of its own
  for responses that must not be stored.
