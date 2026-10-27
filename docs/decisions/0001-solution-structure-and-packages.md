# 0001. Solution structure and packages

- Status: accepted; the delivery of the website assets amended in 3.2 (ADR-0026); the Umbraco floor raised to
  17.6.0 in 5.5a ([ADR-0074](0074-umbraco-17-6-as-the-floor.md))
- Date: 2026-10-23
- Roadmap step: 1.7a (decided); built in 3.1 and 3.2, packed in 18.1, the add-on in 15.3

## Context

Jiron Form Builder must install as one package (§1.1), keep every rule of the product testable without a web
host (§4.2), let sites add the AI add-on only if they want it (D23), keep its test stubs and fakes out of every
site (D22), and move to Umbraco 18 touching as few files as possible (D3, Phase 19). §4.1 sketches the layout and
§4.2 the dependencies.

What is known already:

- **Tandem** (Tandem ADR-0001) ships Core, an AI bridge, Web, a meta package, an optional add-on and a test-only
  Testing project. It proved on this machine that exact versions between its own packages, builds against the lowest
  version of each range, a pack through a solution filter, and a pack check for the client assets work (§3.3).
- **Persistence** is NPoco with Umbraco's migrations (D7, ADR-0003), so nothing differs per database and no package
  per database is needed.
- **The 1.2a and 1.2b sandboxes** built the backoffice extension as a Razor class library from
  `dotnet new umbraco-extension -ex -pm Central`, with `StaticWebAssetBasePath=/` and its files under
  `wwwroot/App_Plugins/<Name>` (§3.4).
- **The spikes** put a few things where they must live: the rate limiter ships with ASP.NET Core (so limiter adapters
  belong to the web project), the public API needs a middleware before routing (ADR-0006), and the outbox works only
  through Umbraco's scopes (ADR-0007).
- **The Marketplace** lists a package that carries the `umbraco-marketplace` tag and depends on `Umbraco.Cms.*`
  (§3.5). Whether a dependency through another of our packages is enough was never confirmed.

## Options

1. **One package.** The simplest to install and release. Nothing would keep HTTP types out of the domain, the AI
   add-on could not be optional, and the stubs would need packaging tricks to stay out.
2. **Core, Web, a meta package, an optional AI add-on and a test-only Testing project** (§4.1). The compiler keeps
   the boundaries; sites install one id; four packages move together.
3. **A finer split** (abstractions, persistence, rendering, backoffice, public API, static assets). It suits
   frameworks with several hosts. Jiron Form Builder has one host, Umbraco, and every part ships to every site, so it
   would double the packages and the version bookkeeping for no gain.

## Decision

**Option 2.**

| Project | Package | Holds | References |
| --- | --- | --- | --- |
| `src/Jiron.Umbraco.FormBuilder.Core` | yes | Every rule of the product: the definition model and its upgraders (ADR-0002), the field, action and option-source registries, the rules, validation, merge-tag and calculation engines (ADR-0005, ADR-0014), entries and the submission pipeline, action runs and the email outbox (ADR-0007), settings (ADR-0008), permissions (ADR-0013), privacy and retention (ADR-0009), persistence (NPoco DTOs, repositories, the migration plan), jobs, notifications, the rich-text and CSS sanitiser, and every extension interface. | `Umbraco.Cms.Core`, `Umbraco.Cms.Infrastructure` |
| `src/Jiron.Umbraco.FormBuilder.Web` | yes | A Razor class library: the public API (`/jfb/api/v1`), the management API (`/umbraco/jfb/management/api/v1`), rendering (view component, tag helpers, the surface controller, the middleware of ADR-0004 and ADR-0006), themes (Razor views and CSS, ADR-0015), the form picker property editor and its value converters, health checks, the rate-limiter adapters, and the static assets built from `Client/`. | Core, `Umbraco.Cms.Web.Common`, `Umbraco.Cms.Web.Website`, `Umbraco.Cms.Api.Common`, `Umbraco.Cms.Api.Management`, `Umbraco.Cms.Api.Delivery` |
| `src/Jiron.Umbraco.FormBuilder` | yes, the one sites install | No code: the meta package with the tag `umbraco-marketplace`. | Core, Web, and `Umbraco.Cms.Web.Website` directly |
| `src/Jiron.Umbraco.FormBuilder.AI` | yes, optional (Phase 15) | The AI add-on (D23): drafting, translating and suggesting in the designer, with its own management endpoints, backoffice files and health check. ADR-0018 decides its Umbraco.AI references. | Core, Web, Umbraco.AI |
| `tests/Jiron.Umbraco.FormBuilder.Testing` | **never packed** | The HTTP stub on 5560, the captcha and AI fakes, the test clock, user and member seeders, the test-only endpoints and the test transport (ADR-0011). Referenced only by test projects and the demo. | Core, Web |
| `samples/Jiron.Umbraco.FormBuilder.Samples` | no | The compiled extension samples (15.2). | Core, Web |
| `tests/…Tests.Unit`, `tests/…Tests.Integration`, `tests/e2e`, `demo/Jiron.Umbraco.FormBuilder.Demo` | no | ADR-0011 and 3.3. | as needed |

Nothing references the AI add-on, and Core and Web never reference Umbraco.AI.

**Boundaries the build enforces:**

- **Core knows nothing about HTTP.** It references no ASP.NET Core HTTP, MVC, routing or Razor assembly and no
  Umbraco web or API package. Data Protection is allowed, because tokens are made where mail is queued. An
  architecture test reads Core's referenced assemblies and fails on any of them (3.4).
- **Core opens no database connection of its own** (ADR-0007, 4.3).
- **Only the Testing assembly reaches loopback,** through `IFormBuilderHttpTransport` (§4.10); a test proves that a
  host without it refuses loopback.

**Versions:**

- All packages share one version from `Directory.Build.props` (D16, ADR-0010).
- A reference between our own packages is the exact `[version]`, rewritten by `src/Directory.Build.targets`, so they
  always install in lockstep and may share internals through `InternalsVisibleTo`.
- Dependencies on others are ranges, never floating: Umbraco `[17.6.0, 18.0.0)`. Central package management holds
  them, and NuGet resolves the lowest version of a range, so the libraries compile against the oldest version they
  claim. The demo and the test hosts run the latest 17.x through version overrides.
- No direct reference to a package that only comes transitively (ImageSharp above all, CLAUDE.md rule 4); transitive
  advisories follow Tandem ADR-0014 (a suppression of exactly the reviewed advisory, plus the guard script).
- **The meta package references `Umbraco.Cms.Web.Website` `[17.6.0, 18.0.0)` itself.** That satisfies the
  Marketplace's rule whether or not it reads dependencies through our other packages, so the listing never depends on
  how it reads them (§3.5).

**Composition:**

- Each packed project registers itself through its own composer (`FormBuilderCoreComposer`,
  `FormBuilderWebComposer`, later `FormBuilderAIComposer`), and each composer calls one public, idempotent builder
  extension (`builder.AddJironFormBuilderCore()`, `builder.AddJironFormBuilderWeb()`) that adds the layers it needs.
  A host that turns auto-composition off calls them itself.
- The builder extensions live in the namespace `Jiron.Umbraco.FormBuilder.Extensions`, so one `using` finds them.
- Extensions register through `builder.JironFormBuilder()`, which offers one method per extension point of §4.9
  (`AddFieldType<T>()`, `AddActionType<T>()`, `AddOptionSource<T>()` and so on). Registries are collections, so a
  site can add, replace or remove types (§4.3.2).
- **Umbraco-major glue** lives in clearly named classes in Web: `FormBuilderOpenApi` (the documents `jfb` and
  `jfb-public`), `FormBuilderManagementApiRoute`, `FormBuilderAuthorizationPolicies` and `FormBuilderPackageManifest`.
  The 18 port (19.1) starts with these.

**Assets:**

- `StaticWebAssetBasePath` is `/`. The backoffice is served from `/App_Plugins/JironFormBuilder/` with
  `umbraco-package.json` (`id` `Jiron.Umbraco.FormBuilder`, the id sites install; `version` stamped at build), and the
  website runtime, the web component and the theme CSS from `/jfb/assets/`, through Jiron Form Builder's own serving
  with compression and fingerprints (§3.4, 6.2). **Amended in 3.2 (ADR-0026):** the website assets are embedded in
  the Web assembly with their Brotli and gzip copies, not packed as static web assets; only the backoffice is.
- **Built client assets are not committed.** `npm run build` writes them in scripts and CI, and an MSBuild check
  (`JFB001`) fails `dotnet pack` when they are missing, stale, built for another version, without the licence notice,
  or with source maps (3.2). Source maps are made only on request for local debugging.
- **The generated management client is committed**, generated from the committed OpenAPI snapshot rather than from a
  running site (§3.3).
- **The JSON schemas and TypeScript types** of the definition and the public projection live in `schemas/` and are
  packed into the Web package as plain files (ADR-0002, 6.6).

**Packing:**

- Through `src/Jiron.Umbraco.FormBuilder.Packages.slnf`, a solution filter with the packed projects. A second solution
  file in the root would make `dotnet format` ask which one to use (§3.3).
- A pack listing allowlists every file of every package (18.1), so a stub or a source map cannot ship by accident.
- Shared test-project settings live in `tests/Directory.Build.props` for projects named `*.Tests.*`; the Testing
  library is not a test project.

**Visibility and names:**

- Everything not meant for extension is `internal`. Test projects and our other assemblies get `InternalsVisibleTo`.
  Every public type and member has XML docs, and the public-surface snapshot (15.1) fails on unreviewed changes.
- Assembly and namespace roots equal the project names. Extension aliases are `Jiron.FormBuilder.*`, tables `jfb*`,
  the settings section `JironFormBuilder`, CSS classes `jfb-*` (CLAUDE.md conventions).
- **Fixed identifiers are chosen once and never change:** the migration plan `Jiron.Umbraco.FormBuilder`, Data
  Protection purposes (`Jiron.FormBuilder.<Purpose>.v1`), cache refresher ids (new GUIDs), job names
  (`Jiron.FormBuilder.<Job>`), HttpClient names, the email type `JironFormBuilder` (ADR-0007), and BroadcastChannel and
  Web Lock names (`jfb-*`). None is copied from Tandem, because both packages may run on one site (§3.3).

## Consequences

- Every rule of the product builds and is tested without a web host, and an HTTP or database shortcut in Core fails
  the build instead of a review.
- The AI add-on brings its dependencies only to sites that choose it, and it can use the management API's conventions
  and the designer's extension points without Core or Web knowing about it.
- Four packages move in lockstep; the release workflow packs and tags them together, and package validation checks
  them (18.1, 18.2).
- The stubs cannot reach a site, because the Testing project is not packable and the pack listing names every file.
- §3.5's open question about transitive dependencies needs no answer: the meta package references Umbraco directly.
  §4.2 shows the add-on's references and Core's boundary.
- **Revisit** if Umbraco publishes guidance for package layouts that conflicts with this, or if the 18 line needs a
  different split.
