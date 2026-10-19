namespace Groovefin.Core.Services;

/// <summary>
/// Localized strings for code outside XAML. Keys are <c>.resw</c> resource names.
/// </summary>
public interface IStringResources
{
    /// <summary>Gets the string for <paramref name="key"/>, or the key itself when it is missing.</summary>
    string GetString(string key);
}
