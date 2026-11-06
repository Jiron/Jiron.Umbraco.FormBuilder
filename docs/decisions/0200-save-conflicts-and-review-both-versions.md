# 0200. Save conflicts, Review both versions, and the guard against leaving unsaved

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.3c; builds on [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the
  concurrency version), [ADR-0045](0045-versions-comparison-and-draft-cleanup.md) (the comparison) and
  [ADR-0196](0196-the-history-view-and-the-comparison-in-words.md) (the comparison in words); IA 5.7, copy.md

## Context

Every change to a form raises its concurrency version, and a save sends the version it read: the server refuses a stale
one with `409 concurrency_conflict` (ADR-0002). The backoffice said that refusal in a toast, "Someone changed this after
you opened it. Reload the page, then try again", and then read the form again to take the version a rename before it
had given. That read took the newest version too, so a second Save went through and replaced the other person's draft
unseen. A save also renamed the form before it saved the draft, so a refused draft could leave a rename half done.

IA 5.7 asks for more: the save conflict says who saved and when, after the user opened the form, and that the user's
changes are still here, with **Review both versions**, where each difference is taken from either side. Leaving with
unsaved changes asks first, for a change on the canvas and in a view as for the name, and when the tab is closed.

The design skills shaped it (ADR-0019, ADR-0020): Operate mode inside Umbraco's design system, for a designer who
finds that someone else saved the same form and wants to keep both people's work without fear. The owner answers no
questions during the run (CLAUDE.md rule 8), so IA 5.7 and copy.md answer the discovery questions, and the rest is
decided here.

## Options

**Detecting a conflict**

1. Only the server's `409`: any change to the form, a move to another folder or a publish too, would ask for a review
   of drafts nobody changed.
2. **The server's `409`, and before sending, the stored draft and name as the save reads them:** a save reads the
   stored draft first anyway, so a draft saved, or a name changed where the user changed it too, since the user read the
   form is a conflict without sending anything; any other change is none, and the save goes on from the version it
   gave.

**After a conflict**

1. Read the form again and let the next Save go through: what happened before, and what overwrote.
2. Discard the user's changes and show the other draft: loses work.
3. **Keep the version the user read, so the server refuses every later save too, refuse it in the workspace as well,
   and offer the review;** the conflict ends when the review saves, or when the form is read again.

**Where the merge happens**

1. On the server, from the user's choices: a new endpoint that writes the merged draft.
2. **In the backoffice, key by key, from the server's comparison:** the server compares a draft not saved yet with a
   version (one new endpoint, which 10.3d's publish dialog also needs), and the backoffice merges and saves the result
   as any draft is saved, with the checks a save has.

**What a difference is**

1. Each changed property: a field half from one side, half from the other could contradict itself.
2. Each element's change, its properties and its place together: one side's move and the other's new label of the same
   field would be one choice, so taking either loses the other.
3. **An element's own properties, its place, or the element added or removed with everything it holds, each one
   choice:** a settings group whole; a rule or a check whole, as History shows one; the form's name.

## Decision

Option 2, 3, 2 and 3.

- **A save** (`form-detail.data-source.ts`) reads the stored draft. A draft version other than the one the user read,
  or a name other than the one read where the user renamed the form too, is a conflict, and nothing is sent; a name
  goes only where the user changed it, so someone else's rename stays. Otherwise it saves the draft first, with the
  version the read gave, then renames the form with the version the draft's save gave, so a refused draft leaves the
  name as it was; a name refused after the draft went in is said, and the form as saved is read. A `409` from either is
  a conflict too, which the data source answers without a toast. The read after a refused save is gone.
- **The workspace** (`form-workspace.context.ts`) learns of a conflict from the data source, which sends its host
  element `jfb:save-conflict` as it answers: Umbraco's workspace replaces a save's error with its own, so no message
  gets through. The form stays as it was read, so every later save is refused the same way until both versions are
  reviewed, and the dialog shows each time.
- **The dialog** is Umbraco's: "Your draft wasn't saved", then who saved the form's draft and when, from the History,
  where that draft is newer than the one the user started from ("Ana Novak saved this form at 14:02, after you opened
  it. Review both versions before you save. Your changes are still here."), or "Someone changed this form after you
  opened it." where it isn't (a rename); Cancel and **Review both versions**. One dialog or review at a time for a
  workspace: a second save meanwhile adds none, and the review's own save, refused again, says itself again.
- **Review both versions** (`designer/conflict/`), Umbraco's large sidebar: each difference under what it is about
  (`Field "Email"`, `Field "Email": place`, `Settings: Appearance`, `The form`), each side's values side by side under
  the versions' names ("Ana Novak's version", "Your version"), which head the two columns as the choice, its place
  where it moved ("After "Name"", "First in "About you""), and for an element added or removed what it holds. Compared
  with the draft both started from, a difference only one side made starts on that side; one both made is marked
  "Changed in both" and waits; one neither comparison names, or any where the draft both started from is gone, waits
  unmarked. Save says once how many still wait, marks each with "Choose a version", which its choice is described by,
  and goes to the first. Save merges and saves; Cancel keeps everything as it is. A draft changed while the versions
  were read is compared again, and a form left meanwhile is left alone.
- **The comparison:** `POST form/{key}/versions/{number}/compare` (`design` on the form) compares a definition not saved
  yet with a version and answers the changes, as the version comparison does, and the definition as a save would store
  it. The review asks it twice (the draft saved meanwhile and the user's, the draft both started from and the user's)
  and reads the version comparison once.
- **The merge** (`review-model.ts`) starts from the draft saved meanwhile and takes the user's side where chosen, key by
  key: settings groups, then elements' own properties, then elements added (each with what it holds, but what the merge
  already holds), then places, then elements removed. An element the user's version keeps elsewhere leaves a container
  the user removed for where the user's version has it; where that place isn't in the merge, the container stays,
  holding only what the user's version keeps, so nothing either side keeps is lost. The draft saved meanwhile becomes
  the one the user's changes start from, and the merged draft is a step the canvas's Undo takes back to the user's own
  version ("Undone: Merged both versions"); it is saved with the version read with it.
- **Problems a merge makes**, two show rules on one field or two fields with one alias, are a draft's problems as an
  edit's would be: the form check shows them, and they block publishing, not saving.
- **Leaving unsaved:** Umbraco's detail workspace already asks before leaving the form's route for any change in the
  workspace's data, the canvas included ("Discard unsaved changes", with Stay and Discard changes), and asks the
  browser's own question before the tab is closed or reloaded, from Umbraco 17.6.0 on (read in its source at that
  release and in the backoffice the sites serve). The workspace adds nothing: a handler of its own only repeated
  Umbraco's, which the tamper round showed, so it went. The journey checks the question still comes.

## Consequences

- A save never replaces someone else's draft unseen; the draft saved meanwhile stays in the History, the review's Save
  adds the merged one, and Undo gives the user's own version back until they leave the form.
- A move to another folder, an access change or a publish since the user opened the form is no conflict.
- The review's words are History's: a property is named as History names it, in English and German.
- `POST form/{key}/versions/{number}/compare` has its rows in the permission matrix and the OpenAPI snapshot; 10.3d's
  publish dialog compares the draft not saved yet with the live version through it.
- A publish with changes not saved yet saves them first, so a conflict stops it there, and the publish waits for the
  review.
