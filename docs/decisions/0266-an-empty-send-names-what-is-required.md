# 0266. An empty send names what is required

- Status: accepted; amends [ADR-0112](0112-checks-and-messages-on-the-page.md) (the order of the checks on the page)
  and the pipeline's step for the empty submission of [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md)
- Date: 2026-11-26
- Roadmap step: 27.1 (the first site's finding 1)

## Context

A send with no answer at all was refused with one problem, `empty` ("Answer at least one question before you send the
form"), before any field was checked: in `FormSubmissions` for every transport, in the runtime's checks, and so on the
page without JavaScript, in the browser and from the public API alike. The first site outside the demo met it at once:
its form shows no required marks, so a visitor who pressed Send first learnt only that the form was empty, answered
something, and learnt what was required at the second try. A send with one answer names every required field the
first time.

The empty refusal was cheap and that mattered: it came before the proof of work, the captcha, the spam score and the
uploads, so an empty post spent nothing and stored nothing, and the message was right for a form that requires
nothing, where no field has an error to show.

## Options

1. **Keep the order.** The visitor needs two tries on a form with required fields.
2. **Drop the empty refusal.** A form that requires nothing would then store an entry without an answer.
3. **Check the fields of an empty send too, and say it is empty only when no field has an error.** The empty send
   still ends before the proof, the captcha, the spam score and the store; only the reading and the checks run, which
   for a send without answers ask no option source, no upload and no other entry.

## Decision

Option 3.

- **The order.** A send with no answer at all (every posted text blank once cleaned, a consent's wording aside, as
  before) is read and checked as any send. When a shown field has an error, the refusal names those errors exactly as
  a send with one answer would, and no `empty` problem; only when no field has one does the single `empty` problem
  come back, at the form. A page's check (Next) and a draft are unchanged.
- **What it still skips.** The empty send is refused before the proof of work or challenge is asked for, before the
  captcha is verified (a token is spent once), before the spam score, the places and the unique answers, and before
  anything is stored; it reads no upload (a token is an answer, so an empty send names none). It now reads the site's
  time zone and the member's groups, which the rules need, and runs the reader.
- **Server.** `FormSubmissions.EmptySend` makes the refusal from the fields' problems; every transport (the page
  without JavaScript, the entries endpoint, the public API) gets it from the one pipeline.
- **Browser.** `sendRefusal` in `packages/logic` decides it alike from the post and the page's errors. Where a shown
  required field's check is the server's (a field whose files the page does not take, a value the server resolves),
  the browser cannot know whether the server names it, so it lets that send go and shows the server's answer, which
  is either that field's error or the empty message.
- **The proof.** The `send` cases of `tests/vectors/posts.json` post a form with required fields and one without,
  empty, blank, with a consent's wording only and with one answer, through the no-JS reader, `SubmissionReader` and
  `EmptySend` in NUnit and through `readPost`, the built-in checks and `sendRefusal` in Vitest. The rendering fixture
  of the server's own pages (`Rendering/ErrorFixtureTests`) posts nothing at all to its form of every check, which now
  shows each required field's error, and to a form that requires nothing, which shows the empty message.

## Consequences

- A visitor learns what is required at the first press of Send, with or without JavaScript, in every theme, and a
  headless site gets the same `required` errors it gets for a send with one answer.
- The empty message stays, for a form that requires nothing; its wording is unchanged in all seven languages.
- An empty send costs the reading and the checks of a form without answers, more than the old early refusal and still
  well below a send that is stored.
