# Step 10.3a: History, its list and the comparison

ROADMAP 10.3a replaces the form's History "coming" screen with its view: the published versions newest first, the draft
saves on request, each row's when and by whom, what changed, comment and pin; a row's "…" with Pin and Add comment…;
Compare for two ticked versions; and a version compared with the current draft, in plain language
([ADR-0196](../decisions/0196-the-history-view-and-the-comparison-in-words.md), building on ADR-0045 and ADR-0052). Per
ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again on the result; the design
comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.6, the History wireframe and
[copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would
have asked about was decided here or in the ADR, `impeccable`'s discovery questions included.

## What was looked at

Before building, the server had kept every version with its comment and pin since 4.5b, and compared two versions as
paths and JSON values (ADR-0045), but the History view was a sentence. The skills' discovery questions were answered from
IA 5.6, the History wireframe and copy.md's state labels.

After building, and again after the reader's review, a scratch look in Edge shot a form published twice and saved
twice more, in light and dark, English and German, at 1440 and 375 px: the list, the draft saves shown, two versions
compared in the sidebar, a row's "…" and the comment dialog, and at 375 px the view by itself as well.

- **The list** reads as one table in Umbraco's box: the version's name bold with the green "Published" chip beside it,
  when and by whom small and muted under it, what changed and the comment in their columns, the "…" at the row's end.
  The draft saves are muted, the current draft first, and the line under the list says what is kept for good.
- **The sidebar** says "Fields" and "Settings" as small headings, what was added and removed as a short list, then a
  table of what changed with the older value muted. In German every word is German except the form's own labels.
  At 375 px each row stacks under its name, each value with its version's name.
- **The dialog** is Umbraco's small one: the label, the box, the help under it, Cancel and Save.
- **At 375 px** Umbraco keeps its tree beside the workspace and the page scrolls sideways, as everywhere in the
  backoffice; the view itself gets about 620 px, where the comment folds under what changed and no text is squeezed.

Nothing in the shots needed changing beyond what the reader's review found.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for a designer who wants to know who changed what in
a form and when, and what any two versions differ in, without reading JSON; calm, native, a table that reads at a
glance. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a to 10.2f. What it still
asks held:

- **One look:** Umbraco's box and table as the library's folder list draws them, the "Published" chip in the header
  chip's colours, Umbraco's checkbox, toggle, "…" popover, dialog and medium sidebar; the muted text token for when
  and by whom, the draft saves and "none". No colour, motion or icon of the package's own.
- **Every state composed:** reading (a skeleton that names nothing until the words are loaded), a read that fails (Try
  again), nothing published yet, one version, many (Show more), draft saves shown, a version that is also the draft,
  two ticked (Compare), a comparison reading, failing, unreadable and without differences.
- **Pre-flight:** no emoji, no gradient, no spinner of its own; every string in copy.md's voice.

## impeccable

Run only through its bundled engine (no downloads, hooks or live mode). Its context step loaded PRODUCT.md and
DESIGN.md; its discovery questions were answered from IA 5.6, the wireframe and copy.md, as above. Mode: Operate, a
refinement inside Umbraco's design system. `impeccable detect` on the view, the sidebar and the dialog found nothing,
before the reader's review and again after it.

Its critique, on the shots, decided here:

- **Hierarchy:** the version's name is the row's anchor (bold, the row's header), the chip and pin beside it, when
  and by whom under it in the small muted size; what changed reads before the comment, which is quieter. Compare is
  the view's only outlined button and appears only when it can act; the switch sits at the toolbar's end.
- **States:** every state in the list above was shot or tested, the two failures added by the review included; the
  empty state says what fills the list and how to see the saves until then, in one sentence.
- **Colour and contrast:** the backoffice's tokens only, the package's muted token for the quiet text (mixed from
  Umbraco's colours to meet AA in each theme, tokens.md) and its danger text for a failed Show more, the chip in
  Umbraco's positive colours; nothing else of the package's own.
- **Targets:** the "…" is 32 px, the boxes Umbraco's own; the menu's items 36 px high.
- **Phone:** Umbraco's backoffice keeps its tree beside the workspace at 375 px and scrolls sideways; within the
  view, the comment folds under what changed below 56 rem, and the sidebar stacks each row under its name below
  32 rem, so no value is squeezed into a third of the width.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | no animation on the list, the ticks or Compare appearing | the list is read, not played with; Compare appears where the eye already is, at the toolbar's start |
| — | Umbraco's own sidebar and dialog motion, nothing added | they are opened a few times a session; their movement is the backoffice's, so the section feels like one product |
| — | the comparison's values side by side, the older muted | the newer value is what the reader looks for; muting the older one reads as "was" without a word or an arrow |
| — | a narrow sidebar stacks each row, its values named by their version | a third of 375 px holds a word or two; stacked, each value keeps the sidebar's width |

## The words

English and German for every string, in `lang/history.*.ts`, loaded with the view; a property that has a name in the
Settings view or the inspector keeps that name. copy.md has the History list's words, the empty state, the comparison's
"No differences." and the comment's help.

## A reader's review

An outside reader, given the step's code and the server's comparison code but not this review, went through the view,
the words and the dialogs. What it found, and what changed:

- **The words filled in defaults** for a value one side lacked, "a switch is off unless said". The canonical form
  writes every switch, number and choice (ADR-0034), so a side without one is a side without the object that holds
  it: the defaults are gone, and such a side says "none". The tests had used made-up shapes; they now use the
  server's.
- **A publish didn't read the list again.** A publish adds a new live version and leaves the draft where it was, so
  watching the draft missed it; the view now watches the live version too, and the journey publishes from the
  History.
- **Lists said whole**, "a@…, b@…" against "a@…, b@…, c@…": a list of addresses or names now says what was added and
  what was removed. Two values that read alike (rich text that differs only in its markup) say "(changed)" after the
  newer, instead of showing the same words twice. Markup's inline tags no longer leave spaces ("full ." was
  "full.").
- **Options by the wrong side's labels:** an order row said the older side's options by the newer labels; each side
  now names them as it gave them, and options of one value, which the server can't key, by their labels too.
- **Outcomes and pages:** an outcome's row named it by what it does, which its own row changes; it is named by its
  place, "After sending, Outcome 1", and a redirect's page by its name in the site, asked of Umbraco once.
- **Names:** a path segment that is no language was taken for one, and deeper paths lost their names; both are said
  now. In a sentence "today" and "yesterday" are small.
- **The view:** the failure while its own words load showed a key, so it uses the backoffice's start words, with Try
  again; a page that Show more can't read was silent, and is said beside it; the draft saves' controls all read
  "Select Draft save", and now carry the save's moment; the version's cell is the row's header; a version's name
  says that it opens the comparison with the current draft; another form starts the view afresh.
- **The dialogs:** the comment's help was tied to the box by a description that can't reach into the box's shadow
  root, so the box's own name holds it; in a narrow sidebar each value's version name was read on top of its
  header, so it is for sight only, and the table keeps its roles where its rows stack.
- **Words:** German for Unpin and its messages, "keine Angabe" for none, "Verwendet" for a switch in use; English
  "In use"; the pin's message says that the cleanup keeps "this version". German "Try again" now reads the same
  everywhere ("Erneut versuchen"; one Settings key said "Noch einmal versuchen").

One finding didn't hold: the pin's icon was said to be read beside its hidden word, but Umbraco's icon hides itself
where it has no label. The attribute stays, as on the view's other icons, and its tamper was dropped, as it can't
change anything. Another guards a case the server doesn't make today: the current draft first among the draft saves,
which the server's order by number already gives; the view puts it first itself, and a test pins it. Writing the
tests for a form change showed that clearing the draft's and the live version's numbers would lose them where two
forms share them, as the workspace only calls when a number differs; the view keeps them, and a read too many is
dropped.

## The tamper round

In a throwaway clone of the last pushed commit with the step's files copied in, its client built and its base checks
passing, 45 tampers each took back one rule, and each was caught by a named test failing: 2 in the server (a publish
named by who saved it, and the list answering no names) and 43 in the backoffice:

- the words: a text of another language said as the default, an option by its value, the options' order by values,
  an option by the other side's label, a type's setting by its own name, a former alias said again, a removed field
  said as added, rules among the fields, a rule that is off said on, rich text with its markup, an inline tag said as
  a space, a long text whole, a nested setting not said, a value of a list by its code, a list said whole, two values
  alike said alike, an outcome named by what it does;
- a row's names and summary: one field said as a count, settings groups left out, a publish that is the draft named
  the current draft, a publish dated when it was saved, a sentence's "today" large;
- the view: draft saves never asked for, a third box not resting, the newer compared first, the current draft's row
  with a link, a save or a publish not read again, the current draft where the server put it, another form keeping the
  ticks, an unchanged comment sent, the focus left on Show more, a failed Show more not said, a pin not said, an older
  read's versions said last, a draft save named without its moment, a redirect's page by no name, the view's failure
  in words not loaded;
- the sidebar and the dialog: a value without its version on a narrow sidebar, stacked rows without their table, Try
  again reading nothing, a comment kept with its spaces, the comment's help not in its box's name.

The first round, before the reader's review, had two server tampers that broke the build, which proves nothing; they
were rewritten to compile, and then caught. In this round "a former alias said again" first passed: lists are now said
as lines, and the test only looked at the rows; it now checks the whole part, and the tamper is caught. A tamper that
took the pin icon's `aria-hidden` away also passed, and was dropped: Umbraco's icon hides itself without a label.

## What the journeys found

History's journey (Edge, as a designer) found two things after the review's changes, both in the journey itself:

- the boxes are now found by their names ("Select Version 3"), but Umbraco's box draws itself over its input, which
  Playwright then can't press; the journey clicks the drawn box and checks the named input;
- the publish from the History added a new version, as a publish always does, rather than making the draft live: the
  journey now takes the live version's number from the list after the publish, and the view's tests, ADR-0196 and
  this review say it that way.

The UX walk's History and "coming" shots once failed when the e2e host's SQLite database hung while the client's
tests ran beside it (ADR-0011); run alone, all twelve passed.

## The checks

The formatters, the build, `dotnet format`, the unit tests, the integration tests of the version endpoints and the
OpenAPI snapshot, the backoffice's lint, typecheck and tests, the journeys' typecheck and the journeys above (ADR-0194).
WORKLOG has their results.
