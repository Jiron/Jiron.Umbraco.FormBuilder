# Step 10.3f: the preview's test entries and sample answers

ROADMAP 10.3f has what the preview sends become test entries, which run no action unless the user asks, and has Fill
with sample answers fill every field so the form sends in one click
([ADR-0204](../decisions/0204-test-entries-and-sample-answers.md), building on ADR-0203):

- the frame's ticket now reaches the entries, uploads and receipt endpoints; what it sends is a test entry of the
  draft's version, which stands in for the puzzle, the captcha and the spam score, and sets off nothing: no action, no
  confirmation mail, no notification for Umbraco's webhooks, no count;
- "Run actions for test entries", in the "..." for who may manage the form's actions, hands the frame a ticket whose
  test entries run their actions as a visitor's entry does, counting nowhere still;
- after sending, the note above the frame says which test entry was saved, and Run actions... runs its actions once,
  after a confirmation that lists them;
- Fill with sample answers renders the draft with the sample entry's answers as the starting values of every page, a
  grid's rows and a signature's typed name among them, and the preview script gives each file field a sample file;
  Start again, after sending, renders it afresh.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.7, and [copy.md](copy.md)).
The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would have asked about was
decided here or in the ADR.

## What was looked at

Edge, through a scratch spec that is never committed: the preview of a form with a file field, a signature and two
actions, at 1440 and 375 px, light with English and dark with German: the note before sending, the "..." with Test
entries, the draft filled with the sample answers, the note once sent with Run actions..., and its confirmation. The note
sits in one line of small text under the toolbar, muted until something was sent, so the frame below it never moves;
at 375 px the toolbar wraps Fill with sample answers onto a second row beside the "...", and Run actions... onto the
note's. The sample file shows in the file field's list as a visitor's would, and the typed name in the signature. The
confirmation names each action as its card in the Actions view does ("Email the team, To team@example.com"), German
included. Two things were changed after the first round of shots: Fill with sample answers was 33 px tall beside the
36 px widths, and Run actions... spun while its dialog was still asking.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's design system, for a designer who tries the form end to end before
publishing it and must never send a real email by accident. Dials 2 / 1 / 5, as for the rest of 10.3. What it asked
held:

- **One look:** the toolbar's outline buttons and the "..." menu's switches, as the canvas has them; the note in the
  sidebar's small text, the backoffice's muted colour until something was sent.
- **Every state composed:** the note before sending, off and on; saved, with Run actions... or its actions running or
  waiting for a confirmation; no test entry found; the run refused. Each is one line above the frame, so the frame
  never moves.
- **Pre-flight:** no emoji, no gradient, no new motion; every string in copy.md's voice: "test entry", never "dummy".

## impeccable

Operate mode, a refinement inside Umbraco's design system. The critique it guides kept the sidebar without a primary
action: sending the form in the frame stays the action, and Fill with sample answers sits with the widths as the one
thing a user does often. The switch that makes emails go out lives in the "...", off each time the preview opens, and
the note says plainly what sending does before anything is sent. Run actions... asks first, naming each action and whom
it reaches, because its effects leave the backoffice.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| a test entry that ran its actions like any other | none until asked, the note saying so | an editor trying the form must never mail a customer by accident |
| Run actions... with a bare "Are you sure?" | the confirmation lists each action and whom it reaches | the user sees what will go out before it goes |
| the button spinning while the dialog asked | still while asked, waiting only while the run goes out | a spinner means work under way, not a question open |
| the note following the switch | the note following the ticket the frame holds | between the switch and its ticket, sending still does what the old ticket says |
| sample files dropped into the fields at once | given once the runtime has taken the form on | the runtime loads after the preview script; a file before it is a file nobody hears of |
| a sample upload of a text file | a file of the first kind the field accepts | a field takes images and PDFs by default; a text file would be refused |
| every "sent" the frame says heeded | an id as the public API makes one, once, ten for one document | a script in the frame cannot keep the session alive with reads |

## The words

English and German for every new string, in `lang/preview.*.ts`, which load with the form workspace (ADR-0201); Run
actions... names each action as the Actions view does, with the Actions view's own words, which load with it. copy.md
has each string.

## A reader's review

A reader who was given the part's diff and ADR-0204, not these notes, found seven things and no regression on a
visitor's path. Each held in the code, and each is fixed with a test:

1. **A draft that changed how a field stores its values could not send** (a published number turned into a text): its
   catalogue row stays the published versions', and writing a text into a number's row failed with a 500. Such a field
   now keeps no value in a test entry, its answer checked and the outcome shown as for any other (ADR-0204 says so as a
   consequence); an integration test sends one, and a unit test holds which fields are left out, a grid row moved to
   another grid among them.
2. **Two test entries at once could fail** adding the same catalogue row. A row another submission added a moment
   before is taken as it is, and the catalogue read again; a proxy of the repository makes the stale read in a test.
3. **Switching Run actions for test entries off was not immediate:** the note followed the switch, while the frame kept
   its ticket until the new one came. The note now says what the ticket the frame holds does, so between the switch
   and its ticket it still says the actions run.
4. **A form that stores no entries kept every test entry**, even one whose rules chose no action to wait for. Only one
   whose actions could run waits now; the other goes at once.
5. **The frame's "sent" messages were heeded without bound:** a script in the frame could post a new id every minute,
   each one a read with the user's token. The backoffice now heeds an id only as the public API makes one, a UUID,
   each once, and ten for one document.
6. **A test that didn't test its name:** the visitor's receipt went to a form never published, so it was refused before
   the new guard. The form is now published first, and the answer is the guard's `404 not_found`. tests/README no
   longer says this fixture holds places and unique answers, which the repository's tests hold.
7. **A test entry waiting for its address to be confirmed** said its actions were running. The answer now says it waits
   (`awaitsConfirmation`), and the note says its actions run once the address is confirmed.

It also checked and found fine: the ticket's flags and the refusal of the first layout, the permission on each
endpoint, Run actions... running once under concurrency, the answer's shape the script reads, and the confirmation and
the note rendering text, not markup.

## The tamper round

79 tampers, each taking back one rule of the part in a throwaway clone of 3e943b32 with the part's files copied in,
its client built and its base checks passing first (48 client tests, 40 integration tests and 158 unit tests); a
tamper counts as caught only when a test fails. 73 were caught at once. Five did not compile, as each left a branch
unreachable, compared a value with itself, a parameter unused or a collection with no type; written so that they
compile, all five were caught. One was missed:

- **A visitor's receipt finding a test entry:** the test asked for the visitor's receipt with a challenge of its own,
  which the frame's entry never matched, so the receipt stayed unfound whatever the rule said. It now asks with the
  frame's challenge, the one that made the entry; with the rule taken back the receipt comes back `200`.

That leaves 79 tampers, all caught.

## What the journeys and the tests found

`preview.spec.ts` in Edge, against the demo built from the working tree, and the tests written beside it:

- **Sample files went to nobody:** the preview script loads before the runtime, so the touch it made to wake the form
  came before anything listened, and no file field woke. It now waits for the runtime's `jfb:ready` for each form
  before it wakes it.
- **The sample upload was refused** in the integration test that first tried a text file: a file field takes images and
  PDFs unless told otherwise, so the script gives the first kind the field accepts.
- **A consent without its wording** posts no hash, and the page check refuses its tick; the journey's consent has its
  wording, as a published form's must.
- **The upload store refused the preview's file** for a form never published; a preview's file now needs the form, not
  its publish, and the store still refuses one in the trash.
- **The receipt's shape:** the script first read the submission id from the answer's top, where the public API answers
  it inside `receipt`; the client test now answers as the API does.
- With those fixed, every field type, a file, a signature, a grid, a ranking and a repeatable group among them, sends
  as the samples fill it; the test entry runs no action, Run actions... asks and then the team's email arrives in the
  pickup folder; with Run actions for test entries on, the next one's email arrives at once; axe finds nothing in the
  sidebar.

## The checks

On the working tree, after the last change:

- `node scripts/fix.mjs`; the client's typecheck and lint; the whole client suite, 5,411 tests in 155 files; the
  journeys' check (their typecheck, formatting and support tests);
- `dotnet format --verify-no-changes`; the unit suite, 8,472 passed (16 skipped, as before); the integration tests of
  the preview, the public API, the permission matrix, both OpenAPI snapshots, the submissions, entries, files, the
  field catalogue, the email and outcome previews and every rendering test with its fixtures, 596 passed;
- the size check: the start 42.16 KB of 64, the largest file 45.13 KB of 64, the bundle 389.88 KB of 512, and the
  runtime with every feature 59.80 KB of 60, which the preview script, a build of its own, adds nothing to;
- the ASCII scan of the changed files, and the name scan, with no hits;
- `preview.spec.ts` in Edge, its three journeys passing, and the look shots above.
