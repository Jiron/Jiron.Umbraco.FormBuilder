using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Web.Setup;

/// <summary>
/// The sentences of the health checks, in the backoffice's two languages (ADR-0165). Umbraco's Health Check dashboard shows what
/// the server wrote as it is, so they are translated here, and each language handles its own plurals.
/// </summary>
internal sealed class HealthWords
{
    private readonly bool _german;
    private readonly bool _namesSites;

    private HealthWords(bool german, bool namesSites)
    {
        _german = german;
        _namesSites = namesSites;
    }

    /// <summary>Whether these are the German texts.</summary>
    public bool German => _german;

    /// <summary>The texts for a culture such as <c>de-CH</c>; English unless it is German.</summary>
    /// <param name="culture">The user's language, or null.</param>
    /// <param name="namesSites">Whether a sentence may name a site's root content; a user who may not read it hears of "a site".</param>
    public static HealthWords Of(string? culture, bool namesSites = true) => new(culture?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true, namesSites);

    /// <summary>Umbraco has no mail server.</summary>
    public string EmailNoServer => _german
        ? "E-Mails k\u00f6nnen nicht gesendet werden, weil keine Mailserver-Verbindung eingerichtet ist. Sie wird unter Umbraco:CMS:Global:Smtp festgelegt."
        : "Emails can't be sent because no mail server is set up. It is set under Umbraco:CMS:Global:Smtp.";

    /// <summary>Mail has no address to come from.</summary>
    public string EmailNoFrom => _german
        ? "E-Mails haben keine Absenderadresse. Tragen Sie eine in den E-Mail-Einstellungen ein."
        : "Emails have no From address. Add one in the email settings.";

    /// <summary>Mail is set up.</summary>
    public string EmailPassed => _german ? "E-Mails k\u00f6nnen gesendet werden und haben eine Absenderadresse." : "Emails can be sent and have a From address.";

    /// <summary>Uploads lie where the website serves files from.</summary>
    public string UploadsReachable => _german
        ? "Hochgeladene Dateien liegen in einem Ordner, den die Website ausliefert. W\u00e4hlen Sie unter JironFormBuilder:Files:Root einen Ordner au\u00dferhalb des Web-Stammverzeichnisses."
        : "Uploaded files are in a folder the website serves. Choose a folder outside the web root under JironFormBuilder:Files:Root.";

    /// <summary>Uploads lie on each server's own disk.</summary>
    public string UploadsOnEachServer => _german
        ? "Hochgeladene Dateien liegen auf der Festplatte des jeweiligen Servers, die anderen Server sehen sie nicht. W\u00e4hlen Sie unter JironFormBuilder:Files:Root einen gemeinsamen Ordner."
        : "Uploaded files are on each server's own disk, so the other servers can't see them. Choose a shared folder under JironFormBuilder:Files:Root.";

    /// <summary>Uploads are safe.</summary>
    public string UploadsPassed => _german ? "Hochgeladene Dateien liegen in einem Ordner, den die Website nicht ausliefert." : "Uploaded files are in a folder the website doesn't serve.";

    /// <summary>The keys are only in memory.</summary>
    public string KeyRingInMemory => _german
        ? "Die Data-Protection-Schl\u00fcssel liegen nur im Arbeitsspeicher, also werden offene Formulare und Links bei jedem Neustart ung\u00fcltig. Richten Sie einen dauerhaften Speicher ein (PersistKeysToFileSystem oder \u00e4hnlich)."
        : "The Data Protection keys are kept in memory only, so open forms and links stop working at every restart. Set up a lasting store (PersistKeysToFileSystem or similar).";

    /// <summary>The keys are inside a container.</summary>
    public string KeyRingInContainer => _german
        ? "Die Data-Protection-Schl\u00fcssel liegen im Container und gehen mit ihm verloren, also werden offene Formulare und Links bei jedem Neustart ung\u00fcltig. Richten Sie einen dauerhaften Speicher ein (PersistKeysToFileSystem auf einem Volume oder \u00e4hnlich)."
        : "The Data Protection keys are inside the container and go with it, so open forms and links stop working at every restart. Set up a lasting store (PersistKeysToFileSystem on a volume or similar).";

    /// <summary>The keys are on each server's own disk.</summary>
    public string KeyRingOnEachServer => _german
        ? "Jeder Server hat seine eigenen Data-Protection-Schl\u00fcssel, also scheitern Formulare und Links, die ein anderer Server begonnen hat. Richten Sie einen gemeinsamen Speicher ein (PersistKeysToFileSystem oder \u00e4hnlich)."
        : "Each server has its own Data Protection keys, so forms and links another server started fail. Set up a shared store (PersistKeysToFileSystem or similar).";

    /// <summary>The keys last.</summary>
    public string KeyRingPassed => _german ? "Die Data-Protection-Schl\u00fcssel bleiben \u00fcber Neustarts hinweg erhalten." : "The Data Protection keys last across restarts.";

    /// <summary>The jobs have stopped.</summary>
    public string JobsStopped => _german
        ? "Die Hintergrundaufgaben laufen seit \u00fcber 15 Minuten nicht mehr, also werden keine E-Mails und Aktionen ausgef\u00fchrt. Pr\u00fcfen Sie das Protokoll der Website."
        : "The background jobs haven't run for over 15 minutes, so no emails or actions go out. Check the site's log.";

    /// <summary>The jobs run.</summary>
    public string JobsPassed => _german ? "Die Hintergrundaufgaben laufen." : "The background jobs are running.";

    /// <summary>A host's switch holds sending back.</summary>
    public string SendingPaused => _german
        ? "Das Senden ist angehalten (JironFormBuilder:Actions:Paused). Nichts geht verloren: Alles wird gesendet, sobald der Schalter aus ist."
        : "Sending is paused (JironFormBuilder:Actions:Paused). Nothing is lost: it all goes out once the switch is off.";

    /// <summary>Mail failed for good.</summary>
    /// <param name="count">How many mails.</param>
    public string EmailsFailing(int count) => _german
        ? (count == 1 ? "1 E-Mail konnte in den letzten 24 Stunden nicht gesendet werden." : Format("{0} E-Mails konnten in den letzten 24 Stunden nicht gesendet werden.", count))
        : (count == 1 ? "1 email couldn't be sent in the last 24 hours." : Format("{0} emails couldn't be sent in the last 24 hours.", count));

    /// <summary>Actions failed for good.</summary>
    /// <param name="count">How many runs.</param>
    public string ActionsFailing(int count) => _german
        ? (count == 1 ? "1 Aktion ist in den letzten 24 Stunden fehlgeschlagen." : Format("{0} Aktionen sind in den letzten 24 Stunden fehlgeschlagen.", count))
        : (count == 1 ? "1 action failed in the last 24 hours." : Format("{0} actions failed in the last 24 hours.", count));

    /// <summary>Deliveries go out.</summary>
    public string DeliveriesPassed => _german ? "E-Mails und Aktionen werden zugestellt." : "Emails and actions are going out.";

    /// <summary>The migrations are pending.</summary>
    public string MigrationsPending => _german
        ? "Jiron Form Builder ist angehalten, weil seine Datenbank-Migrationen ausstehen: Besucher sehen keine Formulare und nichts wird gesendet. F\u00fchren Sie die Migrationen im Bereich Pakete aus."
        : "Jiron Form Builder is paused because its database migrations are pending: visitors see no forms and nothing goes out. Run them in the Packages section.";

    /// <summary>The database is current.</summary>
    public string MigrationsPassed => _german ? "Die Datenbank ist auf dem neuesten Stand." : "The database is up to date.";

    /// <summary>A site has no address for its links.</summary>
    /// <param name="site">The site's name.</param>
    public string SiteNoBaseUrl(string site) => !_namesSites
        ? (_german
            ? "Eine Website hat keine Basis-URL, also fehlen die Links in ihren E-Mails. Sie wird in den allgemeinen Einstellungen eingetragen."
            : "A site has no base URL, so its emails carry no links. It is set in the general settings.")
        : _german
        ? Format("Die Website {0} hat keine Basis-URL, also fehlen die Links in ihren E-Mails. Tragen Sie eine in den allgemeinen Einstellungen ein oder weisen Sie ihr eine https-Domain zu.", site)
        : Format("The site {0} has no base URL, so its emails carry no links. Add one in the general settings, or give it an https domain.", site);

    /// <summary>Every site has an address.</summary>
    public string BaseUrlPassed => _german ? "Jede Website hat eine Basis-URL f\u00fcr die Links in E-Mails." : "Every site has a base URL for the links in its emails.";

    /// <summary>The "used on" index is current.</summary>
    public string UsedOnPassed => _german ? "Der Index \u201eVerwendet auf\u201c ist aktuell." : "The \"used on\" index is up to date.";

    /// <summary>A rebuild is under way.</summary>
    public string UsedOnRebuilding => _german
        ? "Der Index \u201eVerwendet auf\u201c wird neu aufgebaut. Bis dahin kann die Liste der Seiten eines Formulars unvollst\u00e4ndig sein."
        : "The \"used on\" index is being rebuilt. Until it is done, the list of a form's pages may be incomplete.";

    /// <summary>The name of the action that asks for a rebuild.</summary>
    public string UsedOnRebuild => _german ? "Index neu aufbauen" : "Rebuild the index";

    /// <summary>What the action does.</summary>
    public string UsedOnRebuildHelp => _german
        ? "Geht alle Seiten im Hintergrund durch und schreibt, wo jedes Formular verwendet wird. Die Website bleibt dabei erreichbar."
        : "Goes through every page in the background and writes where each form is used. The site stays up meanwhile.";

    /// <summary>A form's file and the form both changed: the site kept the form.</summary>
    /// <param name="file">The file's name.</param>
    public string MirrorConflict(string file) => _german
        ? Format("{0}: Die Datei und das Formular wurden beide ge\u00e4ndert, also hat die Website das Formular behalten und die Datei gelassen, wie sie ist. \u00dcbernehmen Sie die Datei mit \u201eAus einer Datei ersetzen\u2026\u201c und ver\u00f6ffentlichen Sie, oder schreiben Sie die Datei mit der Aktion der Pr\u00fcfung \u201eForms as files\u201c neu.", file)
        : Format("{0}: the file and the form both changed, so the site kept the form and left the file as it is. Take the file in with Replace from a file\u2026 and publish, or write the file again with the action of the Forms as files check.", file);

    /// <summary>A form's file could not be written.</summary>
    /// <param name="file">The file's name.</param>
    public string MirrorUnwritable(string file) => _german
        ? Format("{0} konnte nicht in den Ordner der Formulardateien geschrieben werden. Das Protokoll der Website nennt den Grund.", file)
        : Format("{0} couldn't be written to the forms folder. The site's log says why.", file);

    /// <summary>A file of the folder is no form file the site can take in.</summary>
    /// <param name="file">The file's name.</param>
    public string MirrorUnreadable(string file) => _german
        ? Format("{0} im Ordner der Formulardateien ist keine Formulardatei, die die Website \u00fcbernehmen kann. Das Protokoll der Website nennt den Grund.", file)
        : Format("{0} in the forms folder isn't a form file the site can take in. The site's log says why.", file);

    /// <summary>A changed file went into its form's draft, but publishing refused it.</summary>
    /// <param name="file">The file's name.</param>
    public string MirrorNotPublished(string file) => _german
        ? Format("{0} ist im Entwurf seines Formulars, aber das Formular lie\u00df sich nicht ver\u00f6ffentlichen. \u00d6ffnen Sie es, beheben Sie, was die Ver\u00f6ffentlichung nennt, und ver\u00f6ffentlichen Sie es.", file)
        : Format("{0} is in its form's draft, but the form couldn't be published. Open it, fix what publishing names, and publish it.", file);

    /// <summary>Every published form is in step with its file.</summary>
    public string MirrorPassed => _german ? "Jedes ver\u00f6ffentlichte Formular stimmt mit seiner Datei \u00fcberein." : "Every published form is in step with its file.";

    /// <summary>The name of the action that writes the files of the forms in conflict.</summary>
    public string MirrorKeep => _german ? "Dateien neu schreiben" : "Write the files again";

    /// <summary>What the action does.</summary>
    public string MirrorKeepHelp => _german
        ? "Schreibt die ver\u00f6ffentlichte Version jedes Formulars im Konflikt in seine Datei, \u00fcber die \u00c4nderung der Datei hinweg."
        : "Writes the published version of each form in conflict to its file, over the file's own change.";

    /// <summary>What the action did.</summary>
    /// <param name="count">How many files it wrote.</param>
    public string MirrorKept(int count) => _german
        ? (count == 1 ? "Die Datei eines Formulars wurde neu geschrieben." : Format("Die Dateien von {0} Formularen wurden neu geschrieben.", count))
        : count == 1 ? "The file of one form was written again." : Format("The files of {0} forms were written again.", count);

    /// <summary>The mirror is off.</summary>
    public string MirrorOff => _german ? "Ver\u00f6ffentlichte Formulare werden nicht als Dateien geschrieben." : "Published forms aren't written to files.";

    /// <summary>The site's entries keep full IP addresses.</summary>
    public string FullIp => _german
        ? "Eintr\u00e4ge speichern die vollst\u00e4ndigen IP-Adressen der Besucher, was die Konfiguration erlaubt (JironFormBuilder:Privacy:AllowFullIp). Speichern Sie sie unter Datenschutz \u203a Datenerfassung anonymisiert, wenn Sie sie nicht ganz brauchen."
        : "Entries keep visitors' full IP addresses, which the configuration allows (JironFormBuilder:Privacy:AllowFullIp). Keep them anonymised under Privacy \u203a Data collection unless you need them whole.";

    /// <summary>Forms of their own keep full IP addresses.</summary>
    /// <param name="count">How many live forms.</param>
    public string FullIpForms(int count) => _german
        ? (count == 1
            ? "Ein Formular speichert die vollst\u00e4ndigen IP-Adressen der Besucher, was die Konfiguration erlaubt (JironFormBuilder:Privacy:AllowFullIp). Jedes Formular legt das in seiner Ansicht Einstellungen unter Datenschutz fest."
            : Format("{0} Formulare speichern die vollst\u00e4ndigen IP-Adressen der Besucher, was die Konfiguration erlaubt (JironFormBuilder:Privacy:AllowFullIp). Jedes Formular legt das in seiner Ansicht Einstellungen unter Datenschutz fest.", count))
        : count == 1
            ? "One form keeps visitors' full IP addresses, which the configuration allows (JironFormBuilder:Privacy:AllowFullIp). Each form sets it in its Settings view, under Privacy."
            : Format("{0} forms keep visitors' full IP addresses, which the configuration allows (JironFormBuilder:Privacy:AllowFullIp). Each form sets it in its Settings view, under Privacy.", count);

    /// <summary>Entries of a state are kept for good.</summary>
    public string RetentionOff => _german
        ? "Eintr\u00e4ge in manchen Status werden unbegrenzt aufbewahrt, was die Konfiguration erlaubt (JironFormBuilder:Privacy:AllowRetentionOff). Legen Sie ihre Tage unter Datenschutz \u203a Aufbewahrung fest."
        : "Entries in some states are kept for good, which the configuration allows (JironFormBuilder:Privacy:AllowRetentionOff). Set their days under Privacy \u203a Retention.";

    /// <summary>A captcha loads before visitors are asked.</summary>
    public string CaptchaUnasked => _german
        ? "Ein Captcha l\u00e4dt, ohne die Besucher vorher zu fragen, was die Konfiguration erlaubt (JironFormBuilder:Privacy:AllowImplicitCaptchaConsent). W\u00e4hlen Sie in den Spamschutz-Einstellungen eine andere Art der Einwilligung."
        : "A captcha loads without asking visitors first, which the configuration allows (JironFormBuilder:Privacy:AllowImplicitCaptchaConsent). Choose another way to ask in the spam protection settings.";

    /// <summary>Nothing keeps more of visitors than the defaults.</summary>
    public string PrivacyPassed => _german
        ? "Eintr\u00e4ge speichern keine vollst\u00e4ndigen IP-Adressen, jeder Status hat eine Aufbewahrungsfrist, und Captchas fragen vorher."
        : "Entries keep no full IP addresses, every state has a retention period, and captchas ask first.";

    /// <summary>A check itself failed.</summary>
    public string CheckFailed => _german ? "Eine Pr\u00fcfung konnte nicht abgeschlossen werden. Das Protokoll der Website nennt den Grund." : "A check couldn't finish. The site's log says why.";

    private static string Format(string text, params object[] values) => string.Format(CultureInfo.InvariantCulture, text, values);
}
