using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>A network whose state the test sets.</summary>
internal sealed class FakeNetworkStatus : INetworkStatus
{
    private bool _isOnline = true;

    public event EventHandler? Changed;

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            _isOnline = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsMetered { get; set; }
}
