using Groovefin.Core.Services;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Groovefin.App.Services;

/// <summary><see cref="IStringResources"/> backed by the app's <c>Resources.resw</c> files.</summary>
internal sealed class ResourceStringProvider : IStringResources
{
    private readonly ResourceLoader _loader = new();

    public string GetString(string key)
    {
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }
}
