# ADR-0163: Save and resume

Status: accepted, 2026-11-02 (ROADMAP 8.6); the runtime's control added 2026-11-14 (ROADMAP 8.6b, after ADR-0251). Builds on ADR-0009 (privacy defaults), ADR-0038 (drafts take a number), ADR-0090 and ADR-0091 (places and unique answers), ADR-0138 (the challenge), ADR-0159 (visitor mail) and ADR-0162 (what waits).

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

**The runtime's control** waited for the budget until the owner raised it to 100 KB (ADR-0251); it is a chunk of its own, about 2.6 KB gzip, that only a form offering it loads.

- **Where it shows** is the Phase 2 design's (docs/ux/website/apply-room.html): a link button last in the form's buttons' row opens a panel in the row's place, drawn as a notice and named by its heading. It says, before the address, where the link goes, for how many days and what is not kept; then the address as a field, with its error under it, and its own row: "Send the link", the one primary action while the form's buttons are out of sight, and "Cancel", which, as Escape does, brings the buttons back with the focus on the link. Enter in the address saves; it never sends the form. It shows only with JavaScript, on a form the projection offers it for (one that stores entries) and on a page of the site, which the link comes back to; the Razor views and the headless element write the same template. The pages chunk, which writes the row's buttons for each page, keeps what else the row holds after them.
- **What it says once saved:** a notice in the panel's place, which takes the focus, says where the link went and until when it works, the last day in the form's language from the answer's `expiresUtc`, so a mistyped address shows at once. The panel, which says the days before anything is saved, and the mail each have a sentence of their own for a link of one day.
- **What it sends:** only the draft's members (revision, address, culture, content, page, answers, the token it replaces, and the protection a send takes: the challenge, renewed once on `401`, the puzzle, the captcha, which asks the visitor first, and the honeypot). A saving that went through ends the token the form held, as the new draft takes its place.
- **A link taken up:** the token leaves the address before anything else; a link opened in the tab that already shows its page changes only the fragment, which loads nothing, so the frame loads the page again; one lookup serves every form of the page, and only the form the draft belongs to takes it up. Its answers come back in the background, so the form is ready while they are on their way; then the page they were saved on shows, and "Welcome back" says what to add again and takes the focus. A link used or past its time says so above the empty form, on the form whose tab held it or the first form that asked; the design put that notice in the form's place with a "Start again" button, but the form is already on the page, so it stays below the notice and starting again needs no press. With no answer at all, the tab keeps the token, so a refresh asks again. The notices are the theme's, written with the form's other notices, so they look and read as the rest; a status line only a screen reader hears would leave a sighted visitor wondering why the file is gone. As every notice with a code, they are alerts.
- **Its words** are visitor texts of a group of their own (ADR-0230), in the seven languages, the sensitive answers named as the mail names them.
- **The editor's switch** is under the Settings view's Submission › More options, with the days the link works (1 to 365) under it once it is on; it waits for entries to be stored, as a form that stores none keeps no draft, and a closed Submission group says it is on.

## Consequences

- A resumed form asks again for files, consents, rankings and sensitive answers.
- Each draft takes an entry number, so numbers show gaps (ADR-0038).
- The token sits in the outgoing mail's row until that row is purged, as a verification link's does. Whoever can read that table can read the draft anyway.
