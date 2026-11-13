# Email

How Jiron Form Builder sends mail, what a site sets up for it, and what happens when mail can't go out. The email
actions themselves, what they send and to whom, are in [actions.md](actions.md#email).

## Contents

- [How mail is sent](#how-mail-is-sent)
- [Setting up sending](#setting-up-sending)
- [Settings › Email](#settings--email)
- [Links in emails](#links-in-emails)
- [Email layouts](#email-layouts)
- [Limits](#limits)
- [When mail can't go out](#when-mail-cant-go-out)
- [Who may do what](#who-may-do-what)

## How mail is sent

- **Through Umbraco.** Forms send through Umbraco's own mail sender, with Umbraco's SMTP settings. A site that already
  handles Umbraco's emails in code, for example to send them through a mail service, handles these too.
- **Stored first.** Every mail is stored with what caused it, then sent by the site's background worker, usually within
  seconds. A mail server that is down loses nothing.
- **Tried five times.** A mail the server refuses is tried again after 1, 3, 9 and 27 minutes, then it is **Failed**.
  A mail whose sending was cut off after the server answered may have arrived; it becomes **Uncertain** and is never
  sent again by itself.
- **Listed.** Activity › Deliveries lists every mail, problems first, for 30 days after it was sent or gave up, with
  **Retry**. It shows to whom and why, never the subject or the text.

## Setting up sending

Mail is set up where Umbraco's is, in `Umbraco:CMS:Global:Smtp`. For a mail server:

```json
{
  "Umbraco": {
    "CMS": {
      "Global": {
        "Smtp": {
          "From": "noreply@example.com",
          "Host": "smtp.example.com",
          "Port": 587
        }
      }
    }
  }
}
```

Keep the server's user name and password out of files you commit, in user secrets, environment variables or your
host's secret store. Umbraco's documentation lists its other SMTP keys.

**For testing,** let Umbraco write mail to a folder instead of sending it. Each mail lands there as an `.eml` file that
mail programs open. Create the folder first:

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

**Without either,** entries are still stored and other actions still run. Their mail waits, without using up its
tries, and goes out once sending is set up. Settings › Email and the Overview say so.

## Settings › Email

On a site with several root sites, a **Site** switch at the top chooses whether a value is for all sites or for one;
a site's own value says so, with **Use the value for all sites** to go back.

**Sender**

- **Name:** who mail comes from, as inboxes show it.
- **Address:** the sender's address. Use an address of the site's own domain, so mail servers trust it. Empty, Umbraco's
  `From` is used.
- **Default layout:** how mail looks unless an email action picks another layout.

An email action can name another sender under its More options, which sites usually fix in their configuration
(`JironFormBuilder:Designer:SettingLocks`). A visitor's address is never the sender; to answer visitors, an email
action's **Reply to** names their email field.

**The team**

- **System notices go to:** addresses that hear when a delivery fails for good, at most once an hour per form. Health
  warnings show on the Overview and on Umbraco's Health Check dashboard.
- **Addresses entries may be forwarded to:** besides backoffice users, **Forward…** offers only these, so entries leave
  the site only to people it already trusts.

**Send test email** saves the settings and sends a test to your own address, with the chosen site's sender and layout.

## Links in emails

Links in emails, such as the address-confirmation link or the link to an entry, start with the site's **base URL**:

1. Settings › General › **Base URL**, for all sites or one;
2. otherwise the site's first `https` domain in Umbraco's Culture and Hostnames;
3. otherwise `Umbraco:CMS:WebRouting:UmbracoApplicationUrl`.

The address a request came in on is never used, so a forged request can't put another host into a link. Without a
base URL, mail carries no links and the address check of email verification can't go out; the Overview names each site
that lacks one.

## Email layouts

**Library › Email layouts** holds the looks mail is sent in. Two are built in: **Card**, the mail on a card framed in
the page colour, and **Letter**, a plain letter. A layout of your own adds:

- **Logo:** an image from the media library, or the address of one;
- **Colours** for light and dark mode: links and buttons, the background, the mail, text, quieter text and dividers,
  each checked for contrast;
- **Texts** per language: a header and a footer, in simple HTML.

Dark colours are worked out unless you set them; mail programs that ignore dark mode show the light mail.
**Preview with a sample entry** shows a layout in both, and **Send test email** sends it to you. A layout that is in
use, or the site's default, can't be deleted.

## Limits

Mail to an address a visitor typed could go to anyone, so the site limits it:

- **3 mails a day to one address,** across all forms, however it is written;
- **200 mails an hour** to all such addresses together.

A mail over a limit is stored as **Limited** and never sent; Deliveries says which limit held it back. Mail to the team's
own addresses doesn't count. Files attached to one mail may total 20 MB; a larger file is left out and the mail goes
without it.

A developer can change the limits in `JironFormBuilder:Limits:Mail` ([configuration.md](configuration.md#limits)).

## When mail can't go out

| What happens | What the site does | What the Overview says |
| --- | --- | --- |
| No mail server or folder is set up | Keeps the mail until there is one | "Emails can't be sent because no mail server is set up." |
| The mail server can't be reached | Keeps the mail without using up its tries, and tries again after a pause that grows to 5 minutes | "This server couldn't reach the mail server on its last try." |
| The mail server refuses a mail | Tries it again, then marks it Failed and tells the team | In Deliveries: how many failed in the last 24 hours |
| No sender address anywhere | The sending fails | "Emails have no From address." |
| The background jobs have stopped | Nothing goes out | "The background jobs haven't run for over 15 minutes." |
| Sending is paused by the site's configuration | Nothing goes out, nothing is lost | "Sending is paused." |

The same checks appear in Umbraco's Health Check dashboard, in the **Jiron Form Builder** group.

## Who may do what

- **Manage settings:** Settings › Email, its test email, and Activity › Deliveries.
- **Manage the library:** email layouts.
- **Manage actions:** the email actions of a form, and retrying them.
