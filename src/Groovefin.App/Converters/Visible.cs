using Microsoft.UI.Xaml;

namespace Groovefin.App.Converters;

/// <summary>x:Bind helpers for bool → Visibility.</summary>
internal static class Visible
{
    public static Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Unless(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
}
