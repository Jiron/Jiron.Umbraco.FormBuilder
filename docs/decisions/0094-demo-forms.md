# 0094. The demo's forms

- Status: accepted; extends [ADR-0028](0028-demo-site.md) with the forms the feature steps add
- Date: 2026-10-28
- Roadmap step: 6.2b (decided and built, the contact form); each later feature step adds its forms the same way, and
  6.7 replaces the page's alias with the form picker

## Context

ADR-0028 seeds the demo through Umbraco's own services and leaves its forms to the feature steps. 6.2b renders the
first form on a page, so the demo needs one: created on a fresh install, found again on a restart, never changed
once an editor works on it, and shown on the contact page. Jiron Form Builder's form service, which creates, saves
and publishes a form with its checks and audit entries, is internal: a site has no public way yet to create forms
from code (the extension points are documented with 15.1). The form picker arrives with 6.7.

## Options

**How the seeder creates a form:**

1. **Through the management API,** signed in as the generated admin. The real path of the backoffice, but the
   seeder would need an OAuth client of its own and the admin's password inside the site, against ADR-0028's rule
   that secrets reach the site only through its environment at start.
2. **Rows written into the tables.** Skips every check and audit entry the form service makes, so the demo could hold
   forms the service would refuse.
3. **Through the form service,** which the demo sees as the test projects do (`InternalsVisibleTo`). The same checks,
   versions, audit entries and notifications as an editor's publish. Chosen.

**Where a definition lives:** in C# (types checked, but a definition read only by the compiler), or as a file in the
package's definition format (`schemas/jfb-form.v1.schema.json`), read by the package's own reader with its limits and
upgrades. The file is chosen: a feature step adds a form by adding a reviewed file, and the format is exercised as a
site's import would exercise it.

**How a page names its form until 6.7:** a fixed page key in a view, or a property of the page. The property is
chosen: an editor can change it in the backoffice as they will pick a form with 6.7.

## Decision

- `Seeding/DemoFormSeeder.cs` runs after the pages are seeded. Umbraco runs the package's migrations in a background
  service after an install or an upgrade, so the seeder first waits until they are done (polling the migration state
  each second, for up to two minutes, then stopping with a message). For each demo form it then looks the alias up
  through the form service and, when none is found, reads `Seeding/Forms/<alias>.json` (embedded in the assembly, as
  `DemoContent.json` is) with the package's definition reader, creates the form, saves the definition under the new
  form's key and publishes it, as the super-user, with the comment "Created by the demo's seeder". A form that exists
  is never changed; a definition that does not read stops the seeding with its problems, which the seeder logs.
- `src/Directory.Build.props` lets the demo see the packages' internals, beside the test projects. The demo is never
  packed or shipped, and its views and pages use only the public surface a site has.
- The demo's page types get an invariant text property `form` ("Form": the alias of a form to show below the
  content), which `DemoContent.json` sets per page (`"form": "contact"` on the contact page) and the page's partial
  renders with `<jfb-render form="…" />`. The demo's `_ViewImports.cshtml` adds the package's tag helpers.
- The contact form asks for a name, an email address, a phone number (optional) and a message (with a counter), in
  English and German; later steps add its choices, consent and actions as those features arrive.

## Consequences

- A fresh demo shows a real published form on the contact page in both languages, with its versions and audit
  entries as an editor's would have.
- An existing demo database keeps its old page types: after this step, `scripts/demo.ps1 reset` brings the property
  and the form.
- The shipped assemblies name one more friend assembly, which exists only in this repository.
- **Revisit** when 15.1 decides the public way for a site to create forms from code; the seeder then uses it.
