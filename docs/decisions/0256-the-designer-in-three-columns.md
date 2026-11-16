# 0256. The designer in three columns: a picker to add with, pages at their tabs, After sending in place

- Status: accepted. Builds decisions 1 and 3 of [ADR-0254](0254-a-simpler-backoffice.md). It turns round parts of
  [ADR-0168](0168-the-canvas-draws-with-the-elements-views.md) (the palette beside the canvas, "Recently used"),
  [ADR-0169](0169-the-designers-moves.md) (dragging a type from the palette),
  [ADR-0170](0170-the-inspector.md) (the palette's rail beside the inspector),
  [ADR-0174](0174-changing-a-fields-type-in-the-designer.md) (Change type in two places),
  [ADR-0182](0182-the-phase-9-ux-walk.md) ("Nothing selected") and
  [ADR-0185](0185-the-outcomes-editor.md) (the outcomes as a group of the form's Settings)
- Date: 2026-11-16
- Roadmap step: 22.1

## Context

The owner tried the designer and found three things ([simpler-backoffice.md](../ux/simpler-backoffice.md), A to C):
a page could be started in three places and deleted in none that was found; "Add field" between two fields opened
nothing; and the form had the least room of four columns. ADR-0254 decided the direction: the list of types beside the
form goes for a picker, and "After sending" is edited where it is shown.

Each thing found was decided on purpose once. The reasons, which this record has to answer:

- **The palette beside the canvas** (ADR-0168, ADR-0169): one click adds a field after the selected one, and a type
  can be dragged onto the form.
- **The palette's rail and the "crowded" width** (ADR-0170): up to 1024 px the palette and the inspector did not both
  fit beside the canvas.
- **"Recently used"** (ADR-0168): the list of types is long, and the last three save a search.
- **A page selected by its background** (ADR-0016): every element of the canvas is selected the same way.
- **The outcomes in the form's Settings** (ADR-0185): they are settings of the form, and the canvas only shows.
- **Change type in the header and in the element's menu** (ADR-0174): reachable from wherever the element is acted on.
- **"Nothing selected"** (ADR-0182): the inspector's column is always there, so the form never moves on a click.

## Decision

### 1. The picker

- **One way to add.** Every page's "Add field" and every insert point (between two elements, a group's first place,
  an empty group) opens the picker, at every width. Nothing else adds a field or a group.
- **It is a sidebar of Umbraco's,** opened through the modal manager as Umbraco's own block picker is: headed "Add a
  field", with the focus kept inside, Esc and Close closing it, and the focus given back to what opened it. It lies
  over the inspector's side, so on a wide window the place on the form stays in sight, and the insert point stays
  marked while it is open.
- **What it holds:** the search, with the focus; then Layout (Group), the field types under their groups, and the
  site's saved groups, in two columns where the sidebar has the room and in reading order for the Tab key; in its
  footer "Paste questions…" and Close. A type the site cannot offer stays in its place with its reason under its name
  and cannot be picked; the reason is read where it is, not in a toast.
- **The search** filters as it is typed, as the palette's did. Enter in it adds the first match, which is marked
  while a search is typed; Arrow down goes from the search to the first item. Without a match the picker says so.
- **A pick** closes the picker, puts the element where the click was, selects it and gives it the focus. "Paste
  questions…" closes the picker and opens its dialog for the same place.
- **"Recently used" goes.** With every type on one screen and the search ready, it showed three of them a second
  time and saved no click.
- **The palette goes:** its column, its rail, its fold, the "crowded" width that existed for it, dragging a type from
  it (a drag's `new` source) and the browser's list of recent types, with their code, texts and tests. The picker's
  code is loaded when it is first opened.

The earlier reasons: one click became two, and a type is no longer dragged from a list. The owner accepted both
(ADR-0254). Against the second click stand the search with the focus, Enter for the first match, "Add field" right
under the field just added, and Paste questions and Duplicate for many fields at once.

### 2. Pages at their tabs

- **The row of pages is the pages' home.** A tab shows its page. The open page's tab holds a menu, "More for
  Page 1": **Rename**, **Duplicate**, **Delete**.
  - Rename goes to the page's Title in the inspector, its text selected; it opens the inspector where it is folded
    or lies over a narrow canvas. The inspector is the one editor of a page's texts.
  - Duplicate adds the copy after the page and shows it.
  - Delete deletes with Undo in its toast, as before. It is not offered while the form has one page.
- **"Add page"** follows the last tab as "+", named "Add page" for the pointer's tip and for screen readers, as the
  "+" after a row of tabs is read everywhere. It adds an empty page at the end, shows it and puts the focus on its
  "Add field", as before.
- **"New page" leaves the list of types:** a page is not a field. Cutting a page in two stays in an element's "…"
  alone, renamed **"Move what follows to a new page"** ("Folgendes auf eine neue Seite verschieben"), and offered
  only where something follows the element on its page.
- **The page has no button on the form any more.** A click on the sheet's empty room clears the selection. A page's
  chips (its rules, a missing translation) stay at its top and describe its tab. Wherever the focus went to the
  page's button, it goes to the page's tab. On a tab the keys stay: Delete, Ctrl+D, F2 (Rename), Alt+Up and Alt+Down;
  a tab is dragged to move its page and takes a dropped field or group.

The earlier reason (every element selected the same way) gives way to the owner's: a page's commands were behind a
click nobody found. A page can still be the selection (a link from the form check, Rename over a narrow canvas); it
only has no handle on the form.

### 3. After sending, in place

- **The row's last step shows what visitors get,** as before, without "Change".
- **The outcomes are edited beside it,** in the inspector's column: "After sending", the sentence on their order
  where there is more than one, the list as the Settings view had it (handle, name, when visitors get it, its
  problems; a row opens the outcome's sidebar with its preview), the default message, "Add outcome". Where the
  inspector is not open beside the form (folded, or a narrow view), the same list lies under what visitors get.
- **The form's Settings lose the group.** Every "Fix it" and link that led to it (the form check, the publish
  dialog) opens the Design view on After sending, with the outcome's sidebar on the setting named. The code moves
  with it, out of `designer/settings/`.

The earlier reason (the outcomes are settings of the form) is answered by the owner's third decision: they were shown
in one place and changed in another.

### 4. Three columns and one row

- **Columns:** the form and the inspector (320 px; 56 px folded; over the form in a view up to 700 px). The form takes
  the room the palette had: 820 px of a 1440 px window.
- **The inspector is never empty.** With no field or group selected it shows the open page ("Page", then the page's
  sections as they are today: its Title, and its rules), and on After sending the outcomes. "Nothing selected"
  goes; the column still never comes or goes with a selection, so the form never moves (ADR-0182 stands in that).
- **One row above the form:** the tabs, "+" and After sending at its start; at its end the width (three buttons with
  Umbraco's device icons, named Phone, Tablet and Desktop), the language on sites with more than one (a button
  showing the culture, "en-US", which opens the languages by name), the form check's badge and "…".
  - The badge is its icon and the number of problems, a check mark alone where there is none, at one width whatever
    it says; its full words ("Form check: 2 problems") are its name for screen readers and the pointer's tip, and
    the list it opens has them. With its words ("No problems", "Keine Probleme") the wireframe's row did not hold
    two pages in German at 1440 px, and a badge whose width changed with every check would move the row.
  - Where the pages need the room, the row becomes two: the pages, then the tools at the end. A tab alone on its row
    first shrinks, its name cut with an ellipsis and whole in its tip, and the tabs scroll within their own strip
    only when they are still wider than the row; "+" and After sending never leave it (the fourth rule of
    ADR-0254).
  - At a 1024 px window the form's column is 404 px beside the open inspector (348 px before), which shows the form
    as a phone would; folding the inspector gives it 668 px.
- **"Narrowed to fit"** stands at the sheet's top edge, in the room above the sheet, which is kept whether it shows or
  not. Desktop is offered at every width: section 4.13 asks for the three, and a switch that changes with the window
  would be one more thing to learn.
- **Change type** is offered once, in the inspector's header beside the type's name.

## Consequences

- **Measured by the step's checks:** at 1440 px the form's column has more than half the window under one row, in
  English and in German, for a form of two pages on a site with two languages; at 1920 px Desktop shows the form at
  its 960 px.
- **Adding a field costs one click more** (the top tasks are counted again), and the designer's start loads less
  code.
- **The browser tests that added fields through the palette** add them through the picker; the walk, the journeys,
  the guides and the screenshots follow.
- [information-architecture.md](../ux/information-architecture.md) section 5.1 and 5.2, the copy guide, the editor
  guide and the wireframes describe the designer as it now is.
