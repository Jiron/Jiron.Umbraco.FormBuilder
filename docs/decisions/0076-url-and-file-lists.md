# 0076. URL and file lists, and reaching other hosts

- Status: accepted; amends [ADR-0073](0073-option-lists-and-sources.md) (how each kind keeps its options) and
  [ADR-0075](0075-content-and-data-type-lists.md) (a URL list keeps what it fetches for 60 seconds at least);
  amended in 5.5g by [ADR-0081](0081-what-visitors-and-failures-cost-the-option-lists.md) (a URL list that fails with no copy; a body that breaks off or
  does not decompress) and [ADR-0082](0082-ports-of-calls-to-other-hosts.md) (an allowed host's port)
- Date: 2026-10-27
- Roadmap step: 5.5c (decided and built); 7.2 sends captcha verification and 8.4 webhooks the same way, 12.1 builds
  the editors, the upload and "Fetch now", 16.x reviews the guard

## Context

5.5c brings the last two kinds of option list: options fetched as JSON from a web address, and options read from an
uploaded CSV or TXT file. Section 4.10 sets the rules for the first: `https` only (`http` only when the host allows
it), hosts only from `JironFormBuilder:Webhooks:AllowedHosts`, private, loopback and link-local addresses refused
after DNS resolution, no redirects to other hosts, timeouts and caps on the answer, and an `IFormBuilderHttpTransport`
through which only the Testing assembly reaches the stub on `127.0.0.1:5560`. This is the first time Jiron Form
Builder calls another host, and captcha verification (7.2) and webhooks (8.4) will too. Open were:

1. **Where the guard sits.** A check of the address's name before the request can be fooled: the name may resolve to
   another address when the connection is made (DNS rebinding), a system proxy resolves names itself, and a redirect
   leads somewhere else.
2. **The URL kind's configuration:** where the list is in the answer, and which value of each element becomes an
   option's value and its label.
3. **Keeping fetched options.** ADR-0073 keeps a code list's options per culture, page and parent answers for the
   list's `cacheSeconds`; a field's own source has no time at all. A fetch does not depend on the culture or the page,
   it goes to someone else's server, and section 4.10 wants the last copy in use while the address does not answer.
4. **The upload:** the files editors have (spreadsheet exports in several dialects, hand-written text files), and what
   a broken line is.

## Options

1. **The guard.**
   - Resolve the name, check the addresses, then send the request by name: the connection resolves again and may
     reach an address nobody checked.
   - **Resolve, check and connect in one place:** the transport's connect step resolves the name itself, refuses the
     request when any address is not public, and connects only to the addresses it checked; no proxy, no automatic
     redirects. Chosen.
2. **Which addresses count as public.**
   - Only the three kinds section 4.10 names (private, loopback, link-local).
   - **Every range that is not globally reachable,** as the IANA special-purpose registries list them, with IPv6
     addresses that carry an IPv4 address checked by that address. Chosen: shared address space, documentation and
     benchmark ranges, multicast and reserved addresses lead into a network as surely as `10.0.0.0/8`.
3. **Mapping the answer.**
   - The answer must already be a list of `{ "value", "label" }`: most services an editor wants to use answer in their
     own shape.
   - **Three paths in a small path syntax of Jiron Form Builder's own:** where the list is in the answer, and where an
     element's value and label are inside the element. Chosen.
4. **Keeping fetched options.**
   - The option service's keeping of ADR-0073: one copy per culture, page and parent answers of the same answer, and
     none for a field's source, which would fetch on every page view.
   - **The kind keeps one copy per configuration and server,** for the list's `cacheSeconds` or, for a field's source,
     a fixed time, fetches once at a time, and uses the last copy while a fetch fails. Chosen.
5. **Lines of an upload.**
   - CSV by RFC 4180, records over several lines included: no value or label may hold a line break anyway.
   - **One option per line, in two dialects with the same quoting:** CSV with `,` or `;`, and TXT with `|`. Chosen.

## Decision

- **Reaching another host** (`OutboundHttp`, over the transport):
  - the address is absolute, without a user name or password, `https` (or `http` when
    `JironFormBuilder:Webhooks:AllowHttp` is on), and its host is one of `JironFormBuilder:Webhooks:AllowedHosts`
    (host names, international names in their ASCII form, or IP addresses; any letter case; checked when the site
    starts, no wildcards);
  - **the transport** (`IFormBuilderHttpTransport`, internal) resolves the name itself and connects only to addresses it
    checked; a name with any address that is not public is refused before a connection is made. It uses no proxy, no
    cookies and no automatic redirects, and it re-resolves names every two minutes;
  - **not public:** IPv4 `0.0.0.0/8`, `10.0.0.0/8`, `100.64.0.0/10`, `127.0.0.0/8`, `169.254.0.0/16`, `172.16.0.0/12`,
    `192.0.0.0/24`, `192.0.2.0/24`, `192.88.99.0/24`, `192.168.0.0/16`, `198.18.0.0/15`, `198.51.100.0/24`,
    `203.0.113.0/24`, `224.0.0.0/4` and `240.0.0.0/4`; IPv6 outside the global unicast range `2000::/3`, and
    within it `2001::/23`, `2001:db8::/32` and `3fff::/20`. `::ffff:0:0/96`, `64:ff9b::/96` and `2002::/16` count by
    the IPv4 address they carry;
  - **redirects:** at most 3, each to the same host and checked again as the first address was; another host, or
    `http` after `https` without `AllowHttp`, is refused;
  - **limits:** one time limit for the whole exchange, redirects and body included, and a cap on the answer's bytes
    after decompression; a declared length over the cap is refused before the body is read. Only a `2xx` answer counts;
  - a failure is a `FormBuilderOutboundException` with its reason (and the status for a refused answer). Logs name the
    reason and the status, never the address, which may carry a token in its query, nor the addresses it resolved to;
  - **tests:** the Testing assembly's transport reaches `127.0.0.1` at the stub's port only, and only in Development
    (`JironFormBuilderTesting:StubPort`); a test host without it refuses loopback, which a test proves against the
    running stub.
- **The URL kind** (`url`, `UrlOptionListSettings`, on a field too):
  - `url` (required, at most 2,048 characters), `listPath` (`$` by default), `valueFrom` (`$.value`), `labelFrom`
    (`$.label`);
  - **paths:** `$`, then any of `.name` (letters, digits, `_` and `-`), `['name']` (any name, `\'` and `\\` for a
    quote and a backslash) and `[index]` (`0` to `9999999`), at most 256 characters. `listPath` starts at the answer,
    `valueFrom` and `labelFrom` at each element of the list, so `$` alone is the element itself (a list of words);
  - a value or a label is a JSON string as it is, a number as the answer writes it, or `true` or `false`; anything else
    gives none, so an element without a value is left out (counted, ADR-0073), and an option without a label shows its
    value;
  - **checks:** the kind's own check, on every read, holds the address's shape and the paths' syntax; the check of
    what it names (ADR-0075), when a list is saved and a form is published, holds the scheme and the host to the host
    configuration, which the fetch holds it to again;
  - **a fetch:** `GET` with `Accept: application/json`, 4 seconds in all (below a source's 5), at most 2 MiB, read as
    JSON whatever its content type says (a byte order mark skipped, 64 levels deep at most); an answer that is no JSON,
    or whose `listPath` is no list, fails the fetch.
- **Keeping** (`OptionKind.Keeping`, which replaces `UsesCacheSeconds`):
  - `never`: manual, file and countries, which cost nothing to read;
  - `untilSiteChanges`: content and data type (ADR-0075);
  - `byRequest`: code, kept by the option service per culture, page and parent answers for the list's `cacheSeconds`
    (ADR-0073);
  - `byConfiguration`: URL, kept by the kind, one copy per configuration and server, for the list's `cacheSeconds`
    (60 seconds to 30 days; any other time is refused at `$.cacheSeconds`) or, for a field's source, 10 minutes. One
    fetch per configuration and server runs at a time; while a fetch fails, the last copy is used for another minute
    and a warning is logged; without a last copy the source fails. The copy lives in the runtime cache while it is
    used, so a restart starts without one.
- **The file kind** (`file`, no configuration, its options its items as a manual list's, never on a field, ADR-0073):
  `OptionFileReader` reads an upload into items, refused as a whole at `$.file` with the line in each message:
  - CSV (`.csv`) or TXT (`.txt`), UTF-8, at most 4 MiB; a byte order mark is skipped, any other encoding refused;
  - one option per line, `value` or `value` and `label`; CSV separates them with `,` or `;` (whichever comes first
    outside quotes on the first line), TXT with `|`;
  - a part may be quoted (`"..."`, `""` for a quote) and then holds the separator; spaces around a part are left out;
    a quote left open is refused;
  - an empty line is skipped; a first line that reads `value` and `label` is a header and skipped;
  - each value is cleaned as answers are and held to the option checks (length, a label on one line), a value that
    repeats an earlier one once letter case is ignored is refused with both lines, more than two parts are refused,
    and so is a file of more than 10,000 options or one with none.

## Consequences

- Captcha verification (7.2) and webhooks (8.4) send through `OutboundHttp`, with their own time limits and caps; a
  webhook's `POST` decides its own redirect rule.
- 12.1 builds the URL list's editor (the address, the three paths, the keep time with the last fetch and "Fetch now",
  which needs a cache instruction so every server forgets its copy), the upload into a file list or a field's own
  options through `OptionFileReader`, and the `#` keys of the new settings.
- ADR-0073's `UsesCacheSeconds` is `Keeping` now, and the option service keeps only `byRequest` kinds.
- **Revisit** if a site must reach the internet through a proxy (the guard would then have to trust the proxy's own
  rules), if a URL list needs a secret header (`$JironFormBuilder:Secrets:<Name>` as webhooks get in 8.4), or if the
  last copy should survive a restart.
