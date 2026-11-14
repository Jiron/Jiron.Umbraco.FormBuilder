# Installation

What Jiron Form Builder needs, what installing it adds to a site, and the few things a site sets up for it.
[getting-started.md](getting-started.md) is the short version.

## Requirements

- **Umbraco 17.6 or later** within 17 (17.6 is the first 17.x whose runtime cache clears reliably). Umbraco 18 is not
  supported yet.
- **.NET 10**, as Umbraco 17 needs.
- **SQLite or SQL Server.** SQLite suits a small site on one server; a busy site, or one on more than one server,
  belongs on SQL Server ([performance.md](performance.md#where-sqlite-stops-keeping-up) has the numbers).
- **No Node.js** on the site: the backoffice and website scripts come built inside the package.

## The packages

| Package | What it is |
| --- | --- |
| `Jiron.Umbraco.FormBuilder` | The one a site installs. It brings the two below. |
| `Jiron.Umbraco.FormBuilder.Core` | The forms, entries, rules, actions and database. Installed with the first. |
| `Jiron.Umbraco.FormBuilder.Web` | The backoffice, the website rendering and the APIs. Installed with the first. |
| `Jiron.Umbraco.FormBuilder.AI` | Optional: drafts and translates forms through Umbraco.AI, for editors only ([ai.md](ai.md)). |

```
dotnet add package Jiron.Umbraco.FormBuilder
```

While only prereleases are published, add `--prerelease`. Build and start the site; no change to `Program.cs` is
needed, because Umbraco discovers the package's composers by itself.

Each package carries its licence, the [PolyForm Perimeter License 1.0.1](../LICENSE.md), and asks for it to be
accepted where the tool supports that, as Visual Studio does. The [licence FAQ](license-faq.md) explains it in plain
words. The package collects no telemetry.

## The first start

### The database

The package brings one migration plan. Umbraco runs it as the site starts when
`Umbraco:CMS:Unattended:PackageMigrationsUnattended` is on, which is Umbraco's default. With it off, a user with access
to Umbraco's **Packages** section runs it there: **Installed**, then **Run pending package migrations** on Jiron Form
Builder's row. Each step runs in its own transaction and can safely run again.

The plan creates 25 tables, all named `jfb…`, and keeps its state in Umbraco's `umbracoKeyValue` table.
[database.md](database.md) describes each table and step.

**While migrations are pending, Jiron Form Builder pauses:** visitors see no forms, nothing goes out, and the Overview
says only that the migrations need to run. Nothing is half done.

### What it adds

None of this needs setting up:

- **The Form Builder section** in the backoffice's top bar, with its sidebar of forms, entries, the library, privacy
  tools and settings.
- **Outside the section:** a Forms tab in the backoffice search, the **Jiron Form Builder form picker** property
  editor, a **Form entries** view on each member, and a **Jiron Form Builder** group on Umbraco's Health Check
  dashboard.
- **Built-in content:** nine form templates, three themes (Default, Bootstrap 5 and Unstyled), two email layouts, a few
  standard option lists and the list of countries. No demo forms or pages.
- **Addresses:** the public API under `/jfb/api/v1` and the website's scripts and stylesheets under `/jfb/assets/`.
  Keep your own content off those two paths. The backoffice uses `/umbraco/jfb/management/api/v1` and
  `/App_Plugins/JironFormBuilder/`.
- **Background jobs** for emails, webhooks, retention and statistics. They run as Umbraco's distributed background
  jobs, so a site on several servers needs nothing extra.
- **A private folder for uploaded files**, outside the web root: `umbraco/Data/JironFormBuilder/Uploads` unless
  configured otherwise ([files.md](files.md)).
- **Help in your editor:** when the site builds, Umbraco adds the package's settings schema to the site's
  `appsettings-schema.json`, so editors that read JSON schemas suggest and explain every `JironFormBuilder` key.

Nothing is added to the site's content, document types or data types until someone asks for the form block.

## Who can open the section

At first, only Umbraco's **Administrators** group can open Form Builder; the migration adds the section to that group
once, so an administrator who removes it later keeps it removed. Administrators hold every permission, because they
could grant themselves any.

Another user group needs two things:

1. **The Form Builder section,** added to the group in Umbraco's **Users** section. Form Builder's **Settings ›
   Access** lists every group that is missing it, with a link to each.
2. **Permissions,** set in **Settings › Access**: what the group may do with forms, entries, the library, settings and
   privacy. Folders and forms can be limited further.

[permissions.md](permissions.md) explains the eleven permissions. Editors who only place forms on pages need neither:
the form picker works for anyone with Umbraco's Content, Media, Members or Settings section.

## Email

Forms send mail through Umbraco's own mail sender, so they use the SMTP settings under `Umbraco:CMS:Global:Smtp`, and
any handler the site already has for Umbraco's emails sees them too. Without a mail server, entries are still stored
and other actions still run; their emails wait and go out once sending works.

Two settings make sure mail arrives and links work:

- **A From address:** Settings › Email › Sender, or Umbraco's `Umbraco:CMS:Global:Smtp:From`.
- **A base URL** for links in emails: Settings › General › Base URL, otherwise the site's first `https` domain in
  Umbraco, otherwise `Umbraco:CMS:WebRouting:UmbracoApplicationUrl`. The request's own address is never used.

Settings › Email › **Send test email** checks the whole path. [email.md](email.md) covers sending, layouts, limits and
local testing.

## Putting forms on pages

A form must be published before a page can show it. There are three ways to place one:

- **In a template or view,** by code: `@await Component.InvokeAsync("FormBuilderForm", new { form = "contact" })`, or
  `<jfb-render form="contact" />` with the tag helpers imported.
- **As a block** that editors add to pages, described below.
- **Headless,** through the public API or the `<jfb-form>` web component ([headless.md](headless.md)).

The script and stylesheet a form needs are added to the page by themselves, once per page. [rendering.md](rendering.md)
covers every option, themes and views.

### A form block for editors

**Create a form block type…** is in the Overview's checklist and in Settings › General, for users who may manage
settings and can open Umbraco's Settings section. It creates:

- a data type **Jiron Form Builder form picker**, for one form;
- an element type **Form** (alias `jfbFormBlock`) with one required property, `form`.

It never changes a type that exists already, and it creates no block list. Then:

1. **Allow the block:** add **Form** to the available blocks of the Block List or Block Grid data type that your
   document types use.
2. **Write its view,** as for any other block. For a block list, `Views/Partials/blocklist/Components/jfbFormBlock.cshtml`:

   ```cshtml
   @inherits UmbracoViewPage<Umbraco.Cms.Core.Models.Blocks.BlockListItem>
   @if (Model.Content.Value<Jiron.Umbraco.FormBuilder.Web.Rendering.FormBuilderFormReference>("form") is { } reference)
   {
       <jfb-render picked="reference" />
   }
   ```

   It needs `@addTagHelper *, Jiron.Umbraco.FormBuilder.Web` in `Views/_ViewImports.cshtml`. For a block grid, the view
   is `Views/Partials/blockgrid/Components/jfbFormBlock.cshtml`, with the same body under
   `@inherits UmbracoViewPage<Umbraco.Cms.Core.Models.Blocks.BlockGridItem>`.

Editors then add a **Form** block to a page, choose a form and publish the page.

## Configuration

Jiron Form Builder has two layers of settings:

- **The backoffice settings,** under Form Builder › Settings: General, Email, Spam protection, Files, Access and
  Integrations, plus Privacy › Retention and Data collection. Administrators change them there; their defaults live in
  the code, so a new site needs none of them.
- **The host configuration,** the `JironFormBuilder` section of `appsettings.json` and its siblings. It is for
  developers: secrets, allowed hosts, hard limits and locks. It always wins over the backoffice, which shows what it
  sets as read-only. The site checks it at start-up, and a key it doesn't know or a value out of range stops the site
  with a message that names the key.

A site needs no `JironFormBuilder` section to run. A typical one names the hosts that may embed forms or receive
webhooks:

```json
{
  "JironFormBuilder": {
    "PublicApi": { "AllowedOrigins": [ "https://www.example.com" ] },
    "Webhooks": { "AllowedHosts": [ "hooks.example.com" ] }
  }
}
```

[configuration.md](configuration.md) lists every key with its default.

## More than one server

On a load-balanced site, or one that moves between servers:

- put the uploads folder (`JironFormBuilder:Files:Root`) on storage every server shares, or use a blob store
  ([files.md](files.md));
- share ASP.NET Core's Data Protection key ring between the servers, as Umbraco itself needs;
- prefer SQL Server.

The **Jiron Form Builder** health checks warn about each of these, and about background jobs that have stopped.
[deployment.md](deployment.md) has the details, and how to keep forms in source control.

## Uninstalling

Removing the package is not automated, and the migrations never run backwards. Back up the database first. Then
remove, in this order:

1. the site's own code that uses the package: views with `<jfb-render>` or `FormBuilderFormReference`, the tag helper
   import, and the form block's view;
2. the blocks, properties and data types that use the form picker, and the **Form** element type, if they were
   created;
3. the package itself.

What stays behind until you remove it: the 25 `jfb…` tables with every form and entry, the plan's state in
`umbracoKeyValue`, the section in user groups, the uploads folder, and the copied
`appsettings-schema.Jiron.Umbraco.FormBuilder.json`.
