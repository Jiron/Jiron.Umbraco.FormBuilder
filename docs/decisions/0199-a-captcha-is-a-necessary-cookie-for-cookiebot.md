# 0199. For Cookiebot, a captcha is a necessary cookie, or a marketing one

- Status: accepted
- Date: 2026-11-06
- Roadmap: the owner's messages of 2026-11-06; amends [ADR-0157](0157-protection-on-the-page.md) (the consent modes on
  the page); §4.10, 12.4a

## Context

A site that uses a third-party captcha (reCAPTCHA, hCaptcha) chooses when a page may load its script (§4.10, ADR-0157).
In the `cookiebot` mode the page loads it once Cookiebot reports the visitor's consent to the site's chosen category.
That category was `marketing` by default, and could be `preferences` or `statistics`; `necessary` could not be chosen,
as ADR-0157 offered only the categories a visitor is asked about.

The owner wrote on 2026-11-06: "not sure if recaptcha and cookiebot was a topic here already but it should be marked as
necessary cookie (like cookiebot already does) not marketing cookie", and then: "Yeah ig add an option for cookiebot to
switch between necessary and marketing ig, necessary being default". Cookiebot's own scan files reCAPTCHA's cookie as
necessary, so a site's cookie declaration lists it there; asking for marketing consent before the check loads holds
back a form whose protection the site counts as necessary.

## Options

1. Keep `marketing` as the default and add `necessary` beside the three.
2. `necessary` as the default, beside preferences, statistics and marketing.
3. **A switch between `necessary`, the default, and `marketing`,** as the owner wrote.
4. Change the default mode from `explicit` to `cookiebot`.

## Decision

Option 3.

- **In the `cookiebot` mode** the category is `necessary` by default, or `marketing`; a save of any other is refused.
  Cookiebot reports `necessary` as given on every page it runs on, so the page loads the captcha as soon as Cookiebot is
  there. With `marketing` it waits for the visitor's consent to marketing cookies, as before. Without Cookiebot on the
  page nothing loads, and the notice offers the cookie settings: for `marketing` it names the category; for
  `necessary` it says only that the cookie settings load the check.
- **The default mode stays `explicit`** (D13, privacy by default): a site turns Cookiebot's mode on when it uses
  Cookiebot. The owner's messages name the category Cookiebot files the captcha under, not the mode. `implicit` still
  needs the host's `Privacy:AllowImplicitCaptchaConsent`; a site that wants consent before the check loads chooses
  `marketing` or the `explicit` mode.
- **Stored values:** no release has shipped the old choices, so nothing is migrated. A row that still holds
  `preferences` or `statistics` works on the page as before, and is refused only when it is saved again.
- **12.4a's screen** offers the switch with `necessary` first, and says that Cookiebot files the captcha there.

## Consequences

- With Cookiebot, the captcha is declared and loaded as a necessary cookie, as Cookiebot itself files it, unless the
  site switches it to marketing.
- `necessary` loads without the visitor's choice, as Cookiebot's own declaration says; the site's privacy notice names
  the provider, as for any captcha (§3.7).
- The visitor's words name only the marketing category.
