# Deployment

What a site with Jiron Form Builder needs when it goes to a server, how forms move between environments, including as
files in source control, and what a site on several servers sets up.

## Contents

- [What a deployment needs](#what-a-deployment-needs)
- [Forms as files](#forms-as-files)
- [Moving forms by hand](#moving-forms-by-hand)
- [A copy of the live database](#a-copy-of-the-live-database)
- [Several servers](#several-servers)

## What a deployment needs

- **The package,** restored and built with the site; nothing to copy by hand. The website's scripts and stylesheets are
  embedded in the package, and its backoffice files are published with the site as for any Razor class library.
- **The database migrations,** which run as the site starts unless `Umbraco:CMS:Unattended:PackageMigrationsUnattended`
  is off; then an administrator runs them in Umbraco's Packages section. Until they have run, forms pause rather than
  half work. On several servers, let one server finish them before the others start the new version
  ([upgrading.md](upgrading.md#upgrading-a-site)).
- **The configuration,** checked as the site starts: a key it doesn't know, or a value out of range, stops the site with a
  message that names it. Keep secrets, such as captcha keys or a webhook's signing secret, in environment variables or
  your host's secret store, never in files you commit ([configuration.md](configuration.md)).
- **A writable folder for uploads** outside the web root, `umbraco/Data/JironFormBuilder/Uploads` unless configured;
  back it up with the database ([files.md](files.md)).
- **A lasting Data Protection key ring.** The signed checks, uploads, links in emails and Umbraco's own tokens rely on it.
  If the keys are lost at each restart, as in a container without a mounted key folder, visitors' open forms stop
  working. The **Data Protection keys** health check warns when the keys won't last.
- **A base URL** for links in emails: Settings › General, or an `https` domain in Umbraco, or
  `Umbraco:CMS:WebRouting:UmbracoApplicationUrl` ([email.md](email.md#links-in-emails)).
- **Behind a reverse proxy or a CDN,** ASP.NET Core's forwarded headers with the proxy's address, so rate limits, blocked
  ranges and stored addresses see the visitor's address rather than the proxy's.
- **A request size limit** in IIS or the proxy at least as large as the largest file forms take, or larger uploads are
  refused before they reach the site.

The **Jiron Form Builder** group on Umbraco's Health Check dashboard checks most of this after a deployment.

## Forms as files

A site can keep its published forms as JSON files in source control: developers change forms in files that travel with
the code, and forms editors publish are written back to the files.

```json
{
  "JironFormBuilder": {
    "Definitions": { "Mirror": true, "Folder": "~/umbraco/JironFormBuilder/forms" }
  }
}
```

`Folder` is optional and must lie outside the web root. With it on:

- **Each published form has a file,** `<alias>.json`, in the same format as **Export as JSON**, with the library
  option lists it uses. Drafts never reach the files.
- **Publishing writes the file** at once. A publish never overwrites a file that someone changed since the site last saw
  it: that is a conflict.
- **A pass every minute** takes changed files in, on the server that schedules Umbraco's work:

  | The file | What happens |
  | --- | --- |
  | changed, and the form wasn't published since | the file becomes the form's new draft and is published, with the comment "Published from its file." |
  | names a form the site doesn't have | the form is created with the file's key, alias and name, and published |
  | changed, and the form was published since too | a conflict: both are kept, and the health check says so |
  | holds an older version than the one published | rewritten from the published version |
  | deleted | nothing: deleting forms is the site's job |

  A file is compared by what its form holds, never by its bytes, so reformatting it changes nothing.
- **On several servers,** a publish writes the file on the server that published, and only the scheduling server takes
  files in. Keep the folder on storage every server shares, so both see the same files.
- **Keys travel with the files,** so pages that pick a form keep working in every environment.

**Conflicts** are listed by the **Forms as files** health check, each naming its file. To keep the file, use **Replace
from a file…** on the form and publish it. To keep the site's version, choose **Write the files again** in the health
check, which writes each conflicting form's published version to its file.

**Commit the folder's files,** and commit back the files that editors' publishes write. Check once that your deployment
copies the folder to the server: ASP.NET Core publishes a project's `.json` files by default, but a deployment tool may
leave folders under `umbraco/` out. On a read-only file system, such as an app running from a package, keep the mirror
off: the site couldn't write its files.

## Moving forms by hand

| What | Out | In |
| --- | --- | --- |
| A form, as a new copy | **Export as JSON** in the form's **…** menu | **Import a form…** in the create menu: a new form with new keys, unpublished; it lists what the site lacks, such as a theme |
| A form, keeping its keys | **Export as JSON** | **Replace from a file…** in the form's **…** menu: the file becomes the form's draft; its name, entries, access and history stay |
| A theme | **Export** in the theme's **…** menu | **Import a theme…** in the themes list's **…** menu |
| Option lists | travel inside the forms that use them | reused when the site has the same list, otherwise added |

Email layouts, settings and permissions don't travel this way; set them up on each environment, or in configuration
where they have keys there. From code, `IFormBuilderFormExchangeService` exports, imports and replaces forms
([extending.md](extending.md)).

## A copy of the live database

A copy of the live database on a test server would send the live site's emails and webhooks again, for example when
retried deliveries fall due. Pause them before the copy starts:

```json
{ "JironFormBuilder": { "Actions": { "Paused": true } } }
```

or `JironFormBuilder__Actions__Paused=true` as an environment variable. Entries are still stored, nothing is lost, and
everything that waits goes out once it is off; this one setting takes effect at once, without a restart. Activity ›
Deliveries and the **Deliveries** health check show that sending is paused. Check the forms-as-files setting on the
copy too.

## Several servers

Jiron Form Builder needs no extra infrastructure on a load-balanced site:

- **Background work** runs as Umbraco's distributed jobs, one server running each, coordinated through the database: the
  delivery of actions and emails, retention, exports, summary emails and the "used on" index. Each server also has a
  worker that takes due deliveries as soon as its own entries are stored, so mail goes out within seconds.
- **Caches** follow Umbraco's server messages: a form, theme, list, setting or permission changed on one server reaches
  the others usually within seconds, and cached pages using a changed form are evicted on every server.
- **The file scans, the clean-up of abandoned uploads and the forms-as-files pass** run on the server that schedules
  Umbraco's work.

What the site provides:

- **SQL Server.** SQLite can't be shared between servers.
- **Uploads on shared storage:** `JironFormBuilder:Files:Root` on a folder every server reaches, or a blob store
  ([files.md](files.md#for-developers)). The site warns at start-up when it runs on several servers with the default
  folder.
- **One Data Protection key ring** for all servers, with one application name, as Umbraco itself needs.

Rate limits count on each server, so with several servers a visitor can get a little more through than one server
allows. The health checks warn about uploads on each server's own disk, keys that differ between servers, and
background jobs that have stopped.
