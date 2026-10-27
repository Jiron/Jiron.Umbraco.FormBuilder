# 0082. Ports of calls to other hosts

- Status: accepted; amends [ADR-0076](0076-url-and-file-lists.md) (what `JironFormBuilder:Webhooks:AllowedHosts`
  holds, and the port a call may use)
- Date: 2026-10-27
- Roadmap step: 5.5g (decided and built); every later call to another host (webhooks in Phase 8) goes through it

## Context

`JironFormBuilder:Webhooks:AllowedHosts` names the hosts the site may call (ADR-0076), and the transport refuses any
address that is not globally reachable. An allowed host could be called on any port, so an editor who may configure a
URL list could make every server send requests to any port of an allowed host: its mail or database port, say, which
the host's owner never meant to open to web requests from the site. The Phase 5 review found that the host rule is the
only one an editor's address meets before the call, and that a redirect may change the port too.

## Options

1. **Keep any port.** Simple, but the host rule then trusts an editor's choice of port, which nothing else checks.
2. **A fixed list of web ports** (443, 80, 8443, 8080). Arbitrary: a site's own service may answer on another, and a
   listed port may be the very one a host does not mean to open.
3. **The scheme's standard port, unless the host's entry names another.** A host alone is called on 443 (or 80 for
   `http`); `events.example.com:8443` allows that port as well. The person who configures the site decides, as for the
   hosts themselves. Chosen.

## Decision

- An entry of `JironFormBuilder:Webhooks:AllowedHosts` is a host, as before, or a host and a port after a colon:
  `events.example.com:8443`, `203.0.113.5:8443`, `[2001:db8::1]:8443` (an IPv6 address with a port in brackets, as an
  address writes it). The port is a number from 1 to 65535. Brackets hold an IPv6 address only, so
  `[events.example.com]` is no host, and an IPv6 address without them is a host alone, its colons its own
  (`2001:db8::1:8443` is an address).
- A host alone allows its scheme's standard port only. A host with a port allows that port. Several entries may name
  one host, each with another port or none; the same host and port twice is refused at start-up, naming both entries.
- Every call checks the port with the host, before the request and at every redirect, so a redirect to another port of
  the same host is refused as one to a port that is not allowed (`PortNotAllowed`). A URL list's check of the site says
  which port the address names and how to allow it.
- Start-up refuses an entry that is no host, or a host with a port that is no number from 1 to 65535.

## Consequences

- An editor can reach an allowed host only where the site's configuration says, port included.
- A site that calls a service on a port of its own adds the port to the entry, once, at the upgrade; the start-up
  check and the URL list's check say what to write.
- The integration tests name their stub with its port (`127.0.0.1:<port>`), as a site with a service of its own would.
