# 0157. Protection on the page

- Status: accepted; builds on [ADR-0141](0141-proof-of-work-on-the-server.md), [ADR-0142](0142-the-captcha-providers.md) and
  [ADR-0156](0156-what-the-challenge-tells-the-page.md)
- Date: 2026-11-02
- Roadmap step: 7.2c

## Context

The server checks a puzzle's answer and a provider's token. A page has to find the answer without freezing, and has to show a
third party's check without letting anything reach that third party before the visitor agrees, under a mode the site chooses.
No setting held that mode yet, and the JS API lacked `consent`. The runtime had 2.17 KB of its 60 KB left.

## Decision

- **The hash is the runtime's own.** A browser's own digest exists only on secure pages and takes far longer per hash. The
  puzzle only ever asks for the first word of one block of ASCII (a nonce of 32 characters, a colon and at most 20 digits), so
  the code is that and no more.
- **One file is the chunk and the worker.** The chunk starts itself as a module worker, so it adds no second file and needs no
  bundler plugin, and it imports nothing, so it runs in a worker as it does on the page. A worker cannot start for a runtime from
  another origin, as under the headless element, or under a policy that refuses it; the page then looks in slices of 20,000
  between its other work.
- **A puzzle is solved at the send,** for the challenge held then: one that was renewed brings its own. A slow device says so in
  the form's live region after 1.5 seconds, beside the busy button. A form put in another's place keeps its challenge but not the
  puzzle, so its first send meets `401`, and the renewal brings one; a file sent under the first challenge would have to go up
  again, in a case this rare.
- **The default stays at 16 bits,** measured at 140 to 200 ms on the demo machine from the press to the send.
- **Each form decides by its own mode, and nothing stores consent.** Consent lasts the page. A page may hold forms under different
  sites' settings, so one form's visitor agreeing never loads another form's provider on terms that form's mode does not accept:
  - `explicit` (the default): the notice's button, or a consent field of that form whose `loadsCaptcha` setting says that ticking it
    agrees;
  - `cookiebot`: the visitor's choice for the site's category, read at the start and at Cookiebot's events, with a button to its
    settings;
  - `cmp`: the site's consent manager calls `consent.grant('captcha')` and `withdraw()` on `window.JironFormBuilder` on each page, from
    its own record, once the runtime is there, as `jfb:ready` tells it; `jfb:consent` on the document tells the forms, and the notice's
    button of an `explicit` form gives the same consent;
  - `implicit`: the form itself, only where the host allows it (`Privacy:AllowImplicitCaptchaConsent`; a stored `implicit` reads
    as `explicit` otherwise, ADR-0008).
- **The mode and category are the site's protection settings** (`captchaConsent`, `cookiebotCategory`), per site, and the challenge
  tells them with the provider. Where several sites' settings count (ADR-0147), sites that differ read as `explicit`.
- **A form with a captcha asks as the page loads,** so the notice stands before the form is touched (`data-jfb-asks`, ADR-0156), for
  its own captcha and for one it inherits. A form with only a puzzle waits for its first touch.
- **The place** is the one the captcha field's view writes, or a new one above the form's buttons. It shows the notice, the
  provider's check, or nothing for a score-based provider, which is asked at each send. A place on a page the visitor is not on, or
  one a rule hid, comes to the buttons when a send asks for it, and a consent field they cannot reach gives way to the button.
- **A send without a token does not go, while the visitor can do something about it.** The place says what is missing and the focus
  moves to what acts on it. A token counts once, so any refusal resets the check, and `403 captcha_failed` asks for a new one.
- **Where no one can pass, the lead still counts (D14).** A script that did not load and a score too low have no check to complete, so
  the place says so and the send goes by post, into Spam with `no_script`, or shows the notice of a failed send where a form has no
  address. A first challenge that failed goes the other way: the page asks again and stays, since a post could not pass there.
- **Nothing waits for ever.** A provider's token and a puzzle's answer get what a send's own request gets, 40 seconds.
- **The challenge's answer counts wherever it comes,** so a page cached before a site's level changed still shows the check at the
  form's first touch.
- **The provider is the form's:** `hcaptcha` reads `window.hcaptcha`, any other `window.grecaptcha`.
- **Withdrawn, the check and its token go.** A script already loaded cannot be unloaded; the next page loads none.
- **The providers' scripts are one API:** `render`, `reset`, `execute` and `ready`. A site's own provider offers the same under
  `grecaptcha`.
- **Budget:** uploads and signatures now share one file (`files-chunk`), for a banner and the code they have in common, which a form
  with only one of them pays for with the other's 2 to 3 KB. The runtime with every feature is 59.66 KB of 60.

## Alternatives

- **A browser's digest in a Web Worker:** unavailable on plain HTTP sites, and each hash awaits a promise, far slower per hash.
- **A worker file of its own:** a second file and banner, and the same code again for the slices.
- **Storing consent, so a visitor is asked once:** a store of consent is itself something to consent to, and a site's consent
  manager already holds that record. Rejected; the `explicit` button is asked per page.
- **Writing the notice in the server's markup:** every theme's view would carry it, and its wording depends on the site's mode,
  which a page from a cache may hold an old copy of.

## Consequences

- A site in the `cmp` mode calls `grant` on every page, as consent managers do for what they gate.
- A strict Content Security Policy has to allow the chosen provider's hosts, and a nonce is carried to its script.
- A shared address past the `challenge` policy's limit, and the second page-load ask it brings, shows no check until a send asks again.
- Real keys are checked by the owner only (`docs/OWNER-TODO.md`); these tests run against a fake script and the stub.
- 8.6 and 11.5 still add to the runtime, with 0.34 KB left; past 60 KB is the owner's to decide (ADR-0146).
