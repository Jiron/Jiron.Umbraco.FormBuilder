// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The upgrade test's seed (ROADMAP 16.5, ADR-0240). scripts/install-test.ps1 adds this project to a site that runs the
// BASELINE packages (the tag baseline/schema-v1, packed as 17.0.0-alpha.0.baseline) and removes it before the upgrade;
// it is never part of a package. With JFB_INSTALL_SEED set to a file path it writes, once the site has started (the
// baseline's migrations ran unattended before), what a site of that time could hold, through the baseline's own
// services: the general settings; a folder with a folder inside it; a form in it published twice and changed again in
// its draft; a second form at the root; entries on both forms' published versions with values of every storage kind
// and a file, one of them approved. The baseline had no submission pipeline, so the entries go in through its entry
// repository, as its later pipeline stores them. The file at the path receives what the checks after the upgrade read
// back: every key, name, value and the file's hash, or "error: ..." when the seed failed.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace JfbInstallTest;

/// <summary>Adds the seed when the install test asks for it.</summary>
public sealed class BaselineSeedComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JFB_INSTALL_SEED")))
        {
            builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, BaselineSeed>();
        }
    }
}

/// <summary>Writes the baseline's data once the site has started.</summary>
internal sealed class BaselineSeed(
    IFormBuilderSettingsService settings,
    IFormBuilderFolderService folders,
    IFormBuilderFormService forms,
    IFormRepository formRows,
    IEntryRepository entries,
    IFormBuilderFileStore files,
    FormDefinitionSerializer serializer) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    // The fields' keys are fixed, so the checks after the upgrade can name them.
    private const string Name = "a1000000-0000-4000-8000-000000000001";
    private const string Email = "a1000000-0000-4000-8000-000000000002";
    private const string Guests = "a1000000-0000-4000-8000-000000000003";
    private const string Arrival = "a1000000-0000-4000-8000-000000000004";
    private const string Room = "a1000000-0000-4000-8000-000000000005";
    private const string Newsletter = "a1000000-0000-4000-8000-000000000006";
    private const string Message = "a1000000-0000-4000-8000-000000000007";
    private const string Plan = "a1000000-0000-4000-8000-000000000008";
    private const string Phone = "a1000000-0000-4000-8000-000000000009";
    private const string Score = "a2000000-0000-4000-8000-000000000001";
    private const string Remarks = "a2000000-0000-4000-8000-000000000002";

    private static readonly Guid Admin = Constants.Security.SuperUserKey;

    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <inheritdoc />
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("JFB_INSTALL_SEED")!;
        try
        {
            var written = await SeedAsync(cancellationToken);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(written, Output), cancellationToken);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(output, "error: " + exception, CancellationToken.None);
        }
    }

    private async Task<object> SeedAsync(CancellationToken cancellationToken)
    {
        // The general settings, saved over the defaults that the baseline's second step wrote.
        var general = await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken);
        await settings.SaveAsync(
            general.Value with { TimeZone = "Europe/Berlin", History = new HistorySettings { DraftDays = 120, DraftCount = 40 } },
            general.Version,
            Admin,
            cancellationToken);

        var events = await folders.CreateAsync(new NewFolderRequest("Upgrade events", null, Admin), cancellationToken);
        var archive = await folders.CreateAsync(new NewFolderRequest("Archive", events.Key, Admin), cancellationToken);

        // The booking form: published, published again with a phone field, then changed in its draft only.
        var booking = await forms.CreateAsync(new NewFormRequest("Upgrade booking", "upgradeBooking", events.Key, Admin), cancellationToken);
        var info = await SaveAsync(booking, Booking(booking.Key, phone: false, message: "Message"), "The first questions", cancellationToken);
        info = (await forms.PublishAsync(booking.Key, info.ConcurrencyVersion, "Open for bookings", Admin, cancellationToken))!.Form;
        var firstVersionId = (await formRows.GetAsync(booking.Key, cancellationToken))!.PublishedVersionId!.Value;
        info = await SaveAsync(info, Booking(booking.Key, phone: true, message: "Message"), "A phone number", cancellationToken);
        info = (await forms.PublishAsync(booking.Key, info.ConcurrencyVersion, "With a phone number", Admin, cancellationToken))!.Form;
        var bookingRow = (await formRows.GetAsync(booking.Key, cancellationToken))!;
        var secondVersionId = bookingRow.PublishedVersionId!.Value;
        await SaveAsync(info, Booking(booking.Key, phone: true, message: "Anything else?"), "A friendlier question", cancellationToken);

        // The feedback form, at the root.
        var feedback = await forms.CreateAsync(new NewFormRequest("Upgrade feedback", "upgradeFeedback", null, Admin), cancellationToken);
        var feedbackInfo = await SaveAsync(feedback, Feedback(feedback.Key), null, cancellationToken);
        await forms.PublishAsync(feedback.Key, feedbackInfo.ConcurrencyVersion, null, Admin, cancellationToken);
        var feedbackRow = (await formRows.GetAsync(feedback.Key, cancellationToken))!;

        var bookingFields = await FieldsAsync(bookingRow.Id, cancellationToken);
        var feedbackFields = await FieldsAsync(feedbackRow.Id, cancellationToken);

        // The plan a visitor sent with the first booking, kept in the baseline's file store.
        var planBytes = Encoding.UTF8.GetBytes("%PDF-1.4\n% The plan of the upgrade test's first booking.\n%%EOF\n");
        var storedName = Guid.NewGuid().ToString("N");
        using (var content = new MemoryStream(planBytes))
        {
            await files.SaveAsync(storedName, content, cancellationToken);
        }

        var planHash = Convert.ToHexStringLower(SHA256.HashData(planBytes));
        var plan = (await entries.AddFileAsync(
            new NewEntryFile(Guid.NewGuid(), bookingRow.Id, Guid.Parse(Plan), "plan.pdf", storedName, "application/pdf", planBytes.Length, planHash, "unscanned"),
            cancellationToken))!;

        var seeded = new List<JsonObject>();
        async Task<(long Id, JsonObject Record)> EntryAsync(string form, int formId, int versionId, IReadOnlyDictionary<string, int> fields, (string Alias, NewEntryValue Value)[] values, IReadOnlyList<int>? fileIds = null)
        {
            var stored = (await entries.CreateAsync(
                new NewEntry
                {
                    Key = Guid.NewGuid(),
                    FormId = formId,
                    FormVersionId = versionId,
                    SubmissionId = Guid.NewGuid(),
                    SubmissionHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()))),
                    Culture = "en-US",
                    PageUrl = "/book/",
                    SearchText = EntrySearchText.For(values.Where(v => v.Value.FileId is null).Select(v => v.Value.Text!)),
                    DurationSeconds = 95,
                    Values = [.. values.Select(v => v.Value with { FieldId = fields[v.Alias] })],
                    FileIds = fileIds ?? [],
                },
                cancellationToken))!;
            var record = new JsonObject
            {
                ["form"] = form,
                ["key"] = stored.Key,
                ["number"] = stored.Number,
                ["state"] = "submitted",
                ["values"] = new JsonObject(values.Select(v => KeyValuePair.Create(v.Alias, (JsonNode?)v.Value.Text))),
            };
            seeded.Add(record);
            return (stored.Id, record);
        }

        static (string, NewEntryValue) Text(string alias, string text) => (alias, new NewEntryValue(0, text));

        var first = await EntryAsync("upgradeBooking", bookingRow.Id, firstVersionId, bookingFields,
        [
            Text("name", "Ada Upgrade"),
            Text("email", "ada@upgrade.jfb.test"),
            ("guests", new NewEntryValue(0, "2", Number: 2)),
            ("arrival", new NewEntryValue(0, "2026-12-24", Date: new DateTime(2026, 12, 24, 0, 0, 0, DateTimeKind.Utc))),
            Text("room", "double"),
            ("newsletter", new NewEntryValue(0, "true", Bool: true)),
            Text("message", "A quiet room, please."),
            ("plan", new NewEntryValue(0, plan.Key.ToString("D"), FileId: plan.Id)),
        ],
        [plan.Id]);
        var second = await EntryAsync("upgradeBooking", bookingRow.Id, firstVersionId, bookingFields,
        [
            Text("name", "Bruno Baseline"),
            Text("email", "bruno@upgrade.jfb.test"),
            ("guests", new NewEntryValue(0, "4", Number: 4)),
            Text("room", "suite"),
            ("newsletter", new NewEntryValue(0, "false", Bool: false)),
        ]);
        await EntryAsync("upgradeBooking", bookingRow.Id, secondVersionId, bookingFields,
        [
            Text("name", "Chlo\u00e9 Schema"),
            Text("email", "chloe@upgrade.jfb.test"),
            Text("phone", "+49 30 1234567"),
            ("guests", new NewEntryValue(0, "1", Number: 1)),
            Text("room", "single"),
            Text("message", "Late arrival, around 22:00."),
        ]);
        await EntryAsync("upgradeFeedback", feedbackRow.Id, feedbackRow.PublishedVersionId!.Value, feedbackFields,
        [
            ("score", new NewEntryValue(0, "4", Number: 4)),
            Text("remarks", "Smooth upgrade."),
        ]);

        // The second booking was approved.
        await entries.ChangeStateAsync(second.Id, EntryState.Submitted, EntryState.Approved, Admin, cancellationToken);
        second.Record["state"] = "approved";

        return new
        {
            settings = new { timeZone = "Europe/Berlin", draftDays = 120, draftCount = 40 },
            folders = new[]
            {
                new { key = events.Key, name = events.Name, parentKey = (Guid?)null },
                new { key = archive.Key, name = archive.Name, parentKey = (Guid?)events.Key },
            },
            forms = new[]
            {
                new { key = booking.Key, alias = "upgradeBooking", name = "Upgrade booking", folderKey = (Guid?)events.Key, publishedVersions = 2, draftMessageLabel = "Anything else?", publishedMessageLabel = "Message" },
                new { key = feedback.Key, alias = "upgradeFeedback", name = "Upgrade feedback", folderKey = (Guid?)null, publishedVersions = 1, draftMessageLabel = (string)null!, publishedMessageLabel = (string)null! },
            },
            entries = seeded,
            file = new { key = plan.Key, name = "plan.pdf", size = planBytes.Length, sha256 = planHash, entry = first.Record["key"]!.GetValue<Guid>(), form = "upgradeBooking" },
        };
    }

    private async Task<FormInfo> SaveAsync(FormInfo form, string definitionJson, string? comment, CancellationToken cancellationToken)
    {
        var read = serializer.Read(definitionJson);
        if (read.Definition is null)
        {
            throw new InvalidOperationException("The seed's definition does not read: " + string.Join("; ", read.Problems.Select(p => $"{p.Path} {p.Code}")));
        }

        var saved = await forms.SaveDraftAsync(form.Key, form.ConcurrencyVersion, read.Definition, comment, Admin, cancellationToken)
            ?? throw new InvalidOperationException($"The draft of {form.Alias} was not saved.");
        return saved.Form;
    }

    private async Task<IReadOnlyDictionary<string, int>> FieldsAsync(int formId, CancellationToken cancellationToken) =>
        (await formRows.ListFieldsAsync(formId, cancellationToken)).ToDictionary(field => field.Values.Alias, field => field.Id, StringComparer.Ordinal);

    private static string Booking(Guid key, bool phone, string message)
    {
        var phoneField = phone ? $$"""{ "key": "{{Phone}}", "type": "phone", "alias": "phone", "label": { "": "Phone" } },""" : string.Empty;
        return $$"""
        {
          "schemaVersion": 1,
          "key": "{{key}}",
          "pages": [
            {
              "key": "b1000000-0000-4000-8000-000000000001",
              "title": { "": "Your stay" },
              "groups": [
                {
                  "key": "b1000000-0000-4000-8000-000000000002",
                  "fields": [
                    { "key": "{{Name}}", "type": "text", "alias": "name", "label": { "": "Name", "de": "Name" }, "required": true, "autocomplete": "name" },
                    { "key": "{{Email}}", "type": "email", "alias": "email", "label": { "": "Email address", "de": "E-Mail-Adresse" }, "required": true, "autocomplete": "email" },
                    {{phoneField}}
                    { "key": "{{Guests}}", "type": "number", "alias": "guests", "label": { "": "Guests" }, "required": true, "settings": { "min": "1", "max": "8" } },
                    { "key": "{{Arrival}}", "type": "date", "alias": "arrival", "label": { "": "Arrival" } },
                    {
                      "key": "{{Room}}",
                      "type": "dropdown",
                      "alias": "room",
                      "label": { "": "Room" },
                      "required": true,
                      "options": {
                        "source": "items",
                        "items": [
                          { "value": "single", "label": { "": "Single" } },
                          { "value": "double", "label": { "": "Double" } },
                          { "value": "suite", "label": { "": "Suite" } }
                        ]
                      }
                    },
                    { "key": "{{Newsletter}}", "type": "checkbox", "alias": "newsletter", "label": { "": "Send me the newsletter" } },
                    { "key": "{{Message}}", "type": "textarea", "alias": "message", "label": { "": "{{message}}" }, "settings": { "rows": 4 } },
                    { "key": "{{Plan}}", "type": "file", "alias": "plan", "label": { "": "Your plan" }, "settings": { "maxFiles": 1 } }
                  ]
                }
              ]
            }
          ],
          "outcomes": [ { "key": "b1000000-0000-4000-8000-000000000003", "kind": "message" } ]
        }
        """;
    }

    private static string Feedback(Guid key) => $$"""
        {
          "schemaVersion": 1,
          "key": "{{key}}",
          "pages": [
            {
              "key": "b2000000-0000-4000-8000-000000000001",
              "groups": [
                {
                  "key": "b2000000-0000-4000-8000-000000000002",
                  "fields": [
                    { "key": "{{Score}}", "type": "scale", "alias": "score", "label": { "": "How was it?" }, "settings": { "from": 1, "to": 5 } },
                    { "key": "{{Remarks}}", "type": "textarea", "alias": "remarks", "label": { "": "Remarks" } }
                  ]
                }
              ]
            }
          ],
          "outcomes": [ { "key": "b2000000-0000-4000-8000-000000000003", "kind": "message" } ]
        }
        """;
}
