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
| [overview.html](overview.html) | the section's start page: setup checklist, health, recent entries, forms at a glance | first run, warnings, loading, error, no access; 620 px (default, first run) |
| [form-library.html](form-library.html) | a folder's list, Umbraco's create dialog, Import a form, Unpublish…, Create a form block type…, the Recycle Bin with Restore | create, empty folder, loading, error, trashing a form in use, trashed (Undo), unpublish, import, form block type, block type created, Recycle Bin, emptying it, deleting a form for good, restore, restore elsewhere (its folder is gone), empty Recycle Bin; 620 px (default, import) |
| [template-gallery.html](template-gallery.html) | the template gallery with a preview, and Library › Templates | loading, library; 620 px (the preview over the cards, the cards) |
| [designer.html](designer.html) | the designer in three columns (ADR-0256): the row of pages and tools above the sheet, the canvas, the inspector (the open page while nothing is selected), the field picker, After sending with its outcomes and an outcome's sidebar, and Move to…, Paste questions, the shortcuts, the save conflict and "Used on" | field selected, picker, picker searched, picker without a match, page menu, a form of one page, field menu, After sending, outcome, outcome deleted (Undo), the row in two rows, tablet width (inspector folded), phone width, insert point, keyboard move, drag, label edited in place, the row's menu, shortcuts, Move to…, undo, new form, paste questions, loading, save conflict, save failed, used on, translate, skip to the form; 1024 px; 620 px (default, picker, After sending) |
| [rules-builder.html](rules-builder.html) | the rules builder and the rules overview | new rule, two groups, rule deleted, overview, overview empty; 620 px (default, overview) |
| [form-check.html](form-check.html) | the form check | no problems, a form without fields |
| [form-settings.html](form-settings.html) | the form's Settings view: Submission, Availability, Spam protection, Privacy, Appearance and Developer (the outcomes are on the designer's After sending) | More options; 620 px |
| [actions.html](actions.html) | the form's Actions view and a failed run's details | added, run details, empty, read-only, loading; 620 px (default, run details, read-only) |
| [email-editor.html](email-editor.html) | the action sidebar for an email, with the merge-tag picker and a live preview | merge tags, tested; 620 px (default, preview) |
| [preview.html](preview.html) | the full-screen preview | phone, menu, sent, run actions; 620 px |
| [publish-dialog.html](publish-dialog.html) | the publish dialog and the toasts after it | errors, no problems, published, first publish, first published; 620 px (default, errors) |
| [history.html](history.html) | the form's History, a comparison and one version | draft saves, compare, comparison, version, row menu, restore, loading; 620 px (default, draft saves) |
| [entries.html](entries.html) | a form's Entries view, its bulk bar and the export sidebar | filtered, bulk, bulk done (Undo), delete, export, empty, no results, loading, error; 620 px |
| [entry-panel.html](entry-panel.html) | the entry panel beside the Inbox, the image viewer, and Forward… | approved (Undo), timeline, notes, edit answers, reveal, image, spam, forward; 620 px (default, approved) |
| [inbox.html](inbox.html) | the Inbox, its filter menu, the column chooser, the shortcuts, the summary email and the header search | filter, columns, more, shortcuts, summary email, header search, sensitive search, empty, loading; 620 px |
| [spam.html](spam.html) | Spam | bulk, empty, emptying it; 620 px |
| [insights.html](insights.html) | the form's Insights view | scrolled, date range, table, answers, answers as table, empty, off, loading; 620 px |
| [member-entries.html](member-entries.html) | the Form entries view on Umbraco's member workspace | empty; 620 px |
| [option-list.html](option-list.html) | an option list of each kind, the list of them, and choosing a kind | content, data type, JSON address, list, create, empty; 620 px (default, JSON address) |
| [theme-editor.html](theme-editor.html) | a theme with its kitchen-sink preview, and the list of themes | dark, list; 620 px (default, preview) |
| [email-layout.html](email-layout.html) | an email layout with its preview, and the list of layouts | dark, list; 620 px (default, preview) |
| [global-settings.html](global-settings.html) | Settings › General, Email, Spam protection, Files and Integrations, and Privacy › Data collection | one site, email, no email, test email sent, spam protection, files, integrations, locked setting; 620 px (default, email, with the six tabs folded behind "…") |
| [permissions.html](permissions.html) | Settings › Access, and Permissions… on a folder | folder, restricting a folder; 620 px (default, folder) |
| [deliveries.html](deliveries.html) | Activity › Deliveries and a run's details | details, uncertain retry, limited, paused, empty, loading; 620 px |
| [audit-log.html](audit-log.html) | Activity › Audit log and an event's details | details, no results, loading; 620 px |
| [privacy-tools.html](privacy-tools.html) | Privacy › People, with its export, legal hold and Erase…, and Retention | results, exporting, exported, legal hold, held, erase preview, erased, nothing found, retention, retention edited; 620 px (default, held) |

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
  wireframes' own, and step 2.3 named the ones the backoffice code keeps in `docs/ux/tokens.md` (`--jfb-bo-*`).
- **The workspace is a container** (`container: workspace`): a list drops columns with
  `@container workspace (max-width: 899px)`, by the room the workspace has rather than the window. The designer's
  stage is one too (`stage`), so the canvas reflows when it has less room than the chosen width.
- **[assets/kit.js](assets/kit.js)** adds Umbraco's top bar and the section's sidebar for the role a page shows, draws
  the icons and switches the states. The sidebar starts with the five places the role may open (Inbox with its count,
  Library, Settings, Privacy, Activity; ADR-0257), then the tree of forms under Umbraco's header. On a tab's page the
  kit puts the place's tabs the role may open where the header's title stood, with a second-level heading for
  screen readers ("Settings: Email", the name once on "Inbox"), and folds the tabs that do not fit behind "…"; with
  one tab left, the heading alone. A page holds only its workspace and its overlays. It also gives the checkboxes,
  switches and radios the kit draws as spans the tab stop UUI's own controls have: one each, and one per group of
  radios (the chosen one, or the first).
- **The canvas, the preview and the template gallery's preview** render the visitor's form with the website theme of
  step 2.3 (`../website/assets/base.css` and `theme.css` on the website's markup, marked `data-wf-sample`); the
  screens behind a dialog (publish, form check, rules) keep a plain stand-in.
- **[assets/icons.js](assets/icons.js)** is the icon sprite: Umbraco's own backoffice icons, which it draws from
  Lucide, plus UUI's "more" symbol. Licences: [ICONS-LICENSE.txt](assets/ICONS-LICENSE.txt) and
  THIRD-PARTY-NOTICES.md.

The attributes a page uses:

| Attribute | On | Means |
| --- | --- | --- |
| `data-role` | `<body>` | the persona of ADR-0013 the page shows: `designer`, `handler`, `viewer`, `admin`, `privacy` or `none`; the sidebar and the top bar follow it |
| `data-active` | `<body>` | what is open: a place's tab (`inbox`, `themes`, `general`, …), whose place the sidebar marks and whose tabs the header shows, `form:<id>`, `folder:<id>` or `bin`; an editor opened from a tab (a theme's) names the tab and keeps its own header |
| `data-role-<state>`, `data-active-<state>` | `<body>` | the same, for one state |
| `data-state="a b"` | any element | shown only in those states |
| `data-frame="none"` | `<body>` | without the kit's frame: a full-screen modal of its own (the preview), or a page that draws another Umbraco section itself (the member workspace) |
| `data-wf-more` | a button | a "…" menu, which doesn't count as a visible action |
| `data-wf-frame` | a button | part of a modal's or panel's frame (its close button), which doesn't count |
| `data-wf-field` | a select | a setting in a header (the site switch), not an action |
| `data-wf-actions="name"` | a container | a group of actions the check counts (at most three visible) |
| `data-wf-layer` | a container | a layer of its own, with at most one primary button |
| `data-wf-grid` | a container | a grid of boxes, spaced by its gap |
| `data-wf-sample` | a container | a visitor's theme or an email layout: its colours are the sample's, not the backoffice's, so the token check leaves it out (axe still checks it) |

## What the wireframes leave to later steps

- **The look** is Umbraco's own, so the screens feel native. Step 2.3 fixed the tokens in `docs/ux/tokens.md`: UUI's
  own inputs keep UUI's border, and the controls the package draws itself get a darker one, for WCAG 1.4.11.
- **Dark mode** comes from UUI's own tokens and is not drawn here.
- **German** is checked in copy.md and in Phases 9 to 14; the wireframes are in English.
- **The visitor's side** (the rendered form) is designed in [`../website/`](../website/README.md), not here.
