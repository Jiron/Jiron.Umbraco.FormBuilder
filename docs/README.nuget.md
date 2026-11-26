# Jiron Form Builder

Forms for Umbraco 18: designed in the backoffice, filled in on any page, and kept in one calm place afterwards.

Editors build forms in a **Form Builder** section of the backoffice, with pages, rules and actions. Visitors fill them
in on the website, in Razor or headless. The site's team reviews the entries, approves and exports them, and keeps
personal data in check with the privacy tools.

## What it does

- **A drag-and-drop designer** with 33 field types, multi-page forms, rules, a live preview, drafts and a history of
  every published version.
- **Actions after each entry:** emails, signed webhooks, Slack and Microsoft Teams, Umbraco content and email
  verification, each with a delivery log.
- **Entries** in an Inbox with Spam, approval, notes and exports to CSV, Excel, JSON and ZIP, plus insights without
  cookies.
- **Accessible forms** (WCAG 2.2 AA) that work without JavaScript, with spam protection that needs no third party.
- **Privacy built in:** retention, sensitive fields, no IP addresses by default, and tools to find, export and erase one
  person's data.
- **For developers:** a form picker and block, tag helpers, themes, a headless API with a web component, and extension
  points for field types, actions, option sources and more.

## Getting started

You need Umbraco 18.1 or later (within 18) on .NET 10, with SQLite or SQL Server. Install
`Jiron.Umbraco.FormBuilder`, which brings `Jiron.Umbraco.FormBuilder.Core` and `Jiron.Umbraco.FormBuilder.Web` with
it. `Jiron.Umbraco.FormBuilder.AI` is an optional add-on that drafts and translates forms through Umbraco.AI.

Start the site and choose **Form Builder** in the backoffice's top bar: its setup checklist leads you to a first form.
Publish it, then show it on a page:

```cshtml
@await Component.InvokeAsync("FormBuilderForm", new { form = "contact" })
```

- [Getting started](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/getting-started.md): from
  installing to the first entry in about ten minutes.
- [The documentation](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/README.md): for editors, site
  owners and developers.
- [Upgrading](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/upgrading.md) and the
  [changelog](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/CHANGELOG.md).

## Licence

Jiron Form Builder is **free to use and change, including commercially; no selling, no published versions of your own
(free or paid, open source or not), no competing products; source-available.** The package asks you to accept the
[PolyForm Perimeter License 1.0.1](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/LICENSE.md), and the
[licence FAQ](https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/license-faq.md) explains it in plain
words.

It is a hobby project: as far as the law allows, it comes as is, without any warranty or liability, and no lawyer has
checked its privacy templates or default texts. Whether your forms meet the law that applies to you is yours to check.

Made by [Manassés Zähnler](https://jiron.dev) · [Ligata](https://ligata.ch). Issues and security reports go to the
[repository](https://github.com/Jiron/Jiron.Umbraco.FormBuilder).

Umbraco is a trademark of Umbraco A/S. Jiron Form Builder is an independent community project and is not affiliated
with or endorsed by Umbraco A/S.
