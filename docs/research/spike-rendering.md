# Rendering and posting: what the sandbox spike showed

Step 1.4a rendered forms on Umbraco 17.7.1 pages in the sandbox (SQLite, port 5510) and sent them both ways D11
names: a no-JS post back to the page through Umbraco's surface routing, and a JavaScript `fetch` to a public
controller outside `/umbraco`. The pages ran behind Umbraco's website output cache and under a Content Security
Policy. The spike code is throwaway and git-ignored (`.local/sandbox/Spike/Rendering/`, its view and
`wwwroot/jfb/`); the decisions are in [ADR-0004](../decisions/0004-rendering-and-submission-paths.md). Measured on
2026-10-23 on the demo machine, in Microsoft Edge through Playwright, against the site built with `dotnet build`
(Development) and against the same site after `dotnet publish` (Production).

## 1. What was built

- **Forms:** an in-memory stand-in for published definitions: "contact" (name, email, message), "newsletter" (first
  name, email, an optional topic), a members-only stand-in that carries an antiforgery token, and a 50-field form for
  the render budget. A lab endpoint publishes a new revision of a form, with or without evicting its pages.
- **Rendering:** a view component (`JfbForm`), a `<jfb-render alias="…" />` tag helper that invokes it, and a
  `<jfb-assets />` tag helper that writes the stylesheet and the module script once per page, with the request's
  CSP nonce when there is one. Instance ids are deterministic per request (`jfb-1`, `jfb-2`), ported from
  Ligata.Forms; field ids are `<instance>-<alias>`.
- **The no-JS path:** a `SurfaceController` with one `[HttpPost, IgnoreAntiforgeryToken]` action. One form opens
  with Umbraco's `BeginUmbracoForm(…, antiforgery: false)`, the other with our own `<form>` element and
  `Html.SurfaceControllerHiddenInput<T>()`, so both ways were tested on the same page.
- **The JS path:** an `[ApiController]` at `/jfb/api/v1/forms/{key}` with `challenge`, `entries` and `render`,
  ProblemDetails errors with a `code`, and `Cache-Control: no-store` on every response.
- **The runtime:** an ES module of 6.3 KB (1.9 KB Brotli) with no inline script, no `eval`, no style attributes, no
  cookies and no storage. It enhances every server-rendered form, fetches the challenge when the visitor first
  interacts, posts with `fetch`, and imports its error-display chunk (2.7 KB) only after a `422`.
- **Lab pages,** seeded as content with one template: `/forms-lab/` (both forms, cached, `script-src 'self'`),
  `/forms-lab-nonce/` (not cached, a nonce per request, optionally with `'strict-dynamic'`),
  `/forms-lab-nonce-cached/` (a nonce on a cached page, to show why that breaks) and `/forms-lab-antiforgery/` (the
  members-only stand-in).
- **The site:** `Umbraco:CMS:Website:OutputCache:Enabled` on, with a `ContentDuration` of 5 minutes so no entry
  expires during a run. The CSP comes from a middleware registered as an `UmbracoPipelineFilter` before Umbraco's
  pipeline, so it also runs for responses from the cache.
- **The check:** `checks/rendering.mjs`, 70 checks plus 5 recorded facts, run in Edge with JavaScript on and off.

## 2. Umbraco's website output cache

Umbraco 17 can cache rendered pages through ASP.NET Core output caching. It is off by default; a site turns it on
with `Umbraco:CMS:Website:OutputCache:Enabled` (`ContentDuration` defaults to 10 seconds). It is not in 17.3.0; it
is in 17.5.0 (D3's floor), 17.7.1 and 18.2.1, with the same extension points. What the spike found:

- **The cache key ignores the query string.** `/forms-lab/?utm_source=x` was answered from the entry of
  `/forms-lab/` (an `Age` header on the first request). A page that renders something from the query is therefore
  answered with whatever was cached first. Without a vary rule, the outcome after a no-JS post (`?jfb-sent=jfb-2`)
  came back as the cached form: checks B1, B3, C10 and C14 failed in that run (section 11).
- **Vary-by providers are a collection.** `WebsiteOutputCachePolicy` asks every registered
  `IWebsiteOutputCacheVaryByProvider` to add to the rules, so a package adds a rule without taking the site's away.
  The spike's provider acts only when the request carries `jfb-sent`: it adds that key and marks the response
  `no-store`. The outcome then always renders fresh, and no value of `jfb-sent` (`?jfb-sent=x123`) can create a
  cache entry (check B0b).
- **The request filter is a single service.** `IWebsiteOutputCacheRequestFilter` is replaced, not added to. A package
  that registered its own would remove the site's, or be removed by it, so Jiron Form Builder must never register
  one. The lab did, for its nonce page only.
- **What blocks storage:** a `Set-Cookie` header, or `no-store` in `Cache-Control`. Umbraco's policy checks neither
  the HTTP method nor the status code. Even so, posts routed to a surface controller were never served from or
  stored in the cache on 17.7.1: two valid posts in a row both reached the controller and were both stored, and an
  invalid post never answered the next visitor, also with the spike's own `no-store` switched off (facts P4 to P6).
  A post without a route token (a bot) rendered the page normally (`200`) and did not swallow the next real post.
- **Cached responses** carry an `Age` header and are byte-identical for every visitor: three visitors, one with other
  cookies and another `Accept-Language`, got the same 4,435 bytes and the same render time stamp (A1, A2).
- **Eviction by tag:** `IWebsiteOutputCacheTagProvider` (also a collection) tags a page; the lab tagged each page
  with `jfb-form-<key>` for the forms it shows, and `IWebsiteOutputCacheManager.EvictByTagAsync` re-rendered exactly
  those pages after a publish (F9). Pages that show a form without Jiron Form Builder knowing about it (a template
  that names the form) stay stale until their duration ends; section 7 covers what the runtime does then.

## 3. Rendering

- **Nothing per visitor.** The cached page holds no antiforgery token, no nonce, no challenge and no `jfb-sent`
  marker (A3), sets no cookie (A4), and issuing it created no challenge (A5).
- **The route token is not per visitor either.** `ufprt` is the controller, action, area and route values, encrypted
  with Data Protection (`EncryptionHelper.CreateEncryptedRouteString`); nothing about the visitor or the page goes in.
  Every visitor of a cached page posts the same value, and it worked for each of them (sections 4 and 6); a token
  taken from `/forms-lab/` also routed a post on another page (H2). It depends on the Data Protection key that made
  it, so servers behind a load balancer must share their key ring (as Umbraco already requires).
- **The render budget** (§4.12: a 50-field form from the cached definition in ≤ 20 ms): the view component rendered
  the 50-field form (15,629 characters) in a median of 0.12 to 0.15 ms and a p95 of 0.19 to 0.23 ms over 500 runs,
  after 20 warm-up runs. The slowest single run was 0.3 ms in Development and 19.3 ms in Production, where a garbage
  collection fell into it. The budget holds by two orders of magnitude.

## 4. The no-JS path (D11)

Checked with JavaScript disabled in the browser context (C1: the runtime never marked a form ready).

- **Surface routing:** the form posts to its own page with `ufprt`; Umbraco routes the post to the surface action,
  and `[IgnoreAntiforgeryToken]` on the action overrides the controller's `[AutoValidateAntiforgeryToken]`.
- **Errors:** `CurrentUmbracoPage()` renders the page again in the same request (`200`). The action left the answers
  and errors in `HttpContext.Items`, where the view component found them by form key and instance id. The summary
  links each error to its field, the inline errors sit at their fields, `aria-invalid` and `aria-describedby` are set,
  and the answers are kept (C2 to C7). The response is `no-store` (C3).
- **Success:** post/redirect/get. Umbraco's `RedirectToCurrentUmbracoPage(QueryString)` answered `302` to
  `/forms-lab/?jfb-sent=jfb-2`, with no way to add a fragment (D1). The spike's own redirect,
  `LocalRedirect(<relative page URL>?jfb-sent=<instance>#<instance>)`, brings the visitor back to the outcome of the
  form they sent (C9, C10). Reloading it posts nothing again (C11).
- **`BeginUmbracoForm(…, antiforgery: false)`** writes no token, but it always sets the action to the current path
  without a fragment, always adds `enctype="multipart/form-data"`, and gives the form a random `id` on every render.
  Our own `<form>` with `Html.SurfaceControllerHiddenInput<T>()` gets the same routing with full control of the
  markup. Its action ends in `#jfb-2`, so the page with errors opens at the form that has them (C13), and it also
  works when the form is rendered outside the page (section 7).
- **`CurrentPage` throws** when the action is reached without Umbraco's route values, which happens through the
  auto-route `/umbraco/surface/jfbsurface/submit`: the first run answered `500`. The action now checks the
  `UmbracoRouteValues` feature first and answers `400` (D2).
- **A tampered `ufprt`** never reaches the controller: Umbraco renders the page as if nothing had been posted
  (`200`, fact D3).
- **A stale revision without JavaScript:** the post re-renders the current form with a notice and the answers (F8).

## 5. The JavaScript path

- **The public controller** at `/jfb/api/v1` works with plain attribute routing next to Umbraco's routes; every post
  of the run went there and none under `/umbraco` (E12).
- **The challenge** is fetched when the visitor first interacts (a `focusin` or `input`), never on load (E1, E2).
  The API refuses an entry without one (`400 challenge_required`, E14).
- **Errors:** a `422 validation_failed` with errors by alias. The runtime then imports its error chunk, builds the
  same summary and inline errors as the server (E3, E4, E6), moves focus to the summary (E5), and a summary link
  moves focus to its field (E8). The answers stay and the other form is untouched (E7).
- **Success:** the outcome replaces the form in place and takes focus; the page never navigates (E9, E10).
- **Idempotency:** the same `submissionId` sent twice returns the same receipt and stores one entry (E15).
- **Browser noise:** Edge logs every `4xx` answer to `fetch` as a console error ("Failed to load resource"). The
  expected `409` and `422` are not runtime errors, and tests have to tell them apart.

## 6. Two forms on one page

Each form keeps its own errors and answers on both paths: a no-JS post of one form shows its errors and leaves the
other empty (C8, C12), and the outcome of one leaves the other a form (C10, C14). With JavaScript the same holds
(E7). A second visitor posting invalid answers saw only their own, and the cached page held neither (C15).

## 7. `form_changed`: where the runtime gets the new form

A publish that does not evict the page leaves the cached page on the old revision (F1). A post with that revision
gets `409 form_changed`. Two ways were possible:

- **Reload the page:** the reload can come from the same cache and show the old form again, the answers are lost
  unless they are stored first (D13 allows no browser storage outside "save and resume"), and the whole page is
  fetched for one form.
- **Fetch the published form:** `GET /jfb/api/v1/forms/{key}/render?instance=jfb-1&page=/forms-lab/&contentKey=…`
  returns the form's HTML from the same view component, with the page's instance id and the page path for the no-JS
  action. The runtime parses it in a `<template>`, copies the answers by field name, swaps it in, enhances it again,
  and moves focus to a notice that says the form has changed. The second send stored the entry against the new
  revision (F2 to F7).

The spike took the second. With JavaScript disabled, the post itself re-renders the current form (F8).

## 8. Content Security Policy

- **`script-src 'self'` on a cached page:** the runtime and its lazily imported chunk run, with no violation (G2).
  Nothing in the markup is per visitor, so this is the policy for cached pages.
- **A nonce on an uncached page:** `<jfb-assets>` writes the request's nonce on the module script. With only
  `'nonce-…'` in `script-src` (no `'self'`) the dynamic `import()` of the chunk still loads, because a module's
  imports inherit its nonce; with `'strict-dynamic'` added it loads too (G2). The page got a fresh nonce per request
  and was never cached (G1).
- **A nonce on a cached page breaks.** The cached HTML keeps the first visitor's nonce, while the next response gets
  a new one in its header, so the browser blocks the runtime (`script-src-elem`, G3). Had the policy been stored with
  the page instead, every visitor would share one nonce, which defeats it. A site that uses nonces must not cache
  those pages.
- **No violations** on any of the product-intended pages, with JavaScript (E13, F7, H5 and the three G2 runs).

## 9. Members-only forms keep antiforgery

The stand-in form carries an antiforgery token. ASP.NET Core's antiforgery then marks the page
`Cache-Control: no-cache, no-store` and sets its cookie (`SameSite=Strict`, `HttpOnly`), so the page is never cached
(H1). Both paths validate the token themselves (`IAntiforgery.ValidateRequestAsync`), because the actions ignore it
for anonymous forms: a no-JS post without the token is refused with `400` (H2) and with it succeeds (H3); the runtime
sends the token from the form as the `RequestVerificationToken` header, and without it the API answers
`antiforgery_failed` (H4).

## 10. Static web assets under `dotnet run` and after `dotnet publish`

- **Development:** the files come from the extension project's `wwwroot` through the static web assets manifest,
  under `/jfb/assets/` because the project sets `StaticWebAssetBasePath` to `/`.
- **Published, run in Production:** `dotnet publish` copied them into the site's `wwwroot/jfb/assets/`, and the
  publish folder holds no development manifest. The whole check passed against it (section 11). The publish also
  wrote `.br` and `.gz` copies (`jfb.js`: 6,302 B, 1,898 B Brotli, 2,322 B gzip), because compression is on in the
  extension project even though the site project turns it off.
- **Headers:** in both modes Umbraco's static files answered with an `ETag`, but without `Content-Encoding`, without
  `Cache-Control` and without `X-Content-Type-Options`, also when the browser accepted Brotli. That confirms §3.4:
  6.2 serves the runtime itself, choosing the precompressed copy.

## 11. The runs

| Run | Site | Result |
| --- | --- | --- |
| dev | `dotnet build`, Development | 70 of 70 checks passed |
| dev without the vary rule | the same, with the spike's `IWebsiteOutputCacheVaryByProvider` switched off | 64 of 70: B0b, B1, B2, B3, C10 and C14 failed, the outcome after a no-JS post came back as the cached form |
| published | `dotnet publish -c Release`, run in Production with a fresh database | 70 of 70 checks passed |

The five facts were the same in every run: another query string is answered from the page's entry (B5); a tampered
`ufprt` renders the page (D3); and without the spike's `no-store`, posts were still never replayed (P4 to P6).
Screenshots are in `.local/sandbox/checks/shots/1.4a-*.png` (not committed).

## 12. Notes for later steps

- **6.2:** our own `<form>` with `SurfaceControllerHiddenInput<T>()`, not `BeginUmbracoForm`; the action URL with the
  instance fragment; `enctype="multipart/form-data"` only on forms with an upload field; the
  `UmbracoRouteValues` check before anything else in the surface action; the vary rule and the `no-store` rules of
  ADR-0004.
- **6.4:** `crypto.randomUUID()` exists only in secure contexts (HTTPS or localhost). The runtime needs a fallback
  through `crypto.getRandomValues()`, or a plain-HTTP intranet site has no submission id. Lazily imported chunks need
  their own cache busting (a hash in the file name from the build), because the `?v=` of the entry module does not
  reach `import()`.
- **6.4 and 16.x tests:** filter Edge's "Failed to load resource" console lines for the expected `409` and `422`.
- **ADR-0012:** a no-JS post cannot carry the challenge, which is fetched by script. Its protection is the honeypot,
  the rate limits, the content rules and the time since the page was rendered; on a cached page that time is the
  cache's, so it can only make the check weaker, never refuse a real visitor.
- **Load balancing:** `ufprt`, the challenge and the antiforgery token all use Data Protection, so every server needs
  the same key ring. The install docs (17.x) say so.

## 13. What changed in the roadmap

- §3.4: the no-JS note is confirmed and rewritten with these facts; the output-caching note gains what the cache
  varies by and what blocks storage.
- §4.6: a `GET forms/{key}/render` row for `form_changed`.
- §4.9: the nonce attribute on `<jfb-assets>` and `IFormBuilderCspNonceProvider`.
- §4.10: the CSP bullet says which policy fits cached pages and that nonces need uncached pages.
- §4.12: the render budget's measurement.
- 6.2 builds the render endpoint, the vary rule and the `no-store` rules, with tests against a cached page; 6.4
  tests `form_changed` on a page served stale from the cache, the CSP pages with the lazy chunks, and the
  submission id on plain HTTP.
