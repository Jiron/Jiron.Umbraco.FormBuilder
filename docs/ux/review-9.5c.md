# Step 9.5c: seeing rules, and validation rules in the builder

ROADMAP 9.5c lets the designer see the form's rules where it looks at the form and all in one place, and gives
form-level validation rules a screen: rule chips on the canvas's pages, groups and fields (none on the selected
element), a rules overview from the canvas's "..." that lists every rule by page, warns of a conflict on its row and
adds a rule after asking what it affects, and validation rules built in the rules builder of 9.5a, whose first line then
reads "Show a message on this field: Email when" with the message's box under it
([ADR-0178](../decisions/0178-seeing-rules-and-validation-rules-in-the-builder.md), building on ADR-0002, ADR-0005,
ADR-0170 and ADR-0175). Per ADR-0019 and ADR-0020 the three design skills ran on the result; the design comes from
Phase 2 ([information-architecture.md](information-architecture.md), section 5.1, the wireframes `rules-builder.html` and
`designer.html`, and [copy.md](copy.md)). The owner answers no questions during the run (CLAUDE.md rule 8), so every
choice was decided here or in the ADR.

## What was looked at

The e2e host (the demo on `127.0.0.1:5520`, ADR-0031) in Edge through Playwright, as the designer, with a form made
through the API: on its first page an email address with a validation rule ("Use your work address" when it ends with
gmail.com), a topic (Shipping, Press, Delivery) and "Which country?" shown when the topic is Shipping or Press; on its
second an order number required when the topic is Delivery and an attachment hidden when the topic is Press but required
when it is Shipping or Press; and a redirect used when the topic is Press. A second form has no rules.

| Shot | What it shows |
| --- | --- |
| [chips](review-9.5c/chips-1440.png) | a chip above Email address and above Which country?, each saying its rule; none on Topic, which has none |
| [selected](review-9.5c/chips-selected-1440.png) | Which country? selected: no chip on it, its rule in the inspector's Rules line; Email's chip shortened with an ellipsis beside the inspector |
| [the menu](review-9.5c/menu-1440.png) | the canvas's "...": Undo, Redo, Rules overview, Keyboard shortcuts |
| [the overview](review-9.5c/overview-1440.png) | a box per page, each row the element's name and its rules in words, the conflict on Attachment's row, the redirect's rule under After sending |
| [a row hovered](review-9.5c/overview-hover-1440.png) | the row's background and its name underlined, as a link |
| [Add rule](review-9.5c/overview-add-1440.png) | "What should the rule affect?" at the top, its help, the list by page, Continue and Cancel |
| [nothing chosen](review-9.5c/overview-add-refused-1440.png) | Continue with nothing chosen: "Choose what the rule affects." under the list, the list marked |
| [no rules](review-9.5c/overview-empty-1440.png) | the overview of a form without rules: one sentence |
| [a validation rule](review-9.5c/builder-message-1440.png) | "Show a message on / this field: Email address / when", the message's box and its help, Try it: "The message shows on Email address." |
| [no message](review-9.5c/builder-message-refused-1440.png) | Add with the message empty: "Write the message visitors read." at the box, the focus in it |
| [the inspector](review-9.5c/inspector-message-1440.png) | Email address's Rules: the validation rule among its rules, as a button that opens it |
| [at 768 px](review-9.5c/overview-768.png) | the overview over the canvas |
| [at 375 px](review-9.5c/overview-phone.png) | the overview the whole width, rows wrapped, nothing cut |
| [dark](review-9.5c/overview-dark.png) | Umbraco's dark theme |
| [high contrast](review-9.5c/chips-contrast.png) | Umbraco's high-contrast theme, the chips on the canvas |

**Checks that ran on the last look:**

- **Axe** (WCAG 2.2 A and AA) on the overview, the overview asking what a rule affects, the overview of a form without
  rules, and the builder with a validation rule, at 1440 and 768 px, on a phone, in the dark theme and in high
  contrast: none, but for the primary button's contrast in the dark theme, below.
- **The bundled detector** (`impeccable detect --json`) on the overview, the builder, the canvas's styles, the canvas,
  a field's element and the inspector: `[]` for each.
- **Widths:** at 1440, 768 and 375 px no control is wider than its row and no sidebar scrolls sideways.
- **The journeys** of `designer-rules-overview.spec.ts` and `designer-rules.spec.ts`, below.

## design-taste-frontend

Read as: Operate-mode product UI inside Umbraco's own design system, for editors finding why part of a form shows, hides
or is required, calm and native. The skill's landing-page rules don't apply (ADR-0019); dials 2 / 2 / 4. What it still
asks held: one accent (the chips are a cool neutral beside the translation chip's amber, which keeps meaning "a text is
missing"; the conflict's warning icon is Umbraco's own warning colour), one radius scale (UUI's), Umbraco's own boxes,
sidebar and buttons, the browser's own select for what a rule affects, every state composed (no rules, a rule off, a
conflict, nothing chosen, a message missing, a rule its element can no longer have), and no motion beyond Umbraco's
sidebar.

## impeccable

Operate mode, a refinement inside the incumbent world. Critique against IA 5.1, the wireframes and copy.md, and what the
look in Edge found:

| Finding | What was done |
| --- | --- |
| A field's chip drawn in the flow under its control, as the wireframe has it, would move every field below it as the field is selected and its chip goes | the chips sit out of the flow at the element's top end, beside the translation chip (ADR-0178) |
| Out of the flow at the field's top end, the chips covered the start of its label | a field's chips stand above its frame; a group's and a page's sit on their lower edge, where no label is |
| The overview's page boxes were headed by an `h5` under the sidebar's `h3`, which axe reported | the boxes' headings are `h4` |
| The sidebars' actions floated up under a short overview or builder, leaving the footer mid-height | the overview and the builder fill the sidebar's height, so the actions sit at its foot |
| "Choose the element the rule shows, hides or requires" left out the field a message is shown on | "Choose the question, group or page the rule shows, hides or requires, or the field it shows a message on." |
| Add rule in the overview's header touched the sidebar's edge, its right border cut at 1440 px, while the headline is inset | Add rule takes the headline's inset: Umbraco's header insets its headline slot but not its action menu |
| In Umbraco's dark theme the primary button's label is under AA's 4.5:1, as every primary button of the backoffice is | already carried into 9.6 (9.4d), to be decided once for every dialog and footer button of the section |

**Noticed:** the chips say the whole rule in a line that shortens on a narrow canvas; the button that selects the
element carries each in full for a screen reader, and the overview has them all in full.

## emil-design-eng

| Before | After | Why |
| --- | --- | --- |
| — | the chips appear and go at once, with the selection | selecting is done tens of times a minute; motion on it would only slow the next click |
| — | the overview and the builder are Umbraco's own sidebars, with their own slide | an editor knows them; a second motion inside would compete with it |
| — | a row's hover is a background and an underlined name, no transition | it is a list read at speed; the change must keep up with the pointer |
| — | a row closes the overview with the focus on the button that selects its element, set once the sidebar is gone | the dialog gives the focus back to its opener as it closes, which 9.5a found undoes a focus set earlier |
| — | Cancel in Add rule puts the focus back on Add rule | a step taken back must leave the keyboard user where they started it |
| — | Continue with nothing chosen says so under the list, the focus on it | a disabled Continue would not say why |

## A reader's review

A reviewer read the whole change with fresh eyes, both runtimes' engines and the server's structure check beside the
client. Each finding was taken up; each fix has a test:

| Finding | What was done |
| --- | --- |
| "Show a message on" was offered for a name, an address, a grid and a repeatable group, whose answers neither engine compares, and publishing accepted such a form, which both runtimes then refuse whole | the builder offers it only for a field whose answer rules compare and the visitor gives, and the structure check refuses the rest at publishing (`unknown_field`, or `invalid_reference` for a value kept on the server) |
| A conflict could be said for answers no visitor gives: an option the choice lacks, no language, member groups without signing in | only such answers are tried: a choice's own options where the form has them, the site's languages, groups only signed in |
| Two answers that each conflicted while the other stayed were both left out, and the conflict said "whatever the answers" | answers are left out only where they conflict whatever they are together |
| A text the rules don't name was tried as a made-up word, which would have been said as "is zz-other", and for a rule looking inside a text it stood for nothing | it is tried only where the rules test a text for being equal, and said as "is none of" the texts they name |
| Files were said as "has at least 2 files" for a conflict found at exactly two | "Upload has 2 files" |
| Add rule offered a group that already decides whether it shows, where the inspector offers no Add a rule | the same test as the inspector's |
| A validation rule on a grid's row (written through the API) was in no list, and deleting a field left the validation rules that show their message on it naming nothing | a row's rule is listed and opened on its grid and stays on its row; a delete takes them along, in one step of undo |
| The overview worked out every conflict on each render | once, as it opens |
| A rule whose element can no longer have its effect (after a type change) opened on another effect without saying so | it opens with its effect shown and the first line saying why it can't be kept until another is chosen |
| Two helpers and a term nothing used | removed |

## What the journeys found

`designer-rules-overview.spec.ts` finds a rule's chip on the element it affects and none on the selected one, opens the
overview from the canvas's "...", finds the hide and the require rule that hold for the same answer warned on their row
(axe clean), follows a row to its element, adds a rule from the overview after choosing what it affects, and finds the
empty overview's sentence; then builds a validation rule on the email address with its message in the builder,
publishes, and on a page of the site's origin the form shows the message at the field for a gmail.com address and sends
once the address is another. `designer-rules.spec.ts` of 9.5a passed beside it.

| Failure | What was done |
| --- | --- |
| The inspector's Rules section was closed, so its rules were not in the page to read | the journey reads them from the section's header, which says them in one line |
| At 768 px the inspector covers the canvas, so the overview's row could not be shown beside its element | the look closes the inspector first, as an editor would |
| A row's text starts with the whitespace of its markup, which an anchored pattern does not trim | the patterns allow it |

## The tamper round

The round ran in a throwaway clone of the work (ADR-0117): 82 changes to the conflict search, the overview, the list of
rules, which fields take a message, the words, Try it, the landing, the builder, the canvas's chips, the editor model's
delete, the Design view, the inspector and the server's structure check, each run against the tests that should notice
it. 66 were caught at once. Of the 16 that went unnoticed, one found a bug: the guard that keeps a text another answer is
compared with from standing for every text the rules don't name named the field by its key where the subject carries
`field:` before it, so it never held, and a field whose answer another looks inside ("does not contain the answer to
Nick") claimed a conflict no visitor meets; it now holds, with a test. Four showed code that did nothing and is gone: a
visit in member groups marked signed in (the engine reads each part of the visit alone), the overview leaving a field's
message rules out of the search (which passes them by), a message's clash refused before the element's own rules were
read (where message rules never are), and, found by the second pass, the search skipping a rule that is off itself (the
engine leaves it undecided, as one it refuses, so the engine alone says what off means). One was at first the round's
own mistake, a change that left the file unreadable so no test ran; written again, it went unnoticed too. The eleven
others each got a test, and on the second and third pass, on df0c56f, every change left was caught:

| Unnoticed | The test now |
| --- | --- |
| a field compared with another tried without the other's values | the guests, named only by a comparison with the kids, are tried with the kids' values |
| a show rule the engine refuses read as hiding the field | a refused show rule: no conflict |
| answers past the limit of tries claimed not to matter | five texts of nine values each: the one past the limit is said |
| several choices tried with an option they lack | "none of its options ticked" beside "something ticked": no conflict |
| a language tried as none, said as "the language is empty" | a conflict on the language says the language found |
| a form whose only rules are an outcome's said to have none | it lists them under After sending |
| a field's chip list made anew on every change, so the field drawn again | a change that leaves its words the same keeps the same list |
| Rules overview hidden in the canvas's "..." | the menu's shown items include it |
| the builder not told the site's default language | the Design view gives it each language with whether it is the default |
| a rule on an element that can have none any more not opened | a captcha's rule from the API opens in the builder |
| a validation rule on a field whose type the site lacks refused at once | it waits for the type, as the field's own rules do |

