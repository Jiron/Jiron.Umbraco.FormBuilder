# Information architecture

This is where everything lives in the **Form Builder** section: its menus, trees, workspaces and views, what each screen is
for, its one primary action and its secondary actions, and who sees what. The wireframes of step 2.2 draw these
screens, and Phases 9 to 14 build them. The words are in [copy.md](copy.md), the most common tasks and their click
paths in [top-tasks.md](top-tasks.md), and the reasons for the section's shape in
[ADR-0021](../decisions/0021-forms-section-structure.md).

## Rules this document applies

- **One primary action per screen** at most, in the place Umbraco gives it: the workspace footer, or a modal's or
  sidebar's action bar. The tables below name it in **bold**. A screen for reading, such as the Audit log, has none.
  - **In a workspace with views,** the footer's primary belongs to the whole workspace and stays on every view, as
    Umbraco's document workspace keeps "Save and publish" on its Info view.
  - **A view adds no second primary:** its own main action is the first button of its toolbar, in the secondary style.
- **At most three visible secondary actions** in any toolbar, header or card. Everything else is in "…".
  - **A segmented switch** (Phone · Tablet · Desktop) counts as one control.
  - **The close button** in a modal's corner is part of its frame, and doesn't count.
- **Contextual actions appear in context:** on the selected element, on hover and on keyboard focus, never as
  permanent rows of icons.
- **Say it once** (§4.13): the section's name, a form's name, a count or a state appears once per screen. The tree's
  highlighted item and the workspace header both name the open form, and an unpublished form is muted in the tree
  while its header shows the state chip, as for every item in Umbraco (D12); nothing adds a third.
- **A detail panel beside a list** shows what the row doesn't. While one is open, the list narrows to each row's name,
  state and time, and the selected row is highlighted. Where the panel covers the list (at 620 px), its heading adds
  the name and the state.
- **Actions are offered only when they can work:** a menu never lists what the item's state or the user's permissions
  rule out.
- **One width to design for:** every screen works in a 620 px workspace, the space Umbraco leaves at its 920 px
  minimum (`docs/research/umbraco-backoffice.md`, section 12). Wider screens give the canvas and the lists more room,
  never more buttons. Phones are not a backoffice target.
- **The UI follows permissions** (ADR-0013): what a user may not use is not shown. Where a view is shown read-only,
  one line says which permission changes it.
- **Counting clicks:** a path starts on the section's start page, with the sidebar in view. Every click on a menu
  item, tree item, "…" menu, tab, row or button counts, up to and including the click that starts the task: the button
  that does it, or the one that opens the place where it is done. The task's own input doesn't count: what the user
  types, the values they choose (a filter, a date, an option, a field's settings), the boxes they tick to pick the
  rows to act on, and the button that confirms that input (Save, Find, Add, Update, a dialog's Publish). Opening a row
  counts; ticking its box is input. Keyboard shortcuts exist for the frequent tasks, but no path needs them. Paths are
  counted with the workspace at least 1024 px wide; at 620 px, where the designer's palette opens over the canvas
  instead of staying open, a designer path may take one more click.

## 1. The section at a glance

```text
Form Builder                the section in Umbraco's top bar (ADR-0171)
├── Overview                the start page: setup checklist, health, recent entries, forms at a glance
├── the forms tree          under Umbraco's sidebar header "Forms" with "+" and "…"; the Recycle Bin last
├── Entries                 Inbox · Spam
├── Library                 Option lists · Themes · Email layouts · Templates
├── Settings                General · Email · Spam protection · Files · Access · Integrations
├── Privacy                 People · Retention · Data collection
└── Activity                Deliveries · Audit log
```

Outside the section: a **Forms** tab in Umbraco's header search, the form picker and the form block in the content
editor, a **Form entries** view on the member workspace, and the health checks on Umbraco's Health Check dashboard
(section 10). The section's name in the top bar is the owner's choice and stays "Form Builder" in German, a name as
the product's is; inside it, the tree and the search tab, which hold forms, keep "Forms" (ADR-0171). Where other
documents of this repository say "the Forms section", they mean this section.

### The sidebar

| Group | Item | Opens | Shown to | German label |
| --- | --- | --- | --- | --- |
| (the sidebar header) | the forms tree | folders and forms, then the Recycle Bin | everyone in the section; it lists only the forms and folders they hold a permission on | – |
| Entries | Inbox | the entries of every form, unseen first | `viewEntries` on any form | Posteingang |
| Entries | Spam | the entries in Spam, of every form | `viewEntries` on any form | Spam |
| Library | Option lists | the list of reusable option lists | `manageLibrary` | Optionslisten |
| Library | Themes | the list of themes | `manageLibrary` | Themes |
| Library | Email layouts | the list of email layouts | `manageLibrary` | E-Mail-Layouts |
| Library | Templates | the template gallery | `manageLibrary` | Vorlagen |
| Settings | General | defaults, labels, the form block type, how long a form's History keeps draft saves, per-site base URL and time zone | `manageSettings` | Allgemein |
| Settings | Email | sender, default layout, team addresses, test email | `manageSettings` | E-Mail |
| Settings | Spam protection | default level, content checks, blocked address ranges, provider status | `manageSettings` | Spamschutz |
| Settings | Files | allowed type groups, the largest sizes, scanner status | `manageSettings` | Dateien |
| Settings | Access | who gets which permissions, globally, and the folders and forms with their own access | `managePermissions` | Zugriff |
| Settings | Integrations | webhook signing secret reference, Slack and Teams defaults, Umbraco webhook events | `manageSettings` | Integrationen |
| Privacy | People | find, export, hold or erase one person's data across every form | `privacyTools` | Personen |
| Privacy | Retention | how long entries are kept per state, and what will be deleted soon | `privacyTools` or `manageSettings` (changing it: `manageSettings`) | Aufbewahrung |
| Privacy | Data collection | IP handling, sensitive-field defaults, captcha consent mode, the insights switch | `manageSettings` | Datenerfassung |
| Activity | Deliveries | every email and webhook run, with retry | `manageSettings` | Zustellungen |
| Activity | Audit log | who did what, with filters and export | `manageSettings` | Protokoll |

- **The sidebar header** is Umbraco's own: "Forms", with the root's "+" (create) and "…", where Umbraco users look for
  them. With the top bar, it is the second place the section's name appears; the Overview has no title, so nothing
  adds a third (ADR-0021).
- **A group heading** appears only when the user sees at least one of its items.
- **The Inbox item** carries the unseen count as its badge; the Inbox screen doesn't repeat it. Spam has no badge, so
  it never draws attention to itself.

## 2. Overview (the start page)

The section's only dashboard, so Umbraco shows it without a tab bar. It is for reading, so it has no primary action:
forms are created from the sidebar header's "+", as everywhere in Umbraco, and until the checklist is complete its
second item creates the first form. Its top row holds its "…" on the right.

| Block | Shown | Content |
| --- | --- | --- |
| Setup checklist | to everyone in the section, until all five items are done | the five steps of §4.13, each turning green by itself, with "1 of 5 done" in the box's header: **Check email sending** (opens Settings › Email, where Send test email sits); **Create a form from a template** (opens the template gallery); **Preview and send a test entry** (opens the newest form's Preview); **Put the form on a page** (its button is Create a form block type…, and it says how to allow the block on a page); **Choose who sees entries** (opens Settings › Access). The next open item carries its action as a button (Choose a template… on a fresh install); later ones link, so items can be done in any order, unless they can't work yet (there is no form to preview) or the user can't do them, in which case they say who can ("needs an administrator"). A done item shows its check and its name, and says "done" once, to screen readers. |
| Health | to everyone in the section | "All checks pass" in one line, or one line per warning: what is wrong, what to do, and who can fix it when the user can't. While the checklist shows an open item, Health leaves out the warning that item already covers (email sending). |
| Recent entries | to users with `viewEntries` | the latest ten entries of the forms they may view; a row opens the entry panel (6.3) |
| Forms at a glance | to users with `viewEntries` | the site-wide table of 11.5: per form, views, starts, entries and conversion, each with its change from the period before. Its period menu (last 30 days, the default; last 7 days; this month; last month; this year) compares periods as Insights does (5.5). A row opens the form's Insights view. |

- **"…"**, once the checklist is complete, for everyone in the section: Show the setup checklist. Create a form block
  type… then lives in Settings › General (section 8).
- **A fresh install** shows the checklist and the health line; its second item creates the first form.
- **A user without any permission** sees the checklist, the health line, and in the tree's place one sentence naming
  who can give their group access (copy.md, empty states).

## 3. The forms tree

Folders and forms, sorted by name, with the **Recycle Bin** as the last item. Folders expand in place with Umbraco's
expanders.

- **Marks:** a form that is not published, or was unpublished, is muted, as Umbraco shows unpublished content. A
  restricted folder or form carries a small lock mark. For users with `viewEntries`, a form with unseen entries shows
  a dot; the number is said once, on the Inbox item.
- **Clicking a form** opens its workspace (5).
- **Clicking a folder** opens its list: its folders and forms with the last 30 days' entries and when each was last
  changed. Their state shows in the tree's marks, not again in the list. Its header has the editable name (that is the
  rename) and the tree's "…" without Rename…; its primary action is **Create form** in this folder.
- **Clicking the Recycle Bin** opens the list of trashed forms, with when and by whom; its primary action is **Empty
  recycle bin…**. An empty Recycle Bin says so in one sentence and has no footer, since there is nothing to empty.

| Item | "+" (create) | "…" |
| --- | --- | --- |
| Sidebar header (the root) | Form · Form from a template… · Import a form… · Folder… · Draft a form with AI… (only with the AI add-on) | Reload |
| Folder | the same options, inside the folder | Permissions… · Rename… · Move to… · Delete… (only when the folder is empty) |
| Form | – | Duplicate… · Move to… · Copy actions to… · Save as template… · Export as JSON · Permissions… · Trash |
| Recycle Bin | – | Empty recycle bin… |
| A form in the Recycle Bin | – | Restore… · Delete… |

- **Umbraco shows the first entry of a "…" menu** as an icon beside it, on hover and on focus, so each menu above
  starts with its most common action.
- **Trash** asks first only when the form is still picked on a page, and then names the pages. Otherwise it moves the
  form at once and offers **Undo** in the toast (§4.13: reversible actions use undo).
- **Restore…** opens Umbraco's restore sidebar, which puts the form back in the folder it came from, with its entries
  and its published state, so the pages that pick it show it again. When that folder no longer exists, Umbraco's
  folder picker asks where it goes.
- **Permissions…** opens a sidebar (`managePermissions`, ADR-0013): the user groups with their permissions on this
  folder or form, as a small matrix; "Add user group"; the switch **Only these groups**; primary **Save**. It offers
  only the Forms and Entries permissions: the library, settings and privacy tools are granted for the whole section.
  - **The switch** restricts the folder or form. While it is on, only the grants in this matrix, and those on the
    folders and forms inside, count here; global grants and grants on parent folders stop reaching it. Administrators
    keep access, because they could grant it to themselves anyway (ADR-0013).
  - **Turning it on** with no group in the matrix asks first, because then only administrators keep access.

## 4. Creating a form

| Screen | Content | Primary | Secondary |
| --- | --- | --- | --- |
| Create dialog | Umbraco's create dialog: Form ("a new, empty form"), Form from a template…, Import a form…, Folder…, and Draft a form with AI… with the add-on | – (choosing an option is the action) | – |
| Template gallery (modal) | the built-in templates (contact, callback, newsletter, event registration, job application, feedback, quote request, support request, privacy request; 12.3) and the site's saved templates, as cards: name and one line on what it is for. The cards are one radio group; choosing one shows its preview beside the gallery, and the preview shows the form, so the cards carry no picture of it. Below a 1000 px window the preview no longer fits beside the cards: Preview opens it over them, with "All templates" to go back. | **Use template** | Cancel · Preview (below a 1000 px window) |
| Import a form (sidebar) | choose a JSON file; the sidebar says what it holds (pages and fields, rules, outcomes, actions; never entries or permissions) and what it can't import: a theme the site lacks gives way to the default theme, a page the site lacks must be chosen before publishing, and an action whose address the site lacks arrives switched off. The form arrives as a new draft in the chosen folder. | **Import as a new copy** | Cancel |
| Draft a form with AI (sidebar, add-on) | a description, or the pasted text of an existing questionnaire | **Draft form** | Cancel |

A new form opens in the designer with its name selected, ready to change. A drafted form opens marked "Drafted by AI,
please review" until its first save.

## 5. The form workspace

**Header:** the back arrow, the name (editable with `design`), the state chip (Draft, Published, Published (pending
changes) or Unpublished, Umbraco's own words), the link "Used on 3 pages", and "…". "(pending changes)" shows only to users
with `design`, who can act on it; to others the chip says what visitors get, "Published".

- **"Used on":** opens a sidebar listing the pages that pick the form and where on each page ("Form block in Main
  content"), each linking to the content editor. With none, it reads "Not used on any page", and its sidebar says how
  to put a form on a page, with "Create a form block type…" for users who may.
- **"…"** starts with Restore an earlier version… (users with `design`, 5.6), then holds the tree's form actions,
  Unpublish… and, for users with `manageSettings`, "Audit log for this form" (the Audit log, filtered to it).

**Views,** as tabs in Umbraco's place: **Design, Settings, Actions, Entries, Insights, History**. Users see the views
their permissions allow (section 11). A form opens on Design for users with `design`, otherwise on Entries.

**Footer** (users with `design`): Preview · Save draft · **Publish…**. Leaving with unsaved changes asks first
(Umbraco's "Discard unsaved changes", with Stay and Discard changes).

### 5.1 Design

**Layout:** the palette on the left, the canvas in the middle, the inspector on the right. The canvas shows the form
as visitors with JavaScript will see it, drawn as the website draws it, in the default theme on a white sheet; Preview
shows it in the form's own theme on the site's page (ADR-0168). A repeatable group shows its first row. The chosen
width is the form's own width: Desktop 960 px, Tablet 768 px, Phone 375 px. The canvas leaves out the visitor's step
progress, since the steps across its top stand in for it.

- **Beside the canvas, the inspector's column is always there,** with "Nothing selected" and a sentence on how to
  choose an element until one is, so selecting or clearing never changes the canvas's width or moves the element just
  clicked: a double-click on a field edits its label in place (ADR-0182).
- **At 1024 px and wider,** the palette stays open beside the canvas, and both panels can be collapsed to rails.
- **Between 620 and 1024 px,** the palette lies on its rail (its expand button and its search, which open it over the
  canvas) whenever the view is that narrow, selected or not; folding the inspector gives the palette back as the user
  left it. The widths are the Design view's own, as for the narrow view (ADR-0170): a 1024 px window gives it 724 px.
  On a narrow sheet the selected element's bar shows its buttons' icons alone, still named for screen readers. The
  wireframes of step 2.2 found that both panels open leave the canvas about 150 px at a 1024 px window.
- **The canvas keeps room of its own** above each field and group, a little more than the visitor's page has, so a
  selected element's bar and handle never lie over the element above it, and the form never shifts when one is
  selected (ADR-0182).
- **When the canvas has less room than the chosen width,** it shows the form as it reflows in that room, as a
  visitor's narrow window would (columns stack), and "Narrowed to fit" beside the width switch says so. It never
  scales the form down: text stays readable, and collapsing the inspector gives the canvas its width back.
- **At 620 px** (and in any workspace up to 700 px), the canvas takes the width. The palette opens over it from an
  insert point or a page's "Add field", and the inspector opens over it when an element is selected; Esc closes either.
- **The header** keeps its name, state chip and link on one line; when room runs out, the views fold, from the last
  one and never the open view, into the form's own "…" ("Views and more actions for Contact us"), so the header always
  has one "…". If that isn't enough, the link ("Used on 3 pages") moves into the same "…".
- **While the form loads,** the canvas is a skeleton, the toolbar and the steps are hidden, and the footer's buttons
  can't be used yet.

| Part | Content | Actions |
| --- | --- | --- |
| Toolbar | the canvas width (Phone · Tablet · Desktop), the language (only on sites with more than one), and the form check badge | "…": Undo · Redo · Rules overview · Keyboard shortcuts · Translate with AI… (add-on) |
| The steps | the pages by name across the top of the canvas (an unnamed page reads "Page 1"), then "Add page" (an empty page at the end, whose empty state says that the palette's Layout › New page splits a page instead), then **After sending** | Clicking a step shows that page. After sending shows the outcome as visitors will see it, read-only, with "Change", which opens that outcome's editor in the Settings view. |
| Canvas | groups with their columns, and fields as visitors see them. An insert point, a button "Add a field here" in the gap above each element and below a group's last one, appears on hover and on keyboard focus, and every page ends with a visible "Add field", which opens the palette for the end of that page, or moves the focus to its search where it is already open. Rules show as small chips on the elements they affect, except on the selected element, whose rules are in the inspector: one per rule in use, in words, at the element's top end beside its translation chip, and the description of the button that selects it; a click on one selects the element ([ADR-0178](../decisions/0178-seeing-rules-and-validation-rules-in-the-builder.md)). | An insert point opens the palette for that point, whose Layout › New page starts a page there. Every field and group has a handle ("Move Email address") at its top left, shown on hover, on focus and while it is selected, which drags it; a page is dragged by its step, and a step takes a dropped field or group at that page's end (ADR-0169). On the selected element: Move to… · Duplicate · Delete · "…" (Change type… · Start a new page after this · Save group as template… for groups · Copy alias). At 620 px they sit in the header of the inspector, which then covers the canvas. |
| Palette | search, "Recently used", then **Layout** (Group: a set of fields, with optional columns; New page: starts a page at that point and moves what follows onto it), the field types in groups, the site's saved groups, and "Paste questions…" at the end | A click inserts at the chosen point, or after the selected element, or at the end of the current page; dragging a type from the palette beside the canvas places it. |
| Inspector | shows the selected element: its type at the top ("Email field"), with Change type beside it for a field whose type may become another, then the sections; the label is the first field of Essentials | Change type opens the same dialog as the element's "…": the types the field may become, and what the one chosen takes from the field and what it then needs, before anything changes (ADR-0174) |

**The inspector's sections,** each under a header that is a button opening or closing it (`aria-expanded`):

- **Essentials** (open): label, help text, **Required**, placeholder.
  - **File fields** add the allowed types and the largest file. Its help names the site's limit; for users with
    `manageSettings` it adds "Change the site's limit", a sidebar holding that one setting of Settings › Files.
  - **Choice fields** add "Options from" (this field, an option list, a standard list, content, a data type, a JSON
    address, the country list), "Depends on" for a field whose options follow another's answer, and the options
    themselves, or a grid's rows and columns ([ADR-0172](../decisions/0172-the-option-editor-and-the-option-sources.md)).
    The option list picker ends with "Create an option list…" for users with `manageLibrary`, which makes one in a
    sidebar without leaving the form.
  - **Rich text** (a paragraph) is edited in place with a small toolbar: Bold, Italic, Link, Bulleted list and
    Numbered list, each saying whether it is on. A consent's and a checkbox's text, which sit in the visitor's label,
    are one line: Bold, Italic and Link. A link takes a web, email or phone address or a path on the site, and may open
    in a new tab.
  - **Umbraco's items** (an image's picture, the page whose children are a field's options) are chosen with Umbraco's
    own pickers; the site's document types and its data types that hold values, which Umbraco shows only to users of
    its Settings section, are ticked or chosen from the site's own list.
  - **Texts made for merge tags** (a heading, a paragraph, a consent's wording, a field's default value) have "Insert
    merge tag" under their box: a search, then the tags this text may use by source, the answers of the fields
    before it first by their labels; a choice puts the tag where the caret was, and the focus leaving the list closes
    it. A label, help or placeholder takes a typed tag only
    ([ADR-0173](../decisions/0173-rich-text-merge-tags-pickers-and-patterns-in-the-inspector.md)).
- **Validation** adds, for text, email, phone and web address fields, **Must match a pattern**: the pattern, its
  message, and **Try it**, which says at once whether a typed answer matches, as the site will check it. A pattern the
  site's matcher refuses is said under its box; the answer tried is never saved.
- **Validation, Rules, Appearance and Advanced** (closed, remembered per user):
  - a closed section shows a short summary of what is set ("Maximum length: 200"), or "Nothing changed", wrapping
    rather than cut, so a rule's sentence keeps what it depends on (ADR-0182);
  - a section shows at most seven settings before "More options".
- **Each box** has its label, its help and why a value can't be kept tied to it for screen readers; a value is checked
  as it is typed and said once the box is left. Typing in one box is one step to undo. A setting the site locks shows
  its value and the configuration key behind it, read-only; one it hides is not shown (ADR-0060, ADR-0170).
- **The alias** is checked as the server checks it. Once the form has entries, changing a saved field's alias asks
  first ("Change the alias?") and keeps the old one for the merge tags that use it.
- **Rules:**
  - the closed section's line holds its action directly: "Always shown · Add a rule", whose link sits beside the
    header's button, not inside it, and opens the rules builder without opening the section first (the link comes
    with the builder in 9.5a; until then the line says "Always shown" or how many rules);
  - the section's help says that a rule sits on the element it shows, hides or requires, or on the field it shows a
    message on: a field's validation rules are listed among its rules, each opening the builder (ADR-0178).
- **The AI add-on:** "Suggest help text" sits beside the help text.

**The keyboard** (ADR-0016) reaches everything on the canvas, in the visual order:

- **"Skip to the form"** is the workspace's first stop, so the canvas doesn't wait behind Umbraco's header, the tree
  and the palette;
- each field is a button that selects it (Enter or Space), and each page step is a button; Esc clears the
  selection, closes the palette or cancels a drag;
- **F2** or a double click edits the selected field's label in place: Enter keeps it, Esc puts the old one back;
- **Up** and **Down** go through the page's elements in reading order; **Ctrl+D** duplicates, **Delete** deletes, **F2**
  edits a label, and **Alt+Up** and **Alt+Down** move one place, a field into the next group at the end of its own and
  a group into the next page, each acting on the element the focus is on, else on the selection; **?** lists every
  shortcut in a dialog with only Close (ADR-0169);
- **Move to…** lists the groups and repeatable groups by page (for a group, the pages) and puts the element at the
  end of the chosen one; places the form refuses are listed with why and can't be chosen;
- a move the form refuses says why at the element (while dragging, at the line), never in a toast;
- the focus stays on the moved element after every move, goes to the field that took its place after a delete, and to
  what Undo or Redo restored.

**Undo and redo** have no buttons of their own:

- every designer change can be undone with Ctrl+Z and redone with Ctrl+Y;
- deleting something, or moving it by a drop or Move to…, shows a toast with **Undo**; keyboard moves, one after
  another, are said instead (ADR-0169);
- both are in "…" with their shortcuts.

**Paste questions…** opens a dialog: one question per line, then the fields it will add with the type guessed for
each, which can be changed afterwards; primary **Add 5 fields**.

**Translating** (on sites with more than one language): choosing another language in the toolbar shows that
language's texts on the canvas and in the inspector, which says that visitors see the default language's text where
one is empty, and shows that text in an empty box. A field without a text in the chosen language carries the chip "No
German text yet".

**The form check badge** shows the number of problems ("2 problems"), or "No problems".

- It opens a sidebar listing errors first, then warnings, each with **Fix it**, which selects the element and opens
  the setting.
- Errors block publishing.

**The rules builder** (sidebar):

- its first line names what the rule affects ("Show **this field** when…"), and the target can be changed there, so a
  rule started from the question it depends on still lands on the right element;
- a form-level validation rule is a rule whose first line reads "Show a message on **this field: Email** when", the
  message's box under it; the builder offers it for a field whose answer the visitor gives and rules compare (not a
  name, an address, a grid or a repeatable group), and its conditions may look at that field itself (ADR-0178);
- a rule its element can no longer have (after a type change) opens with what it does still shown and the first line
  saying so, kept only once another effect is chosen;
- rule rows in plain language ("Show this field when **Country** **is** **Other**"), with "Add condition" under the
  last row;
- condition groups, "all" and "any": each starts with "all of these are true", which can be changed to "any of these is
  true", and "Add condition group" sits beside "Add condition" (never "group", a form's group of fields; ADR-0175);
- **Try it** under the conditions (ADR-0175): a box for each field and part of the visit the rule looks at, and one
  sentence on what the element then does ("Which country? shows."), from the same engine the website uses;
- the switch **Use this rule**, which keeps the rule without using it;
- the action bar: Delete rule at its start (an existing rule only; it acts at once, as every designer change does, and
  the toast offers Undo), Cancel, and primary **Update**, or **Add** for a new rule.

**The rules overview** (sidebar, from "…" and from the form check):

- every rule in the form, by page, each linking to its element, then the outcomes' rules under After sending (an
  action's rule shows on its card in the Actions view, IA 5.3);
- a conflict, a hide rule and a require rule that hold for the same answer, is warned on the row it concerns, with the
  answers it holds for, where the form engine finds such answers among those a visitor can give (ADR-0178);
- **Add rule**, which first asks what the rule affects, in a box at the overview's top; Continue then selects the
  element and opens the builder on it for a new rule;
- empty, one sentence on what most rules do.

**A new blank form** shows one empty page with its "Add field" and a single sentence pointing to it and to "Paste
questions…". The form check counts its one error ("Add at least one field", whose Fix it opens the
palette for the end of the page), so an empty form never reads "No problems"; Publish… stays usable, and the publish
dialog blocks it with Fix it.

### 5.2 Settings

One page with the groups of §4.3.4, in their order: Submission, After sending, Availability, Spam protection, Privacy,
Appearance, Developer.

- **Open or closed:** Submission and After sending are open. The others are closed, each with a summary line ("Open
  until 30 November · 120 entries at most").
- **Submission** shows five settings: the send button's label, Store entries, Manual approval, Email verification and
  the entry title (with its merge-tag picker). Its "More options" holds the settings of forms with several pages (the
  labels of Next and Back, Summary page, Progress, Save and continue later) and the entry reference: its prefix, the
  year and the digits, with an example of a reference visitors get.
- **Availability** names the site's time zone once, on whose clock the opening and the closing are shown and typed.
- **Spam protection** is a list of choices with a line each: "Site default (Standard)" names what the site chooses, and a
  captcha provider without its keys is dimmed, its line saying that a developer adds them. A chosen provider's own
  settings follow it (reCAPTCHA v3's lowest score).
- **Problems:** the form check's problems in the settings show under their setting, a closed group counts them on its
  line, and Fix it opens the group, and More options where the setting is there, with the focus on it (ADR-0184).
- **Help:** each setting has its help text below it and, where useful, "Learn more".
- **The outcomes** (After sending) are an ordered list, each with an optional rule; visitors get the first one whose
  rule matches. A row's move handle ("Move Redirect to /press/thanks") shows on hover and on focus (ADR-0016).
  - A row opens the outcome's editor in a sidebar: a message (rich text with merge tags, optionally with a copy of the
    answers), a redirect (a page or an address), or a message and then a redirect, and its rule, beside a live preview
    with a sample entry. The action bar: Delete outcome at its start (it acts at once and the toast offers Undo),
    Cancel, primary **Update**.
  - Each row says when visitors get it: its rule in words, "Otherwise" for the first outcome for every visitor ("Used
    for every visitor" when it is first), and "Never used" for any after that one, which no visitor gets (ADR-0185).
    Its problems are counted on it, in the error colour where an error is among them.
  - The sidebar asks the form check about the draft as Update would make it and says its problems under their setting:
    those the outcome had at once, others once their setting is left; an error keeps Update back, a warning does not,
    nor does a problem in another language's message, which names the language.
  - The sidebar's **Place in the list** moves the outcome without dragging; the live region says where it went.
  - **Preview with a sample entry** sits beside the settings in Umbraco's large sidebar, kept in view as they scroll, and
    follows them where the sidebar has no room for two columns. The server renders the outcome of the draft as
    Update would make it, for a sample entry made from the form's fields (number 42, a made-up person's name and
    address), with the site's own views in the canvas's look: the message with the sample's answers in place of its
    tags and a sensitive answer masked, the reference, "Go now" with the countdown visitors see, and the copy of the
    answers. A redirect shows as "Visitors go straight to /press/thanks". It follows each change once the changes
    pause, the last preview staying until the next; one that fails says so with Try again (ADR-0187).
  - "Add outcome" sits under the list, under the list's own problems; at the most outcomes a form can have, a line
    says so in its place. A new outcome with a rule goes before the first outcome for every visitor, after which it
    would never be used, until its place is chosen.
  - A new form has one outcome: the default thank-you message (copy.md). Without any outcome, visitors get that
    message, and the list says so.
- **Settings, outcomes and actions belong to the draft:** a sidebar's **Add** or **Update** changes the draft like any
  designer change, Save draft keeps it, and Publish puts it live. On a published form, the toast after Add or Update
  says that it applies once the form is published (copy.md, success); Publish… stays in the footer only.

### 5.3 Actions

The actions grouped by trigger: **Submitted**, **Approved**, **Rejected** and **Verified**, each a heading with when
it runs ("when an entry comes in"). The actions one event queues run in the order shown (ADR-0188).

- **Groups whose setting is off:** Rejected needs manual approval, and Verified needs email verification (10.2). While
  its setting is off, the group shows one line saying so, with a link that opens the setting in the Settings view
  with the focus on it, and any actions it holds under that line, which don't run until the setting is on. Approved,
  on a form without manual approval, says that it runs as each entry comes in.
- **The toolbar:** Add action, the view's main action and its one button. It opens the action sidebar ready to email
  the team, the most common action. At the most actions a form can have, a line says so in its place.
  - **Its first setting, What happens,** offers every preset of every action type, and a type without presets as
    itself, in the registry's order: Email the team, Confirm to the visitor, Webhook, Slack, Microsoft Teams, Change the
    entry's state, Create or update content. Choosing another replaces the settings below it, and the name with them
    until a name is typed. An action of the form keeps what it does, said where the choice would be: another type is
    another action.
  - **Then** the name, the trigger (Submitted unless chosen; each choice says when it runs or the setting it needs),
    the place in the list among that trigger's actions (a pointer orders them without dragging, WCAG 2.5.7), the type's
    own settings as the inspector draws them (Essentials, then More options), Stop the rest if this
    fails, and the rule, which the rules builder edits ("Run Email the team when").
  - **Problems:** the sidebar asks the form check about the draft as Add or Update would make it and says its problems
    under their box: those an action of the form had at once, others, and all of a new action's, once their box is
    left; an error keeps the button back, a warning does not. A problem under More options opens them; one of a setting
    whose editor comes later shows under What happens, by the setting's name.
  - **Footer:** Delete action (for an action of the form) and Test at its start; Cancel · **Add**, or Cancel ·
    **Update**, at its end. Test tests the action as the sidebar holds it, before Add or Update (ADR-0190).
  - **After Add,** the sidebar closes, the new card takes the focus, the live region says where it runs ("Added. Email
    the team runs when an entry comes in."), and on a published form the toast says when it starts to run.
- **A card:**
  - the type icon, the name, a one-line summary ("To sales@example.com", "Webhook to crm.example.com"), its problems
    counted in front of it, and the rule chip; then its last run on a line of its own (10.2f, ADR-0195), in the type's
    words: "Delivered 2 hours ago" for a webhook, Slack or Teams, "Mail queued 2 hours ago" for an email, "Ran 2 hours
    ago" for another, muted; "Failed at 14:02. Next try at 14:30." while it is tried again ("Next try soon." once that
    time has passed) and "Failed at 14:02." once it has failed for good, in the danger colour; "Uncertain at 14:02. It
    may have gone through." in the warning colour; "Running now"; "Skipped 2 hours ago". An action that has never run
    says nothing. The line has the card's width under the name, and comes before the card's controls as it is read.
    The view reads the last runs as it opens and again when its page is shown again;
  - visible: the on/off switch, named for what it turns on ("Use Email the team"), and **Test** (10.2c), which a card
    too narrow to keep its name readable beside it offers first in its "…" instead; "…": Duplicate · Move to another
    trigger… · Delete. Duplicate puts "<name> (copy)" right after it, and is not offered at
    the most actions a form can have; Move to another
    trigger… opens the sidebar with the focus on the trigger; Delete acts at once and the toast offers Undo;
  - cards reorder within their trigger by drag and by Alt+Up and Alt+Down (ADR-0016), through a move handle that shows
    on hover and on focus (always on touch); the live region says where a card went. A change closes the toast whose
    Undo would take back another, and Undo and Redo keep the focus on the card it was on;
  - clicking a card opens its sidebar (footer: Delete action · Test · Cancel · **Update**);
  - a failed or uncertain last run ("Failed at 14:02. Next try at 14:30.") links to **Details**, a small sidebar
    named for the action, in four parts under small headings: Last run, the line again; What happened ("The server
    answered 503.", or a site type's own message where the backoffice has no words for its code); Tries ("Tried 3
    times, the first at 13:40.", as the line says the latest's time); and What can be done, never the entry. It says
    who can retry the run, and where: the entry, found in Entries with the Actions filter, or Activity › Deliveries
    (ADR-0013, amendment), and offers Test, which sends a sample entry to see whether the server answers again; who may
    only read sees it without Test. Its footer: Test · Close.
- **The email editor** (10.2b, ADR-0189), in Umbraco's large sidebar:
  - **To** is one box: the addresses as chips, then the other kinds of recipient there are, each a row with its remove
    button: The answer of [an email field], The person who created the form, and Addresses chosen by [a choice field],
    with each of its options and the addresses it sends to. **Add recipient** opens An address (one or several, apart by
    commas) and the kinds not there yet; a kind the form has no field for is not offered. What can't be kept of a typed
    address is said under the box it was typed in.
  - The settings go: To, the subject, the body, Leave out unanswered fields, Reply to, Copy to; **Blind copy to**
    waits under More options with the layout, the sender and the view.
  - Another field chosen for Addresses chosen by keeps the addresses typed for the one before while the sidebar is
    open: choosing it again brings them back.
  - **Copy to** and **Blind copy to** are chips with a box that adds them. A field picker offers only the fields of its
    kind (an email field for Reply to, a field marked sensitive for Show sensitive fields, a file upload for Attach
    files) and says when the form has none. **Layout** offers the site's default by the layout it is ("Site default
    (Card)"), the built-in card and letter, and the library's.
  - The subject and the body offer **Insert merge tag** for mail.
  - **Preview with a sample entry** shows beside the settings where the sidebar has room for two columns, after them
    where it has not: From, To (and the address a mail of its own goes to), Copy to, Blind copy to, Reply to and the
    subject, then the mail as the team gets it (as the visitor gets it, and its help says so, where only a field's
    answer gets it), in a frame that runs nothing, with a Light/Dark switch; a plain-text mail shows its text. It is
    asked once the changes pause, and who may only read the actions sees it too.
  - The form check's Fix it of an email setting lands on its part: a recipient's row, or Add recipient for the To
    box itself.
- **The webhook editor** (10.2d, ADR-0191), for a webhook and its Slack and Microsoft Teams presets:
  - **Where it goes:** the target (An address of its own, Slack, Microsoft Teams), then the address, whose help says
    what it takes there: an https address on a host the site allows, or a reference where
    it holds a key; for a chat, empty for the site's, or a reference, as a chat's address holds its key. One typed as
    itself for a chat is said under the box and not kept.
  - **What it sends:** for its own address, the format, then one choice, each with its line: Every answer, by its
    field's alias; The names and values you choose (a table of names and values that starts with every field, Add a
    name, a remove button a row, and one Insert merge tag that puts a tag into the value last in focus); A body you
    write (in the format's own language, with Insert merge tag); Only that something happened. Send metadata too
    follows the first two. For a chat: A message (with Insert merge tag; empty sends the form's name and the answers)
    or A body you write (JSON). The chosen way's box is drawn under its own choice, the choices after it following.
    Another choice, or another target, keeps what the one before held while the sidebar is open; only the chosen one is
    saved.
  - **More options:** the signing secret, whose help says what `X-Jfb-Signature` is for (not for a chat, which checks
    no signature; its address's secret comes back with it), and Headers, rows of a name and a value with Add header; a
    credential, the secret above all, is taken only as a reference, and one typed as itself is said under its box and
    never kept. Send sensitive fields follows. On a phone a row's two boxes stand one under the other, named by their
    placeholders where the columns' heads go, the remove button beside both.
  - The form check's Fix it of a webhook setting lands on its box: the target, the address, the format, the table, the
    body, the message, a switch, the headers or the secret; a problem of a way not chosen is said under the choices.
  - **Who may only read it** sees each box in words: the target and the address, then the format and what goes
    ("JSON: every answer, by its field's alias, with metadata"), or the chat's message; the headers a line each. A
    secret written as itself where a reference belongs is said to be there, never shown.
  - **A host's lock** on the target draws the address alone, with the same guard, and What it sends for the host's
    target; a lock on a way's setting draws the table and each of that target's settings as they are.
- **The content editor** (10.2e, ADR-0192), for Create or update content:
  - **Document type:** the site's document types an item may have, by name, as the setting's own choices (Umbraco
    lists them only to users of its Settings section); its help says each entry becomes an item of that type.
  - **Where it goes:** three choices, each with its line: under an item you pick (at the root of the content tree
    without one), under the page the form is sent from, or under the root of the visitor's site, each of those within
    the item you pick; the item picked is set in under the chosen one, named "Under this item" or "Only within this
    item" to who hears it.
  - **Name**, with Insert merge tag; **Properties**, a table of rows, each a property of the document type and what
    it takes: "The answer to" a field it can hold (one with one answer, never a repeatable group's), or a value written
    for it in the form the property stores (words with Insert merge tag, a whole number, a decimal, which a decimal
    comma becomes a point, a date, Yes or No). A field it can't hold, or one that is gone, is said so; a property
    nothing on the form fills says so in place of its choice. Add property offers the properties not set yet that the
    form can fill, by their groups; a row stays out of the draft until it has a property and what it takes, and its
    problems show under it once the table is written. Another document type keeps the table of the one before while
    the sidebar is open, an emptied one too; one chosen for the first time starts with the properties a field of the
    same alias can fill. Properties that could not be read have Try again. **Publish it** follows, with its help;
    under More options, Update an item of the same name (its help says a visitor's answer can choose the item), the
    media folder (only where a row takes a file upload's answer, or where it holds one) and Copy sensitive fields.
  - The form check's Fix it lands on its box: the document type, where it goes, the item picked, the name, or a row's
    property, what it takes or its value.
  - **Who may only read it** sees the document type by its name, where it goes in words, the item picked as Umbraco's
    own input names it under what it is for, and each row as "Title: the answer to Headline".
- **Change the entry's state** (10.2e): "Move the entry to" Approved, Rejected or Spam, whose help says approving or
  rejecting starts that trigger's actions. The form check refuses two actions that would move an entry between
  Approved and Rejected for ever, naming the other, and warns of one that moves an entry to the state its trigger's
  entries are in already.
- **Test** runs the action as the card's draft or its sidebar holds it, saved or not, against a sample entry made
  from the form's fields (8.1, ADR-0190); its button is busy until the answer, and a toast says what it came to:
  - an email goes only to the user who tests, and its first line, in their backoffice language, names the recipients
    the real one would go to; where the site cannot send mail, the toast says so and nothing is queued;
  - a webhook asks first ("Send a test?"), then sends with `X-Jfb-Test: true`; a chat's message starts with a line
    that says it is a test, and the toast says what the server answered, or why it did not go, in the backoffice's
    words;
  - a content action says what a run would save or publish, under its name and where, the properties it would set
    and any it would leave out, with the sample entry's answers, and creates nothing; a test that would leave
    something out is a warning, which stays until it is closed; Change the entry's state says the state a run would
    move the entry to and how many actions that starts (ADR-0192).
- **Without `manageActions`,** the view is read-only, and one line at the top says which permission changes it. It
  names no folder: the grant that decides can be global, a folder's or a restricted folder's (ADR-0188). It has no Add
  action, move handles, switches, Test or "…", and a card that is off says "Off"; a card still opens its settings as
  they are, with Close only. Designers meet it where a grant gives them `design` without `manageActions` (section
  11).
- **Fix it and links:** the form check's Fix it of an action opens its sidebar on the setting the problem names, or
  puts the focus on its card; a link with `?action=<key>`, such as the copy-actions result's (ADR-0166), puts the focus
  on that action's card.
- **Empty:** one sentence on what actions do, pointing to Add action in the toolbar, which stays the view's only button.

### 5.4 Entries

The entries list of 6.1 for this form only, without the form column, and with the form's fields available as filters
and columns. Its "Export…" exports this form's entries. The columns chosen here are the user's for this form, and the
Inbox uses them too while it is filtered to this form.

### 5.5 Insights

**Toolbar:**

- **the date range:** last 7 days, last 30 days (the default), this month, last month, this year, or a range; its
  popover also holds "Group by" (day, week or month);
- **the switch** "Compare with the previous period": a calendar period compares with the same days of the one before
  (this month so far with the same days of last month), a rolling one with the span just before it. It applies to the
  numbers and to the chart of entries per day;
- **the segmented switch** Overview · Answers.

**Overview:** views, starts, entries and conversion, each with its change in words ("up 6% from 4,540"); entries per
day (or per week or month, as grouped); who reaches each page, with the median time to complete; the fields that stop
visitors most, with the kind of error; the top website pages; entries by hour of day, in the site's time zone; and,
on sites with members, how many members sent entries.

**Answers** (11.5b): a summary per field. Choice fields show their options as bars; free-text answers are listed,
newest first, each linking to its entry, and never summarised; other fields say how often they were answered.

- **Every chart** has "Show as table", which swaps it for its table in place; each box remembers the choice per user.
- **Empty:** "No data yet", with when numbers appear.
- **Insights off:** one line naming where to turn them on, and nothing else.

### 5.6 History

A list of the published versions, newest first. Each row says when, who, and what changed against the version before
("Changed: 2 fields, After sending"), with its comment and pin; the version visitors get carries a "Published" chip,
so a form with pending changes shows which version they are pending against. History answers who changed what in a
form, its settings included; the Audit log answers it for the site's settings and permissions, and for what was done
with entries.

- **Draft saves:** the switch "Show draft saves" adds them to the list. Settings › General says how long they are
  kept; published and pinned versions are kept for good.
- **Clicking a row** opens that version in a sidebar, compared with the current draft in plain language. Primary
  **Restore as a new draft**; secondary "Restore and publish…", which opens the publish dialog for the restored draft.
- **Comparing:** ticking the boxes of two versions shows Compare, the view's main action, in the toolbar. The
  comparison lists fields added, removed and changed, then settings, actions and rules.
- **A row's "…":** Restore as a new draft · Pin · Add comment…. Restoring is reversible, so it acts at once (§4.13):
  the toast names what changed, since the menu skips the comparison, and Undo puts the draft before it back, which
  also stays in History.
- **Restore an earlier version…,** first in the form's "…", offers the same from every view: a sidebar listing the
  versions by date, with the same "Show draft saves" switch, where choosing one shows the same comparison and the same
  two buttons.

### 5.7 Preview, publishing and conflicts

| Screen | Content | Primary | Secondary |
| --- | --- | --- | --- |
| Preview (full-screen modal) | the draft rendered by its theme in a frame, at the form's width as in the designer (Desktop 960 px, Tablet 768 px, Phone 375 px), with "Narrowed to fit" when the window has less room. Submissions there are test entries, which run no actions unless "Run actions for test entries" is on. After sending, the note offers **Run actions…** for that one test entry, which asks first and lists the actions, because their emails and messages then really go out. | – (sending the form is the action) | the canvas width · Fill with sample answers (Start again, after sending) · "…" (Light or dark · Language · Run actions for test entries) |
| Publish dialog | what changed since the live version, grouped (fields, settings, actions, rules), or, on a first publish, what the first version holds; the form check's result, where errors block with **Fix it** and warnings are listed too, with Fix it, without blocking; an optional comment for the version. After a first publish, while no page shows the form, the toast offers Show how, which opens the "Used on" sidebar. | **Publish** | Cancel |
| Save conflict | that the draft wasn't saved, who saved, and when, after this user opened the form, and that the user's changes are still here | **Review both versions** | Cancel |
| Review both versions | the two versions side by side; each difference can be taken from either side | **Save** | Cancel |

## 6. Entries

### 6.1 The Inbox and a form's entries

Both are the same list. The Inbox shows every form the user may view, with a form column, unseen entries first and
then the newest. A form's Entries view shows that form only.

- **Columns:** the selection box (named "Select entry 1042"), with a box in the header that selects the page; the
  entry title, or its number when the form has none; the form (Inbox only); the state chip; the time received; the
  action status ("3/3 delivered"); and the columns the user adds.
- **A column whose value a filter fixes** to one value is hidden while that filter is on, since its chip already says
  it; a filter that leaves several values (a date range) keeps its column.
- **The column chooser** sits at the end of the header row. It opens a sidebar of checkboxes: the details every entry
  has, and the form's fields on a form's Entries view or while the Inbox is filtered to one form. Otherwise one line
  says that filtering by a form offers its fields.
- **At 620 px** the form's name moves under the entry's title, and a failed delivery shows in the state column, under
  the chip, so the names keep their width.
- **Unseen entries** are bold with a dot. Opening one marks it seen.
- **Toolbar:** the search field · Filter · Export…; "…": Email me a summary… · Keyboard shortcuts.
- **Search** matches the entry title, number and reference, and the answers of fields that are not sensitive. When it
  finds nothing and sensitive fields exist, the empty state says that they aren't searched.
- **Filter** opens a menu:
  - Form (Inbox only), State, Received (today, this week, last 7 days, this month, last month, or a range), Actions
    (failed, uncertain, all delivered: where entries whose emails or webhooks failed are found), and the form's fields
    by type;
  - under "Saved", the user's own filter sets: on the Inbox all of them, each naming its form when it has one
    (choosing such a set filters the Inbox to that form); on a form's Entries view, the sets for that form and those
    without one.
- **Active filters** show as chips in a row of their own under the toolbar, followed by "Save as…" and "Clear filters".
- **Bulk bar** (Umbraco's, at the bottom, once a row is selected): visible, the first three actions the user may use,
  in this order: Approve · Reject · Mark as spam · Delete… · Export selected… · Re-run actions. The rest are in "…".
  - **An action is offered** when it applies to at least one selected entry. It acts on those, and says how many it
    left out ("3 entries approved. 3 weren't waiting for approval.").
  - **After a state change** (approve, reject, mark as spam, not spam) the selection clears, the rows change in place,
    and the toast offers **Undo** for 10 seconds. The actions the change starts wait until then, so Undo also cancels
    them (ADR-0023). Delete… asks first instead, because deleting can't be undone.
  - **On a form's Entries view** the bulk bar sits directly above the workspace footer. The two never merge: the
    footer's buttons are about the form, the bulk bar's about the selected entries.
- **Keyboard:** `j`/`k` move, `Enter` opens, `a` approves, `r` rejects, `s` marks as spam, `/` searches, `Esc` closes,
  `?` lists them in a dialog with only Close (§4.13).
  - **The keyboard cursor** is a focus ring around the row, without a fill, so it never looks like a ticked row or the
    open entry. `a`, `r` and `s` act on the cursor's row in the list, and on the open entry in the panel.
  - **Nothing animates:** these keys are pressed hundreds of times a day (ADR-0016, amendment).
- **States:**
  - **first use,** which depends on what the user may do: users who may design the form get "share the form, or
    preview it and send a test entry"; everyone else is told that entries appear as soon as visitors send the form,
    and is offered Email me a summary…. The Inbox's own first use says that entries arrive from every form the user
    may view, offers the same, and shows no toolbar, since there is nothing to search, filter or export;
  - **no results:** one sentence that points to the chip row above, whose Clear filters is the only one;
  - **a search that finds nothing,** with Clear search;
  - loading as a skeleton of rows; an error with "Try again".

### 6.2 Spam

The same list for entries in Spam, without the state column: everything here is in Spam, so neither the rows nor the
entry panel's header repeat it.

- **Toolbar:** search · Filter · Empty spam… (`deleteEntries`).
- **Bulk bar:** Not spam · Delete….
- **Empty:** "No spam", with one sentence on what lands here.

### 6.3 The entry panel

A sidebar beside the list; the list keeps its place, its selection and its scroll position. It opens on Answers.

- **Header:** what the narrowed row beside it doesn't show (see "Rules" at the top): on the Inbox, the form's name;
  and the number ("Entry #1042"), left out when the row already shows it because the form has no entry title. The
  state chip joins them only where no row shows the state: at 620 px, where the panel covers the list, and when the
  panel opens without a list. In Spam it never shows. Opened without a list beside it (from Recent entries, People
  or a member's Form entries view), the header also shows the entry title and the form's name.
- **The actions** sit in the sidebar's action bar at the bottom, as in every Umbraco sidebar: the primary on the
  right, the visible ones beside it, then "…".
- **Actions, by state:**

  | State | Primary | Visible (the first three the user may use) | "…" |
  | --- | --- | --- | --- |
  | waiting for approval | **Approve** | Reject · Mark as spam · Forward… | Delete… · Print · Re-run actions |
  | in Spam | **Not spam** | Delete… | Print |
  | any other | – | Mark as spam · Forward… · Delete… | Print · Re-run actions |

  A user who may use fewer actions sees fewer: a privacy officer, who may delete but not change states, sees Delete…
  as the panel's visible action. Approve and Reject appear only on forms with manual approval, while the entry waits;
  on other forms entries are approved automatically, so there is nothing to decide.
- **After a state change** (Approve, Reject, Mark as spam, Not spam, or their keys), the panel stays on the entry, so
  `j` moves on. The row's chip changes in place, the bar shows the actions of the new state, and the toast offers
  **Undo** ("Entry #214 approved. [Undo]") for 10 seconds, while the actions the change starts wait (ADR-0023). Once
  the window has passed, the toast loses its Undo and says that the state can be changed back by hand. Nothing
  animates.
- **Tabs:** Answers · Timeline · Notes.
- **Answers:**
  - **A line of details** comes first: the website page it was sent from ("Sent from /contact"), the language and the
    time to complete. "More details" adds the member, the IP address if stored, and the spam reasons; in Spam, the
    reasons come first.
  - **The answers** follow, grouped by page and group, with fields from older versions of the form marked as such.
  - **Files:** images (PNG, JPEG, GIF, WebP) open in a viewer dialog through the authorised download, with Download;
    every other file downloads, and SVG and HTML are never shown inline (7.3).
  - **Consents** show the exact text the visitor saw.
  - **Fields kept only until the actions have run** show as "not kept".
  - **Sensitive values** are masked, with **Reveal** for users with `viewSensitive`: one value at a time, at once and
    without asking, with Hide to mask it again, and a line under it saying that it is recorded in the audit log.
  - **"Edit answers"** in the tab's header (`editEntries`) turns the answers into fields, and the action bar holds
    Cancel and **Save** alone. Files, consent texts and the sensitive values the user can't see stay as they are: a
    file is the visitor's upload, and a consent is proof of what they agreed to. One line says that the changes are
    recorded with the old values (audited).
- **Timeline:** every event, the action runs (with **Retry** for users with `manageActions`) and the emails.
- **Notes:** a box for a new note, whose help says that visitors never see notes and everyone who may open the entry
  can read them, with **Add note**; then the notes, newest first. Users edit their own.
- **Forward… opens a sidebar:**
  - choose a backoffice user, or one of the team addresses of Settings › Email;
  - an optional message;
  - "Include sensitive values", only for users with `viewSensitive`, off by default;
  - primary **Forward**.

  Other addresses can't be chosen: entries leave the site only to people the site already trusts (11.2).

### 6.4 Exports and the summary email

| Screen | Content | Primary | Secondary |
| --- | --- | --- | --- |
| Export (sidebar) | the format (CSV, Excel, JSON, printable view, ZIP of the uploaded files), the fields, and the current filters with the number of entries they match; under "More options" the delimiter, labels or aliases as headers, and the byte-order mark. It opens with the user's last choices for this form. | **Export** | Cancel |
| Email me a summary (sidebar) | Off, daily, or weekly on Mondays; it goes out at 07:00 in the site's time zone and covers the day or week before. It holds counts and links, never answers, only for the forms the user may view, and goes to the user's own address. Below the choice, what it looks like. | **Save** | Send test email · Cancel |

A large export continues in the background; a notification links to the file when it is ready (11.4).

## 7. Library

Each item opens a list. A row opens the item's own workspace, with its name in the header and **Save** in the footer.

| Item | The list | The item's workspace | Header and "…" |
| --- | --- | --- | --- |
| Option lists | name, kind, number of forms that use it; primary **Create option list**, which asks for the kind first (fixed options, content, a data type, a JSON address, or an upload) | the editor for its kind with a live preview of the options, CSV or TXT upload, and, for a JSON address, how long fetched options are kept, with Fetch now | "Used by 2 forms" · "…": Duplicate · Delete… |
| Themes | name, a swatch, and "Default" on the default theme, unless its name already says so (Site default); primary **Create theme**; "…": Import a theme… | colours (light and dark), type, spacing and corners, buttons and the focus ring, custom CSS under "More options", beside a live kitchen-sink form, with each colour's contrast check under it | "Used by 5 forms" · "…": Duplicate · Export · Delete… |
| Email layouts | name, a thumbnail, and "Default" on the default layout; primary **Create email layout** | logo, colours, header and footer per language, beside a preview with a sample entry in light and dark; Send test email sits with the preview, because it sends what the preview shows | "Used by 3 actions" · "…": Duplicate · Delete… |
| Templates | the gallery of built-in and saved templates; no create button, because templates come from "Save as template" on a form or a group | none: a card opens its preview, with **Use template** | saved templates' "…": Rename… · Delete… |

- **A field uses an option list** through "Options from" in the inspector's Essentials (5.1).
- **Settings beside a preview** (a theme, an email layout, and the action sidebar of 5.3) show one side at a time
  where both don't fit (a workspace under 900 px, or a window under 1000 px for the action sidebar): the header ends
  with the switch Settings · Preview.
- **The default theme and email layout** are chosen in Settings › General and Settings › Email, the one place that
  decides them. The "Default" mark in the lists says which they are, and links there.

## 8. Settings

Every settings item opens one screen:

- groups of at most seven settings before "More options", each setting with its help text below it;
- a setting locked by host configuration shows the key that locks it (ADR-0008);
- **Save** in the footer.

**General and Email** start with a site switch, set to "All sites", for the fields that can differ per site. A field
with its own value for the chosen site says so, and offers to use the value for all sites again.

| Item | What it holds | Primary | Visible secondary |
| --- | --- | --- | --- |
| General | default theme, which fields get a mark (the optional or the required ones), default labels, new-form defaults, how long a form's History keeps draft saves; per site the base URL and the time zone; the form block type, with Create a form block type… while there is none (section 10) | **Save** | site switch |
| Email | sender name and address, default layout, team recipients for system notices, team addresses that entries may be forwarded to | **Save** | site switch · Send test email (to the user who sends it), offered only while Umbraco can send mail; until then one line at the top says so and names the key that sets up sending |
| Spam protection | default level, each choice with its provider's key status (a captcha without keys can't be chosen, and names the key that adds them); content checks (blocked words, blocked email domains, the most links an entry may hold); blocked address ranges, compared when an entry arrives and never stored. A blocked email domain is enough to send an entry to Spam; blocked words and extra links add up (at the default strictness, three blocked words, or two with too many links). Entries are never refused for what they say (D14). A form sets only how strict its checks are, and its Spam protection group says where the words are kept. | **Save** | – |
| Files | allowed type groups; the largest size any file field may allow; the scanner and storage status. Each file field sets its own limit, up to this one, in its Essentials, whose help names this limit and, for users who may change it, opens it in a sidebar. | **Save** | – |
| Access | a matrix of every user group and its permissions, globally, in three groups (forms, entries, and the library, settings and privacy tools); when the groups don't fit, it scrolls sideways inside its box with the permission column fixed, because a site can have any number of groups. The Administrators column is ticked and can't be changed, and one line above the matrix says why. A group that can't open the Forms section yet is named once, in a line above the matrix, with a link to Umbraco's user groups, where the section is added. Below it, the folders and forms with their own access or a restriction, each with one sentence on what is special about it, and each opening the tree's Permissions sidebar. | **Save** | Add folder or form… |
| Integrations | webhook signing secret reference; the hosts webhooks may call (read-only, from host configuration); Slack and Teams defaults, whose addresses are references to host configuration too; Umbraco webhook events on or off | **Save** | – |

## 9. Privacy and Activity

| Item | What it holds | Primary | Visible secondary |
| --- | --- | --- | --- |
| People | an email address, or a member chosen with Umbraco's member picker. After **Find**: that person's entries in every form, restricted ones included (ADR-0013), with their files and emails. A row opens the entry panel (6.3). | **Find** | after a search: Export everything (a ZIP of JSON and files) · Set legal hold… · Erase… |
| Retention | how long entries are kept, per state (Submitted, Approved, Rejected, Spam); the forms with their own retention; and "What will be deleted", per form, for the next 7, 30 or 90 days, without a button to press. While the days are being changed, it counts with the values not yet saved. | **Save** (`manageSettings`; read-only otherwise) | the span of "What will be deleted" |
| Data collection | IP handling, sensitive-field defaults, captcha consent mode, the insights switch | **Save** | – |
| Deliveries | every email and webhook run, problems first (failed, uncertain, limited), then the newest; state, recipient, form and time. A row opens its details. | in the details: **Retry** for a failed or uncertain run | search · Filter |
| Audit log | who did what, when, on which form or setting. A row opens its details, with the values before and after; a form's save or publish links to that version in the form's History, where the changes are listed. | – | search · Filter · Export |

- **Export everything** prepares the ZIP in the background: the results box shows how far it is, the button can't be
  used meanwhile, the user can carry on, and a toast and a notification link to the file.
- **Set legal hold…** (a sidebar, ADR-0009): which of the person's entries, the reason, and the end date, at most a
  year ahead; primary **Set legal hold on 1 entry**. A held entry shows the hold and its end date in its row, and
  Erase… leaves it out until the hold ends.
- **Erase…** always shows its preview first: what will be deleted or anonymised, in which forms, and how many files
  and emails (13.2). Afterwards, one line in the list's place gives the counts and, for users with `manageSettings`,
  links to the audit record; others read that it is recorded in the audit log.
- **Retention** is set in this one place for the whole site. A form's own values sit in its Settings view (Privacy
  group), and this screen lists those forms.
- **Deliveries:**
  - an `Uncertain` run's **Retry** asks first, and says it may deliver twice;
  - a `Limited` mail offers no retry, and names the limit that held it back;
  - while sending is paused, one line at the top says so (12.4b).

## 10. Outside the section

| Place | What Jiron Form Builder adds |
| --- | --- |
| Header search | a **Forms** tab: forms by name and alias (with their folder), and fields across all forms by label or alias ("Email, in Contact us"). A result opens the form, or the form with that field selected. For users with `viewEntries`, the tab ends with "Search entries for '…'", which opens the Inbox with that search. |
| Content editor | the "Jiron Form Builder form picker" property editor (one or more forms; optional theme and redirect override), and a block labelled with the form's name ("Form: Contact us") |
| Overview (checklist item 4), later Settings › General | Create a form block type…, for users with `manageSettings` and Umbraco's Settings section. Its dialog says what it creates, a "Form" element type and data type, and that it never changes existing ones (6.7); its primary is **Create**. Then the same dialog says the one step left, allowing the block on a document type, with **Open document types**. |
| Member workspace | a **Form entries** view after Details and Info (not on the create screen, where no member exists yet), listing the entries the member sent while signed in, newest first, with permissions applied: forms the user may not view are left out without a trace. A row names the entry by its number, since its title would mostly repeat the member's name, and opens the entry panel (14.3). The view adds nothing to the footer. |
| Umbraco's Health Check dashboard | the same health checks as the Overview (9.1) |

## 11. Who sees what

The personas are those of ADR-0013, each granted globally. Grants per folder and form, and restricted folders, narrow
or widen this per form. "–" means the screen or action is not shown.

| Screen or action | Designer | Entry handler | Viewer | Administrator | Privacy officer | No access |
| --- | --- | --- | --- | --- | --- | --- |
| Overview: setup checklist and health | yes | yes | yes | yes | yes | yes |
| Overview: recent entries, forms at a glance | – | yes | yes | yes | yes | – |
| The forms tree | all forms | all forms | all forms | all forms | all forms | empty, with one sentence |
| Create, duplicate, move, trash and import forms; folders | yes | – | – | yes | – | – |
| Form views Design, Settings and History; Preview, Save draft, Publish | yes | – | – | yes | – | – |
| Form view Actions | change and test² | – | – | change and test | – | – |
| Form views Entries and Insights; Inbox; Spam | – | yes | yes | yes | yes | – |
| Email me a summary | – | yes | yes | yes | yes | – |
| Approve, reject, spam, not spam, notes, edit answers, forward | – | yes | – | yes | – | – |
| Reveal sensitive values | – | – | – | yes | yes | – |
| Delete entries, empty Spam | – | – | – | yes | yes | – |
| Export entries from a list (6.4), the ZIP of uploaded files included | – | yes | – | yes | – | – |
| Open or download one uploaded file | – | yes | yes | yes | yes | – |
| Re-run actions, retry a delivery | –¹ | – | – | yes | – | – |
| Library | yes | – | – | yes | – | – |
| Settings: General, Email, Spam protection, Files, Integrations; Privacy › Data collection; Activity | – | – | – | yes | – | – |
| Settings › Access; Permissions… on folders and forms | – | – | – | yes | – | – |
| Privacy › People, with its export of one person's data, legal holds and Erase…; Privacy › Retention (read) | – | – | – | yes | yes | – |
| Privacy › Retention (change) | – | – | – | yes | – | – |
| Header search, Forms tab | all forms | all forms | all forms | all forms | all forms | the tab, finding nothing |
| Member workspace: Form entries | – | yes | yes | yes | yes | – |
| Content editor: the form picker | yes | yes | yes | yes | yes | yes |

¹ A retry needs `manageActions` for the run's form and a screen that shows the run: the entry's timeline, which needs
`viewEntries`, or Activity › Deliveries, which needs `manageSettings` (ADR-0013, amendment). The designer holds
`manageActions` but neither of the others, so it tries actions with **Test** instead, and a failed run's Details in
the Actions view say who can retry it (5.3).

² Read-only where a restricted folder's own grant gives the designer `design` without `manageActions` (5.3).

**The outsider,** a backoffice user without the Form Builder section, sees no such section, no Forms search tab and no
Form entries view. Every management endpoint refuses them (ADR-0013), and the form picker in the content editor still works
for them: it shows only the names of published forms that are not restricted.

## 12. How the design skills shaped this

ADR-0019 and ADR-0020 load the three design skills for every step that designs UI. This step designs the structure,
not the look, so each skill contributed what applies to structure.

- **`design-taste-frontend`:**
  - **The design read:** "Reading this as: the backoffice section of an Umbraco package, for editors, entry handlers,
    administrators and privacy officers at work, with a calm and native language, leaning toward Umbraco's UUI
    components and its own patterns."
  - **The dials:** the skill's presets are for landing pages and don't fit. A native backoffice reads as variance 2,
    motion 1 and density 5: symmetric Umbraco layouts, no added motion, and lists as dense as the work needs.
  - **Anti-default discipline:** no second design system; cards only where they carry a real hierarchy (action cards
    and the template gallery); no eyebrow labels.
- **`impeccable`:**
  - **Mode:** Operate. The visitor's success here is a finished task.
  - **The `shape` brief:** job and audience (§1.2's personas at work), the outcome (each top task in three clicks or
    fewer), the scope (every screen of Phases 9 to 14, structure only), states (each screen's empty, loading and error
    states named where they differ), and constraints (D12, §4.13, a 620 px workspace, en and de). The skill would
    confirm the brief with a person. The owner answers no questions during the run (CLAUDE.md rule 8), so the brief
    rests on the roadmap's personas and rules, and the first-time-editor check of `top-tasks.md` tests it.
  - **`clarify`** shaped [copy.md](copy.md).
  - **The product context file** `PRODUCT.md` is written in 2.2, as the roadmap orders.
- **`emil-design-eng`:**
  - **How often decides motion:** list navigation, approving and designer edits happen hundreds of times a day, so
    they get no added animation; only a field dropped by the pointer settles, over 150 ms, so the eye can follow it
    (ADR-0016, amendment).
  - **Feedback** comes from UUI's own states, optimistic updates, and an **Undo** toast after a delete, a move or an
    entry's change of state (ADR-0023).
  - Motion choices proper belong to the wireframes and the review of 2.2.
