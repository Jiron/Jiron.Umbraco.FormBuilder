# 0246. The packages and their listings

- Status: accepted
- Date: 2026-11-13
- Roadmap step: 18.1 (NuGet metadata and `umbraco-marketplace.json`)

## Context

Step 18.1 asks for the metadata every package carries (§3.5), a check that the packed packages hold what they should,
and the Umbraco Marketplace listing, validated against the Marketplace's own schema. The packages already carried most
of the metadata since 3.1 (the licence file and its acceptance, symbols, package validation, the exact versions between
siblings); the readme, the icon and the allowlist of entries had waited for 17.1 and 18.1 (`scripts/pack.mjs` checked
only the main package's schemas, ADR-0125).

Two packages carry the `umbraco-marketplace` tag, because a site installs both: `Jiron.Umbraco.FormBuilder` and the
optional AI add-on (§3.5). Both name the repository as their project, and the Marketplace reads a listing from the root
that `PackageProjectUrl` names: by default `umbraco-marketplace.json`, which would give the add-on the product's title,
pictures and description.

## Options

For the add-on's readme:

1. **One readme for all four packages.** Core and Web are never installed on their own, so the product's readme suits
   them; on the add-on's page it would describe a package the reader already has and not the one in front of them.
2. **The add-on gets its own readme** (`docs/README.nuget.ai.md`), the other three the product's.

For the add-on's listing:

1. **No listing of its own:** take the tag away. §3.5 gives it the tag, so the Marketplace knows it as installable.
2. **A listing of its own beside the product's,** in the per-package file the Marketplace looks for
   (`umbraco-marketplace-<package id>.json`): a second entry that competes with the product in the results.
3. **A listing of its own, marked as a sub-package of the product** (`IsSubPackageOf`), which the Marketplace shows
   with its parent, and named in the product's `RelatedPackages`.

For the check of the packed files: Tandem's allowlist (Tandem ADR-0077), ported, or a list of required entries only.

## Decision

- **Readmes:** option 2. `src/Directory.Build.props` packs `docs/README.nuget.md` as `README.md` unless a project names
  its own (`JfbPackageReadme`), as the add-on does. Both readmes link only to whole addresses, which the link check
  holds for every `docs/README.nuget*.md`. Every package carries the colour mark at 128 px as `icon.png` (17.1).
- **Tags:** the two installed packages carry `umbraco umbraco-marketplace forms form-builder contact-form` (18.1); the
  add-on adds `ai`, its own subject, so a search for an AI add-on finds it. Core and Web carry `umbraco` only.
- **Listings:** option 3. `umbraco-marketplace.json` lists the product (Editor Tools, with Campaign & Marketing, since
  there is no forms category), every picture of `docs/screenshots` with a caption, the licence in its plain words and
  the add-on as a related package. `umbraco-marketplace-jiron.umbraco.formbuilder.ai.json` lists the add-on (Artificial
  Intelligence, with Editor Tools) as a sub-package of the product, without pictures. The documentation links point to
  the repository's `docs/` until the owner publishes docs.jiron.dev.
- **`scripts/check-marketplace.mjs`** validates every listing against the Marketplace's schema, downloaded once into
  `.local/marketplace/` and used only while its SHA-256 is the pinned one (Ajv compiles a schema into code; a changed
  schema waits for a review), then by our rules: the titles, categories, author, price type, links into the repository
  that name files it holds, every picture once at its raw GitHub address, no "open source" claim. It finds the listings
  from the packages' tags, so a package that gains the tag without a listing fails. It runs in the gate and in CI before
  the script tests, which read the same copy.
- **`scripts/check-packages.mjs`** (Tandem's allowlist, ported): every entry of each `.nupkg` and `.snupkg` must be on
  its package's list, and a source map, an appsettings file, a test, demo or sample assembly, a key file or anything
  from `.local/`, `.private/` or `node_modules` is refused with its reason first. Each nuspec must carry the id, a
  semantic version, the title, a description of at most 4,000 characters, the authors and copyright of
  `Directory.Build.props`, the project and repository address, the licence file and its acceptance (never an expression,
  §3.5), the readme, the icon and the tags; its siblings at exactly its own version, and every other dependency in the
  range `Directory.Packages.props` gives it, with no dependency beyond those. The packed `LICENSE.md`, readme and icon
  must be the repository's files byte for byte, so the owner's licence ships as placed (D2). `scripts/pack.mjs` runs it
  after every pack, so the gate, CI and the release check alike.

## Consequences

- A new kind of file in a package (another chunk folder, a new schema) needs its line in the allowlist; that is the
  point, and the failure names the entry.
- The Marketplace reads both listings only once the repository is public; the owner runs its validator then
  (OWNER-TODO).
- Should the Marketplace's documented file name for a package's listing differ in letter case from ours, the add-on's
  listing shows the product's file until it is renamed; the owner's validator run shows it.
- Revisit the sub-package listing if the add-on grows features that deserve a listing of their own.
