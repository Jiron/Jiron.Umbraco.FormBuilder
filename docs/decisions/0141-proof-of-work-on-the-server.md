# 0141. Proof of work on the server

- Status: accepted; builds on [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) and
  [ADR-0138](0138-the-challenge.md); the runtime's part waits with [ADR-0120](0120-sending-waits-for-the-runtimes-budget.md)
- Date: 2026-11-01
- Roadmap step: 7.2a

## Context

ADR-0012 set the puzzle: the challenge carries a nonce and a difficulty, the runtime finds a counter, and the server checks
it once with the challenge. It left open what the nonce is, what a wrong answer answers, where a form's level comes from
when the challenge endpoint knows no page, and what difficulty to start at while the runtime's part is blocked.

## Decision

- **The nonce is the challenge's own id,** and the difficulty a byte of the signed token. The signature and expiry of the
  challenge then hold for the puzzle, and since a challenge makes one entry, an answer counts once, with nothing new to
  store. The hash is the SHA-256 of `nonce:number`, the number in digits, compared on its first 32 bits; a puzzle never
  asks for more than 24.
- **A wrong or missing answer is `401 challenge_required`,** before the answers are read: the stable codes have nothing
  better, and it says what a page should do, which is to start again with a new challenge. A challenge issued before the
  form reached the level holds no puzzle, so it meets the same answer. A retry of a stored submission is answered before
  this, so it never needs its answer checked again.
- **Only a submission is checked.** A page's check and an upload need the challenge alone, since the puzzle should cost a
  bot per entry, not a visitor per page. `proofOfWork` on a page's check is an unknown member, as `captchaToken` is.
- **The level is the form's, or its site's for a form that inherits it.** `protection` gains `Level` (`standard` by default)
  and `ProofBits`. A site's level is `standard` or `proofOfWork`, and the captcha level with its provider since 7.2b ([ADR-0142](0142-the-captcha-providers.md)). The
  challenge endpoint's body gains an optional `contentKey`, the same page a submission names, so both read the same site's
  level; without one, it reads the global settings, as a submission without a page does.
- **A post without JavaScript** cannot solve anything, so it is scored `no_script` (100) and goes to Spam, as ADR-0012 says, so
  a real visitor is never lost and never trusted. A server key skips the puzzle.
- **The difficulty starts at 16 bits,** settable from 8 to 24 per site. Each further bit doubles the work. ADR-0012's example of
  18 assumed a figure the blocked runtime part must measure; 7.2c's check (under 2 s on the demo machine) sets the default
  it can prove, and until then no page solves anything, so a lower start is the safe side for whoever enables it.

## Consequences

- **Since 7.2c the runtime solves the puzzle at a send** (ADR-0157). Until then, a website form at this level took no entry from
  a browser with JavaScript in practice. A post without JavaScript still reaches Spam with `no_script`, and nothing is lost.
- **Measured in 7.2c (ADR-0157):** at the default of 16 bits the page takes 140 to 200 ms on the demo machine from the press to the send, worker included, so the check's 2 seconds leave room for a device ten times slower. The default stays, as each further bit doubles what a slow phone waits.
