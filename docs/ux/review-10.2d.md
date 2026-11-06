# Step 10.2d: the webhook, Slack and Teams editors

ROADMAP 10.2d builds the webhook action's editors in an action's sidebar, for a webhook and its Slack and Microsoft
Teams presets ([ADR-0191](../decisions/0191-the-webhook-slack-and-teams-editors.md), building on ADR-0161, ADR-0188,
ADR-0189 and ADR-0190). Per ADR-0019 and ADR-0020 the three design skills shaped it before it was built and ran again
on the result; the design comes from Phase 2 ([information-architecture.md](information-architecture.md), section 5.3,
and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills
would have asked about was decided here or in the ADR.

## What was looked at

Before building, a look of the scratch kind in Edge shot the three sidebars as 10.2a had drawn them: the field mapping
and the headers not drawn at all, five loose settings for what a webhook sends with the own body in a one-line box, and
Slack's and Teams' messages as JSON to edit. ADR-0191 answers what that look found.

After building, the look opened a form with a name, an email address and a message and three actions: a webhook to a
host with a table of two names and two headers (one a reference), a Slack message with words of its own and a Teams
message with none. It shot each sidebar a screen at a time at 1440 px in Umbraco's light and dark themes, the webhook's
at 375 px, the webhook with A body you write chosen, and a credential typed as itself under More headers.

| Shot | What it shows |
| --- | --- |
| [what it sends](review-10.2d/what-it-sends-1440-light.png) | the address with its help and the check's line, then the format and the four ways, the table under its own choice with Add a name and Insert merge tag |
| [a body you write](review-10.2d/body-1440-light.png) | the body started from the table's rows in JSON, under its choice |
| [a credential typed as itself](review-10.2d/credential-1440-light.png) | the secret's two-sentence help, Headers, and "Api-Key carries a credential: give it as a reference, such as …" under its row |
| [a Slack message](review-10.2d/slack-1440-light.png) | the address's help for a chat, A message under its choice, Insert merge tag |
| [a Teams message, dark](review-10.2d/teams-1440-dark.png) | an empty message showing the words it sends as its placeholder, in Umbraco's dark theme |
| [the table on a phone](review-10.2d/table-375-light.png) | each row's name over its value, the remove button beside both |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) inside the sidebar in the journeys: the webhook's with its table, its secret and a header,
  and the Slack message's.
- **The bundled detector** (`impeccable detect --json`) on `webhook-editors.ts`, `webhook-model.ts`, `editor-lines.ts`,
  `action-editors.ts` and the sidebar: `[]`, also with `--no-config` (its configuration, inline ignores and DESIGN.md
  switched off).
- **The journeys**, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for an editor or developer who sends a form's
entries to another system or a chat channel and must not leak a credential doing so; calm, native, every choice saying
what it means. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 1 / 5, as for 10.1a to 10.2c. What it
still asks held:

- **One look:** UUI's select, the browser's own radios as the trigger's choices draw them, the inspector's text boxes
  and code box, the email editor's remove buttons and Add links, Umbraco's Insert merge tag. No colour, motion or icon
  of the package's own.
- **Every state composed:** a table without names says what then goes; a chat without a message says what then goes and
  shows those words; a cleared body, a wrong name, a name twice, a header the request sets itself and a credential typed
  as itself each say why under their box; a way not chosen keeps its values for when it is chosen again.
- **Pre-flight:** no emoji, no gradient, no spinner of its own; every string in copy.md's voice.

## impeccable

Operate mode, a refinement inside the incumbent world, shaped before it was built (`shape`, its questions decided in
ADR-0191) and critiqued after. The critique ran as two isolated assessments (method: dual-agent): a design review that
read the code, IA 5.3, copy.md, section 4.13, the ADR and the shots, and the bundled detector (`impeccable detect`, the
bundled engine; no hooks, live server or browser injection, which the owner rules out). The brief, decided rather than
asked: an editor or developer sends a form's entries to another system or a chat channel; they want to see what will
be sent, and must not leak a credential doing it.

The design review scored the first build 29 of 40:

| Heuristic | Score | What it found |
| --- | --- | --- |
| Visibility of status | 3 | problems said as typed, counted on the card; nothing says whether a reference exists until Test |
| Match with the real world | 3 | plain words; "Form fields" (a format), "The site's Slack address" above a box for another, "merge tags written for it" |
| User control | 4 | another way or target and back restores what was there; the focus moves sensibly after a remove |
| Consistency | 2 | the own body had three names; a typed problem and the check's look different |
| Error prevention | 4 | a credential typed as itself never reaches the draft, and Update leads to it |
| Recognition over recall | 2 | the chosen way's box was drawn under the last choice, not its own; no column heads on a phone |
| Flexibility | 3 | four ways from simple to raw; the table starts with every field, a body from the table |
| Minimalist design | 2 | the reference's example said four times on one scroll; the secret's help four sentences, for chats too |
| Error recovery | 3 | every line says what to do; the address's check line repeats its help |
| Help | 3 | each setting answers its question; the secret's help had HMAC jargon and a path that is no link |

The detector found nothing, which matches the review: its findings are about places and words, not patterns.

What was done:

| Finding | What was done |
| --- | --- |
| The chosen way's box sat under the last choice, tied to its radio only by a faint line | the box is drawn under its own choice, the choices after it following |
| The reference's example four times on one scroll | once a box: the address's help and a credential's or secret's line under its box have it; the headers' and the secret's help do not |
| The secret's help four sentences, with HMAC and a path, and drawn for chats | two sentences on what it is for; the signature's working stays in `docs/webhooks.md`; a chat's sidebar draws no secret, which a chat never checks, and its address's secret comes back with it |
| "The site's Slack address" above a box for another address | the targets are An address of its own, Slack and Microsoft Teams; the address's help says where an empty one goes |
| On a phone a row's boxes had no names and its remove button stood by the value alone | the boxes show their placeholders (Name, Value) where the columns' heads go, and the remove button stands beside both |
| "Form fields" as a format, beside the form's own fields | URL-encoded form data |
| The own body had three names | A body you write, as the choice, the setting and the words for who reads it |
| "merge tags written for it" | "Merge tags are escaped for the format above." |
| A row without its name was "Remove 3" | "Remove row 3", "Value of row 3" |
| The table's group was named "What it sends", inside the fieldset of that name | named by its choice; the headers' by their label |
| "More headers" | Headers |

Questions skipped: the owner answers no questions during the run (CLAUDE.md rule 8); each was decided here.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| five loose settings for what a webhook sends | one choice of four ways, each with its line, the chosen way's box set in under it | a choice says what it means once; settings that only make sense together are drawn together |
| the chosen way's box after the last choice | under its own choice, set in to its words | proximity is the tie; a line on the left alone is a faint one, in the dark theme above all |
| a phone's rows: two unnamed boxes, the remove button by the second | the boxes' placeholders name them where the heads go; the remove button stands beside both | a control belongs to what it stands beside |
| the format and Send metadata too set in like the chosen way's box | flush with the box | the line on the left says "this belongs to that choice"; the format belongs to the whole box |
| a chat's message as JSON in a one-line box | a text box in the chat's own formatting, the JSON written by the server | an editor changes words, not escapes |
| a chat's empty message box | the words it sends as the box's placeholder, and a line that says empty sends them | an empty box hides what goes out |
| Insert merge tag on every row of the table | one under the table, into the value last in focus | one control instead of a column of the same button |
| a credential typed as itself kept, for the check to refuse | said under its box once it is left, never kept in the draft | the draft is saved; a secret must never be |
| — | no transition when a way or a target is chosen; its box replaces the other at once | chosen a few times while setting up; motion would only delay the box it brings |
| — | the remove buttons 32 px, named for their row | WCAG 2.5.8 and a name a screen reader can say |

## The words

English and German for every string: the setting labels and the help of the signing secret and the headers with the
types' names in `lang/field-types.*.ts`, which the server's schema names; the editors' words and the test's in
`lang/actions.*.ts`, which load with the view. copy.md has the ways of sending, Add a name and Add header, a credential
typed as itself, the test's answers and the card's line of an address that is a reference.

## Kept as they are

- **A typed problem and the check's look different:** what was typed and can't be kept is said as the inspector says it
  under every box (ADR-0170), always an error; the check's lines carry their weight's sign, as a warning or a hint can
  be one (ADR-0181). The webhook's boxes say them as every other box does.
- **Insert merge tag for the table on a line of its own,** under Add a name, its name the inspector's: Umbraco's picker
  opens its panel under its button, which beside Add a name would squeeze it on a phone, and the tag lands in the value
  last in focus, which takes the focus, so the editor sees where it went.
- **Whether a reference is in the site's configuration** shows at Test, which now names the key it lacks; the
  integrations screen (12.4a) says it for the site's own, and a lookup for any reference would read the host's
  configuration from the sidebar, which is the Settings section's to show.
- **What happens says "Webhook"** for an action made from the Slack preset, whose card says "Slack message": a preset is
  where an action starts, and its type is what it stays (ADR-0188); its target says Slack under it.
- **A chat keeps its headers:** a chat ignores them, but a proxy in front of one may need them, and they hold nothing
  that says something wrong.

## A reader's review

A reader who had not seen the work read the three work commits against the step, ADR-0191, IA 5.3, copy.md and
`docs/webhooks.md`. They found no way for a visitor's answer to become markup in a Slack message, nothing a user typed
drawn as HTML, the client's rules for names, headers and references the server's own, and a test's `args` naming a key,
never a value. These findings were fixed, each with a test:

1. **A secret written as itself before this step** (the signing secret was a plain text, and a draft saves with the
   check's errors; an import or the API can write one too) was shown to who may only read the action. A value where only
   a reference belongs is now said to be there, never shown.
2. **A host's lock on the target** drew the address with the plain text box, which kept a Slack address typed as
   itself, and the sidebar read the draft's target rather than the host's, so What it sends drew the wrong ways. The
   address has its own editor (`webhookUrl`) that keeps its guard alone, and the sidebar reads a locked setting's value
   as the inspector does.
3. **A credential's row could pass through the draft** while its name was typed ("Au" before "Authorization"), on its
   way to the check. A row reaches the draft once its box is left, and the headers' help names the words that make a
   credential's name.
4. **A test of a webhook without its address** said the site had no address for "this chat". The answer names the
   target (`no_address` with it in `args`), and the toast says the webhook has no address yet.
5. **The metadata switch did not come back** after Only that something happened. It does.
6. **A draft from before could send an empty body** (no answers and no metadata, with no switch shown for it). Without
   answers the metadata now goes, whatever its switch says.
7. **A host's lock on one way's setting** dropped the table and drew a chat's message for a webhook of its own. The
   table is drawn, and only that target's settings.
8. **Choosing the table overwrote a mapping the draft held** under a body. It is kept.
9. **The table started from more and less than Every answer sends** (a field in a repeatable group; not a sensitive
   field the action names). It starts from what the server sends.
10. **A body to start from broke on literal text** (a quote in JSON, `&` in XML or a form), and the Teams body put the
    whole message in one text block. The text outside the merge tags is written for the format, and Teams gets a block a
    line.
11. **Fix it of a setting its way does not draw** fell back to the name; it lands on the box's chosen way. A message's
    problem stayed after another way was chosen and held Update back with nothing to show; it goes with its box.
12. **Two answers side by side** in a Teams message could form a link (`[Open]` and `(https://…)`). A visitor's `]` is
    followed by a zero-width space, so no link's words end in an answer.
13. **The box editors were not tied to the webhook:** the schema reader allowed any type's settings within them, which
    the client finds by the webhook's names. The reader now takes those names only.
14. **A screen reader heard each way's line twice** (in the label and as its description); each radio is named by its
    name and described by its line.
15. **Copy:** "merge tags written for it", a repository path in the secret's help, "it stays" without saying what, the
    own body and "Send no answers" each with two names, and the German missing-key line; all said again.
16. **Tests that passed without what they claimed:** the cleared body now checks what is kept; Fix it now checks where
    the focus lands for the address, the format, the table and a body not drawn.

## The tamper round

In a throwaway clone (ADR-0117), on `b2cb77c0` after both reviews' fixes, each change took back one rule of the step,
one at a time, after every check had passed untampered, each under a time limit and with the file restored after it.

- **The client, 44:** the model (the way of sending as the server reads it, references, the header and mapping rules,
  where the table and a body start: 12), the editors (a target's values kept, the preset's message, a chat's address and
  the signing secret kept only as references, where the table starts and what it keeps, the metadata after no answers, a
  row kept once it is left, the focus after a remove, the value a tag goes into, the box under its choice, a way named
  by its name, the address alone under a lock, secrets hidden from who reads: 23), the sidebar (a host's lock read, Fix
  it of a way not drawn, a webhook's tags: 3), the test's words (a host's status, a missing key, an address of its own,
  the type handed on: 5) and the card's line of a reference (1). All 44 were caught. One first came back "no test ran":
  the tamper itself left an `else` without its `if`, which broke the file rather than the rule; written again, it was
  caught.
- **The server, 18:** a chat's message (the Teams card's lines and their spacing, the test's line and its German, an own
  body winning, its own address ignoring a message, the Slack guard, the presets as messages: 8), the test's `args` and
  the missing key in the run's message and the test, a header's missing secret, a test with nowhere to go naming its
  target, the metadata without answers, the Teams guard for two answers side by side (7), and the schema reader's
  boxes (3). All 18 were caught. Two first broke the build rather than a test (an unused variable and an unused
  parameter, which warnings as errors refuse); written again so they compile, both were caught.

## What the journeys found

`webhook-editors.spec.ts` has four journeys, each against the stub on 5560:

1. **A webhook:** Add action, Webhook; its own address, the stub's; The names and values you choose starts with every
   field by its alias, one renamed, one added and one removed; under More options the signing secret and an
   Authorization header, each typed as itself first (said under its box) and then as a reference. Test, after its
   question, sends what the table says, the header from the host's secret, signed; the toast says "Webhook: Test sent.
   The server answered 200."; Add and Save draft keep the settings as the editors set them, every secret a reference.
2. **A Slack message:** a Slack address typed as itself is said under the box and not kept; a reference is. The
   message, with words of its own and merge tags, goes to the stub as Slack's `text`, its first line "This is a test."
3. **A Teams message:** a reference, the preset's message; the stub gets an Adaptive Card, a text block a line, the
   test's line first and the blank line after it the space before the message.
4. **Fix it:** the form check's problem with a merge tag in the table leads to its sidebar, the focus on the table's
   first name, the problem under the table.

`form-actions.spec.ts`, `form-email.spec.ts` and `action-test.spec.ts` ran with them, and the existing Fix it of a webhook's address lands on
the new address box: all passed.

## The gate

The gate runs on the commit of this review in a throwaway clone (ADR-0117), with its clean clones and the whole browser
suite; WORKLOG has its result.
