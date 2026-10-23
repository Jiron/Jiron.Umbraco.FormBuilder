# Step 2.4: design critique of Phase 2

This critique covers the backoffice wireframes of step 2.2 (`docs/ux/wireframes/`) and the website form's default
theme of step 2.3 (`docs/ux/website/`), as they stood at the end of 2.3 (commit `d29fb9e`). The three design skills
of ADR-0019 and ADR-0020 ran again, and a fourth reviewer checked every shot against ROADMAP §4.13. Every finding that
is not purely taste is fixed and re-rendered; the taste items that stay are listed with their reasons at the end.

Both surfaces scored 28 of 40 on the heuristic review. The website form failed silently on a dark site that had not
opted into the dark scheme, had no design for the moments around sending, and let German step names overlap. The
backoffice had no designed keyboard layer, lost information at Umbraco's narrowest width, and showed the designer's
canvas in a placeholder look instead of the finished theme. Both now pass every check the project has (see
[Checks](#checks)), and four ADRs record the decisions the fixes needed.

## How the critique ran

| Part | Who | Notes |
| --- | --- | --- |
| `design-taste-frontend` | the main session | the design read, the dials and the pre-flight check, per surface |
| `impeccable` critique | four subagents | per surface, assessment A (a design review that read the work and probed it in Edge) and assessment B (the bundled detector plus the repo's width sweeps), kept apart |
| `impeccable` audit | the main session | the five audit dimensions, scored from A's and B's evidence and the checkers, before and after the fixes |
| `emil-design-eng` | the main session | the motion and interaction review, as a Before, After, Why table |
| §4.13 checklist | a fifth subagent | every shot of both surfaces against the measurable rules of §4.13 |

- **Isolation.** The skill asks that assessment A not see B's results. The website's B report reached the main
  session before A had finished; it was not passed on, and A's report says it opened neither earlier review nor the
  detector output. Neither A saw the reviews of 2.2 or 2.3.
- **Questions skipped:** unattended run (CLAUDE.md rule 8). Where a skill asks the user to choose, the choice was
  made here and is listed under [Decisions](#decisions).
- **The engine** was the one bundled with the installed skill
  (`C:\Users\Server\.claude\skills\impeccable\scripts\bin\windows-x64\impeccable.exe`), with no download, hooks or
  live mode (ADR-0020). The critique snapshots were written with `critique-storage write` to `.impeccable/critique/`,
  which is git-ignored like `.impeccable/review/`; they were written after the fixes, so their content fingerprints
  are of the fixed files.
- **The fixes** to the website were made in the main session. The wireframes were fixed by four subagents, each
  owning a group of pages (the form's life cycle; entries; library and settings; the designer, preview and template
  gallery) with page-local styles; shared kit changes were made only by the designer's agent (its canvas rules) and
  the main session.

## `design-taste-frontend`

**The read, the website form:** an embedded multi-page form for members of the public, mostly on phones, inside
sites it has never seen; trust-first and accessibility-critical; native CSS custom properties, the site's own font,
restrained motion. Dials: variance 3, motion 2, density 4, the skill's trust-first row. Unchanged from 2.3.

**The read, the backoffice:** an operations tool inside Umbraco 17 for editors, entry handlers, administrators and
privacy officers, used daily; native to Umbraco's UUI, calm, keyboard-friendly. Dials: variance 2, motion 2,
density 6. The skill's design-system map points to the platform's own system, which here is UUI (D12).

**Scope.** The skill is written for landing pages and portfolios and says multi-step product UI is outside it (its
section 13). These parts applied: the anti-default discipline, one accent, one corner system, the full set of
interactive states (its section 4.5), form patterns, dark mode tested in both modes, reduced motion, the em-dash ban
and the AI tells. Where it disagreed with the roadmap, D12, D18, §4.13 and WCAG 2.2 AA won (ROADMAP "How to use").

**Pre-flight, the boxes that apply:**

| Check | Before (`d29fb9e`) | After |
| --- | --- | --- |
| Brief inference and dials declared | yes | yes, above |
| Design system | website: native CSS with the project's tokens (ADR-0015); backoffice: UUI | unchanged |
| No em or en dash anywhere | yes; both checkers scan every visible text | yes |
| Theme lock | **no** for the website: a form without a scheme class kept its light ink on a dark site (about 1.15:1) | yes: the ground check gives such a form the dark tokens (ADR-0022, amended) |
| One accent | yes: evergreen on the website, UUI's interactive colour in the backoffice | yes |
| One corner system | yes | yes |
| Button contrast | yes, measured per shot | yes; the busy Send keeps its fill and turns its label into a ring in `--jfb-color-on-accent` |
| Form contrast | yes on the two mock grounds | yes, and on any ground thanks to the ground check |
| Every state (loading, empty, error) | **no**: no sending, failed, limited or changed state; no headless loading; save and resume, verification, members-only, sent-before and redirect not drawn; in the backoffice no save conflict, no undo, no state after Erase | yes: 23 new website states on two new pages and seven changed ones, and 71 new backoffice states (more widths of existing states not counted), listed in the two READMEs |
| Skeletons rather than spinners | n/a | the headless element's loading state is a skeleton in the form's shape; the only spinner is the ring inside a pressed Send, where nothing is loading into the page and the button must show it heard the press |
| Tactile feedback | yes: a 1 px press | yes |
| Dark mode tested in both modes | website yes; backoffice: our two own colours had no dark values | yes: they are mixed from UUI's tokens and measured in all three Umbraco themes (`tokens.md`) |
| Icons from one library | yes, Lucide (the backoffice's own) | yes; a mail icon added for the waiting outcome |
| Generic names, AI tells | none | none |

## `impeccable`

Mode: Operate for both surfaces (the visitor completes a task; the editor operates a tool). Commands: `critique`,
then `audit`; the detector (`detect`) ran in both assessments B and again after the fixes.

### Critique: the website form, 28 of 40

| # | Heuristic | Score | The main issue |
| --- | --- | --- | --- |
| 1 | Visibility of system status | 2 | nothing showed a busy Next or Send; the uploading row was a bar without words |
| 2 | Match with the real world | 3 | the error heading "Check these answers" next to the step "Check your answers" |
| 3 | User control and freedom | 3 | no save and resume drawn; Clear on the signature had no undo |
| 4 | Consistency and standards | 3 | the outcome's copy of the answers differed from the summary; read-only and disabled looked the same |
| 5 | Error prevention | 3 | nothing prevented a double Send |
| 6 | Recognition rather than recall | 3 | below 480 px only "Step 1 of 3" names the pages |
| 7 | Flexibility and efficiency | 3 | prefill and resume not drawn |
| 8 | Aesthetic and minimalist design | 3 | sample content stacked headings that said the same thing |
| 9 | Recognise, diagnose, recover | 2 | two sample errors named the wrong cause; system failures had no design |
| 10 | Help and documentation | 3 | why consent is needed sat only behind the privacy link |

Design specificity: the structure is written for a form embedded in an unknown site (container queries, the
site's font, the inset card group, the hidden default button); on a phone it looks like any careful accessible
form, which suits a trust-first form.

| Priority | Finding | What changed |
| --- | --- | --- |
| P1 | On a dark site without `jfb-scheme-dark`, labels sat at about 1.15:1 | the runtime checks the ground behind an unclassed form and adds `jfb-scheme-dark` when it is dark (ADR-0022 amendment; a Playwright check in 6.4; the mock does it with `?scheme=dark-site`) |
| P1 | No design for sending, a failed or limited send, a changed form, or a choice that filled up meanwhile | `book-check`: `sending`, `failed`, `limited`, `changed`, `full` (a notice above the buttons that takes the focus; the changed notice in the error summary's place; the summary links to the row's Change) |
| P1 | Step names overlapped at 480 to 719 px with long German names | step names show from 480 px for up to four steps and from 720 px for up to six; seven or more fold; the runtime also folds when a word does not fit (`data-jfb-compact`); the specimen's second form is German with six pages, and the checker now measures each step |
| P2 | "mira.okafor@" got the empty-field message; a zip was refused as too large on a field that takes only PDFs and pictures | "Enter a complete email address, for example name@example.com"; the large file is `hall-floor-plan.pdf` |
| P2 | The outcome's copy dropped five answers | the copy shows the same rows as the summary, without Change |
| P3 and minor | the ranking's end buttons lost the focus when disabled; the website field's `aria-label` hid its visible label; the picture's alt repeated its caption; the signature help sat 12 px below its legend; the counter's over-limit message had no icon; read-only looked disabled; "(optional)" on a single checkbox; redundant headings in the samples; no 768 px shots | `aria-disabled` and still focusable; the affix joins the description; distinct alt; 4 px like every field; the alert icon; read-only keeps full ink with the ordinary edge; no mark on a lone checkbox or switch; the samples rewritten ("How we reach you", "Before we confirm"); shots at 375, 768 and 1440 px |
| Missing states | members only, sent before, full; verify your address; redirect with a delay; the verification page; save and resume; too many files; a stopped upload; the repeater at its limit; "Other"; the captcha's consent and failure; cascading lists loading and empty; the upload without JavaScript; a file dragged over; the headless skeleton; right to left on a real right-to-left language | all drawn (`book-closed`, `book-sent`, `book-verify`, `apply-room`, `book-session`, `fields`, `book-details`, `book-rtl`) |

Persona red flags were these same items seen by a first-time visitor, a screen-reader and keyboard user, a visitor on
one hand with a slow connection, and a visitor who did not come for the form.

### Critique: the backoffice wireframes, 28 of 40

| # | Heuristic | Score | The main issue |
| --- | --- | --- | --- |
| 1 | Visibility of system status | 3 | no "Draft saved", nothing after Approve, "1 failed" lost at 620 px |
| 2 | Match with the real world | 3 | merge tags shown as code with no picker for the entry title |
| 3 | User control and freedom | 2 | undo is a product principle, yet no Undo toast was drawn; the gallery's preview had no way back at 620 px |
| 4 | Consistency and standards | 3 | Library showed 4 of 9 templates; grips were not buttons |
| 5 | Error prevention | 3 | an empty form showed "No problems" with Publish live; the publish dialog hid warnings |
| 6 | Recognition rather than recall | 3 | the list beside the entry panel dropped the state; no shortcut overlay |
| 7 | Flexibility and efficiency | 2 | no j/k cursor, no `?` overlay; the canvas was the 40th tab stop |
| 8 | Aesthetic and minimalist design | 3 | calm, but routine states were louder than exceptions |
| 9 | Recognise, diagnose, recover | 3 | save failure and save conflict not drawn |
| 10 | Help and documentation | 3 | no shortcut help, little orientation in the designer |

Design specificity: authored for this product and native to Umbraco 17; the product's personality sits in its words
and states. The one generic part was the designer's canvas, drawn in a placeholder look.

| Priority | Finding | What changed |
| --- | --- | --- |
| P1 | No keyboard and undo layer: canvas fields, steps, insert points and "Add field" could not take focus; grips were hidden from assistive technology; no `?` overlay, no cursor row, no Undo, no "Move to…" | every canvas part is a button, "Skip to the form" is the first stop, the designer has `keyboard`, `shortcuts`, `move`, `undo`, `menu`, `drag` and `inline-edit` states; the entries screens have a cursor row and a shortcuts dialog; Approve shows "Entry #214 approved. [Undo]" (ADR-0016 amendment, ADR-0023); every grip is a button; the kit gives its drawn checkboxes, switches and radios a tab stop |
| P1 | At 620 px the Inbox dropped State, Entries dropped "1 failed", the gallery's preview had no way back, and three editors hid their preview | State stays and the form name moves under the entry; "1 failed" sits beside the state; "All templates" goes back; Settings and Preview switch in the email editor, theme editor and email layout, with Send test email kept |
| P1 | The canvas was narrower than "Desktop" with nothing saying so, narrower than "Phone" at 1024 px, and drawn in the placeholder look | "Narrowed to fit" whenever the stage is narrower than the chosen width (960, 768, 375 px, the form's own widths); both panels collapse; the canvas, the preview and the gallery preview render the 2.3 theme |
| P2 | High-stakes moments not drawn: save conflict and save failure, Unpublish, Trash for a form in use and its Undo, deleting entries, the bulk result, after Erase, legal hold, editing Retention, three settings sections, Used on, Import, Paste questions, the palette at 620 px, translating | all drawn as states of the existing pages, and a new `member-entries.html` |
| P2 (taste) | Routine states louder than exceptions | kept with a change: see [Decisions](#decisions) |
| Minor | among others: "All checks pass" while loading; a check and "Done" on one step; Umbraco folds sections at 920 px; an empty folder offering no template; the empty-bin confirm leaving out files; the insert state showing an inspector; no way to delete a rule or an outcome; a toast repeating Publish; two merge-tag controls; the publish dialog hiding warnings; restore not saying the draft is kept; "…" on the wrong side of the entry panel; previous-period bars at 1.8:1; 30 days drawn as 15; the theme editor's colours not matching the tokens; "locked" that was only "restricted"; an editable Administrators column; HR sample data that disagreed; our two backoffice colours without dark values; the merge-tag popup as a menu holding a search field; inspector sections as divs; switches named with their state | each fixed on its page; the popup is a dialog with a combobox and a listbox; the inspector's sections are buttons with `aria-expanded`; switches are named "Use this rule", "Use Email the team"; the two colours are mixed from UUI tokens |

### Assessment B: the detector and the width sweeps

| Run | Before | After |
| --- | --- | --- |
| `impeccable detect docs/ux/website` | exit 0; 20 advisories, all `design-system-color`: the stand-in host site's two colours and the three documented `color-mix()` tokens | exit 0; 32 advisories, the same kinds plus the RTL and verification pages and the three page colours of `standalone.css`, now documented in `tokens.md` ("The package's own page") |
| `impeccable detect docs/ux/wireframes` | exit 2; 210 hits: 130 colour advisories (UUI and kit tokens, samples), 76 `cramped-padding`, 2 `overused-font`, 2 `low-contrast` | exit 2; 241 hits: 131 colour advisories, 102 `cramped-padding`, 6 `low-contrast`, 2 `overused-font` (a `skipped-heading` the first run after the fixes found is fixed) |
| website sweep | `--widths=375,768,1440`: 78 shots, 0 with findings | see [Checks](#checks) |
| wireframe sweep | `--widths=920,1024,1440,1920`: 452 shots, 0 with findings | see [Checks](#checks) |

Verdicts on the detector's hits after the fixes:
- **`cramped-padding` (102): false positives.** Every hit is a wrapper with no padding of its own (fixed-height
  inputs, selects and segmented switches that centre their text, boxes whose head and body carry the padding). B
  measured the real gap from text to edge on every page and state: at least 7 px everywhere except the History box,
  whose last row sat 6 px from the edge; two-line cells there now have row padding (9 px, measured).
- **`low-contrast` (6): exempt.** Each is the label of an `aria-disabled` button (`#c4c4c4`, UUI's disabled
  colour); WCAG 1.4.3 does not apply to inactive controls, and the product uses UUI's own disabled buttons.
- **`overused-font` (2): samples.** Arial in the email sample (an email-safe font) and Lato, Umbraco's backoffice font.
- **`skipped-heading` (1): fixed.** The Design view drawn behind the Unpublish confirm in `form-library.html` used an
  `h3` with no `h2` above it.
- **Colour advisories: false positives.** The detector compares against `DESIGN.md`, which describes the website
  form; the backoffice uses UUI's tokens (`tokens.md`, "The backoffice").

### Audit

| # | Dimension | Website before | Website after | Backoffice before | Backoffice after |
| --- | --- | --- | --- | --- | --- |
| 1 | Accessibility | 2: text at about 1.15:1 on an unopted dark site; focus lost on the ranking; a label replaced by `aria-label`; reduced motion removed even fades | 3: AA measured per element and state; reduced motion keeps fades; left for 6.4: a screen-reader run on the real runtime | 2: 95 role-bearing controls without a tab stop; canvas unreachable; a menu holding a search field; 1.8:1 chart bars | 3: every drawn control reachable (0 left, measured on every state); the static wireframes cannot prove a screen-reader path, which 9.x tests |
| 2 | Performance | 4: 6.8 KB gzip; only transform and opacity move | 4: 7.5 KB of 10 KB; the ring turns with `rotate`, the skeleton pulses `opacity` | 4: static pages; nothing animates but toasts | 4 |
| 3 | Responsive design | 3: name parts side by side at 375 px; steps overlapping at 480 to 719 px; no 768 px shots | 4: shots at 375, 768 and 1440 px and every state at 480, 600, 720 and 1024 px without a finding | 2: information and ways back lost at 620 px; a 308 px canvas at 1024 px | 4: every state at 920, 1024, 1440 and 1920 px without a finding (phones are not a backoffice target, research §12) |
| 4 | Theming | 3: tokens everywhere, but the dark scheme only by opt-in | 4: the ground check | 3: UUI tokens, but our two colours were fixed light values | 4: mixed from UUI tokens, measured in light, dark and high contrast |
| 5 | Implementation integrity | 3: samples with wrong causes; the outcome's rows reshaped | 4: detector advisories explained; samples consistent | 2: the placeholder canvas look drifted from the 2.3 theme; sample data disagreed; 4 of 9 templates | 4: the canvas uses the theme itself; samples consistent |
| | **Total** | **15 of 20 (good)** | **19 of 20 (excellent)** | **13 of 20 (acceptable)** | **19 of 20 (excellent)** |

## `emil-design-eng`

| Before | After | Why |
| --- | --- | --- |
| reduced motion set every duration to 0 s | it zeroes only the distances (`--_rise`, `--_slide`) and the duration of movement (`--_move`); fades and colour changes keep their timing | reduced motion is about things travelling; a fade carries the state change without moving anything, and an instant swap hides that anything changed |
| no state while the answers are on their way | Send keeps its size and focus, its label gives way to a ring that turns once every 700 ms, linear; a slow opacity pulse under reduced motion; "Sending your answers" in the live region | the press needs an answer within a frame; constant motion is linear; nothing around the button jumps; a second press does nothing |
| no loading state for the headless element | a skeleton of hairline bars in the form's shape, pulsing `opacity` over 1.4 s, still under reduced motion | it shows where the form will be, so nothing shifts when it arrives |
| "drag with a spring, UUI's sorter feedback" in `review-2.2.md` (the sorter is not used, ADR-0016) | a pointer drop settles over 150 ms ease-out; the insertion line jumps from gap to gap; keyboard moves, "Move to…", undo and redo never animate | the line follows the pointer, so easing would make it lag; keyboard moves repeat many times in a row and must land before the announcement |
| the entry panel and the j/k cursor undefined | neither animates | entry handlers do this hundreds of times a day |
| step names that no longer fit wrapped mid-word | they fold to "Step 2 of 6" at once, with no transition | a layout change on resize should not perform |
| a new page slid 8 px from the side the visitor moved towards | unchanged; with reduced motion it only fades | spatial consistency: forward comes from the right, back from the left, mirrored right to left |
| "Clear" on the signature wiped it at once | "Undo clear" until the next stroke, with no animation | a slip of the finger should cost nothing |
| the redirect after a delay was not designed | the countdown changes its number without motion and is announced once, not every second | WCAG 2.2.1; a ticking announcement is noise |

## Fourth view: the §4.13 checklist

**Before:** backoffice 137 shots, 76 passing every rule, 20 with a fail, 41 with only doubtful items; website 53
shots, one fail.

| Fail | What changed |
| --- | --- |
| website: name and address parts side by side below 480 px | they stack under 480 px |
| `history--920`: the Comment column squashed to about 70 px | at 620 px the comment moves under the row's change list |
| `history--drafts`: blank comments | "none" |
| `entry-panel--spam`: "Entry #1051" in the row and in the panel's header | the header leaves out what the row shows |
| `entries--no-results`, `audit-log--no-results`: "Clear filters" twice | the empty state points to the chip row's Clear filters |
| `theme-editor--list`: a "Default" chip beside "Site default" | no chip where the name says it |
| `theme-editor`: the contrast problem shown twice | once, under its field |
| three editors at 920 px: no preview | Settings and Preview switch |
| `template-gallery--920`: no way back | "All templates" |
| the designer: panels could not collapse | collapse buttons and rails |
| the preview: the step name and the heading both shown | the steps stand in for the title, as on the website |
| `designer--new-form`: "1 Page 1" | the step reads "Page 1" |

The doubtful items were decided: a column whose value a filter fixes is hidden while the filter is on; permissions
are grouped into three groups of at most five; the empty-state copy depends on the role; modal sidebars for
deliveries and the audit log may repeat the dimmed row, because they open over the list rather than beside it.

## Decisions

Recorded as ADRs:
- **ADR-0022, amended:** the ground check; reduced motion stops travel, not feedback; the send states and notices;
  the verification page; step folding; read-only unlike disabled; new parts of the markup contract.
- **ADR-0016, amended:** the designer's keyboard layer, "Skip to the form", the shortcuts and the motion rules.
- **ADR-0023 (new):** undo for entry state changes; the actions a user's change starts wait 10 seconds for the undo
  window.
- **ADR-0013, amended:** a retry needs `manageActions` for the run's form and a screen that shows the run.

Recorded in the roadmap (strengthening only; the reasons are in `docs/WORKLOG.md`): 6.4 builds and tests the ground
check, step folding and the send states; 8.5 tests the undo window; 9.1 measures our two backoffice colours in all
three Umbraco themes; 11.1 and 11.2 test the Undo toasts; §4.4 and §4.5 describe the window and `ChangeKey`.

Made here, without an ADR:
- the backoffice's own colours are mixed from `--uui-color-text` and `--uui-color-border-emphasis` (50 % for muted
  text, 20 % for control borders), because the theme leaves no mark on the page to switch on;
- an action switch, like a rule's, is named "Use …";
- the header's state chip shows "(pending changes)" only to users who may design the form; others see "Published".

Taste, kept:
- **Routine states louder than exceptions** (the solid "Approved" chip): UUI's own tag colours, as Umbraco shows its
  own states; failures now carry words and a mark beside the state at every width, and waiting entries the warning
  colour.
- **Next before Back, and a plain "Send":** the primary action comes first in reading order, and the editor names the
  button for the form ("Book my place").
- **The error summary's weight:** it is the one place where the form raises its voice, and its words are the fixes.
- **The step label smaller than group headings,** the card group's inset (ADR-0022), and summary headings that repeat
  step names (they are the same sections).
- **Modal sidebars for deliveries and the audit log beside a docked entry panel:** the entry panel is for working
  through a list; the others are for looking one thing up.

## Before and after

| | Before | After |
| --- | --- | --- |
| A form without a scheme class on a dark site: unreadable, then the ground check | ![](critique-2/website-dark-site--before.png) | ![](critique-2/website-dark-site--after.png) |
| German step names: overlapping at 560 px, then folded | ![](critique-2/website-steps-german--before.png) | ![](critique-2/website-steps-german--after.png) |
| Errors: the wrong cause for a half-typed address | ![](critique-2/website-errors--before.png) | ![](critique-2/website-errors--after.png) |
| Sending: no state at all, then a send that failed | ![](critique-2/website-send-failed--before.png) | ![](critique-2/website-send-failed--after.png) |
| The outcome: four rows, then the whole copy | ![](critique-2/website-outcome--before.png) | ![](critique-2/website-outcome--after.png) |
| Uploads and signature: a bar without words, then words, a stopped file with Try again, save for later | ![](critique-2/website-upload-signature--before.png) | ![](critique-2/website-upload-signature--after.png) |
| The designer: placeholder look and no "Narrowed to fit", then the theme and a keyboard-reachable canvas | ![](critique-2/backoffice-designer--before.png) | ![](critique-2/backoffice-designer--after.png) |
| The designer at 1024 px: a 308 px canvas, then rails and an honest width | ![](critique-2/backoffice-designer-1024--before.png) | ![](critique-2/backoffice-designer-1024--after.png) |
| The Inbox at 620 px: State lost, then kept | ![](critique-2/backoffice-inbox-920--before.png) | ![](critique-2/backoffice-inbox-920--after.png) |
| Approve: nothing designed, then the Undo toast | ![](critique-2/backoffice-approve--before.png) | ![](critique-2/backoffice-approve--after.png) |
| The template gallery at 620 px: a preview with no way back, then "All templates" | ![](critique-2/backoffice-gallery-920--before.png) | ![](critique-2/backoffice-gallery-920--after.png) |
| Permissions: 11 rows and an editable Administrators column, then three groups and a fixed column | ![](critique-2/backoffice-permissions--before.png) | ![](critique-2/backoffice-permissions--after.png) |
| The first run: a full tree, then a fresh install | ![](critique-2/backoffice-first-run--before.png) | ![](critique-2/backoffice-first-run--after.png) |
| The theme editor: colours that were not the tokens, then the nine tokens in two groups | ![](critique-2/backoffice-theme-editor--before.png) | ![](critique-2/backoffice-theme-editor--after.png) |

The "before" pictures are the shots at `d29fb9e`, except the first two, which are the critique's own probe shots;
the first "after" picture renders `book-details.html?state=errors&scheme=dark-site`, and the second the specimen's
German form at 560 px.

## Checks

The final runs of step 2.4, on the fixed work:

```text
node scripts/wireframes.mjs --check
  wireframes: 221 shots of 26 pages, 0 with findings
node scripts/wireframes.mjs --widths=920,1024,1440,1920
  wireframes: 736 shots of 26 pages, 0 with findings
node scripts/website-mocks.mjs --check
  ok   theme stylesheets: 7538 bytes gzip of 10240
  website-mocks: 217 shots of 9 pages, 0 with findings
node scripts/website-mocks.mjs --widths=480,600,720,1024
  website-mocks: 288 shots of 9 pages, 0 with findings
node --test "scripts/tests/*.test.mjs"
  tests 146, pass 146, fail 0
impeccable detect docs/ux/website
  exit 0: 32 advisories, all design-system-color (see assessment B)
impeccable detect docs/ux/wireframes
  exit 2: 131 colour advisories, 102 cramped-padding, 6 low-contrast, 2 overused-font (see assessment B)
keyboard reach (a Playwright probe over every state of the 26 wireframe pages)
  0 drawn checkboxes, switches, radios, buttons or tabs without a tab stop (menu items excluded: they take a
  roving focus inside their open menu)
```

Each finding of the critique above is either fixed in this step or listed under [Decisions](#decisions) as taste
with its reason.
