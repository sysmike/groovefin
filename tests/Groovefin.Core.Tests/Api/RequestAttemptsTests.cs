using Groovefin.Core.Api;

namespace Groovefin.Core.Tests.Api;

public sealed class RequestAttemptsTests
{
    [Fact]
    public void Retries_reach_the_watcher_of_their_own_flow_only()
    {
        var seen = new List<string>();
        RequestAttempts.Retrying(2);

        using (RequestAttempts.Watch(a => seen.Add($"outer {a}")))
        {
            RequestAttempts.Retrying(2);
            using (RequestAttempts.Watch(a => seen.Add($"inner {a}")))
            {
                RequestAttempts.Retrying(3);
            }

            RequestAttempts.Retrying(4);
        }

        RequestAttempts.Retrying(5);
        seen.Should().Equal("outer 2", "inner 3", "outer 4");
    }

    [Fact]
    public async Task A_watch_follows_the_request_into_awaited_calls()
    {
        var seen = new List<int>();

        using (RequestAttempts.Watch(seen.Add))
        {
            await Task.Run(() => RequestAttempts.Retrying(2), TestContext.Current.CancellationToken);
        }

        await Task.Run(() => RequestAttempts.Retrying(3), TestContext.Current.CancellationToken);
        seen.Should().Equal(2);
    }
}
