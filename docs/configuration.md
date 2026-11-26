# Configuration

Jiron Form Builder reads its host configuration from the `JironFormBuilder` section of the site's configuration:
`appsettings.json`, an environment's `appsettings.<Environment>.json`, environment variables or user secrets, as
any ASP.NET Core setting. Most of what editors and administrators change lives in the backoffice instead, under
**Form Builder › Settings**; this section is for developers, for the keys that must never be in the database, and for
locks that the backoffice then shows read-only.

> This page is generated from the settings types by `node scripts/configuration-docs.mjs`, through the appsettings
> schema that the package ships. Don't edit it by hand: a test fails when it is out of date.

## How the section works

- **Checked at start:** the section is read once when the site starts. A value out of range, or a key Jiron Form
  Builder does not know, stops the site with a message that names the key, so a typing mistake never goes unnoticed.
- **A change needs a restart,** except `Actions:Paused`, which takes effect at once.
- **Your editor helps:** the Core package adds `appsettings-schema.Jiron.Umbraco.FormBuilder.json` to the site's
  `appsettings-schema.json` when the site builds, so editors that read JSON schemas suggest and explain every key.
- **Secrets stay out of files you commit:** keep keys such as a captcha's secret key in user secrets, environment
  variables or your host's secret store. Settings that need a secret name it by reference, as
  `$JironFormBuilder:Secrets:<Name>`, so the value itself never reaches the database or a screen.
- **Environment variables** use two underscores between the parts of a key, and a list's items are numbered from 0:
  `JironFormBuilder__PublicApi__BasePath`, `JironFormBuilder__Webhooks__AllowedHosts__0`.

```json
{
  "JironFormBuilder": {
    "PublicApi": { "AllowedOrigins": [ "https://www.example.com" ] },
    "Webhooks": { "AllowedHosts": [ "hooks.example.com" ] },
    "LockedSections": [ "privacy" ]
  }
}
```

Each table below lists a part of the section: the key below `JironFormBuilder`, what kind of value it takes, its default
(empty where the setting has none of its own), and what it does.

## PublicApi

The public API visitors' browsers and headless sites call: where it lives and who may call it.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `PublicApi:BasePath` | text | `/jfb/api` | The path the API lives under, before its version (/v1): / and one or more segments of letters, digits, '.', '-', '_' and '~', at most 100 characters in all, and neither /umbraco, /App_Plugins or /jfb/assets nor a path below or above one of them. |
| `PublicApi:AllowedOrigins` | list of texts | none | The other sites whose pages may call the API from the browser, each an origin such as https://www.example.com: http or https, a host and a port when it is not the scheme's, and nothing after them. The site's own pages may always call it. None by default. |
| `PublicApi:ServerKeys` | list of objects | none | The keys headless servers submit with, sending Authorization: Jfb-Key &lt;key&gt;: a name and the SHA-256 of each key, never the key. None by default. |
| `PublicApi:ServerKeys[]:Name` | text | none | The key's name, which an entry the key submits records as its context value jfbServerKey: 1 to 64 letters, digits, hyphens and underscores, each name once. |
| `PublicApi:ServerKeys[]:Sha256` | text | none | The SHA-256 of the key's UTF-8 bytes as 64 hexadecimal digits, each hash once; scripts/server-key.mjs makes a key and its hash. |

## Rendering

How the forms placed on the site's pages are written.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Rendering:IncludeAssetsAutomatically` | true or false | `true` | Whether a form placed on a page writes the tags that load the website runtime, the base rules and its theme's stylesheet, once per page, when its placement does not say: true by default. Turned off, a placement writes them only when it asks (assets="true" on &lt;jfb-render&gt;, assets = true for the view component), and the site loads them itself, with a &lt;jfb-assets&gt; in its layout for example. |

## General

The defaults of the General settings for a site that has saved none.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `General:DefaultTimeZone` | text |  | The time zone of a site that has saved none, an IANA id in the server's exact spelling, such as Europe/Zurich: "today", opening dates and relative dates are in it. Missing or empty for UTC. A zone saved in the Form Builder section's General settings, for every site or for one, wins, and saving the General settings stores the zone they show. A zone the server does not know stops the site at start-up. |

## Files

Where uploaded files are kept, how large one may be, and which extensions no field takes.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Files:Root` | text | `~/umbraco/Data/JironFormBuilder/Uploads` | The folder the default store keeps the files in: ~/ followed by a path relative to the site's content root, or an absolute path, for example a folder that the servers of a load-balanced site share (the default lies on each server's own disk, and a load-balanced site that keeps it is told so at start-up). It must not lie inside the web root or contain it, as written or with its links followed: a site configured so does not start. Name the folder by its own path, because the check cannot see through an administrative share, a drive letter made with subst, a short 8.3 name or a link inside the web root that points to the folder. |
| `Files:MaxFileBytes` | whole number | `52428800` | The most bytes any uploaded file may have, 1 to 999,999,999,999,999: 50 MiB unless the site sets another. A file field's own limit is held to it, and publishing refuses a field whose limit is higher. The upload endpoint sets each request's limit from its field's, a little above the file's for the parts around it; a server or proxy in front of the site with a lower limit of its own (IIS's maxAllowedContentLength, for example) refuses larger files first. |
| `Files:MaxWaitingBytes` | whole number | `10737418240` | The most bytes of uploads may wait for an entry together, 0 to 999,999,999,999,999: 10 GiB unless the site sets another, and 0 leaves the cap out. Past it, an upload is refused before a byte of it is stored, until entries take what waits or the cleanup removes it a day later. Set it to what the disk can spare: the count is read every few seconds, so it can pass the cap by what other servers take in that time. |
| `Files:MaxWaitingBytesPerAddress` | whole number | `1073741824` | The most bytes one address may upload in the day an upload waits for an entry, 0 to 999,999,999,999,999: 1 GiB unless the site sets another, and 0 leaves it out, so that a few addresses cannot use up MaxWaitingBytes for everybody. An IPv6 address counts as its /64 network, and the /48 around it holds four shares together. Counted on each server, in whole mebibytes. Past Limits:MaxTrackedClients, a new address shares an overflow window's share with others, as its requests do. Raise it for a form that takes large files from visitors behind one address. |
| `Files:BlockedExtensions` | list of texts | none | The extensions no field takes, in lowercase letters and digits without the point, each once, such as dwg. A site's own list replaces the defaults (exe, dll, js, mjs, bat, cmd, ps1, sh, msi, msp, scr, pif, cpl, jar, vbs, wsf, hta, lnk, reg, html, htm, xhtml, shtml, svg, php, phtml, asp, aspx, jsp and cgi), which apply while it lists none. Publishing refuses a field that lists one, and a field's groups of file types leave them out. Programs and scripts are refused by their first bytes, whatever their name. |

## Actions

Whether actions and email go out.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Actions:Paused` | true or false | `false` | Holds back every action run and email: entries are still stored and nothing is lost, and all of it goes out once this is off. It is meant for a copy of a live database restored onto a test server, and for incidents. Unlike the other options, a change takes effect at once. |

## Definitions

Whether published forms are also kept as JSON files for source control, and where.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Definitions:Mirror` | true or false | `false` | Writes each published form to &lt;alias&gt;.json and reads the folder back on a background pass, so teams that deploy through source control review form changes as files; off by default. |
| `Definitions:Folder` | text | `~/umbraco/JironFormBuilder/forms` | Where the files are: ~/ and a path relative to the content root, or an absolute path, never inside the web root or around it; read only while Mirror is on. |

## Limits

Hard limits that editors cannot exceed.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Limits:Definitions:MaxBytes` | whole number | `2097152` | The largest document, in UTF-8 bytes: 2 MB. |
| `Limits:Definitions:MaxPages` | whole number | `30` | Pages per form. |
| `Limits:Definitions:MaxGroups` | whole number | `100` | Groups per form, on all pages. |
| `Limits:Definitions:MaxFields` | whole number | `300` | Fields per form, counting the fields of repeatable groups and the rows of grids, which store like fields. |
| `Limits:Definitions:MaxOptionsPerField` | whole number | `1000` | A choice field's own options. |
| `Limits:Definitions:MaxActions` | whole number | `50` | Actions per form. |
| `Limits:Definitions:MaxOutcomes` | whole number | `50` | Outcomes per form. |
| `Limits:Definitions:MaxRules` | whole number | `200` | Rules per form: those of pages, groups, fields, actions and outcomes, and the form-level validation rules. |
| `Limits:Mail:PerRecipientPerDay` | whole number | `3` | How many mails one address that visitors type may get in 24 hours, across all forms: 1 to 1,000,000, 3 by default. Every spelling of a mailbox counts as one. A mail over a limit is stored as limited and never sent. |
| `Limits:Mail:PerSitePerHour` | whole number | `200` | How many mails may leave for addresses that visitors type in an hour, on this installation: 1 to 1,000,000, 200 by default. |
| `Limits:Mail:AttachmentBytes` | whole number | `20971520` | How many bytes of attachments one mail may carry as they travel, a third more than the files: 1 to 104,857,600, 20 MB by default, since mail servers refuse larger mail. A file past it is left out, and the mail goes without it. |
| `Limits:MaxTrackedClients` | whole number | `100000` | The most rate windows one server keeps for addresses, server keys and forms: 1,000 to 10,000,000, 100,000 by default. Past that, new ones share one overflow window per limit, so a flood of distinct addresses is limited and memory stays bounded. |
| `Limits:ServerKeyFactor` | whole number | `10` | How many times an address's limits a server key gets, since one server sends for all its visitors: 1 to 1,000, 10 by default. The limits of a form and of the site stay as they are. |
| `Limits:Read:PerAddressPerMinute` | whole number | `120` | Requests per minute from one address, or one server key. |
| `Limits:Read:PerAddressPerHour` | whole number | `0` | Requests per hour from one address, or one server key. |
| `Limits:Read:PerAddressAndFormPerMinute` | whole number | `0` | Requests per minute from one address, or one server key, about one form. |
| `Limits:Read:PerFormPerMinute` | whole number | `0` | Requests per minute about one form, from anyone. |
| `Limits:Read:SitePerSecond` | whole number | `200` | Requests per second to the whole site, from anyone; whatever the other limits do, this one holds. |
| `Limits:Challenge:PerAddressPerMinute` | whole number | `30` | Requests per minute from one address, or one server key. |
| `Limits:Challenge:PerAddressPerHour` | whole number | `0` | Requests per hour from one address, or one server key. |
| `Limits:Challenge:PerAddressAndFormPerMinute` | whole number | `10` | Requests per minute from one address, or one server key, about one form. |
| `Limits:Challenge:PerFormPerMinute` | whole number | `0` | Requests per minute about one form, from anyone. |
| `Limits:Challenge:SitePerSecond` | whole number | `100` | Requests per second to the whole site, from anyone; whatever the other limits do, this one holds. |
| `Limits:Submit:PerAddressPerMinute` | whole number | `20` | Requests per minute from one address, or one server key. |
| `Limits:Submit:PerAddressPerHour` | whole number | `100` | Requests per hour from one address, or one server key. |
| `Limits:Submit:PerAddressAndFormPerMinute` | whole number | `5` | Requests per minute from one address, or one server key, about one form. |
| `Limits:Submit:PerFormPerMinute` | whole number | `600` | Requests per minute about one form, from anyone. |
| `Limits:Submit:SitePerSecond` | whole number | `50` | Requests per second to the whole site, from anyone; whatever the other limits do, this one holds. |
| `Limits:Upload:PerAddressPerMinute` | whole number | `30` | Requests per minute from one address, or one server key. |
| `Limits:Upload:PerAddressPerHour` | whole number | `200` | Requests per hour from one address, or one server key. |
| `Limits:Upload:PerAddressAndFormPerMinute` | whole number | `20` | Requests per minute from one address, or one server key, about one form. |
| `Limits:Upload:PerFormPerMinute` | whole number | `600` | Requests per minute about one form, from anyone. |
| `Limits:Upload:SitePerSecond` | whole number | `20` | Requests per second to the whole site, from anyone; whatever the other limits do, this one holds. |
| `Limits:Events:PerAddressPerMinute` | whole number | `120` | Requests per minute from one address, or one server key. |
| `Limits:Events:PerAddressPerHour` | whole number | `0` | Requests per hour from one address, or one server key. |
| `Limits:Events:PerAddressAndFormPerMinute` | whole number | `0` | Requests per minute from one address, or one server key, about one form. |
| `Limits:Events:PerFormPerMinute` | whole number | `0` | Requests per minute about one form, from anyone. |
| `Limits:Events:SitePerSecond` | whole number | `500` | Requests per second to the whole site, from anyone; whatever the other limits do, this one holds. |
| `Limits:Verify:PerAddressPerMinute` | whole number | `10` | Requests per minute from one address, or one server key. |
| `Limits:Verify:PerAddressPerHour` | whole number | `0` | Requests per hour from one address, or one server key. |
| `Limits:Verify:PerAddressAndFormPerMinute` | whole number | `0` | Requests per minute from one address, or one server key, about one form. |
| `Limits:Verify:PerFormPerMinute` | whole number | `0` | Requests per minute about one form, from anyone. |
| `Limits:Verify:SitePerSecond` | whole number | `0` | Requests per second to the whole site, from anyone; whatever the other limits do, this one holds. |

## Designer

Settings of field types and action types that the designer cannot change.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Designer:SettingLocks` | list of objects | none | Settings fixed for every form, for example the sender of every email action. Each names a kind, a type and a setting once. |
| `Designer:SettingLocks[]:Kind` | text |  | Whether the lock is on a field type (field) or an action type (action), in any case. |
| `Designer:SettingLocks[]:Type` | text |  | The type's alias, for example email: a letter, then letters, digits, '.', '-' and '_', at most 64 characters. |
| `Designer:SettingLocks[]:Setting` | text |  | The setting's name at the top of the type's settings, for example from, spelt exactly as the type names it. |
| `Designer:SettingLocks[]:Value` | text |  | The value every form uses, as text, at most 50,000 characters: a text, a number, a date, a time, a choice or a key as it is, true or false, a whole number as digits, a localised text as the text of every culture, and a list, an object or JSON written as JSON. Without one, the setting keeps its default. |
| `Designer:SettingLocks[]:Hidden` | true or false | `false` | Hides the setting in the designer instead of showing it read-only. |

## Access

Who holds which permission: the global grants, set from the host.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Access:Grants` | list of objects | none | The global grants, each naming a user group by its alias and the permissions it holds everywhere, for example { "group": "editor", "permissions": [ "viewEntries" ] }. With at least one, they replace the backoffice's global grants, which it then shows read-only; grants on folders and forms stay the backoffice's. Each group is listed once, and a permission comes with the one it needs (export with viewEntries). |
| `Access:Grants[]:Group` | text |  | The user group's alias, for example editor. A group the site does not have is granted nothing. |
| `Access:Grants[]:Permissions` | list of texts | none | The permissions, in any case: design, manageActions, viewEntries, viewSensitive, editEntries, deleteEntries, export, manageLibrary, manageSettings, managePermissions or privacyTools. |

## Captcha

The keys of the captcha providers, which live only here.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Captcha:RecaptchaV2:SiteKey` | text |  | The public key a page shows the captcha with. |
| `Captcha:RecaptchaV2:SecretKey` | text |  | The secret key tokens are verified with; it never leaves the server except to the provider. |
| `Captcha:RecaptchaV2:VerifyAddress` | text |  | Another address to verify tokens at, for a test host's stub; null for the provider's own. An https address, or an http one on this machine, as the secret key goes with each token. |
| `Captcha:RecaptchaV3:SiteKey` | text |  | The public key a page shows the captcha with. |
| `Captcha:RecaptchaV3:SecretKey` | text |  | The secret key tokens are verified with; it never leaves the server except to the provider. |
| `Captcha:RecaptchaV3:VerifyAddress` | text |  | Another address to verify tokens at, for a test host's stub; null for the provider's own. An https address, or an http one on this machine, as the secret key goes with each token. |
| `Captcha:Hcaptcha:SiteKey` | text |  | The public key a page shows the captcha with. |
| `Captcha:Hcaptcha:SecretKey` | text |  | The secret key tokens are verified with; it never leaves the server except to the provider. |
| `Captcha:Hcaptcha:VerifyAddress` | text |  | Another address to verify tokens at, for a test host's stub; null for the provider's own. An https address, or an http one on this machine, as the secret key goes with each token. |
| `Captcha:UseRecaptchaNet` | true or false | `false` | Whether reCAPTCHA is reached through recaptcha.net instead of google.com. |

## Privacy

What the host allows of the settings that touch a visitor's privacy.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Privacy:AllowRetentionOff` | true or false | `false` | Whether the site may keep entries of a state for good, without retention. Off by default, and a stored "for good" then reads as the default days of its state. |
| `Privacy:AllowFullIp` | true or false | `false` | Whether entries may keep visitors' full IP addresses. Off by default, and a site or form that stores full then keeps them anonymised. |
| `Privacy:AllowImplicitCaptchaConsent` | true or false | `false` | Whether a site may load a captcha's script without asking its visitors, the implicit consent mode. Off by default, and a stored implicit then reads as explicit. |

## Secrets

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Secrets:<name>` | text |  | The secrets settings may name by reference, as $JironFormBuilder:Secrets:&lt;Name&gt;: each name is 1 to 64 letters, digits and '_', and its value lives only here, never in the database or a screen. |

## Webhooks

The other hosts Jiron Form Builder may call: JSON option lists and webhooks.

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `Webhooks:AllowedHosts` | list of texts | none | The hosts that may be called, each a host name such as events.example.com or an IP address, in any case, with no scheme, path or wildcard. A host alone is called on its scheme's standard port only (443, or 80 for http); to call it on another, name the port too: events.example.com:8443, or [2001:db8::1]:8443 for an IPv6 address. None by default, so nothing is called. |
| `Webhooks:AllowHttp` | true or false | `false` | Whether http addresses may be called too; only https by default. |

## LockedSections

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `LockedSections` | list of texts | none | Backoffice settings sections that are read-only: general, email, protection, files, privacy, access (every grant and restriction) or integrations, in any case. |
