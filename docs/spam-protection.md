# Spam protection

Jiron Form Builder keeps robots out without asking visitors to prove they are human, and without loading anything from
another company's servers. A captcha from reCAPTCHA or hCaptcha is there for the sites that want one, and loads only on
the forms that use it, only once the visitor agrees.

## Contents

- [How it works](#how-it-works)
- [Protection levels](#protection-levels)
- [Content checks](#content-checks)
- [Blocked address ranges](#blocked-address-ranges)
- [Captchas](#captchas)
- [Rate limits](#rate-limits)
- [Several sites](#several-sites)
- [For developers](#for-developers)

## How it works

Each entry gets a **spam score** from what it shows. At 100 or more it goes to **Spam**: it is stored, nothing is lost,
and no action runs for it until someone takes it out with **Not spam** ([entries-and-exports.md](entries-and-exports.md#spam)).
The visitor sees the usual thank-you message either way, so a robot learns nothing.

| Signal | Score |
| --- | --- |
| A hidden field only robots fill in was filled in | 100 |
| Sent less than 3 seconds after the visitor started | 60 |
| Sent from a blocked address range | 100 |
| An email address at a blocked domain | 100 |
| Each blocked word, counted once | 40 (80 when strict) |
| More links than the site allows | 40 (80 when strict) |
| Sent without JavaScript where the protection needs it | 100 |
| The captcha's provider couldn't check it | 100 |

So one signal on its own puts an entry in Spam only when it is certain; a quick visitor alone, or a few links alone,
does not.

An entry is **refused**, rather than scored, only for something a real visitor can fix or try again: a failed captcha,
or sending too often. The visitor keeps every answer.

**The signed check** behind every form is what makes this work without cookies: when a visitor starts filling in a
form, the page fetches a signed, single-use token from the site, which the entry must carry; a page without JavaScript
gets one written into the form as it renders. It is tied to the form and the time, not to the visitor's address, since
phones change networks. At the Standard level, a visitor without JavaScript is judged like any other.

## Protection levels

**Settings › Spam protection › Default protection** sets the level for a site's forms; a form can choose another in its
Settings view, under Spam protection.

- **Standard,** the default: the hidden field, the shortest time to fill it in, the signed check and the content checks.
  Nothing loads from other sites.
- **Standard, with a proof-of-work check:** the visitor's browser also solves a small puzzle before it sends, which
  costs robots time. It takes a phone a moment at the default strength; **Puzzle strength** (16 by default, 8 to 24)
  doubles the work with each step. It needs JavaScript, so an entry sent without it goes to Spam.
- **A captcha:** reCAPTCHA v2, reCAPTCHA v3 or hCaptcha, once the site has its keys ([below](#captchas)).

The content checks and blocked ranges apply at every level.

## Content checks

**Settings › Spam protection › Content checks** looks at what visitors typed: text answers, the parts of names and
addresses, and "Other" answers, never option values, files or numbers. A match never refuses an entry; it adds to its
score.

- **Blocked words:** words or phrases, in any letter case, that count towards Spam when they stand as a word of their
  own.
- **Blocked email domains:** an email address at one of them, or at a domain below it, sends the entry to Spam.
- **The most links an entry may hold:** 3 by default, counted over all answers. A link starts with `http://`,
  `https://`, `ftp://` or `www.`; a bare domain name isn't one.
- **Default strictness:** **Normal**, where three blocked words, or two with too many links, make Spam; **Strict**,
  where two blocked words, or one with too many links, do; or **Off**, which skips the content checks. A form can choose its own strictness in its Settings view.

With nothing set, no word or domain is blocked, so only the links count.

## Blocked address ranges

**Ranges** under Settings › Spam protection › Blocked address ranges take address ranges such as `203.0.113.0/24` or
IPv6 ranges. Entries sent from them go to Spam. The address is compared when the entry arrives and never stored.

Behind a proxy or a CDN, the site sees the proxy's address unless the developer sets up ASP.NET Core's forwarded
headers; then ranges, and the limits below, apply to the visitor's real address.

## Captchas

### Keys

A captcha provider can be chosen once its keys are in the site's configuration. They never go into the database or onto
a screen:

```json
{
  "JironFormBuilder": {
    "Captcha": {
      "RecaptchaV3": { "SiteKey": "…", "SecretKey": "…" }
    }
  }
}
```

The keys are `RecaptchaV2`, `RecaptchaV3` and `Hcaptcha`. Keep the secret key out of files you commit: in user secrets,
an environment variable such as `JironFormBuilder__Captcha__RecaptchaV3__SecretKey`, or your host's secret store. Only
the site key reaches the visitor's page. While a form's provider has no keys, its entries go to Spam.

`UseRecaptchaNet: true` loads and checks reCAPTCHA through `www.recaptcha.net`, for visitors who can't reach Google.

### Checking

The site sends only the visitor's token and the secret key to the provider, never the visitor's address. It checks
that the token is fresh, was made for this site, and, for reCAPTCHA v3, that its score reaches the form's **Lowest
score** (0.5 by default, from 0 to 1). A failed check lets the visitor try again. A provider that doesn't answer sends
the entry to Spam, so no entry is lost and none runs actions unchecked.

### Consent

A captcha's script comes from its provider's servers and may set cookies there, so it loads only on forms that use it,
and only as **Before reCAPTCHA or hCaptcha loads** in Settings › Spam protection allows:

| Choice | The script loads |
| --- | --- |
| **Ask on the form** (the default) | when the visitor chooses **Load the check** on the form, or ticks a consent field that loads it |
| **Follow a Cookiebot category** | once the visitor accepts the category you choose: Necessary, the default, where Cookiebot files a captcha's cookie, so it loads at once; or Marketing |
| **Follow the site's consent manager** | once the site's consent manager calls `JironFormBuilder.consent.grant('captcha')` |
| **Load without asking** | with the form, for sites that have consent another way; only with `JironFormBuilder:Privacy:AllowImplicitCaptchaConsent` in the configuration |

The consent lasts as long as the page is open; nothing is stored. If the script can't load, or the visitor doesn't
agree, the form can still be sent, and the entry goes to Spam for someone to look at.

[privacy.md](privacy.md) lists exactly what each provider loads.

## Rate limits

Every server limits how often one address may read, start and send forms. Over a limit, the visitor is told when they
can send again and keeps their answers. The defaults suit most sites; for sending, an address may send 20 times a
minute and 100 times an hour, 5 times a minute to one form, and the whole site takes 50 entries a second. A developer can
change each limit in `JironFormBuilder:Limits` ([configuration.md](configuration.md#limits)).

The limits count on each server, so on a site with several servers an address can get a little more through.

## Several sites

A form shown on a page uses the spam protection of that page's site. A form sent without a page, such as from a
headless site or a view outside Umbraco's content, uses the strictest settings of all sites together. The address a
request came in on never chooses the protection.

## For developers

- **Server keys:** a headless site's server can send entries with a key of its own, which skips the hidden field, the
  time, the signed check and the captcha, but never validation, content checks or blocked ranges
  ([headless.md](headless.md)).
- **Spam checks of your own,** such as a call to a spam-filtering service, and **captcha providers of your own** can be
  added in code ([extending.md](extending.md)). A check that fails or takes longer than 5 seconds counts for nothing.
