# Backoffice wireframes

Static HTML pages of every screen of the **Forms** section (ROADMAP step 2.2), drawn from
[information-architecture.md](../information-architecture.md) and [copy.md](../copy.md). They are design documents:
nothing here ships, and Phases 9 to 14 build the real screens from Umbraco's UUI components. The review against
ROADMAP §4.13 is [review-2.2.md](../review-2.2.md); the product context the design skills read is
[PRODUCT.md](../../../PRODUCT.md).

Open any page straight from the disk in a browser. There is no build step. `?state=<name>` shows one of a page's
states, for example `entries.html?state=bulk`.

## The pages

| Page | Screen | States |
| --- | --- | --- |
| [overview.html](overview.html) | the section's start page: setup checklist, health, recent entries, forms at a glance | first run, warnings, loading, error, no access |
| [form-library.html](form-library.html) | a folder's list, Umbraco's create dialog, the Recycle Bin | create, empty folder, loading, error, Recycle Bin, emptying it |
| [template-gallery.html](template-gallery.html) | the template gallery with a preview, and Library › Templates | loading, library |
| [designer.html](designer.html) | the designer: palette, canvas, inspector | inspector closed, phone width, insert point, new form, loading, 620 px |
| [rules-builder.html](rules-builder.html) | the rules builder and the rules overview | new rule, two groups, overview |
| [form-check.html](form-check.html) | the form check | no problems |
| [form-settings.html](form-settings.html) | the form's Settings view and an outcome's editor | outcome |
| [actions.html](actions.html) | the form's Actions view | added, empty, read-only, loading |
| [email-editor.html](email-editor.html) | the action sidebar for an email, with the merge-tag picker and a live preview | merge tags, tested |
| [preview.html](preview.html) | the full-screen preview | phone, menu, sent |
| [publish-dialog.html](publish-dialog.html) | the publish dialog | errors, published |
| [history.html](history.html) | the form's History, a comparison and one version | draft saves, compare, comparison, version, loading |
| [entries.html](entries.html) | a form's Entries view and the export sidebar | filtered, bulk, export, empty, no results, loading, error |
| [entry-panel.html](entry-panel.html) | the entry panel beside the Inbox, and Forward… | timeline, notes, spam, forward, 620 px |
| [inbox.html](inbox.html) | the Inbox, its filter menu and the column chooser | filter, columns, sensitive search, empty, loading |
| [spam.html](spam.html) | Spam | bulk, empty, emptying it, 620 px |
| [insights.html](insights.html) | the form's Insights view | answers, table, empty, off, loading |
| [option-list.html](option-list.html) | an option list, the list of them, and choosing a kind | list, create, empty |
| [theme-editor.html](theme-editor.html) | a theme with its kitchen-sink preview, and the list of themes | dark, list |
| [email-layout.html](email-layout.html) | an email layout with its preview, and the list of layouts | dark, list |
| [global-settings.html](global-settings.html) | Settings › General, Email, and Privacy › Data collection | one site, email, locked setting |
| [permissions.html](permissions.html) | Settings › Access, and Permissions… on a folder | folder, restricting a folder |
| [deliveries.html](deliveries.html) | Activity › Deliveries and a run's details | details, uncertain retry, paused, empty, loading |
| [audit-log.html](audit-log.html) | Activity › Audit log and an event's details | details, no results, loading |
| [privacy-tools.html](privacy-tools.html) | Privacy › People and Retention | results, erase preview, nothing found, retention |

The PNGs in [png/](png/) are the rendered shots: `<page>.png` for the default state, `<page>--<state>.png` for the
others, a `--920` suffix for the 920 px window (a 620 px workspace, Umbraco's minimum; IA "One width to design
for"), and `--1024` for the designer at a 1024 px window.

## Rendering and checking

```bash
node scripts/wireframes.mjs
```

renders every shot into `png/` with Playwright in Edge (a page's shots are listed in its
`<meta name="wf-shots">`). `--check` also measures each shot against the measurable rules of ROADMAP §4.13 and the
tokens, and runs axe-core for WCAG 2.2 A and AA; a filter keeps only the pages whose name contains it:

```bash
node scripts/wireframes.mjs --check designer
```

`--widths` measures every state of every page at the given window widths instead, without writing PNGs:

```bash
node scripts/wireframes.mjs --widths=920,1024,1920
```

The script's header lists every check. Its tests are in `scripts/tests/wireframes.test.mjs`.

## How a page is built

- **[assets/kit.css](assets/kit.css)** holds the tokens and the components. The `--uui-*` tokens are Umbraco 17.7.1's
  light theme, under Umbraco's own names, so the built screens can use the same names; the `--wf-*` tokens are the
  wireframes' own until step 2.3 turns them into the design system.
- **The workspace is a container** (`container: workspace`): a list drops columns with
  `@container workspace (max-width: 899px)`, by the room the workspace has rather than the window. The designer's
  stage is one too (`stage`), so the canvas reflows when it has less room than the chosen width.
- **[assets/kit.js](assets/kit.js)** adds Umbraco's top bar and the section's sidebar for the role a page shows, draws
  the icons and switches the states. A page holds only its workspace and its overlays.
- **[assets/icons.js](assets/icons.js)** is the icon sprite: Umbraco's own backoffice icons, which it draws from
  Lucide, plus UUI's "more" symbol. Licences: [ICONS-LICENSE.txt](assets/ICONS-LICENSE.txt) and
  THIRD-PARTY-NOTICES.md.

The attributes a page uses:

| Attribute | On | Means |
| --- | --- | --- |
| `data-role` | `<body>` | the persona of ADR-0013 the page shows: `designer`, `handler`, `viewer`, `admin`, `privacy` or `none`; the sidebar and the top bar follow it |
| `data-active` | `<body>` | the sidebar item that is open: an item id (`inbox`, `themes`, …), `form:<id>`, `folder:<id>` or `bin` |
| `data-role-<state>`, `data-active-<state>` | `<body>` | the same, for one state |
| `data-state="a b"` | any element | shown only in those states |
| `data-frame="none"` | `<body>` | a full-screen modal of its own, without Umbraco's frame (the preview) |
| `data-wf-more` | a button | a "…" menu, which doesn't count as a visible action |
| `data-wf-frame` | a button | part of a modal's or panel's frame (its close button), which doesn't count |
| `data-wf-field` | a select | a setting in a header (the site switch), not an action |
| `data-wf-actions="name"` | a container | a group of actions the check counts (at most three visible) |
| `data-wf-layer` | a container | a layer of its own, with at most one primary button |
| `data-wf-grid` | a container | a grid of boxes, spaced by its gap |
| `data-wf-sample` | a container | a visitor's theme or an email layout: its colours are the sample's, not the backoffice's, so the token check leaves it out (axe still checks it) |

## What the wireframes leave to later steps

- **The look** is Umbraco's own, so the screens feel native. Step 2.3 fixes the tokens in `docs/ux/tokens.md`,
  including the proposal to draw input borders darker than UUI's (3.3:1 instead of about 1.4:1, for WCAG 1.4.11).
- **Dark mode** comes from UUI's own tokens and is not drawn here.
- **German** is checked in copy.md and in Phases 9 to 14; the wireframes are in English.
- **The visitor's side** (the rendered form) is designed in step 2.3, the default theme; the canvas and the previews
  show a neutral placeholder look.
