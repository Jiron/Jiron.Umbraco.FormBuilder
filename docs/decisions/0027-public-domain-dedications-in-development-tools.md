# 0027. Public-domain dedications in development tools

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.2 (decided, from the client workspace's licence scan); 3.5's `scripts/check-npm-licences.mjs`
  enforces it

## Context

CLAUDE.md rule 4 names the licences Jiron Form Builder accepts: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC
and 0BSD for anything that ships to sites, and in addition MPL-2.0, BlueOak-1.0.0, CC-BY-4.0 data and Python-2.0
for development tools used unmodified. It forbids GPL, AGPL, LGPL, SSPL, source-available and commercial licences
anywhere.

The client workspace of 3.2 installs 404 npm packages, all of them development tools (none of the four client
packages has a runtime dependency, ADR-0026). A scan of its lock file found five whose licences the rule does not
name:

| Package | Licence | Brought by |
| --- | --- | --- |
| `@csstools/color-helpers` 6.1.2 | MIT-0 | jsdom 30.1.2 (Vitest's browser environment) |
| `@csstools/css-syntax-patches-for-csstree` 1.1.15 | MIT-0 | jsdom |
| `mdn-data` 2.27.1 | CC0-1.0 (data) | jsdom, through `css-tree` |
| `fetch-cookie` 2.2.0 | Unlicense | `@microsoft/signalr`, a peer of `@umbraco-cms/backoffice` 17.5.0 |
| `@heximal/expressions` 0.1.5 | "BSD 3-Clause" | a peer of `@umbraco-cms/backoffice` |

The last one is BSD-3-Clause spelled without its hyphen. The other three are public-domain dedications: MIT-0 is
MIT without the condition to keep the notice, the Unlicense and CC0-1.0 give up every right the law allows and fall
back to a licence without conditions where it does not. Each asks less of Jiron Form Builder than MIT does.
`@umbraco-cms/backoffice` is Umbraco HQ's own package and cannot be left out, and jsdom is the browser environment
the tests of the runtime, the element and the backoffice need.

## Options

1. **Refuse them,** and replace jsdom and the backoffice types. The backoffice types have no replacement, so the
   backoffice package could not be built.
2. **Accept public-domain dedications (MIT-0, Unlicense, CC0-1.0) like the permissive licences,** and read licence
   names in their usual spellings ("BSD 3-Clause" as BSD-3-Clause).

## Decision

**Option 2, for development tools only.** MIT-0, the Unlicense and CC0-1.0 count like the permissive licences in
packages that never ship. Shipped code keeps rule 4's list exactly; a shipped dependency under a public-domain
dedication would need the owner's word first. npm's `UNLICENSED` (no licence: all rights reserved) is not the
Unlicense and stays refused, as do a missing licence and anything the rule forbids.

## Consequences

- The client workspace's tools, and the backoffice types the backoffice cannot be built without, are within the
  rule; `THIRD-PARTY-NOTICES.md` notes the dedications in its development section.
- 3.5's `scripts/check-npm-licences.mjs` (ported) accepts exactly this set for development packages and rule 4's
  list for everything else, and is tested with `MIT-0`, `Unlicense`, `CC0-1.0` and `BSD 3-Clause` as accepted in
  a development package, a public-domain dedication refused in a shipped one, and `UNLICENSED`, an empty licence
  and the forbidden families refused everywhere.
- **Revisit** if a public-domain package turns out to carry other terms (a patent clause or a field-of-use limit)
  in its files.
