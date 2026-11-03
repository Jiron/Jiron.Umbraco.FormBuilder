# ADR-0163: Save and resume

Status: accepted, 2026-11-02 (ROADMAP 8.6). Builds on ADR-0009 (privacy defaults), ADR-0038 (drafts take a number), ADR-0090 and ADR-0091 (places and unique answers), ADR-0138 (the challenge), ADR-0159 (visitor mail) and ADR-0162 (what waits).

## Context

A long form should survive its visitor leaving. A saved form holds personal data, and a link that brings it back is a credential in a mailbox. Mail to an address a visitor typed is the main abuse surface of any form.

## Decision

**A draft is an entry in the state `Draft`.** It sits in the tables every privacy tool already looks at. It takes no place, no unique answer, no count and no number that matters, and it runs nothing. Its answers need not be whole: an answer that does not read well is not kept, and the rest are.

**Some answers are not kept at all.** Files (an upload without an entry goes after 24 hours), consents (one counts only for the wording shown when it is given) and rankings. So too anything a form marks as sensitive or keeps only until its actions run, on its own or through its repeatable group: a link in a mailbox should not bring those back, and asking again costs little. And what the server fills in itself, which no visitor gets to read: a hidden field's value, and a value prefilled from the member and hidden. The check runs again when a link is opened, so a field marked sensitive since does not come back either. The mail and the page say what is asked for again.

**A form that stores no entries keeps no draft.** It answers `409 resume_unavailable`, and does not offer the control.

**Submitting makes an ordinary entry.** The body of `entries` names the link's token as `resume`, and the whole pipeline runs: the checks, the protection, the places, the unique answers. The draft goes in the unit that stores the entry, so one that cannot be stored keeps its draft, and of two submits that name one draft the second answers `409 duplicate_submission`. Turning the draft's row into the entry was the alternative, but it would need a second way through the pipeline for what is a plain submission. A token that names nothing, or another form's draft, never stops a submission. Saving again names the token too, and the new draft takes the old one's place, so an older link cannot bring back older answers.

**The link.**

- The token is 32 random bytes, and only its hash is stored. The address's fragment carries it, so no server log sees it, and the runtime posts it in a body.
- The link keeps working until the draft is submitted or replaced, its time is over, or the form stops offering it. A link past its time answers `410`, and an unknown or used one `404`.
- The link goes to the site's own address and a page of that site, by the content's own address: the request says which page, as every submission does, and never writes the address. A request with no such page, or a site that does not know its address, answers `409 resume_unavailable`.
- A draft answers with when the link stops working. Nothing a response holds gives the link away.

**What keeps it from being abused.** A draft takes the protection a submission takes: the challenge, which makes one draft, the puzzle, the captcha and the honeypot. A request the protection calls spam is answered the same, spends its challenge the same, and stores nothing, so nobody learns what tripped it or makes the site send anything. The mail goes through the visitor limits of ADR-0159. A limited mail is answered the same too, and its draft goes at once, as nothing could ever bring those answers back.

**Only the mail keeps the address.** A draft does not store the address it was sent to.

**Drafts are purged when they expire,** through an event due at that time, as ADR-0162's verification does, with any mail still waiting.

**The answers come back by the form the site serves now,** with the fields it still has. A form published since has its way: the revision in the answer is the current one, and a field it dropped is gone.

**The runtime's control waits for a decision on the budget.** The control and the restoring of answers come to 1.94 KB gzip as a chunk of their own, which puts the runtime at 61.95 KB against the 60 KB of ADR-0146. That ADR leaves anything past 60 KB to the owner, so step 8.6b holds this part, its work waits on `wip/8.6b-resume-control`, and `docs/OWNER-TODO.md` asks. The server side stands on its own in the meantime: a headless site can use both endpoints today.

## Consequences

- A resumed form asks again for files, consents, rankings and sensitive answers.
- Each draft takes an entry number, so numbers show gaps (ADR-0038).
- The token sits in the outgoing mail's row until that row is purged, as a verification link's does. Whoever can read that table can read the draft anyway.
