# 0154. The field behaviours

- Status: accepted; builds on the rows of [ADR-0098](0098-the-repeaters-rows.md), the ranking of
  ADR-0101 and the options endpoint of [ADR-0086](0086-the-options-endpoint.md)
- Date: 2026-11-02
- Roadmap step: 6.4o (decided and built)

## Context

Without JavaScript a row's Add and Remove and a ranking's moves post the page, and a cascading field offers what the last post's
answers offered. With the runtime they work in place, and a counter, the "Other" text and a text area follow what is typed.

## Options

**Where a new row's markup comes from:** a clone of the row before, which holds its answers, errors and rules' state and needs
every part undone; or a row the server writes as a first render starts it, in a `template`. The template: its fields start as the
server's would, in every theme and every site's view, and the runtime only gives it its place.

A row in the template has no state of the rules yet, so it requires what the definition requires, and the rules decide the rest
once it is on the page. A cascading choice list's own template stands behind what shows, since a site's stylesheet may follow a
legend with `legend + *`.

**A removed row:** leave a gap in the numbers, which the server's reader does not take; or give the later rows its place, as a post
would render them. The latter: every name, id, reference and word that holds a row's number moves with it, so what the page
posts next is what a post would have rendered.

**A cascading field's options:** filter a field's own options in the browser from the model's `parentValues`, and ask only for a
list or a source; or always ask the endpoint. Always ask: one way, whatever the options come from, with what the server resolves.

**A ranking or a grid below a parent:** their options would need another way of writing each, for a case that stays rare. They keep
the server's options until a post, as without JavaScript, so such a form sends by post, which renders them anew.

**An option's image** on a card comes as a media key, which the page cannot show without a request of its own per image. A card of
a cascading field written in the browser shows no image.

## Decision

- **A chunk of its own** (`fields-chunk`), which a form with one of the behaviours loads. It listens at the form, so a row added
  later needs nothing of its own.
- **The repeater's markup:** its limits and words as `data-jfb-least`, `-most`, `-legend` and `-removed`, a `template` with a row
  at the place after the others, and Add and Remove always written, `hidden` where the limits leave them nothing to do. Without
  JavaScript a hidden button is no button, as before.
- **A row added** takes the focus at its first control, and brings its uploads' zones and its defaults with it. **A row removed** gives
  the focus to the next row's Remove, the row before's, Add, or the group, says "Guest 2 removed." in the live region, and takes the
  error summary away: its links name the rows as they were, and the next send lists them anew.
- **A ranking's move** renumbers its items, adds or takes off a place's position where the field's `top` says, keeps the focus on the
  button pressed, marks the ends `aria-disabled` as the server does, and says "Bread moved to place 1."
- **A counter** keeps the server's "Up to 2000 characters" while the text is empty, then says what is left and how far past the
  limit, with `data-jfb-over`, in the units the server counts. The server's check, not the counter, refuses the text.
- **"Other"** shows its text only while its choice is chosen, and moves no focus, so arrowing through radio buttons is not stopped. A
  text left behind takes no name, so it is not an answer, and comes back with its choice.
- **`autoGrow`** writes `data-jfb-grow`: such a text area takes the height of its text; without it, its rows and a scrollbar.
- **A cascading field** carries `data-jfb-cascade` on its select, or a `template[data-jfb-cascade]` holding a sample choice for radio
  buttons and checkboxes. The runtime asks `options` when its parents' answers change, naming each level, the culture and the page's
  content, all from one reading of the page; only the answer to the last question counts, and no answer leaves what shows. It also
  asks at the start for a field whose parents hold answers by then, a default or what the browser restored, and for a row added
  below a parent outside it. Options that have not changed are not written again, so the focus stays. An answer still offered stays,
  as does a chosen "Other" with its text; one no longer offered goes. A written list is off for as long as a rule hides it or it has
  nothing to offer, and the rules and the checks then read it anew.
- **A form with a repeater or a ranking takes its files on the page:** a refused file in a row words its error for that row, and a
  row's zones and signature pads read their names from their markup, as a row moved renames them.

## Consequences

- The markup grows by one row per repeater on every page that has one, inert in its `template`.
- A site's own `repeater` or `ranking` view that leaves out what the runtime needs keeps working through its buttons only where it
  writes them as the contract says; ADR-0015 lists what the runtime reads.
- A form published again meanwhile still goes by post when it holds a repeater or a ranking (ADR-0120): their state is not carried,
  and the files and drawings already up ask to be given again there, as a post cannot claim them (ADR-0152).
- **Revisit** if a card's image in a cascading field is asked for: the endpoint would answer an address.
