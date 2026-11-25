# Security policy

Jiron Form Builder handles what visitors type into forms, files they upload and mail that goes out in a
site's name, so security reports are taken seriously. Thank you for reporting responsibly.

## How to report a vulnerability

Please report privately, never in a public issue, discussion or pull request.

- **On GitHub:** use private vulnerability reporting ("Report a vulnerability" on the repository's
  **Security** tab).
- **Or by email:** write to **info@ligata.ch** with "Security: Jiron Form Builder" in the subject.

Please include:

- the Jiron Form Builder version, the Umbraco version and the database (SQLite or SQL Server);
- what an attacker can do, and what they need first (an account, a role, a particular setting);
- the steps or a small proof of concept that shows it;
- whether you have told anyone else.

Never send real personal data: no visitors' entries, uploaded files or email addresses. Make up test data
instead.

## What is in scope

- The packages built from this repository: `Jiron.Umbraco.FormBuilder`, `Jiron.Umbraco.FormBuilder.Core`,
  `Jiron.Umbraco.FormBuilder.Web` and the optional `Jiron.Umbraco.FormBuilder.AI`.
- Their public endpoints under `/jfb/api/v1`, the management endpoints under
  `/umbraco/jfb/management/api/v1`, the website runtime and the `<jfb-form>` element, the backoffice
  extension, the rendered markup, the emails they send and the files they store.
- The JSON schemas and the documented extension points.

## What is out of scope

- Umbraco CMS itself: report those to Umbraco HQ through their own security process.
- A site's own configuration choices that the documentation warns against, such as turning off the spam
  protection or allowing webhooks to any host.
- Third-party captcha services and mail servers.
- The demo site and the test tools in this repository, which are not meant to run in production. Reports
  about them are still welcome as ordinary issues.

## What happens next

These are goals, not promises; Jiron Form Builder is maintained by one person.

- **Acknowledgement** within 5 working days.
- **A first assessment** (confirmed or not, and how severe) within 14 days.
- **A fix** as fast as the severity calls for, in the supported line below, followed by a release note that
  credits you if you want to be named.

Please give us a reasonable time to ship a fix before you publish anything, and we will keep you informed
along the way.

## Supported versions

| Line | Umbraco | Supported |
| --- | --- | --- |
| 17.x | 17 LTS | the latest release and prerelease |
| 18.x | 18 | the latest release and prerelease, once the line exists |

Older prereleases are not patched; upgrade to the latest one.
