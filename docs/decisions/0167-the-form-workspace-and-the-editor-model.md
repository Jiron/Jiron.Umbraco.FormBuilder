# 0167. The form workspace and the editor model: Umbraco's detail workspace, pure edits, and the engine's own cycle check

- Status: accepted
- Date: 2026-11-03
- Roadmap step: 9.3a (decided and built); 9.3b draws the canvas and the palette on it, 9.3c the moves by drag and keyboard, 9.5b the
  form check in the publish dialog

## Context

Step 9.3a builds the frame the designer works in and the model every edit goes through (`docs/ux/information-architecture.md`,
section 5; ADR-0016). Four things had to be decided:

- **The workspace.** The form needs a header with its name and state, views as tabs, a footer with Save draft and Publish…, the
  "Discard unsaved changes" question when the user leaves, and the views and actions of each user's permissions on that form.
- **The editor model.** ADR-0016 says every move goes through pure model functions that refuse a move breaking a rule; §3.1 asks to
  keep every behaviour of the owner's earlier editor (selection kept across moves, undo after a removal, refused reorders), which held
  one flat list and a single undo step for removals only.
- **"A move that breaks a rule".** The earlier editor allowed a condition only on earlier fields. §3.1 replaced that with a graph check
  that also covers pages and groups, which the form engine already runs (`order` in `@jfb/logic`, `DefinitionStructureChecks` in Core).
- **The Design view before the canvas.** 9.3a must open a field's search result with that field selected (ADR-0166), but the canvas
  comes with 9.3b.

## Options

For the workspace:

1. **Umbraco's named detail workspace** (`UmbEntityNamedDetailWorkspaceContextBase`) over a detail repository and store, as the 1.2a
   spike measured (`docs/research/umbraco-backoffice.md`, section 6). The header's name field, the views' routes and tabs, the
   footer's actions and the unsaved-changes question are Umbraco's.
2. **A workspace element of our own**, as the library's screens are (ADR-0166). Every one of the parts above would be ours to build
   and keep in step with Umbraco.

For the move check:

1. **The form engine's own ordering**, exported from `@jfb/logic` as `hasCycle`, fed by a model of the draft.
2. **A graph walk of the designer's own.** Smaller, but a second statement of what waits on what, which could drift from the engine's.

For the Design view in 9.3a:

1. **An outline** of the pages, groups and fields, each field a button that selects it, until 9.3b's canvas takes its place.
2. **The coming screen**, as the other views have; a selected field would have nowhere to show.

## Decision

**The workspace is option 1.** The context extends Umbraco's named detail workspace; its repository reads the form, and its
draft for who may design it (the draft endpoint is the designer's), and a save renames the form when its name changed and saves the
draft when its definition did, each with the concurrency version the last answer gave, so a change someone else made meanwhile is
refused and said in the user's words. A save by who may not design, or of nothing changed, sends nothing. The views and the footer's
actions sit behind a condition, `Jiron.FormBuilder.Condition.FormPermission`, that reads the user's permissions on the open form from
its answer (section 11): Design, Settings and History with `design`, Actions with `design` or `manageActions`, Entries and Insights
with `viewEntries`, Save draft and Publish… with `design`. Umbraco opens the first view a user sees, so a form opens on Design for who
may design it, otherwise on Entries. The views still to come show the coming screen, which step 13.4 checks is gone.

**The header** holds Umbraco's name field, read-only without `design` through Umbraco's own name guard, and the state chip: Draft
(never published), Published, Published (pending changes) and Unpublished (taken offline after a publish), the English in Umbraco's
own words. On a narrow window the chip shortens before the name does, with the whole state in its title. "Pending changes" is
said only to who may design the form; anyone else is told what visitors get. German uses "Entwurf", "Veröffentlicht",
"Veröffentlicht (Änderungen bereit)" and "Nicht veröffentlicht": Umbraco's German says "Entwurf" for an unpublished page, which
would make a form taken offline and a form never published read the same.

**Publish…** asks first, with an optional comment for the version, saves what is unsaved, and publishes. What changed since the live
version and the form check's result join the dialog with their steps.

**The editor model** is a set of pure functions over a state (the definition and the selected element's key): select, change,
insert, move, step (Alt+Up/Down, crossing into the neighbouring group or page at a container's end), Move to… (the end of the chosen
group), duplicate and remove. Each returns the next state or a refusal with its reason: `missing`, `unchanged`, `edge`, `wrongPlace`,
`cycle`, `notInRepeater`, `onePerForm`, `lastPage`, `aliasTaken` and, from the workspace while the field types are not read yet,
`notReady`. A change in place is checked as a move is: its alias must be free, its type one a repeatable group around it can hold and
the only one of a type a form has once, its rules must not make anything wait on itself, and a change that changes nothing is
`unchanged`. A move selects the moved element; a removal selects the element that took its
place, else the one before it, else its container, as the focus goes after a delete (ADR-0016). A duplicate gives everything it copies
a fresh key, points the copy's references at the copies (a rule of a copied field that looks at a sibling it copied, and a copied
calculation's formula at the new aliases, where the calculation language says each name stands), keeps its references to anything
outside, gives each copied field the lowest free alias and leaves former aliases behind. The definition's other properties pass
through untouched, so a save sends back what the designer does not know about. The server writes a definition in its canonical
form, which leaves out every empty list; the model reads it with them (a blank form's group without fields), and a save compares
both sides read so, so it sends nothing that only differs there.

**The undo history** keeps each change to the definition with the selection it had, a hundred steps back. Selecting alone is no step,
a new change drops what could have been redone, and a load starts it over, keeping the selection when the element is still there. A
save puts the server's copy of the saved definition in the present's place without a step, so what is unsaved compares against what
the server holds. A save that fails after its rename went through reads the form again, so the next save sends the version the
rename gave. Each step keeps what the announcement said ("Moved Phone"), so Undo and Redo can say what they took back.

**The move check is option 1.** `hasCycle` runs the engine's own `index` and `order`: what holds an element, the fields its enabled show
or hide rule looks at, and, with the calculation language, the fields its formula names. A move is refused when it makes an element
wait on itself where the draft did not before; a draft the engine can't read at all (two fields with one alias) is the form check's to
report, and the model lets such a change through. Beside it the model refuses what a repeatable group can't hold, as Core's structure
check does: another repeatable group, a type a form holds once, and the field the email check reads. Reordering inside one container
never makes a cycle, so the earlier editor's "earlier fields only" has no place here.

**The Design view in 9.3a is option 1.** It is replaced by 9.3b's canvas, palette and inspector.

## Consequences

- 9.3b and 9.3c build on `edit`, `undo` and `redo` of the context and on the model's functions; neither changes the definition any
  other way.
- The backoffice now bundles `@jfb/logic` and its calculation language for the move check. The backoffice has no size budget until 9.6
  sets one.
- The publish dialog is small until 9.5b and the version history's steps fill it.
- A save that only renames sends no draft, and one that only edits sends no rename, so neither makes a version or an audit line it
  doesn't need.
