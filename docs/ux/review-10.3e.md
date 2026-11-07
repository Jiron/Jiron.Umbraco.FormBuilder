# Step 10.3e: the preview, its frame, its ticket and its choices

ROADMAP 10.3e has Preview in the form workspace's footer show the draft as visitors would get it, rendered by its theme
in a frame that cannot reach the backoffice, at the designer's widths, light or dark and in another language
([ADR-0203](../decisions/0203-the-preview-frame-and-its-ticket.md), amending ADR-0012 and ADR-0084):

- Preview saves what is not saved yet, as Publish... does, and opens Umbraco's full-screen sidebar with the saved draft
  in a frame sandboxed without `allow-same-origin`, so its origin is the opaque `null`;
- the server renders the draft as a page of the site renders a form, its theme, views and runtime included, with a
  ticket that lets the frame's own requests reach the public API for that form and version for ten minutes;
- the widths are the canvas's, and the preview opens at the canvas's; dark keeps the answers given; another language
  renders the draft again;
- a draft whose rules have errors is not shown, and the preview says where they are listed.

Sending from the preview, test entries and sample answers are 10.3f's: until then the entries, uploads, drafts and
receipt endpoints refuse a preview's request.

Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.7, the wireframe
`wireframes/preview.html`, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8),
so every choice the skills would have asked about was decided here or in the ADR.

## What was looked at

Edge, through a scratch spec that is never committed: the footer and the preview at 1440 and 375 px, light with
English and dark with German, with the "..." open, the page turned dark, and the draft in German. The form sits in its
own theme on the page's ground at the width chosen, the frame's outline the only line around it; dark turns the page
and the theme's dark colours without a new render, the answers kept; at 375 px the preview opens at the canvas's
Desktop and says "Narrowed to fit" ("Schmaler als gewählt"), with the "..." below the widths where they leave no room.
A light page in a dark backoffice, and the other way round, read as two things, as they are.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's design system, for a designer who wants to see the form as visitors
will get it, at a phone's width or in German, before publishing it. Dials 2 / 1 / 5, as for 10.1a to 10.3d. What it
asked held:

- **One look:** Umbraco's full-screen sidebar; the canvas's own width switch and "...", the same words; the form in its
  own theme on the page's ground, the backoffice's colours only around it.
- **Every state composed:** loading, the frame, a draft whose rules have errors, no answer with Try again; each in the
  frame's box at the width chosen, so nothing jumps when the frame comes.
- **Pre-flight:** no emoji, no gradient; the only motion is the "..." menu's, the canvas's, which reduced motion keeps
  to a fade; every string in copy.md's voice, "Preview of Contact us", never "live".

## impeccable

Operate mode, a refinement inside Umbraco's design system. The bundled engine's `detect` found nothing in the sidebar or
its words (`[]`). The critique it guides kept the sidebar without a primary action, as IA 5.7 has it: sending the form
in the frame is the action, and Close is Umbraco's; it kept the "..." for the two choices a visitor never makes, so the
toolbar holds the one that matters most, the width; and it hid what cannot apply, the scheme for a theme without the dark
one and the language on a site of one.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| the form in the browser's serif | the system's sans-serif on the preview's page | a site's page gives the form its font; the browser's default misrepresented every theme |
| a border round the frame | an outline | the frame's own window is the width chosen, 375 px to the pixel, so a phone's media queries match |
| dark as a new render | the class switched inside the frame | the answers typed stay; a render for a look would lose them |
| a new ticket every few minutes | a new one only when a request needs it | an open preview nobody uses never renews the session (§4.10) |
| one frame for every document | a new frame for each | no history of another document stays in the frame |
| asks and answers on the frame's window | a port of their own, handed to its first document | a page the frame goes on to can neither ask nor hear |
| the page's ground left to the browser | its colours said outright | a check, and a site's own stylesheet, read the ground the browser paints |
| a width chosen in the preview kept for the canvas | the preview's own | trying a phone in the preview leaves the canvas where the designer left it |
| "Loading..." above the stage | the words in the frame's box | the box keeps its place when the frame comes |

## The words

English and German for every string, in `lang/preview.*.ts`, which load with the form workspace (ADR-0201); the footer's
Preview is the designer's, at the start, and the widths, "Narrowed to fit", Language, Try again and the failure sentence
are the canvas's and the outcome preview's own. copy.md has each of them.

## A reader's review

A reader who was given the part's diff and ADR-0203, not these notes, found eight things. Each held in the code, and
each is fixed with a test:

1. **Buttons that posted to a real address.** `At(id)` cut a `#` out of `about:blank` that wasn't there, so the
   summary's Change buttons, a repeater's Add and Remove and a ranking's up and down had the bare id as their
   `formaction`, which resolves against the backoffice page. The form and every button now have no address to post to,
   as the headless element's (ADR-0128); the API test checks there is no `action` and no `formaction`, a ranking's
   buttons among them.
2. **A post that blanked the frame.** With an `action`, the runtime fell back to a post after Send was refused, on
   stepping onto a summary page and after a failed swap on `form_changed`. Without one it never posts, as for a
   headless form, and shows its notice in the form instead.
3. **An upload aborted while it waited still went.** Under the XMLHttpRequest standard, `abort()` before the real
   `send()` changes nothing, and the test's fake request had it reset. The fake now does what the standard says; the
   script sends such a request and aborts it at once, so its listeners hear "abort", and the ticket that comes later
   sends nothing.
4. **A publish in another tab broke an open preview.** The page check read the aliases of the revision the page posted.
   A preview's are now its ticket's version's, whatever it posts (tests: a publish meanwhile, and a published form's
   draft checked at revision 2).
5. **A page the frame went on to could get a ticket.** Counting `load` events left a gap: a new page's scripts run
   before its own `load`. The backoffice now hands the first loaded document a port of their own and answers nothing
   else.
6. **A frame could keep the session alive** by asking again and again. The backoffice now answers only once the last
   ticket is about to run out (the frame's two minutes, with 30 seconds' slack between the two clocks), one ask at a
   time; the document's answer says how long its own ticket counts.
7. **Two records printed the ticket.** The document, its result and the answer now print their names alone, with a
   test.
8. **Tests that were missing:** the test member's view of a form for members; a page check on a published form's
   draft; a ticket from a third origin refused; and the renewal in Edge, on Playwright's clock. The render's two other
   preview guards, no outcome for a `jfb-sent` token and no member's values, can't be told apart from their absence
   through the preview's only caller: its request carries the backoffice user, never a member, and an address the
   backoffice writes. They stay, as the preview's intent.

Its smaller point, that a width chosen in the preview moved the canvas's, is gone too: the preview opens at the
canvas's width and keeps its own.

## The tamper round

56 tampers, each taking back one rule of the part in a throwaway clone of 591a9eba with the part's files copied in,
its client built and its base checks passing first (24 client tests, 11 integration tests and 56 unit tests); a tamper
counts as caught only when a test fails. 51 were caught at once. Three did not compile, as each left a parameter or a
helper unused; written so that they compile, all three were caught. Two were missed:

- **A second port taken:** the script's test handed a second port after the first had done its work, so nothing asked
  over either. It now asks again after the second port, and holds the ask to the first.
- **A ticket kept by a cache:** Umbraco's management API answers every request `no-store`, the entry files' among
  them, so the preview's own header changed nothing. It is gone; the tests still hold both answers to `no-store`.

That leaves 55 tampers, all caught. The two render guards the reader named have none, as no request through the
preview reaches them.

## What the journeys found

`preview.spec.ts` in Edge, against the demo built from the working tree, after the reader's fixes:

- **The frame's title:** Playwright's `frame.title()` read nothing in the sandboxed `srcdoc` frame, though the
  document has its title; the check reads `document.title` in the frame.
- **Axe in the frame, dark:** the labels' contrast. The page's ground took the browser's dark canvas without saying
  so, and a check of the page read white behind light text. The ground now says its colours, the system's `Canvas`
  and `CanvasText`, which follow the scheme shown; axe finds nothing in the frame, light or dark.
- **The renewal:** a clock moved by Playwright reaches the backoffice but not the sandboxed frame (the frame's moved
  five seconds while the page's moved eight and a half minutes), and moving the backoffice's alone ran out Umbraco's own
  session. The test instead answers the preview's read as for one opened nine minutes ago, with a minute left of the
  ticket for both sides: the frame asks over its port, the backoffice gets a ticket for the version shown, and the
  challenge goes with it from `null` and is taken.
- **The client suite** found the History views' tests reading the clock as it was: after midnight a version minutes
  old was "Yesterday". They run at a fixed midday now, as the Actions view's do. The footer's test and the frames' test
  count the Preview button and the preview's frame, each frame with a check of its own.

## The checks

On the working tree, after the last change:

- `node scripts/fix.mjs`; the client's typecheck and lint; the whole client suite, 5,390 tests in 154 files; the
  journeys' check (their typecheck, formatting and 37 support tests);
- `dotnet format --verify-no-changes`; the unit suite, 8,467 passed (16 skipped, as before); the integration tests of
  the preview, the public API's frame, challenge and page check, the permission matrix, both OpenAPI snapshots and
  every rendering test with its fixtures, 264 passed;
- the size check: the start 42.13 KB of 64, the largest file 45.16 KB of 64, the bundle 387.44 KB of 512, and the
  runtime with every feature 59.80 KB of 60, which the preview script, a build of its own, adds nothing to;
- the ASCII scan of the 61 changed files, and the name scan, with no hits;
- `preview.spec.ts` in Edge, both journeys passing (run before the preview's own `no-store` lines went), and the
  look shots above.
