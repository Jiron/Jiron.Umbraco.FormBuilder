# 0140. Content rules and the spam folder

- Status: accepted; builds on [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md),
  [ADR-0023](0023-undo-for-entry-state-changes.md) and [ADR-0139](0139-the-signals-and-the-score.md)
- Date: 2026-11-01
- Roadmap step: 7.1d

## Context

ADR-0012 set the weights of the content rules, the strictness levels, the shape of `IFormBuilderSpamCheck` and what
"not spam" does. Building them raised what it left open: what the rules read, what counts as a word and as a link, whose
maximum "the form's maximum" is, what a failing check does, how "not spam" knows the actions a rule chose, and what of it
comes before the entries screen.

## Decision

- **The rules read what a visitor typed:** the answers of fields whose type compares as text and whose value the server
  does not resolve. An option's value, a file, a number and a hidden value never count, so a form that offers "Casino" as a
  choice cannot score itself. They read the answers after the field types did, so what they judge is what is stored.
- **A blocked word stands as a word of its own,** folded as rules compare (ADR-0005), so "class" does not hold "lass"; a
  phrase is compared as written. Each word that stands in the texts scores once however often it stands, as in ADR-0012's
  table. The reason is stored once, and the score tells how many there were.
- **A blocked domain** compares in its ASCII form, also with what lies under it, on email fields only.
- **A link** starts with `http://`, `https://`, `ftp://` or `www.`, not inside a word or an address. A bare domain is no
  link, as people write them in plain sentences; counting them would cost more real visitors than it stops bots.
- **The maximum is the site's.** ADR-0012's table calls it the form's, but a form has only its strictness (section 4.8), so
  the site's `MaxLinks`, 3 by default, holds for all its forms. At `normal` a web address field's answer is no link to
  count, as holding one is its job; at `strict` with a maximum of 0, it counts as well, which is what ADR-0012's "any link"
  means.
- **The settings:** `ContentRules` (normal by default), `BlockedWords`, `BlockedDomains` and `MaxLinks` join the
  `protection` section, each list of at most 1,000. A site's level is never `inherit`.
- **`IFormBuilderSpamCheck`** is an ordered collection through `SpamChecks()`, empty by default. A check that throws or takes
  longer than 5 seconds counts for nothing and is logged by its type's name, since a broken or slow check must never lose an
  entry; even one that ignores its token is left behind. A check is not asked once the score has reached 100, since it may be
  a paid call. Its score is held to 0 to 1,000, and a reason that could not be stored or would read as two is stored as
  `spam_check`. It sees the texts by alias, the culture, the page, the address and whether a server key sent it.
- **Every one of these holds for a server key,** as section 4.6 says; only the honeypot and the time do not.
- **"Not spam" is `EntrySpamFolder`,** one unit of work over the state, a `notSpam` event, the runs and the count.
  - The rules chose the actions when the entry came in, over the answers then, so the `submitted` event of an entry that
    went to Spam records them, and the move queues those of its version, which are still enabled there. Re-running the rules
    later would need the query values, which no entry keeps.
  - A backoffice user's move waits out ADR-0023's undo window; the event and the runs share a change key, so 8.5's Undo finds
    them. The system's move runs at once.
  - An entry on a form with verification and no confirmed address moves to Unverified and queues nothing; 8.5 sends its mail.
  - The day it moves counts as a submission, and its `spam` count stays, since that is what came in on its day.
  - Its places are taken again by the repository, also past the limit: the screen that offers the move asks first (11.2).
- **Its screen and endpoint come with the entries screens,** where the undo toast and the question about a full option live
  (11.x). This step gives them the service they call.

## Consequences

- The default settings block no word and no domain, so a site that sets nothing gets only the links rule, whose 40 points put
  nothing into Spam by themselves.
- An entry marked as spam by hand or by an action has no recorded actions, so "not spam" queues none for it: its `submitted`
  actions already ran when it came in.
- **Revisit** if sites ask for a maximum per form, or for bare domains to count as links.
