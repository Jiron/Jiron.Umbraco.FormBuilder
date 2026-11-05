# 0185. The outcomes editor: an ordered list in the Settings view, a sidebar per outcome, its rule in the builder

- Status: accepted
- Date: 2026-11-05
- Roadmap step: 10.1b (10.1c adds the outcome's live preview); builds on [ADR-0184](0184-the-form-settings-view.md)
  (the Settings view), [ADR-0181](0181-the-form-check-in-the-designer.md) (Fix it), [ADR-0175](0175-the-rules-builder.md)
  (the rules builder), [ADR-0016](0016-designer-interaction.md) (moving with the pointer and the keyboard) and IA 5.2

## Context

After sending (§4.3.4) holds the form's ordered **outcomes**: visitors get the first whose `use` rule matches, or that
has none, and the built-in thank-you message where none applies. An outcome is a message (rich text with merge tags,
perhaps with a copy of the answers), a redirect (a content page or an address), or a message and then a redirect after
some seconds. IA 5.2 and the wireframe (`docs/ux/wireframes/form-settings.html`, states `outcome` and
`outcome-deleted`) draw the group open, second on the page, as a list with move handles and "Add outcome", and one
outcome edited in a sidebar with Delete outcome, Cancel and Update. What 10.1b's survey found:

1. **Nothing in the backoffice reads or writes an outcome.** The client passes `outcomes` through untyped; the canvas's
   After sending shows the first outcome read-only with "Change", which only moves to the Settings view.
2. **The server holds the rules of an outcome** (`DefinitionStructureChecks.Rules.cs`): a redirect outcome shows no
   message, no answers and leaves at once; a redirect leads to a page or an address, never both and never neither; an
   address keeps the publish rule's form (`RedirectUrls`); the delay is 0 to 600 seconds; at most one `use` rule. These
   run in the form check and at publishing, not when a draft is saved: a draft may hold an outcome that is not ready.
3. **The rules builder edits an element's rule only:** its origin is always a page, group or field, and its effects lack
   `use`.
4. **The form check leads to the view** for any problem under `$.outcomes`, by its path, which is an index into the
   definition the check was made of.

## Options

1. **The outcomes in the inspector of the canvas's After sending:** close to the preview, but the inspector edits
   elements, and IA 5.2 puts the outcomes in the Settings view with the form's other settings.
2. **Each outcome edited in place in the list,** as the option editor's rows: no sidebar, but an outcome holds a rich
   text, a page picker and a rule, too much for a row, and the wireframe gives it a sidebar.
3. **A list in the Settings view and a sidebar per outcome** that writes the outcome to the draft on Update, its rule in
   the rules builder over it, its problems from the form check of the draft as Update would make it.

## Decision

Option 3. The shape brief, decided here (the owner answers no questions, CLAUDE.md rule 8):

- **Job and audience:** an editor who designs the form decides what a visitor gets after sending, often once per form
  and again when a campaign needs another page; Operate mode, inside Umbraco's own design and the Settings view's.
- **Outcome:** the right outcome for each answer, said in plain words in the list, with nothing a visitor could never
  get left unremarked.
- **Untouched:** the other groups, the canvas's After sending as it shows the outcome (only its Change leads further),
  and the runtime.

1. **The group.** After sending is the view's second group, open by default (IA 5.2). Its closed line names the one
   outcome, or the number of outcomes, or the default message. It lives in its own element (`jfb-outcome-list`), so the
   view does not grow by a list's logic, and its sidebar in its own modal (`Jiron.FormBuilder.Modal.Outcome`), loaded
   when it opens.
2. **The list** is an ordered list. Each row: a move handle ("Move <name>", ADR-0016: the pointer by the handle,
   Alt+Up and Alt+Down, the new place said, the focus kept), the kind's icon, the outcome's name as the button that opens
   its editor, and a second line: the rule in words ("Used when Topic is Press"), or "Otherwise" for the first outcome
   for every visitor, "Used for every visitor" where that is the first outcome, or "Never used: an outcome above is used
   for every visitor" for any outcome after that one, which no visitor can get. An outcome is for every visitor where it
   has no rule, its rule is off, or its rule is "all of" nothing. The form check's problems in an outcome count on its
   row, as on a closed group's line: in the error colour where an error is among them, else the warning colour.
   Under the list: where no outcome is for every visitor, "Otherwise, visitors get the default message: <the visitor's
   words>" ("Visitors get the default message: ..." for none), then the check's problems with the list itself (too many
   outcomes), which describe "Add outcome" under them. At the most outcomes a form can have (`DefinitionLimits`, 50 by
   default, which the settings choices now carry) a line says so in place of Add outcome.
3. **An outcome's name** says what visitors get: `Message: "<its text>"` (the default thank-you message's words where it
   has none), `Redirect to /press/thanks` or `Redirect to <the page's name>` (read from Umbraco, "a page of the site"
   until it comes), `Message, then redirect to ...`. A text past 60 characters is cut at its last word that fits,
   with an ellipsis, so the row wraps a name and never cuts it.
4. **The sidebar** ("Outcome 2", "New outcome") holds, in order: **What visitors get** (A message, A redirect, A
   message and then a redirect), **Message** (the rich text editor with Insert merge tag, place `outcome`, in the
   language the view shows), **Show a copy of the answers** (a switch; message kinds), **Leads to** (A page of this
   site, with Umbraco's document picker, or An address, a line with Insert merge tag, place `redirect`), **After** a
   number of seconds (the message then redirect), **Place in the list** ("2 of 3"; only where the list has another
   place), and **Rule** (its words or "Used when no earlier outcome applies", "Never used: ..." where an outcome above is
   for every visitor, then Add a rule or Change rule, a native button its words describe). The place is how a pointer
   moves an outcome without dragging (WCAG 2.5.7). Changing the kind keeps what the other kinds hold until the sidebar
   closes, and Update writes only what the chosen kind uses, as the server keeps it (no message or answers on a
   redirect, no delay but on a message then redirect).
5. **Problems in the sidebar.** The sidebar runs its own `FormChecker` with the workspace's ask over the draft as Update
   would make it, 600 ms after its last change, and shows the problems at its outcome's paths under their setting: those
   the outcome had when the sidebar opened at once, others once their setting is left or Update is pressed. An answer
   about the outcome as it was before the last change shows only the problems of boxes still drawn as themselves, so a
   redirect's problem never lands on the kind after a switch to a message. A problem in the message of another language
   says the language ("German: ...") and does not stop Update: the sidebar edits one language. Update keeps the outcome
   once that check finds no error in it that the sidebar can fix, or gives no answer; warnings never stop it, and a
   second press while the check is asked keeps nothing twice. What a save would refuse is refused at the box and said
   once it is left: a delay that is no whole number from 0 to 600, an address over 2,000 characters; a refused box that
   another kind takes away no longer stops Update.
6. **Add outcome** opens the sidebar for a new message outcome, primary **Add**. Until its place is chosen the new
   outcome goes at the end, except that one with a rule goes before the first outcome for every visitor, after which it
   would never be used; the live region says where it went, and where Update moved an outcome. **Delete outcome** acts at
   once, closes the sidebar and offers Undo in the toast (copy.md), the focus on the next row's name, else the last, else
   Add outcome; the toast's Undo puts the focus on the outcome back in the list. On a published form, Add and Update say
   in a toast that visitors get it once the form is published (copy.md, success); an Update that changes nothing says
   nothing and makes no step. Each of Add, Update, Delete and a move is one step of Undo. The rows are kept by their
   outcome's key, so the focus stays on its outcome as the list changes.
7. **The outcome's rule** is edited in the rules builder over the sidebar: its data names the outcome by its number at
   the place the sidebar shows, the builder then shows "Use outcome 2 when" ("Use the new outcome when") in place of its
   effect and element choices, its effect is `use`, an existing `use` rule opens as itself, and every field and context
   of the form is a subject, a value that stays on the server and a field a member's value stands in for too: the rule
   is the server's, once the answers are all in. Nothing can wait on itself. Its Done or Delete changes the sidebar's
   outcome, which Update then keeps.
8. **Fix it, the canvas and the rules overview.** A view Fix it may now carry the outcome's key, which the check reads
   from the definition it checked; the Settings view's Fix it of an outcome opens After sending and the outcome's sidebar
   with the focus on the setting the path names (`message`, `showAnswers`, `redirect`, `redirect.content`,
   `redirect.url`, `redirect.delaySeconds`, `rules`), and a path at the list itself (`$.outcomes`, the limit) on Add
   outcome. The canvas's Change and the rules overview's outcome rows hand the outcome on the same way, without a
   problem, through `followFix`.
9. **Not in this step:** the live preview with a sample entry, which needs a server endpoint of its own, is 10.1c.

## Consequences

- An outcome's settings are drawn by the sidebar, not by the inspector's schema editors: its kinds and their rules are
  the definition's, which no field type's schema describes. The sidebar reuses their pieces: the rich text editor, the
  document picker, the merge-tag picker and the box styles.
- The rules builder learns an origin that is no element; its tests hold both.
- The client's merge-tag places gain `outcome` and `redirect`, which the server already had.
- The view's opened groups, kept per browser, show After sending open only where the browser kept no choice yet.
- The settings choices gain `limits.outcomes` (OpenAPI snapshot updated), so a site that raises the limit raises it in
  the list too.
- The option editor's rows move only by dragging or Alt and the arrows; a pointer way without dragging (WCAG 2.5.7) is
  owed there as here; the phase review (10.4) takes it with the designer's other open findings.
