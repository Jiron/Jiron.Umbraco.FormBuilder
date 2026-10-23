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

## Documents

| Material | Licence | Where |
| --- | --- | --- |
| Contributor Covenant 2.1, adopted by reference with its attribution | CC BY 4.0 | [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) |
| Wireframe icons: Umbraco CMS 17.7.1's backoffice icons (drawn from Lucide) and UUI's "more" symbol, copied into the design documents only | Lucide: ISC; Umbraco CMS and UUI: MIT | [docs/ux/wireframes/assets/icons.js](docs/ux/wireframes/assets/icons.js), licence texts in [ICONS-LICENSE.txt](docs/ux/wireframes/assets/ICONS-LICENSE.txt) |
| Website theme icons: Lucide's check, alert, chevrons, plus, x, cloud upload, file, printer, star, calendar and clock, embedded as CSS masks in the default theme (a design mock now; step 6.3 ships it and moves this row to "Shipped to sites", with the notice kept in the CSS) | ISC | [docs/ux/website/assets/theme.css](docs/ux/website/assets/theme.css), licence text in [ICONS-LICENSE.txt](docs/ux/website/assets/ICONS-LICENSE.txt) |
