# A simpler backoffice: a rough roadmap

- Status: a draft the owner asked for on 2026-11-16, gone over a second time the same day. The owner then decided its
  five open questions ([ADR-0254](../decisions/0254-a-simpler-backoffice.md)) and made its four steps Phase 22 of
  [ROADMAP.md](../../ROADMAP.md), steps 22.1 to 22.4, which hold the checks. **The phase is built** (2026-11-17 and
  2026-11-18): the designer in step 22.1 ([ADR-0256](../decisions/0256-the-designer-in-three-columns.md)), the left
  side in 22.2 ([ADR-0257](../decisions/0257-five-places-before-the-tree.md)), a form's views in 22.3
  ([ADR-0258](../decisions/0258-a-forms-views-pick-first.md)) and the other screens in 22.4
  ([ADR-0259](../decisions/0259-the-other-screens-and-the-walks-count.md), [review-22.md](review-22.md)). Under each
  finding below, one line says what became of it; the table at the end has the numbers measured so far.
- What it is: where the backoffice asks too much at once, a direction for each place, and an order to work through
  them. It is a concept, not a design: each step still starts with a wireframe and a decision record.

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

- **The designer's code**, for what each button does today.
- **The design records** ([information-architecture.md](information-architecture.md) and the decision records it
  names), for why things are as they are. Several things the owner stumbled over were decided on purpose; the reason
  is named with each below, since a new direction has to answer it.
- **About twenty screens of the last walk** (1440 x 900 px, English).
- **Umbraco's own backoffice** as the measure, since an editor already knows its patterns. No other product was looked
  at, as the project's rules ask.

The patterns of Umbraco that matter here:

- **The block picker:** "Add content" in a block list opens a dialog with everything that can be added, and a pick
  puts it where the button was.
- **Tabs at the top of a screen:** a workspace's views, as a form already has (Design, Settings, Actions and so on),
  and a section's views, as Umbraco's Packages section has.
- **A menu on the thing itself** for what can be done to it.
- **The product's own Create dialog** ("+" beside Forms): five choices with one line each, then the screen for the
  one chosen. It is "pick first, details after" already, and the model for the rest.

## What the second look changed

1. **"Add page" and "New page" are not the same.** One adds an empty page at the end; the other cuts the page in two
   where you are. So there are two things under three entries, and the names do not tell them apart.
2. **The inspector's empty column was decided on purpose,** so that the form never jumps sideways when a field is
   clicked. The column should stay; what is wrong is that it has nothing to say.
3. **The left side's trouble is the order more than the number.** The seventeen items come after the tree of forms. At
   900 px height with about twenty forms, not one of them is on the screen, the Inbox with its count of new entries
   included. Five items in the same place would be out of sight just the same.
4. **Taking the field list away has a price:** one more click for every field added, and no dragging of a type from
   the list onto the form. It is named under "What it costs".
5. **A form has six views, not seven,** and what can be done to a field is not offered twice in general: only "Change
   type" is.

## Six rules for the pass

1. **One way, one place.** Every command has one home. A second way stays only for the keyboard.
2. **Pick first, details after.** A screen shows what there is to choose from; the details come once something is
   chosen.
3. **The form gets the room.** In the designer the form is the widest thing on the screen, and no column stands
   empty.
4. **What is fixed never gives way to what grows.** A list that grows with the site (forms, entries) never pushes a
   fixed item out of sight.
5. **One level of folding.** A screen folds things away once (a tab, or a section that opens), never a section inside
   a section.
6. **Umbraco's pattern before an own one.**

## What was seen

### A. The designer: adding things

**A1. Three entries for two ways to start a page.**

- **Now:** "Add page" in the row of pages adds an empty page at the end. "New page" in the field list and "Start a new
  page after this" in a field's menu both cut the page in two at that point.
- **Why:** cutting a long page was wanted beside adding an empty one.
- **Direction:** "Add page" in the row of pages only. The cut stays in the field's menu alone, under a name that says
  what it does. "New page" leaves the field list: a page is not a field.

**Became:** step 22.1 (ADR-0256): "+" in the row of pages adds an empty page; "Move what follows to a new page" in a
field's "…" cuts the page; the field list is gone.

**A2. "Add field" opens nothing.**

- **Now:** with the field list open beside the form, "Add field" on a page and "Add a field here" between two fields
  only put the cursor into the list's search. On a narrow window the list does open over the form.
- **Why:** the list is already on the screen there, so the click was pointed at it.
- **Direction:** both open a picker at every width: search, the field types by group, the site's saved groups, "Paste
  questions". A pick puts the field where the click was. Half of it exists: it is what a narrow window does today.

**Became:** step 22.1 (ADR-0256): "Add field" and every insert point open the field picker at every width.

**A3. The field list is a column of its own.**

- **Now:** every field type is on the screen all the time, with "Recently used" repeating some of them above.
- **Why:** one click adds a field after the selected one, and a type can be dragged onto the form.
- **Direction:** with A2 the picker is the way to add, and the column goes. Dragging stays for moving fields and
  pages.

**Became:** step 22.1 (ADR-0256): the column is gone; dragging moves fields, groups and pages.

### B. The designer: pages

**B1. A page has no handle.**

- **Now:** a page is selected by clicking its empty background, and only then a bar offers Duplicate and Delete. Its
  name is changed behind the same hidden click. The page's tab only shows the page.
- **Direction:** the tab is the page's handle, with a menu: Rename, Duplicate, Delete (not offered for a form's only
  page). And see C1: with no field selected, the inspector shows the open page.

**Became:** step 22.1 (ADR-0256): the open page's tab holds Rename, Duplicate and Delete; with nothing selected the
inspector shows the open page.

**B2. "After sending" is shown in one place and changed in another.**

- **Now:** it stands at the end of the row of pages and shows what visitors get, read-only; "Change" leads to the
  form's Settings, which hold the same thing as a group.
- **Why:** the row of pages is the visitor's way through the form, and its end belongs to it.
- **Direction:** one home. Since the row already shows it, it is edited there, and the Settings lose a group.

**Became:** step 22.1 (ADR-0256): After sending's outcomes are edited in the designer; a form's Settings have six
groups.

### C. The designer: the layout

**C1. Four columns, and the form has the least room.**

- **Now:** at 1440 px: tree 300, field list 265, form 555, inspector 320. The form has 39 % of the width. With
  "Desktop" chosen it says "Narrowed to fit": on a common laptop the designer cannot show the desktop layout at all.
  The inspector reads "Nothing selected" until a field is clicked.
- **Why:** the inspector's column is always there so the form never jumps when a field is clicked.
- **Direction:** three columns. The field list goes (A3), which gives the form 820 px. The inspector's column stays
  and is never empty: with no field selected it shows the open page (B1).

**Became:** step 22.1 (ADR-0256): three columns, the form 819 px of 1440 (57 %).

```text
today   | tree 300 | field list 265 | form 555 | inspector 320 |
after   | tree 300 | form 820                  | inspector 320 |
```

**C2. Up to three rows of controls above the form.**

- **Now:** the widths, then language, the form check and a menu, then the row of pages.
- **Direction:** one row: the pages on the left, the rest on the right. To check: whether "Desktop" should be offered
  where it cannot be shown, since Preview has the same switch with room for it.

**Became:** step 22.1 (ADR-0256): one row, a second only where the pages need the room; Desktop stays offered, with
"Narrowed to fit" where the canvas has less room.

**C3. "Change type" is offered twice at once:** in the inspector's header and in the field's menu. One of them; the
header, beside the type's name.

**Became:** step 22.1 (ADR-0256): in the inspector's header alone.

**C4. Two green buttons, one above the other.**

- **Now:** Preview, Save draft and Publish stay at the bottom on every view of a form. On Entries, with an entry open,
  the entry's own bar (Forward, Mark as spam, Reject, **Approve**) sits right above it (**Publish**).
- **Why:** Umbraco's document workspace keeps its buttons on every view too.
- **Direction:** the form's buttons only on the views that change the form (Design, Settings, Actions).

**Became:** step 22.3 (ADR-0258): Preview, Save draft and Publish… on Design, Settings and Actions only; no footer on
Entries, Insights and History.

### D. The section's left side

**D1. The fixed items come after the tree, and there are seventeen.**

- **Now:** five headings with seventeen items (Inbox, Spam; Option lists, Themes, Email layouts, Templates; General,
  Email, Spam protection, Files, Access, Integrations; People, Retention, Data collection; Deliveries, Audit log), all
  below the tree of forms. See point 3 above: with about twenty forms none is on the screen.
- **Why:** each screen is one click away this way, and the tree sits under Umbraco's own header, where its users look
  for it.
- **Direction:** a few fixed items first, the tree after them. Each item is one screen with tabs along its top.
  Nothing is taken away; it moves one level down. A tab shows only to who may use it, as an item does today.

**Became:** step 22.2 (ADR-0257): Inbox, Library, Settings, Privacy and Activity before the tree, each a screen with
its tabs along its top.

```text
today                              after
Forms            the tree, grows   Inbox            Inbox · Spam
Entries          2 items           Library          Option lists · Themes · Email layouts · Templates
Library          4 items           Settings         General · Email · Spam protection · Files · Access · Integrations
Settings         6 items           Privacy          People · Retention · Data collection
Privacy          3 items           Activity         Deliveries · Audit log
Activity         2 items           Forms            the tree, grows
```

**D2. "Settings" names three things:** Umbraco's own section, the Form Builder's item on the left, and a view of
every form. To check: a name for each that says whose settings they are.

**Became:** kept (ADR-0257): the owner's own word for the place is "settings", a form's view stands inside its form,
and running text names the place with it ("Settings › Email", "the form's Settings view").

### E. A form's own views

**E1. A form's Settings fold twice.**

- **Now:** one long page of seven groups that open and close, some with "More options" inside.
- **Direction:** as D1 does it for the site: pick a group, see that group. Six groups if "After sending" moves (B2).

**Became:** step 22.3 (ADR-0258): the six groups as a list, the group picked beside it; Submission's "More options"
is the one fold left.

**E2. A new action shows everything at once.**

- **Now:** "Add action" opens one sheet with seven kinds to choose from, four moments to run at, each with its
  explanation, and all the settings of the kind below.
- **Direction:** pick the kind first, from a list with a line each, as the Create dialog does. The sheet then holds
  that kind's settings only.

**Became:** step 22.3 (ADR-0258): Add action lists the kinds, then the kind's sheet; "When it runs" is one choice.

**E3. Restoring a version has two doors:** the History view, and "Restore an earlier version" in the form's menu. One
of them; the view, where the versions are.

**Became:** step 22.3 (ADR-0258): the History view alone.

**E4. Six tabs** (Design, Settings, Actions, Entries, Insights, History) is many beside the two to four of Umbraco's
own screens. Each is a clear word, and they already stand in a good order: what builds the form first, what it
brought in after. They stay as they are.

**Became:** kept, as the direction says.

### F. The other screens

Looked at and found calm: the Overview, the Create dialog, Preview, the publish dialog, the form check, the rule
builder, the theme and email layout editors, Access.

Small things seen on the way:

- **The inspector folds twice,** like a form's Settings: sections that open, with "More options" inside.
  **Became:** step 22.3 (ADR-0258): a section folds nothing inside it; and in 22.4 a closed section is as tall as its
  header (ADR-0259).
- **The entries list has a column "Actions" that says "none" in nearly every row.** It could speak only when
  something went wrong.
  **Became:** step 22.4 (ADR-0259): it speaks only where something did not go out.

Not looked at yet, one pass each with the same four questions: an entry's screen, exports, the option list editor,
People, Retention, Deliveries, the Audit log, Insights.

1. Is there a second way to do this?
2. What could wait until something is picked?
3. What is said twice?
4. Does the empty state say what to do next?

**Became:** step 22.4 (ADR-0259): each screen asked, each finding fixed or kept with its reason in
[review-22.md](review-22.md).

## Four steps

They are steps 22.1 to 22.4 of [ROADMAP.md](../../ROADMAP.md), where each has its full list and its checks. Each is
one step with as many commits as it needs: a wireframe first, then the decision record, then the code in English and
German on `main`, merged into the Umbraco 18 line.

1. **The designer (A, B, C1 to C3).** The path every editor walks daily, and where the owner stumbled first.
   Roughly done when: a field is added through the picker from "Add field" and from between two fields, by mouse and
   by keyboard; a page is added in one place and renamed, duplicated and deleted at its tab; "After sending" is
   edited where it is shown; the inspector is never empty; at 1440 px the form has more than half the width under one
   row of controls.
2. **The left side (D).** The largest change to how the section is laid out.
   Roughly done when: at 900 px height with fifty forms every fixed item is on the screen; Inbox, Library, Settings,
   Privacy and Activity are each one screen with tabs that follow the permissions; every link that led to an old
   screen (the Overview's checklist, the health lines, "Fix it") opens the right tab.
3. **A form's views (C4, E).** Roughly done when: a form's Settings show one group at a time; an action's kind is
   picked before its settings; the form's buttons show only on the views that change it; a version is restored from
   one place.
4. **The other screens (F), and everything that describes the section.** The pass over what was not looked at yet,
   then the editor guide, the screenshots (the Marketplace's included), the copy guide and the top tasks.

After each step the walk runs again and is compared with the one before.

## How to tell that it got simpler

| | Today | After |
| --- | --- | --- |
| Columns in the designer at 1440 px | 4 | 3 (measured, 22.1) |
| The form's share of the width at 1440 px | 39 % | more than half (measured, 22.1: 57 %, 819 px) |
| Rows of controls above the form at 1440 px | 3 | 1 (measured, 22.1: one for up to two pages on a site of two languages, two from three) |
| Entries that start a page | 3 | 2, each named for what it does (22.1: "+", and "Move what follows to a new page") |
| Fixed items on the left at 900 px height, with twenty forms | 0 of 17 | 5 of 5 (measured, 22.4: with fifty forms in the tree, at 1440 by 900 px, each place lies inside the window; `places.spec.ts`) |
| Clicks to add a field at the end of a page | 1 | 2 (22.1) |
| Clicks in each of the top tasks ([top-tasks.md](top-tasks.md)) | counted there | none grows by more than one (counted, 22.4: ten tasks one more for a later tab, 22.2; designer task 11 one more for the kinds, 22.3; designer task 7 one more, 22.1; none in 22.4; the first five of each persona clicked on the build by `top-tasks.spec.ts`) |
| Words on each screen | the last walk's counts | no screen grows |

## What it costs

- **One more click for every field added,** and no dragging of a type from the list onto the form. Against it: the
  picker opens with its search ready, "Add field" stands right under the field just added, and "Paste questions" and
  Duplicate stay for many fields at once. Should it be missed, the list can come back as something to pin open.
- **Earlier decisions are turned round:** the sidebar with every screen as an item, the field list beside the form,
  the form's buttons on every view. Each needs its record, with the old reason answered.
- **One screen for all site settings** has to say what Save saves: the tab that is open, or every tab.
- **Much of what surrounds the screens follows:** the browser tests that click through the field list and the left
  side, the guides, the screenshots, the top tasks.
- **It is done before the repository goes public,** as the owner decided: afterwards every moved screen is something
  an editor has to learn again.

## Kept on purpose

- **No function goes.** Things move behind a pick; they are not removed.
- **Dragging** to move fields and pages.
- **The keyboard paths** and the shortcuts, and WCAG 2.2 AA.
- **The inspector's column,** so the form does not jump.
- **The screens found calm** under F.

## Decided by the owner

On 2026-11-16, each as this draft proposed it ([ADR-0254](../decisions/0254-a-simpler-backoffice.md)):

1. **The field list (A3) goes** in favour of the picker. A pin to hold it open comes only if it is missed.
2. **The left side (D1):** the fixed items come before the tree, cut into Inbox, Library, Settings, Privacy and
   Activity.
3. **"After sending" (B2)** is edited in the designer, where it is shown.
4. **The form's buttons (C4)** show only on the views that change the form, though Umbraco keeps its own
   everywhere.
5. **When:** before the repository goes public.
