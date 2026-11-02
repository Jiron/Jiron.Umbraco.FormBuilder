# 0159. Mail: the queue, the layouts and the limits

- Status: accepted; builds on [ADR-0007](0007-background-work-outbox-and-email.md), [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md),
  [ADR-0040](0040-outbox-repositories.md) and [ADR-0158](0158-the-action-framework.md)
- Date: 2026-11-02
- Roadmap step: 8.2 (decided and built); 8.3 (the email action), 8.5 (verification mail), 12.2 (the layout editor)

## Context

ADR-0007 decided the states of a mail, its one body and how it leaves; ADR-0012 the limits for addresses visitors type. Step 8.2
builds them. These questions were left open.

## Decision

**A mail is prepared, then queued.** Preparing reads the site's settings and layout and renders the body. On SQLite a read waits
for an open unit of work, and a settings miss loads outside the caller's context, so preparing inside a unit would wait for
itself. So `EmailOutbox.PrepareAsync` refuses inside a unit, and `QueueAsync` of what it prepared joins the caller's unit, so a mail
leaves only if that unit commits. `QueueAsync` of an `OutgoingEmail` does both for a caller without a unit.

**A missing sender does not hold a mail back at the door.** The stored sender is empty when a site has none and neither has
Umbraco; `IEmailSender` then decides when it is sent, and that fails visibly in the log. Only the mail's own attempts can see that,
and a retry from the log works once the host is fixed. The health check of 9.1 names the cause.

**Two looks, one renderer.** A layout has a `look`: `card` (a bordered surface on a quiet page) or `letter` (text on the page
itself). Both built-in layouts have fixed keys, so settings and actions always name a layout by its key. A layout of the library
adds a logo, colours for each scheme, and a header and footer per culture. Nothing about a layout can stop a mail: one that is
gone or unreadable gives way to the card, and a colour that is not `#rrggbb` is ignored.

**The markup follows what mail clients do.** Tables, with the styles inline on each element; a fixed width in `<!--[if mso]>`
tables; nothing on `body`; a fixed table layout with words that may break. The one style sheet moves the inline colours to the
dark scheme, so a client that ignores it shows the light mail. The document is serialised as XHTML, so a parser proves it
well-formed, and what XML cannot hold is removed first.

**The body passes a sanitiser of its own:** the editors' allowlist of ADR-0084 plus tables, which an entry's answers need. Links get
their address from the site's base URL, never from a request; one that goes nowhere loses its address and keeps its words. The
text version is made from the same cleaned text, so the two versions cannot say different things.

**One address is one mailbox.** An address with a quoted local part is refused, as it can hold commas and stored recipients are a
list split on them. The key folds compatibility forms and the domain as a mail server will look it up (punycode, full-width letters,
other dots), so those spellings cannot pass a limit one by one. A key too long for its column means no mail, never an uncounted one.

**The limits count what was allowed.** One `INSERT ... SELECT` counts and inserts, with a range lock on SQL Server. The site's count
reads an index of its own (`IX_jfbEmail_createdUtc_recipientKey`), so the lock covers the last hour and not all kept mail. A mail held back
does not count, so a flood cannot keep a mailbox closed after it stops. "Site" means this installation, as the rate limits read it:
the cap protects the installation's sending reputation, and the mail server is shared. A held-back mail records which limit held it
back, for the deliveries log. Mail to the team has no recipient key and is not counted.

**Attachments are read when the mail is sent,** because a scan may have finished by then: what is quarantined, or waits for a scanner
the site still has (ADR-0144), is left out. So is what is gone or does not fit `Limits:Mail:AttachmentBytes`, which counts files as
they travel, base64 being a third larger, and the mail goes without it, with a warning in the log: a large upload must not stop the
team hearing of an entry.

**An attempt follows ADR-0158:** the same back-off and number of attempts, and a cut-off is uncertain with a late finisher. Only an
error's type is kept, as its message can hold an address. A failure to read an attachment gives the claim back without using an
attempt, since nothing left, so a long outage of the file store cannot use up a mail's five. While `CanSendRequiredEmail()` is false a pass claims nothing.

**A test mail goes to the user who asks, and nobody else.** `POST email/test` takes no address, so it cannot be used to send mail
elsewhere, and answers `409 email_not_configured` while Umbraco could not send it.

## Consequences

- 8.3's action prepares and queues through this outbox, and verification and resume mail in 8.5 prepare before their unit.
- How long mail is kept is a constant of 30 days until the privacy settings of 11.x make it editable (section 4.5).
- A mail over a limit is visible only through the log. The deliveries screen (12.x) shows it, and the health check (9.1) warns.
- Counting under a range lock serialises mail to visitors on SQL Server. That is what the cap asks for.
- A mail to a visitor is for that address alone, and its copies go to fixed addresses: 8.3 sends each address a visitor typed a mail of
  its own, so the limits count them all.
