# Actions

Actions are what happens after a visitor sends a form: an email to the team, a confirmation to the visitor, a call to
another system, a message in a chat, a change of the entry's state, or a new item in Umbraco's content. A form has its
actions in its **Actions** view.

## Contents

- [Triggers](#triggers)
- [Adding an action](#adding-an-action)
- [Email](#email)
- [Webhooks, Slack and Microsoft Teams](#webhooks-slack-and-microsoft-teams)
- [Change the entry's state](#change-the-entrys-state)
- [Create or update content](#create-or-update-content)
- [Rules on actions](#rules-on-actions)
- [Testing an action](#testing-an-action)
- [How actions run](#how-actions-run)
- [When something fails](#when-something-fails)
- [Who may do what](#who-may-do-what)
- [Checking a webhook's signature](#checking-a-webhooks-signature)

## Triggers

The Actions view groups a form's actions by what starts them:

| Trigger | Runs |
| --- | --- |
| Submitted | when an entry comes in |
| Approved | when someone approves an entry; on a form without manual approval, as each entry comes in |
| Rejected | when someone rejects an entry; needs manual approval |
| Verified | when a visitor confirms their address; needs email verification |

**Manual approval** and **email verification** are switched on in the form's Settings view, under Submission
([the editor's guide](editor-guide.md#submission)). Where a trigger needs one that is off, the view says so with a link
to the setting, and actions placed there wait until it is on.

On a form without manual approval, **Submitted** and **Approved** both run as each entry comes in, so put each action
under one of them: an email under both goes out twice.

The entry states have names of their own in the lists: an entry waiting for approval is **Submitted** for the
triggers, and an entry waiting for its address to be confirmed is not yet submitted at all
([entries-and-exports.md](entries-and-exports.md#entry-states)).

## Adding an action

**Add action** opens the action's editor. Choose **What happens**:

- **Email the team:** a mail with the answers to addresses you name;
- **Confirm to the visitor:** a mail to the address the visitor gave;
- **Webhook,** **Slack** or **Microsoft Teams:** a call to another system or a chat;
- **Change the entry's state:** approve, reject or move it to Spam;
- **Create or update content:** an item in Umbraco's content for each entry.

Then:

- **Name:** shown on its card and in the delivery log. It follows the choice until you type your own.
- **When it runs:** its trigger, **Submitted** by default.
- **Position in the list:** actions of the same trigger run in this order.
- **The action's settings,** described below for each kind.
- **Stop the rest if this fails:** off by default. On, if this action fails for good, the actions after it for the same
  entry don't run; retrying it brings them back.
- **Rule:** without one, the action runs for every entry its trigger brings ([below](#rules-on-actions)).

Each action is a card in its trigger: its name, a line of what it does, its rule, and how its last run went. A card has
a switch to turn the action off without deleting it, **Test**, and **…** with **Duplicate**, **Move to another
trigger…** and **Delete**. Drag cards, or use Alt+↑ and Alt+↓, to change their order. A form holds up to 50 actions.

**Actions are part of the draft.** On a published form, a new or changed action runs for new entries once you publish;
each entry's actions are the ones of the version it was sent with.

## Email

| Setting | What it does |
| --- | --- |
| To | Who gets it: addresses, the answer of an email field, the person who created the form, everyone who may use the privacy tools, or addresses chosen by an answer (for example a department per topic). |
| Subject | With merge tags, per language. |
| Body | Formatted text with merge tags, per language. The presets start with a line and `{{ entry.table }}`, the answers as a table. |
| Leave out unanswered fields | Leaves the empty questions out of `{{ entry.table }}`. |
| Reply to | An email field whose answer replies go to. |
| Copy to | More addresses, in copy. |

Under **More options**:

| Setting | What it does |
| --- | --- |
| Send as plain text | Plain text instead of a designed mail. |
| Layout | The email layout; the site's default unless chosen ([email.md](email.md#email-layouts)). |
| Blind copy to | Addresses in blind copy. |
| From address | Another sender than the site's. Sites usually fix it in their configuration. Never a visitor's address. |
| Razor view | A view of the site's own that writes the body, for developers ([extending.md](extending.md)). |
| Show sensitive fields | Sensitive answers this mail may show; all others are masked. |
| Attach files | File fields whose files go with the mail, up to 20 MB in all. |

**Preview with a sample entry**, beside the settings, shows the mail as it will arrive: sender, recipients, subject and
the mail itself in light and dark, with sample answers made from the form's fields.

### The team's mail and the visitor's mail

- **Addresses you typed,** the creator, the privacy team and addresses chosen by an answer are the team's: one mail to
  all of them, with copies, reply-to and attached files.
- **The answer of an email field** is an address a visitor typed, which could be anyone's. It gets a mail of its own,
  without copies, reply-to or files, and links a visitor typed in their answers don't become clickable in it. The site
  sends at most 3 mails a day to one such address, and 200 an hour to all of them, so a form can't be used to send mail
  to strangers ([email.md](email.md#limits)). An empty answer means no mail, and the action still succeeds.

In both, sensitive answers are masked, and fields whose **Include in emails** is off are left out of
`{{ entry.answers }}` and `{{ entry.table }}`.

## Webhooks, Slack and Microsoft Teams

A webhook sends each entry to an address as a signed `POST`, for a CRM, an automation tool or your own code.
**Slack** and **Microsoft Teams** are the same action, set up for a chat channel.

| Setting | What it does |
| --- | --- |
| Where it goes | An address of its own, Slack or Microsoft Teams. The address must be on a host the site's configuration allows. |
| What it sends | **Every answer, by its field's alias**; **The names and values you choose**; **A body you write** with merge tags; or **Only that something happened**, without answers. In JSON, form data or XML. For a chat, **a message** in its own formatting. |
| Send metadata too | On by default: what happened, the delivery, the form and the entry, beside the answers. |
| Signing secret | Under More options. Signs each request, so the receiver can tell it came from this site. |
| Headers | Under More options. Sent with each request. |
| Send sensitive fields | Under More options. Sensitive answers it may send; others are left out. |

Keys, secrets and a chat's address are never typed into the action: they live in the site's configuration, and the
action names them as `$JironFormBuilder:Secrets:<Name>`. A developer sets them up; [webhooks.md](webhooks.md) has the
details, what each answer of the receiving server means, and Umbraco's own webhook events for entries.

## Change the entry's state

Moves the entry to **Approved**, **Rejected** or **Spam**, usually with a rule: "approve when the order total is under
100", "move to Spam when the company field says test". Approving or rejecting starts that trigger's actions. It runs at
once, without the 10 seconds a person's change waits, and an entry already in that state stays as it is. The form check
refuses two actions that would move an entry back and forth.

## Create or update content

Saves each entry as an item in Umbraco's content, such as a testimonial or an event registration.

| Setting | What it does |
| --- | --- |
| Document type | Each entry becomes an item of this type. |
| Where it goes | Under an item you pick, under the page the form was sent from, or under the root of the visitor's site, optionally only within a part of the content tree. |
| Name of the item | With merge tags. |
| Properties | For each property, the answer to a field or a value you write. |
| Publish it | Off by default: the item is saved for an editor to look at and publish. |
| Update an item of the same name | Under More options. Where the name comes from an answer, a visitor's answer chooses which item is overwritten, so turn it on only where that is what you want. |
| Media folder for files | Under More options. Where uploaded files go in the media library. |
| Copy sensitive fields | Under More options. Sensitive answers it may copy. |

A retry never creates a second item. An answer a property can't hold fails the run, and nothing is saved. Whoever sets
up the action needs the Umbraco rights to create, and to publish if it publishes, under that item.

## Rules on actions

An action's rule decides whether it runs for an entry: "Run Email the sales team when Topic is Sales". It can look at
the answers, the language, whether the visitor was signed in, their member groups, query values and today's date
([rules-and-validation.md](rules-and-validation.md)). Unlike rules on fields, it can also look at hidden fields.

The rule is decided once, when the entry arrives. Approving, rejecting or confirming later runs the actions the rule
chose then, even if someone edited the answers since.

## Testing an action

**Test**, on the card or in the editor, tests the action as the editor holds it, saved or not:

- **An email** goes to you alone, with a first line saying it is a test and who the real mail goes to.
- **A webhook or chat message** asks first, then sends a sample entry where the action sends, marked as a test, and
  shows what the server answered.
- **Changing a state** and **creating content** do nothing, and say what a run would do.

A test changes no entry and leaves no trace in the delivery log. Each user can run 60 tests an hour.

## How actions run

When an entry arrives, it is stored together with one queued run for each action whose trigger and rule apply, in one
step: no entry is lost when the mail server or a webhook's receiver is down. The site's background worker then runs
them, usually within seconds.

- **In order:** the actions of one trigger run in the order shown, each starting once the one before has had its first
  try. An action that waits for a retry doesn't hold the others back, unless it has **Stop the rest if this fails**.
- **Retries:** an action that fails is tried again after 1, 3, 9 and 27 minutes, five tries in all, then it is
  **Failed**.
- **Uncertain:** a try that was cut off, for example because the receiving server stopped answering, may have arrived.
  It is never retried by itself, so nothing goes out twice by accident; a person decides.
- **A person's change waits 10 seconds.** Approving, rejecting or marking as spam by hand starts its actions only after
  the 10 seconds in which **Undo** can take it back. Changes by rules, actions or email verification run at once.
- **An email action succeeds once its mail is queued.** The mail then has five tries of its own, which the delivery log
  shows.

Nothing runs for:

- an entry in **Spam**. Taking it out with **Not spam** runs the actions it missed, and only those;
- an entry **waiting for its address to be confirmed**, until the visitor confirms;
- **test entries** from the preview, unless the tester chose to run them;
- an action that is **switched off**, or whose rule doesn't match;
- runs that hadn't started when the entry was **rejected** or moved to Spam.

A developer can hold back every action and mail on the site with one setting, for example on a test server with a copy
of the live database ([configuration.md](configuration.md#actions)). Entries are still stored, and everything goes out
once it is off.

## When something fails

- **The card** says how the last run went: "Delivered 2 hours ago", "Failed at 14:02. Next try at 14:30.", or
  "Uncertain at 14:02. It may have gone through." **Details** says what happened, how often it was tried and what can
  be done.
- **The entry's timeline** lists each run with **Retry**, and the Entries view's **Actions** filter finds entries whose
  actions failed.
- **Activity › Deliveries** lists every mail and action run of the site, problems first, with **Retry**. Retrying an
  uncertain one asks first, as it may have arrived already.
- **The team hears of it:** an action that fails for good, or ends uncertain, emails the addresses in Settings › Email ›
  **System notices go to**, at most once an hour per form. The notice names no answer.

## Who may do what

- **Design forms** on a form shows its Actions view and the mail preview.
- **Manage actions** adds, changes, switches, tests and retries actions, and runs them again for chosen entries.
- An action that copies sensitive answers also needs **View sensitive data**, and a webhook header that sends a secret
  also needs **Manage settings**.

[permissions.md](permissions.md) explains each permission.

## Checking a webhook's signature

For developers: each request a webhook action sends carries a header such as:

```text
X-Jfb-Signature: t=1793620800, v1=0c4dd8ef53dd6e64...
```

`t` is the time of the attempt in seconds since 1970, and `v1` is the lowercase hexadecimal HMAC-SHA256 of the time, a
dot and the body, with the signing secret as its key. To check it:

1. Read the body as bytes, before any parsing.
2. Work out the HMAC of `t`, `.` and those bytes.
3. Compare it with `v1` in constant time.
4. Refuse a `t` more than five minutes from now, so a recorded request cannot be sent again later.
5. Refuse a header that gives `t` or `v1` twice.

Working samples, each with the same tests behind it:

- [C#](samples/webhook-signature/Verify.cs)
- [Node](samples/webhook-signature/verify.mjs)
- [PHP](samples/webhook-signature/verify.php)

The signature covers the body and the time, not the other headers. Where you rely on the delivery key, keep **Send
metadata too** on, so that it is in the signed body as well. [webhooks.md](webhooks.md) describes the body, the headers
and what each answer of your server makes the site do.
