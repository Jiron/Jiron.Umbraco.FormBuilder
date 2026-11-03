# Webhooks

A form can tell another system about an entry in two ways:

- **The webhook action** sends one signed `POST` for each entry, with the answers you choose. Slack and Microsoft Teams are presets of it.
- **Umbraco's webhooks** (Settings, Webhooks) can subscribe to entry events. They carry no answers.

## Before the first call

Jiron Form Builder only calls hosts the site's configuration names, over `https`:

```json
{
  "JironFormBuilder": {
    "Webhooks": { "AllowedHosts": ["crm.example.com", "hooks.slack.com"] },
    "Secrets": {
      "CrmSigning": "put this in your secret store, not in the file",
      "SlackMessages": "https://hooks.slack.com/services/..."
    }
  }
}
```

An address that resolves to a private, loopback or link-local address is refused after the name is resolved, so a name cannot be pointed at the site's own network. A redirect is never followed, as it would carry the signed body to wherever the answer says.

**Secrets stay in the configuration.** A setting names one as `$JironFormBuilder:Secrets:<Name>`, and the value is read when the call is made. Name letters, digits and `_`, up to 64 characters. Publishing refuses a value in these settings, and a test run of a draft does not send one:

- the signing secret;
- the address of a Slack or Teams action, which holds its own key;
- a header whose name mentions `auth`, `cookie`, `key`, `password`, `secret` or `token`.

A plain address is allowed as it is. If yours holds a key, in its path or query, name it as a reference too.

A form whose secret is missing still publishes, and each run fails with `secret_missing`. An import names a missing secret the action names itself, but not the signing secret or address it takes from the integrations settings.

## The webhook action

| Setting | What it does |
| --- | --- |
| Where it goes | The action's own address, or the site's Slack or Teams address from the integrations settings |
| Address | An address, or a reference to one |
| Format | `json`, `form` (`application/x-www-form-urlencoded`) or `xml` |
| Field mapping | Names with templates, such as `{"email": "{{ field.email }}"}`. Without one, every field with an answer goes, by its alias |
| Send no answers | Only says that something happened; the receiver asks for what it may see |
| Send metadata too | Adds the event, delivery, form and entry; on by default |
| Own body | The whole body as a template, in the format's own language; each merge tag is escaped for it |
| Signing secret | A reference; without one, the integrations settings' secret signs, and with neither the request is unsigned |
| More headers | Names with texts or references. Headers of the request itself and `X-Jfb-*` are refused |
| Send sensitive fields | The sensitive fields it may send; none by default |

A field the rules hid is never sent. A sensitive field the action does not name goes as no field at all, and as a mask where a mapping or own body asks for it.

"Include in emails" is about mail: a field without it can still go to a webhook.

A field with several values goes as one text, its values joined by a comma.

A preset that goes to Slack or Teams keeps what a visitor typed as text, so `<!channel>` calls nobody and `[Open](https://...)` is no link.

### The body

With JSON, the default looks like this:

```json
{
  "event": "submitted",
  "delivery": "9f2c...",
  "form": { "alias": "contact", "name": "Contact us" },
  "entry": {
    "key": "62b87815-24ba-486e-89b5-000000000000",
    "number": "42",
    "state": "submitted",
    "submitted": "2026-11-02T09:00:00.0000000+00:00",
    "culture": "en"
  },
  "fields": { "name": "Ada", "message": "Hello" }
}
```

A form-encoded body uses the same names with dots (`form.alias`, `fields.name`). An XML body is a `submission` element with `meta` and `field` elements, each with a `name` attribute. Without metadata, a JSON or form-encoded body holds the fields alone.

### What comes of a call

| The host's answer | The run |
| --- | --- |
| `2xx` | Delivered |
| `408`, `425`, `429`, `5xx`, or no connection (10 seconds to connect) | Tried again later, as `http_<status>` or `no_connection` |
| Any other status, a redirect included | Failed for good, as `http_<status>` |
| No answer within 30 seconds | Uncertain, as `timed_out`: it may have arrived, so a person decides |
| An address the configuration refuses | Failed for good, as `not_allowed` |
| A body longer than 100,000 characters from one template | Failed for good, as `too_long`, as a body cut short is no body |
| A header from a secret that HTTP cannot carry | Failed for good, as `header_invalid`, which names no value |

`X-Jfb-Delivery` stays the same across the attempts of a run, so a receiver can recognise a repeat. A test run from the designer sends `X-Jfb-Test: true` and `X-Jfb-Delivery: test`.

The signature covers the body and the time, not these headers. Where you rely on the delivery key, keep the metadata on, so that it is in the signed body too.

## Checking the signature

Each signed request carries:

```text
X-Jfb-Signature: t=1793620800, v1=0c4dd8ef53dd6e64...
```

`t` is the time of the attempt in seconds since 1970, and `v1` is the lowercase hexadecimal HMAC-SHA256 of the time, a dot and the body, with the signing secret as its key.

To check it:

1. Read the body as bytes, before any parsing.
2. Work out the HMAC of `t`, `.` and those bytes.
3. Compare it with `v1` in constant time.
4. Refuse a `t` more than five minutes from now, so a recorded request cannot be sent again later.
5. Refuse a header that gives `t` or `v1` twice.

Working samples, each with the same tests behind it:

- [C#](samples/webhook-signature/Verify.cs)
- [Node](samples/webhook-signature/verify.mjs)
- [PHP](samples/webhook-signature/verify.php)

## Slack and Microsoft Teams

Choose the preset on a new webhook action, and name the address in **Integrations** as a reference (`$JironFormBuilder:Secrets:SlackMessages`), or on the action itself. Its host has to be in `AllowedHosts` too.

- **Slack** sends a `text` message for an incoming webhook.
- **Teams** sends an Adaptive Card for a workflow that receives webhooks.

Each preset is an ordinary own body that you can change, with its merge tags escaped for JSON and for the chat.

## Umbraco's webhook events

Jiron Form Builder registers these in Settings, Webhooks:

| Event | Alias | When |
| --- | --- | --- |
| Forms: entry submitted | `jfbEntrySubmitted` | An entry arrives and is not in Spam or waiting for its confirmation, or comes out of Spam |
| Forms: entry approved | `jfbEntryApproved` | An entry reaches approved; at once on a form without an approval step, and once its undo window is over for a user's change |
| Forms: entry rejected | `jfbEntryRejected` | An entry is rejected, by a rule at once and by a user once the undo window is over |
| Forms: entry deleted | `jfbEntryDeleted` | Registered; published once a user can delete an entry (ROADMAP 11.2) |
| Forms: action failed | `jfbActionFailed` | An attempt of an action failed for good, or ended unsure whether it arrived |

The payload never holds an answer, because Umbraco sends it as it is:

```json
{
  "form": { "alias": "contact" },
  "entry": { "key": "62b87815-24ba-486e-89b5-000000000000", "number": 42, "state": "submitted" },
  "occurredUtc": "2026-11-02T09:00:00+00:00"
}
```

`jfbActionFailed` adds `"action": { "type": "webhook", "code": "http_404" }`.

**Integrations** can turn these off, and your site's own `INotificationAsyncHandler<EntrySubmittedNotification>` hears them either way.

A handler's failure is logged and never fails the visitor, and a slow one holds the visitor up for 5 seconds at most. As with every Umbraco notification, one that throws can stop the handlers after it.

The request is queued by whichever server took the entry. Umbraco sends it from its scheduling server, under its own settings (`Umbraco:CMS:Webhook`) and its own retries, and Jiron Form Builder does not sign it.

These events tell of a change at most once: a server that stops between the entry's save and the event loses that event. Use the webhook action where each entry has to arrive, as it is part of the entry's own save and retried. On a form that stores no entries, the key names an entry that is gone moments later.
