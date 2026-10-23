# Third-party notices

Jiron Form Builder is the original work of Manassés Zähnler, licensed under the PolyForm Perimeter License
1.0.1 ([LICENSE.md](LICENSE.md)). This file lists the third-party material it uses (CLAUDE.md rule 4).

From step 3.5 on, `scripts/third-party-notices.mjs` generates the package sections of this file from the
restored packages and checks that it is current.

## Shipped to sites

Runtime dependencies, direct and transitive, may only be MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC or
0BSD.

None yet. Umbraco CMS packages are not listed here: sites already run them, and Jiron Form Builder only
declares them as dependencies.

## Development only (never shipped)

These tools build and test Jiron Form Builder. They are used unmodified and are not part of any package.

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| `@playwright/test`, `playwright`, `playwright-core` | 1.63.0 | Apache-2.0 | browser checks and screenshots (`scripts/`) |
| `axe-core` | 4.14.0 | MPL-2.0 | accessibility checks (`scripts/`) |
| `@axe-core/playwright` | 4.14.0 | MPL-2.0 | accessibility checks in Playwright (`scripts/`) |
| `NUnit` | 3.14.0 | MIT | unit and integration tests (`tests/`) |
| `NUnit3TestAdapter` | 5.0.0 | MIT | running the NUnit tests (`tests/`) |
| `Microsoft.NET.Test.Sdk` | 18.9.0 | MIT | running the tests (`tests/`) |
| `NSubstitute` | 6.2.0 | BSD-3-Clause | test doubles in unit tests (`tests/`) |
| `coverlet.collector` | 10.1.0 | MIT | test coverage (`tests/`) |
| `Umbraco.Cms.Tests.Integration` | 17.7.1 | MIT | integration tests on a real Umbraco (`tests/`) |
| `Umbraco.Cms` | 17.7.1 | MIT | the demo site, a whole Umbraco site on SQLite (`demo/`) |
| `@umbraco-cms/backoffice` | 17.5.0 | MIT | the backoffice extension's types; external in its build, since Umbraco provides it in the page (`Client/`) |
| `@hey-api/openapi-ts` | 0.99.0 | MIT | generating the management API client (`Client/`) |
| `typescript` | 6.0.3 | Apache-2.0 | type checking (`Client/`) |
| `vite` | 8.3.3 | MIT | the library builds (`Client/`) |
| `vitest` | 5.0.3 | MIT | the client tests (`Client/`) |
| `jsdom` | 30.1.2 | MIT | the browser environment of the client tests (`Client/`) |
| `eslint`, `@eslint/js` | 10.12.0, 10.0.1 | MIT | linting (`Client/`) |
| `typescript-eslint` | 8.71.0 | MIT | linting TypeScript (`Client/`) |
| `eslint-config-prettier` | 10.1.8 | MIT | leaving formatting to Prettier (`Client/`) |
| `globals` | 17.13.0 | MIT | the browser and Node globals for linting (`Client/`) |
| `prettier` | 3.9.9 | MIT | formatting (`Client/`) |
| `@types/node` | 24.19.1 | MIT | Node types for the build scripts (`Client/`) |

The client workspace's 391 transitive packages are MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, 0BSD,
MPL-2.0, BlueOak-1.0.0 or Python-2.0, except four: `@csstools/color-helpers` and
`@csstools/css-syntax-patches-for-csstree` (MIT-0) and `mdn-data` (CC0-1.0) through jsdom, and `fetch-cookie`
(Unlicense) through a peer of `@umbraco-cms/backoffice`. These public-domain dedications are accepted for
development tools only ([ADR-0027](docs/decisions/0027-public-domain-dedications-in-development-tools.md)).

## Documents

| Material | Licence | Where |
| --- | --- | --- |
| Contributor Covenant 2.1, adopted by reference with its attribution | CC BY 4.0 | [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) |
| Wireframe icons: Umbraco CMS 17.7.1's backoffice icons (drawn from Lucide) and UUI's "more" symbol, copied into the design documents only | Lucide: ISC; Umbraco CMS and UUI: MIT | [docs/ux/wireframes/assets/icons.js](docs/ux/wireframes/assets/icons.js), licence texts in [ICONS-LICENSE.txt](docs/ux/wireframes/assets/ICONS-LICENSE.txt) |
| Website theme icons: Lucide's check, alert, chevrons, plus, x, cloud upload, file, printer, star, calendar and clock, embedded as CSS masks in the default theme (a design mock now; step 6.3 ships it and moves this row to "Shipped to sites", with the notice kept in the CSS) | ISC | [docs/ux/website/assets/theme.css](docs/ux/website/assets/theme.css), licence text in [ICONS-LICENSE.txt](docs/ux/website/assets/ICONS-LICENSE.txt) |
