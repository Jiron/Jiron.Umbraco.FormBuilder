# Top tasks

The fifteen most common tasks of each persona of ADR-0013, as the user would say them, with the shortest click path
through the Forms section. The screens are in [information-architecture.md](information-architecture.md); clicks are
counted as its "Rules" section says, from the section's start page.

- **The paths** are the ones a subagent playing a first-time editor found from the design alone (step 2.1, the fourth
  round; see "How the paths were checked"). Steps in parentheses are the task's own input or its confirming button, so
  they don't count.
- **Each task assumes the usual setup:** answers that aren't marked sensitive and, for approving and rejecting, a form
  with manual approval.
- **Two limits are kept on purpose:** search leaves sensitive answers out, and entries are forwarded only to people
  the site already trusts.

## Designer

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | Start a new, empty form. | sidebar header "+" → Form | 2 |
| 2 | Start a form from a ready-made contact form. | "+" → Form from a template… → (Use template; the gallery has the contact form chosen) | 2 |
| 3 | Add an email field to a form. | the form → Email in the palette | 2 |
| 4 | Change the wording of a question. | the form → the question → (its label) | 2 |
| 5 | Make a question compulsory. | the form → the question → (Required) | 2 |
| 6 | Show a question only when a visitor picked "Other" in another question. | the form → the question to show → "Add a rule" → (Country is Other, Add) | 3 |
| 7 | Split a form into two steps. | the form → the insert point "Add a field here" → Layout › New page in the palette | 3 |
| 8 | See the form as a visitor will, before it goes live. | the form → Preview | 2 |
| 9 | Put your changes live. | the form → Publish… → (Publish) | 2 |
| 10 | Change what visitors read after they send the form. | the form → Settings → the outcome's row → (the message, Update) | 3 |
| 11 | Have the sales team emailed about every new entry. | the form → Actions → Add action → (the recipients, Add) | 3 |
| 12 | Try the team email on a sample entry, without waiting for a real visitor. | the form → Actions → the card's Test | 3 |
| 13 | Find out which pages show a form. | the form → "Used on 3 pages" | 2 |
| 14 | Go back to how a form was last week. | the form → History → last week's row → (Restore as a new draft) | 3 |
| 15 | Make a list of the company's offices that several forms can reuse. | Library › Option lists → Create option list → (the kind, the offices, Save) | 2 |

At 620 px, where the palette doesn't stay open, task 3 is the form → "Add field" → Email (3). Task 7 also works by
selecting the field the new page follows, then the palette's Layout › New page (3).

## Entry handler

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | See everything new that came in, from all forms. | Entries › Inbox | 1 |
| 2 | Read one entry. | Inbox → the entry's row | 2 |
| 3 | Approve an entry. | Inbox → the entry's row → Approve | 3 |
| 4 | Reject an entry. | Inbox → the entry's row → Reject | 3 |
| 5 | Move an obvious junk entry out of the way. | Inbox → the entry's row → Mark as spam | 3 |
| 6 | Rescue a real entry that was caught as spam. | Spam → the entry's row → Not spam | 3 |
| 7 | Leave a note for colleagues on an entry. | Inbox → the entry's row → Notes → (the note, Add note) | 3 |
| 8 | Send an entry to a colleague. | Inbox → the entry's row → Forward… → (the colleague, Forward) | 3 |
| 9 | Correct a typo in someone's answer. | Inbox → the entry's row → Edit answers → (the fix, Save) | 3 |
| 10 | Download the CV someone uploaded. | Inbox → the entry's row → the file | 3 |
| 11 | Get a spreadsheet of a form's entries. | the form (it opens on Entries) → Export… → (Excel, Export) | 2 |
| 12 | Find the entry from "Jana Weber". | Inbox → (search "Jana Weber") → the row | 2 |
| 13 | See only this week's entries, and keep that view for next time. | Inbox → Filter → (This week) → Save as… → (the name, Save) | 3 |
| 14 | Check that the confirmation email to a visitor went out. | Inbox → the entry's row → Timeline | 3 |
| 15 | Get a daily email instead of watching the list. | Inbox → "…" → Email me a summary… → (Daily, Save) | 3 |

Tasks 3 to 6 take 2 clicks for several entries at once: tick their boxes, then use the bulk bar.

## Viewer

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | See what came in lately. | the Overview's Recent entries | 0 |
| 2 | Read one entry. | Inbox → the entry's row | 2 |
| 3 | Find an entry by someone's email address. | Inbox → (search the address) → the row | 2 |
| 4 | See only last month's entries. | Inbox → Filter → Received → (Last month, Apply filter) | 3 |
| 5 | Use the filter set saved yesterday. | Inbox → Filter → (the saved set) | 2 |
| 6 | Add the "Company" answer as a column of the list. | the form → the column chooser → (Company) | 2 |
| 7 | See the entries of one form only. | the form | 1 |
| 8 | Open a photo someone uploaded. | Inbox → the entry's row → the photo | 3 |
| 9 | See everything that happened to an entry. | Inbox → the entry's row → Timeline | 3 |
| 10 | See how many visitors started and finished a form. | the Overview's Forms at a glance | 0 |
| 11 | See what people answered to a rating question. | the form → Insights → Answers | 3 |
| 12 | See whether this month went better than last month. | the Overview's Forms at a glance → (This month, in its period menu) | 0 |
| 13 | Compare all forms at a glance. | the Overview's Forms at a glance | 0 |
| 14 | Look at what was caught as spam. | Entries › Spam | 1 |
| 15 | Get a weekly summary by email. | Inbox → "…" → Email me a summary… → (Weekly, Save) | 3 |

Task 4's Filter menu names what to filter by first (Received, State, Form and the form's fields), so the period is
one step in.

## Administrator

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | Make sure the site can send emails. | Settings › Email → Send test email | 2 |
| 2 | Change the sender address of all form emails. | Settings › Email → (the sender, Save) | 1 |
| 3 | Let the support team see entries. | Settings › Access → (tick the support group's entry permissions, Save) | 1 |
| 4 | Close the HR folder to everyone but the HR team. | the HR folder's "…" → Permissions… → (the HR team, Only these groups, Save) | 2 |
| 5 | Create the block that lets editors put forms on pages. | the Overview's checklist item "Put the form on a page" → Create a form block type… | 1 |
| 6 | Find emails that failed to send. | Activity › Deliveries | 1 |
| 7 | Send a failed email again. | Deliveries → the failed row → Retry | 3 |
| 8 | Find out who changed a form's settings. | the form → History | 2 |
| 9 | Send entries from throwaway email addresses to Spam. | Settings › Spam protection → (the blocked email domains, Save) | 1 |
| 10 | Allow larger files in a form's upload field. | the form → the upload field → (the largest file) | 2 |
| 11 | Choose the look that forms get by default. | Settings › General → (the default theme, Save) | 1 |
| 12 | Give the second site its own sender address. | Settings › Email → (the site switch, the sender, Save) | 1 |
| 13 | Keep rejected entries for only 30 days. | Privacy › Retention → (Rejected: 30 days, Save) | 1 |
| 14 | Get rid of the forms in the Recycle Bin for good. | the Recycle Bin → Empty recycle bin… | 2 |
| 15 | Bring in a form exported from another site. | "+" → Import a form… → (the file, Import as a new copy) | 2 |

Once the checklist is complete, task 5 is Settings › General → Create a form block type… (2). When the site's limit is
lower than the size wanted, task 10 adds "Change the site's limit" in the field's help (3). Blocked words take the
same path as task 9, but one word alone doesn't send an entry to Spam: words and extra links add up (ADR-0012;
information-architecture.md, section 8).

## Privacy officer

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | Find everything stored about a person, by email address. | Privacy › People → (the address, Find) | 1 |
| 2 | Find everything stored about a website member. | Privacy › People → (the member's email address, Find) | 1 |
| 3 | Give a person a copy of their data. | Privacy › People → (Find) → Export everything → the ready toast's Download | 3 |
| 4 | Erase a person's data after a request. | Privacy › People → (Find) → Erase… | 2 |
| 5 | See what retention will delete in the coming weeks. | Privacy › Retention | 1 |
| 6 | Check what a visitor agreed to when they sent a form. | Inbox → the entry's row | 2 |
| 7 | See a masked answer in full. | Inbox → the entry's row → Reveal | 3 |
| 8 | Delete one entry. | Inbox → the entry's row → Delete… | 3 |
| 9 | Delete several entries at once. | Inbox → (tick the rows) → Delete… | 2 |
| 10 | Empty the spam folder. | Spam → Empty spam… | 2 |
| 11 | See which page and language an entry came from. | Inbox → the entry's row | 2 |
| 12 | See which emails were sent about an entry. | Inbox → the entry's row → Timeline | 3 |
| 13 | Find an entry by a person's name. | Inbox → (search the name) → the row | 2 |
| 14 | See all entries of one form. | the form | 1 |
| 15 | See one form's entries from a date range. | the form → Filter → (Received: the range) | 2 |

Task 3's export is put together in the background, as a person's data may be large, and its toast's Download saves it.
Task 2's search by address finds the member's entries too, those sent while signed in among them. A user who may
also open Umbraco's Members section can pick the member instead: Privacy › People → Find a member instead… → (the
member, Choose) (2); Umbraco lists its members to nobody else.

## No access

This persona sees the section and nothing in it, by design. Its tasks are the three things the section still tells it.

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | Find out why no forms appear, and who can help. | the sentence in the tree's place | 0 |
| 2 | See how far the site's setup is. | the Overview's setup checklist | 0 |
| 3 | See whether anything needs attention. | the Overview's Health line | 0 |

Once the checklist is complete, task 2 is the Overview's "…" → Show the setup checklist (2).

## How the paths were checked

Four subagent runs played a first-time editor with nothing but the design documents, each with the same brief, and the
design was changed after each:

1. **The first** found 9 tasks over 3 clicks and a dozen controls the design didn't describe.
2. **The second** found none over 3, but tasks that couldn't be finished as described.
3. **The third** found four tasks at 4 clicks: emailing the team, restoring last week's version, raising a file limit
   past the site's, and finding who changed a form.
4. **The fourth** found every task in 3 clicks or fewer on its best path, and every task possible for its persona. The
   paths above are its own, with the small changes made after it: the Overview lost its duplicate create button, so
   designer tasks 1 and 2 and administrator task 15 start from the sidebar header's "+", which that run found too.

**The fourth run's notes** on the longer routes a newcomer might try first shaped the last changes:

- **A new page's empty state** points to Layout › New page.
- **A toast** says when an addition waits for publishing.
- **The Audit log** links a form's changes to its History.

Step 16.6 counts the clicks of the top five tasks per role again, on the built screens.
