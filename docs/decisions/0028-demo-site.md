# 0028. The demo site: seeded from code, started only by its script

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.3 (decided and built); every feature step adds its demo forms to the seeder, and ADR-0011's
  end-to-end host (3.4) runs the same site with its own content root

## Context

Step 3.3 creates `demo/Jiron.Umbraco.FormBuilder.Demo`, the site that every later step shows its work on: Umbraco
17.7.1 on SQLite with an unattended install on port 5500, English and German pages (home, contact, events, careers,
feedback, a members area with sign-in, privacy and imprint), a footer slot for a newsletter form and mail written to
`.local/mail-pickup/demo`. It has to come back the same after every `scripts/demo.ps1 reset`, take new content from
each feature step, and keep every generated secret out of git and off command lines (CLAUDE.md rules 3 and 6).

What this machine taught along the way:

- **The imaging key.** Umbraco 17 generates `Umbraco:CMS:Imaging:HMACSecretKey` on its first start when none is set
  and writes it into the site's `appsettings.json` (§3.3). In this repository that file is tracked, so a start
  outside the script would put a secret into a tracked file.
- **Byte-order marks.** `ITemplateService.CreateAsync` with a `TemplateOnDisk` reads the committed view and writes
  it back with a UTF-8 byte-order mark, which the repository's text check refuses (check-text, step 0.2).
- **Domains before publishing.** A page published in two cultures while its root has no domains makes Umbraco warn
  on every request ("published with multiple cultures, but no domains are configured").
- **Public access** shows the sign-in page at the protected address (an internal rewrite), so a menu cannot tell
  the current page from the content item alone.

## Options

1. **A committed SQLite database.** Quick to start, but binary, bound to one Umbraco version, unreviewable, and it
   would hold the admin's and the members' password hashes.
2. **Umbraco's package import** (a package migration that imports a package file of document types and content).
   Umbraco's own format, but an opaque export that a feature step cannot extend by a reviewed line or two, and it
   imports once rather than adding what is missing.
3. **A seeder in C#** that runs when the site has started and creates, through Umbraco's own services, whatever is
   missing, with the page texts in a data file.

## Decision

**Option 3.** `Seeding/DemoSeeder.cs` handles `UmbracoApplicationStartedNotification` and creates each item only
when it is missing, found again by alias (the language, the dictionary items, templates and types) or by a fixed key
(the block list data type and every page), so a restart changes nothing and logs `content created: False`. Beyond
adding a missing child type to the home page's allowed children, it never changes an item that exists, so what an
editor does on the demo stays until a reset.

- **Content as data:** the page and block texts and the views' dictionary items are in `Seeding/DemoContent.json`,
  embedded in the assembly so a host with another content root (the end-to-end host) reads the same file. A feature
  step adds a page by adding it there. The views hold no language strings: their texts are dictionary items
  (`Demo.*`), in English and German.
- **The site:** Lindenhall Library, the host site of the Phase 2 mocks (`docs/ux/website/`), grown into a whole
  site. It stands for any customer's site, so its stylesheet never styles anything inside a form. English is the
  default language under `/en`, German under `/de`; every page is created unpublished, then the domains are
  assigned, then the pages are published, home first.
- **Members:** the group "Library members", two generated members and public access on the members page, with
  the sign-in page as its login and error page. Sign-in and sign-out use Umbraco's own surface controllers, so
  the demo carries no authentication code of its own. The menu marks the current page from the request path.
- **Secrets:** `scripts/demo.ps1` generates the admin, the two members and the imaging key into
  `.local/credentials.json` ("demo") on first use and passes them to the child process as environment variables,
  set for the moment of the start and removed straight after; nothing is printed or written elsewhere.
  `Program.cs` refuses to start without the imaging key, so the demo is never started any other way.
- **Processes:** the PID file holds the process id and its start time, and `stop` ends only that process with its
  tree (`taskkill /PID … /T /F`), never anything found by name (CLAUDE.md rule 6).
- **Byte-order marks:** right after creating a template, the seeder removes the mark Umbraco wrote, which leaves the
  committed view byte for byte as it was. The text check is not weakened.
- **Mail** goes to `.local/mail-pickup/demo` as `.eml` files (`SpecifiedPickupDirectory`) and is never sent.
- **No app-local ICU:** the template's copy of ICU comes in a package whose licence is not on rule 4's list, and
  every supported host has ICU, which Jiron Form Builder requires anyway (ADR-0005).
- **Checks:** `scripts/demo-check.mjs` checks the running demo in Edge (both languages, the members flow, axe, no
  request to another host, the backoffice sign-in with the generated admin); `scripts/tests/demo-check.test.mjs`
  checks its helpers, that every dictionary key a view uses is seeded in both languages, and that the script prints
  no secret.

## Consequences

- Each feature step updates the seeder and `DemoContent.json` together with its feature, and the check grows with
  it. Seeding stays additive: to see changed texts on pages that exist, reset the demo.
- The seeder is demo code, not a product feature: it is never packed, and its Umbraco calls follow Umbraco 17's
  services. `v18/main` adapts it when those change.
- A seeding error is logged as "Demo seeding failed" and the script stops the demo at once rather than waiting.
- Revisit if Umbraco stops writing the imaging key into `appsettings.json`, or if a supported host lacks ICU.
