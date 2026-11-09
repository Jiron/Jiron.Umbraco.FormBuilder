# 0224. Visitors' IP addresses: none by default, anonymised or full by choice

- Status: accepted; builds on [ADR-0008](0008-settings-model.md) (host rules and the restricted lock),
  [ADR-0009](0009-privacy-defaults.md) (what a submission stores, the health checks) and
  [ADR-0165](0165-the-section-shell.md) (the health probes)
- Date: 2026-11-09
- Roadmap step: 13.1, its part 13.3 (ADR-0197)

## Context

ADR-0009 decided that entries keep no IP address by default, that a site or a form may keep it anonymised (an IPv4 /24,
an IPv6 /48), and that a full address needs the host's `AllowFullIp`. The column was there from the start
(`jfbEntry.ipAddress`), the form's setting too (`settings.privacy.ip`, `inherit` by default), and the entry panel shows
an address where one is kept; nothing set it yet. ADR-0008 named `ip: full` as the first example of a restricted value:
refused while the host does not allow it, read as the nearest allowed one (`anonymised`), shown with its key, and named
by the form check.

## Decision

1. **Three modes.** `none` (the default), `anonymised` and `full`. The site chooses on Privacy › Data collection, in a box
   of its own before Insights; the privacy section is the same for every site (ADR-0008). A form chooses in its Settings
   view's Privacy group, `Site default (…)` naming the site's mode. The site cannot store `inherit`
   (`ipHandling`).
2. **Full addresses wait for the host.** `JironFormBuilder:Privacy:AllowFullIp` is off by default. While it is off, the
   site's `full` is refused and a stored one reads as `anonymised` (the restricted lock, its choice greyed with the key);
   a form's `full` is kept anonymised when its entries are stored, its choice is greyed in the Privacy group with the key
   in the help, and the form check warns with `lock_replaces_value` at `$.settings.privacy.ip`, as for a field's locked
   setting.
3. **What anonymised keeps.** An IPv4 address's first three bytes and an IPv6 address's first six, the rest zero:
   `203.0.113.42` becomes `203.0.113.0`, and `2001:db8:85a3:8d3:1319:8a2e:370:7348` becomes `2001:db8:85a3::`. An IPv4 address
   carried in IPv6 (`::ffff:203.0.113.42`) counts as IPv4, and no address keeps an IPv6 scope, which names the server's
   own interface. The entry stores the address in its usual text form.
4. **Which address.** The connection's remote address as ASP.NET Core gives it. Jiron Form Builder never reads
   `X-Forwarded-For` itself, which a visitor can write; a site behind a proxy sets up ASP.NET Core's forwarded headers,
   which the privacy documentation (13.4) says.
5. **Entries that keep none.** A preview's test entry (the editor's address), an entry sent with a server key (the site's
   own server's address) and a draft keep no address; the entry a draft becomes keeps its address when it is sent. Rate
   limits and blocked address ranges read the address in memory and never store it.
6. **Where a kept address goes.** The entry panel's More details, and the person's export (ADR-0223). Erasing or
   anonymising an entry clears it, and retention deletes it with its entry. Entry exports do not carry it.
7. **The privacy health check.** A probe of its own, on the Overview and Umbraco's Health Check dashboard, warns while
   the host allows something beyond the defaults and a setting uses it: full addresses (the site's, or the count of live
   forms with their own), a state's entries kept for good (`AllowRetentionOff`, ADR-0222) and a captcha that loads before
   visitors are asked (`AllowImplicitCaptchaConsent`, ADR-0157), each linking to the screen that changes it.

## Consequences

- A site that changes nothing keeps no address, as before; a change of mode applies to the entries sent after it, and
  nothing stored is rewritten.
- A site behind a proxy without forwarded headers keeps the proxy's address. The facts sheet says so.
- The settings screen's choices the host restricts share one editor: captcha consent's and IP handling's.
