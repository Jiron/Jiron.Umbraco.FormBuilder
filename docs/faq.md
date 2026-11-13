# FAQ

Short answers to common questions, each with the page that says more.

## Which versions of Umbraco does it run on?

Umbraco 17, from 17.6.0, on .NET 10. Umbraco 18 is planned. The package's versions follow Umbraco's: version 17 is for
Umbraco 17 ([installation.md](installation.md#requirements), [upgrading.md](upgrading.md)).

## SQLite or SQL Server?

Both. SQLite suits a small site on one server; a busy site, or one on several servers, belongs on SQL Server.
[performance.md](performance.md#where-sqlite-stops-keeping-up) has the measurements behind that advice.

## Does it work on several servers?

Yes. Background work is coordinated through the database, caches follow Umbraco's server messages, and no email or
webhook goes out twice. A load-balanced site shares its uploads folder and its Data Protection keys, and uses SQL
Server ([deployment.md](deployment.md#several-servers)).

## Does it run on Umbraco Cloud?

It hasn't been tested there. Nothing in it is specific to a host, but check two things on any host with more than one
instance: uploads on storage every instance reaches, and a lasting Data Protection key ring.

## Can I use it headless?

Yes: through the public API, with the `<jfb-form>` web component on any page, and with the form picker in Umbraco's
Content Delivery API ([headless.md](headless.md)).

## Does it set cookies? What about GDPR?

It sets no cookies and stores nothing in the visitor's browser, and nothing loads from other companies' servers unless
an editor chooses a captcha, which then waits for the visitor's consent. IP addresses aren't stored by default, entries
are deleted after a set time, and the privacy tools find, export and erase one person's data
([privacy.md](privacy.md)). Whether a site complies with the law is for the site and its adviser to decide; the
[privacy facts](privacy/facts.md) and [checklist](privacy/checklist.md) help.

## Where are the answers stored?

In the site's own database, in Jiron Form Builder's tables, one typed row per answer so that lists can filter, sort and
export them. Uploaded files are kept in a private folder outside the web root, or in a blob store
([database.md](database.md), [files.md](files.md)).

## Can a form keep no entries at all?

Yes: **Store entries › Never** in the form's Settings view runs its actions, such as an email, and keeps nothing. A
form can also keep its entries for a set number of days ([privacy.md](privacy.md#retention)).

## Which languages does it speak?

The backoffice speaks English and German. The words forms bring for visitors come in English, German, French, Italian,
Dutch, Spanish and Danish, and every word can be changed. Forms are translated in the designer, or with Umbraco's
dictionary items ([multi-language.md](multi-language.md)).

## Is it accessible?

Forms and the backoffice are built and tested against WCAG 2.2 AA, with automated checks, keyboard runs and the
accessibility tree in the browser. [accessibility.md](accessibility.md) says what was tested and found, and
[a statement template](accessibility-statement-template.md) helps a site write its own.

## How fast is it?

A form renders in a few milliseconds from the cache, and a page loads only the parts of the script its forms use, about
2.5 KB for the smallest form. [performance.md](performance.md) has the measurements.

## Does it use AI?

Only if you add the optional `Jiron.Umbraco.FormBuilder.AI` package, which helps editors draft and translate forms
through Umbraco.AI. Nothing a visitor enters ever reaches an AI ([ai.md](ai.md)).

## Can visitors save a form and finish it later?

Not yet.

## Can I move forms from another form system?

There is no importer for other systems. Forms move between sites running Jiron Form Builder as JSON files, and can be
kept in source control ([deployment.md](deployment.md)).

## Can I add my own field types or actions?

Yes, in code: field types, actions, option sources, export formats, captcha providers, spam checks, merge tags, file
stores and scanners, with notifications and services to work with entries ([extending.md](extending.md)).

## Is it free? Is it open source?

It is free to use and change, including commercially, under the PolyForm Perimeter License 1.0.1. It is
source-available, not open source: you may not sell it, publish your own version of it, or build a competing product
with it. The [licence FAQ](license-faq.md) explains what that means in practice.

## How do I report a security problem?

Privately, as [SECURITY.md](../SECURITY.md) describes; never in a public issue ([security.md](security.md)).
