# 0004. Rendering and submission paths

- Status: accepted; members-only forms and the no-JS action amended in 1.8; the assets amended in 3.2 (ADR-0026);
  `<jfb-assets>` and its nonce built in 6.2a (ADR-0092); the view component, `<jfb-render>` and the views built in 6.2b
  (ADR-0093)
- Date: 2026-10-23
- Roadmap step: 1.4a (decided); built in 6.1 (the public API), 6.2 (Razor rendering, the no-JS post, the cache
  rules, `render`), 6.4 (the runtime) and 6.6 (headless); reviewed in 6.8 and 16.x

## Context

D9 renders forms on the server with Razor and enhances them with a small runtime; without JavaScript they still
submit and validate. D11 sends JavaScript submissions to `/jfb/api/v1`, outside `/umbraco`, and no-JS posts back to
their page through Umbraco's surface routing, so errors re-render the page. §3.4 and §4.10 require markup with
nothing per visitor, because sites may put pages behind an output cache or a CDN: the challenge is fetched on first
interaction, and anonymous forms carry no antiforgery token. §4.10 asks for a CSP-friendly runtime with a nonce
option. After a publish, a cached page can still show the old form, so this ADR also decides where the runtime gets
the new one after `form_changed`.

Step 1.4a built both paths in the sandbox on Umbraco 17.7.1, behind Umbraco's website output cache and a CSP, and
checked them in Edge with JavaScript on and off, under `dotnet build` and after `dotnet publish`
([research](../research/spike-rendering.md)). What it found that shapes the decision:

- **The website output cache ignores the query string.** The outcome after a no-JS post (`?jfb-sent=…`) came back as
  the cached form until a vary rule was added.
- **Vary-by and tag providers are collections; the request filter is a single service.** A package can add to the
  cache's rules, but replacing the filter would remove the site's own.
- **Umbraco's cache policy checks neither the method nor the status code;** only `Set-Cookie` and `no-store` block
  storage. In practice posts to a surface controller were never stored or replayed on 17.7.1, even without
  `no-store`, but that rests on behaviour Umbraco does not document.
- **`BeginUmbracoForm(…, antiforgery: false)` works,** but it always posts to the bare current path (no fragment),
  always adds `enctype="multipart/form-data"`, gives the form a random `id` on every render, and cannot target the
  page when the form is rendered outside it. `Html.SurfaceControllerHiddenInput<T>()` gives the same routing with
  our own `<form>` element.
- **A nonce cannot live on a cached page:** the cached HTML keeps an old nonce, and the browser blocked the runtime.
  `script-src 'self'` needs nothing per visitor; a nonce on an uncached page works, and the runtime's dynamic
  imports inherit it.
- **`SurfaceController.CurrentPage` throws** when the action is reached through the auto-route
  `/umbraco/surface/…` without Umbraco's route values.

## Options

**The no-JS post:**

1. **`BeginUmbracoForm` with its antiforgery token** (Umbraco's default). The token and its cookie make every page
   with a form uncacheable. Rejected for anonymous forms, and for members-only forms too, because D13 sets no cookies
   (below).
2. **`BeginUmbracoForm(…, antiforgery: false)`.** Cacheable, but with the limits above: no way back to the form after
   errors, a needless multipart encoding, a random id, and the wrong action when the form is rendered by the API.
3. **Our own `<form>` with `Html.SurfaceControllerHiddenInput<T>()`.** The same encrypted route token and surface
   routing, with the markup and the action fully ours.
4. **A classic post to `/jfb/api/v1`, redirecting back.** Errors could only travel back in the query string or in
   storage, and the page could not render them in its own layout. D11 rules it out.

**The state after a successful no-JS post (post/redirect/get):**

1. **A query value (`?jfb-sent=<instance>`)** with a vary rule and `no-store`.
2. **TempData or another cookie.** D13 allows no cookies, and the outcome would still need a way past the cache.
3. **No redirect:** the post renders the outcome. A reload posts again.

**Where the runtime gets the new form after `form_changed`:**

1. **Reload the page.** The reload can come from the same cache and show the old form again, and the answers are
   lost unless they are stored (D13 allows browser storage only for save and resume).
2. **Fetch the published form from the API** and swap it in, keeping the answers in memory.

## Decision

Option 3 for the no-JS post, option 1 for the state after it, option 2 for `form_changed`.

- **One rendering for both paths.** A view component renders the form; the `<jfb-render>` tag helper and
  `Component.InvokeAsync` call it, and `<jfb-assets>` writes the stylesheet and the module script once per page.
  Instance ids are deterministic per request (`jfb-1`, `jfb-2`, …), and field ids are `<instance>-<alias>`, so two
  forms on one page never collide. The markup of an anonymous form holds nothing per visitor: no antiforgery token, no
  challenge, no nonce, no time stamp (a members-only form's page is per member, below).
- **The no-JS post:**
  - The form is `<form method="post" action="<page path>#<instance>" novalidate>` (with
    `?jfb-post=<key>.<instance>` before the fragment when the form has a file field, ADR-0006), with
    `Html.SurfaceControllerHiddenInput<…>()` for Umbraco's route token. `novalidate` with `required` and the other
    constraint attributes still in place: the server's messages are the errors on both paths, not the browser's
    bubbles. `enctype="multipart/form-data"` only when the form has an upload field.
  - The surface action is `[HttpPost, IgnoreAntiforgeryToken]`, reads the form asynchronously, and first checks
    Umbraco's `UmbracoRouteValues` feature: reached through `/umbraco/surface/…` without a page, it answers `400`.
  - Errors, and a revision the pipeline cannot accept (§4.4), render the page again through
    `CurrentUmbracoPage()`, with the answers, the errors and any notice handed to the view component through
    `HttpContext.Items`, keyed by form and instance. The page opens at the form through the action's fragment.
  - Success redirects (`302`, which browsers follow with a `GET`) to `<the page's relative URL>?jfb-sent=<instance>#<instance>`,
    built by Jiron Form Builder because `RedirectToCurrentUmbracoPage(QueryString)` cannot add the fragment. The
    view component shows the outcome for that instance. A redirect outcome goes straight to its target.
- **The JavaScript path:** the public controller at `/jfb/api/v1` (§4.6), attribute-routed, anonymous, with
  ProblemDetails codes and `Cache-Control: no-store` on every response but the definition (`no-cache` with its `ETag`,
  ADR-0012). The runtime fetches the challenge on the visitor's first `focusin` or `input`, posts entries with a
  stable `submissionId`, loads its error display on demand, renders the same summary and inline errors as the server,
  and moves focus to the summary or the outcome.
- **The output cache rules** (Umbraco's website output cache, and any CDN that respects `Cache-Control`):
  - Jiron Form Builder never registers an `IWebsiteOutputCacheRequestFilter`, because that would replace the
    site's.
  - It registers an `IWebsiteOutputCacheVaryByProvider` that acts only on requests carrying `jfb-sent`: it adds
    `jfb-sent` to the vary rules and marks the response `no-store`. The outcome is always rendered fresh, and no
    value of `jfb-sent` can create a cache entry.
  - It marks `Cache-Control: no-store` on every response of the surface action, and on every page render that shows
    anything per visitor (answers, errors, an outcome), that happens on a request other than `GET` or `HEAD`, or
    that carries `jfb-sent`. This does not depend on Umbraco's undocumented handling of posts.
  - It tags pages that show a form with `jfb-form-<key>` through an `IWebsiteOutputCacheTagProvider`, using the
    "used on" index of ADR-0017, and evicts that tag through `IWebsiteOutputCacheManager` when the form is
    published. Pages the index does not know (a form named in a template) stay cached until their duration ends;
    `form_changed` covers them.
- **`form_changed`:** the runtime calls `GET /jfb/api/v1/forms/{key}/render?instance=&page=&contentKey=&culture=`,
  which renders the published form through the same view component, in the page's theme and culture, with the
  page's instance id. `page` must be a local path (it becomes the no-JS action); the response is `no-store`. The
  runtime parses the HTML in a `<template>` (inert), copies the answers by field name, except consent fields whose
  wording changed, which stay unticked, replaces the form, enhances it again with the same `submissionId`, and moves
  focus to a notice that says the form has changed (and, when it applies, that a consent needs agreeing to again). A
  no-JS post that meets `form_changed` renders the current form with the answers in the same way. The page is
  never reloaded. The headless element (6.6) fetches the definition (`GET forms/{keyOrAlias}`) instead.
- **CSP:**
  - The runtime has no inline script, no event handler attributes, no `eval` and no style attributes; its CSS is a
    file. On cached pages `script-src 'self'` is enough.
  - **The nonce option:** `<jfb-assets nonce="…">`, or, for sites whose CSP middleware keeps the nonce elsewhere,
    an optional `IFormBuilderCspNonceProvider` service that returns the request's nonce. The module script carries
    it, and the chunks it imports inherit it, with or without `'strict-dynamic'`.
  - A nonce belongs only on uncached pages; the docs explain why with the spike's result, and how to exclude such
    pages from the cache.
- **Members-only forms bind every post to the member** instead of using ASP.NET Core's antiforgery, whose cookie D13
  rules out:
  - Their pages are per member, so the view sends `Cache-Control: no-store`, which also keeps them out of Umbraco's
    output cache. In `Response.OnStarting`, so headers that site middleware adds later count too, it adds a separate
    `Content-Security-Policy: frame-ancestors 'self'` header (never replacing the site's own CSP) and
    `X-Frame-Options: SAMEORIGIN`, unless the response already has an `X-Frame-Options` header or a CSP with
    `frame-ancestors`. No other site can then frame the page and trick the member into sending it. `render` does the
    same for such a form.
  - Their challenge (ADR-0012) holds the signed-in member's key. The runtime fetches it from the same origin with
    `credentials: 'same-origin'` (it never uses `'include'`), so the browser sends the member's login cookie, and the
    no-JS form carries one rendered into the page.
  - Both paths accept a post only when the challenge names the member who is signed in now, and only from the site's
    own origin: the surface action checks the origin before any re-render and refuses others without showing their
    answers (ADR-0012). Another origin cannot read such a challenge: the public API never allows credentials
    across origins (ADR-0012), and the browser's same-origin policy protects the page. A site whose own CORS lets
    another origin read its pages with credentials hands that origin the challenge too; the docs say so.
  - Members-only forms therefore work only on the site's own origin: across origins and with server keys they answer
    `member_required`.
  - Jiron Form Builder sets no cookie of its own; the member's login cookie is Umbraco's.
- **Assets:** the extension's static web assets under `/jfb/assets/` (`StaticWebAssetBasePath` `/`), the same under
  `dotnet run` and after `dotnet publish`; publishing also writes Brotli and gzip copies. Umbraco serves static files
  without compression or caching headers, so 6.2 serves them itself, as §3.4 and §4.12 already plan.
  **Amended in 3.2 (ADR-0026):** packed as static web assets, the build's compressed copies broke every installing
  site's build, so the website assets are embedded in the Web assembly with their copies and 6.2 serves them from
  there; the URLs under `/jfb/assets/` stay as planned.
- **Data Protection:** the route token and the challenge (members-only forms included) depend on it, so load-balanced
  sites need a shared key ring. The install docs say so.

**The render budget** (§4.12, a 50-field form from the cached definition in ≤ 20 ms): the view component rendered
it in a median of 0.12 to 0.15 ms and a p95 of 0.19 to 0.23 ms over 500 runs; one run in Production took 19.3 ms
because a garbage collection fell into it.

## Consequences

- §3.4: the no-JS note is confirmed and the output-caching note extended. §4.6 gains the `render` row, §4.9 the
  nonce attribute and `IFormBuilderCspNonceProvider`, §4.10 the CSP rules for cached and uncached pages, §4.12 the
  measured render time. WORKLOG records the change.
- 6.2 builds the surface action, the view component's `no-store` rules, the vary-by and tag providers, eviction on
  publish and the `render` endpoint. Its tests run against a site with output caching on: the cached page is
  byte-identical for two visitors and holds nothing per visitor; `?jfb-sent` shows the outcome and is never stored;
  no response to a post is stored; the auto-route answers `400`; `render` returns the published form with the given
  instance id.
- 6.4 handles `form_changed` through `render` and is tested on a page served stale from the cache; its CSP tests
  cover `script-src 'self'` on a cached page and a nonce on an uncached one, lazily loaded chunks included. The
  runtime does not rely on `crypto.randomUUID()`, which exists only in secure contexts, and names its chunks with a
  content hash, because the `?v=` of the entry module does not reach `import()`.
- ADR-0012 takes as input that a no-JS post on an anonymous form carries no challenge (it is fetched by script), and
  that on a cached page the time since rendering is the cache's.
- ADR-0015 builds the markup contract on the instance ids, the `data-jfb-*` attributes and `novalidate`.
- **Revisit** if Umbraco's website cache starts to vary by query string or to check the method, or if a later
  Umbraco adds a supported way to mark a response as not cacheable without `no-store`.
