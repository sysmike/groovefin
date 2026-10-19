namespace Groovefin.Core.Services;

/// <summary>
/// Navigates between pages identified by keys from <see cref="Navigation.PageKeys"/>.
/// </summary>
public interface INavigationService
{
    /// <summary>Gets a value indicating whether there is a page to go back to.</summary>
    bool CanGoBack { get; }

    /// <summary>Gets the key of the page that is currently shown, or <see langword="null"/> before the first navigation.</summary>
    string? CurrentPageKey { get; }

    /// <summary>Raised after a navigation (forward or back) has completed.</summary>
    event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>Navigates to the page registered for <paramref name="pageKey"/>.</summary>
    /// <returns><see langword="true"/> when navigation happened.</returns>
    bool NavigateTo(string pageKey, object? parameter = null);

    /// <summary>Navigates to the previous page.</summary>
    /// <returns><see langword="true"/> when navigation happened.</returns>
    bool GoBack();
}

/// <summary>Event data for <see cref="INavigationService.Navigated"/>.</summary>
public sealed class NavigatedEventArgs(string pageKey, object? parameter) : EventArgs
{
    public string PageKey { get; } = pageKey;

    public object? Parameter { get; } = parameter;
}
