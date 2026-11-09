# Phase 12 review: the UX walk

Step 12.4's last part, 12.6, walks every screen Phase 12 added: Library › Option lists and an option list, Themes and a
theme, Email layouts and a layout, Templates and Save as template…, Settings › General for all sites and for one site,
Email, Spam protection, Files and Integrations, Privacy › Data collection, Settings › Access and a folder's
Permissions…, Activity › Deliveries with a delivery's details, the Audit log with an action's details, and Replace… on
a form. It reads the result against ROADMAP §4.13 with the three design skills of ADR-0020, follows the Overview's setup
checklist end to end on a freshly reset demo, and closes the phase with its wiring sweep. What changed is decided in
[ADR-0221](../decisions/0221-the-phase-12-ux-walk.md).

## How the walk works

`tests/e2e/specs/ux/walk.spec.ts` (`JFB_E2E_UX=1`, project `ux`, then `ux-sheets`) now visits Phase 12's 20 screens
beside those of Phases 9 to 11, in twelve combinations, 375 × 812, 768 × 1024 and 1440 × 900, light and dark, English
and German. This review's walks shot those 20 and the seven earlier ones Phase 12 changed (the Overview with its
checklist, a folder, its empty state and its name dialog, Import, General and Data collection), 27 shots a look. The
walk finds its way by tags, `data-mark` and icons, never by words. Its Phase 12 items come from the API: an option list,
a theme and a template of the look's own, an email layout (`/jfb-testing/mail-layouts`), and a form with a limited, a
failed and an uncertain delivery (`/jfb-testing/deliveries`); the audit log holds what the seeding did. The Permissions
shot adds a group to a folder before it shoots, and Replace… reads the look's own export of a form. Each shot keeps
axe's WCAG 2.2 A and AA findings inside the section's own elements, the page's sideways overflow, boxes that scroll
inside themselves and words cut by an ellipsis.

## What was looked at

The last walk ran after the fixes (`.local/ux/12.6-2`, not committed), all 27 shots in all twelve looks, with its
contact sheets. These shots stand for it; "before" is the first walk (`.local/ux/12.6-1`).

| Shot | What it shows |
| --- | --- |
| [Deliveries at 768 px](review-12/deliveries-768.png) ([before](review-12/deliveries-768-before.png)) | what each delivery is under whom it went to, on a line of its own; before, "Email the / team" in a squeezed column beside an address on three lines |
| [Deliveries, German](review-12/deliveries-de.png) | state, recipient, what, form and when at 1440 px, problems first |
| [A delivery on a phone](review-12/delivery-phone.png) ([before](review-12/delivery-phone-before.png)) | a limited mail: the address broken inside the sidebar, the limit with its key, no retry; before, the address past the sidebar's edge |
| [The audit log, German](review-12/audit-log-de.png) | who did what where, the search, What, When and Export in one row |
| [An action, dark](review-12/audit-entry-dark.png) | who, when with its zone, where, and Open the form's History |
| [General for one site, German](review-12/settings-site-de.png) ([before](review-12/settings-site-de-before.png)) | the site and the language of its texts, the line on where shared settings live, "Standard-Theme"; before, "Standarddesign" |
| [Email](review-12/settings-email.png) | the sender, the default layout, the team's addresses, Send test email beside Save |
| [Spam protection at 768 px, dark](review-12/spam-768-dark.png) | the levels with a captcha without keys not to be chosen and its key, the consent mode |
| [Access, dark, German](review-12/access-dark-de.png) | the four groups without the section named once with their links, the matrix in its three parts, the administrators fixed, the shade at the right edge |
| [Permissions… on a phone, German](review-12/permissions-phone-de.png) | Only these groups, Add a user group, the group's column with its remove |
| [An option list](review-12/option-list.png) | the options in 9.4's editor, the upload and its line, the preview as a dropdown and as buttons |
| [A theme, German](review-12/theme-de.png) | the colours with each contrast under its colour, beside the kitchen-sink form with an error shown |
| [An email layout, dark](review-12/email-layout-dark.png) | the look, the logo, the colours with their checks, beside the sample entry's mail, Light · Dark and Send test email |
| [Replace… on a phone, German](review-12/replace-phone-de.png) | the file, what it holds, what this site lacks, Replace the draft |

## What the first walk measured

The first walk (`.local/ux/12.6-1`, not committed, run twice as Umbraco 17's SQLite hung under four workers) took 319
of the 324 shots. Inside the section's own elements:

- **Deliveries at the 620 px workspace** squeezed its columns: a long address on three lines and "Email the / team"
  on two (the shots, not a measure).
- **A delivery's sidebar** scrolled sideways by 7 to 11 px at 375 px, on a long address (4 shots).
- **Settings › Access** scrolls inside its box in every look (12 shots), as ADR-0218 has it.
- **Words cut by an ellipsis:** a delivery's headline (12 shots), Permissions… and Replace… on long names at 375 px
  (4 and 3), each a sidebar's one-line headline.
- **axe colour contrast:** Umbraco's open row of the tree in the dark theme on the folder's two screens (12 shots).
- **Five shots not taken:** the tree row's "…" timed out in three looks for Save as template… and in two for Replace….
- **Before the walk,** one look at 1440 px found the email layout editor's groups touching and the option list editor
  scrolling sideways by 43 px; both were fixed before the first walk (`496c167c`).

## design-taste-frontend

Reading this as: an operator's tool inside Umbraco's backoffice, for the administrators and developers who set a site's
forms up once and look back when something went wrong, in Umbraco's own language (UUI, its tokens, sidebars, footers
and toasts), leaning toward restraint: variance 2, motion 1, density 5. Settings are read more than changed, and the
activity screens are lists to scan.

| Finding | What was done |
| --- | --- |
| Deliveries squeezed four columns into the 620 px workspace: "Email the / team" on two lines beside an address on three | as the entries list does since Phase 11, what a delivery is goes under whom it went to at 760 px and narrower, and the form gives way; each fact keeps its line (ADR-0221, decision 1) |
| Sending paused: a thick warning border down one side of the notice | the note the publish dialog draws: a quiet frame and the warning's icon beside its words (decision 2) |
| One look across the screens | held: one settings screen draws every section (ADR-0218), the library's lists share their table and header (ADR-0214), the activity screens share their tools row; no colour of the package's own beyond Umbraco's states and the selected colour |
| Every state composed | held: quiet lines while reading, a failed read with Try again, the empty lists in the words of what to do next, nothing found for a filter, a host's lock with its key, a missing permission named, a save someone made first |
| Settings › Access scrolls sideways even at 1440 px with the test site's groups | kept: a matrix of groups by permissions; the permission column stays put and a shade says more is to the right (ADR-0218) |
| Small sidebars for a delivery, an action, a folder's permissions and Replace…; small dialogs for a new option list, a new theme and a template's name | kept: each is the size its content asks for, and the list stays in sight beside a sidebar |

## impeccable

The bundled detector (`impeccable detect --json`, no downloads, hooks or live mode) on Phase 12's 26 files: the eight
settings files, the access screen, matrix and Permissions sidebar, the activity screens and their two sidebars, the
import dialog with Replace, the option list, theme and email layout screens with their editors and new-item dialogs,
Library › Templates and the tokens. It found one: a side-tab accent border, the paused notice's 4 px edge
(`deliveries.screen.ts:69`). After the fix, `[]`. It cannot see a layout's widths, so the walk's measures and its shots
carried the critique and the audit.

| Finding | What was done |
| --- | --- |
| The option list editor's hidden file input drawn at full width, so its screen scrolled sideways by 43 px at 1440 px (WCAG 1.4.10) | the visually hidden utility now wins over any sizing rule a control's styles bring (`jfbVisuallyHidden`, decision 3) |
| A long address pushed a delivery's sidebar 11 px sideways at 375 px (WCAG 1.4.10) | the sidebar's sentences break an address anywhere, as its rows already did (decision 4) |
| The email layout editor's groups touched each other: the grid's gap sat on the box, which lays out its own content | the groups sit in a block of their own inside the box, 24 px apart (decision 5) |
| "Standarddesign" for the default theme in Settings › General, where every other German screen says "Standard-Theme" and the glossary keeps "design" off the word | "Standard-Theme" (decision 6) |
| The paused notice's side-tab border | a quiet frame with the icon (decision 2) |
| A long name in the forms tree ran on past its box, under the row's "…" and past the sidebar's edge, so a click on the "…" opened the form | the tree's label is Umbraco's own `#label`, which ends a name with an ellipsis; a browser test holds that the name ends before the "…" (decision 11) |
| The walk's Permissions shot found no table: a folder without groups shows only Add | the walk adds a group first, so the shot shows the table it is for (decision 7) |
| The walk's tree-row "…" timed out now and then: it brought a row only just into sight, at the sidebar's edge, where a row moves as the pointer reaches it | the walk brings the row to the sidebar's middle as the browser specs do, and tries the hover and the menu's opening again together until the item shows (decision 7) |

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| a settings box's More options opening | kept: Umbraco's own disclosure, no motion of ours | it opens once per visit; the box's height follows at once |
| the site switch redrawing the boxes | kept without motion | the values change in place; a fade would make a slow read look like a slow screen |
| Save in the footer while it saves | kept: UUI's waiting state, then a toast | the button says it is busy where the user pressed |
| the access matrix's boxes | kept without motion | a box ticked with what it needs ticks them at once and says so in one line; a cascade would only delay it |
| Deliveries and the audit log reading a filter | kept: the rows stay until the answer replaces them | the list keeps its place; only its rows change |
| Library › Templates' cards | kept: 120 ms to `scale(0.99)` on press and the border on hover, none with reduced motion | as the gallery's cards since 12.3 |
| what a delivery is moving under its address at 760 px | moved without motion | it moves once, when the workspace's width changes; nobody watches a layout follow a resize |

## The words

Read in both languages against copy.md, whose glossary holds the phase's words: option list ("Optionsliste"), theme
("Theme"), template ("Vorlage"), delivery ("Zustellung"), audit log ("Protokoll") and site ("Website"). One label had
left it, "Standarddesign" in Settings › General, now "Standard-Theme" as everywhere else. The settings' help lines, the
access matrix's permissions and the activity screens' states each have their row in copy.md, and the dictionaries'
tests hold both languages to the same keys and the activity and access areas to the keys their code uses.

## Kept as they are

- Umbraco's 920 px page, its tree's rows' names and its open row in the dark theme (ADR-0182, OWNER-TODO).
- A delivery's sidebar names the address in its first sentence although its headline names it too: the headline cuts a
  long address with an ellipsis, so the sentence is the one place it is whole.
- A long name in a sidebar's headline cut with an ellipsis (Permissions… and Replace… on a long form or folder name):
  the headline keeps one line, and the name it cuts is the one the user has just chosen.
- Settings › Access scrolls sideways once the groups pass the workspace, with the permissions' column fixed and a shade
  (ADR-0218).
- The Permissions sidebar lists Add a group above the groups' table: who has access is chosen first, then what they
  may do.
- Forms' own words in the activity screens, such as an action named "Email the team" in a German backoffice: a form's
  texts are its editors', in the languages they wrote.

## The §4.13 checklist, screen by screen

One primary action where Umbraco puts it, at most three secondary actions in sight, each fact said once, nothing
squashed at the 620 px workspace, and the screen's states.

| Screen | Primary | Secondary in sight | Said twice? | At 620 px | States |
| --- | --- | --- | --- | --- | --- |
| Library › Option lists | Create option list (footer) | a row's "…" | no: a list's kind and use once, in their columns | all three columns; the kind gives way below 480 px | reading, none yet in the words of what lists are for, failed with Try again, not allowed, Show more |
| An option list | Save (footer) | Fetch now (a URL list), the "…" | no: the preview shows the items once, as a dropdown or as buttons | one column, the preview under the settings | reading, a URL list's last fetch and its failure, a CSV that could not be read, the list in use by forms named in the delete |
| Library › Themes | Create theme (footer) | Import a theme…, a row's "…" | no | as the option lists | reading, none yet, failed with Try again |
| A theme | Save (footer) | Light · Dark, the "…" | no: each colour's contrast once, under it | the settings or the preview, by a switch | reading, a base theme the site no longer has, a contrast that fails said at the colour, what was taken out of the custom CSS |
| Library › Email layouts | Create email layout (footer) | a row's "…" | no | as the option lists | reading, none yet, failed with Try again |
| An email layout | Save (footer) | Send test email, Light · Dark | no | the settings or the preview, by a switch | reading, the test email sent or failed with the reason |
| Library › Templates | Use template | a template's "…" | no: a template's line once, beside its preview | one column: the cards, then the chosen one | reading, failed, only built-in ones, a group's line on where it is used |
| Save as template… | Save template | Cancel | no | Umbraco's small dialog | a name needed |
| A settings section (General, Email, Spam protection, Files, Integrations, Data collection) | Save (footer) | Send test email (Email), More options in a box of more than seven | no: a setting's help once, under it; a host lock's key once | one column | reading, locked by the host with its key and no Save, a site's own value with "Use the value for all sites", failed with Try again, not allowed, a save someone made first |
| Settings › Access | Save (footer) | Add folder or form…, a group's link to Umbraco's user group | no: a group without the section named once, above the matrix | the matrix scrolls with its first column fixed | reading, administrators fixed, a box's needs ticked with it and said, a change made meanwhile |
| Permissions… | Save | Cancel, Only these groups | no | Umbraco's small sidebar | no group yet, asks before restricting to none |
| Activity › Deliveries | — | the search and three filters, a row opens it | no: sending paused said once, above the list | what it is under whom it went to, the form gives way | reading, none yet, nothing matches, failed with Try again, not allowed, Show more |
| A delivery | Retry (failed or uncertain) | Close | the address in the headline and the first sentence, kept: the headline cuts it | Umbraco's small sidebar | an uncertain retry asks first; a limited mail names its limit and offers none; who may retry when the reader may not |
| Activity › Audit log | — | the search, two filters and Export, a row opens it | no | where gives way, and who below 520 px | reading, none yet, nothing matches, failed with Try again |
| An action | Close | Open the form's History, Show only that person's actions | no | Umbraco's small sidebar | a grant's permissions before and after, the settings a save changed by name |
| Replace… | Replace the draft | Cancel, Choose another file | no: what the file holds once, what the site lacks once | Umbraco's small sidebar | a file that is no form, a changed draft, unsaved changes to be lost, actions it may not change |

## The setup checklist end to end

`tests/e2e/specs/backoffice/setup-checklist.spec.ts` starts the demo fresh on its own port, 5537, as a new site has
it: `Demo:Seed` false skips the demo's forms and content, the test team stays out, and Umbraco's sender carries a
display name, which Umbraco accepts and forms' mail cannot use as its own sender. All five items of the Overview's
checklist start open. The administrator follows each from its own button and sees it turn green:

1. **The sender:** Open email settings, the address in Settings › Email, Save.
2. **A form:** Choose a template…, the gallery's first template, Use template.
3. **A test entry:** Open the form, Preview, a filled form sent from the preview.
4. **The form block:** Create a form block type…, Create; the line that one step is left, Close.
5. **Access:** Open access settings, View entries for the editors, Save; then the editors' link under the matrix, and
   the Form Builder section added in Umbraco's own user group editor, Save.

Once all five are done the checklist stays away from the Overview, and shown again from its "…" it reads "5 of 5
done" with five green marks. The run passed in 2.9 minutes, and runs again in the phase gate's browser stage.

## The wiring sweep

A reader went through the phase's wiring (`b74cfedb..6aae2234`), each name checked against product code and tests.
What it found is fixed in `605be3c0`:

- a theme read, then created or changed, could stay stale in the same request, as the library only told the servers;
  it now forgets the theme on this server at once, then tells every server, as the option lists do (ADR-0073);
- the file mirror's problem keys held the kind as stored, and "notPublished" has a capital letter, which a key may not:
  a changed file that publishing refused threw instead of being named; keys now take the kind in lowercase;
- tests now hold the "Forms as files" check with its action through Umbraco's collection, a file that cannot be
  written, a change that publishing refuses until it may, and the mirror's job with its gates and a failing pass;
- `theme.deleted` and `emailLayout.deleted`, `duplicateGroup` and `administrators` are asserted;
- five activity words nothing used are gone, and a test holds the activity and access areas to the keys their code
  uses;
- ADR-0219 says the form filter of Deliveries and the audit log is the API's alone for now.

## What the last walk found

The walk ran twice: the first (`12.6-1`) on Phase 12's screens and the seven it changed in all twelve looks, 319 of
324 shots; the last (`12.6-2`) after the fixes, all 324, with the contact sheets. Inside the section's own elements:

| Measure | First | Last | What remains, and whose |
| --- | --- | --- | --- |
| axe findings of ours | 0 | 0 | — |
| axe colour contrast | 12 shots | 12 | Umbraco's open tree row in the dark theme (ADR-0182, OWNER-TODO) |
| boxes that scroll inside the section | 16 | 12 | the access matrix, kept (ADR-0218) |
| the page's sideways scroll beyond Umbraco's | 0 | 0 | Umbraco's own 545 and 152 px at 375 and 768 px (ADR-0182) |
| words cut by an ellipsis | 19 | 20 | sidebars' one-line headlines on long names and addresses, kept |
| Deliveries' columns squeezed at 620 px | every look at 375 and 768 px | 0 | — |
| shots that could not be taken | 5 | 0 | — |

The one more cut headline is Replace… at 375 px in the look whose shot the first walk lost. Umbraco's tree rows' names
(every shot) stay Umbraco's (ADR-0182, OWNER-TODO).
