# Third-party notices

Jiron Form Builder is the original work of Manassés Zähnler, licensed under the PolyForm Perimeter License
1.0.1 ([LICENSE.md](LICENSE.md)). This file lists the third-party material it uses (CLAUDE.md rule 4).

`scripts/third-party-notices.mjs` writes the two generated parts of this file from the restored packages, and
checks that the development table names every direct development tool with the versions and licences restored now
(`--check`, in the local gate and in CI). `scripts/check-npm-licences.mjs` and `scripts/check-nuget-licences.mjs`
check every package, direct and transitive ([ADR-0032](docs/decisions/0032-ci-gate-and-licence-checks.md)).

## Shipped to sites

Runtime dependencies, direct and transitive, may only be MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC or
0BSD.

<!-- jfb-shipped:start -->
| Package | Version | Licence | Copyright |
| --- | --- | --- | --- |
| `AngleSharp` | 1.7.2 | MIT | Copyright 2013-2026, AngleSharp. |
| `AngleSharp.Css` | 1.0.2 | MIT | - |
| `HtmlSanitizer` | 9.2.1039 | MIT | Copyright 2013-2026 Michael Ganss |
<!-- jfb-shipped:end -->

Umbraco CMS packages, and what they bring, are not listed here: sites already run them, and Jiron Form Builder only
declares them as dependencies.

Material embedded in the shipped files, which no package graph lists:

| Material | Licence | Where |
| --- | --- | --- |
| Lucide's check, alert, chevrons, plus, x, cloud upload, file, printer, star, calendar, clock and mail icons, as CSS masks in the default theme (ROADMAP 6.3b) | ISC, with its notice at the top of the stylesheet, which the build keeps | [packages/themes/src/default.css](src/Jiron.Umbraco.FormBuilder.Web/Client/packages/themes/src/default.css), served as `/jfb/assets/themes/default.css` |
| Lucide's star, outlined and filled, as CSS masks on a rating's points in the bootstrap5 theme (ROADMAP 6.3d) | ISC, with its notice at the top of the stylesheet, which the build keeps | [packages/themes/src/bootstrap5.css](src/Jiron.Umbraco.FormBuilder.Web/Client/packages/themes/src/bootstrap5.css), served as `/jfb/assets/themes/bootstrap5.css` |
| Lucide's chevron up and chevron down, as CSS masks on a ranking's moves in the base rules every theme ships (ROADMAP 6.3c) | ISC, with its notice at the top of the stylesheet, which the build keeps | [packages/themes/src/base.css](src/Jiron.Umbraco.FormBuilder.Web/Client/packages/themes/src/base.css), served as `/jfb/assets/themes/base.css` |

## Development only (never shipped)

These tools build and test Jiron Form Builder. They are used unmodified and are not part of any package.

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| `@playwright/test`, `playwright`, `playwright-core` | 1.63.0 | Apache-2.0 | browser checks and screenshots (`scripts/`), the browser tests (`tests/e2e/`) |
| `axe-core` | 4.14.0 | MPL-2.0 | accessibility checks (`scripts/`, `tests/e2e/`) |
| `@axe-core/playwright` | 4.14.0 | MPL-2.0 | accessibility checks in Playwright (`scripts/`, `tests/e2e/`) |
| `@actions/workflow-parser` | 0.3.61 | MIT | GitHub's workflow parser, for the workflow check (`scripts/`) |
| `yaml` | 2.9.1 | ISC | reading the workflow files and `dependabot.yml` (`scripts/`) |
| `NUnit` | 3.14.0 | MIT | unit and integration tests (`tests/`) |
| `NUnit3TestAdapter` | 5.0.0 | MIT | running the NUnit tests (`tests/`) |
| `Microsoft.NET.Test.Sdk` | 18.9.0 | MIT | running the tests, and with its MIT dependency Microsoft.CodeCoverage the coverage of the phase reviews, instrumented in memory (ADR-0079) (`tests/`) |
| `NSubstitute` | 6.2.0 | BSD-3-Clause | test doubles in unit and integration tests (`tests/`) |
| `JsonSchema.Net` | 8.0.5 | MIT | validating definitions against the schema in `schemas/` in the unit tests, and the element endpoint's answers against the element's schema in the integration tests, with its MIT dependencies JsonPointer.Net, Json.More.Net and Humanizer.Core; kept below 9.0, whose releases carry a fee agreement (ADR-0034) (`tests/`) |
| `Umbraco.Cms.Tests.Integration` | 17.7.1 | MIT | integration tests on a real Umbraco (`tests/`) |
| `Umbraco.Cms` | 17.7.1 | MIT | the demo site, a whole Umbraco site on SQLite (`demo/`) |
| `MimeKit` | 4.17.0 | MIT | reading the test hosts' mail in the Testing project; Umbraco already brings it (`tests/`) |
| `@umbraco-cms/backoffice` | 17.5.0 | MIT | the backoffice extension's types; external in its build, since Umbraco provides it in the page (`Client/`) |
| `@hey-api/openapi-ts` | 0.99.0 | MIT | generating the management API client (`Client/`) |
| `typescript` | 6.0.3 | Apache-2.0 | type checking (`Client/`, `tests/e2e/`) |
| `vite` | 8.3.3 | MIT | the library builds (`Client/`) |
| `terser` | 5.51.2 | BSD-2-Clause | a second minifying pass over the website runtime's build, besides the bundler's ([ADR-0119](docs/decisions/0119-a-terser-pass-for-the-website-runtime.md)) (`Client/`) |
| `vitest` | 5.0.3 | MIT | the client tests (`Client/`) |
| `jsdom` | 30.1.2 | MIT | the browser environment of the client tests (`Client/`) |
| `eslint`, `@eslint/js` | 10.12.0, 10.0.1 | MIT | linting (`Client/`) |
| `typescript-eslint` | 8.71.0 | MIT | linting TypeScript (`Client/`) |
| `eslint-config-prettier` | 10.1.8 | MIT | leaving formatting to Prettier (`Client/`) |
| `globals` | 17.13.0 | MIT | the browser and Node globals for linting (`Client/`) |
| `prettier` | 3.9.9 | MIT | formatting (`Client/`, `tests/e2e/`) |
| `@types/node` | 24.19.1 | MIT | Node types for the build scripts and the browser tests (`Client/`, `tests/e2e/`) |

`Client/` is `src/Jiron.Umbraco.FormBuilder.Web/Client/`. The transitive packages of these tools are MIT,
Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, 0BSD, MPL-2.0, BlueOak-1.0.0 or Python-2.0, with two kinds of
exception. What the Umbraco packages above bring is Umbraco's platform, as on every site
([ADR-0032](docs/decisions/0032-ci-gate-and-licence-checks.md)). And the packages below rely on public-domain
dedications, which are accepted for development tools only
([ADR-0027](docs/decisions/0027-public-domain-dedications-in-development-tools.md)): the MIT-0 and CC0-1.0
packages come through jsdom, the Unlicense one through a peer of `@umbraco-cms/backoffice`.

<!-- jfb-public-domain:start -->
| Package | Version | Licence | Where |
| --- | --- | --- | --- |
| `@csstools/color-helpers` | 6.1.2 | MIT-0 | `Client/` |
| `@csstools/css-syntax-patches-for-csstree` | 1.1.15 | MIT-0 | `Client/` |
| `fetch-cookie` | 2.2.0 | Unlicense | `Client/` |
| `mdn-data` | 2.27.1 | CC0-1.0 | `Client/` |
<!-- jfb-public-domain:end -->

## In the demo (never shipped)

Material copied into the demo, which no package graph lists:

| Material | Licence | Where |
| --- | --- | --- |
| Bootstrap 5.3.8's compiled stylesheets, left to right and right to left, as published (npm `bootstrap@5.3.8`), so the demo and the browser tests show the `bootstrap5` theme on a page that loads Bootstrap, as a Bootstrap site does (ROADMAP 6.3d); `scripts/tests/demo-vendor.test.mjs` holds them to the published files | MIT, with its licence beside them | [demo/.../wwwroot/demo/vendor/bootstrap-5.3.8/](demo/Jiron.Umbraco.FormBuilder.Demo/wwwroot/demo/vendor/bootstrap-5.3.8/LICENSE) |

## Documents

| Material | Licence | Where |
| --- | --- | --- |
| Contributor Covenant 2.1, adopted by reference with its attribution | CC BY 4.0 | [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) |
| Wireframe icons: Umbraco CMS 17.7.1's backoffice icons (drawn from Lucide) and UUI's "more" symbol, copied into the design documents only | Lucide: ISC; Umbraco CMS and UUI: MIT | [docs/ux/wireframes/assets/icons.js](docs/ux/wireframes/assets/icons.js), licence texts in [ICONS-LICENSE.txt](docs/ux/wireframes/assets/ICONS-LICENSE.txt) |
| Website theme icons in the design mocks: Lucide's check, alert, chevrons, plus, x, cloud upload, file, printer, star, calendar, clock and mail, as CSS masks, and its image icon in the stand-in photo (the shipped theme carries the same icons, under "Shipped to sites") | ISC | [docs/ux/website/assets/theme.css](docs/ux/website/assets/theme.css) and [placeholder.svg](docs/ux/website/assets/placeholder.svg), licence text in [ICONS-LICENSE.txt](docs/ux/website/assets/ICONS-LICENSE.txt) |
