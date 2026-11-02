# 0156. What the challenge tells the page

- Status: accepted; builds on [ADR-0138](0138-the-challenge.md) and [ADR-0004](0004-rendering-and-submission-paths.md)
- Date: 2026-11-02
- Roadmap step: 7.1e

## Context

A page may come from a cache, so its markup holds nothing per visitor and nothing that changes from moment to moment.
ADR-0138 made the challenge's answer carry what such a page cannot: the places left on the options it can name and what
the form's rules ask of the member. The runtime had yet to use them. Three questions were open: when the page asks, where
the words come from, and what a full option does.

## Decision

- **A form that needs the answer asks as the page loads.** The runtime otherwise wakes at a form's first touch, by when a
  visitor has already read the options and a member has already seen the form without their field. The server writes
  `data-jfb-asks` on a form with an option the answer would name, or with a rule that looks at the member, and the frame
  wakes such a form at once. The attribute says what the form holds and nothing about the visitor, so the markup stays the
  same for everyone. Every other form keeps waking at the first touch, and costs a page nothing until then.
- **The numbers are the runtime's to write.** They are in no markup, so a post without JavaScript shows none and the server
  refuses a full option there as everywhere. The words are in the runtime's language files with its other visitor words,
  not in the markup, which would carry them on every page for a request that may fail.
- **Where they show:** in a choice's label, in the `jfb-places` element the themes already style, so its accessible name
  says it; in a list's option, whose text is all it has, in brackets after its own label.
- **A full option is disabled, unless the visitor has chosen it.** Its label says why. A choice the visitor made stays theirs
  to change: a disabled control would not be sent, and the answer would vanish without a word. They hear why from the
  server's `full` error when they send it, and once they move off it, it is disabled like the others. A full option that
  only a default chose is let go, as the visitor never chose it. The one the keyboard is on keeps the focus until the
  visitor's next change. A rule leaves a full option disabled when it shows the field (`data-jfb-disabled`), and an option
  the view disabled for another reason stays disabled whatever its places. An error link to a full first choice goes to the
  field's next control.
- **Each answer counts.** A renewal tells the numbers again and the page shows them. A send refused for a full option has
  made them old, so the page asks once more only to hear them, whose token it drops, and keeps the challenge it holds, which
  the entry and its files name. A form put in another's place when it was published again does the same: an unused challenge
  costs nothing, and the new form may have other options.
- **The member is known to the rules through the context,** kept per model, and an answer that names no member takes it
  back. A rule decided before the answer comes decides as for a visitor, as the server's render does; the answer counts as
  an answer does, so the rules are decided again and what follows them too: a page they hid, the buttons and the progress.
  A form put in another's place keeps the member, so a page for members is still there at its wake. Validation rules count
  as well as rules. The server decides for itself on every page check and entry, so a visitor who forges the answer only
  changes what their own page shows.

## Alternatives

- **Asking at the first touch for every form.** The cheapest for a page, and the one ADR-0138 described, but a visitor would
  read options and only see them fill up, or lose them, when they touch the form.
- **Asking from the frame with no logic loaded.** It would save the logic's chunk, but the rules need it for the member, and
  the frame would have to carry the places' words and code that every page without such a form then pays for.
- **Writing the words in the markup.** It would keep the runtime smaller by a few bytes for each language, but every form
  with places would carry them, and a theme's view would have to.

## Consequences

- A form with places or a rule on the member loads the logic and asks for a challenge on every page view, which the
  `challenge` policy limits (by default 10 a minute per address and form). A busy shared address past it shows no numbers,
  and the server still refuses a full option; a site that expects this raises `Challenge` in the rate limits.
- Its first time counts from the page's load, not the first touch. That makes the time signal more generous, except for a
  tab left open for days: past the renewal's day of grace its next challenge counts from then, as it does for a form touched
  days ago, and a quick send scores as one. It only adds to a score, and the visitor's answers stay.
- A signed-in member sees their field appear a moment after the page shows, as a page from a cache cannot have it.
