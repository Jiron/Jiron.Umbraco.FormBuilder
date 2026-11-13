# 0250. The Umbraco 18 line

- Status: accepted (refines D3, D16, ADR-0010 and ADR-0213)
- Date: 2026-11-13
- Roadmap step: 19.1 (19.1 and 19.2, the Umbraco 18 line and its phase review)

## Context

D16 and ADR-0010 put the Umbraco 18 code on `v18/main`, branched from `main`, with `main` merged into it and never
the reverse; ADR-0074 names 18.1.0 as the first 18.x whose runtime cache clears reliably. On 2026-11-13 nuget.org had
Umbraco CMS 18.3.1 (18.1.0 the floor), Umbraco.AI 18.8.0 (each 17.x of it has an 18.x twin: 17.5.2 and 18.5.2,
17.7.0 and 18.7.0), Umbraco.StorageProviders.AzureBlob 18.0.0 and `@umbraco-cms/backoffice` 18.3.1. Umbraco 18 stays
on .NET 10, and its integration test package still needs NUnit 3.

Building Jiron Form Builder against them showed that the domain, the persistence, the rendering, the jobs and the
samples compile unchanged. What broke:

- **OpenAPI.** Umbraco 18 replaced Swashbuckle with Microsoft.AspNetCore.OpenApi, served at
  `/umbraco/openapi/{name}.json` through its own document builder (`AddBackOfficeOpenApiDocument`). The new generator
  describes the same API differently: framework types as schemas of their own, numbers as "integer or string", every
  record constructor parameter as required, nullable optional bodies as "the model or null", read-only members as
  writable and nullable, problems without their extensions. It also describes only the actions ASP.NET Core's API
  explorer lists, which without `[ApiController]` are only those that ask.
- **Obsoletions** that warnings as errors refuse: a page's variants and a content type read synchronously; in the
  tests, the publish status service, the copy notification with a parent id and the publish overload without the
  save's own status. Umbraco 18 also has an `IElement` of its own beside AngleSharp's.
- **The backoffice package 18.** A tree asks a level for a page from an offset or for a window around one of its
  items (`paging`), and wants how many come before and after; the tree item context matches only a tree view that
  loads children itself, with a base context for every view; and the package path of the generated backend API is the
  same file Umbraco's own modules import.
- **The integration test run.** Umbraco's test helper gives each fixture one mocked web host environment, which answers
  every read of its content root's file provider with a new provider and remembers it. Every Umbraco 18 test host
  watches one, and the watchers nobody disposed kept the mock, and through it every host of the fixture, in memory: the
  test host held 31 GB by the 2,089th test, and late requests timed out. A refused value's save and publish are also
  reported apart now, and a test host has a single server's role where it had none.
- **The workspace's header row.** Umbraco 18.3 gives its header slot no width of its own (`flex-basis: 0`): the views'
  tabs take their whole width first, and the header what is left, where Umbraco 17 folds tabs into its "..." and
  leaves the header its content's width. Beside all the form's views on a narrow window, the name and the state chip
  lay over the tabs. Umbraco offers no hook for that row.

## Options

1. **One branch with both lines behind conditions** (`#if`, two backoffice packages). The OpenAPI stack, the package
   ranges and the backoffice's tree API differ; conditions would make both lines harder to read and to test.
2. **`v18/main` with the smallest port** (the shape of the owner's earlier 18 line): the same code, the ranges raised,
   the OpenAPI registration ported to Umbraco 18's builder with rules that keep the documents describing what the API
   sends, and the backoffice's adapters taking the new tree API, so the backoffice code is the same on both lines
   apart from its adapters.

## Decision

Option 2.

- **17 comes first** (D3): every feature and fix lands on `main` and is released for 17 first; `v18/main` follows by
  merging `main`. A change on `main` for the 18 line's sake (a check that reads the line's major, the install test's
  line upgrade) must not change what 17 does, and `main`'s own checks prove it.
- **Versions:** CMS `[18.1.0, 19.0.0)`; Umbraco.AI `[18.5.2, 19.0.0)`, the twin of the 17 line's floor; the demo and
  the test hosts on CMS 18.3.1 and Umbraco.AI 18.7.0 (the twin of what the 17 line tests with); the blob store sample
  on Umbraco.StorageProviders.AzureBlob 18.0.0; Microsoft.NET.Test.Sdk 18.10.1, which the 18.3.1 test package needs;
  `@umbraco-cms/backoffice` 18.1.0, the line's floor, as the 17 line builds against its own; the packages
  `18.0.0-alpha.0`.
- **OpenAPI:** `jfb`, `jfb-public` and `jfb-ai` register through `AddBackOfficeOpenApiDocument`, once each however
  often the registration runs (a keyed mark per name), `jfb` and `jfb-ai` with the backoffice's JSON settings and
  sign-in. `FormBuilderOperationIds` names every operation after its controller and action as on 17.
  `FormBuilderOpenApiSchemas` holds the rules: Jiron Form Builder's models named as Umbraco names its own; files and
  JSON values inline; validation problems with their errors; numbers as numbers; a nullable member optional unless the
  model marks it `required`; a member the model only reads (no setter, no constructor parameter) read-only where it
  is written in place, never on a model or an enum whose schema is shared, and null only when it may be; problems open
  to extensions; an optional body its model, which the client may leave out; and no media type without a body. The
  public API's base controller carries `[ApiExplorerSettings]`, which lists its actions without `[ApiController]`'s
  automatic answers, and its declarations refer to a model under the document's own name, refusing a generic type of
  another library, which the document names otherwise. Each rule has a unit test. The snapshots in `docs/api/` are written as served, OpenAPI 3.1; the
  generated clients then differ from 17's only in the order of `null` in a union, a body now required where the API
  always wanted one, one key typed nullable as its model says, and a 403 no longer listed for a download that signs in
  by ticket.
- **One test support for both lines:** the integration tests' assembly-wide action, which already released Umbraco's
  runtimes after every test, also disposes and forgets the file providers handed out during the test. It lands on
  `main`, where no host watches and nothing else changes, and `TestHostReleaseTests` fails without it on both lines.
- **The form's header (refines ADR-0213):** the header lives in the box Umbraco's row leaves it. Where that is less
  than the name's 6rem, it is cramped: the name takes what there is, the state under it is cut with an ellipsis, its
  words its title and read in full, and the header clips its parts to its box, so nothing of it lies over Umbraco's
  menu or the tabs; where Umbraco leaves nothing, neither shows until the window leaves room. On 17 the header always
  has room and nothing changes. The header test checks what Umbraco leaves, with room the design of ADR-0213.
- **The backoffice's adapters:** the forms tree and the Recycle Bin read a level through `readLevel`, which pages by
  offset and reads a window around an item from the start, 500 items at a time (the endpoints' most), until it holds
  the item, answering "not found" for one that is gone (the tree then picks another or starts from the top); the
  library's conditions read the tree item's base context.
- **Ports and sites:** the 18 sandbox on 5540 (`scripts/sandbox.mjs create --site sandbox18`, Umbraco.Templates 18.3.1
  in its own hive), the 18 demo on 5541. The lines share every other port, so only one line's gate runs at a time.
- **The install test learns the line:** an upgrade whose baseline runs on another Umbraco raises every Umbraco package
  the site names with Jiron Form Builder's; every upgrade then checks that Umbraco runs with nothing left to upgrade,
  and one across a major that Umbraco's own migration state moved on. On `v18/main` the newest baseline is a 17
  release, so its upgrade run is the real 17 to 18 upgrade of a site with data.
- **Documents:** the documentation on `v18/main` names Umbraco 18, and `docs/upgrading.md` there says what moving a
  site from 17 to 18 takes. `umbraco-marketplace.json` and the add-on's listing stay `main`'s on both lines: the
  Marketplace reads the listing from the default branch.

## Consequences

- On Umbraco 18.3, beside all the form's views, a window narrower than about 1,100 px (in German) leaves the name
  little or no room, as Umbraco's row does for every workspace with that many views; a wider window brings it back.
- The backoffice code is the same on both lines but for the tree adapters and the conditions; the generated clients and
  the snapshots differ per line.
- A fix on `main` reaches `v18/main` by merging `main` into it; a conflict there is resolved on `v18/main`.
- The script checks and the install test take the Umbraco major from the build files, so the same checks pass on both
  lines.
- Revisit `FormBuilderOpenApiSchemas` when Microsoft.AspNetCore.OpenApi describes file results itself, or Umbraco makes
  its own file rule public.
