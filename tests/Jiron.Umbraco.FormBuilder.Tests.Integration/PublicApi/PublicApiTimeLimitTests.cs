using System.Net;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// An endpoint's time limit on the site's clock (ADR-0012, ADR-0084): past it, the request is cancelled and answers
/// <c>504 timeout</c>, while the read it waited for goes on for the requests to come; an answer the action decided before
/// it ran out is written (ADR-0193). The site's clock fires its timers only when the test says so.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicApiTimeLimitTests : PublicApiTestBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly ManualTimers _timers = new();
    private readonly BeforeTheAnswer _late = new();

    [Test]
    public async Task ARequestPastItsTimeLimit_IsCancelled_AndAnswersTimeout()
    {
        var key = await PublishAsync();
        var gate = new ReadGate();
        Watched.Gate = gate;

        var sent = GetAsync($"{Root}/forms/{key}");
        await gate.Reached.WaitAsync(Patience);
        var limit = _timers.Timers.Last(timer => !timer.Disposed && timer.Period == Timeout.InfiniteTimeSpan && timer.DueTime == TimeSpan.FromSeconds(10));
        limit.Fire();
        using var response = await sent.WaitAsync(Patience);
        gate.Open();
        using var next = await GetAsync($"{Root}/forms/{key}");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(response), Is.EqualTo("504 timeout"));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(response.Headers.ETag, Is.Null);
            Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the read went on");
            Assert.That(Watched.PublishedReads, Is.EqualTo(1), "and the next request took what it read");
            Assert.That(limit.Disposed, Is.True, "the limit ends with its request");
        });
    }

    [Test]
    public async Task AnAnswerTheActionDecided_IsWritten_WhenTheTimeLimitRunsOutBeforeIt()
    {
        var key = await PublishAsync();
        _late.Arm(() => _timers.Timers.Last(timer => !timer.Disposed && timer.Period == Timeout.InfiniteTimeSpan && timer.DueTime == TimeSpan.FromSeconds(10)).Fire());

        // MVC's file results abort the connection on a cancelled write, which no visitor could tell from a lost one.
        using var response = await GetAsync($"{Root}/forms/{key}").WaitAsync(Patience);

        Assert.Multiple(() =>
        {
            Assert.That(_late.Fired, Is.True, "the limit ran out before the answer was written");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<TimeProvider>(_timers);
        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(_late));
    }

    // Runs once, armed, after an action has decided its answer and before the answer is written; as a global filter it
    // runs outside the API's own.
    private sealed class BeforeTheAnswer : IAsyncResultFilter
    {
        private Action? _armed;

        public bool Fired { get; private set; }

        public void Arm(Action action) => _armed = action;

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (Interlocked.Exchange(ref _armed, null) is { } armed)
            {
                armed();
                Fired = true;
            }

            await next();
        }
    }
}
