# 0026. The client workspace, and website assets embedded in the Web assembly

- Status: accepted; the embedded assets are served as ADR-0092 decides (6.2a)
- Date: 2026-10-23
- Amended in 6.4g by [ADR-0116](0116-the-runtimes-budget-counts-one-language.md): the runtime with every feature counts
  the largest language's words, as a page loads its culture's alone, and the chunk report names each chunk's modules
- Amended in 6.4h by [ADR-0119](0119-a-terser-pass-for-the-website-runtime.md): the website runtime takes a terser pass
  besides the bundler's minifier
- Amended in 6.4k by [ADR-0121](0121-a-strict-site-in-the-browser-tests.md): the size check holds every file of the
  runtime's build but its entry to a name with the bundler's content hash
- Amended in 12.1 by [ADR-0216](0216-smaller-template-text-in-the-backoffice.md): the backoffice's Lit templates lose
  their comments and line breaks before the bundler compiles them
- Roadmap step: 3.2 (decided and built); 6.2 serves the embedded assets, 6.3 adds the themes, 9.6 sets the
  backoffice budget, 18.1 and 18.2 pack and install

## Context

Step 3.2 creates `src/Jiron.Umbraco.FormBuilder.Web/Client`, the npm workspace of §4.1, with four packages: the
shared logic (ADR-0005), the website runtime, the headless web component and the backoffice extension. It has to
meet §4.12's size budgets, keep the PolyForm notice on every copy of the code, never pack assets that are missing,
stale, built for another version or with source maps, and give 6.2 Brotli and gzip copies to serve, because
Umbraco's static files send neither compression nor caching headers (§3.4, measured in 1.4a).

ADR-0001 and ADR-0004 planned the website assets as the extension's static web assets under `wwwroot/jfb/assets/`,
and the roadmap had the build write the compressed copies next to them. Packing that way was tried in this step,
with the Release pack of `scripts/pack.mjs` and a throwaway site in `%TEMP%\jfb-swa-3.2` that installs the packages
from `.local/nuget-feed`:

- The pack succeeded once the copies were kept out of NuGet's `content/` folders. But the Razor SDK (10.0.401) sees
  a `.br` or `.gz` file next to its original as that file's compressed asset, and records the original as the
  copy's `RelatedAsset` with the packing machine's absolute path. Every site that installs the package then fails
  to build with "Related asset not found for compressed asset", and the package carries a path of the machine
  that packed it.
- Without the copies, the SDK compresses static web assets only when the site is published (1.4a), with the site's
  build settings, so `dotnet run` would serve the files uncompressed and 6.2 would need a second path.

## Options

1. **Static web assets with the build's own copies** (the plan). Breaks every installing site's build, as above.
2. **Static web assets without copies,** compressed by the SDK. Copies exist only after a publish, and only as the
   site's settings allow; development and production would be served differently, and §4.12's budgets would hold
   only for published sites.
3. **Compression at run time** (response compression, or compressing once per server and keeping the result).
   Brotli at quality 11 is too slow for a request, a lower quality gives larger files, and every server pays for it
   again after a restart.
4. **Embedded resources:** the website assets and their copies are built into a folder of the Web project and
   embedded in its assembly; 6.2's own endpoint serves them.

## Decision

**Option 4 for the website assets; the backoffice stays a static web asset.**

**Where the files go:**

| Package | Built into | Served at |
| --- | --- | --- |
| `@jfb/logic` | not built: the runtime and the element import its TypeScript, and its test vectors are shared with C# (ADR-0005) | |
| `@jfb/runtime` | `WebsiteAssets/runtime/jfb-runtime.js`, features loaded on demand in `chunks/<name>-<hash>.js` | `/jfb/assets/runtime/...` |
| `@jfb/element` | `WebsiteAssets/element/jfb-element.js` | `/jfb/assets/element/...` |
| themes (6.3) | `WebsiteAssets/themes/...` | `/jfb/assets/themes/...` |
| `@jfb/backoffice` | `wwwroot/App_Plugins/JironFormBuilder/` with `umbraco-package.json` and `jfb-backoffice.js` | `/App_Plugins/JironFormBuilder/...` (static web assets, where Umbraco finds the package manifest) |

- **Embedding:** `WebsiteAssets/` is git-ignored like the backoffice output. The `JfbEmbedWebsiteAssets` target in
  the Web project embeds every file in it, the copies included, under the logical name `jfb/assets/<path>`. A site
  serves the same bytes under `dotnet run`, after `dotnet publish` and on every server, from the assembly that holds
  the code, whatever its own build settings are.
- **The build** (Vite library mode, ES modules, `es2022`): `scripts/vite-shared.mjs` holds the settings every
  package shares. Vite leaves library output unminified, so the shared settings minify it. Comments marked as legal
  are kept, annotations and JSDoc are dropped. Source maps are written only with `JFB_SOURCEMAP=1`, for local
  debugging.
- **The licence notice:** `scripts/banner.mjs` builds a `/*! ... */` banner from the version, the licence's name
  and address and every `Required Notice:` line of `LICENSE.md`, read at build time so the notice is never copied
  into a source file. Rolldown's `postBanner` adds it to every JS file after minification, so no minifier can drop
  it, and a small plugin adds it to every CSS file. The size check counts it.
- **Compression:** `scripts/compress.mjs` writes a Brotli (quality 11) and a gzip (level 9) copy of every website
  asset and removes copies whose original is gone.
- **The version** is `JFB_VERSION` when set (the pack script sets it), otherwise `VersionPrefix` and
  `VersionSuffix` from `Directory.Build.props`. It is stamped into `umbraco-package.json`, the banners and the
  runtime's `window.JironFormBuilder.version`.
- **The pack check `JFB001`:** after the build, `scripts/stamp.mjs write` records the version, a hash of every
  input (sources, configs and the lock file; tests excluded) and the list of outputs in `Client/.build/stamp.json`.
  Before `GenerateNuspec`, the Web project runs `stamp.mjs check <PackageVersion>`, which fails the pack with
  `error JFB001: ...` when the client was never built, a source changed since, the build was for another version,
  an output is missing, a JS or CSS file has no banner or no `Required Notice` line, a website asset's copy is
  missing or is not a copy of the current file, a source map would be packed, a file appeared after the build, or
  the Web assembly to be packed (`$(TargetPath)`) does not hold the bytes of every website asset and copy as they
  are now. Embedded resources are stored unchanged, so that last check catches an assembly built before the last
  client build, as `dotnet pack --no-build` would pack it.
- **One pack script:** `scripts/pack.mjs` builds the client for the version it packs (by default
  `<VersionPrefix>-dev.<yyyyMMddHHmm>` in UTC), packs `src/Jiron.Umbraco.FormBuilder.Packages.slnf` and checks
  that exactly the three packages and the two symbol packages of that version came out. The gate, CI and the
  release use it (§3.3, 18.1).
- **The size check:** each build writes a chunk report to `Client/.build/<name>.chunks.json`.
  `scripts/size-check.mjs` measures gzip sizes against §4.12: the runtime's core (the entry and everything it
  imports statically) at most 16 KB, the runtime with every feature at most 40 KB, each theme at most 10 KB; the
  element and the backoffice bundle are reported until a step sets their budgets (9.6 for the backoffice, which
  ADR-0183 records).
- **Boundaries,** enforced by ESLint: the logic package uses no globals and imports nothing; the runtime and the
  element import only their own files and `@jfb/logic`, and never write HTML strings into the page (`innerHTML`,
  `outerHTML`, `insertAdjacentHTML`, `document.write`); the backoffice takes Lit only from
  `@umbraco-cms/backoffice/external/lit` and never uses `unsafeHTML` or `unsafeSVG`.
- **Dependencies:** none of the four packages has a runtime dependency. `@umbraco-cms/backoffice` (17.5.0, the
  floor of D3) is external in the backoffice build, since Umbraco provides it in the page. Every npm package is a
  development tool; the management client generated with `@hey-api/openapi-ts` from 4.7 on brings the first
  shipped npm code (its fetch client, MIT), which `THIRD-PARTY-NOTICES.md` then lists.
- **Advisories in the tools:** the first install reported six. Two parents pin a vulnerable version exactly:
  `@hey-api/json-schema-ref-parser` 1.4.4 (under `@hey-api/openapi-ts` 0.99.0) pins `js-yaml` 4.2.0 (quadratic
  CPU on crafted YAML), and `monaco-editor` 0.57.0 (under `@umbraco-cms/backoffice` 17.5.0) pins `dompurify` 3.4.15
  (several sanitiser bypasses). npm's own fix would downgrade the client generator below the 0.99 line that 1.2a
  chose, so `package.json` overrides the two instead, to the patched `js-yaml` 4.3.2 and `dompurify` 3.4.17 (the
  version `@umbraco-cms/backoffice` itself uses). `npm audit` then reports none. Each override goes when its parent
  pins a patched version; Dependabot's updates (3.5) show when.

## Consequences

- Every site that installs Jiron Form Builder builds and publishes without anything to resolve, and no packing
  machine's path reaches a package.
- 6.2 serves the assets from the assembly: it answers only names the assembly holds, compared exactly (no case
  folding, no decoding of `%2F`, nothing outside `jfb/assets/`), picks the Brotli or gzip copy by
  `Accept-Encoding`, and sets the caching and `nosniff` headers. A site can no longer replace an asset by putting
  a file under its own `wwwroot/jfb/assets/`; themes and their custom properties are the way to change the look
  (ADR-0015).
- The Web assembly grows by the website assets and their two copies (the runtime's budget is 40 KB gzip, so a few
  hundred KB at most with the themes).
- A change to `Client/` needs a client build before a pack, which the pack check enforces and `scripts/pack.mjs`
  does by itself.
- This amends the Assets paragraphs of ADR-0001 and ADR-0004 (static web assets under `/jfb/assets/`).
- **Revisit** if a later SDK packs precompressed static web assets without recording a machine path, checked with
  a throwaway site that installs the package (18.2's install test is the place).
