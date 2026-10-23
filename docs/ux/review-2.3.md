# Step 2.3: the design skills' review

ROADMAP 2.3 designs the website form's default theme (`docs/ux/website/`, ADR-0022) and fixes the tokens
(`docs/ux/tokens.md`). Per ADR-0019 and ADR-0020, three design skills were loaded before the design started and ran
on the work. This file records what each one said and what was done about it. The owner answers no questions during
the run (CLAUDE.md rule 8), so every choice the skills would ask about was decided and recorded here or in
ADR-0022.

## design-taste-frontend

**The read:** an embedded multi-page form theme for members of the public (mostly on phones), inside customer sites
the theme has never seen; calm and trust-first; accessibility-critical; native CSS custom properties, the site's
own font, restrained motion.

**The dials:** variance 3, motion 2, density 4: the skill's "trust-first, public-sector, accessibility-critical" row
of its dial table.

**Scope:** the skill says multi-step forms are outside what it is for (its section 13) and asks to apply only the
parts that fit. These parts applied:
- the anti-default discipline: no purple, no gradients, no Inter, no three equal cards;
- one accent, locked;
- one corner system;
- the form patterns: label above the input, no placeholder as label, error below;
- dark-mode tokens tested in both schemes;
- reduced motion;
- the em-dash ban;
- the AI tells.

The landing-page rules (hero, bento, logo walls, marquees) have nothing to act on here.

**Pre-flight, the boxes that apply:**

| Check | Result |
| --- | --- |
| Brief inference and dials declared | yes, above |
| Design system | none of the listed systems fits a theme that must take each site's look; native CSS with the project's own tokens (ADR-0015) |
| No em or en dash anywhere | yes; the checker scans every visible text of every shot |
| Theme lock | yes; the form follows the site's light or dark design as a whole (`jfb-scheme-dark` or `-auto`) |
| One accent | yes: evergreen for actions, chosen states, links and done steps; focus blue and error red are signals, not accents |
| One corner system | yes: one radius and four steps derived from it (tokens.md) |
| Button contrast | yes: 6.16:1 light, 9.53:1 dark on the primary button, measured per shot |
| No button label wraps at desktop | yes; the checker measures every button at 1440 px |
| Form contrast (inputs, placeholders, rings, labels) | yes; measured per element in place, 49 then 53 shots |
| Motion motivated, reduced motion | yes; see the emil section; every duration is 0 s under reduced motion |
| Dark mode tested | yes; every state rendered on the dark host site |
| Empty, loading and error states | errors, uploading, refused file, closed and not-yet-open forms, the outcome; no empty state applies to a form |
| Cards only where they earn it | yes: groups are hairline-separated by default; the card style is an editor's option, and a repeater inside it is not a second box |
| No decorative dots, no `border-t` and `border-b` on every row | yes; rows carry one hairline |
| Icons from one library, not hand-drawn | one library, Lucide, stroke 2. The skill prefers others; Lucide is kept because Umbraco's backoffice draws its icons from it, so the product uses one family |
| Real images or explicit placeholders | `assets/placeholder.svg` is a labelled placeholder slot for an editor's photos |
| No generic names | Mira Okafor, Ines Hartmann, Lindenhall Library, the repair café; no "Acme" or "Jane Doe" |

## impeccable

- **Mode:** Operate (the visitor completes a task), recorded in the surface brief
  `.impeccable/surfaces/docs-ux-website.md`. `impeccable context` loaded PRODUCT.md (written in 2.2) before the work;
  the bundled engine ran locally, with no downloads, hooks or live mode.
- **The concept roll** (`concept-seed`, seed key `3155e951`):
  - **The deal:** structures 3, 7 and 1 of the ranked list of seven, with 3 leading: the Question ledger.
  - **The pick:** this run is unattended, so the dealt lead was taken.
  - **The challenger worlds:** the roll also printed challenger visual worlds. They were declined, because an embedded
    form theme has to take the host site's font and ground.
  - **The contract:** the surface brief holds the direction contract: THESIS, OWN-WORLD, STORY, FIRST VIEWPORT, FORM
    and FINISH.
- **Craft floor:** read before the first edit; no kickers or eyebrows, no gradient text, no side stripes, no glyph
  icons, no display face of the theme's own.
- **`impeccable detect --json docs/ux/website`:** two findings, both fixed, then `[]`.
  - `gpt-thin-border-wide-shadow` (advisory): the card group had a hairline border and a soft shade. It now has the
    shade only, with a transparent border that becomes its edge in forced colours.
  - `cramped-padding` (warning): the ranking list's rows sat against its border. The list now has its own inner
    padding.
- **The finish review** (the impeccable finish reviewer, fresh eyes on the captures):
  - **Review round: `fix`,** with eight material fixes, all applied in one batch:
    1. A card group broke the ledger's lines.
    2. A repeater row drew a box inside the card.
    3. The outcome repeated facts and buried the reference.
    4. The drop frame stayed on touch screens.
    5. The browser's date and time icons did not match the set.
    6. The typed signature had two labels.
    7. The phone 0 to 10 scale left a hole.
    8. No shot showed a chosen yes/no button with the field it reveals: the new `answered` state.
  - **Verdict pass:** seven resolved, fix 1 partial. The card group's field column was aligned, but its heading and
    right edge sat one padding in.
  - **The inset kept:** a bleed would need free space outside the form, which the theme cannot see. A host container
    that clips its content would cut off the card's frame and shade, and a full-width layout would scroll sideways
    (WCAG 1.4.10). This is recorded as a deliberate adaptation in ADR-0022.
  - **Final verdict: `ship`.** The reviewer notes it covers the eight scored fixes.
- **The documenter** wrote `DESIGN.md` and `.impeccable/design.json` from the shipped theme. DESIGN.md names the
  tokens and points to `docs/ux/tokens.md` for their values (the roadmap's rule beats the format's frontmatter).

## emil-design-eng

The theme's motion, against the obvious defaults. The form is something a visitor uses once, carefully, often on a
phone, so motion is only feedback and explanation, and none of it delays input.

| Before | After | Why |
| --- | --- | --- |
| `transition: all 300ms` on controls | `border-color` and `background-color` only, `120ms ease` | Name the exact properties; colour changes use `ease`, and a hover answer must feel instant |
| `transform: scale(0.97)` on `:active` | `translateY(1px)` over 120 ms on the theme's ease-out curve | Buttons go full width on phones; scaling a 343 px button shrinks its edges by 5 px each and reads as a jump, while a 1 px sink reads as a press |
| Hover styles everywhere | Hover only inside `@media (hover: hover)` | On touch screens a hover state sticks after the tap |
| An error appears instantly, or slides in from 20 px | `opacity` 0 to 1 and `translate: 0 -4px` to `0`, 200 ms, through `@starting-style` | It shows where the change happened without pulling the eye far; no JavaScript is needed to animate the entry |
| The error summary slides down | Opacity only | The summary takes the focus and pushes the page; movement on top would make the jump worse |
| A new repeater row pops in | The same 4 px fade-in as errors | Says "this is new" in the same voice as the errors; nothing in the form has a second animation language |
| Pages swap instantly, or cross-fade every time | An 8 px slide from the side the visitor moved towards, only while the runtime sets `data-jfb-direction`, mirrored right to left | Spatial consistency: Next comes from the right and Back from the left, and nothing moves on first load |
| `ease-in` or the default `ease` for movement | `cubic-bezier(.23, 1, .32, 1)` (`--jfb-motion-ease`) | A strong ease-out starts fast, so feedback is immediate |
| The switch thumb jumps | `translate` 20 px over 120 ms on the ease-out curve | The thumb is the state; its motion explains the change |
| Focus rings fade in | No transition on focus | The keyboard is used many times a minute; a delayed ring makes the form feel slow |
| Motion regardless of settings | `--jfb-motion-fast` and `--jfb-motion-base` become 0 s under `prefers-reduced-motion: reduce` | One switch turns off every movement, and things still appear in their final state |

No animation exceeds 200 ms. Only `transform`, `translate` and `opacity` move. Colours change, and nothing animates
layout.
