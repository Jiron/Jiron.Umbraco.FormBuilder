# Files

What happens to the files visitors upload: what they may upload, how each file is checked, where it is kept, who can
download it and when it goes. A form takes files through a [file upload field](field-types.md#file-upload).

## Contents

- [What visitors may upload](#what-visitors-may-upload)
- [How each file is checked](#how-each-file-is-checked)
- [Where files are kept](#where-files-are-kept)
- [Malware scanners](#malware-scanners)
- [Downloading files](#downloading-files)
- [When files go](#when-files-go)
- [For developers](#for-developers)

## What visitors may upload

**Settings › Files**, for all sites:

- **Allowed types:** the groups each file field may pick from, all on by default.

  | Group | Extensions |
  | --- | --- |
  | Images | jpg, jpeg, png, gif, webp, avif, heic, heif |
  | PDF | pdf |
  | Office documents | doc, docx, xls, xlsx, ppt, pptx, odt, ods, odp, rtf |
  | Text | txt, csv |
  | Archives | zip, 7z, rar, tar, gz |

- **Largest file,** in MB: each file field sets its own limit, up to this one. The site's configuration sets the
  ceiling, 50 MB unless a developer changes it.

Each file field then chooses its types, any further extensions such as `dwg`, its largest file and how many files it
takes (one by default, up to 20). The form check suggests a size for a field that takes files up to the site's
limit, as a new file field does, so you give it the size its answers need, and warns when the site no longer takes a
size or type the field allows.

**Never accepted,** whatever a field allows: programs, scripts and web pages, by their extension. The list, which a
developer can replace with `JironFormBuilder:Files:BlockedExtensions`, is: exe, dll, js, mjs, bat, cmd, ps1, sh, msi,
msp, scr, pif, cpl, jar, vbs, wsf, hta, lnk, reg, html, htm, xhtml, shtml, svg, php, phtml, asp, aspx, jsp and cgi.
An extension counts anywhere after the first part of the name, so `report.exe.pdf` is refused too. SVG pictures are
never images here, as they can carry scripts.

## How each file is checked

- **Its contents must match its type,** whatever its name says: a file named `photo.jpg` must start like a picture. A
  program is refused under any name, and so is a picture with an archive or a web page hidden inside.
- **Its size** is checked as it arrives; the site never reads past the limit. A server or proxy in front of the site
  with a lower limit of its own refuses larger files first, such as IIS's `maxAllowedContentLength`.
- **Its name** is cleaned of characters that could make it pose as another type.
- The visitor's browser checks the type and size first, so a visitor learns at once; the site checks again, whatever a
  browser says.

Each file is uploaded as soon as the visitor chooses it, and waits for the entry. The site limits how much may wait,
in all and from one address, so uploads can't fill its disk.

## Where files are kept

In a **private folder outside the website**, never in Umbraco's media: `umbraco/Data/JironFormBuilder/Uploads` unless
the configuration names another with `JironFormBuilder:Files:Root`. Files there have no public address and no
extension, under random names. The site refuses to start if the folder lies inside the web root.

**On a site with several servers,** each server's own disk can't see the others' files. Use a folder every server
shares, or a blob store ([below](#for-developers)). The **Uploaded files** health check warns when files are on each
server's own disk, or in a folder the website serves.

## Malware scanners

The site can check each file for malware before anyone downloads it, with a scanner a developer adds. Settings › Files
says whether one is installed.

- Files are scanned in the background, never while the visitor waits.
- While a file waits for its scan, nobody can download it, and emails leave it out.
- A file a scanner flags is **quarantined**: the entry's timeline says so, and nobody, administrators included, can
  download it again. The entry itself stays as it was.

Without a scanner, files are not checked for malware; say so to the people who open them.

## Downloading files

Files are downloaded only from their entry in the backoffice, by users who may view the form's entries. A sensitive
field's file also needs **View sensitive data**, and each such download is recorded in the audit log. Every file
downloads as an attachment, never opened in the backoffice's own page.

**Export… › ZIP of the uploaded files** prepares a form's files in the background, in a folder per entry, and offers
the download once it is ready, for users who may
export entries ([entries-and-exports.md](entries-and-exports.md#exports)).

## When files go

- **With their entry:** deleting an entry deletes its files, whether a user deletes it, retention does, or the privacy
  tools erase it ([privacy.md](privacy.md)).
- **Uploads no entry took,** because a visitor chose a file and never sent the form, go after 24 hours.
- Leftovers of interrupted uploads are swept away once a day.

## For developers

- **Blob storage:** the site can keep files elsewhere by registering its own store,
  `builder.Services.AddUnique<IFormBuilderFileStore, YourStore>()`. The samples hold one on Umbraco's official Azure
  Blob Storage provider, with a private container ([extending.md](extending.md#file-stores)). A store of your own
  cleans up its own leftovers.
- **Scanners:** `builder.JironFormBuilder().FileScanners()` adds one; each answers clean, quarantine or unavailable for
  a file. The samples include one that quarantines test files named "eicar"
  ([extending.md](extending.md#captchas-spam-signals-and-file-scanners)).
