# Entries and exports

An entry is one sent form: the answers, where and when they came from, and everything that happened to them since.
This page covers the lists of entries, one entry's panel, approving, spam, working with many entries at once and
exporting them.

## Contents

- [Where entries are](#where-entries-are)
- [Entry states](#entry-states)
- [Finding entries](#finding-entries)
- [One entry](#one-entry)
- [Spam](#spam)
- [Several entries at once](#several-entries-at-once)
- [Exports](#exports)
- [A summary by email](#a-summary-by-email)
- [How long entries are kept](#how-long-entries-are-kept)
- [Who may do what](#who-may-do-what)

## Where entries are

- **Entries › Inbox** in the sidebar: the entries of every form you may see, entries you haven't seen first, then the
  newest. Its badge counts the unseen ones.
- **A form's Entries view:** the same list for that form alone.
- **Entries › Spam:** what the spam checks caught ([below](#spam)).
- **The Overview's Recent entries:** the latest ten.

An entry is **unseen** until someone opens it; it is shown in bold with a dot, and opening it marks it seen for
everyone. Test entries sent from the preview never appear in any list.

## Entry states

| State | Means |
| --- | --- |
| Waiting for confirmation | The form asks visitors to confirm their address, and this visitor hasn't yet. No action runs until they do. |
| Waiting for approval | The form has manual approval, and nobody has approved or rejected the entry. The triggers call this state **Submitted**. |
| Approved | Approved by someone or by an action, or, on a form without manual approval, as it arrived. |
| Rejected | Rejected by someone or by an action. |
| Spam | Caught by the spam checks, or moved there by someone or by an action. |

On a form with manual approval, **Approve** and **Reject** in the entry's panel decide; an approved or rejected entry
can go **Back to waiting for approval**. Nobody can approve an entry around its address check: an entry waiting for
confirmation can only be confirmed by the visitor, or moved to Spam.

An entry nobody confirms is deleted a week after its link runs out, 14 days after it was sent unless the form gives
the link longer.

## Finding entries

- **Search** looks in the answers, the entry's title, its number (`1042` or `#1042`) and its reference. Sensitive
  answers are never searched.
- **Filter** narrows the list by form, state, when the entries arrived (in the site's time zone), how their actions
  went (failed, uncertain, all delivered) and by the form's fields, for example a choice that is one of a few options or
  a date in a range. Each filter shows as a chip above the list.
- **Save as…** keeps the filters as a named set, under **Saved** in the Filter menu, for you alone.
- **Choose columns** adds the form's fields as columns, up to 20. In the Inbox, filter by one form first.

Every list shows each entry's title (or its number), its state, when it arrived and its actions: "2/2 delivered", "1
failed" or "Not run". The list shows 50 entries at a time, with **Show more**.

The **entry title** comes from the form's Settings view, such as `{{ field.name }}, {{ field.company }}`; without one,
the list shows the number. Each form numbers its entries from 1.

Keyboard shortcuts in a list (**?** shows them): `j` and `k` next and previous, `Enter` open, `Esc` close, `a` approve,
`r` reject, `s` mark as spam, `/` search.

## One entry

Choosing an entry opens its panel beside the list, in three tabs.

### Answers

The answers, by page and group, as the form was when the entry was sent: a field removed since is marked "No longer on
the form". Above them: the page it was sent from, its language and how long it took to fill in; **More details** adds
the member who sent it, the IP address if the form keeps it, and the spam reasons.

- **Files** open in a viewer if they are images, and download otherwise. A file the site is still checking is held
  back until the check ends ([files.md](files.md)).
- **Sensitive answers** show as "Hidden: sensitive". Users who may see sensitive data can **Reveal** each one; every
  reveal is recorded in the audit log, without the value.
- **A consent** shows the exact wording the visitor agreed to.
- An answer the form keeps only until its actions have run shows "not kept" once they have.

**Edit answers**, for users who may edit entries, changes texts, numbers, dates, single choices and checkboxes. Each
change is recorded in the timeline with the value before. Files, consents, signatures, calculations, grids, rankings,
names, addresses and repeatable groups can't be edited.

### Timeline

Everything that happened, newest first: when it arrived, who opened it, each change of state and who made it, edits,
forwards, legal holds, and each action and mail with its result. A failed or uncertain action has **Retry**.

### Notes

Notes for the team. Visitors never see them; everyone who may open the entry can read them. You can edit your own.

### The panel's actions

Depending on the state: **Approve** and **Reject** (on forms with manual approval), **Mark as spam** or **Not spam**,
**Forward…**, and under **…** **Delete…**, **Print** and **Re-run actions**.

- **Forward…** sends the entry to a backoffice user who may see the form's entries, or to one of the addresses in
  Settings › Email › **Addresses entries may be forwarded to**, with an optional message. Sensitive values stay masked
  unless you include them and the recipient may see them too.
- **Print** opens a printable view of the entry, without sensitive answers.
- **Re-run actions** runs the actions again, so their emails and messages really go out again.

Each entry has its own address in the backoffice, which `{{ entry.url }}` writes into emails to the team.

## Spam

Entries the spam checks catch are stored in **Spam**, never thrown away, and no action runs for them. The entry's panel
says **why it is in Spam**, for example "Sent too soon after the page loaded", "Contains too many links" or "Filled in a
field only robots see" ([spam-protection.md](spam-protection.md)).

- **Not spam** moves an entry to where it would have gone and runs the actions it missed, and only those.
- **Empty spam…** deletes everything in Spam you may delete, except entries under a legal hold.

Spam is deleted after 30 days unless the site's retention says otherwise.

## Several entries at once

Tick entries to work on several at once: **Approve**, **Reject**, **Mark as spam**, **Delete…**, **Export selected…**
and **Re-run actions**, up to 500 at a time. An action applies to the entries it can; the message says how many were
left out and why.

**Undo:** after a change of state, by hand, alone or in bulk, the message offers **Undo** for 10 seconds. The actions
the change starts wait for those 10 seconds too, so Undo takes them back as well. After that, change the state back by
hand.

**Deleting** can't be undone. It deletes the entries with their files, notes and timelines, and asks first.

## Exports

**Export…** in a form's Entries view, or in the Inbox once it is filtered by one form, exports the entries that match
the filters; **Export selected…** exports the ticked ones.

| Format | Holds |
| --- | --- |
| CSV | One row per entry. Choose the delimiter, labels or aliases as headings, and a byte-order mark (on by default) so spreadsheet programs read letters such as ä and é correctly. |
| Excel | One row per entry, and a sheet of its own for each repeatable group. |
| JSON | One object per entry, with the answers by alias. |
| Printable view | A page to print or save as PDF. |
| ZIP of the uploaded files | The files, in one folder per entry. |

Every export starts with the entry's number, reference, when it arrived and its state, then the fields you choose, in
the form's order. Several answers to one field are joined with commas; a grid has a column per row and a ranking a
column per place. Sensitive answers are left out unless you may see them and include them. Entries waiting for email
confirmation stay out unless you filter by that state or tick them.

Up to 10,000 entries download at once. A larger export, and every ZIP, is prepared in the background while you go on
working, and a notification links to the file when it is ready. Only you can download it, and it is deleted after 24
hours. Every export is recorded in the audit log.

A cell that starts with `=`, `+`, `-` or `@` gets a `'` before it, so a spreadsheet program never runs a visitor's
answer as a formula.

## A summary by email

**Email me a summary…** in a list's **…** menu sends you a daily or weekly email (Mondays) at 07:00 in the time
zone set for all sites (Settings › General), with how many entries arrived on each form you may see, and how many wait for approval, with links. It holds
counts and links, never answers, and nothing is sent when nothing arrived.

## How long entries are kept

The site keeps entries for a number of days in each state, by default a year for entries waiting for approval and for
approved ones, 90 days for rejected ones and 30 days for spam, counted from the last change of state. A form can keep
its entries for less or more, or store none. Test entries are deleted after 7 days. [privacy.md](privacy.md) covers
retention, legal holds and the privacy tools.

## Who may do what

| Permission | Lets a user |
| --- | --- |
| View entries | see the lists, entries, timelines, notes and files, and get the summary email |
| View sensitive data | reveal sensitive answers and include them in exports and forwards |
| Edit entries | approve, reject, mark as spam, edit answers and forward entries |
| Delete entries | delete entries and empty Spam |
| Export entries | export and print |
| Manage actions | run actions again and retry them |

[permissions.md](permissions.md) explains how to give them.
