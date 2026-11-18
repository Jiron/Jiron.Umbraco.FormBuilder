# 0258. A form's views: pick first, one fold, the form's buttons where they apply

- Status: accepted. Builds decision 4 of [ADR-0254](0254-a-simpler-backoffice.md) and its rules 2 and 5. It turns
  round parts of [ADR-0184](0184-the-form-settings-view.md) (the Settings view as groups that open and close),
  [ADR-0170](0170-the-inspector.md) ("More options" inside the inspector's sections),
  [ADR-0188](0188-the-actions-view.md) (the action's sheet with its kinds in it) and the rule of
  [information-architecture.md](../ux/information-architecture.md) that a workspace's footer is the same on every
  view; and it removes "Restore an earlier version…" from the form's "…"
- Date: 2026-11-18
- Roadmap step: 22.3

## Context

The owner: "I don't want to be bombarded with everything at once. I just want to have to pick something, and then I
go to that place, and then in that place I have the more detailed stuff." Four places in a form's views still show
everything at once or offer a thing twice ([simpler-backoffice.md](../ux/simpler-backoffice.md), C4 and E1 to E3):

- **A form's Settings** are six groups on one page that open and close, any number at a time, and Submission folds
  again inside ("More options").
- **The inspector's sections** open and close, and a section with more than seven items folds again inside. What
  runs over seven today is two things: the texts a field says when it refuses an answer, which stand among its
  checks, and what is kept of an answer, which stands among the alias and the default value.
- **"Add action"** opens one sheet with every kind to choose from, four moments each with its explanation, and the
  settings of the kind chosen.
- **Preview, Save draft and Publish…** stay in the footer on every view, so on Entries with an entry open two primary
  buttons stand one above the other (Approve, Publish…).
- **An earlier version** is restored from the History view and from the form's "…".

The earlier reasons: a page of groups shows what is set everywhere at a glance (ADR-0184); seven settings before
"More options" keep a section short (section 4.13); one sheet makes an action in one place (ADR-0188); Umbraco's
document workspace keeps its footer on every view; and the "…" was a second door for who looks there first.

## Decision

### 1. A form's Settings: the groups to pick from, and the one picked

- **The view has two parts:** the six groups as a list (Submission, Availability, Spam protection, Privacy,
  Appearance, Developer), each row with the line a closed group has today (what is set, its problems counted, a text
  the language shown lacks), and beside it the group picked, alone. The list is always there, so what is set
  everywhere is still seen at a glance, which answers ADR-0184's reason; only one group's settings are on the screen.
- **One group is always picked:** Submission at first, then the one the user picked last.
- **In a view too narrow for both parts** (a 620 px workspace) the list stands alone; a pick shows the group in its
  place, with "All settings" to go back. The focus follows: to the group's first setting on a pick, to the group's
  row on the way back.
- **"Fix it" picks the group** and puts the focus on the setting, as it opened the group before.
- **Submission's "More options" stays,** now the view's only fold.
- **The language** is chosen with the button the Design view has, in the same words, so the form's views have one
  control for it.

### 2. One level of folding in the inspector

- **The sections stay the fold. "More options" goes from them.** Two sections come to hold what ran over:
  **Messages,** after Validation, for the texts a field says when it refuses an answer; and **Privacy,** before
  Advanced, for Sensitive, Keep the answer and Include in emails. Each says what it holds, which "More options" did
  not.
- **A section never holds more than seven.** Where a type's own settings still run over (a site's own field type
  may), the rest stands in a section of its own right after it, named for it ("Essentials, more"), closed. Section
  4.13's seven are kept by that, without a fold inside a fold.
- **An action's sheet** has one fold, its "More options", and keeps it.

### 3. An action's kind first

- **"Add action" opens the kinds to pick from,** each with its icon, its name and one line, as the Create dialog
  lists what can be made. A pick shows that kind's sheet in the same sidebar, headed by the kind, with a way back to
  the kinds while the action is not added yet.
- **The sheet holds that kind's settings only.** When it runs is one choice among the moments, and only the moment
  chosen is explained under it.
- An action already added opens on its sheet, as before; its kind does not change.

### 4. The form's buttons where they apply

- **Preview, Save draft and Publish… show on Design, Settings and Actions,** the views that change the form, and not
  on Entries, Insights and History. With an entry open its own bar is the only one, with one primary button.
- **Where the footer holds nothing it is not drawn,** so no empty bar stands under a list.
- **Unsaved changes are kept across the views** as before, and leaving the form with them asks first.

Umbraco's document workspace keeps its footer on every view because every view of a document changes the document.
A form's Entries, Insights and History show what the form brought in; a Publish… under them says something about
them that is not true. That is the answer to the rule this turns round, and the rule now reads: the footer's
buttons show on the views they act on.

### 5. One door to an earlier version

The History view. "Restore an earlier version…" leaves the form's "…", with its code where nothing else uses it.

## Consequences

- **A form's Settings take the workspace's width** instead of a column in its middle; the inspector gains two
  sections and loses a fold; adding an action is a pick and then a sheet, one click more for the first action and
  the same clicks for who had to find the kind in the sheet.
- **The top tasks are counted again;** none may grow by more than one click.
- The browser tests that opened a group, "More options" in the inspector, the kinds in the sheet or the "…"'s
  Restore follow; [information-architecture.md](../ux/information-architecture.md) sections 5 to 5.6, the copy guide
  and the guides describe the views as they now are.
