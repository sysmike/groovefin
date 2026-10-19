using Groovefin.Core.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace Groovefin.App.Converters;

/// <summary>x:Bind helpers for the lyrics pane: lines not being sung are dimmed, the sung one is bold.</summary>
internal static class LyricsStyle
{
    public static double Opacity(bool isDimmed) => isDimmed ? 0.5 : 1;

    public static FontWeight Weight(bool isCurrent) => isCurrent ? FontWeights.SemiBold : FontWeights.Normal;

    public static TextAlignment Alignment(LyricsAlignment alignment) => alignment switch
    {
        LyricsAlignment.Left => TextAlignment.Left,
        LyricsAlignment.Right => TextAlignment.Right,
        _ => TextAlignment.Center,
    };
}
