# Getting started

From installing the package to the first entry in your Inbox, in about ten minutes. This page takes the shortest path;
[installation.md](installation.md) explains each part in full.

## Before you start

- An Umbraco 17.6 or later site that runs, on .NET 10, with SQLite or SQL Server. A site made with `dotnet new umbraco`
  is fine.
- A backoffice user in Umbraco's **Administrators** group.
- For step 6, a page on the site that is published and has a template, or access to the site's views.

## 1. Install the package

In the site's project folder:

```
dotnet add package Jiron.Umbraco.FormBuilder
```

While only prereleases are published, add `--prerelease`. Then build. No change to `Program.cs` is needed: Umbraco finds
the package by itself.

If you want to see the emails forms send while you try things out, let Umbraco write them to a folder instead of
sending them. Create the folder first, then add this to `appsettings.Development.json`:

```json
{
  "Umbraco": {
    "CMS": {
      "Global": {
        "Smtp": {
          "From": "noreply@example.com",
          "DeliveryMethod": "SpecifiedPickupDirectory",
          "PickupDirectoryLocation": "C:/temp/mail-pickup"
        }
      }
    }
  }
}
```

## 2. Open Form Builder

Start the site. Its database tables are created as it starts. Sign in to the backoffice (reload it if it was already
open) and choose **Form Builder** in the top bar.

The section opens on its **Overview**, with a **Setup checklist** of five steps. Each step ticks itself off when it
is done, whoever does it, so you can follow it in any order. This page follows it too.

## 3. Create a form from a template

In the checklist, choose **Choose a template…**. The gallery opens with the **Contact** template selected: a name, an
email address and a message, then a thank-you message. Choose **Use template**.

The new form, named "Contact", opens in the designer. It is a draft: visitors see nothing of it until you publish it.

## 4. Preview it

Choose **Preview** at the bottom of the designer. The preview shows the form as visitors see it, at phone, tablet or
desktop width. **Fill with sample answers** fills it in for you; **Send** sends a test entry.

Test entries tick off the checklist step, but they never appear in the Inbox or in any list of entries, and they are
deleted after 7 days. The real entry comes in step 7.

## 5. Publish it

Choose **Publish…**. The dialog shows what the form check found (a built-in template has no problems) and lets you add
a comment for this version. Choose **Publish**. From now on, visitors get this version of the form.

## 6. Put it on a page

The quickest way is one line in a page's template. Open the template in Umbraco (Settings › Templates) or in your
editor, and add:

```cshtml
@await Component.InvokeAsync("FormBuilderForm", new { form = "contact" })
```

`contact` is the form's alias, made from its name. That is all: the script and stylesheet the form needs are added to
the page by themselves.

If editors should place forms themselves, use a block instead. In the checklist, **Create a form block type…** creates
a "Form" block; you then allow it on a block list and write its view. [installation.md](installation.md#a-form-block-for-editors)
shows how.

## 7. Send an entry

Open the page on the website, fill in the form and choose **Send**. The form makes way for its thank-you message.

The Contact template sends no email. To get one for each entry, open the form's **Actions** view and choose
**Add action** (see [actions.md](actions.md)).

## 8. Find it in the Inbox

In the Form Builder sidebar, open **Entries › Inbox**. The new entry is at the top, marked as new. Select it to see the
answers, its timeline and its notes. The Overview lists it under **Recent entries** too.

## What next

The checklist has two more steps:

- **Check email sending:** Settings › Email › **Send test email** sends a message to your own address. Without a mail
  server, entries are still kept, and their emails go out once sending works ([email.md](email.md)).
- **Choose who sees entries:** only administrators can open Form Builder at first. Settings › Access gives other user
  groups their permissions ([permissions.md](permissions.md)).

Then:

- [The editor's guide](editor-guide.md) tours the designer.
- [Rendering forms](rendering.md) covers every way to put a form on a page, and [headless sites](headless.md) the API.
- [The documentation index](README.md) lists every page.
