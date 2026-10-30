# Step 6.3: the design skills' review

ROADMAP 6.3 ships the website forms' themes: 6.3a the registry and the functional base rules, 6.3b the `default`
theme, 6.3c `unstyled` and `bootstrap5`. Per ADR-0019 and ADR-0020 the three design skills were loaded before any
theme was styled, and ran on the result. This file records what each one said and what was done about it, one part
per step. The owner answers no questions during the run (CLAUDE.md rule 8), so every choice the skills would ask
about was decided here or in ADR-0106.

## 6.3b The default theme

The default theme is the design of 2.3 and 2.4 (`docs/ux/website/`, ADR-0022), ported into
`packages/themes/src/default.css` and checked on the real markup of 6.2 for the first time.

### What was looked at

The demo's specimen (`/en/specimen/`): one form with a field of every built-in type, on two pages, with seven groups
(two and three columns, a card group, content, a repeatable group), picture choices and the demo's own picture. Edge
through Playwright, with reduced motion; JavaScript on for axe and the runtime blocked, so each page renders as it
does without JavaScript (no rule of the theme reads whether scripts run).

| Shot | Light | Dark | Right to left |
| --- | --- | --- | --- |
| 375 px, empty | [shot](review-6.3/specimen-light-375-empty.png) | [shot](review-6.3/specimen-dark-375-empty.png) | [shot](review-6.3/specimen-rtl-375-empty.png) |
| 375 px, errors | [shot](review-6.3/specimen-light-375-errors.png) | [shot](review-6.3/specimen-dark-375-errors.png) | [shot](review-6.3/specimen-rtl-375-errors.png) |
| 1440 px, empty | [shot](review-6.3/specimen-light-1440-empty.png) | [shot](review-6.3/specimen-dark-1440-empty.png) | [shot](review-6.3/specimen-rtl-1440-empty.png) |
| 1440 px, errors | [shot](review-6.3/specimen-light-1440-errors.png) | [shot](review-6.3/specimen-dark-1440-errors.png) | [shot](review-6.3/specimen-rtl-1440-errors.png) |

- **Dark:** `?scheme=dark`, which gives the demo site its dark design and puts `jfb-scheme-dark` on the page's root
  (ADR-0106).
- **Right to left:** `dir="rtl"` on the form, with the English texts, so a sentence's full stop shows at its start,
  as English set right to left does; an Arabic site sets the direction from its culture (6.2b).
- **Errors:** the page after an empty post: the error summary and an error under each field that needs one.

Axe (WCAG 2.2 A and AA) found nothing on any of the twelve, and none scrolls sideways; `specs/website/default-theme.spec.ts`
keeps both checks. The first round found the defects below; the second round confirmed them fixed.

### design-taste-frontend

**The read:** a form set into a customer's site, for any member of the public, mostly on a phone; calm, trust-first
and accessibility-critical; native CSS with tokens a site overrides, keeping the design of 2.3 (a "redesign,
preserve" in the skill's terms, since 2.3 and 2.4 settled the look).

**The dials:** variance 3, motion 2, density 5: the skill's trust-first row.

**Scope:** as in 2.3, the skill's landing-page rules (hero, bento, logo walls, marquees) have nothing to act on, and
it names multi-step forms as outside its reach (its section 13). Its form, contrast, theme-lock and anti-default
rules applied.

**Pre-flight, the boxes that apply:**

| Check | Result |
| --- | --- |
| Brief inference and dials declared | yes, above |
| Design system | none of the listed systems fits a theme that takes each site's look; the project's own tokens (ADR-0015, ADR-0022) |
| No em or en dash | none in the theme or the specimen's texts |
| Theme lock | yes: one scheme per form, from its class or an ancestor's; the demo switches page and form together |
| One accent | yes: evergreen in both schemes; focus blue and error red are signals |
| One corner system | yes: one radius, four steps |
| Button and form contrast | axe clean on all twelve shots; the token contrasts are those measured in 2.3 (`docs/ux/tokens.md`) |
| No button label wraps at desktop | yes ("Next", "Add a guest") |
| Dark mode tested | yes, both widths, empty and with errors |
| Empty, loading and error states | errors shown; the loading skeleton belongs to the headless element (6.6); no empty state applies to a form |
| Cards only where they earn it | yes: the card group is an editor's choice; the repeatable group adds no second box |
| Real images | the demo's picture, drawn for it (`Seeding/Media/picture.svg`), in the image field and the picture choices |
| Copy self-audit | two of the specimen's own texts said a thing twice (finding 4) |

### impeccable

- **Mode and context:** Operate, from the surface brief `.impeccable/surfaces/docs-ux-website.md`. `impeccable
  context` loaded PRODUCT.md and DESIGN.md with the bundled engine; no downloads, hooks or live mode. The command was
  `polish`: refinement of the incumbent design, which preserves its look, copy and behaviour.
- **Craft floor:** read before the first edit. Nothing in the port adds an eyebrow, gradient text, a side stripe, a
  glyph icon or a display face.
- **`impeccable detect --json`** on `default.css` and `base.css`: `[]`. Its URL mode was not used, since it may fetch
  a browser; the Edge review above covers the rendered page.
- **The polish pass**, in impeccable's order (broken paths first, then layout, then cosmetics):

| # | Finding | Kind | Fix |
| --- | --- | --- | --- |
| 1 | A form whose direction differs from its page's scrolled 10,000 px sideways: the base rules moved the honeypot to the start side, which is then the scrolling side | broken path (WCAG 1.4.10) | the honeypot is clipped to nothing where it stands (ADR-0106); the browser test checks it is no bigger than a pixel and clipped |
| 2 | Fields side by side did not line up: a label that wraps, or help on one field and not its neighbour, pushed one control below the other (the dates, the name beside the email) | layout drift | fields side by side share their rows through a subgrid, so every control of a row starts on the same line (ADR-0106) |
| 3 | An option's description sat nearer the next option than its own label: the option's grid kept the flex row's gap as a row gap | local defect | the gap is set for columns only; the description sits right under its label |
| 4 | The specimen's texts said two things twice: a help "Up to 40 characters." above the counter that says the same, and a help on the list of several above the view's own hint | copy | the help now says how the name is printed, and the list keeps only the view's hint |
| 5 | Without a stylesheet of its own (`unstyled`, and `bootstrap5` until 6.3c), the specimen's 600 px pictures widened a phone's page | broken path | the base rules hold a picture to its field's width as they hold controls, and keep its shape |
| 6 | The default stylesheet would have styled another theme's form on the same page | system drift | every rule nested under `.jfb-form:where(.jfb-theme-default)`; the browser test puts the default stylesheet on an `unstyled` page and finds its form unchanged |
| 7 | The error summary writes "Where would you like to sit?: Choose an answer" after a label that ends in a question mark | copy, outside the theme | the summary entry is shared logic of C# and TypeScript with vectors in every visitor language; 6.4 takes it (ADR-0106, ROADMAP 6.4) |

- **Kept as designed:** the content heading inside a group is as large as the group's heading in 2.3; the specimen
  now places it where an editor would, before the calculation it introduces. The upload without JavaScript shows the
  browser's own file button inside the solid frame, as 2.4 decided.

### emil-design-eng

The port adds no motion; the review checked each moving thing of 2.3 on the real markup and found one gap.

| Before | After | Why |
| --- | --- | --- |
| The yes/no and scale choices, drawn as buttons, did not move when pressed | `translate: 0 1px` on `:active`, over `--jfb-motion-fast` on the theme's ease-out curve, as the buttons sink | Anything that looks like a button must answer the press the same way; DESIGN.md says every button sinks |
| Buttons sink `translateY(1px)` on `:active` | unchanged | A full-width button that scales shrinks its edges by pixels and reads as a jump; a 1 px sink reads as a press (2.3) |
| Errors fade in and drop 4 px through `@starting-style` | unchanged, also after a post without JavaScript | It shows where the change happened without JavaScript; under reduced motion it only fades |
| The error summary fades in | unchanged | It takes the focus and moves the page; movement on top would make the jump worse |
| A new page slides 8 px from the side the visitor moved to | unchanged, and mirrored right to left (`:dir(rtl)`) | Spatial consistency; checked in the right-to-left shots' markup |
| The switch's knob slides 20 px | unchanged, mirrored right to left | The knob is the state; its motion explains the change |
| Hover colours change over 120 ms with `ease`, inside `@media (hover: hover)` | unchanged | A hover answer must feel instant, and a tap must not leave one behind |
| The focus ring | no transition | The keyboard moves many times a minute; a ring that fades in feels slow |
| The busy ring turns, linear | unchanged; under reduced motion it pulses instead | Constant motion is linear; reduced motion keeps the signal without the turning |

Nothing animates layout; only `translate`, `transform` and `opacity` move, and no transition exceeds 200 ms.
