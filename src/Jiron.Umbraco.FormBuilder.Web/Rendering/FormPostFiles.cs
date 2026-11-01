using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Reads a post without JavaScript of a form that stores files (ADR-0006, ADR-0102) before Umbraco's routing reads it:
/// a post whose action names the form (<c>?jfb-post=&lt;key&gt;.&lt;instance&gt;</c>) gets the form's limits and is read
/// part by part, each file buffered up to its field's limit and as many as its field takes. A file past either is noted
/// at its field and every other answer is kept; a post past the drain allowance, or one that breaks the reading's
/// limits, goes on with no form, so the page renders with a notice that nothing was sent.
/// </summary>
/// <param name="forms">The published forms.</param>
/// <param name="uploads">The upload's rules, whose limits the files are read to.</param>
/// <param name="types">The field types, which say which fields store files and how many.</param>
/// <param name="limiter">The rate limits, which such a post meets before a byte of it is read (ADR-0012).</param>
/// <param name="time">The clock its reading is limited by.</param>
internal sealed class FormPostFilesMiddleware(PublishedFormCache forms, FileUploads uploads, FieldTypeRegistry types, FormBuilderRateLimiter limiter, TimeProvider time) : IMiddleware
{
    /// <summary>The most parts a post may have.</summary>
    public const int MostParts = 1024;

    /// <summary>The most headers a part may have.</summary>
    public const int MostHeaders = 8;

    /// <summary>The most bytes of headers a part may have.</summary>
    public const int MostHeaderBytes = 4096;

    /// <summary>The most bytes of a value that is no file.</summary>
    public const int MostValueBytes = 1_048_576;

    /// <summary>What the form's limit allows for everything but its files.</summary>
    public const long OtherBytes = 1_048_576;

    /// <summary>The most bytes a post's values may hold together: what the limit allows for them, with room for the form's own hidden inputs.</summary>
    public const long MostValuesBytes = OtherBytes + 65_536;

    /// <summary>The longest a post may take to be read: as long as an upload may take, so a post that trickles holds nothing for good.</summary>
    public static readonly TimeSpan ReadLimit = TimeSpan.FromMinutes(5);

    // A file's bytes held in memory before they go to a temporary file, as ASP.NET's own form reader holds them.
    private const int MemoryBytes = 65_536;

    // The longest boundary a multipart body may have (RFC 2046).
    private const int LongestBoundary = 70;

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (Limited(context))
        {
            return;
        }

        if (await MarkedAsync(context) is { } marked)
        {
            await ReadAsync(context, marked);
        }

        await next(context);
    }

    // A marked post counts here, before its body is read and before its form is looked up, and only once: the handler
    // takes the decision from the request. A flood gets a bare 429 and is not read at all.
    private bool Limited(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || context.Request.Query[FormPostFields.PostMarker] is not [{ } marker]
            || marker.IndexOf('.', StringComparison.Ordinal) is var dot && dot < 0
            || !Guid.TryParseExact(marker.AsSpan(0, dot), "D", out var key))
        {
            return false;
        }

        var limit = limiter.Take(context, FormBuilderRateLimitPolicies.Submit, key);
        FormPostRateLimit.Set(context, limit, key);
        if (limit is { Allowed: false, Renders: false })
        {
            FormBuilderPublicProblems.CloseConnection(context);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = limit.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers.CacheControl = "no-store";
            return true;
        }

        return false;
    }

    /// <summary>The most bytes a post of the form may have, and the most Kestrel reads of one, twice that (ADR-0006).</summary>
    /// <param name="definition">The form's published version.</param>
    public (long Form, long Allowance) Limits(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var form = OtherBytes;
        foreach (var (field, rows) in Stored(definition))
        {
            var type = uploads.TypeOf(field)!;
            var add = uploads.LimitOf(type, field) * Most(type, field) * rows;
            form = form > long.MaxValue - add ? long.MaxValue : form + add;
        }

        return (form, form > long.MaxValue / 2 ? long.MaxValue : form * 2);
    }

    /// <summary>How many files a field takes: a file field's own count, else as many texts as its type takes.</summary>
    /// <param name="type">The field's type, one that stores files.</param>
    /// <param name="field">The field.</param>
    public static int Most(IFormBuilderFieldType type, FormField field)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type is FileFieldType file ? file.ReadSettings(field.Settings).MaxFiles : type.AnswerLimits(field).MostTexts;
    }

    // Every field that stores files, with the rows its group may have (1 outside groups).
    private IEnumerable<(FormField Field, int Rows)> Stored(FormDefinition definition)
    {
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (uploads.TypeOf(field) is not null)
            {
                yield return (field, 1);
            }

            var rows = types.FindType(field.Type) is RepeaterFieldType repeater ? Math.Min(repeater.ReadSettings(field.Settings).MaxRows, RepeaterFieldType.MostRows) : 0;
            foreach (var child in field.Fields.Where(child => uploads.TypeOf(child) is not null))
            {
                yield return (child, Math.Max(rows, 1));
            }
        }
    }

    // A post to read: a multipart POST whose marker names a published form and an instance as the render gives them.
    private async Task<Marked?> MarkedAsync(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsPost(request.Method) || request.Query[FormPostFields.PostMarker] is not [{ } marker]
            || !MediaTypeHeaderValue.TryParse(request.ContentType, out var media) || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(media.Boundary).Value is not { Length: > 0 and <= LongestBoundary } boundary)
        {
            return null;
        }

        var dot = marker.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || !Guid.TryParseExact(marker.AsSpan(0, dot), "D", out var key) || marker[(dot + 1)..] is not { } instance || !FormInstances.IsId(instance)
            || await forms.GetAsync(key, context.RequestAborted) is not { } cached)
        {
            return null;
        }

        return new Marked(key, instance, cached.Published.Definition, boundary);
    }

    // Reads the post into its form, or, past the allowance or the reading's limits, leaves it with none.
    private async Task ReadAsync(HttpContext context, Marked marked)
    {
        var (_, allowance) = Limits(marked.Definition);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        {
            size.MaxRequestBodySize = allowance;
        }

        if (context.Request.ContentLength > allowance)
        {
            Refuse(context, marked);
            return;
        }

        // A post the limits refuse still shows its answers, and its files, which it stores nothing of, are not even written down.
        var limited = FormPostRateLimit.Of(context, marked.Key) is { Allowed: false };
        var read = new Read(FormPostReader.Read(new FormCollection([]), marked.Definition, types));
        using var timer = new CancellationTokenSource(ReadLimit, time);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, context.RequestAborted);
        try
        {
            var multipart = new MultipartReader(marked.Boundary, context.Request.Body) { HeadersCountLimit = MostHeaders, HeadersLengthLimit = MostHeaderBytes };
            var parts = 0;
            while (await multipart.ReadNextSectionAsync(reading.Token) is { } section)
            {
                if (++parts > MostParts)
                {
                    throw new InvalidDataException("The post has too many parts.");
                }

                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
                    || HeaderUtilities.RemoveQuotes(disposition.Name).Value is not { Length: > 0 } name)
                {
                    continue;
                }

                // A part with a file name is a file, even an empty one: a file input left empty posts one so.
                if (disposition.FileName.HasValue || disposition.FileNameStar.HasValue)
                {
                    await FileAsync(context, read, section, disposition, name, limited, reading.Token);
                }
                else
                {
                    read.Value(name, await ValueAsync(section, reading.Token));
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException || (exception is OperationCanceledException && timer.IsCancellationRequested))
        {
            Refuse(context, marked);
            return;
        }

        context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(read.Values(), read.Files)));
        if (read.Notes.Count > 0 || read.LeftOutFiles)
        {
            FormPostFileNotes.Set(context, marked.Key, marked.Instance, new FormPostFileRead(read.Notes, IsRefused: false, read.LeftOutFiles));
        }
    }

    // A file part: buffered when a field that stores files names it, up to its limit and its count; past either noted.
    private async Task FileAsync(HttpContext context, Read read, MultipartSection section, ContentDispositionHeaderValue disposition, string name, bool limited, CancellationToken cancellationToken)
    {
        var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? string.Empty;
        if (fileName.Length == 0 || read.Names.FieldOf(name, anyRow: true) is not { } found || uploads.TypeOf(found.Field) is not { } type)
        {
            // No file chosen, or a file of no field that stores files: the next part drains it.
            return;
        }

        if (limited)
        {
            read.LeftOutFiles = true;
            return;
        }

        var counted = read.Counts.GetValueOrDefault(name);
        if (counted >= Most(type, found.Field))
        {
            read.Notes.Add(new FormPostFileNote(name, FieldUploads.Declared(type, FieldMessageKinds.FileCount)));
            return;
        }

        var buffered = new FileBufferingReadStream(section.Body, MemoryBytes, uploads.LimitOf(type, found.Field), TempFolder);
        context.Response.RegisterForDisposeAsync(buffered);
        try
        {
            await buffered.DrainAsync(cancellationToken);
        }
        catch (IOException exception) when (exception is not BadHttpRequestException)
        {
            // Past the field's limit: the rest of the part is drained by the next one, within the allowance.
            read.Notes.Add(new FormPostFileNote(name, FileUploads.SizeError(type)));
            return;
        }

        buffered.Seek(0, SeekOrigin.Begin);
        read.Counts[name] = counted + 1;
        read.Files.Add(new FormFile(buffered, 0, buffered.Length, name, fileName)
        {
            Headers = new HeaderDictionary
            {
                [HeaderNames.ContentDisposition] = section.ContentDisposition,
                [HeaderNames.ContentType] = section.ContentType,
            },
        });
    }

    // A value as UTF-8, held to its most bytes.
    private static async Task<string> ValueAsync(MultipartSection section, CancellationToken cancellationToken)
    {
        using var value = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await section.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (value.Length + count > MostValueBytes)
            {
                throw new InvalidDataException("A value of the post is too long.");
            }

            await value.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        return Encoding.UTF8.GetString(value.GetBuffer(), 0, (int)value.Length);
    }

    // Nothing of the post can be kept: no form, so Umbraco renders the page, whose form shows that nothing was sent.
    private static void Refuse(HttpContext context, Marked marked)
    {
        context.Features.Set<IFormFeature>(NoFormFeature.Instance);
        PageCaching.NoStore(context);
        FormPostFileNotes.Set(context, marked.Key, marked.Instance, new FormPostFileRead([], IsRefused: true));
    }

    // Where a buffered file waits, as ASP.NET's own form reader keeps it.
    private static string TempFolder() => Environment.GetEnvironmentVariable("ASPNETCORE_TEMP") is { Length: > 0 } folder ? folder : Path.GetTempPath();

    private sealed record Marked(Guid Key, string Instance, FormDefinition Definition, string Boundary);

    // What reading a post collects.
    private sealed class Read(FormPostReader names)
    {
        private KeyValueAccumulator _values;
        private long _valueBytes;

        public FormPostReader Names { get; } = names;

        public FormFileCollection Files { get; } = [];

        public List<FormPostFileNote> Notes { get; } = [];

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public bool LeftOutFiles { get; set; }

        public void Value(string name, string value)
        {
            _valueBytes += Encoding.UTF8.GetByteCount(value);
            if (_valueBytes > MostValuesBytes)
            {
                throw new InvalidDataException("The values of the post are too long together.");
            }

            _values.Append(name, value);
        }

        public Dictionary<string, StringValues> Values() => _values.GetResults();
    }
}

/// <summary>A file a post without JavaScript could not take, by the name it was posted under, and the kind of error.</summary>
/// <param name="Name">The part's name: the field's alias, or its row's path.</param>
/// <param name="Kind">What was wrong: past the field's limit, or one too many.</param>
internal sealed record FormPostFileNote(string Name, FieldMessageKinds Kind);

/// <summary>What reading a post's files left for its surface action and its form's render.</summary>
/// <param name="Notes">The files it could not take.</param>
/// <param name="IsRefused">Whether nothing of the post could be kept (ADR-0102).</param>
/// <param name="LeftOutFiles">Whether a post the limits refuse held files, which were not read.</param>
internal sealed record FormPostFileRead(IReadOnlyList<FormPostFileNote> Notes, bool IsRefused, bool LeftOutFiles = false);

/// <summary>Where reading a post's files leaves what it found: the request's items, by the form's key and the instance.</summary>
internal static class FormPostFileNotes
{
    private const string Prefix = "Jiron.FormBuilder.Rendering.Files:";

    /// <summary>Leaves what reading a post's files found for the form and the instance.</summary>
    public static void Set(HttpContext httpContext, Guid form, string instance, FormPostFileRead read)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Items[Key(form, instance)] = read;
    }

    /// <summary>What reading a post's files found for the form and the instance, or null.</summary>
    public static FormPostFileRead? Get(HttpContext httpContext, Guid form, string instance)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(Key(form, instance), out var read) ? read as FormPostFileRead : null;
    }

    private static string Key(Guid form, string instance) => Prefix + form.ToString("N") + ":" + instance;
}
