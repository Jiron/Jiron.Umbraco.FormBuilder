# 0189. The email editor: one box of recipients, fields picked by kind, and the mail's live preview

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.2b (Test is 10.2c); builds on [ADR-0188](0188-the-actions-view.md) (an action's sidebar),
  [ADR-0187](0187-the-outcome-preview.md) (an outcome's preview), [ADR-0173](0173-rich-text-merge-tags-pickers-and-patterns-in-the-inspector.md)
  (the inspector's editors and merge tags), [ADR-0159](0159-mail-the-queue-the-layouts-and-the-limits.md) (layouts) and IA 5.3

## Context

The email action's sidebar (10.2a) draws the type's settings with the inspector's editors, which have none for four of
them: an email field's answer as a recipient (`toField`) and as the reply-to (`replyToField`), the addresses an answer
chooses (`recipientMap`), the layout (`layout`), the sensitive fields to show and the files to attach (both lists of
fields). IA 5.3 and the wireframe `email-editor.html` give the email a recipient builder ("To": chips and Add
recipient), Insert merge tag on the subject and the body, a layout, and a live preview of the mail with a sample entry
beside the settings, with a Light/Dark switch. What the survey found:

1. **Who gets the mail is four settings** of the schema: the addresses (`to`), an email field's answer (`toField`, a
   mail of its own to that address), the form's creator (`toCreator`) and the addresses an answer chooses
   (`recipientMap`: a choice field and, per option, addresses). The wireframe's "the member who sent it" and "a user
   group" are none of them.
2. **A field picker's schema says nothing of the fields it may hold:** only the type's own check does (an email field, a
   choice field, any field), so a picker would offer every field and let the check refuse most.
3. **Nothing lists the layouts** to the designer: the built-in card and letter, and the library's, which 12.2 edits.
4. **The mail is rendered in steps** the email type keeps private (the recipients, the subject and body with their
   tags, a site's Razor view) and in the outbox (the sender, the layout, the HTML and its text), which refuses a mail
   with no address. Nothing renders a draft's mail without queueing it.
5. **The layouts set dark colours** for clients that ask for them (`prefers-color-scheme`), so a dark preview shows
   what such a client shows.

## Options

For who gets it:

1. **One editor per setting,** as the inspector draws them: four boxes, and a map editor of its own.
2. **One box, "To",** whose chips are every recipient the four settings name and whose Add recipient offers each kind;
   the schema says which settings the box holds.

For the fields a picker offers:

1. **Every field,** the check refusing the wrong kind once chosen.
2. **The kind in the schema** (`picks`): an email field, a choice field, a field that holds files, a sensitive field.

For the preview:

1. **The test mail only** (10.2c): nothing to see while writing.
2. **A preview endpoint** that renders the draft's mail for the sample entry as a run renders it, queueing nothing.

## Decision

**Option 2 in each.**

- **To** is one box: its chips are the addresses, "The answer of Email address", "The person who created the form" and
  "Addresses chosen by Topic", each a row with its remove button; Add recipient (a disclosure) offers An address (a box
  that adds one or several, refused under the box where one is no address), The answer of a field (the form's email
  fields), The person who
  created the form, and Addresses chosen by an answer, which shows under the chips the choice field and, for each of
  its options, the addresses it sends to. A kind that can be there once is not offered twice. The schema says it: `to`
  names the editor `recipients`, and `toField`, `toCreator` and `recipientMap` say they are `within` it, so the sidebar
  draws them nowhere else; a site's own type may do the same. The wireframe's member and user group are no settings
  of the type and are not offered.
- **Copy to and Blind copy to** are address chips with a box that adds them (editor `addresses`), as the To box's
  addresses are; a list of addresses is no longer one a line.
- **The order** is who gets it, then what it says: To, Subject, Body, Leave out unanswered fields, Reply to, Copy to.
  Blind copy to is under More options, with the sender, the layout and the view: few mails need it, and a blind copy
  the team does not see is better looked for than met.
- **Another field for Addresses chosen by** keeps the addresses typed for the one before while the sidebar is open,
  and choosing it again brings them back: the options differ, so they do not carry over, but a wrong pick costs
  nothing.
- **A field picker** offers the draft's fields of its kind, by their labels in the language shown, `picks` naming the
  kind: `email` (fields of the email type), `choice` (fields with options), `file` (fields that store files),
  `sensitive` (fields marked sensitive), or `any`. One field is a select with "None"; a list of fields is a list of
  boxes to tick. A form without such a field says so in one line.
- **Layout** is a select of the site's default (named: "Site default (Card)"), the built-in card and letter, and the
  library's layouts, which the form's setting choices now list (`emailLayouts`, `defaultEmailLayout`).
- **Subject and Body** offer Insert merge tag for mail, its paths from 9.4c's endpoint for the draft.
- **The preview:** `POST form/{key}/email-preview` takes the draft as Add or Update would make it, the action's key and
  the language shown, and answers the mail the team gets for the sample entry, as a run renders it with the site's
  sender and layout and queueing nothing: From, To (and the address a mail of its own goes to, the sample's answer of
  the recipient field), Copy to, Blind copy to, Reply to, the subject, the HTML (none for plain text) and its text,
  and the language's direction. It needs `design` on the form, as the outcome preview does, so who may only read the
  actions sees it too. The sidebar opens Umbraco's large sidebar for an email, the preview beside the settings where
  there is room for two columns and after them where there is not; it is asked once the changes pause and the newest
  answer wins (as an outcome's). The mail shows in a sandboxed frame that runs nothing, its height its own, with a
  Light/Dark switch that sets the frame's colour scheme; a mail with no recipient yet shows "No recipient yet" in To,
  a mail only a field's answer gets shows that address in To and says it is the visitor's mail, and a preview that
  fails says so with Try again.
- **Fix it** of an email setting lands on its part of its editor: a chip or Add recipient for the To box's settings,
  the field's box for the others.

## Consequences

- The setting schema gains `within` and `picks` (`FormBuilderSetting.Within`, `.Picks`), which the field types' and
  the action types' snapshots show; the email type names them, and the webhook's and the content action's Show
  sensitive fields pick sensitive fields too. The schema refuses a setting within one that is none, or within one that
  is itself within another, or inside an object; and `picks` on another editor than the field picker.
- The sidebar takes the editors through the inspector's `editors`, so the inspector itself stays as it is; an address
  box checks what is typed with the email field's own check (`isEmailAddress` of the shared logic), the server's
  check of the action having the last word.
- A preview of an action that names a view the site lacks, with no body of its own, answers `email_view_missing`
  (409), which the preview says in its words; an action of another type is `action_not_email` (409).
- The email type gives a preview its recipients and its mail without queueing it, and the outbox composes a mail
  without refusing one with no address (`EmailOutbox.ComposeAsync`, which `PrepareAsync` uses).
- The form's setting choices list the layouts.
- The backoffice's axe checks (the journeys' `axeInside`, the UX walk) leave frames alone: axe injects itself into
  every frame and waited without end for the preview's, where no script may run. The mail's markup is the email
  renderer's, which its own tests hold; Playwright's own locators read inside the frame.
- 10.2c tests the action as the sidebar holds it, the email to the tester with a line naming its real recipients.
