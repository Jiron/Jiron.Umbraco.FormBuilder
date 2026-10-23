# 0025. What the projects keep from the extension template, and no package telemetry

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.1 (decided; the projects); 3.2 builds `Client/` and `umbraco-package.json`; 9.1's manifests
  test checks the telemetry setting

## Context

Step 3.1 bases `Jiron.Umbraco.FormBuilder.Web` on Umbraco's extension template:
`dotnet new umbraco-extension -n Jiron.Umbraco.FormBuilder.Web -ex -pm Central`, from `Umbraco.Templates` 17.7.1 in
the private template hive `%TEMP%\jfb-templates-17.7.1` (§3.4), generated into a throwaway `%TEMP%\jfb-*` folder.
It creates a Razor class library with its own `Directory.Packages.props`, a composer that registers a Swagger
document, an example API controller and base controller, a root `Constants` class, a `README.txt`, and `Client/`
(Vite, Lit, TypeScript, a pre-generated API client, a dashboard example and `public/umbraco-package.json`).

The sandboxes of 1.2a and 1.2b found several template defaults that must not ship
([docs/research/umbraco-backoffice.md](../research/umbraco-backoffice.md), section 1), and left one choice to this
step: `umbraco-package.json` allows package telemetry (`"allowTelemetry": true`). With it, Umbraco includes the
package's name and version in the telemetry a site sends to Umbraco HQ when the site's telemetry level allows that.

## Options

For the template:

1. **Keep the template as generated** and grow it. Its examples, its placeholder Content-section policy and its
   exact 17.7.1 versions would linger, and its layout (one project) does not match ADR-0001.
2. **Take what the product needs and move the rest where ADR-0001 puts it.**

For telemetry:

1. **Allow it,** as the template does. Umbraco HQ would see which sites run Jiron Form Builder and which version.
   Jiron Form Builder itself would gain nothing from it: it receives none of that data.
2. **Turn it off** explicitly.

## Decision

**Option 2 for the template, and package telemetry off.**

| Template part | In Jiron Form Builder |
| --- | --- |
| Razor SDK, `StaticWebAssetBasePath` `/`, `Content Remove="Client\**"`, the `wwwroot` folder | kept in `Jiron.Umbraco.FormBuilder.Web.csproj`, with `None Remove="Client\**"` too |
| `TargetFramework`, `Nullable`, `ImplicitUsings` in the project | the repository's `Directory.Build.props`, which already sets them with warnings as errors |
| `PackageId`, `Product`, `Title` in the project | `PackageId` from `src/Directory.Build.props`, `Product` "Jiron Form Builder" from the root, a `Title` and `Description` per project |
| its own `Directory.Packages.props` with exact 17.7.1 versions and `CentralPackageFloatingVersionsEnabled` | dropped: the root file holds the ranges `[17.5.0, 18.0.0)` (D3), and floating versions stay off |
| references to `Umbraco.Cms.Api.Common`, `Api.Management`, `Web.Common`, `Web.Website` | kept, plus `Api.Delivery` (§4.2) |
| the composer with the Swagger document and the operation id handler | dropped for now; the first management API step registers the documents in `FormBuilderOpenApi`, the Umbraco-major glue of ADR-0001 |
| the example controller and its base with the Content-section policy | dropped; the management API uses Jiron Form Builder's own section policy (ADR-0013) |
| the root `Constants` class | dropped: in the package's root namespace it hides `Umbraco.Cms.Core.Constants` |
| `README.txt` | dropped; the docs of Phase 17 cover development |
| `Client/` | not in 3.1. Step 3.2 creates it as the npm workspace of §4.1 and takes the template's Vite set-up with the 1.2a fixes: `@hey-api/openapi-ts` pinned to 0.99.x, a regenerated client, the corrected `runtimeConfigPath`, no source maps in release builds |
| `umbraco-package.json` | written by 3.2: `id` `Jiron.Umbraco.FormBuilder` (the id sites install), `name` "Jiron Form Builder", `version` stamped at build, **`allowTelemetry` `false`**, one `bundle` extension with an alias under `Jiron.FormBuilder.`, served from `/App_Plugins/JironFormBuilder/` |

- **Composers:** one per project that has code, empty until a later step registers something:
  `FormBuilderCoreComposer` and `FormBuilderWebComposer` in each project's `Composing` namespace, and
  `FormBuilderTestingComposer` in the Testing project. The meta package has no code.
- **Telemetry:** `allowTelemetry` is written out as `false`, so the choice never depends on Umbraco's default.
  The manifests test of 9.1 fails if it is ever missing or `true`.

## Consequences

- The projects carry nothing that the product does not use, and the examples' unsafe defaults (the Content-section
  policy, source maps, the outdated client generator) never reach a package.
- Nothing about a site leaves it because of Jiron Form Builder, in line with its privacy defaults (ADR-0009) and the
  rule that visitors' pages load nothing from third parties (CLAUDE.md rule 4). Umbraco HQ cannot count Jiron Form
  Builder installs through telemetry; NuGet's download counts remain.
- The 18 port starts from the same short list of glue classes (ADR-0001), since none of the template's 17-specific
  Swagger code was kept.
- **Revisit** if Umbraco ties a Marketplace feature to package telemetry, or if the owner wants install counts;
  either needs a privacy note in the docs first.
