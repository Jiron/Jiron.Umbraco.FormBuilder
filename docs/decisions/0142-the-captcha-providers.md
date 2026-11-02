# 0142. The captcha providers

- Status: accepted; builds on [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md),
  [ADR-0035](0035-structural-validation-and-public-projection.md) and
  [ADR-0057](0057-field-types-endpoint-and-availability.md); amends ADR-0012 on where a page reads the site key; the runtime's
  part waits with [ADR-0120](0120-sending-waits-for-the-runtimes-budget.md)
- Date: 2026-11-01
- Roadmap step: 7.2b

## Context

ADR-0012 set the outcomes: a token that fails answers `403 captcha_failed`, a provider that cannot answer sends the entry to
Spam, a server key and a post without JavaScript have no token to solve. It left open how a provider is shaped, what a site
configures, what a token is held to, when in the pipeline a token is spent, and what happens when a form's provider cannot be
shown at all.

## Decision

- **A provider is one small public extension point,** `IFormBuilderCaptchaProvider`: a name (`Name`, as `Alias` is a keyword
  in another .NET language), whether it has its keys, what a page needs to show it, and the verification. Sites add theirs through `builder.JironFormBuilder().CaptchaProviders()`, and
  the last registration of a name wins, so a site can replace a built-in one. The built-in ones (`recaptchaV2`, `recaptchaV3`,
  `hcaptcha`) share one internal base class, because their calls are the same; they call through the site's own transport
  (ADR-0076), which stays internal.
- **Keys live in host configuration only:** `JironFormBuilder:Captcha:RecaptchaV2|RecaptchaV3|Hcaptcha`, each with its
  `SiteKey` and `SecretKey`. One key without the other refuses start-up, so a half-set provider is not silently off.
  `VerifyAddress` points a provider at a test host's stub, and `UseRecaptchaNet` moves reCAPTCHA's script and verification to
  `recaptcha.net`.
- **Only the secret and the token go out,** never the visitor's address, which a provider does not need to answer. An answer
  passes when it says so, its host is one of the site's, its age is inside the provider's limit (five minutes, two for the
  scored v3, 30 seconds of skew both ways), and, for v3, its action is `submit` and its score reaches the form's threshold.
- **The site's hosts come from its domains and allowed origins,** never the request's `Host` header, which a caller chooses.
  A site with neither has nothing to compare, so the host is not checked there. This is the case on a fresh site and in a
  development host; the moment a domain is set, a token from anywhere else fails.
- **What counts as the provider's fault:** a timeout of 8 seconds, a status that is no success, an answer it cannot read, an
  answer over 64 KB, a failed call, and an error code that blames the site's own keys. No visitor can mend those, so they are
  `Unavailable`, not `Failed`, and the entry goes to Spam with `captcha_unavailable` (score 100), losing no lead and running
  no action. A site's own provider that throws counts the same and is logged.
- **A form's threshold is text,** `{"threshold":"0.7"}`, as settings write every decimal; anything else is the default 0.5.
- **A form whose provider cannot be shown goes to Spam without asking for a token.** A provider the site no longer has, or one
  that lost its keys, gives the challenge no `captcha` part, so the page shows none and its visitor cannot have a token.
  Refusing them with `captcha_failed` would lock every visitor out of a form over a change in the host's configuration.
  Publishing refuses a provider the site lacks (`unknown_type` at `$.settings.protection.captcha.provider`), but not one
  without keys: keys differ between environments, and a definition travels between them.
- **A token is spent only on answers that can be stored.** It is single use at the provider, so it is verified after the
  answers are read, and only when they hold no errors; a retry finds its entry first and spends none. The places left and
  unique answers are checked after it, because Spam takes neither (ADR-0090, ADR-0091), so a visitor who loses a race there
  needs a new token. The runtime asks for one on any refusal (7.2c).
- **A site may set the level:** `protection.level` may be `captcha` with `captchaProvider`, required there and refused
  elsewhere, and a form that inherits takes it. The field type's availability looks at all providers; it says "not configured"
  with `#jfbFieldTypeAvailability_captchaNoKeys` until one has its keys, and it is checked by what the designer reads.
- **The challenge carries** `captcha: {provider, siteKey, scriptAddress, action}`, not the projection as ADR-0012 said: the
  provider of a form that inherits depends on the page's site, which only the challenge knows. It holds the public key only,
  and the script's address is for the runtime to load after consent (section 4.10).

## Consequences

- The runtime's part, which loads the script after consent and posts the token, came with 7.2c (ADR-0157), with the consent modes of section 4.10.
- Greying out a provider without keys is for the settings screens, which do not exist yet: `IsConfigured` is what they read.
  The form's protection screen comes with them.
- A built-in provider is not a public type, so a site cannot derive from it; it writes its own against the interface, or asks
  for a seam when several need the same one.
- ADR-0012's health check of the providers comes with the health checks; until then an outage shows as `captcha_unavailable`
  on the entries in Spam.
- Real keys are checked by the owner only (`docs/OWNER-TODO.md`); these tests run against the stub.

## Alternatives considered

- **Asking `Failed` for a provider that lost its keys:** simple, and it locks visitors out. Rejected.
- **Sending the visitor's address:** both providers accept it as an option, and it is personal data they do not need.
  Rejected.
- **Verifying before the answers are read:** spends a single-use token on answers that come back with errors, so the visitor
  would solve it twice for a typo. Rejected.
- **Using the request's host when a site has no domains:** a caller chooses that header, so the check would prove nothing.
  Rejected.
