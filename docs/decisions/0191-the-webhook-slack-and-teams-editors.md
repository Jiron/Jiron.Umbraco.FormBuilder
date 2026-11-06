# 0191. The webhook, Slack and Teams editors: where it goes, what it sends, and a message for chats

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.2d; builds on [ADR-0161](0161-webhooks-slack-teams-and-entry-events.md) (webhooks, Slack and Teams,
  amended here), [ADR-0188](0188-the-actions-view.md) (an action's sidebar), [ADR-0189](0189-the-email-editor.md)
  (settings drawn within another's box) and [ADR-0190](0190-test-an-action-as-the-sidebar-holds-it.md) (Test)

## Context

The webhook action's sidebar (10.2a) draws its settings with the inspector's editors, which have none for two of them,
so they are not drawn at all: the field mapping (`fields`, names with templates) and the headers (`headers`, names with
texts or references). What the look in Edge and the survey found:

1. **What a webhook sends is five settings** that only make sense together: the format, the field mapping, an own body
   (`template`), "Send no answers" and "Send metadata too". The check refuses some of their mixes (no answers with a
   mapping or a body), the body takes the template over the mapping, and the metadata goes with the answers only; the
   sidebar draws them as five loose settings, the own body in a one-line box.
2. **Slack and Teams are presets of the webhook,** each an own body in JSON (ADR-0161): to change the words of a chat
   message, an editor edits JSON, escapes included. Their format, mapping and switches mean nothing for a chat.
3. **Where it goes** is a choice (its own address, the site's Slack address, the site's Teams address) and an address,
   which means another thing for each: an https address on an allowed host, or a reference for a chat's own address,
   whose site address comes from Settings › Integrations (12.4a builds that screen). Nothing says so.
4. **The signing secret and the headers** take references to the host's secrets, never the secret itself, which the
   check enforces and nothing explains; the signature (`X-Jfb-Signature`) is explained only in `docs/webhooks.md`.
5. **A webhook's test** answers with its run's codes under an English sentence (10.2c's review), and a chat's test
   message is marked only by a header the chat never shows.

## Options

For how the settings are drawn:

1. **Each setting by its own editor,** with the help in descriptions and the irrelevant ones left visible.
2. **Settings within two boxes,** as the email's To box draws its recipients (ADR-0189): **Where it goes** (the target
   and the address) and **What it sends** (the format, the answers, the body, the message and the metadata), each box
   drawing what the target and the choice make relevant.

For Slack and Teams:

1. **Keep the presets as own bodies** in JSON.
2. **A message:** plain text with merge tags, which the server writes as the chat's JSON; an own body stays for whoever
   wants another message shape.

## Decision

**Option 2 in each.**

- **Within boxes.** The schema's `within` (ADR-0189) also holds for two webhook editors: `webhookAddress` draws a text
  within it (the address within the target), and `webhookBody` draws a choice, two texts and two booleans within it
  (the format, the own body, the message, "Send no answers" and "Send metadata too" within the field mapping). The
  reader refuses any other setting within them, as it does for `recipients`; the editors find their settings by name, as
  they are the webhook type's own.
- **Where it goes:** the target's select, then the address, its help saying what it takes for the target chosen: an
  https address on a host the site allows (`JironFormBuilder:Webhooks:AllowedHosts`) or a reference where the address is
  a secret; for Slack or Teams, empty for the site's address from Settings › Integrations, or a reference to this
  action's own, as a chat's address holds its key.
- **What it sends,** for its own address: the format, then one choice of what goes, each with what it means: **Every
  answer, by its field's alias** (the default), **The names and values you choose** (a table of names and values, each
  value a text with merge tags; choosing it fills the table with every field, so it starts from what was sent), **A body
  you write** (a code box with Insert merge tag, in the format's own language) and **Only that something happened**
  (no answers). **Send metadata too** follows where it applies (not with a body you write, which it does not change).
  For Slack or Teams: **A message** (a text box with Insert merge tag, in the chat's own formatting) or **A body you
  write** (JSON); the format, the answers and the metadata do not apply and are not shown. The draft holds only the
  chosen way's values, so what it says is what the server sends; the sidebar keeps the others while it is open, so
  choosing back brings them back, and another target keeps its address and What it sends the same way. A body you write
  starts from the table's rows (or every field) in the format's own language, a chat's from its message as the chat
  reads it; a body cleared to nothing is said under its box and not kept, as no body is no way of sending. **Only that
  something happened** sends the metadata, which then is what says what happened, so its switch is not shown.
- **The table's values** take merge tags through one Insert merge tag under the table, which puts the tag into the value
  last in focus (the last row's before any): a picker on every row would repeat one button down the table (section
  4.13), and one that shows only on the row in focus would move the rows under it each time the focus moves.
- **The message** (`message`, ADR-0161 amended): for Slack, `{"text": …}`; for Teams, an Adaptive Card with the message
  as one wrapping text block. Each merge tag's value is kept as text for the chat, as the presets' bodies were. An own
  body still wins. Without either, a chat sends "New entry in {{ form.name }}" and the answers. The presets set a
  message instead of a body; drafts and versions that hold the old bodies keep working, as an own body.
- **Headers:** rows of a name and a value, Add header, each row's remove button; a name the request may not carry, a
  name twice, or a credential (a name with auth, key, token, secret, password or cookie) given as itself is said under
  its row as it is typed, as the check says it, and such a row is not kept: a credential typed as itself never reaches
  the draft, which is saved. Its help says that a credential is a reference such as
  `$JironFormBuilder:Secrets:CrmToken`. The table's rows follow the server's rules for a mapping's names the same way.
  The rules are the server's (`WebhookMaps`, `FormBuilderSecrets`) written again in the client, which the server's
  check still holds to.
- **The signing secret's help** says what it signs and how: the `X-Jfb-Signature` header, the time and an HMAC-SHA256
  of the time and the body, and that it takes a reference; empty uses the site's, and with neither a request goes
  unsigned. How a receiver checks it stays in `docs/webhooks.md`, which the help names until the documentation's Learn
  more links come (17.2a). Its editor is `secretReference`, which keeps only a reference, as a chat's address box does
  for a chat's address.
- **Test** (ADR-0190): a webhook's test answers with its run's code and, for a host that answered, the status, in a new
  `args` of the answer; the backoffice says each code in its words (`sent` with the status, `http_<status>`,
  `no_address`, `not_allowed`, `secret_missing`, `secret_literal`, `header_invalid`, `timed_out`, `no_connection`,
  `too_long`), `secret_missing` with the configuration key the site lacks, which is no secret itself, in its `args`
  and in the run's message for the deliveries log. The codes are said by the action's type, as an email's
  `no_address` means another thing. A chat's test message starts with a line, in the tester's backoffice language,
  saying it is a test, as a test mail does; an own body is sent as it is, marked by its header only.
- **The card's line** names an address that is a reference by its name ("Webhook to the address CrmHook"), never as a
  host it is not.
- **Fix it** lands on the control a problem names: the target, the address, the format, the choice, the table, the
  body, the message, a switch, the headers' box or the secret.
- **Who may only read** the action sees each box in words: where it goes, and what it sends (the names, the message,
  or that it writes its own body), never a secret's value, which is only ever a reference.

## Consequences

- `FieldSettingsReader` keeps a table of the editors that draw settings within them and what each draws; 10.2e may add
  the content action's.
- `WebhookActionSettings` gains `Message`; `WebhookBody` writes a chat's message; the presets change; `docs/webhooks.md`
  says so, and ADR-0161's "each preset is an ordinary own body" now holds for bodies written before.
- `ActionTestResult` gains `Args` and the test answer `args`, which the OpenAPI snapshot shows.
- A body's preview with a sample entry, as the email has, was weighed and left out: the table shows a body's names and
  values row by row and Test sends the real one, marked; a preview may follow where editors need more.
