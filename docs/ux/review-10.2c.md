# Step 10.2c: Test

ROADMAP 10.2c adds Test to the Actions view: on each card and in an action's sidebar, for the action as the designer
holds it, saved or not, against the sample entry
([ADR-0190](../decisions/0190-test-an-action-as-the-sidebar-holds-it.md), building on ADR-0158, ADR-0187, ADR-0188 and
ADR-0189). Per ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from Phase 2
([information-architecture.md](information-architecture.md), section 5.3, and [copy.md](copy.md)). The owner answers
no questions during the run (CLAUDE.md rule 8), so every choice the skills would have asked about was decided here or
in the ADR.

## What was looked at

A look of the scratch kind in Edge, on a form with a name and an email address and two actions: an email to the team
(an address and the email field's answer) and a webhook to a host. It shot the cards at 1440 px in Umbraco's light and
dark themes and at 375 px, the card's Test and its toast, the webhook's question, the "…" of a card wide and narrow, the
email's sidebar in light and dark and at 375 px, and its Test before and after Update. The UX walk
(`tests/e2e/specs/ux/walk.spec.ts`), run with `JFB_UX_ONLY=actions,action-`, shot the Actions view, an action's sidebar
and a new action's at 375, 768 and 1440 px, light and dark, in English and German.

| Shot | What it shows |
| --- | --- |
| [the cards](review-10.2c/cards-1440-light.png) | Test outlined beside each card's switch, before "…" |
| [a card tested](review-10.2c/card-tested-1440-light.png) | the toast "Email the team: Test email sent to you (e2e-designer@jfb.test)." |
| [the webhook's question](review-10.2c/confirm-1440-light.png) | "Send a test?" naming the place in the card's own line: "Webhook to hooks.example.com. A sample entry goes there as a real one would, marked as a test." |
| [a narrow card's "…"](review-10.2c/menu-375-light.png) | a list narrowed to 22rem: Test first in the menu, none beside the switch |
| [the sidebar tested](review-10.2c/sidebar-tested-1440-light.png) | the footer's Delete action and Test apart, Test outlined; two toasts, each led by its action's name, the second by the name the sidebar holds before Update |
| [the sidebar, dark](review-10.2c/sidebar-1440-dark.png) | the footer in Umbraco's dark theme |
| [the walk's cards, dark, German](review-10.2c/actions-1440-dark-de.png) | "Testen" beside each switch of the demo's two emails |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the cards, the open "…", the question and the sidebar, in the light and the dark
  theme and at 375 px: nothing. The walk's 36 shots: nothing in the view's, the cards' or the sidebars' own elements
  but one, `target-size` on a new action's Verified radio at 375 px in English, which the walk leaves half under the
  sidebar's sticky footer. The look opened a new action at the same size: axe found nothing there as the walk leaves
  it, nor with the radio scrolled into view, so it is the scroll position, not the radio. Umbraco's tree reports
  `label-content-name-mismatch` on its menu items, as on every screen of the section (ADR-0182, OWNER-TODO).
- **The bundled detector** (`impeccable detect --json`) on the list, the sidebar, the view and `action-test.ts`: `[]`,
  also with its configuration, inline ignores and DESIGN.md switched off.
- **The layout measures** of the walk: no box scrolls sideways inside the view or the sidebars, and no word is cut by
  an ellipsis at any width. The window scrolls sideways by 545 px at 375 and 152 px at 768, Umbraco's 920 px page
  (ADR-0182). So a card is 572 px wide at the least in Umbraco's shell, where Test stays beside the switch; the "…"
  keeps it for a narrower list.
- **The journeys**, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors checking that an action does what
they set it up to do, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for
10.1a to 10.2b. What it still asks held:

- **One look:** UUI's outline button and waiting state, Umbraco's confirm dialog, toast and popover. No colour, motion
  or icon of the package's own; the menu's Test takes Umbraco's play icon, as the menu's other items take theirs.
- **Every state composed:** a test that asks first, one declined, one busy, one sent to the tester, one that reached a
  host, a dry run, and each failure with what to do about it; a site that is paused or cannot send mail says so before
  anything waits in a queue.
- **Pre-flight:** no emoji, no gradient, no spinner of its own; every string in copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world. The critique ran as two isolated assessments (method:
dual-agent): a design review that read the code, IA 5.3, copy.md, the ADR and the shots, and the bundled detector
(`impeccable detect`, the bundled engine; no hooks, live server or browser injection, which the owner rules out). The
brief, decided rather than asked: an editor who has set up an action wants to know, before a real entry comes, that it
does what they meant, without fearing that a test reaches the team or a customer's system unasked.

The design review scored the first look 28 of 40:

| Heuristic | Score | What it found |
| --- | --- | --- |
| Visibility of status | 3 | the busy state and "Testing …" began before the question was answered; toasts did not name the action |
| Match with the real world | 3 | plain words; "pickup folder" and "Razor view" are the system's |
| User control | 3 | Cancel in the question; a test never changes the draft |
| Consistency | 3 | three looks for Test (card, footer, menu); a webhook's English sentence in a German toast until 10.2d |
| Error prevention | 3 | the mail reaches only the tester, a webhook asks first; Delete action touched Test in the footer |
| Recognition over recall | 3 | the question names where; stacked toasts alike |
| Flexibility | 3 | Test from the card without opening anything, or before Add or Update |
| Minimalist design | 3 | calm cards; the footer's two buttons read as one block |
| Error recovery | 2 | failures named the problem but not what to do |
| Help | 2 | nothing said what a test does before it ran |

The detector found nothing, which matches the review: its findings are about states and words, not patterns.

What was done:

| Finding | What was done |
| --- | --- |
| A card's Test was busy and said "Testing …" while the question was still open, and Cancel left it unresolved | a test is busy and says so once it runs: after the yes where it asks first, never before (`runTest`'s `started`); a second press still waits for the first |
| Delete action and Test touched in the sidebar's footer, alike in look; Delete acts at once | Test keeps a space from Delete action and is outlined, as on its card |
| Toasts of several tests could not be told apart | each leads with the action's name ("Email the team: Test email sent to you (…).") |
| Failures ended without what to do | mail that cannot leave names the developer's setting, a test that threw points a developer at the site's log, unfit settings say to open the action, and no answer at all says to try again in a moment; a problem the server named does not |
| Nothing said what a test does before it ran | Test's title says it per type ("Sends a test to you alone.", "Sends a sample entry where this action sends, after asking you.", "Shows what a run would do. Nothing is sent or changed."); the question says the sample entry goes there "as a real one would" |
| copy.md had a row of five cells | fixed |

Questions skipped: the owner answers no questions during the run (CLAUDE.md rule 8); each was decided here.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| the button busy as soon as Test was pressed, behind the question | busy once the test runs | a state shows what is happening, not what may |
| — | UUI's own waiting state, no motion of the package's own | pressed a few times a session; the backoffice's own busy state is the one editors know |
| a toast's headline over its reason | one message, led by the action's name | read aloud as one; several stacked stay apart |
| Delete action and Test flush in the footer | a space between, and Test outlined | a control that acts at once never touches a harmless one |
| — | Test moves between the card and its "…" with no transition | a fact of the list's width, not an event |
| — | the menu closes before its Test runs | the toast is the answer; an open menu would cover the card |

## The words

English and German for every string, in `lang/actions.en.ts` and `actions.de.ts`, which load with the view; copy.md
has Test and its title, the question, the toasts, the failures and the test mail's first line. The first line is the
server's, in the tester's backoffice language, as the setup test mail is.

## Kept as they are

- **"Test email sent to you"** while the mail waits for its turn in the queue: the outbox sends it at once and retries
  it as any mail, and "sent" is copy.md's word since Phase 2.
- **A card with a problem still offers Test:** the test says what fails (a host the site does not allow, say), which is
  how an editor finds out; the problems stay counted on the card.
- **Every webhook test asks again:** each reaches a real system (ADR-0190).
- **A card that remembers its last test:** a test is no run; the last run's line and its Details come with 10.2f.
- **The question names the host, not the action:** the card's own line is what the editor sees on the card, and the
  toast that follows names the action.
- **The live region keeps "Testing …" after the test:** a live region speaks when it changes; the toast then says what
  the test came to.
- **A touch card's five controls** (its handle always shows on touch): the handle is how a finger reorders (IA 5.3),
  and a card is 572 px wide at the least in Umbraco's shell.

## A reader's review

A reader who had not seen the work read the three work commits against the step, ADR-0190, IA 5.3 and copy.md. They
found no safety hole: an email's test reaches the tester alone, as a mail marked a test; the confirmation is the
server's, from the type's declaration; a held definition gains nothing over a saved one, its locks applied and its
settings checked; nothing a user typed is drawn as HTML. These findings were fixed, each with a test:

1. **The other types' tests contradicted their toasts.** Set state and the content action, which reach nobody,
   answered "Sent" with "Nothing changed." under it, and a webhook's failure put its bare code in the toast. They now
   answer as dry runs (`dry_run`, their sentence as the preview) and the webhook with `sent` or its run's code under a
   sentence naming it; 10.2d says those in the backoffice's words with their editors (ADR-0190, amended).
2. **A screen reader heard nothing of a test.** Umbraco's toast reads its message aloud and not its headline, and the
   sentence was in the headline; a busy button says nothing either. A toast now says it all in its message, each
   sentence standing on its own, and the list or the sidebar says "Testing …" in its live region while it runs. The
   journeys check the words in Umbraco's announcement.
3. **A test while actions and email are paused** queued a mail that would go out later, unasked, and said it was sent.
   The server now refuses any test that would reach something while paused (`paused`), before it asks for a
   confirmation; a dry run still runs.
4. **The first line named what a run never mails:** a typed address with spaces, one that is none, one twice in
   another case. It names the addresses as the outbox keeps them now. A mail only the visitor gets was tested as the
   team's, with links the visitor's mail breaks; the test is the visitor's mail now, to the tester, and its line names
   the visitor alone, without "in a mail of its own" beside a team's mail that does not exist.
5. **Tests that passed without what they checked:** the "added nothing" journey read the server's draft, which the
   designer never saves on its own; it checks that the view holds no card now. A busy button's end was checked without
   its start; the journey checks that the list said "Testing …". The tester's language over the API, a missing view's
   code, the first line's encoding and the settings checked before the confirmation have tests now.
6. **On a phone** the card's end held Test, the switch and "…". A card too narrow to keep its name readable beside
   Test offers it in its "…" instead, once either way (a container query); in Umbraco's shell a card stays wide enough.
7. **Copy:** the German "Ihr fehlt ein Mailserver" read as the formal "your"; "Try again" was advised where retrying
   cannot help; the ADR's example line and its dialog for a dry run differed from what was built; IA's card sidebar
   footer lacked Test.

A dry run's lines go in the toast, one line each, which keeps the breaks, rather than in a dialog as the ADR first said:
10.2d's dry runs are a line or two, and the last run's Details (10.2f) is where a longer answer is read.

## The tamper round

In a throwaway clone (ADR-0117), each change took back one rule of the step, one at a time, after every check had passed
untampered, each under a time limit and with the file restored after it.

- **The first round,** on `79473001`, after the reader's fixes, had 53: the test runner (8), the email's test (13), the
  other types (4), the endpoint (4), `runTest` and the toast (10), the card (6), the sidebar (4) and the view (4).
  49 were caught. Two broke the build rather than a test, each leaving a `using` unread, and were written again. Two
  were missed: a second press of the card's and of the sidebar's Test ran a second test. Their checks pressed again
  only while the button was busy, and a busy UUI button takes no press, so the guard was never reached; the checks
  now press again before the test starts.
- **The second run,** on `87078ba8`, after the design review's fixes, ran every client tamper again on the changed
  code, 38 with the 14 for what the review changed (the start after the yes, the named toast, the hints, the footer's
  Test), and the two server tampers written again. Every check passed untampered first. All 38 client tampers were
  caught, the two second presses among them. Of the two server tampers, one was caught; the other, the first line
  left unencoded, changed nothing anyone can see: the line holds only addresses that pass the address check, which
  allows no `<`, `>` or quote, and the layout's sanitiser encodes an `&` itself. The encoding stays, as a guard should
  the line ever carry other text.

The footer's space and the container query that moves Test into "…" are the browser's: the look holds them.

## What the journeys found

`action-test.spec.ts` has two journeys:

1. **From the sidebar, before Add:** Add action starts ready to email the team; an address goes in the To box, and
   Test sends the test mail before the action exists. The mail lands in the pickup folder, to the designer alone, its
   first line "This is a test. The real email goes to sales@example.com."; the toast says it, and Umbraco announces it.
   Cancel leaves the designer without a card and the stored draft without an action.
2. **From a card:** a saved email to the team and to the email field's answer. The card's Test, named "Test Email the
   team" with Test as its word, sends the mail whose first line names "ops@example.com, and to alex.berger@example.com
   in a mail of its own"; the list said "Testing Email the team…", and the button is not busy after. Axe runs inside
   the view.

`form-actions.spec.ts` and `form-email.spec.ts` ran with them: all passed.

The first runs found one thing: **Umbraco announces a toast's message** in a live region of its own, so the toast's
words were on the page twice and the journeys' locator found both. The journeys read the toast as the others do, and
check the announcement too. After the reader's and the design review's fixes, the two Test journeys and the look
passed on a fresh build.

## The gate

The gate runs on the commit of this review in a throwaway clone (ADR-0117), with its clean clones and the whole browser
suite; WORKLOG has its result.
