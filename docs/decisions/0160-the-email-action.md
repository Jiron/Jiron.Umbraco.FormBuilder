# 0160. The email action

- Status: accepted; builds on [ADR-0014](0014-merge-tag-language.md), [ADR-0044](0044-form-service.md),
  [ADR-0158](0158-the-action-framework.md) and [ADR-0159](0159-mail-the-queue-the-layouts-and-the-limits.md)
- Date: 2026-11-02
- Roadmap step: 8.3 (decided and built); 9.x (its editor in the designer), 9.5b (the form check)

## Context

ADR-0158 runs action types and ADR-0159 queues mail. This step joins them. Five questions were left open.

## Decision

**A run ends once its mail is queued.** The mail of one attempt queues in one unit of work, so a failed attempt leaves none of it
behind to be sent twice, and each mail then has its own attempts in the log. A run tried again finds what an attempt before it queued
and queues nothing more, as a commit can succeed and still answer with an error; a mail is retried from the log. A mail held back by a
limit is no failure of the action: the run succeeds and the log shows the mail as limited. A stop before anything is queued tries
again without a question.

**An entry waits for its mail.** On a form that stores nothing, and for values kept only until the actions have run, the entry stays
while a mail of it is queued, being sent or uncertain, as a mail reads its attachments when it leaves. The mail's end settles it again,
and the retention job catches what a stopped server left. As with runs, a mail that failed for good no longer holds the entry back.

**Recipients come in two kinds.** Fixed addresses, the recipient map and the form's creator are the team's: they share one mail, which
is not counted. The answer of an email field is an address a visitor typed: it gets a mail of its own, counted by the limits, and it
is never a copy. It carries no copies, reply-to or files: a bot must not reach the team's copy addresses, send a stranger mail that
answers to the bot, or attach its upload for a stranger. Copies on a mail with only a visitor's address go as a mail of their own. An
answer that is empty or not an address means no mail, and the run succeeds, because nothing went wrong with the action. A map's route is
checked against the field's options when it publishes, and an answer without a route sends nothing. A sensitive field shows only where
the action names it, as a reply-to or an attachment too.

**A link a visitor typed is no link.** Values are escaped, so no markup gets through, but mail clients turn a bare `https://` or
`example.com` into a link on their own. In mail to an address a visitor typed, values that can hold visitor text get a zero-width
space after each dot, in the forms clients read as one, unless a space follows it, and inside each `://`. Readers see the same words; a
bot cannot use the form to send a working link in the site's name. The team's mail keeps the text as typed, since they need to read it and it comes to nobody else. What the site
writes itself, such as `form.*` or `entry.url`, and an editor's own links stay as they are.

**`entry.table` is `entry.answers` with the unanswered fields.** Both follow the rules of ADR-0014 on what mail shows. An action can
leave the unanswered ones out.

**A Razor view writes the body, not the whole mail.** A view in `Views/JironFormBuilder/Emails` replaces the action's body, and what it
writes passes the mail's sanitiser and layout like any body, so dark mode, well-formedness and the client rules hold for every mail. A
site that needs a whole document of its own writes an action type. A view it lacks gives way to the action's body with a warning.
The worker has no request, so the view runs on a request of its own in a scope of its own, in the mail's culture and without the
site's `_ViewStart`. A view's name is checked where it is found too, as a test run takes drafts.

**Presets are data on the declaration.** Any action type can declare ready-made starting settings, which the management API serves for
the designer of Phase 9.

**"Notify the creator" is a `general` setting for every site at once,** off by default, as ADR-0044 decided. A form from blank or from a
template starts with the action unless it has an email action. Its recipient is resolved when it runs, from the form's creator, so an
address changed later is the one used.

## Consequences

- Tests that only need an action to exist use a stand-in type, `testing.mail`, since a real email action needs its settings.
- The form check of 9.5b can warn about an email action on a form without an email field; publishing already refuses settings that
  could never send.
- Mail to a visitor renders once per address, so a form with both kinds of recipients renders its template twice.
