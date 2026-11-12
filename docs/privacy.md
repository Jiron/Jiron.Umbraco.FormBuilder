# Privacy

Jiron Form Builder is built to keep as little as a form needs, for as long as it needs it, and to make a person's data
easy to find, hand over and erase. This page is the tour of those tools. Three more pages go into detail:

- [the privacy facts](privacy/facts.md): everything a submission stores, where and for how long, and what happens on the
  visitor's device;
- [the privacy checklist](privacy/checklist.md): what a site owner decides for each form and for the site;
- privacy-policy templates in [English](privacy/policy-template.en.md) and [German](privacy/policy-template.de.md), to
  adapt with your data protection adviser.

None of this is legal advice: your site decides what it collects and why.

## Contents

- [By default](#by-default)
- [Retention](#retention)
- [Sensitive answers](#sensitive-answers)
- [IP addresses](#ip-addresses)
- [Consent](#consent)
- [Answering a person](#answering-a-person)
- [Legal holds](#legal-holds)
- [Members who are deleted](#members-who-are-deleted)
- [The audit log](#the-audit-log)
- [On the visitor's device](#on-the-visitors-device)
- [Who may do what](#who-may-do-what)

## By default

- **No cookies** and nothing stored in the visitor's browser.
- **No IP addresses** kept on entries.
- **Entries are deleted** after a set time, from 30 days for spam to a year for approved entries.
- **Sensitive answers are masked** for everyone who may not see them.
- **Consent fields keep the exact wording** each visitor agreed to.
- **Logs never hold answers,** email addresses, file names or IP addresses.
- **Nothing loads from other companies' servers,** unless an editor chooses a captcha from reCAPTCHA or hCaptcha.

## Retention

**Privacy › Retention** sets how long the site keeps entries in each state, counted from the entry's last change of
state:

| State | Kept | |
| --- | --- | --- |
| Submitted | 365 days | entries waiting for approval count here too |
| Approved | 365 days | approved by someone, or at once on forms without manual approval |
| Rejected | 90 days | |
| Spam | 30 days | |

Each state takes 1 to 3,650 days. **Keep for good** is offered only where the site's configuration allows it
(`JironFormBuilder:Privacy:AllowRetentionOff`).

**What will be deleted** below the days shows, before you save, how many entries and files each form would lose in the
next 7, 30 or 90 days. Nothing needs pressing: retention deletes by itself every hour. An entry goes with its answers,
files, notes, timeline and emails, and the audit log records it.

**A form can differ:** its Settings view keeps entries for its own number of days per state (Privacy), for a fixed
number of days from when each was sent, or stores none at all (Submission › **Store entries**: **Never** still runs the
actions). Retention lists the forms that set their own.

Other things the site keeps for a while: test entries 7 days, entries waiting for their address to be confirmed 14
days, uploads no entry took 24 hours, sent mail 30 days, background exports 24 hours, the audit log a year and the
daily counts of Insights three years. [The privacy facts](privacy/facts.md#how-long-entries-are-kept) list them all.

## Sensitive answers

Two settings of a field, under **Advanced** in the inspector, protect an answer:

- **Sensitive:** the answer is shown masked in lists and entries, and only users who may **View sensitive data** can
  reveal it; each reveal is recorded in the audit log. It is left out of emails, exports and webhooks unless an action
  or export names it and the user may see it, and it never appears in entry titles, search, the timeline or logs.
- **Keep the answer › Until the entry's actions have run:** emails and webhooks still get the answer, then it is removed
  from the entry, with every copy the actions made. The entry shows "not kept".

Sensitive only masks an answer; keeping it until the actions have run removes it. Use the second for what the site
needs to pass on but not to keep, such as a phone number for a callback that a CRM takes over.

## IP addresses

**Privacy › Data collection › IP addresses** says what each entry keeps of the address it was sent from:

- **Nothing,** the default;
- **The address with its end removed:** enough to tell networks apart, such as `203.0.113.0` for `203.0.113.42`;
- **The full address,** for a site that needs it whole, such as to report abuse, and only where the configuration allows
  it (`JironFormBuilder:Privacy:AllowFullIp`). Say so in your privacy policy.

A form can choose its own in its Settings view, under Privacy. Rate limits and blocked ranges read the address without
keeping it. Behind a proxy, the developer sets up ASP.NET Core's forwarded headers, or entries keep the proxy's address.

## Consent

A [consent field](field-types.md#consent) stores that the visitor agreed, the exact wording they saw and its
language. The entry shows the wording beside the answer. If the wording changes while a visitor fills in the form,
their entry is refused rather than stored as an agreement to words they never saw.

Settings › General › **New forms** can start every new form with a consent field in the site's own wording. The form
check reminds editors when a form asks for personal data, such as an email address, without a consent field, where
consent is the basis the entries are kept on.

## Answering a person

**Privacy › People** finds, hands over and erases one person's data across every form, restricted and trashed ones
included:

1. **Find** by email address, or by member. It finds entries whose email field holds the address and entries the
   member sent while signed in. An address typed into a plain text field isn't found; search the entries lists for
   those too. Every search is recorded in the audit log, without the address.
2. **Export everything** prepares a ZIP in the background, with a folder per entry holding its answers (sensitive ones
   in full), consent wordings, timeline, notes, files and the mails sent to the person.
3. **Erase…** shows what it will remove first, then either **deletes** the entries with their files and emails, or
   **anonymises** them: they stay in the counts without any answer, file or email of the person. Every mail to the
   person is deleted either way. This can't be undone.

The privacy tools can't reach copies outside the site: delivered mail, exports someone saved, content a content action
created, or what webhook receivers keep. [The checklist](privacy/checklist.md#when-a-person-asks) covers answering a
request step by step.

## Legal holds

**Set legal hold…** in Privacy › People keeps chosen entries from every delete, retention and erasure included, for a
reason and until a date at most a year away, which can be extended. The entry shows the hold; **Release** ends it. A
form with held entries can't be deleted for good.

## Members who are deleted

**Privacy › Data collection › Members** says what happens to a member's entries when the member is deleted in Umbraco:
**Anonymise their entries** (the default), which keeps them in their forms and counts without anything about the
member, or **Delete their entries**. Their entries in every form are treated the same; entries on a legal hold stay as
they are ([members.md](members.md)).

## The audit log

**Activity › Audit log** records who did what, when and where: reveals of sensitive answers, exports, deletions,
changes of state, settings and permission changes, legal holds, erasures and retention. It names the fields a change
touched but never their values, and never an email address. Filter it by kind, time and person, and export it as CSV.
It is kept for a year.

## On the visitor's device

- **No cookies,** from any response of Jiron Form Builder.
- **No browser storage.**
- **Requests go only to the site itself,** for the form's script and stylesheet and its API.
- **Insights** count what visitors do with a form without a cookie or an identifier, and can be turned off
  ([insights.md](insights.md)).
- **A captcha,** where an editor chooses one, loads from its provider only once the visitor agrees, and may set its own
  cookies ([spam-protection.md](spam-protection.md#consent)).

[The privacy facts](privacy/facts.md#on-the-visitors-device) list every request, and a test keeps that list true.

**The privacy health check** on the Overview warns while the site keeps full IP addresses, keeps a state for good, or
loads a captcha without asking.

## Who may do what

- **Use the privacy tools:** Privacy › People, for every form.
- **Manage settings:** changing retention, Privacy › Data collection, and the audit log. Users with the privacy tools
  can see Retention without changing it.
- **View sensitive data:** revealing sensitive answers, per form.
