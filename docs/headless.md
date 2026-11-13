# Headless sites

How a site whose pages are not rendered by Umbraco's Razor views shows and sends forms: through the public API, through
the `<jfb-form>` web component, and with the form picker in Umbraco's Content Delivery API.

## Contents

- [Two ways](#two-ways)
- [Before you start](#before-you-start)
- [The public API](#the-public-api)
- [Sending an entry](#sending-an-entry)
- [From a server](#from-a-server)
- [Errors](#errors)
- [The web component](#the-web-component)
- [The Content Delivery API](#the-content-delivery-api)
- [Types and the OpenAPI document](#types-and-the-openapi-document)

## Two ways

- **The `<jfb-form>` web component** renders a form on any page, with the same markup, themes, validation and
  behaviour as a Razor page, from one script tag. This is the quickest way.
- **The public API** gives the form as JSON and takes entries, for a front end that draws forms with its own
  components, or a server that sends entries.

## Before you start

- **Turn on Headless access** for each form a headless site reads: the form's Settings view › Developer. Without it, the
  form's definition and the web component answer as if the form didn't exist, and the Delivery API gives `null` for a
  form picker's value that names it.
- **Allow the front end's origin,** when its pages run in the browser on another host than Umbraco:

  ```json
  {
    "JironFormBuilder": {
      "PublicApi": { "AllowedOrigins": [ "https://www.example.com" ] }
    }
  }
  ```

  An origin is a scheme, host and optional port, nothing after. The site's own pages are always allowed. Requests from a
  server carry no origin and need no entry.
- **The address:** the API lives under `/jfb/api/v1`. A developer can move it with `JironFormBuilder:PublicApi:BasePath`
  (`/jfb/api` by default), never under `/umbraco`.

A front end on another origin calls the API without cookies or credentials, so members-only forms work only on the
site's own pages, where the browser sends the signed-in member's cookie along.

## The public API

All paths are below `/jfb/api/v1`. `{key}` is a form's key as a GUID with hyphens; the definition also takes the
form's alias.

| Request | What it does |
| --- | --- |
| `GET forms/{keyOrAlias}?culture=&contentKey=` | The published form, to draw it: its pages, groups, fields, rules, checks and texts in the culture, its `revision` and its consent wordings. Cached with an `ETag`. Needs Headless access. |
| `GET forms/{key}/options/{fieldKey}?parent=` | A field's options for its parent's answer, for cascading lists. |
| `POST forms/{key}/challenge` | The signed check an entry must carry, plus places left, the captcha and the puzzle where the form uses them. |
| `POST forms/{key}/uploads?fieldKey=` | One file, before sending, as `multipart/form-data`; answers a token for the field's value. |
| `POST forms/{key}/validate` | Checks one page's answers on the server: `204`, or `422` with the problems. Stores nothing. |
| `POST forms/{key}/entries` | Sends an entry; answers its receipt and the outcome to show. |
| `POST forms/{key}/receipt` | The receipt of an entry whose answer was lost, by its submission id. |
| `GET forms/{key}/element` | What the web component renders. Needs Headless access. |
| `GET forms/{key}/render?instance=&page=` | The form's markup at one place on a page, which the runtime asks for when a cached page's form was published again. Not a general markup endpoint. |
| `POST forms/{key}/events` | The Insights counter the runtime reports to; holds no answer. |

`contentKey` names the Umbraco page the form is shown on, if any: that page's site decides the settings, texts and
merge tags. `culture` is one of the site's languages, such as `de-DE`; otherwise the default language is used.

The API also has endpoints for saving a form to finish later (`POST forms/{key}/drafts` and the resume link); the
feature is not yet available on the website or in the backoffice, so leave them aside for now.

## Sending an entry

1. **Read the form:** `GET /jfb/api/v1/forms/contact?culture=en-US`. Keep `form.key`, `revision` and `consents`.
2. **Get the check** when the visitor starts: `POST /jfb/api/v1/forms/{key}/challenge` with
   `{ "contentKey": "…" }` or an empty body. It holds for 24 hours and for one entry. An entry sent less than
   `minFillSeconds` after the first check counts towards Spam.
3. **Upload files** one by one: `POST /jfb/api/v1/forms/{key}/uploads?fieldKey=…`, with a `challenge` part first, then
   the `file` part. Put the returned `token` into the field's value.
4. **Check a page,** optionally: `POST …/validate` with the answers so far and the page's key.
5. **Send:** `POST /jfb/api/v1/forms/{key}/entries`:

   ```json
   {
     "submissionId": "6f1c2b9e-8d55-4a8e-9a43-1d3f0b7c2e11",
     "revision": 3,
     "culture": "en-US",
     "challenge": "…",
     "values": { "name": "Ada Lovelace", "email": "ada@example.com", "topics": ["events", "news"] },
     "consents": { "privacy": "…the hash from the definition's consents…" }
   }
   ```

   The answer is `{ "receipt": { "submissionId", "reference", "submittedUtc" }, "outcome": { "kind": "message", "html": "…" } }`,
   or a redirect with its `url`.

**What the protection asks of a front end of your own,** besides the `challenge`:

- **Proof of work,** when the challenge's answer has `proofOfWork: { "nonce", "bits" }`: find a whole number `n` such
  that the SHA-256 of the ASCII text `nonce:n` starts with `bits` zero bits, and send `n` in digits as `proofOfWork`.
- **A captcha,** when it has `captcha`: load the provider's script from `scriptAddress`, show its check with `siteKey`
  (and `action` for reCAPTCHA v3, or `global`, the object a site's own provider's script defines), and send the token
  it gives as `captchaToken`. `consent` and `category` say when the
  site's settings allow the script to load ([spam-protection.md](spam-protection.md#consent)).
- **The honeypot:** leave `honeypot` out, or send what a hidden field of your own holds; a person never fills it in, so
  any text counts towards Spam.

Without a solved puzzle the entry is refused with `401 challenge_required`, and without a token that passes with
`403 captcha_failed`; a filled honeypot sends it to Spam without a word. A server key needs none of these.

Answers go by field alias: text, a number, `true` or `false`, or a list of texts; a name's or an address's part as
`alias.part`; a grid's row as `alias.<row key>`; a repeatable group as a list of row objects; a file field as its upload
tokens. Consents go by alias, with the wording's hash the definition gives by field key.

**Retrying is safe:** send the same `submissionId` with the same answers again, and you get the same answer, never a
second entry. If an answer was lost on the way, `POST …/receipt` with the submission id and the check gets it again.

**When the form changed** since it was read, the answer is `409 form_changed`: read the form again, keep what the visitor
typed, and ask them to check it.

## From a server

A server, such as a shop sending its orders as entries, uses a **server key** instead of the check, the hidden field and
the captcha. Validation, content checks and blocked ranges still apply.

Make a key of at least 32 random characters, and give the site its SHA-256, never the key itself:

```json
{
  "JironFormBuilder": {
    "PublicApi": {
      "ServerKeys": [ { "Name": "shop", "Sha256": "…64 hexadecimal digits of the key's SHA-256…" } ]
    }
  }
}
```

The repository's `node scripts/server-key.mjs shop` makes a key and prints the entry. Then send with the key:

```http
POST /jfb/api/v1/forms/{key}/entries
Authorization: Jfb-Key <the key>
Content-Type: application/json

{"revision":1,"values":{"name":"Ada"},"context":{"campaign":"autumn"}}
```

Keys are read when the site starts, so a new or revoked key takes effect at the next start. The entry records the key's
name. A server key gets ten times an address's rate limits, and never reaches a members-only form. Browsers can't send
one across origins, by design.

## Errors

Refusals are `application/problem+json` with a stable `code`:

```json
{ "type": "about:blank", "title": "…", "status": 422, "code": "validation_failed",
  "errors": [ { "code": "required", "path": "email", "message": "…" } ] }
```

For `validation_failed`, each error names the field by its alias path and the kind of problem, such as `required`,
`maxLength` or `fileType`. Show the form's own message for it from the definition, not `message`, which is a line for
developers.

| Status | Code | Means |
| --- | --- | --- |
| 400 | `invalid_request` | the body can't be read |
| 401 | `challenge_required` | the check is missing, used or too old: get a new one and send again |
| 401 | `server_key_invalid` | the server key matches none |
| 403 | `origin_denied` | the origin isn't allowed |
| 403 | `form_closed`, `entry_limit_reached` | the form takes no entries now |
| 403 | `member_required` | the form is for members |
| 403 | `captcha_failed` | the captcha didn't pass: the visitor tries again |
| 404 | `form_not_found` | no such published form, or not open to headless sites |
| 404 | `not_found` | nothing there, such as a receipt for no entry the check or key made |
| 409 | `form_changed` | the form was published again: read it again |
| 409 | `duplicate_submission` | the submission id was used for other answers |
| 409 | `already_submitted` | the member has sent this form already |
| 413 | `payload_too_large` | the body or the file is too large |
| 415 | `unsupported_media_type` | not JSON, or a file type the field refuses |
| 422 | `validation_failed` | answers that fail their checks |
| 429 | `rate_limited` | too many requests: wait the seconds `Retry-After` says |
| 500 | `server_error` | something failed on the server, which logged it |
| 503 | `migrations_pending`, `uploads_full` | try again later, as `Retry-After` says |
| 504 | `timeout` | the request ran past its time limit: send it again |

The rate limits are those of the site's spam protection ([spam-protection.md](spam-protection.md#rate-limits)).

## The web component

```html
<script type="module" src="https://cms.example.com/jfb/assets/element/jfb-element.js"></script>

<jfb-form api="https://cms.example.com/jfb/api/v1" form="1d0c7a8e-…" culture="en-US"></jfb-form>
```

| Attribute | What it does |
| --- | --- |
| `api` | The public API's address, with `/v1`; absolute when the page is on another host. Required. |
| `form` | The form's key. Required. |
| `culture` | The page's language. |
| `content` | The key of the Umbraco page the form belongs to, for its site's settings and merge tags. |
| `theme` | A theme for this placement. |
| `context-<name>` | Context values, for names the form lists. |

The element adds the form's stylesheets to the page, waits for them briefly so nothing jumps, renders the form and runs
the same script as Razor pages. It renders again when an attribute changes; after changing a `context-` attribute, call
`element.render()`.

- **Events:** `jfb:rendered` once it has drawn its form, and `jfb:error` with a `code` when it can't, such as
  `network_error`, `contract_mismatch` or a problem code from the table above. Once drawn, the form inside raises the
  same events as on a Razor page, its own `jfb:error` among them (`timeout`, `rate_limited`, `server_error`,
  `form_changed`), which bubbles through the element too: tell them apart by whether the form has rendered
  ([rendering.md](rendering.md#javascript)).
- **Views:** it draws with the built-in views; a site's Razor view overrides don't apply. A site's own field type
  registers a control writer for it ([extending.md](extending.md#8-headless-sites)).
- **Caching:** each render is made fresh on the server.
- **After a send,** the element shows the form's outcome; a picker's page after a send isn't applied, so a site that
  wants it navigates on `jfb:submitted`.

Load the element's script from the same Umbraco site whose API it calls.

## The Content Delivery API

The form picker's value comes out of Umbraco's Content Delivery API as the form's key, alias and name, with the picker's
theme (`theme`) and page after a send (`redirect`, the page's key) where its data type sets them:

```json
"form": { "key": "1d0c7a8e-…", "alias": "contact", "name": "Contact", "theme": "bootstrap5" }
```

A form that is missing, unpublished or not open to headless sites comes out as `null`, and a multiple picker gives a
list with `null` in its place. Expanding the property adds the form itself, as the definition gives it, in the language of `Accept-Language`:

```
GET /umbraco/delivery/api/v2/content/item/{id}?expand=properties[form]
```

The expansion holds the form's structure only; to send an entry, still read the definition for its `revision` and
consent wordings. A typical page reads its content from the Delivery API, then renders
`<jfb-form form="{form.key}" theme="{form.theme}" content="{page id}">`.

## Types and the OpenAPI document

- **TypeScript types and a JSON schema** of the definition's `form` ship in the `Jiron.Umbraco.FormBuilder` package's
  `schemas/` folder, in your NuGet cache, and in the repository's [schemas/](../schemas):
  `jfb-public-form.v1.d.ts` (`JfbPublicForm`, `PublicField`, `PublicRule` and the rest) and
  `jfb-public-form.v1.schema.json`.
- **The OpenAPI document** of the public API is in the repository at [api/jfb-public.json](api/jfb-public.json), with
  each operation's rate policy, body limit and time limit.
