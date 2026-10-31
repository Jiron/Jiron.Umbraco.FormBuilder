# Step 6.7g: the design skills' review

ROADMAP 6.7g ships the form picker's data type settings: one form or several, the allowed folders and forms on the
picker's own sidebar, the theme and the page after a send
([ADR-0135](../decisions/0135-the-form-pickers-settings-in-the-backoffice.md)). Per ADR-0019 and ADR-0020 the three
design skills were loaded before it was built (in this session, with the settings named in their brief beside 6.7f's
picker), and ran on the result. This file records what each one said and what was done about it. The owner answers no
questions during the run (CLAUDE.md rule 8), so every choice the skills would ask about was decided here or in
ADR-0135.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031), set up through the management APIs: a folder "Events" holding
one of the demo's published forms, a folder "Spring" inside it holding another, and a folder "Old"; a picker's data
type with several forms, the allowed folders Events and Old, one allowed form and the theme "Bootstrap 5", after which
Old went to the trash, so the settings show a folder the user can no longer see. Edge through Playwright, signed in as
the administrator, in Umbraco's data type workspace.

| Shot | What it shows |
| --- | --- |
| [settings, light, 1440 px](review-6.7g/settings-light-1440.png) | the five settings with their labels and help: the toggle, the allowed folders (Events, and Old as not available), the allowed forms, the theme |
| [theme and page after a send](review-6.7g/settings-theme-redirect-light-1440.png) | the lower half: the theme's select and Umbraco's document picker, after Spring was added and Old removed |
| [sidebar, folders](review-6.7g/sidebar-folders-light-1440.png) | the folder mode: folders only, no search, Events chosen as the settings had it |
| [sidebar, folders open](review-6.7g/sidebar-folders-open-light-1440.png) | Events and Spring opened by their chevrons; Spring holds no folder, and says so |
| [sidebar, a folder chosen](review-6.7g/sidebar-folders-chosen-light-1440.png) | Spring chosen by the keyboard (Tab from its chevron to its row, then Space) |
| [sidebar, forms](review-6.7g/sidebar-forms-light-1440.png) | the allowed forms' sidebar: the forms mode of 6.7f, with its search, for several |
| [theme changed](review-6.7g/settings-changed-light-1440.png) | "The form's own theme" chosen again after another theme |
| [saved](review-6.7g/settings-saved-light-1440.png) | the data type after Save |
| [setup's data type](review-6.7g/settings-defaults-light-1440.png) | the data type "Create a form block for me" makes, with the defaults: one form, nothing allowed, the form's own theme |
| [settings, dark](review-6.7g/settings-dark-1440.png) | the settings in Umbraco's dark theme |
| [sidebar, dark](review-6.7g/sidebar-folders-dark-1440.png) | the folder mode in the dark theme |
| [settings, German, 1024 px](review-6.7g/settings-de-1024.png) | the settings in German at the narrow end of the backoffice's widths |
| [sidebar, German, 1024 px](review-6.7g/sidebar-folders-de-1024.png) | the folder mode in German |

The look ran twice; the first run found finding 1. On the second, with the fix in:

- **Axe** (WCAG 2.2 A and AA, the whole page, the violations inside the picker's elements counted): none on the
  settings, the theme and page after a send, or the folder mode in the light theme; none on the settings in the dark
  theme, and one on the folder mode there, which is Umbraco's own positive button, already listed for Umbraco HQ
  (6.7f's finding 6). The page's other violations are Umbraco's own elements (its section menu's
  `label-content-name-mismatch`, its Save button in the dark theme).
- **What the server stored:** `multiple` true, `allowedFolders` Events and Spring in the order chosen, `allowedForms`
  the one form, and no theme once the form's own theme was chosen again.
- **Keyboard and focus:** the folder mode opens with the focus on the dialog, as Umbraco puts it (it has no search
  field to take it); Tab goes from a folder's chevron to its row, Space chooses the folder; after Remove the focus is
  on the next row's Remove.

## design-taste-frontend

**The read:** settings in Umbraco 17's data type workspace, for site builders who set up where editors may place a
form; Umbraco's own calm, native language, with Umbraco's settings pattern (a label and help on the left, the editor on
the right) and its picker pattern (reference rows, a dashed Choose, a sidebar) as the incumbent look. A "redesign,
preserve" in the skill's terms: the settings reuse Umbraco's toggle and document picker, and the property's own rows
and sidebar for the rest.

**The dials:** variance 2, motion 1, density 5, as for 6.7f: the surface must be indistinguishable from Umbraco's own
settings.

**Scope:** as in 6.7f, the skill's landing-page rules and stack defaults have nothing to act on (ADR-0020); its
anti-default, contrast, state and copy rules applied.

**Pre-flight, the boxes that apply:**

| Check | Result |
| --- | --- |
| Brief inference and dials declared | yes, above |
| Design system | Umbraco's UUI, the backoffice's own; Umbraco's toggle and document picker used as they are |
| No em or en dash | none in the English or German texts |
| One accent | the property's: UUI's selected colour (through `--jfb-bo-color-selected`) for a chosen folder, positive for Choose |
| One corner system | `--uui-border-radius` on the rows and the chevrons; nothing else rounds |
| Button and form contrast | axe clean on our elements in both themes; Umbraco's own positive button as in 6.7f |
| No button label wraps at desktop | "Choose", "Close", "Remove", "Try again"; German "Auswählen", "Schließen", "Entfernen" |
| Dark mode tested | yes, with axe; the tokens of 6.7f carry the not-available row and a chosen folder |
| Empty, loading and error states | each setting: rows in the final shape while loading, a box in the select's shape while the themes load, a failure with Try again; the folder mode's empty top level and empty folder each say what makes a folder show |
| Cards only where they earn it | none added: the settings sit in Umbraco's settings box, the sidebar's list in one `uui-box` |
| Copy self-audit | every string checked against docs/ux/copy.md; "say it once": the rule that no allowed folders or forms allow every form is said once, on the allowed forms, and the help of the toggle is left out because its label says it all |

## impeccable

- **Mode and context:** Operate, a refinement inside Umbraco's design system; the bundled engine only, no downloads,
  hooks or live mode; PRODUCT.md and DESIGN.md at the repository root read directly as in 6.7f, with
  docs/ux/tokens.md. The craft floor was read before the first edit.
- **`impeccable detect --json`** on `picked-rows.ts`, `form-picker.element.ts`, `form-picker-allowed.element.ts`,
  `form-picker-theme.element.ts` and `form-picker-modal.element.ts`: `[]`.
- **The critique and polish pass**, in impeccable's order (broken paths first, then accessibility, then copy):

| # | Finding | Kind | Fix |
| --- | --- | --- | --- |
| 1 | uui-select keeps the empty value for a hidden placeholder of its own; "The form's own theme" had the empty value too, so choosing it after another theme would have shown the placeholder's blank in the closed select | broken path | the option takes `(own)`, which no theme's alias can be (they start with a letter), and still writes no value; the Vitest suite holds both, and the second look showed the option after Default and back |
| 2 | A folder below a chosen one can be chosen too, and adds nothing, as a chosen folder allows the folders below it | design decision | kept: refusing it would need the sidebar to know every chosen folder's place, and it does no harm; the allowed folders' help says the folders below count |
| 3 | In the folder mode every folder has a chevron, also one that holds forms but no folder: the levels answer whether a folder leads to a form, not whether it holds folders | design decision | kept, with a sentence where the folders would be ("No folder in here holds a published form yet."), so opening one never shows nothing; telling the two apart would need another answer from the endpoint for one sentence |
| 4 | Two buttons on one row: a chosen folder's highlight covers the row's button only, not its chevron | design decision, not a defect | kept: it shows which part chooses; the chevron has its own hover, its own focus ring and its own name ("Show the folders in Events") |
| 5 | The help of the toggle would repeat its label | copy | left out; the toggle's accessible name is its label, set through Umbraco's `ariaLabel` setting |

- **Kept as designed:** the not-available row of a folder says "Deleted, in the trash or not one you can see", as the
  form's says its own reasons; the settings' pickers apply no data type's limits (ADR-0135).

## emil-design-eng

The settings add no motion (ADR-0020). Each changing thing was put through the skill's questions:

| Before | After | Why |
| --- | --- | --- |
| The folder mode could have animated a folder's level open | its level appears at once, the chevron turning by UUI's own symbol | The same rule as 6.7f's levels: frequent, and a height animation moves layout |
| A chosen folder could have faded its highlight in | the selected colour and the check mark appear at once | Choosing is the sidebar's most frequent action; an instant answer feels faster |
| The theme could have shown a spinner while the themes load | a static box in the select's shape | Nothing moves when the select arrives, and the settings below it keep their place |
| Remove could have animated the row out | the row goes at once and the focus moves on | The focus move is the feedback; an exit animation would delay it |

No transition is added; nothing animates layout.
