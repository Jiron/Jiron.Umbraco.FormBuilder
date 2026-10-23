# 0024. Transitive security advisories from Umbraco

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.1 (decided and listed); the guard script in 3.5; reviewed at every phase review and in 16.1

## Context

Jiron Form Builder builds with warnings as errors, and NuGet audits every restore (`NuGetAudit` on, mode `all`,
level `low`, set in `Directory.Build.props`). The first restore of the solution in step 3.1 failed on 61 audit
errors. They come from 17 advisories in five packages:

| Package | Versions | Advisories | Comes through | Reaches |
| --- | --- | --- | --- | --- |
| MessagePack | 3.1.7 at 17.5.0, 3.1.8 at 17.7.1 | GHSA-qhrr-8q5h-9q3h (moderate) | `Umbraco.Cms.Web.Common` and its hybrid cache, `Api.Common`, `Api.Delivery` | the packages |
| Microsoft.OpenApi | 2.4.1 at 17.5.0 | GHSA-v5pm-xwqc-g5wc (high) | Swashbuckle in `Umbraco.Cms.Api.Common` and `Api.Delivery`; 17.7.1 brings a fixed version | the packages |
| System.Security.Cryptography.Pkcs | 10.0.0, 10.0.6 and 10.0.10 | GHSA-846f-f764-76mx (high), GHSA-2fmv-rv2c-59x7 (moderate) | MailKit and MimeKit in `Umbraco.Cms.Infrastructure`, Examine's Data Protection in `Umbraco.Cms.Web.Common` (.NET servicing) | the packages |
| System.Security.Cryptography.Xml | 10.0.6 and 10.0.10 | eight high ones at 10.0.6, three of them still at 10.0.10 | Examine's Data Protection in `Umbraco.Cms.Web.Common` and `Api.Common` (.NET servicing) | the packages |
| SixLabors.ImageSharp | 3.1.12 | GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w (high); GHSA-gwg2-r3hj-4w44, GHSA-wmxv-xphr-5c9g (moderate) | `Umbraco.Cms` and its imaging package, which only the integration test host brings | site hosts only |

The packages restore at their floor, CMS 17.5.0, because NuGet resolves the lowest version of a range (D3); the
integration tests restore Umbraco 17.7.1. Jiron Form Builder references none of these five packages. A site gets
the versions its own Umbraco chooses, and the latest 17.x still brings most of them. ImageSharp is Apache-2.0 only
for consumers who take it transitively, so Jiron Form Builder must never reference it (CLAUDE.md rule 4).

## Options

1. **Audit direct packages only** (`NuGetAuditMode=direct`). The build passes, and every transitive advisory,
   including a future one that matters, disappears from view.
2. **Pin fixed versions** through central transitive pinning or direct references. Every site would get Jiron Form
   Builder's choice of platform packages, which can break Umbraco's own compatibility, would make them direct
   dependencies against ADR-0001, and for ImageSharp would change the licence terms.
3. **Raise the floor** until the graph is clean. No 17.x graph is clean today, and a higher floor drops sites
   on older 17.x releases (against D3) without fixing anything for them.
4. **Keep the advisories as warnings** (`WarningsNotAsErrors` for NU1901 to NU1904). They stay visible, but the
   zero-warning rule breaks and a new advisory no longer stops the build.
5. **Suppress exactly the reviewed advisories** (`NuGetAuditSuppress`, one entry per advisory URL), keep every
   other advisory an error, and guard that a suppressed package never becomes a direct reference: the pattern
   Tandem proved on this machine (Tandem ADR-0014).

## Decision

**Option 5, with two scopes.**

- `NuGetAudit.props` in the repository root, imported by `Directory.Build.props`, lists each suppressed advisory
  URL with its package, and a comment per package names the dependency it comes through and the versions seen.
- **Two scopes:**
  - advisories that the packages' own Umbraco dependencies bring apply to every project;
  - advisories that only a whole Umbraco site brings (ImageSharp today) apply only to projects that set
    `JfbUmbracoHost` to `true`: the integration tests now, the demo from 3.3. If one of Jiron Form Builder's
    packages ever brought ImageSharp, its restore would fail instead of passing quietly. The condition works on a
    property set in the project file, because MSBuild evaluates items after all properties.
- **Any advisory not listed fails the build.** A new one is fixed when Jiron Form Builder can fix it, or reviewed
  and added here with its reason in the same commit.
- **The guard:** `scripts/check-audit-suppressions.mjs` (3.5) fails when a suppressed package appears as a
  `PackageReference` or `PackageVersion` anywhere in the repository, and `ci-local.ps1` and CI run it.
- **Review:** at every phase review and in 16.1. An entry goes as soon as neither the floor nor the hosts' 17.x
  brings an affected version.

## Consequences

- The build stays at zero warnings, each known risk is written down where a reviewer sees it, and a new advisory
  still stops the build.
- Operators stay responsible for their platform. The installation and security docs (Phase 17) tell them to keep
  Umbraco and .NET patched and to check `dotnet list package --vulnerable --include-transitive`.
- When Umbraco or Microsoft ship fixes, the entries disappear; until then the list documents the real exposure of
  the supported versions.
- **Revisit** if NuGet can scope a suppression to the dependency it comes through, or if a suppressed advisory
  turns out to be reachable through Jiron Form Builder's own code paths. Then Jiron Form Builder works around it or
  refuses the affected version.
