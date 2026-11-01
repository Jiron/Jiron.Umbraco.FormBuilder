# 0126. Server-key submissions

- Status: accepted; builds the "Server keys" of [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md),
  adds the error code `server_key_invalid` to the public API's codes (§4.6) and reserves the context names that start
  with `jfb`
- Date: 2026-10-31; amended 2026-10-31 (6.8a: the keys read once; a receipt without a key)
- Roadmap step: 6.6b (decided and built); 6.8a (the keys read once, a receipt without a key); 7.1 and 7.2 make the challenge, the honeypot and the captcha skip a
  request a server key signs

## Context

A headless site may render a form in the visitor's browser and send the answers from its own server, which has no
browser challenge to solve, no captcha widget and no honeypot to leave empty. ADR-0012 decided the shape: such a
server sends `Authorization: Jfb-Key <key>`; the host configuration holds a name and the SHA-256 of each key, never
the key, compared in fixed time; a server-key submission skips the challenge, the captcha and the honeypot, never
validation, the content rules or the domain limits; it is limited per key; the entry records the key's name in its
context under the reserved name `jfbServerKey`; and the receipt endpoint accepts the key that submitted an entry
instead of its challenge. This step builds it. The challenge, the honeypot and the captcha come in 7.1 and 7.2.

## Decisions

- **Configuration:** `JironFormBuilder:PublicApi:ServerKeys` is a list of `{ "Name": "shop", "Sha256": "<64 hex
  digits>" }`. A name has 1 to 64 letters, digits, hyphens and underscores, and no two keys share a name or a hash;
  the host refuses to start otherwise (ADR-0042), naming the entry and never its hash. The keys are read once, as
  every host option is (ADR-0042): a changed or revoked key takes effect when the site restarts, and so passes the
  start-up checks first (6.8a; they were read on every request before, so a key changed while the site ran skipped
  them). `scripts/server-key.mjs`
  makes a key of 256 random bits (base64url) and prints its hash for the configuration; the key goes only to the
  headless server's own secret store.
- **The header:** the scheme `Jfb-Key` in any letter case, one space, then the key: 32 to 512 visible ASCII
  characters. The server hashes the key's UTF-8 bytes and compares the hash with every configured hash in fixed time
  (`CryptographicOperations.FixedTimeEquals`, never stopping at the first match), so the time it takes says nothing
  about which key or how much of one matched.
- **A key that matches none, or a malformed header with the scheme,** answers `401 server_key_invalid` on every
  public endpoint, with `WWW-Authenticate: Jfb-Key`; it is never treated as an anonymous request, so a server with a
  revoked key notices at once. A request without the scheme is an ordinary anonymous one.
- **What a key changes:** the request counts in its key's partition instead of its address's
  (`FormBuilderRateLimitPolicies.ClientOf`: `key:<name>` or `address:<ip>`), which the limiter of 7.1a applies with `Limits:ServerKeyFactor` times an address's limits ([ADR-0137](0137-the-rate-limiter.md))
  with the same policies; the challenge, the captcha and the honeypot will skip it (7.1, 7.2). The origin check stays
  as it is: a server sends no `Origin`, and a browser cannot send the header across origins, as the preflight allows
  only `Content-Type`. Validation, the content rules, the domain limits and every other check run as for any
  submission.
- **The entry:** a submission with a key stores the key's name in its context under `jfbServerKey`, beside the form's
  own context values, and the receipt endpoint answers for an entry made with that key's name when the same key
  asks; any other request, another key or none at all, meets the same `404 not_found` as an unknown submission
  (6.8a: a request without a key was answered before). An entry no key made answers only a request without one.
- **Reserved names:** a context value whose name starts with `jfb` in any letter case is the package's
  (`FormDeveloperSettings.ReservedContextPrefix`); publishing refuses one in `developer.contextValues`
  (`name_invalid`), and a page or a post that gives one is ignored, as an unlisted name is.
- **Never logged:** neither the key nor its hash is written to a log, a problem or an exception; a refused key is
  logged as "a server key that matches none", with the request's path.

## Consequences

- A headless server can submit without a browser, through the same validation as a visitor, and its entries say
  which key sent them.
- A leaked key is revoked by removing its entry from the configuration; nothing else holds it.
- 7.1 reads `ServerKeyRequest` to skip the challenge and the honeypot, and 7.2 to skip the captcha.
