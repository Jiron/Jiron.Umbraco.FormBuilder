# 0188. The Actions view: a list per trigger, an action's sidebar, and its words loaded with it

- Status: accepted
- Date: 2026-11-05
- Roadmap step: 10.2a (10.2b adds the email editor and Test, 10.2c the other editors, 10.2d the last run); builds on
  [ADR-0185](0185-the-outcomes-editor.md) (the outcomes editor), [ADR-0158](0158-the-action-framework.md) (the action
  types), [ADR-0181](0181-the-form-check-in-the-designer.md) (Fix it), [ADR-0166](0166-the-form-library.md)
  (`?action=`), [ADR-0016](0016-designer-interaction.md) (moving) and IA 5.3

## Context

A form's actions (§4.4) are one ordered list in the definition: each has a type, a name, a trigger (Submitted,
Approved, Rejected or Verified), whether it is used, whether a failure stops the rest, at most one `run` rule and the
type's settings. The actions one event queues run in the order shown. IA 5.3 groups them by trigger as cards, each
with a move handle, a switch, Test, a "…" menu and its last run; Add action opens a sidebar whose first setting, What
happens, offers seven choices. What the survey found:

1. **The action types come with their settings' schema** (`GET action-type`), which the inspector's setting editors
   already draw for field types and captcha providers, and with presets. Email has two (the team, the visitor);
   webhook has Slack and Teams but no plain one, so a plain webhook would have no choice of its own.
2. **The backoffice's names differ from IA 5.3 and copy.md**: "Notify the team" for Email the team, "Confirm to the
   sender" for Confirm to the visitor, "Message to Slack" for Slack, "Set state" for Change the entry's state.
3. **Every area's words load when the backoffice starts**, which the start budget counts (61.54 KB of 64 after 10.1c);
   the actions' words would take most of what is left, before 10.2b to 10.2d add theirs.
4. **No e2e persona has `design` without `manageActions`,** which the read-only view needs; the wireframe draws it in
   a restricted folder, whose grants the test host seeds for one group each.
5. **Fix it of an action** leads to the Actions view without the action's key, and `?action=` (ADR-0166) is read by
   nothing.

## Options

For the words:

1. **In the start tables,** as every area's.
2. **Loaded with the view:** the Actions view's module registers its words with Umbraco's localization manager,
   English and the user's language, before it draws; the start tables stay as they are.

For What happens:

1. **Every type and every preset as a choice:** eight, a blank email among them, which nobody needs.
2. **Every preset, and a type without presets as itself:** webhook gets a plain preset, its first, so the choices are
   IA 5.3's seven, and a site's own type or preset joins them by the same rule.

For an action of another type than it has:

1. **What happens on every action:** a choice changed later replaces settings an editor wrote and tested.
2. **What happens for a new action only;** an action keeps its type, and another type is a new action.

For the read-only state:

1. **A restricted folder for the designers** with `design` alone: every designer journey's tree gains a folder.
2. **A persona of its own,** a layout designer with `design` alone, which the data-driven access checks cover.

## Decision

**Option 2 in each.**

- **The list.** The view shows the four triggers in IA's order, each a section with its heading and when it runs.
  Rejected without manual approval and Verified without email verification show one line with a link to the setting
  (the Settings view's Fix it, which opens it with the focus on it), and any actions they hold under it, which do not
  run until it is on. Approved says, without manual approval, that it runs as each entry comes in.
- **A card** is the outcome row grown by two controls: its move handle (shown on hover and focus where a pointer
  hovers, always on touch), the type's icon, the name as the button that opens its sidebar, a line of what it does
  ("To sales@example.com", "Webhook to crm.example.com"; the type's name where a site's own type says nothing), the
  rule's words where it has one and the form check's problems counted on that line; then the switch, named "Use
  <name>", and "…" (More actions for <name>): Duplicate, Move to another trigger…, Delete. Duplicate puts a copy with a
  new key and "<name> (copy)" right after it; Move to another trigger… opens the sidebar with the focus on the trigger;
  Delete takes it away with Undo in the toast. A card moves within its trigger by its handle or Alt+Up and Alt+Down, and
  the other triggers' actions keep their places in the definition's one list. The switch says its own state; a later
  change closes the toast, whose Undo would take back that one; Undo and Redo keep the focus on the card it was on, or
  put it on Add action where the card is gone; at the most actions a form can have, "…" offers no Duplicate.
- **Add action** opens the sidebar ready to email the team, the most common action. What happens offers every preset
  of every type and a type without presets as itself, in the registry's order: Email the team, Confirm to the
  visitor, Webhook, Slack, Microsoft Teams, Change the entry's state, Create or update content. Choosing another
  replaces the settings below it, and the name follows the choice until it is typed. Then the name, the trigger
  (Submitted unless chosen), the place in the list among that trigger's actions (as an outcome's, a pointer's way to
  order them without dragging, WCAG 2.5.7), the type's settings as the inspector draws them (Essentials, then More
  options), whether a failure stops the rest, and the rule, which the rules builder edits as an outcome's: its effect is `run`, its
  first line "Run Email the team when", and every field and context a subject. Footer: Cancel and Add, or Update
  with Delete action; Test comes with 10.2b. An existing action keeps its type, said under the headline. The sidebar
  asks the form check about the draft as Add or Update would make it (`keptIn`, which the view keeps it by too): an
  action of the form shows the problems it has at once, a new one's show once their box is left or Add is pressed; a
  problem of a setting whose editor comes later shows under What happens, by the setting's name, so the button is never
  held back without a word; and a value typed in a setting that another choice took away holds nothing back.
- **The words** of the view and its sidebar load with the view: `lang/actions.en.ts` and `actions.de.ts`, registered
  through Umbraco's localization manager (which merges a language's words) before the view draws, so no key ever
  shows; `lang.test.ts` holds them to the same keys. The names of the types and presets stay in the start tables, as
  the action types' `#` keys need them anywhere, and take IA 5.3's words.
- **Read-only:** without `manageActions` the view has no Add action, handles, switches or "…", a card's sidebar shows
  its settings without boxes to change (the same settings the editable sidebar draws, so a setting whose editor comes
  later shows to neither), and one line at the top says that changing the actions needs the Manage
  actions permission for this form. It names no folder: the grant that decides can be global, a folder's or a
  restricted folder's, which the backoffice does not know, and a folder that cannot fix it would mislead. The e2e host
  seeds a persona with `design` alone.
- **Fix it and `?action=`.** The form check's Fix it of an action carries its key, as an outcome's does, and the
  Actions view takes it: the action's sidebar on the setting the path names, or its card with the focus for a path at
  the action itself or the list. `?action=<key>` on a form's address is read before the router drops the query, and
  hands the Actions view a Fix it at that action, which shows its card with the focus on its name. On a workspace
  that opens with the link, Umbraco's name box takes the focus and looks 100 ms later whether it holds it, taking it
  again where its first try was still on its way; so the Fix it waits until the box has held the focus past that look
  (`name-focus.ts`: 250 ms, at most five seconds in all). It gives up where the user pressed a
  key or a pointer first, which is their move, and leads nowhere for who may not design the form, who has no Actions
  view.

## Consequences

- `designer/actions/` holds the model, the list, the view and the sidebar; the rules builder takes an action as it
  takes an outcome; `FIXING_VIEWS` holds the Actions view.
- The webhook type's presets start with a plain one; the integration test of the Slack and Teams presets reads them at
  their new places.
- The backoffice at start does not grow by the Actions view's words; the bundle does.
- 10.2b adds Test, on the card and in the sidebar, and the editors for recipients; 10.2c the webhook's mapping, the
  content action's document types and properties; 10.2d the last run on the card and its Details.
