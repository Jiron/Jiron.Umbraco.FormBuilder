# 0204. The preview's test entries, their actions, and sample answers

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 10.3f; builds on [ADR-0203](0203-the-preview-frame-and-its-ticket.md) (the frame and its ticket),
  [ADR-0187](0187-the-outcome-preview.md) (the sample entry), [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md)
  (the pipeline), [ADR-0090](0090-places-on-options.md) and [ADR-0091](0091-unique-answers.md); amends
  [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (what a ticket stands in for); IA 5.7, copy.md

## Context

IA 5.7 lets the preview send the form: what it sends is a **test entry** (`IsTest`), which runs no actions unless "Run
actions for test entries" is on; after sending, a note offers **Run actions…** for that one entry, which asks first and
lists the actions, as their emails and messages then really go out. **Fill with sample answers** fills every field
with valid values in one click (Start again, after sending). §4.4 keeps test entries out of exports, insights and
actions unless the editor asks, and ADR-0090 and ADR-0091 let them hold no place and no unique answer.

The entries, uploads and receipt endpoints took no ticket (ADR-0203). The pipeline reads the published version only,
asks the challenge's puzzle and captcha, and scores spam, the fill time among it; a sample filled in one click would
always be "too fast". The stats counter, the confirmation mail and the arrival notifications for Umbraco's webhooks
fire whatever the entry is. `SampleEntries` (ADR-0187) answers the merge tags of previews and test runs: it leaves out
files, signatures and grids, and nothing maps its answers to a page. Every page of a form is rendered into the DOM, and
a page renders the answers of a post's state as its starting values (`FormPostState`, ADR-0100).

Running an action sends mail and calls other systems: the Actions view's Test needs `manageActions` (ADR-0013), the
preview only `design`.

## Options

**Who decides whether a test entry's actions run**

1. A header the frame sends: the frame's own word, which the server must check against the user all the same.
2. **The ticket**: a ticket says whether its entries run their actions, and only a user who may manage the form's
   actions gets one that does; switching it in the preview hands the frame a new ticket over its port.

**How the sample answers reach the form**

1. The frame fills the controls itself: a second way of writing every field type's value, in the website runtime's
   budget or beside it.
2. **The server renders the document with them as its starting values**, through the post state a page renders after
   a post, which writes every field type's value already; the preview script adds what only a browser makes, a file.

**Which test entry Run actions… means**

1. The public answer names the entry: a change to the public API for the backoffice's sake.
2. **The submission id** the frame sent, which the preview script reads from the answer and hands the backoffice over
   the port; the management API finds the form's test entry by it.

## Decision

Option 2 each time.

- **Sending:** the entries, uploads and receipt endpoints take a ticket (`[FormBuilderPreview]`); drafts, resuming and
  the render endpoint still don't. A ticket's submission reads the ticket's version as its page check does, for a test
  member, and is stored with `IsTest`. The ticket stands in for the puzzle, the captcha and the spam score, which tell a
  person from a script, not an editor from a visitor: a test entry never lands in Spam. Its answers meet every other
  check a visitor's meet, uniqueness and availability included (ADR-0091).
- **What a test entry sets off:** nothing, unless its ticket runs actions. It queues no action, sends no confirmation
  mail (on a form with email verification it arrives as confirmed), raises no arrival notification for Umbraco's
  webhooks, takes no place, holds no unique answer, counts toward no limit and adds nothing to the form's stats. Its
  submitted event always records the actions its rules chose, so Run actions… runs those. With a ticket that runs
  actions, everything but the stats, places, unique answers and limits goes as for a visitor's entry.
- **The ticket** (`Jiron.FormBuilder.Preview.v1`, its payload's version 2) gains whether its entries run their actions.
  `GET form/{key}/preview?…&actions=true` and `POST form/{key}/preview/ticket` with `runActions` issue such a ticket only
  to a user with `manageActions` on the form (`403` otherwise). "Run actions for test entries" sits in the "…" for that
  user only, off each time the preview opens; switching it hands the frame a new ticket over its port, keeping the
  answers typed. The note says what the ticket the frame holds does, so while a switch's ticket is on its way it still
  says what the one before does; a switch whose ticket does not come springs back.
- **After sending,** the preview script reads the submission id from the receipt in the answer of the entries endpoint,
  or of the receipt endpoint it asked after a lost answer, and hands it to the backoffice over the port. The
  backoffice heeds an id only as the public API makes one (a UUID), each once, and ten for one document, so a script in
  the frame that keeps saying it sent cannot keep the session alive with reads (section 4.10).
  `GET form/{key}/preview/entries/{submissionId}` (`design`) answers the test entry's number, whether its actions ran,
  whether they wait for its address to be confirmed (a ticket that runs actions on a form with email verification), and
  the actions Run actions… would run, in the order shown; `POST …/actions` (`manageActions`) queues them once, as its
  submission would have (`409 actions_ran` once they ran or while they wait for the confirmation). Neither answers for
  an entry that is not a test entry of the form.
- **Sample answers:** `GET form/{key}/preview?…&sample=true` renders the draft with the sample entry's answers as the
  starting values of every page, from `SampleEntries`, which now answers a grid (each row's first column) and, for the
  page, a signature (the sample person's typed name, which the runtime draws and sends as a signature). The runtime then
  evaluates the rules on them as on any answer. The preview script gives each file field a sample file of the first
  type its field accepts that it can make (PNG, JPEG, GIF, PDF, text or CSV), which uploads as a visitor's. **Start
  again** reads the draft afresh.
- The preview's words are in copy.md: the note above the frame says whether entries sent here run their actions, and
  after sending which test entry was saved and whether its actions ran, with Run actions… for a user who may run them.

## Consequences

- Positive: an editor tries the form end to end, uploads and outcome included, without one email going out or one
  entry counting, and runs the actions of a test entry once they are sure.
- Positive: sample answers come from the code that renders a post's answers, so every field type writes its value the
  way it writes a visitor's.
- Negative: test entries are stored and kept 7 days (ADR-0009) once Phase 13's retention runs; until then they stay,
  out of every list and count.
- Negative: an outcome's copy of the answers comes from the render endpoint, which takes no ticket; in the preview it
  says it could not be made, as when the copy fails.
- Negative: a field whose own checks no sample candidate passes, such as a text with a pattern, stays empty, and the
  preview shows its error when sent, as it would to a visitor.
- Neutral: a test entry may hold a field only the draft has, which no publish has put in the field catalogue yet. Its
  row is added before the entry's values, without a first version (the rows of ADR-0003 that only a draft has), so the
  columns, filters and exports of the entries visitors sent leave it out until a publish includes it. A row another
  submission added a moment before is taken as it is, and the catalogue read again.
- Negative: a field the draft stores otherwise than its published versions (a number turned into a text) or under
  another parent keeps no value in a test entry: its catalogue row stays theirs until a publish changes it. Its answer
  is checked and the outcome shown as for any other; only the stored test entry, and the actions Run actions… runs
  from it, lack it.
- Neutral: a test entry whose actions wait is kept on a form that stores no entries, so Run actions… finds it; one
  whose rules chose no action its arrival could run has nothing to wait for and goes at once, and one whose ticket runs
  its actions goes as a visitor's entry on that form does.
- Neutral: the upload store takes a preview's file for a form never published, which it otherwise refuses as gone;
  the form must still exist outside the trash.
