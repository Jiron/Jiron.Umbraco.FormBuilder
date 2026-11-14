# Permissions

Who may do what in the Form Builder section, set per Umbraco user group, for every form or for a folder or form of its
own.

## Contents

- [Opening the section](#opening-the-section)
- [The permissions](#the-permissions)
- [Settings › Access](#settings--access)
- [Folders and forms with their own access](#folders-and-forms-with-their-own-access)
- [How permissions combine](#how-permissions-combine)
- [Placing forms on pages](#placing-forms-on-pages)
- [Actions that reach further](#actions-that-reach-further)
- [Setting access in configuration](#setting-access-in-configuration)

## Opening the section

A user group needs two things before its users can work in Form Builder:

1. **The section,** added to the group in Umbraco's **Users › User groups**. When the package is installed, only the
   **Administrators** group gets it.
2. **Permissions** in Form Builder's **Settings › Access**.

Settings › Access lists every group that lacks the section, with a link to each. A user who has the section but no
permission sees an empty section that says an administrator can give their group access.

**Administrators hold every permission everywhere,** restricted folders included, because they could grant themselves
any.

## The permissions

| Permission | Lets a user | Needs |
| --- | --- | --- |
| Design forms | see forms in the tree, create, edit, preview, publish, duplicate, move, trash and restore forms and folders | |
| Manage actions | add, change, test and retry a form's actions | Design forms |
| View entries | see entries, their timelines and files, read and add notes, and see the form's insights | |
| View sensitive data | reveal sensitive answers, download a sensitive field's files, include them in exports | View entries |
| Edit entries | change entries' states and answers, forward entries | View entries |
| Delete entries | delete entries and empty Spam | View entries |
| Export entries | export and print entries, download the ZIP of files | View entries |
| Manage the library | option lists, themes, email layouts and templates | |
| Manage settings | the settings, Privacy › Data collection and Retention, the audit log and Deliveries | |
| Manage permissions | Settings › Access, and folders' and forms' own access | |
| Use the privacy tools | Privacy › People: find, export and erase a person's data in every form | |

The first seven can be given for every form, or for a folder or form; the last four only for every form.

A few things need two permissions: deleting a form for good needs **Design forms** and **Delete entries** on what it
removes, and retrying a delivery needs **Manage actions** and a screen that shows it, the entry (View entries) or
Activity › Deliveries (Manage settings).

Where a user lacks a permission for something, the backoffice names the permission it needs.

## Settings › Access

**Settings › Access** shows what each user group may do with every form, in three parts: Forms; Entries; Library,
settings and privacy. Tick the boxes, then **Save**.

- Ticking a permission ticks the one it needs too, and unticking one unticks those that need it; the screen says so.
- The Administrators column is ticked and can't be changed.
- If someone else changed access while you worked, your save stops and shows their change, so nothing is overwritten
  unseen.

Below the matrix, **Folders and forms with their own access** lists the exceptions, with **Add folder or form…**.

## Folders and forms with their own access

**Permissions…** in a folder's or form's **…** menu gives groups permissions there, on top of what they have for every
form, for example **View entries** on the HR folder for the HR group.

**Only these groups** restricts the folder or form: then only the groups listed there, and administrators, see it, and
access given to every form or to a parent folder stops there. Use it for forms whose entries only a few may see, such
as job applications. Restricting with no group listed asks first, as only administrators would see it.

Moving a form into or out of a restricted folder shows how its access changes; a move that lets more groups see it
needs **Manage permissions**. A duplicated form never takes a form's own access: it gets the access of the folder it
lands in.

## How permissions combine

A user holds a permission on a form if any of their groups has it:

- for every form, on any folder above the form, or on the form itself;
- but when a folder above, or the form, is restricted: only from the nearest restricted one and what is below it.

A form a user may not see doesn't exist for them: it is missing from the tree, the search, the Inbox and every count.

## Placing forms on pages

Editors who place forms need no Form Builder permission and not even the section. The form picker works for users with
Umbraco's Content, Media, Members or Settings section, so Umbraco's Editors group can place forms. It shows only the
names of published forms that aren't restricted, or restricted ones the user's groups have access to, never entries,
settings or actions. A picker's data type can narrow the choice further ([multi-site.md](multi-site.md#the-form-picker)).

## Actions that reach further

**Manage actions** is not enough for actions that reach beyond the form:

- **An action that copies sensitive answers** somewhere needs **View sensitive data** on the form.
- **A webhook header that sends a secret** needs **Manage settings**.
- **A content action** needs the matching rights in Umbraco: the Content section, the right to create (and publish, if
  it publishes) under the item it uses, every language for a document type that varies by language, and for uploaded
  files the Media section and the media folder. Umbraco's own permissions decide, start nodes included.

The check runs when such an action is added or changed; running it later needs nothing more.

## Setting access in configuration

A developer can set the permissions for every form in the site's configuration, so they come with each deployment:

```json
{
  "JironFormBuilder": {
    "Access": {
      "Grants": [
        { "group": "editor", "permissions": [ "design", "viewEntries" ] },
        { "group": "hr", "permissions": [ "viewEntries", "viewSensitive", "editEntries" ] }
      ]
    }
  }
}
```

Groups are named by their alias, and permissions by these names: `design`, `manageActions`, `viewEntries`,
`viewSensitive`, `editEntries`, `deleteEntries`, `export`, `manageLibrary`, `manageSettings`, `managePermissions` and
`privacyTools`. The grants then replace those of Settings › Access, which shows them read-only; folders and forms can
still have their own, unless `LockedSections` holds `access`.
