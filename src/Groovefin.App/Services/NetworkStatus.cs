using System.Runtime.InteropServices;
using Groovefin.Core.Services;
using Windows.Networking.Connectivity;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="INetworkStatus"/> from Windows' connection profiles (#327). Local access counts as online: a server
/// on the home network can be reached without the internet. When Windows cannot tell, the PC counts as online
/// and not metered: a failed request then says more.
/// </summary>
internal sealed class NetworkStatus : INetworkStatus, IDisposable
{
    public NetworkStatus() => NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;

    public event EventHandler? Changed;

    public bool IsOnline
    {
        get
        {
            try
            {
                return NetworkInformation.GetInternetConnectionProfile() is { } profile
                    ? profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None
                    : NetworkInformation.GetConnectionProfiles().Any(p => p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    public bool IsMetered
    {
        get
        {
            try
            {
                return NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost() is { } cost
                    && (cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable || cost.Roaming || cost.OverDataLimit);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void Dispose() => NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;

    private void OnNetworkStatusChanged(object sender) => Changed?.Invoke(this, EventArgs.Empty);
}
