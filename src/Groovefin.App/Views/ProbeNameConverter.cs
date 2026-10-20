using Microsoft.UI.Xaml.Data;

namespace Groovefin.App.Views;

/// <summary>TEMPORARY (#165): names the probe sliders for the CI screenshot; removed before the PR.</summary>
public sealed partial class ProbeNameConverter : IValueConverter
{
    public string Prefix { get; set; } = string.Empty;

    public object Convert(object value, Type targetType, object parameter, string language) => Prefix + value;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
