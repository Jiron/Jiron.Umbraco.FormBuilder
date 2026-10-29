# 0101. Rows and moves without JavaScript

- Status: accepted; extends [ADR-0100](0100-the-post-without-javascript.md) (the buttons beside Back, Next and Send);
  amends [ADR-0096](0096-the-choice-controls.md) (a ranking posts the order it shows, and the options below its top keep
  that order) and [ADR-0098](0098-the-repeaters-rows.md) (a row added starts as a first render starts one)
- Date: 2026-10-29
- Roadmap step: 6.2i (decided and built); 6.4 adds, removes and moves in the browser

## Context

ADR-0096 and ADR-0098 made a ranking's moves and a repeatable group's Add and Remove submit buttons:
`jfb-nav` = `up:<name>:<n>` or `down:<name>:<n>`, the option's place in the order shown counted from 1, and
`add:<alias>` or `remove:<alias>:<row>`. ADR-0100 left each of them showing the posted page again, unchanged.

Open were: what a move acts on, since it must swap the options the visitor saw; what a row added holds; whether a
button checks the answers; where the page opens after a press; and what a button does that cannot act. The page
offers no Add at a group's most and no Remove at its least, and marks a move at an end `aria-disabled`, but a page
shown earlier, a second press or a site's own markup can still post any of them.

## Options

**What a move acts on:**

1. **The options worked out again in the handler,** for the posted answers. For options from a list, a source or a
   parent this can differ from the page: the page asks with a parent a rule hides answering nothing (ADR-0096), so the
   handler would have to run the rules again to match it. And a ranking of the top few posts positions for those
   alone, so the options below them would always fall back to their own order: a move among them would show nothing,
   and an option further down than one place below the top could never climb into the ranking.
2. **The order the page showed, posted with the form:** a hidden `jfb-order-<name>` input per option with its value, in
   the order shown. The handler moves within that order without asking any source, and the page shows the options
   below the top in it. The order is no answer: a post that changes it can only reorder options the page offers, since
   the page keeps to its own options, and the pipeline checks the positions on Send.

**A row added:**

1. **Empty.** A default the editor set would be missing from that one row, unlike the rows a group starts with.
2. **As a first render starts a row:** each child field's default, a ranking in its options' own order.

**Where the page opens after a press:**

1. **At the form,** as after Back, Next and Send (the action's fragment, ADR-0100). A ranking far down a long form,
   or any form on a phone, sends the visitor back to the form's top after every press, to scroll down again.
2. **Where the button acted,** through the button's own `formaction`: the form's address with another fragment.

**A button that cannot act:**

1. **A notice,** such as "This group takes at most 3 rows". The page the visitor now sees already shows the same,
   since it offers no such button.
2. **The page again,** changed in nothing, with every answer.

## Decision

Option 2 for each question.

- **The buttons** (`jfb-nav`, ADR-0100):
  - `add:<alias>` adds a row after the others while the group has fewer rows than its `maxRows`, never more than 100
    (ADR-0098);
  - `remove:<alias>:<row>` removes the row, counted from 0, while the group has more rows than its `minRows`; the rows
    below move up with everything they hold: answers, consents' wordings and rankings' orders;
  - `up:<name>:<n>` and `down:<name>:<n>` swap the option at place `n` with the one before or after it. `<name>` is the
    ranking's control name: `<alias>`, or `<group>[<row>].<alias>` in a row the page showed;
  - a group or a ranking is found by its alias now or one it had, as the post's other names are.
- **Without a check:** a button runs nothing of the pipeline and stores nothing. It is no step through the form, so the
  page shows again with the change, every answer and no errors; Back, Next and Send check as before.
- **The order a ranking posts:** `jfb-order-<name>` once per option, in the order shown, beside its position; it is
  disabled with the field when a rule hides it, as the field's other inputs are. For a move, the handler takes the
  order shown, puts the posted positions first (so a post whose positions and order disagree follows the positions),
  moves the option, and gives positions anew to the first `top` options (all without a `top`). The page then shows
  the ranked options by position, the rest in the order posted, and any option the post did not hold in its own
  order. An answer that holds nothing but an order is no answer for the pipeline. A post without the order (a site's
  own view without it) moves among the ranked options alone.
- **Where the page opens:** each of these buttons posts to the form's address with a fragment of its own
  (`formaction`, made from the form's action, so a change to that address reaches the buttons too): Add at the row
  it adds (the row's id, `<field id>-<row>`), Remove at the group (the field's id), and a move at the option's new
  place, or its own at an end: each option of a ranking carries the id `<field id>-place-<n>`. The browser scrolls
  there and goes on from there with Tab; Back, Next and Send open at the form as before.
- **A row added** starts as a first render starts one: each child field's default, a ranking in its options' own
  order. Only that row does; the other rows keep what was posted.
- **A button that cannot act** changes nothing and shows the page again with every answer, without a notice: an Add
  at the most, a Remove at the least or of a row the page did not show, a move past an end, and any value that names
  no group, no ranking or no place.

## Consequences

- A visitor without JavaScript can fill as many rows as the group allows and put every option of a ranking in order,
  also those below its top, one press per post.
- Each press is a round trip to the server; the runtime (6.4) does the same in place and posts JSON, which needs no
  order.
- The browser puts the target at the top edge of the window; 6.3's themes give the targets a scroll margin, so the
  line above stays in view and a site's sticky header does not cover them.
- A ranking adds one hidden input per option to the page. A first render's order is the same for every visitor, so a
  cached page stays shared.
- The built-in views stay at contract version 1, as nothing has shipped (ADR-0096).
- **Revisit** if rankings grow long enough that one place per press is a burden; a "move to the top" button could
  join the two.
