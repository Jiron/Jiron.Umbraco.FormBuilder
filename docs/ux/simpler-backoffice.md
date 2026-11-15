# A simpler backoffice: a rough roadmap

- Status: a draft the owner asked for on 2026-11-16. Nothing here is built, and none of it is a step of
  [ROADMAP.md](../../ROADMAP.md) yet; the owner picks what becomes one.
- What it is: a first look at where the backoffice asks too much at once, and a rough order to work through it. It is
  not a design. Each part still needs a wireframe and a decision record before code.

## What the owner said

After trying the demo: the functions are welcome, and there are a lot of them. What is missing is a simple way
through them.

- **One way to do a thing, in the place where the thing is.** A page can be added in the list of fields and above the
  form; one of them is enough, and it belongs where the pages are.
- **A click should do something visible.** "Add field" between two fields points back at the list on the left and
  opens nothing.
- **Deleting a page was not found.**
- **Pick first, details after.** The left side lists every settings screen at once and has to be scrolled; one
  "Settings" item with tabs inside would do.
- **Too many columns side by side:** the section's tree, the field list, the form, the selected field's settings.

## How this was looked at

The designer's code, and the screens of the last walk (1440 px, English). No other product was looked at, as the
project's rules ask; the measure is Umbraco's own backoffice, whose patterns an editor already knows:

- **The block picker:** "Add content" in a block list opens a dialog with everything that can be added, and a pick
  puts it where the button was.
- **Workspace views:** the row of tabs at the top of a workspace, as a form already has (Design, Settings, Actions and
  so on).
- **A menu on the thing itself** for what can be done to it.

## Four rules for the pass

1. **One way, one place.** Every command has one home. A second way stays only for the keyboard.
2. **Pick first, details after.** A screen shows what there is to choose from; the details come once something is
   chosen.
3. **The form gets the room.** In the designer the form is the widest thing on the screen, and no column stands
   empty.
4. **Umbraco's pattern before an own one.**

## What was seen

### A. The designer: adding things

| | What is there now | Direction |
| --- | --- | --- |
| A1 | A page is made in three places: "New page" in the field list, "Add page" in the row of pages, and "Start a new page after this" in a group's menu. | "Add page" in the row of pages only. The group's menu item does something else (it splits a page) and needs a name that says so, or goes. |
| A2 | "Add field" on a page and "Add a field here" between two fields only put the cursor into the field list's search when that list is open beside the form. Nothing opens; on a narrow window the list does open over the form. | Both open a picker dialog: search, the groups of field types, the saved groups. A pick puts the field where the click was. |
| A3 | The field list is a column of its own that shows every field type at once, with "Recently used" repeating some of them above. | With A2 the picker is the way to add. The column either goes, or stays closed until asked for. Dragging stays for moving fields. |

### B. The designer: pages

| | What is there now | Direction |
| --- | --- | --- |
| B1 | A page is deleted by clicking its empty background, which selects it, and then "Delete" in the bar that appears. The page's tab in the row of pages offers nothing. | The tab is the page's handle: a click shows and selects it, and its menu holds Rename, Duplicate and Delete. |
| B2 | "After sending" stands in the row of pages like a page. | To check: whether it reads as a page or as a setting, and whether the form's Settings say the same thing a second time. |

### C. The designer: the layout

| | What is there now | Direction |
| --- | --- | --- |
| C1 | Four columns at 1440 px: the section's tree, the field list, the form, the inspector. The form has a bit over a third of the width, and the inspector is an empty column while nothing is selected. | Three at most. The field list goes with A3; the inspector appears with a selection. To consider: the tree folds away while a form is open. |
| C2 | Three rows of controls above the form: the widths, then language, problems and a menu, then the pages. | One row: the pages on the left, the rest on the right or in the menu. |
| C3 | What can be done to a field is offered twice: in the bar over the selected field and in the inspector. | One of them. |
| C4 | Preview, Save draft and Publish stay at the bottom on Entries, Insights and History, where nothing is edited. | To check: whether they belong to the design views only. |

### D. The section's left side

| | What is there now | Direction |
| --- | --- | --- |
| D1 | Under the tree of forms: five headings with seventeen items (Inbox, Spam; Option lists, Themes, Email layouts, Templates; General, Email, Spam protection, Files, Access, Integrations; People, Retention, Data collection; Deliveries, Audit log). With a real number of forms they are below the fold. | A handful of items, each one screen with tabs along its top: for example Inbox, Library, Settings, Privacy, Activity. Nothing is taken away; it moves one level down. |
| D2 | "Settings" names three things: Umbraco's section, the Form Builder's group on the left, and a view of every form. | To check: a name for each that says whose settings they are. |
| D3 | Spam is in three places: its entries on the left, its site settings, and a form's own settings. | To check: whether each place says where the other two are, or whether two can meet. |

### E. A form's own views

- **Seven tabs:** Design, Settings, Actions, Entries, Insights, History, Embed. The pattern is the one the owner likes;
  to check is their number and order, and whether History and Embed are used often enough for a tab each.
- **A form's Settings** is one long page of sections, some behind "More options". Whatever D1 decides for the site's
  settings should hold here too, so both read the same.

### F. Not yet looked at

The entries list and an entry's panel, an action's editor, the rule builder, the theme and email layout editors, the
option lists, the publish dialog. One pass each with the same four questions:

1. Is there a second way to do this?
2. What could wait until something is picked?
3. What is said twice?
4. Does the empty state say what to do next?

## A rough order

1. **Adding and pages (A, B).** The path every editor walks daily, and where the owner stumbled first. Small in code:
   the picker is the field list in a dialog, and the tab gets a menu.
2. **The left side (D).** The largest change to how the section is laid out: the screens' addresses, the guides, the
   screenshots and the walk all follow.
3. **The designer's layout (C).** Follows from 1; best done after it.
4. **A form's views and the rest (E, F).**

Each part, as every UI step before it: a wireframe first, a decision record where an earlier decision is turned
round (the section's shell, the field list, the inspector), English and German, the keyboard paths and WCAG 2.2 AA
kept, the guides and screenshots updated, and the walk run again with its word and screen counts compared.

## Kept on purpose

- **No function goes.** Things move behind a pick; they are not removed.
- **Dragging** to move fields and pages.
- **The keyboard paths** and the shortcuts.

## For the owner to decide

- **The field list (A3):** gone in favour of the picker, or kept but closed until asked for?
- **The left side (D1):** how few items, and whether Inbox stays its own item beside each form's Entries.
- **The tree (C1):** should it fold away while a form is open?
