# Contributing to Jiron Form Builder

Thank you for your interest. This page says how work is done in this repository.

## Outside contributions are not open yet

Pull requests from outside contributors can be accepted only once a contributor licence agreement exists
(it is being prepared). Until then:

- please open an issue to report a bug or suggest a feature (see [SUPPORT.md](SUPPORT.md));
- please report security problems privately (see [SECURITY.md](SECURITY.md)).

## Licence and original work

Jiron Form Builder is free and source-available under the [PolyForm Perimeter License 1.0.1](LICENSE.md). It
is not "open source" in the OSI sense. Every contribution must be your own original work:

- never copy, decompile or derive code, markup, styles, text or icons from another product, open or closed;
- feature ideas may come from public descriptions, but write them down in your own words and design them
  your own way;
- dependencies need a licence that the project accepts (MIT, Apache-2.0, BSD-2/3-Clause, ISC or 0BSD for
  anything that ships to sites), listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Working in the repository

**Requirements:** the .NET SDK named in [global.json](global.json), Node.js 24 and git.

**Once per clone**, install the git hooks:

```
node scripts/install-hooks.mjs
```

The `pre-commit` hook checks the commit identity and runs these checks on the staged files; the
`commit-msg` hook runs the term check on the message:

| Check | What it refuses |
| --- | --- |
| `node scripts/secret-scan.mjs` | key-, token- and password-shaped values, and files such as `.env` or `*.Local.json` |
| `node scripts/term-guard.mjs` | terms from the maintainer's private list (skipped where the list is not present) |
| `node scripts/check-text.mjs` | control characters, byte-order marks and damaged encodings, and non-ASCII characters in code |

While the repository is being built, the identity check accepts only the maintainer's commit identity. It
will be opened up together with outside contributions.

**Test values** that look like secrets must contain the marker `JFB_FAKE`, so the secret scan can tell them
apart from real ones. Real keys never go into the repository, not even in tests.

**Commits** follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`,
`test:`, `chore:` and so on), one change per commit, with docs updated in the same commit as the code they
describe.

**The client** (`src/Jiron.Umbraco.FormBuilder.Web/Client`, an npm workspace) is built before a pack:

```
cd src/Jiron.Umbraco.FormBuilder.Web/Client
npm ci
npm run lint
npm test
npm run build
npm run size
```

`node scripts/pack.mjs` builds the client for the version it packs and writes the packages to
`.local/nuget-feed`. A plain `dotnet pack` refuses client assets that are missing, stale or built for
another version (`JFB001`).

**Before you push**, format and check everything:

```
node scripts/fix.mjs
node --test "scripts/tests/*.test.mjs"
```

From step 3.5 of the roadmap on, `scripts/ci-local.ps1` runs the whole gate that CI runs.

## Where things are

- [ROADMAP.md](ROADMAP.md): the plan, worked through in order.
- `docs/decisions/`: the architecture decision records (ADRs).
- `docs/WORKLOG.md`: what was done, step by step.
