# Review of the backoffice wireframes (step 2.2)

This review checks every wireframe in [wireframes/](wireframes/) against the measurable rules of ROADMAP §4.13 and
the rules of [information-architecture.md](information-architecture.md), then records what each of the three design
skills (ADR-0019, ADR-0020) said and what changed because of it. The pages, their states and how to render them are
in the [wireframes README](wireframes/README.md).

## How the screens were checked

**By script.** `scripts/wireframes.mjs` renders every shot in Edge and measures it. Its tests are in
`scripts/tests/wireframes.test.mjs`.

| Run | Shots | Findings |
| --- | --- | --- |
| `node scripts/wireframes.mjs --check`: every declared shot, at 1440 px, 920 px (a 620 px workspace) and, for the designer, 1024 px | 137 shots of 25 pages | 0 |
| `node scripts/wireframes.mjs --widths=920,1024,1920`: every state of every page at a 620 px workspace and at the two other backoffice widths of §4.13, measured without writing PNGs | 339 shots | 0 |
| `impeccable detect --json docs/ux/wireframes/` (the skill's bundled engine) | 25 pages | see the `impeccable` section |

For each shot the script checks:

- **one primary per layer:** at most one primary or destructive button in the workspace, in each sidebar, in each
  dialog and in each part marked as a layer of its own;
- **three secondary actions at most:** in every toolbar, header, box head, card, panel head, bulk bar and action bar,
  where "…", a modal's close button and a setting in a header (the site switch) don't count, and a segmented switch
  counts once;
- **nothing squashed:** no label of a button, chip, tab, step, tree item, menu item or header item cut off or
  wrapped, no sideways scrolling, and no content running past its box, header row, bar, card, panel or dialog
  (popovers float over the page and are left out);
- **the tokens:** every colour, radius, font size and shade is one of the tokens in `assets/kit.css`, every padding
  and gap sits on the 3 px grid, and no text is set in capitals (no eyebrow labels);
- **no em or en dash** in visible text;
- **every icon** exists in the sprite;
- **axe-core** finds no WCAG 2.2 A or AA violation, target size included.

**By eye.** Every PNG was looked at after each batch. The slips found that way, all fixed before this review, are in
"Findings and changes" below; several of them became new checks in the script, so they can't come back.

**By a second pair of eyes.** The `impeccable-finish-reviewer` subagent read the PNGs against PRODUCT.md, §4.13, the
IA and copy.md, without my conclusions. Its findings and what happened to each are in the `impeccable` section; the
kind it found that the script had missed (header items and tables running past their box) became the overflow check
above. With the sweep widened to 920 px for every state, that check found seven more pages whose lists or answers ran
past their box at 920 or 1024 px, and the label check found the designer's other states squashed at 920 px; all are
fixed.

**The personas.** Each page shows the persona whose work it is (ADR-0013): Lea Hoffmann (designer) on the designer,
settings, actions, history and library pages; Jonas Berger (entry handler) on Entries, the entry panel, the Inbox and
Insights; Ana Novak (administrator) on the overview, the folder list, Spam, Settings, Access and Activity; Mira
Sandberg (privacy officer) on People and Retention; Sam Weller (no access) on the overview's no-access state. The
sidebar, the top bar and the views follow the persona, so a page also checks IA section 11 ("Who sees what").

**Not covered here, and where it is:** dark mode comes from UUI's own tokens and is checked when Phase 9 builds the
screens; German texts are in copy.md and are checked on the built screens; the visitor's side is step 2.3.

## The checklist

Every screen below is checked against these lines. A line that doesn't apply to a screen says so.

| Line | Rule | Source |
| --- | --- | --- |
| **Primary** | one primary action per layer, where Umbraco puts it (workspace footer, a sidebar's or dialog's action bar); a view adds no second primary | §4.13, IA rules |
| **Secondary** | at most three visible secondary actions in any toolbar, header, card or bar; the rest in "…" | §4.13 |
| **In context** | contextual actions on the selected element, on hover and on focus; no permanent rows of icons | §4.13 |
| **Disclosure** | at most seven settings before "More options"; closed groups summarise what is set | §4.13 |
| **Said twice?** | no fact twice on a screen: place, name, count, state, list heading, panel content, marks | §4.13 "Say it once" |
| **620 px** | the screen works in a 620 px workspace: text keeps its width, controls wrap to their own row, close buttons sit in a corner | §4.13, IA rules |
| **Tokens** | the token rules above, plus two radii, one shade, dots only for real states, empty values as "none" | §4.13 "One visual system" |
| **States** | empty, loading and error states where they differ, and success feedback | §4.13, step 2.2 |
| **Access** | axe clean; every icon-only button named; destructive dialogs focus Cancel | WCAG 2.2 AA |

"Script" in a line below means the line's measurable part is the script's, and it passed for every shot of that
screen at every width.

## Screens

### Overview (the start page)

`overview.html`: first run, warnings, loading, error, no access, 620 px.

- [x] **Primary:** none; it is a screen for reading (IA 2). The checklist's item 4 holds its own button.
- [x] **Secondary:** the overview's "…" only, at the end of the health row and inside it, so every box keeps one
  right edge (**changed** after the finish review: it floated beside the box and narrowed it). Script.
- [x] **In context:** the checklist links are the items themselves.
- [x] **Disclosure:** the checklist disappears once complete; "…" brings it back.
- [x] **Said twice?** No. The section's name is in the top bar and the sidebar header only, with no title on the page
  (ADR-0021). Health leaves out the email warning while the checklist's first item covers it. A done item says
  "Done" once, with no date or person.
- [x] **620 px:** the tables keep their columns; the checklist's button wraps under its text. Script.
- [x] **Tokens:** script.
- [x] **States:** all five drawn; the error keeps the rest of the page usable.
- [x] **Access:** script.

### Form library (a folder, the create dialog, the Recycle Bin)

`form-library.html`: create, empty folder, loading, error, Recycle Bin, emptying it, 620 px.

- [x] **Primary:** Create form in a folder's footer; Empty recycle bin… in the Recycle Bin's footer, in the
  destructive style because it deletes for good. The confirm dialog has its own one primary.
- [x] **Secondary:** the header's "…" only; each trashed form's "…" holds Restore… and Delete…. Script.
- [x] **In context:** row actions live in the rows' "…".
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. The folder list shows entries and changes, not the forms' states, which the tree's marks
  already show (IA 3). The Recycle Bin has no "…" in its header, because its only action is the footer's.
- [x] **620 px:** script.
- [x] **Tokens:** script.
- [x] **States:** empty folder ("No forms here yet. Start from a template; it takes a minute."), loading, error with
  Try again.
- [x] **Access:** the confirm dialog focuses Cancel; script.

### Template gallery

`template-gallery.html`: loading, Library › Templates, 620 px.

- [x] **Primary:** Use template, in the sidebar's action bar; disabled while loading.
- [x] **Secondary:** Cancel only. Script.
- [x] **In context:** a card opens its preview beside the gallery.
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. The card says what the template is for; the preview adds what the card can't (pages,
  fields, what it does after sending). **Changed** after the finish review: the cards had a picture of the form,
  drawn in the same grey bars as a skeleton, which made the loaded gallery look as if it were loading and repeated
  the preview; the cards now carry the name and one line. Library › Templates has no create button, because
  templates come from "Save as template" (IA 7).
- [x] **620 px:** the preview covers the gallery, as the designer's inspector does. Script.
- [x] **Tokens:** script.
- [x] **States:** loading as two text-line skeletons per card. An empty gallery can't happen: the built-in templates
  always show.
- [x] **Access:** cards are buttons; the selected one has `aria-current`. Script.

### Designer

`designer.html`: inspector closed, phone width, insert point, new form, loading, 1024 px, 620 px.

- [x] **Primary:** Publish… in the workspace footer, on every view.
- [x] **Secondary:** footer Preview and Save draft; toolbar: the width switch (one control), the form check badge
  and "…"; the selected element: Move to…, Duplicate, Delete and "…". Script.
- [x] **In context:** the element bar appears on the selected element only; insert points on hover and focus; at
  620 px the element's actions move into the inspector's header (IA 5.1).
- [x] **Disclosure:** the inspector opens Essentials and folds Validation, Rules, Appearance and Advanced, each
  with a one-line summary; Rules' summary carries "Add a rule" itself.
- [x] **Said twice?** No. The form's name is in the header and the tree; the state is the chip, not a sentence; the
  selected element's rules sit in the inspector, not as a chip on it; the inspector names the element's type
  ("Email field") rather than repeating its label.
- [x] **620 px:** the inspector covers the canvas in every state with a selection; the views fold into "…", then
  "Used on 3 pages" into the form's "…". **Changed in this step:** at 1024 px both side panels left the canvas about
  150 px wide, so the palette now folds to a rail while an element is selected (IA 5.1 updated). Even then the
  canvas is about 300 px at a 1024 px window, narrower than the phone setting, and the finish review found First
  name and Last name squeezed side by side; the canvas now shows the form as it reflows in that room (columns stack,
  as in a visitor's narrow window) and says "Narrowed to fit" beside the width switch. Scaling the form down instead
  was rejected: at 40 % its text would be unreadable. Script at 920, 1024 and 1920.
- [x] **Tokens:** script.
- [x] **States:** new form (one sentence and "Add field"), loading skeleton. Errors in a form are the form check's.
- [x] **Access:** palette items are buttons, the width switch uses `aria-pressed`, the canvas groups are headed.
  Script.

### Rules builder and rules overview

`rules-builder.html`: new rule, two groups, overview.

- [x] **Primary:** Update, or Add for a new rule; the overview has none (Close).
- [x] **Secondary:** Cancel; inside the builder, Add condition and Add group sit with the group they extend. Script.
- [x] **In context:** a condition's remove button sits on its row.
- [x] **Disclosure:** groups appear only when the editor adds a second one.
- [x] **Said twice?** No. The first line names the target once ("Show this field: Which country? when"); rows don't
  repeat it. The overview groups rules by page and doesn't repeat the page on each row.
- [x] **620 px:** a medium sidebar over the canvas; script.
- [x] **Tokens:** script.
- [x] **States:** a new rule starts with one empty condition whose value waits for its field.
- [x] **Access:** remove buttons are named per condition and group. Script.

### Form check

`form-check.html`: no problems.

- [x] **Primary:** none (Close). Fix it is a secondary per problem.
- [x] **Secondary:** one Fix it per row. Script.
- [x] **In context:** Fix it selects the element and opens the setting (IA 5.1).
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. The badge says the count; the sidebar's headings split it into errors and warnings without
  repeating the total, and only the errors' heading says that they block publishing.
- [x] **620 px:** script.
- [x] **Tokens:** script.
- [x] **States:** no problems, with one sentence.
- [x] **Access:** script.

### Form settings and an outcome

`form-settings.html`: outcome editor, 620 px.

- [x] **Primary:** Publish… in the footer; the outcome sidebar's Update.
- [x] **Secondary:** footer Preview and Save draft; "Add outcome" under the outcomes. Script.
- [x] **In context:** outcomes reorder by their grip and open on click.
- [x] **Disclosure:** Submission shows seven settings, then "More options"; the closed groups summarise themselves
  ("Always open · No limit").
- [x] **Said twice?** No. The button labels' small captions say where each button appears, not its text again; help
  texts answer what the label leaves open (copy.md).
- [x] **620 px:** the three label inputs wrap to rows. **Fixed** after the finish review: "Used on 3 pages" and the
  header's "…" ran under the folded view tab; header links now fold into "…" when folding the views isn't enough
  (`kit.js`, for every form view), and the script checks for it. Script.
- [x] **Tokens:** script.
- [x] **States:** settings load with the workspace, so they share the designer's loading state.
- [x] **Access:** group headers are buttons with `aria-expanded`; switches and radios are labelled. Script.

### Actions

`actions.html`: added, empty, read-only, loading, 620 px.

- [x] **Primary:** Publish… in the footer. Add action is the view's main action: the first toolbar button, in the
  secondary style.
- [x] **Secondary:** per card the switch and Test, then "…". Script.
- [x] **In context:** a card opens its editor; reordering by the grip and by keyboard (ADR-0016).
- [x] **Disclosure:** groups whose setting is off show one line with a link to the setting, and no cards.
- [x] **Said twice?** No. Each trigger's heading carries its meaning once, and cards don't repeat it. The toast after
  adding says only what the screen can't ("Visitors get it once you publish"); the card shows no "not published"
  line beside it. **Changed** after the finish review: the failed card's line read as past tense and disagreed with
  Deliveries; it now says "Failed at 14:02. Next try at 14:30.", and Approved's heading reads "as each entry comes
  in, because this form has no manual approval", like Submitted's "when an entry comes in".
- [x] **620 px:** script.
- [x] **Tokens:** script; the run states use dots for real states only.
- [x] **States:** empty ("No actions yet. Most forms email the team…"), read-only with the one line naming the
  permission, loading, success toast.
- [x] **Access:** each switch is named for its action. Script.

### Email editor (the action sidebar)

`email-editor.html`: merge-tag picker, tested, 620 px.

- [x] **Primary:** Add (Update when editing).
- [x] **Secondary:** Test (at the start of the action bar) and Cancel. Script.
- [x] **In context:** the merge-tag picker opens from the field it fills.
- [x] **Disclosure:** CC, BCC, attachments and "stop the rest if this fails" sit under "More options".
- [x] **Said twice?** No. The preview shows the rendered result, not the settings again.
- [x] **620 px:** the preview moves behind a link at the end of the settings. Script.
- [x] **Tokens:** script; merge tags use the mono token.
- [x] **States:** the test toast names who got the test.
- [x] **Access:** the merge-tag button is named for its field. Script.

### Preview

`preview.html`: phone, menu, sent, 620 px.

- [x] **Primary:** none; sending the form is the action (IA 5.7).
- [x] **Secondary:** the width switch and Fill with sample answers, then "…"; the close button is the frame's. Script.
- [x] **In context:** not applicable.
- [x] **Disclosure:** light or dark, language and running actions sit in "…".
- [x] **Said twice?** No. One line says entries here are test entries; after sending, the status line gives the
  entry's number and the outcome shows as visitors see it.
- [x] **620 px:** script.
- [x] **Tokens:** script.
- [x] **States:** sent.
- [x] **Access:** script.

### Publish dialog

`publish-dialog.html`: errors, published, 620 px.

- [x] **Primary:** Publish; disabled while errors block it.
- [x] **Secondary:** Cancel; Fix it per error. Script.
- [x] **In context:** not applicable.
- [x] **Disclosure:** the changes are grouped (fields, settings, actions, rules), and only groups with changes
  show. **Changed** after the finish review: the list said "since the live version", a word the glossary rules out,
  and listed changes that History already showed in version 14; it now reads "since the published version" and lists
  what the current draft changes (the label of Email address, Post in #leads, and in the errors state the removed
  option that breaks the rule), the same changes History's version sidebar shows.
- [x] **Said twice?** The published toast repeats "Published", which the header's chip also shows. Kept on purpose:
  the toast is announced to screen readers, the chip change isn't, and it adds what the chip can't ("Visitors get
  the new version now").
- [x] **620 px:** script.
- [x] **Tokens:** script.
- [x] **States:** errors with Fix it; the success toast.
- [x] **Access:** the disabled Publish is `aria-disabled` and its reason is the alert above it. Script.

### History and comparison

`history.html`: draft saves, compare, comparison, one version, loading, 620 px.

- [x] **Primary:** Publish… in the footer; the version sidebar's Restore as a new draft.
- [x] **Secondary:** toolbar Compare (once two versions are ticked) and the draft-saves switch; the version
  sidebar's Cancel and Restore and publish…. Script.
- [x] **In context:** a row's "…" holds Pin and Add comment….
- [x] **Disclosure:** draft saves only with the switch.
- [x] **Said twice?** **Fixed:** the "Changed" column's cells began with "Changed:" again; they now hold only the
  changes. The comparison's columns name the two versions once. **Added** after the finish review: nothing said
  which version visitors get, so version 14 carries a "Published" chip. It doesn't repeat the header's "Published
  (pending changes)": the header says the form has pending changes, the chip says which version they are pending
  against. The pin is named "Pinned" for screen readers and as a tooltip.
- [x] **620 px:** script.
- [x] **Tokens:** script; empty comments read "none".
- [x] **States:** loading; the list can't be empty for a published form, and a draft has no History yet.
- [x] **Access:** row checkboxes are named per version. Script.

### Entries (a form's view) and Export

`entries.html`: filtered, bulk, export, empty, no results, loading, error, 620 px.

- [x] **Primary:** none in the view; the entry handler has no footer, because its buttons design the form. Export's
  sidebar has Export 214 entries.
- [x] **Secondary:** toolbar search, Filter and Export…, then "…"; bulk bar Approve, Reject and Mark as spam, then
  "…". Script.
- [x] **In context:** the bulk bar appears once a row is selected.
- [x] **Disclosure:** active filters get a row of their own with Save as… and Clear filters.
- [x] **Said twice?** No. No form column on a form's own view; unseen rows show a dot and bold once; the count of
  entries is the pager's.
- [x] **620 px:** the Actions and field columns give way to the essentials below a 900 px workspace. **Fixed** after
  the overflow check: the columns dropped by the window's width, so at a 1024 px window the table ran 68 px past its
  box; lists now drop columns by the workspace's own width (a container query), here and in the Inbox, Spam,
  Deliveries and the Audit log. Script.
- [x] **Tokens:** script.
- [x] **States:** all drawn, worded as copy.md says. **Changed:** "Previous" no longer shows disabled on the first
  page (IA: actions are offered only when they can work).
- [x] **Access:** select boxes are named per entry; the bulk bar is a named region. Script.

### Entry panel and Forward

`entry-panel.html`: timeline, notes, spam, forward, 620 px.

- [x] **Primary:** Approve while the entry waits; Not spam in Spam; Forward in its sidebar.
- [x] **Secondary:** Reject, Mark as spam and Forward…, then "…" (Print for the entry handler); Delete… in Spam.
  Script.
- [x] **In context:** the panel opens beside the list without losing its place.
- [x] **Disclosure:** "More details" holds the member, the IP address and the spam reasons; in Spam the reasons come
  first.
- [x] **Said twice?** No. The list narrows to name and time; the panel's header adds the form and the number, not
  the name, until it covers the list at 620 px, where it adds the name. Spam's header has no state chip. **Fixed**
  after the finish review: the spam sample showed the next row's title as entry 1051's Name (it is untitled, so the
  Name reads "none"), and its list left out a row the Spam list has.
- [x] **620 px:** the panel covers the list. **Fixed:** the name didn't appear there at first. **Fixed** after the
  overflow check: at 1024 px a long email address ran past the answers box; labels take a third, values two thirds
  and break anywhere. Script.
- [x] **Tokens:** script.
- [x] **States:** sensitive values masked without Reveal for the entry handler, with Reveal for the administrator.
- [x] **Access:** tabs use `role="tab"` and `aria-selected`. Script.

### Inbox

`inbox.html`: filter menu, column chooser, sensitive search, empty, loading, 620 px.

- [x] **Primary:** none; the column chooser's Update.
- [x] **Secondary:** search, Filter and Export…, then "…". Script.
- [x] **In context:** the column chooser sits at the end of the header row.
- [x] **Disclosure:** the field columns appear only when the Inbox is filtered to one form, as the chooser says.
- [x] **Said twice?** No. The unseen count is the sidebar badge's only; the heading has no number.
- [x] **620 px:** **fixed** after the finish review: the table ran past its box, and on the first row the unseen dot
  wrapped onto a line of its own. A name and its dot now stay together and a long name wraps after them (in every
  list, `kit.css`), and the State and Actions columns give way below a 900 px workspace. Script.
- [x] **Tokens:** script.
- [x] **States:** empty (a new line, added to copy.md), a search that finds nothing with the sensitive-fields note,
  loading.
- [x] **Access:** script.

### Spam

`spam.html`: bulk, empty, emptying it.

- [x] **Primary:** none in the list; the confirm dialog's Delete 4 entries.
- [x] **Secondary:** search, Filter and Empty spam…; bulk bar Not spam and Delete…. Script.
- [x] **In context:** the bulk bar appears once a row is selected.
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. No state column, because everything here is in Spam; the sidebar's Spam has no badge.
- [x] **620 px:** **added** after the overflow check found the table 86 px past its box at 920 px: the reasons wrap,
  and the Form column gives way below a 900 px workspace, because in Spam the reason matters more than the form. A
  920 px shot was added. Script.
- [x] **Tokens:** script.
- [x] **States:** empty, worded as copy.md says.
- [x] **Access:** the confirm dialog focuses Cancel. Script.

### Insights

`insights.html`: answers, table view, empty, off, loading, 620 px.

- [x] **Primary:** none; it is for reading.
- [x] **Secondary:** the date range, the comparison switch and the Overview · Answers switch; per chart, Show as
  table. Script.
- [x] **In context:** not applicable.
- [x] **Disclosure:** Group by sits in the date range's popover.
- [x] **Said twice?** No. Each number's change is said once, in words and numbers ("up 6% from 4,540"); the chart's
  legend names the two periods once.
- [x] **620 px:** the stat strip and the charts wrap to rows. **Fixed** after the overflow check: at 1024 px "212
  errors, mostly the format" ran past its box; a bar list's value now wraps when the bar has given all its room.
  Script.
- [x] **Tokens:** script; changes are words, not red and green.
- [x] **States:** empty and off, worded as copy.md says; loading skeleton.
- [x] **Access:** every chart has a table view and a text alternative. Script.

### Option list

`option-list.html`: the list, choosing a kind, empty, 620 px.

- [x] **Primary:** Save in an option list's footer; Create option list in the list's footer.
- [x] **Secondary:** the header's "Used by 2 forms" link and "…"; Add option and Upload… under the options. Script.
- [x] **In context:** each option's remove button sits on its row.
- [x] **Disclosure:** the cache setting appears only for kinds that fetch (not drawn for fixed options).
- [x] **Said twice?** No. The kind is said once, in the options box's head. **Changed** after the finish review: the
  preview showed a dropdown and radio buttons under one label, so it read as two fields; the two renderings are now
  captioned "As a dropdown" and "As radio buttons", without a field label, because a list belongs to no one field. The
  preview's top now lines up with the options box.
- [x] **620 px:** the preview moves under the options. Script.
- [x] **Tokens:** script; the preview is a sample.
- [x] **States:** empty list with one sentence.
- [x] **Access:** script.

### Theme editor

`theme-editor.html`: dark, the list, 620 px.

- [x] **Primary:** Save; Create theme in the list.
- [x] **Secondary:** "Used by 5 forms" and "…". Script.
- [x] **In context:** a failing contrast check sits under its colour and as a flag on the preview where it shows.
- [x] **Disclosure:** type, spacing and corners, buttons and focus, and custom CSS are closed with summaries.
- [x] **Said twice?** No. The default theme's "Default" mark in the list links to Settings › General, the one
  place that sets it (IA 7).
- [x] **620 px:** the preview moves behind the header (noted on the page). Script.
- [x] **Tokens:** script; the theme's own colours are a sample.
- [x] **States:** a failing contrast check, drawn on the control-border colour so the sample itself stays
  accessible.
- [x] **Access:** script.

### Email layout

`email-layout.html`: dark, the list, 620 px.

- [x] **Primary:** Save; Create email layout in the list.
- [x] **Secondary:** "Used by 3 actions" and "…" in the header, as on every library item; the light and dark
  switch and Send test email with the preview. Script.
- [x] **In context:** **changed** after the finish review, which found this header laid out unlike the others: Send
  test email moved from the header's far end to the preview, because it sends what the preview shows (IA 7 updated).
- [x] **Disclosure:** header and footer texts per language behind the language select.
- [x] **Said twice?** No.
- [x] **620 px:** the preview moves behind the header. Script.
- [x] **Tokens:** script; the email is a sample, set in an email-safe font.
- [x] **States:** light and dark previews.
- [x] **Access:** script.

### Settings › General and Email, Privacy › Data collection

`global-settings.html`: one site chosen, Email, a locked setting, 620 px.

- [x] **Primary:** Save.
- [x] **Secondary:** the site switch (a setting, not an action) and, on Email, Send test email. Script.
- [x] **In context:** "Use the value for all sites" sits under the field that has its own value.
- [x] **Disclosure:** at most seven settings per group, then "More options".
- [x] **Said twice?** No. The site's own value is said once, under its field.
- [x] **620 px:** script.
- [x] **Tokens:** script; the locking key uses the mono token.
- [x] **States:** a setting locked by configuration names its key (ADR-0008).
- [x] **Access:** the locked radios are `aria-disabled`. Script.

### Permissions (Settings › Access, Permissions… on a folder)

`permissions.html`: a folder's permissions, restricting a folder, 620 px.

- [x] **Primary:** Save, on the screen and in the sidebar; the confirm dialog's Restrict folder.
- [x] **Secondary:** Add folder or form…; in the sidebar Add user group. Script.
- [x] **In context:** a group's remove button sits on its column.
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. A group that can't open Forms yet is named once, in a line above the matrix.
- [x] **620 px:** **fixed** after the finish review, which found the last column cut off at the box's edge: the note
  that squeezed the Translators column moved above the matrix, the group columns are narrower and permissions wrap,
  so five groups fit a 620 px workspace. A site can have any number of groups, so beyond that the matrix scrolls
  sideways inside its box with the permission column fixed (a named, focusable region); IA 8 says so. Script.
- [x] **Tokens:** script.
- [x] **States:** a folder without its own groups; restricting it asks first (copy.md).
- [x] **Access:** each checkbox is named "group: permission". Script.

### Deliveries

`deliveries.html`: a run's details, an uncertain retry, paused, empty, loading, 620 px.

- [x] **Primary:** none in the list; Retry now (failed) or Retry… (uncertain) in the details; the confirm's Retry
  anyway.
- [x] **Secondary:** search and Filter; Close in the details. Script.
- [x] **In context:** a row opens its details.
- [x] **Disclosure:** the tries are listed in the details only.
- [x] **Said twice?** No. The other server's answer is quoted once, marked as its words.
- [x] **620 px:** the What column gives way below a 900 px workspace (**fixed:** at 1024 px the table ran 51 px past
  its box). Script.
- [x] **Tokens:** script; dots only for delivery states.
- [x] **States:** paused (one line naming the host setting), empty, loading.
- [x] **Access:** script.

### Audit log

`audit-log.html`: an event's details, no results, loading, 620 px.

- [x] **Primary:** none; it is for reading.
- [x] **Secondary:** search, Filter and Export. Script.
- [x] **In context:** a row opens its details with before and after.
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. **Fixed** after the finish review: the sample said version 14 of Contact us was published
  today, while History says 3 Oct; today's event is now another form's. "Privacy › Data collection" was flagged too,
  but it is right: Data collection sits in the Privacy group (IA 1).
- [x] **620 px:** the Where column gives way below a 900 px workspace. Script.
- [x] **Tokens:** script.
- [x] **States:** no results with Clear filters; loading.
- [x] **Access:** script.

### Privacy tools: People and Retention

`privacy-tools.html`: results, erase preview, nothing found, Retention, 620 px.

- [x] **Primary:** Find, beside the field it acts on, as Umbraco's own search dashboards place it; the erase
  preview's Erase 6 entries.
- [x] **Secondary:** after a search, Export everything and Erase…. Script.
- [x] **In context:** the result actions appear only after a search.
- [x] **Disclosure:** not applicable.
- [x] **Said twice?** No. The result count is the results box's heading only.
- [x] **620 px:** entry names wrap (**fixed:** the Emails column ran past the results box at 920 px). Script.
- [x] **Tokens:** script; empty counts read "none".
- [x] **States:** nothing found; Retention read-only for the privacy officer, with the line naming the permission.
- [x] **Access:** the erase preview focuses Cancel. Script.

## Findings and changes

What the checks and the skills found while the wireframes were drawn, and what changed.

| Finding | Found by | Change |
| --- | --- | --- |
| The designer's palette and inspector left the canvas about 150 px at a 1024 px window | the 1024 px sweep | the palette folds to a rail while an element is selected below a 1024 px workspace; IA 5.1 updated; a 1024 px shot added |
| Workspace headers overflowed under their view tabs at 1024 px (a WCAG 2.5.8 target-size failure) | the 1024 px sweep, axe | `kit.js` folds view tabs into "…" when a header runs out of room, as Umbraco does; IA 5.1 says so |
| "Used on 3 pages" wrapped to two lines in a narrow header | eye | header items never wrap; the script now measures header links |
| A form's name wrapped at 620 px | eye | header names never wrap; the script now measures names, steps, tree and menu items |
| Steps in the designer wrapped inside their pills | the 1024 px sweep | steps wrap as whole pills |
| History's "Changed" cells repeated "Changed:" | eye ("said twice?") | removed |
| The entry panel at 620 px didn't name the entry | eye | the heading adds the name there |
| Insights' stat boxes stood as four equal cards, offset by a box margin | eye, `impeccable` craft floor | one stat strip |
| A ring outline was counted as a shade | the script | the script now flags only blurred shadows |
| The theme editor's failing contrast sample was itself inaccessible | axe | the failing check is drawn on the control-border colour |
| Disabled "Previous" on the first page (1.6:1) | `impeccable detect` | not shown on the first page |
| The designer persona saw Entries and Insights views | review against IA 11 | removed for that persona |
| Underlined create-dialog options | eye | an option component in the kit |
| A palette with no focusable items; `aria-pressed` on spans | axe | palette items and segments are buttons |
| Header items ran under the folded view tab at 920 px on Settings, Actions and History | finish review | header links fold into "…" after the views; the script now checks content against its box |
| Tables and answers ran past their box at 920 or 1024 px (Entries, Inbox, Spam, Deliveries, the entry panel, Insights, People) | the new overflow check | lists drop columns by the workspace's width, long values wrap |
| The designer's other states squashed the canvas at 920 px | the 920 px sweep | every state with a selection gets the 620 px layout |
| The permission matrix cut its last column | finish review | the note moved above it, five groups fit, more scroll inside the box |
| The unseen dot wrapped away from its name | finish review | name and dot stay together in every list |
| Nothing said which version visitors get | finish review | a "Published" chip on that version in History |
| The desktop canvas was narrower than the phone one at 1024 px | finish review | the form reflows in the room it has, with "Narrowed to fit" |
| Template cards looked like skeletons and repeated the preview | finish review | name and one line only |
| Header actions sat in different places; the overview's "…" narrowed the health box | finish review | "…" right after the name or link everywhere; Send test email with the email preview; the overview's "…" inside the health row |
| Copy: past tense on a failed card, a broken sentence on Approved, "live" in the publish dialog | finish review | reworded; the new strings are in copy.md |
| Sample data that contradicted itself (a spam entry's name, the pending changes, an audit date) | finish review | made consistent across pages |
| Two renderings in the option list preview read as two fields | finish review | captioned, and aligned with the options box |
| Names were underlined in some lists and plain in others | finish review | names in every list are links, underlined on hover and focus, as in Umbraco's collections |

## `design-taste-frontend`

**The design read:** "Reading this as: the backoffice section of an Umbraco package, for editors, entry handlers,
administrators and privacy officers at work, with a calm and native language, leaning toward Umbraco's UUI
components and its own patterns." It is the read of step 2.1, unchanged.

**Scope.** The skill names dashboards, admin panels, data tables and multi-step forms as outside its scope, and
says to point to the right tool and apply only the parts that fit. The right tool here is Umbraco's own UUI, the
official design system of the platform, which the skill's honesty rule asks for in any case ("use the official
package"). So the wireframes copy no other system and only borrow UUI's tokens and patterns, under UUI's names.

**The dials:** variance 2, motion 1, density 5, as in 2.1: symmetric Umbraco layouts, no added motion, and lists as
dense as the work needs. The skill's presets are for landing pages, so the dials are reasoned from the brief.

**The pre-flight check,** for the boxes that apply to a backoffice:

| Box | Result |
| --- | --- |
| Brief inference declared | yes, above |
| Dials explicit and reasoned | yes, above |
| Design system chosen honestly | UUI, the platform's own; no hand-made system pretending to be one |
| Zero em and en dashes | the script checks every shot; none |
| One theme | light only; dark is UUI's job |
| Colour consistency | one accent: UUI's selected violet for selection and charts; green only for primaries and positive states |
| Shape consistency | two radii, 3 px and round; the script checks every element |
| Button contrast | axe on every shot |
| CTA wrap | the script checks every button, chip, tab and step at four widths |
| Form contrast | inputs use the darker control border (3.3:1), focus rings UUI's focus colour; axe clean |
| Eyebrow count | zero; the script refuses text in capitals |
| No duplicate CTA intent | one label per intent per screen (for example "Add action" once, "Export…" once) |
| Copy self-audit | every string follows copy.md; new strings were added to copy.md |
| Motion motivated | no motion added (see `emil-design-eng`) |
| Empty, loading and error states | per screen above |
| Cards only where they carry hierarchy | action cards and template cards only; stat cards replaced by one strip |
| Icons from one library | Umbraco's own backoffice icons (drawn from Lucide), because the section must look native; the skill's library list is for projects without a platform icon set |
| No decorative dots | dots mark unseen entries and delivery or run states only |
| No border on every row of long lists | table rows use one divider token, as UUI's tables do |
| Long lists use the right component | Umbraco's collection pattern: table, pager, bulk bar |

The boxes about heroes, marquees, logo walls, bento grids, zigzags, scroll cues and GSAP don't apply to a
backoffice.

**Findings:** the stat cards on Insights (now one strip), and no others beyond what the script already enforces.

## `impeccable`

**Setup.** `impeccable context` ran with the skill's bundled engine (no download, no hooks, no live mode). It loaded
PRODUCT.md, found no DESIGN.md, no surface brief and no visual implementation, and reported the platform as web.
Its directives and what happened to each:

- **Interview and decision steps:** the skill asks for one question to the person first. The owner's binding
  instruction is to decide without asking (CLAUDE.md rule 8, ADR-0019, ADR-0020), so the brief stands in for the
  interview: ROADMAP §1, §2 and §4.13, the IA and copy.md of step 2.1.
- **World discovery:** the visual world is established by the platform, Umbraco 17's backoffice, so new-work's world
  discovery resolves to "preserve the incumbent world" and there is no concept tournament or direction contract.
  DESIGN.md is step 2.3's.
- **The detector:** run once on the finished pages, as the skill directs.

**Mode:** Operate. The visitor's success is a finished task; familiarity is the feature, and brand lives in precise
details (operate.md).

**The craft floor,** applied: contrast through axe; one shade with an offset and blur; spacing on the 3 px grid;
tabular numerals in every number column; themed selection, focus ring and underline offset; empty, loading and
error states; copy in the product's own words. Refused: eyebrow labels (none, enforced), the hero-metric template
(Insights' four stat cards became one strip), colored side borders on callouts (none), monospace as a costume
(only for merge tags, aliases, keys and quoted server answers).

**The detector,** `impeccable detect --json docs/ux/wireframes/`:

| Rule | Hits | Verdict |
| --- | --- | --- |
| cramped-padding | 76 | not real: every hit is a fixed-height control whose content is centred by flexbox (inputs, selects, segmented switches, the bulk bar, the header name), or a box whose children carry their own padding (box heads, bodies, table cells). The PNGs show 9 to 12 px around every text. The detector reads the CSS statically and can't see the centring. |
| low-contrast | 4 | disabled controls in UUI's disabled colours (1.6:1). WCAG 1.4.3 exempts inactive controls; still, "Previous" on the first page is no longer shown at all. The two that stay are Publish while errors block it (the alert above says why) and Use template while the gallery loads. |
| overused-font | 2 | Lato on the theme editor is the backoffice's own font (the contrast flag drawn over the preview is the backoffice's, not the theme's), and the native look requires it; Arial on the email layout is the sample email's email-safe font, because mail apps don't load web fonts. |

**The finish review** (the `impeccable-finish-reviewer` subagent, briefed with PRODUCT.md, §4.13, the IA, copy.md
and this review's recorded decisions, and asked to look past what the script measures). Its verdict was "fix":
fidelity to the Umbraco 17 look and the native, calm frame held, with no contradicted rows, and it returned seven
material and seven minor fixes. What happened to each:

| # | Finding | Kind | Disposition |
| --- | --- | --- | --- |
| 1 | Settings, Actions and History at 920 px: "Used on 3 pages" and "…" run under the folded view tab | material | fixed: links fold into "…" after the views; the script now finds content running past its box |
| 2 | Permissions at 920 px: the last column cut off | material | fixed: note above the matrix, narrower columns, scrolling inside the box for more groups |
| 3 | Inbox at 920 px: the table runs past its box, the unseen dot wraps alone | material | fixed in the kit for every list |
| 4 | History: nothing says which version is live; the pin has no visible meaning | material | fixed: "Published" chip, the pin named as a tooltip and for screen readers |
| 5 | Designer at 1024 px: the desktop canvas narrower than the phone one; three rows of toolbar | material | fixed differently: the form reflows with "Narrowed to fit" instead of being scaled to 40 %; the toolbar keeps two rows, because the badge can't sit beside the switch in 300 px |
| 6 | Template gallery: thumbnails look like skeletons and repeat the preview | material | fixed: removed |
| 7 | Header "…" in different places; the overview's "…" outside the health box | material | fixed |
| 8 | Actions: "Failed 40 min ago, tried again at 14:30" | minor | fixed |
| 9 | Actions: Approved's heading not a sentence | minor | fixed |
| 10 | Publish dialog: "live"; the errors state's top edge over the header | minor | fixed; with the shorter change list the dialog no longer reaches the header |
| 11 | Sample data that contradicts itself | minor | fixed |
| 12 | Audit log: "Privacy › Data collection" should be Settings | minor | not changed: Data collection is in the Privacy group (IA 1); the page's title, which had said Settings, was renamed |
| 13 | Option list: preview offset, two renderings under one label | minor | fixed |
| 14 | Names underlined in some lists only | minor | fixed in the kit |

It also listed what it found sound: one primary per layer, the disabled Publish with its reason, Entries at 620 px,
the entry panel's name only once it covers the list, the inspector's seven settings, the stat strip's wording, the
form check's headings, the copy on the empty and error states, the preview's hidden conditional field, the theme
editor's contrast messages, and the persona-driven sidebars. The re-render and both sweeps after these fixes are the
results at the top of this review.

## `emil-design-eng`

The wireframes are static, so the review covers the motion decisions the built screens will follow. The guiding
question is how often a user sees a motion: list navigation, approving and designer edits happen hundreds of times
a day and get no animation; sidebars, dialogs and toasts are occasional and keep UUI's own transitions.

| Before | After | Why |
| --- | --- | --- |
| a shimmering skeleton | static skeleton blocks in `--wf-color-skeleton` | loading is frequent; a shimmer adds movement to every wait, and reduced-motion users see the same picture |
| a row that animates away after Approve | the row's state changes in place (optimistic), with no transition | approving is keyboard-driven and repeated all day; keyboard actions never animate |
| an inspector that slides in on every selection | the inspector appears at once | selecting is the designer's most frequent action |
| a custom sidebar or dialog transition | UUI's own sidebar, dialog and toast transitions | occasional, and native beats new |
| buttons that scale to 0.97 on press | UUI's own pressed state | the backoffice's buttons don't scale; one pressed style across the backoffice beats an added effect |
| chart bars that grow on load | bars drawn at once | Insights is read; growth delays the reading and suggests a change that didn't happen |
| hover effects with movement on palette items | a background change only | palette items are hovered dozens of times a session |
| drag with a spring | our own pointer drag (ADR-0016; Umbraco's sorter is not used): an insertion line that jumps from gap to gap, a 150 ms ease-out settle on drop, and Alt+Up/Down and "Move to…" for the keyboard, which never animate | the line follows the pointer, so easing it would lag; keyboard moves repeat many times in a row (corrected in 2.4, `docs/ux/critique-2.md`) |
| an Undo toast that slides in from the corner | UUI's toast, entering from the same edge it leaves by | spatial consistency is the one purpose motion serves here |

## Decisions

- **Light theme only, English only:** dark mode is UUI's job, and German is checked in copy.md and on the built
  screens.
- **Widths:** PNGs at 1440 and 920 px (and 1024 px for the designer); every state measured at 1024 and 1920 px.
- **Input borders:** the kit draws them in a darker grey than UUI's (3.3:1 instead of about 1.4:1), so they meet
  WCAG 1.4.11. Step 2.3 decides whether the built screens override UUI's token.
- **Destructive primaries** (Empty recycle bin…, the confirms) use the danger style; Retry anyway uses the normal
  primary, because retrying isn't destructive.
- **Find beside its field** on People, as Umbraco's own search dashboards place their search button.
- **Lists respond to the workspace, not the window:** columns give way below a 900 px workspace, measured with a
  container query, because the same window leaves different room depending on the sidebar.
- **The canvas reflows, never scales:** with less room than the chosen width it shows the form as a narrow window
  would and says "Narrowed to fit"; a scaled form would be unreadable at the widths where this happens.
- **The permission matrix scrolls inside its box** when the groups don't fit, the only sideways scrolling in the
  section, because a site can have any number of user groups.
- **IA and copy changes made in this step:** the designer's rail between 620 and 1024 px, the canvas's reflow and
  the header's folding views and links (IA 5.1); the template cards without pictures (IA 4); the "Published" chip in
  History (IA 5.6); Send test email with the email preview (IA 7); the matrix's note and scrolling (IA 8); entry and
  delivery state labels, the "Published" chip, three status lines and the Inbox's empty state (copy.md).
