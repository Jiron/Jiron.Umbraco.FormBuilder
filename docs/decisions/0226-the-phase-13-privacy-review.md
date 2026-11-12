# 0226. What the Phase 13 privacy review changed: unconfirmed entries stay out of exports, ten years at most, no link codes in stored mail, a fuller personal export, and exceptions without their messages

- Status: accepted; builds on [ADR-0009](0009-privacy-defaults.md) (the privacy defaults and logs),
  [ADR-0162](0162-state-changes-verification-and-content.md) (email verification), [ADR-0222](0222-retention.md) (retention),
  [ADR-0223](0223-data-subject-tools.md) (the data subject tools) and [ADR-0225](0225-sensitive-and-kept-answers.md);
  amends ADR-0222's most days (its point 9) and retires the stand-in screen of [ADR-0165](0165-the-section-shell.md)
- Date: 2026-11-09
- Roadmap step: 13.1, its part 13.4 (ADR-0197)

## Context

Step 13.4 writes `docs/privacy/` (a facts sheet, a checklist for site owners and privacy-policy templates in English and
German) and has a subagent review the running demo and the docs as a privacy reviewer would, against the GDPR, the
TDDDG, the revDSG, UWG § 7 on double opt-in, and the European Accessibility Act. The review found the demo's pages
without cookies or third-party requests, consents never ticked in advance, captchas loaded only after consent, no IP
address kept by default, the double opt-in sound (opening the link confirms nothing; the button does), and the form
pages accessible. It also found what this ADR settles:

1. **Unconfirmed addresses in exports.** An export with no state chosen took the states a list shows, entries waiting
   for their email confirmation among them, so a file taken for a mailing could hold addresses nobody confirmed.
2. **Retention up to 36,500 days.** A hundred years is keeping for good without the host's say, which ADR-0222 reserves
   for `AllowRetentionOff`.
3. **Link codes in stored mail.** The confirmation link's code and the resume link's token stayed in the stored mail's
   bodies for the mail's 30 days, and in backups: anyone who reads the database could confirm an address, weakening the
   proof of consent, or open a draft.
4. **The personal export** (Art. 15) left out the entry's context values, its spam check, its time to complete, its
   confirmation time and the mail sent to the person.
5. **Anonymising** left a past hold's reason, free text that may name the person, and the digest of the answers, which a
   guess of the answers could be checked against.
6. **Exceptions in logs.** Some background loops and the public API logged whole exceptions, and a database's message
   can quote a value it refused (SQL Server's truncation error does, and a duplicate key's names it).
7. **The demo** modelled what the checklist warns against: its newsletter had no email confirmation, and its careers
   form made agreeing to keep an application a condition of applying.
8. **Every entry keeps the signed-in member,** with no way for an anonymous survey to leave that out.
9. **The docs** stated the time an unconfirmed entry is kept wrongly (the link's 7 days, where a 7-day grace follows),
   said the challenge is always asked at the first touch, missed the captcha provider's own cookies and the copies an
   erase cannot reach, and lacked HTTPS, access logs and a Swiss section.

## Decision

1. **An export that chooses no state leaves the unconfirmed entries out** (`EntryLists.ExportedStates`), and its plan
   counts them (`EntryExportPlan.Unconfirmed`), which the dialog says under the number: "3 entries waiting for email
   confirmation stay out. To export them, filter by their state." Choosing the state, or ticking entries, exports them.
   The lists still show them by default, as a user needs to see who is waiting. As a new newsletter's entries may all be
   waiting, an export of nothing says "No entries to export" and its button waits for an entry, rather than offering
   "Export 0 entries".
2. **At most 3,650 days** (ten years, the longest that laws on keeping records commonly ask for), for the site's days per
   state and a form's own, its "For a number of days" included (`SiteRetention.MaxDays`, `DefinitionSaveChecks.MaxDays`).
   Longer is keeping for good, which needs the host, as before. The limit is checked where values are saved, as every
   such limit is: the settings' validator, and the form's save checks, which imports and files pass through too.
3. **A sent link mail keeps no code.** Once the outbox records a confirmation or resume mail as sent, it replaces the
   code in its stored bodies with `removed` (`LinkSecrets`, `IEmailRepository.ReplaceSentBodiesAsync`); the link still
   names its page. A mail that has not left yet keeps its code, as a retry must send a working link, and a new link is a
   new mail. Amended by [ADR-0236](0236-what-the-security-review-changed.md): a mail the limits hold back, which is never sent nor tried again, keeps no
   code either, from the unit of work that stores it. The confirmation code stays in the address (`?t=`) rather than moving to a fragment: the confirmation page
   works without JavaScript, which a fragment would need; the checklist and the facts sheet tell sites to keep their
   access logs short or without query strings, and the code is worth nothing once the address is confirmed.
4. **The personal export adds** the entry's context values, its spam check (score and reasons), its seconds to
   complete, when it was confirmed, and the mail sent to the address the visitor typed (to, subject, when, text). Mail to
   the team repeats the answers already exported and names other people, so it stays out.
5. **Anonymising clears** a hold that has ended with its reason, and sets the digest to zeros
   (`IPersonDataRepository.NoDigest`); a retry of the answers is then refused as another submission, which no anonymised
   entry needs.
6. **No exception's message reaches a log.** Every log call that takes an exception passes `LoggedException.Of(…)`,
   which keeps the exception's type, a database's error code and SQL state, its stack and its inner exceptions so
   wrapped, and says "its message is not logged" in place of the message. The type, codes and stack are what an
   operator needs to find a failing part; the message is what may hold an answer.
   Amended by [ADR-0236](0236-what-the-security-review-changed.md): a log message's exception parameter is a
   `LoggedException` itself, now in Core's root namespace, so a call that passes an exception as it is does not build, and
   an architecture test finds any log message of Core and Web that takes another exception.
7. **The demo's newsletter asks for confirmation** and says so after the send ("Almost done: we sent a link to …"), as
   the built-in template does, whose wording now says the subscription starts once the visitor confirms it, not once
   they open the link. The careers form's consent becomes optional and asks for something optional: keeping the
   application for other posts.
8. **The anonymous form goes to step 14.3,** which designs the member link with the features that need it (members
   only, one entry per member); the roadmap adds the switch and its check there. Until then the facts sheet says a form
   cannot leave the member out, and the checklist says what to do meanwhile.
9. **The docs say what the code does,** each point corrected; the templates gain a Swiss section (data abroad, the
   FDPIC), the captcha's own cookies, the spam check and context values among what is stored, and a shortened address
   described without claiming it identifies no one. The questions only a lawyer can answer go to `docs/OWNER-TODO.md`.
10. **The stand-in screen of 9.1 is gone:** every item of the section and every view of the designer names its own
    element, and a test fails if one does not (ADR-0165's `comingScreen` lines are all replaced).

## Consequences

- A test reads the facts sheet's tables of browser storage and requests and compares them with what the demo's contact
  form does as a visitor fills it in and sends it, so the sheet cannot drift from the runtime; another checks that each
  template starts with its warning.
- Exports and their dialog differ from the list by the unconfirmed entries, which the dialog states rather than hides.
- An operator reading a failure in the logs sees its type, codes and stack, and reproduces the message where it is
  needed rather than reading it from the log.
- The demo's newsletter sends a confirmation mail where the site has a base address, and its entries wait until
  confirmed, as a real newsletter's should.
