# 0219. Deliveries and the audit log

- Status: accepted; builds on [ADR-0007](0007-background-work-outbox-and-email.md) (the outbox), [ADR-0013](0013-permissions.md) and its
  amendment (who may retry a delivery), [ADR-0040](0040-outbox-repositories.md) (a retry from the log),
  [ADR-0041](0041-file-store-audit-log-and-daily-stats.md) (the audit log), [ADR-0158](0158-the-action-framework.md) (runs),
  [ADR-0159](0159-mail-the-queue-the-layouts-and-the-limits.md) (mail and its limits) and
  [ADR-0195](0195-an-actions-last-run-and-its-details.md) (a run in words)
- Date: 2026-11-08
- Roadmap step: 12.4, its part 12.4b (ADR-0197)

## Context

Part 12.4b asks for Activity › Deliveries, every email and webhook run with filters and retry, and the audit log viewer
with filters and export (IA 9). The outbox keeps two tables: `jfbEmail`, every mail with its own states (queued, sending,
sent, failed, uncertain, limited), and `jfbActionRun`, every run of an action with its states (pending, running,
succeeded, failed, skipped, uncertain). An email action's run succeeds once it has queued its mail, which then has a
life of its own. Mail is kept 30 days after it reached its state; runs live as long as their entry. The audit log
records who did what to which item, with details that hold keys, counts and identifiers, never content (ADR-0041),
in about sixty actions.

Open questions:

- **What one row is,** when a mail and the run that queued it both exist.
- **What "problems first" covers,** and how a run that failed an attempt and waits for its next one reads.
- **What the list shows of a run's target** without reaching a secret.
- **Who may retry what,** now that mail of no action (a resume link, an address check, a test mail) is retried too.
- **How the audit log names an item** that may be gone, and what its search finds.
- **What the export holds.**

## Decision

### Deliveries

1. **One list of mail and runs.** Every mail is a row; a run is a row unless it queued mail, which then stands for it,
   so an email action shows its mails, one per address, and never a second "delivered" for the same thing. A run that
   ended without mail (it failed before it could queue any) is a row. The list is read in one query over both tables
   (`UNION ALL`), under the read budget of the lists (ADR-0003).
2. **One word per state,** the same for both: **Waiting** (queued or pending, never tried or tried without a failure),
   **Sending** (an attempt holds it), **Trying again** (an attempt failed and the next one has its time), **Delivered**
   (sent, or succeeded), **Failed**, **Uncertain**, **Limited** (mail only) and **Skipped** (runs only). **Problems**
   are Failed, Trying again, Uncertain and Limited: they come first, each group newest first.
3. **Filters:** the state (problems, waiting, delivered, skipped), what it is (emails, webhooks with Slack and
   Microsoft Teams, other actions), and when (the last 7, 30 or 90 days, or all), **the last 30 days** by default, which
   is how long mail is kept. The API takes a form too, for an integration or a later link from a form; no screen sends
   it yet (the phase review, 12.6), and the screen finds a form by its name in the search. The search finds a mail's recipients and a form's name; a run's target is
   not searched, as it lives in the form's version.
   Amended by [ADR-0236](0236-what-the-security-review-changed.md): a mail to an address a visitor typed shows "A
   visitor's address" to a user who may not see that form's sensitive answers, and the search finds such an address only
   for a user who may see sensitive answers on every form.
4. **A row:** its state; **to**: a mail's recipients, a webhook's host (a reference shows the key that holds the
   address, never the address), Slack or Microsoft Teams; **what**: the action's name in the version that queued it,
   or the kind of mail (a resume link, an address check, a test mail, a forwarded entry, a notice of a failed delivery,
   a digest); the form; and when it was queued, with the next try where it waits for one. Never a subject, a body or
   an answer, as they hold what visitors typed.
5. **Details** in a sidebar: what happened, in the words the Actions view uses for a run (ADR-0195), and for a mail
   which exception type stopped it; the tries; the action and its form; the entry by its number, opening it in
   Entries for who may view it; for a **Limited** mail the limit that held it back (so many mails to one address in a
   day, or to visitor addresses in an hour), its number and the key that sets it (`JironFormBuilder:Limits:Mail`),
   saying that a limited mail is never sent later, so it can't be retried.
6. **Retry** is offered for a Failed or Uncertain row only. An **Uncertain** retry asks first and says it may deliver
   twice; the server refuses it without the confirmation (`409 needs_confirmation`), as the entry's retry does. A run's
   retry is the entry's (`ActionRunRetries`, audited `entry.actionRetried`); a mail's sets it queued and due with its
   attempts at 0 (`IEmailRepository.RequeueAsync`), audited `email.retried`, its entity the mail's form when it has an
   entry.
7. **Who:** the list and its details need `manageSettings` (ADR-0013). A retry also needs `manageActions` on the form
   of the run or of the mail's entry, as the amendment of ADR-0013 says; a mail of no entry (a test mail, a digest)
   needs `manageSettings` alone. A user who may read but not retry sees the details without Retry, and one line says
   who can.
8. **Paused:** the list answers whether the host pauses actions and email (`JironFormBuilder:Actions:Paused`), and the
   screen says so once, above the list, with the key: entries are still stored, and their mail and webhooks go out once
   it is off.

### The audit log

9. **The list:** when, who (the user's name, or Jiron Form Builder for its own jobs), what, and where: the item by its
   name as it is now (a form, a folder, an option list, a theme, an email layout, a template, a user group, a site),
   or that it was deleted. The server answers the names of every key it finds in a page's details too (forms, folders,
   user groups, entries by number), so the details read them without a second request.
10. **Filters:** what (forms and folders; entries and deliveries; the library; settings and access), when (the last 24
    hours, the last 7, 30 or 90 days, or all), **the last 30 days** by default, and who: an action's details offer
    **Show only their actions**, and a chip above the list names the person until it is cleared, as Umbraco's user picker
    needs a collection of its own to open. The API takes a form too, which matches the actions on it and those that name
    it in their details as their form, as an entry's actions do; as for Deliveries, no screen sends it yet. The search
    finds people by name or email (Umbraco's user filter) and items by name (forms, folders and the library's items):
    an entry matches any of them.
11. **Words:** every action has a sentence in the backoffice's languages, filled from its details ("Published version
    3", "Exported 214 entries", "Revealed Date of birth in entry 212"); a unit test fails for an action the product
    records without words. The details sidebar lists who, when (with the zone the time is written in), where, and the
    details: a grant's permissions before and after as a table, a settings save's changed fields by their names (the
    values are never recorded, ADR-0041), counts and entry numbers. A form's save, publish or restore links to that
    version in its History.
12. **Export:** `GET audit/export` writes the filtered log as CSV (UTF-8 with its mark, so spreadsheets read it), at
    most 100,000 rows, newest first: the time in UTC (ISO 8601), the user's name and key, the action, the item's type,
    key and name, and the details as JSON. A cell that a spreadsheet would run as a formula starts with an apostrophe,
    as in the entries export. The export is itself recorded (`audit.exported`, with its filters and the rows written),
    since it hands out who did what, and the export holds what was recorded before its own record.

## Consequences

- An email action's mails are each their own row, so a mail to three visitors shows three times, each with its state.
- A run's target is read from its version per page, so a page of runs of many versions reads each version once.
- A settings change in the log names the fields that changed but never their values; a reader who needs the values
  looks at the screen itself.
- **Revisit** if sites keep so many runs that the 30 days' list leaves its budget, or if a delivery ever needs "Retry
  now" for a row that is trying again.
