# Step 6.7f: the design skills' review

ROADMAP 6.7f ships the content editor's form picker: the property, its sidebar and the block label
([ADR-0134](../decisions/0134-the-form-picker-in-the-backoffice.md)). Per ADR-0019 and ADR-0020 the three design
skills were loaded before any of it was built, and ran on the result. This file records what each one said and what
was done about it. The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would
ask about was decided here or in ADR-0134.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) with a page type made for the look through the management APIs:
a single picker (the data type "Create a form block for me" makes), a picker of several forms and a block list whose
form block is labelled `Form: {jfbFormName: form}`. The library held the demo's published forms, one of them moved into
a folder "Events", and the page picked two forms, one of which was then moved to the trash, so the property shows a
form it can no longer show. Edge through Playwright, signed in as the administrator.

| Shot | What it shows |
| --- | --- |
| [property, light, 1440 px](review-6.7f/property-light-1440.png) | the single picker empty (Choose alone), the picker of several with a form and one that is not available |
| [property after choosing](review-6.7f/property-chosen-light-1440.png) | a third form added through the sidebar, in the order picked |
| [sidebar](review-6.7f/sidebar-light-1440.png) | the top level: a folder, then the forms; the property's pick chosen, with its check mark |
| [sidebar, a folder open](review-6.7f/sidebar-folder-light-1440.png) | "Events" opened in place, its form one level in |
| [sidebar, search](review-6.7f/sidebar-search-light-1440.png) | a search by alias, the form chosen from the results |
| [sidebar, no match](review-6.7f/sidebar-no-match-light-1440.png) | a search that finds nothing, with Clear search |
| [block, the catalogue](review-6.7f/block-create-light-1440.png) | Umbraco's catalogue offering the form block of 6.7b |
| [block, the form picked](review-6.7f/block-picked-light-1440.png) | the single picker inside the block's editor, standalone |
| [block label](review-6.7f/block-label-light-1440.png) | the block labelled "Form: Contact" by `{jfbFormName: form}` |
| [property, dark](review-6.7f/property-dark-1440.png) | the property in Umbraco's dark theme |
| [sidebar, dark](review-6.7f/sidebar-dark-1440.png) | a chosen row in the dark theme |
| [property, German, 1024 px](review-6.7f/property-de-1024.png) | the property in German at the narrow end of the backoffice's widths |
| [sidebar, German, 1024 px](review-6.7f/sidebar-de-1024.png) | the sidebar in German |

The look ran seven times; the first six found what the findings below fixed (and once the host's database hung, the
known SQLite limit of ADR-0011). On the last run, with the fixes in:

- **Axe** (WCAG 2.2 A and AA, the whole page, the violations inside the picker's elements counted): none on the
  property or the sidebar in the light theme; in the dark theme none on the property, and one on the sidebar, which is
  Umbraco's own positive button (see finding 6). The page's other violations are Umbraco's own elements.
- **Keyboard and focus:** the sidebar opens with the focus in the search field; Space chooses the focused form; after
  Remove the focus is on the next row's Remove (the Vitest suite can only ask for it, since jsdom keeps no focus inside
  UUI's slotted buttons); the search's count is announced ("1 form found").

## design-taste-frontend

**The read:** a property editor and its sidebar inside Umbraco 17's backoffice, for content editors placing a form on a
page and for site builders labelling blocks; Umbraco's own calm, native language; UUI's components and tokens, with
Umbraco's picker pattern (reference rows, a dashed Choose, a sidebar with a search and a tree) as the incumbent look. A
"redesign, preserve" in the skill's terms: nothing new is invented where Umbraco has the pattern.

**The dials:** variance 2, motion 1, density 5: below the skill's trust-first row, because the surface must be
indistinguishable from Umbraco's own pickers.

**Scope:** the skill's landing-page rules (hero, bento, marquees, imagery, fonts) have nothing to act on, and its
stack defaults do not apply (ADR-0020): Lit and UUI come from the backoffice. Its anti-default, contrast, state and
copy rules applied.

**Pre-flight, the boxes that apply:**

| Check | Result |
| --- | --- |
| Brief inference and dials declared | yes, above |
| Design system | Umbraco's UUI, the backoffice's own; no second system |
| No em or en dash | none in the English or German texts |
| One accent | UUI's selected colour for a chosen form, positive for Choose, as Umbraco's pickers |
| One corner system | `--uui-border-radius` on the rows; nothing else rounds |
| Button and form contrast | axe clean on our elements in both themes; Umbraco's own positive button is 4.37:1 in the dark theme (finding 6) |
| No button label wraps at desktop | "Choose", "Close", "Show more", "Try again", "Clear search"; German "Auswählen", "Schließen", "Mehr anzeigen" |
| Dark mode tested | yes, with axe: two of UUI's colours fell short there and were replaced (findings 3 and 4) |
| Empty, loading and error states | each level and the property: rows in the final shape while loading, a sentence per empty case, a failure with Try again |
| Cards only where they earn it | the sidebar's list sits in one `uui-box`, as Umbraco's tree picker does; no card per row |
| Copy self-audit | every string checked against docs/ux/copy.md: sentence case, verbs on buttons, Umbraco's own words for Choose, Close and Remove |

## impeccable

- **Mode and context:** Operate. `impeccable context` resolved the project root to the client folder and found no
  PRODUCT.md (`NO_PRODUCT_MD`), so, per ADR-0020's fallback, PRODUCT.md and DESIGN.md at the repository root were read
  directly, with docs/ux/tokens.md for the backoffice's tokens. The bundled engine only; no downloads, hooks or live
  mode. `shape` ran without its interview (rule 8): the brief is ADR-0134. The craft floor was read before the first
  edit.
- **`impeccable detect --json`** on `form-picker.element.ts`, `form-picker-modal.element.ts`, `form-name.ufm.ts` and
  `styles/tokens.ts`: `[]`.

- **The critique and polish pass**, in impeccable's order (broken paths first, then accessibility, then copy):

| # | Finding | Kind | Fix |
| --- | --- | --- | --- |
| 1 | The data type "Create a form block for me" makes named no editor UI, so the content editor showed the block's form as a property it cannot edit ("The configured property editor UI could not be found"); Umbraco does not fall back to the schema's default UI there | broken path | the server names the picker's UI (`FormPickerPropertyEditor.EditorUiAlias`); the block's integration test and the manifest test hold both ends (500ff72) |
| 2 | A form that is not available put a full sentence in the reference row's detail line, which Umbraco draws on one line at 60% opacity: in red it measured 2.59:1, the sentence was cut short at narrower widths, and it said "Remove it" beside the Remove button | accessibility (1.4.3), copy | the name and the icon carry the red; a short reason ("Deleted, unpublished or not one you can pick here") stays in the detail's own colour (0dd250c) |
| 3 | In Umbraco's dark theme UUI's danger colour is 2.5:1 on the surface, so "Form not available" failed again there | accessibility (1.4.3, 1.4.11 for the icon) | `--jfb-bo-color-danger-text`, UUI's danger mixed half and half with its text: 11.0, 5.3 and 13.1:1 in the three themes (4c6c934) |
| 4 | In the dark theme UUI's selected colour carries its text at 4.3:1 (3.7:1 for its hover), so a chosen row failed | accessibility (1.4.3) | `--jfb-bo-color-selected`, UUI's selected colour mixed 85% with the surface: 6.5, 5.0 and 6.5:1; a chosen row keeps one colour on hover, and the check mark still says it is chosen (4c6c934) |
| 5 | Umbraco's own pickers keep a picked item's state in the tree only by colour, and their Remove asks first | design decision, not a defect here | the sidebar's rows say their state (`aria-pressed`, `aria-expanded`) and show a check mark; Remove acts at once, since nothing is saved before the page (ADR-0134) |
| 6 | Umbraco's own positive primary button ("Choose" at the sidebar's foot, as on every Umbraco dialog) is 4.37:1 in the dark theme | accessibility, Umbraco's component | kept as Umbraco draws it, so the sidebar's foot matches every other dialog of the backoffice; listed in docs/OWNER-TODO.md to report to Umbraco HQ, and 9.1's check of the three themes looks at it again |

- **Kept as designed:** the rows of a level sit one indent step in from their folder, as in Umbraco's tree; the chosen
  row is lighter than Umbraco's own selected blue in the light theme, the price of one token that passes in all three
  themes.

## emil-design-eng

The picker adds no motion of its own (ADR-0020): editors choose forms many times a day, and Umbraco's sidebar already
slides in. Each moving or changing thing was put through the skill's questions:

| Before | After | Why |
| --- | --- | --- |
| A folder could have opened with a height animation | its level appears at once, the chevron turning by UUI's own symbol | Opening folders is repeated many times a day; animating height also moves layout, which the skill rules out |
| A chosen form could have faded its highlight in | the selected colour and the check mark appear at once | Selection is the most frequent action in the sidebar; an instant answer feels faster |
| Rows could have scaled on press | no press scale; the row's hover and selected colours answer | A full-width row that scales jumps at its edges; Umbraco's tree rows do not scale either |
| A spinner in the middle of the list while it loads | rows of the final rows' shape, static | Perceived speed without motion; nothing moves when the rows arrive |
| The search could have shown a spinner below the field | UUI's small loader in the field's search icon while the pause runs and the search answers | It says where the work is, in the place the editor is looking, as Umbraco's own picker search does |
| Remove could have animated the row out | the row goes at once and the focus moves to the next Remove, or to Choose | The focus move is the feedback a keyboard user needs; an exit animation would delay it |
| Umbraco's sidebar slide | unchanged | Umbraco's own, used as it is |

No transition is added; nothing animates layout.
