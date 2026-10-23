# The demo site

`Jiron.Umbraco.FormBuilder.Demo` is the website of the fictional Lindenhall Library: Umbraco 17.7.1 on SQLite with
Jiron Form Builder, in English and German. Every roadmap step shows its work here, and the feature steps add their
demo forms to it. It is never packed or shipped. Why it is built this way: [ADR-0028](../docs/decisions/0028-demo-site.md).

## Running it

Start the demo only with the script. It generates the accounts and the imaging key and passes them to the site
through its environment; started any other way, the site refuses to start.

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\demo.ps1 start
```

| Command | What it does |
| --- | --- |
| `start` | Builds the client assets when they are missing or stale, starts the demo in the background and waits until it is seeded and answers in both languages. The first start installs Umbraco unattended. |
| `stop` | Stops the demo this script started, with its process tree, and nothing else. |
| `reset` | Stops it and deletes its database, logs, media and mail, so the next start is a fresh install. The accounts are kept. |
| `login` | Prints the backoffice address and where the accounts are. It never prints a password. |

| Address | |
| --- | --- |
| http://127.0.0.1:5500/en/ | the site in English |
| http://127.0.0.1:5500/de/ | the site in German |
| http://127.0.0.1:5500/umbraco | the backoffice |

The port comes from `Umbraco:CMS:WebRouting:UmbracoApplicationUrl` in `appsettings.json`, and the script refuses
ports outside 5500 to 5599. The site's output goes to `.local/demo/demo.log`.

## Accounts and mail

The first `start` generates these into `.local/credentials.json` under `demo` (git-ignored, never printed):

- `admin`: the backoffice administrator;
- `members`: two library members, who can sign in to the members area;
- `imagingHmacSecretKey`: the key Umbraco signs image URLs with.

Mail is written to `.local/mail-pickup/demo` as `.eml` files and never sent.

## What the seeder creates

When the site has started, `Seeding/DemoSeeder.cs` creates whatever is missing and leaves everything else as it is:

- the German language, next to English as the default;
- the dictionary items of the views (`Demo.*`), in English and German;
- the document types `demoHome`, `demoPage`, `demoLegalPage`, `demoMembers` and `demoLogin`, with their templates
  (the legal pages use `demoPage`'s), whose views are committed in `Views/`;
- a block list ("Demo blocks") with a text block and a highlight;
- the pages, from `Seeding/DemoContent.json`: home, events, careers, feedback, contact, members, sign-in, privacy
  and imprint, with English under `/en` and German under `/de`;
- the newsletter slot in the footer (its texts now; the newsletter form comes with a later step);
- the member group "Library members", the two members, and public access on the members page.

To add a page, add it to `DemoContent.json` with a new fixed key. Texts the views show belong in its `dictionary`.
The seeder never changes a page that exists, so after changing texts, run `reset`.

Umbraco writes each new template's view back with a byte-order mark; the seeder removes it at once, so the
committed views stay as they are.

## Checking it

With the demo running:

```
node scripts/demo-check.mjs
```

It checks both languages, the members area (a wrong password, signing in and out), axe, that no page requests
anything from another host, and the backoffice sign-in with the generated admin. Screenshots go to
`.local/demo/shots/`. Its helpers are tested with the other script tests (`node --test "scripts/tests/*.test.mjs"`).
