# ADR-0161: Webhooks, Slack, Teams and entry events

Status: accepted, 2026-11-02 (ROADMAP 8.4). Builds on ADR-0007 (the outbox), ADR-0008 (secrets), ADR-0014 (sensitive values), ADR-0076 and ADR-0082 (outbound calls).

## Context

An entry should be able to reach another system, signed, without the site becoming a way into its own network, and without a credential ending up in a form definition that is exported, copied or shown to editors.

## Decision

**One action type, with Slack and Teams as presets.** Both are a `POST` of a formatted body, so a second and third type would repeat everything. A preset is an ordinary starting value of the settings, which an editor can read and change.

**A call goes through `OutboundHttp`.** It uses the same allowed hosts, schemes and ports as option lists, and the transport refuses addresses that are not public after the name is resolved. A `POST` follows no redirect and reads no answer's body: a redirect would carry the signed body and its headers wherever the answer says.

**Secrets are references, in one place.** `$JironFormBuilder:Secrets:<Name>` names a value in `JironFormBuilder:Secrets`.

- The signing secret, a Slack or Teams address (which holds its own key) and a header whose name says it carries a credential accept only a reference, at publishing and in the integrations settings.
- A plain webhook address may be literal, as most are not secret and the host list needs to be read. A site whose address holds a key names it as a reference, as `docs/webhooks.md` says.
- A draft is not checked until it is published, so a test run refuses the same literals (`secret_literal`) rather than sending them.
  Amended by [ADR-0236](0236-what-the-security-review-changed.md): a save, restore, replace or import refuses them too, in an
  action it adds or changes, so no draft or History keeps a secret as itself.
- An address from a reference is checked when the call is made, under the same rules.
- `MissingConfiguration` names the key and never the value, so an import can say what a site lacks.

**The signature.** `X-Jfb-Signature: t=<unix seconds>, v1=<hex HMAC-SHA256 of "t.body">`. Signing the time lets a receiver refuse a replay, and each attempt signs its own time. `X-Jfb-Delivery` is the run's delivery key, stable across attempts, so a receiver can recognise a repeat. Samples in C#, Node and PHP are compiled or run by the unit tests. The PHP one runs where PHP is installed: on CI's runner, where its absence fails the test, and not on the development machine, where the test says so and a static test holds its constants.

**What each answer means.**

- `2xx` delivers.
- `408`, `425`, `429`, `5xx` and a failed connection try again, the transport's 10-second limit on connecting among them, as nothing was sent. A broken connection could in principle have delivered, but a host that is down is the usual reason, and the delivery key covers a repeat.
- Any other status, a `3xx` included, fails for good.
- No answer in 30 seconds is uncertain, as it may have arrived, and a person decides (ADR-0007).
- A refused address fails for good, as trying again changes nothing.
- A body that would be cut at the merge tags' limit of 100,000 characters fails for good (`too_long`), as a body cut short is invalid or says less than it seems to.

**What goes out.** Every value is a merge tag's text rendered for an action: a sensitive field only where the action names it, a hidden field never. "Include in emails" stays about mail, as a CRM often wants what a mail leaves out. Three shapes cover the uses:

- a mapping, which names exactly what goes;
- no mapping, which sends every field with an answer by its alias, leaving out an unnamed sensitive field, as its mask would say that it was answered;
- `withoutAnswers`, which says only that something happened.

An empty mapping cannot stand for the third: empty settings are not stored, and it would read as "everything", the wider of the two meanings. The third is therefore its own setting, and it refuses a mapping or a body of its own.

**Chat messages are markup.** JSON escaping keeps a Slack or Teams body sound, but the receiver reads the text inside as markup, so a visitor could call a channel or write a link that says what it likes. When the target is Slack or Teams, what a visitor typed is escaped for that chat before it is escaped for JSON, through the same place in the merge-tag service that breaks links in a mail to a stranger (ADR-0160).

**A webhook's templates hold no unknown tag.** A mail's reader sees a gap and tells someone. A tag that renders nothing in a JSON body can break it for a receiver nobody watches, so publishing refuses what a mail only warns of.

**Headers** take names a site may set: not the request's own, not `Content-*` (they would misdescribe the body), not `X-Jfb-*`, and each once. Values are printable ASCII, also after a reference is resolved, or the run fails with `header_invalid`, which names no value.

**Entry events through Umbraco's webhooks.** Five events are registered, and their notifications are public. The payload holds no answer, since Umbraco sends it unsigned and a site's subscription does not pass the action's checks.

- The integrations setting decides whether a webhook fires, through an internal flag on the notification. A site's own handlers hear every event either way.
- Publishing happens after the entry's unit and never fails the visitor, as the entry stands by then. Handlers hold the visitor up for 5 seconds at most, and run on after that.
- Umbraco's base class fires only on a single or scheduling server, which suits its own notifications, as every server hears those. An entry's change happens once, on whichever server took it, so these events give the base class a role that always fires. The request goes in the database, and Umbraco's job sends it from the scheduling server.
- An event is told at most once: a server that stops between the unit and the event loses it. The webhook action is the one that is part of the entry's unit and retried.
- This step publishes submitted and action failed. Approved, rejected and deleted are registered now and published where those changes are built (8.5 and 11.2 name them), a user's change once its undo window is over, as an event cannot be called back.
- `submitted` fires when an entry reaches that state, so an entry in Spam or waiting for its confirmation is told of when it leaves those.

## Consequences

- An action type's constructor can meet a refused host configuration, because the start-up check creates the types. That check now leaves the refusal to the configuration's own check, which names the setting once.
- A literal address with a key in its query cannot be told apart from any other address. `docs/webhooks.md` says to use a reference.
- The PHP sample has not been run on the development machine. `docs/OWNER-TODO.md` lists it under checking CI runs.
