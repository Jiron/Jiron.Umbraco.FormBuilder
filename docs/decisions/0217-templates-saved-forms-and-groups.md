# 0217. Templates: the built-in set, saved forms and saved groups

- Status: accepted; builds on [ADR-0046](0046-copies-templates-and-import.md) (built-in templates as envelopes, a form
  from a template), [ADR-0166](0166-the-form-library.md) (the template gallery),
  [ADR-0169](0169-the-designers-moves.md) (the canvas's moves and Duplicate), [ADR-0013](0013-permissions.md) (the
  permissions) and [ADR-0159](0159-mail-the-queue-the-layouts-and-the-limits.md) (the email action); IA 4, 5 and 7
- Date: 2026-11-08
- Roadmap step: 12.1, its part 12.3 (ADR-0197)

## Context

The gallery has shipped one built-in template, Contact, since 4.5c, and `jfbFormTemplate` has kept a kind (`Form` or
`Group`) since the schema's first version, but nothing saves a template. Part 12.3 asks for eight more built-in
templates in English and German (callback, a newsletter with verification, event registration over several pages with
an entry limit and sessions with places, a job application with uploads and approval, feedback with a 0-to-10 scale and
a grid, a quote request with calculations, a support request, and a privacy request whose entry goes to the people who
may use the privacy tools), "Save as template" with the gallery's previews, and saved groups: a group saved from the
canvas and inserted into any form from the palette as a copy with new keys.

Open questions:

- **Where a template's mail goes.** A template cannot know an address of the site. A made-up address would mail no one,
  or someone else.
- **Who may use the privacy tools** is a global permission (`privacyTools`), and no recipient of the email action names
  people by a permission: `to` takes addresses, `toField` the visitor, `toCreator` the form's creator.
- **What a saved form or group holds**, and what of a group points outside it: its own rules, or a group shown when a
  field elsewhere says yes; a cascading list whose parent is elsewhere; a calculation that names another field.
- **Keys and aliases of an inserted group.** The target form may already have a field with the alias `email`.
- **Who may save, rename and delete** a template, and who may insert a saved group.

## Decision

### The built-in templates

1. **Nine envelopes in Core** (`Forms/Templates/<alias>.json`), each with a fixed key, in the gallery's order: contact,
   callback, newsletter, event registration, job application, feedback, quote request, support request and privacy
   request. Their texts are English, the default text, and German; the backoffice names and describes each by its
   alias in its own languages.
2. **Each one publishes without an error of the form check**, as a test shows for every template on a site with the
   default settings, and each shows what it is for:
   - **callback:** a name, a phone number, the best time of day and what it is about;
   - **newsletter:** an email address, the topics and consent, with the address verified (`verification`, seven days)
     before the entry counts;
   - **event registration:** three pages (the session, the person, the summary page), sessions as options with places,
     the form's entry limit and its closed message, and a confirmation to the person;
   - **job application:** the person, the post, a CV and a letter as files, consent to keep the application, manual
     approval, and a confirmation to the applicant;
   - **feedback:** a 0-to-10 scale, a grid of statements, and a comment that is required when the score is low;
   - **quote request:** a service, the amounts as numbers, a calculation of the estimate shown to the visitor, and the
     contact details;
   - **support request:** the topic, the urgency, a description, a file, and the entry's reference (`SR-` and five
     digits) in the thank-you message;
   - **privacy request:** what the person asks for (a copy or the deletion of their data), how to find their data, and a
     mail to the people who may use the privacy tools.
3. **A template's mail goes only where the template can know:** the visitor (`toField`), the form's creator
   (`toCreator`), or the privacy team (below). Contact, callback, feedback, quote and support add no mail: their entries
   reach the inbox, and a site that wants a mail per entry has the new-form default that mails the creator (8.3).

### The privacy team as a recipient

4. **The email action takes `toPrivacyTeam`** (a switch, "Everyone who may use the privacy tools"): when the mail is
   queued, it goes to every active backoffice user with an email address who holds `privacyTools`, the administrators
   and every group granted it, each once, as `to` addresses do. The people are resolved when the action runs, so a
   change of groups reaches the next entry without editing the form. A group needs the Form Builder section, which the
   tools are in. It counts as a recipient for the check that a mail needs someone to go to. When no one with an address
   holds the permission the run queues nothing and logs a warning, as a mail to a creator without an address does;
   administrators always hold it, so a site meets this only when no administrator has an address.

### Saving a form as a template

5. **Save as template…** in a form's "…" (the tree and the workspace's header) asks a name, the form's own to start
   with, and an optional description, and saves the form's **stored draft** as it is: pages, settings, actions and
   outcomes, with their keys, which every form made from it renews (ADR-0046). In the workspace with unsaved changes the
   dialog says in one line that those are not in the template. References to the library, content and media stay by
   key, as a duplicate keeps them.

### Saving a group, and inserting it

6. **Save group as template…** in a selected group's "…" on the canvas asks a name and a description and saves the
   group **as the canvas shows it**, unsaved changes included, since that is what the editor sees. The server keeps it
   as a definition of one page holding the group, so the reader and the save checks of definitions hold it.
7. **What points outside the group:** a rule's condition that names an element outside it is left out, and a rule left
   without conditions goes; the save says how many rules it changed. A cascading list whose parent is outside keeps its
   options without the parent; a calculation or a merge tag that names a field outside stays as it is, and the form
   check of the form it lands in reports it, as it reports any.
8. **The palette lists the site's saved groups** under "Saved groups", by name; one inserts like a field type, at the
   insert point, after the selection or at the end of the page. The copy is made as Duplicate makes one (ADR-0169):
   every key inside it renewed, every reference inside it to something it holds pointing at the copy, each field taking
   a free alias (`email2`), and each calculation naming its fields' new aliases. It is one step for Undo.

### The Templates screen and permissions

9. **Library › Templates** shows the built-in and saved templates as the gallery does, saved groups among them marked
   as groups, with the chosen one's preview. A form template's preview has **Use template**, which creates the form at
   the root of the forms and opens it; a group's says that the palette inserts it. A saved template's "…" has
   **Rename…** (name and description) and **Delete…**; built-in templates have neither.
10. **Permissions:** saving, renaming and deleting a template takes `manageLibrary`; saving a form as a template also
    takes `design` on the form, whose draft it reads. Every user of the section may list the templates, and a user with
    `design` on a form sees the saved groups in its palette.
11. **The API** (`/umbraco/jfb/management/api/v1/template`): the gallery's list takes `kind` (`form`, the default, or
    `group`); `POST template/form` saves a form, `POST template/group` a group; `PUT template/{key}` renames;
    `DELETE template/{key}` deletes. Each is audited (`template.saved`, `template.renamed`, `template.deleted`, with the
    key, the kind and the form a template came from).

## Consequences

- A site gets nine forms that publish as they are, in two languages, and none mails an address it does not have.
- The privacy team is a recipient any form can use, not only the template's.
- A saved group never makes the form it lands in point at another form's fields through a rule; what else names a field
  outside is shown by the form check where it lands.
- Templates carry no concurrency version (ADR-0037): the last rename wins.
- **Revisit** if sites want categories in the gallery (the column exists), templates shared between sites (an export of
  templates), or a saved group that stays linked to its template.
