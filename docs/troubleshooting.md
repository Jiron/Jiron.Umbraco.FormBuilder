# Troubleshooting

What to check when something doesn't work, symptom by symptom. Start with the **Jiron Form Builder** group on
Umbraco's Health Check dashboard, which the Form Builder section's Overview shows too: most problems below have a
health check that names them and the setting that fixes them.

## Contents

- [The site doesn't start](#the-site-doesnt-start)
- [Forms are paused](#forms-are-paused)
- [Users can't see the section](#users-cant-see-the-section)
- [A form doesn't show on a page](#a-form-doesnt-show-on-a-page)
- [A form looks unstyled or doesn't react](#a-form-looks-unstyled-or-doesnt-react)
- [Emails don't arrive](#emails-dont-arrive)
- [Webhooks fail](#webhooks-fail)
- [Uploads are refused](#uploads-are-refused)
- [Visitors are told to wait](#visitors-are-told-to-wait)
- [Captchas](#captchas)
- [Entries land in Spam](#entries-land-in-spam)
- [Several servers](#several-servers)
- [Background jobs have stopped](#background-jobs-have-stopped)
- [The API's errors](#the-apis-errors)
- [Logs](#logs)

## The site doesn't start

Jiron Form Builder checks its configuration as the site starts, and stops it with every problem at once rather than
running with a wrong value. The messages name the key and say what it takes, for example:

```text
JironFormBuilder:Limits:Definitions:MaxFields (0) must be 1 to 3000, which is 10 times its default.
JironFormBuilder:LockedSection is not a setting of Jiron Form Builder; the settings in JironFormBuilder are PublicApi, Rendering, Files, …
```

Where the message appears depends on the host: the console, IIS's stdout log or the site's log file.

| The message says | Fix |
| --- | --- |
| `… is not a setting of Jiron Form Builder` | A typing mistake in a key; the message lists the keys that exist there. |
| `… must be 1 to …`, `… must be 0 to …` | A value out of range ([configuration.md](configuration.md) has each range). |
| `… lies inside the web root …` | `Files:Root`, or `Definitions:Folder` with the mirror on, must be outside the web root. |
| `… needs both its SiteKey and its SecretKey, or neither` | A captcha's keys are half set. |
| `… must be the SHA-256 of the key …, never the key itself` | A server key was pasted where its hash belongs ([headless.md](headless.md#from-a-server)). |
| `Jiron Form Builder needs .NET globalization with ICU …` | The site runs with invariant globalization. Remove `InvariantGlobalization` from the project, and on Linux install the ICU and tzdata packages. |
| `Jiron Form Builder cannot create its field types: …` (or its action types, themes, merge tags, option sources) | A site's own extension breaks a rule the message names ([extending.md](extending.md)). |

A change to the configuration takes effect at the next start, except `Actions:Paused`, which applies at once.

## Forms are paused

**You see:** the Overview says Jiron Form Builder is paused because its database migrations are pending; pages show no
forms; the API answers `503 migrations_pending`.

The package's migrations haven't run yet, for example after an upgrade on a site where
`Umbraco:CMS:Unattended:PackageMigrationsUnattended` is off. Run them in Umbraco's **Packages** section. Nothing is
half done meanwhile: visitors see nothing rather than a half-upgraded form, and no email goes out.

## Users can't see the section

- **Administrators** get the Form Builder section when the package is installed. If Umbraco's administrators group was
  renamed or missing then, nobody got it: add the section to a group in Umbraco's **Users › User groups**.
- **Other groups** need the section added in Umbraco's Users section **and** permissions in **Settings › Access**,
  which lists the groups that lack the section ([permissions.md](permissions.md)).
- **A user sees the section but no forms:** their groups have no permission on any form, or the forms are in a
  restricted folder. A form a user may not see is missing for them everywhere, as if it didn't exist.
- **Host configuration** in `JironFormBuilder:Access:Grants` replaces the grants of Settings › Access; a group alias the
  site doesn't have grants nothing, and the log says so.

## A form doesn't show on a page

Check, in this order:

1. **Is the form published?** An unpublished, trashed or misspelled form renders nothing, and the log says once:
   "A page names the form '…', which is no published form; the page renders without it."
2. **Are the migrations pending?** See [above](#forms-are-paused).
3. **Did it fail while rendering?** Signed-in backoffice users see a note "This form couldn't be shown" in its place,
   naming the error; the log has the details. Often a view the site overrode ([rendering.md](rendering.md#overriding-views)).
4. **Are the tag helpers imported?** `<jfb-render>` needs `@addTagHelper *, Jiron.Umbraco.FormBuilder.Web` in
   `Views/_ViewImports.cshtml`; without it the tag stays in the page as it is. The view component needs no import.
5. **Headless:** the form's Settings › Developer › **Headless access** must be on, or the API and the Delivery API
   answer as if it didn't exist.
6. **Not open, closed, full, or for members:** the form shows its notice instead.

## A form looks unstyled or doesn't react

- **The stylesheet and script** come from `/jfb/assets/`. A proxy, CDN rule or rewrite that blocks or changes that path
  breaks them. With `Rendering:IncludeAssetsAutomatically` off, the site must write them with `<jfb-assets />`.
- **A Content Security Policy** may block them: the forms need `script-src 'self'`, `style-src 'self'` and
  `img-src 'self' data:`, and `blob:` for upload thumbnails ([rendering.md](rendering.md#content-security-policy)).
  The browser's console names a blocked file.
- **A theme the site doesn't have** renders as Default, and the log names it.

## Emails don't arrive

1. **The Email sending health check:** is a mail server, or a pickup folder, set up in `Umbraco:CMS:Global:Smtp`, and is
   there a From address? Without them, mail waits and nothing is lost.
2. **Is sending paused?** `JironFormBuilder:Actions:Paused` holds back every email; the Deliveries health check says so.
3. **Activity › Deliveries** lists each mail with what happened: **Trying again**, **Failed**, **Uncertain** or
   **Limited**, with **Retry**.
4. **Limited:** an address a visitor typed gets at most 3 mails a day, and all such addresses 200 an hour. A limited
   mail is never sent ([email.md](email.md#limits)).
5. **The mail server can't be reached:** the Overview says so; mail waits without using up its tries, and this server
   tries again after a pause that grows to 5 minutes.
6. **No mail at all for an entry:** the entry is in Spam, or is a test entry; the action is off or its rule didn't
   match; or its recipient answer isn't an address.
7. **No links in emails:** the site has no base URL ([email.md](email.md#links-in-emails)).
8. **Mail arrives, but files don't:** attachments over 20 MB in all, or files still being scanned, are left out.

**Settings › Email › Send test email** checks the whole path for you.

## Webhooks fail

- **The host must be allowed** in `JironFormBuilder:Webhooks:AllowedHosts`; nothing is called until it is. A host
  alone allows its standard port; another port needs `host:port`. Only `https` unless `Webhooks:AllowHttp` is on.
- **Private addresses are refused,** even when the host is allowed: an address that resolves into a private network,
  such as `10.0.0.5` or `localhost`, is never called, so an internal system can't be reached this way.
- **Redirects are never followed.**
- **The run's code in Deliveries** says why: `not_allowed` (with the reason, such as `HostNotAllowed` or
  `AddressNotPublic`), `no_connection` and `http_503` (tried again), `timed_out` (uncertain after 30 seconds),
  `http_4xx` (failed), `secret_missing` (the configuration lacks a secret the action names).

[webhooks.md](webhooks.md) has the full list.

## Uploads are refused

| The visitor or the API sees | Cause |
| --- | --- |
| `413`, a file too large | Larger than the field's limit, which is at most the site's (Settings › Files) and the host's (`Files:MaxFileBytes`). |
| An error page of the server, or a broken connection | A limit in IIS or a proxy below the field's, such as IIS's `maxAllowedContentLength`. |
| `415`, a type not taken | A blocked extension anywhere in the name, a program whatever its name, or content that doesn't match its extension. |
| `503 uploads_full` | Uploads waiting for their entries reached `Files:MaxWaitingBytes`. They clear as entries are sent, or after a day. |
| `429` on uploads | One address uploaded more than its share (`Files:MaxWaitingBytesPerAddress`), or the upload rate limit. |
| A download says the file is still being scanned, or quarantined | A virus scanner hasn't answered yet, or found malware. |

## Visitors are told to wait

`429 rate_limited` with `Retry-After`, or a form that says it was sent too often, means a rate limit was reached. Each
server counts on its own.

- **Behind a proxy or CDN,** every visitor shares the proxy's address unless ASP.NET Core's forwarded headers are set up
  with the proxy's address. This is the most common cause.
- A whole office behind one address can reach the per-address limits; raise them in `JironFormBuilder:Limits`
  ([configuration.md](configuration.md#limits)).
- A server sending entries with a server key gets ten times an address's limits.

## Captchas

| Symptom | Cause |
| --- | --- |
| The site doesn't start | Only one of a captcha's two keys is set. |
| No captcha shows, and entries go to Spam as "The captcha couldn't check it" | The provider has no keys, or didn't answer within 8 seconds. |
| Every send fails the captcha | The token was made for another host than the site's domains or `PublicApi:AllowedOrigins`; it is older than a few minutes; or, for reCAPTCHA v3, the score is below the form's lowest score. |
| The captcha never loads | It waits for consent as Settings › Spam protection says; or the site's Content Security Policy blocks the provider. |
| reCAPTCHA is blocked in a region | Set `JironFormBuilder:Captcha:UseRecaptchaNet`. |

## Entries land in Spam

The entry's panel says why. A form at the proof-of-work or captcha level puts entries sent without JavaScript in Spam;
blocked words, domains and ranges in Settings › Spam protection may catch real visitors; and an entry sent very quickly
counts towards Spam ([spam-protection.md](spam-protection.md)). **Not spam** takes an entry out and runs its actions.

## Several servers

- **Files missing on some servers:** the uploads folder is on each server's own disk. The log says so at start-up, and the
  Uploaded files health check too. Use a shared folder or a blob store.
- **`401 challenge_required` again and again:** the servers don't share their Data Protection key ring, or it is lost at
  each restart. The Data Protection keys health check warns.
- **Changes reach other servers late:** settings, forms and permissions reach them through Umbraco's server messages,
  usually within seconds.

[deployment.md](deployment.md#several-servers) has what a load-balanced site needs.

## Background jobs have stopped

The Background jobs health check warns when the jobs haven't run for 15 minutes: no emails or actions go out. Causes:
the site is still starting or its migrations are pending, Umbraco's job runner is busy, or the process is stopped or
idle. Look in the log for "A pass of Jiron Form Builder's outbox failed". Pausing on purpose is not a stop; the
Deliveries check says so instead.

## The API's errors

Both APIs answer refusals as `application/problem+json` with a stable `code`; branch on `code`, never on the title.
[headless.md](headless.md#errors) lists the public API's codes. The management API's that matter most:

| Status | Code | Means |
| --- | --- | --- |
| 403 | `no_section` | The user's groups lack the Form Builder section. |
| 403 | `forbidden` | A permission is missing; the detail names it. |
| 404 | `form_not_found` | The form doesn't exist, or the user may not see it. |
| 409 | `setting_locked` | The site's configuration sets this value. |
| 409 | `concurrency_conflict` | Someone else changed it meanwhile. |
| 409 | `email_not_configured` | No mail server or pickup folder is set up. |
| 429 | `tests_limited` | The user ran 60 tests in the last hour. |
| 503 | `migrations_pending` | The migrations haven't run. |

## Logs

Jiron Form Builder logs under categories that start with `Jiron.Umbraco.FormBuilder`, and most messages start with
"Jiron Form Builder". It never logs answers, email addresses, tokens, file names or IP addresses: an exception is logged
with its type and stack, but without its message, as the message may hold a visitor's words.

The messages worth knowing:

| Message | Means |
| --- | --- |
| A page names the form '…', which is no published form | A placement names a form that isn't published. |
| Jiron Form Builder could not render form … | A form failed while rendering; the page showed nothing in its place. |
| … keeps uploaded files in its default folder …, and this site runs on more than one server | Uploads need a shared store. |
| Jiron Form Builder could not reach the mail server … | Mail waits for the server. |
| Jiron Form Builder could not send mail … | A mail attempt failed and will be tried again. |
| A form or the settings name the theme '…', which the site does not have | A missing theme; the form shows as Default. |
| A request carried a server key that matches none | A server sent an unknown key; said at most once a minute. |
| The view … declares contract version … | An overridden view is older than the package's (Development only). |
