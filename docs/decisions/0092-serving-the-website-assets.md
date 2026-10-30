# 0092. Serving the website assets

- Status: accepted; builds what [ADR-0026](0026-client-workspace-and-asset-delivery.md) left to 6.2 and the
  `<jfb-assets>` part of [ADR-0004](0004-rendering-and-submission-paths.md); how the automatic inclusion counts
  "once" amended in 6.2b ([ADR-0093](0093-the-razor-rendering.md)); the base rules and the stylesheets of the themes a
  page uses, each once and decided when the page is written, added in 6.3a by
  [ADR-0105](0105-the-theme-registry-and-the-base-rules.md)
- Date: 2026-10-28
- Roadmap step: 6.2a (decided and built); 6.2b includes the assets from `<jfb-render>`, 6.3 adds the themes'
  stylesheets, 6.4 the runtime's chunks, 6.6 the element on other origins

## Context

The website runtime, the headless element and (from 6.3) the themes are embedded in the Web assembly with their
Brotli and gzip copies under the resource names `jfb/assets/<path>` (ADR-0026), because packed as static web assets the
copies broke every installing site's build. Nothing serves them yet. Section 4.12's budgets are gzip sizes, and
Umbraco's static files send neither compression nor caching headers (section 3.4), so 6.2 serves them itself: only names
the assembly holds, compared exactly, the copy the request accepts, a year's caching under a `?v=` fingerprint and
`nosniff`. ADR-0004 adds `<jfb-assets>`, which writes the tags once per page with a CSP nonce when the site gives one.

Open were: where in the pipeline the files are served; what the fingerprint is; what a request without the current
fingerprint gets; which headers a file needs on a page of another origin; and how `<jfb-assets>` and the automatic
inclusion of `<jfb-render>` (6.2b) agree on "once".

## Options

**Where:**

1. **An endpoint route** (`MapGet("/jfb/assets/{**path}")`). It competes with Umbraco's content routing, which may
   render a content page for a path it also matches, and it only runs once Umbraco routes requests at all.
2. **ASP.NET Core's static file middleware over an embedded file provider.** It knows neither the precompressed copies
   nor a fingerprint, so a wrapper would do most of the work anyway, and the provider's name matching is not ours to
   fix.
3. **A middleware of our own before Umbraco's routing** (a pipeline filter, as the public API's), owning the path.
   Chosen.

**The fingerprint:**

1. **The package version.** Two builds of one version (every local build, every prerelease rebuilt) would share it,
   and a browser would keep the old file for a year.
2. **A hash of each file's content.** It changes exactly when the file does, the same on every server. Chosen.

## Decision

- **The middleware** (pipeline filter `Jiron.FormBuilder.WebsiteAssets`, before Umbraco's routing) owns the path
  `/jfb/assets/` below the site's path base, in any casing, as the public API owns its base path (ADR-0084) and as
  Umbraco routes. It never hands such a request on, so Umbraco never renders a page for it, and it serves while the
  migrations are pending too, since no file needs the database.
- **Names:** the originals the assembly holds under `jfb/assets/`, compared by code units: no case folding, no decoding
  (`%2F` stays three characters, as ASP.NET Core leaves it), and no path of its own making, so nothing outside the
  folder can be named. The copies are not names: `jfb-runtime.js.br` is not found, and the copy goes out only for its
  original. Anything else answers `404` with no body.
- **Methods:** `GET` and `HEAD` (the headers alone); any other answers `405` with `Allow: GET, HEAD`.
- **The copy:** Brotli when `Accept-Encoding` accepts it, else gzip, else the original: a coding (or `*`) accepted
  with a quality above zero, as Tandem's widget assets choose (Tandem ADR-0032). `Content-Encoding` names it, and
  every answer says `Vary: Accept-Encoding`.
- **The fingerprint** is the first 12 hexadecimal digits of the SHA-256 of the original. A request whose only `v` is
  the file's fingerprint is cached for a year (`Cache-Control: public, max-age=31536000, immutable`). So is a file in a
  `chunks/` or `assets/` folder, whose name the build gives a hash of its content (`scripts/vite-shared.mjs`), because a
  module's `import()` cannot carry the entry's `?v=` (ADR-0004). Any other request is answered `no-cache`, so a page
  rendered before an upgrade gets the new file and keeps asking.
- **Validation:** each copy has its own strong `ETag` (`"<fingerprint>"`, `"<fingerprint>-br"`,
  `"<fingerprint>-gzip"`), and `If-None-Match` with it, or with `*`, answers `304`.
- **Headers:** `Content-Type` (with `charset=utf-8` for scripts, stylesheets and JSON), `Content-Length`,
  `X-Content-Type-Options: nosniff` on every answer, `404` and `405` included, and
  `Access-Control-Allow-Origin: *` with `Cross-Origin-Resource-Policy: cross-origin`: the files are public, the same
  for everyone and fetched without credentials, and a module script from another origin loads only with CORS (the
  headless element on a static site, 6.6). What another origin may then do with the public API stays with its CORS
  allowlist (ADR-0012).
- **In memory:** the files are read from the assembly once per process, at the first request; with the themes they
  are a few hundred kilobytes.
- **`<jfb-assets>`** writes `<script type="module" src="<path base>/jfb/assets/runtime/jfb-runtime.js?v=<fingerprint>">`,
  and from 6.3 the stylesheets, at most once per request: a request remembers that its tags were written, so a second
  `<jfb-assets>` writes nothing, and `<jfb-render>` (6.2b) includes them only when no `<jfb-assets>` came before it.
  The tag itself leaves no element behind.
- **The nonce** comes from the `nonce` attribute, otherwise from the site's `IFormBuilderCspNonceProvider` when it
  registers one, otherwise there is none; an empty nonce counts as none. It goes on every tag `<jfb-assets>` writes,
  and the runtime's imports inherit it (ADR-0004). Which pages may carry one stays as ADR-0004 says: only uncached ones.

## Consequences

- Every site serves the same bytes with the budgets of section 4.12, under `dotnet run`, after a publish and on every
  server, without a setting.
- A site cannot replace a file by putting its own under `wwwroot/jfb/assets/`, as ADR-0026 already says; themes and
  their custom properties change the look.
- A file a site links without `<jfb-assets>` (no `?v=`) is revalidated on each page view: correct, and one `304` each.
- "Once" follows the order in which Razor runs the tags, and Razor runs a view before its layout: a `<jfb-render>` in
  the view runs before a `<jfb-assets>` in the layout's `<head>`. 6.2b settles how the automatic inclusion treats that
  case, without a second copy of a stylesheet.
- **Revisit** when ASP.NET Core can serve precompressed embedded files with a fingerprint by itself.
