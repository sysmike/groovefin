using System.ComponentModel;
using Groovefin.App.Converters;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Groovefin.App.Controls;

/// <summary>
/// The lyrics on the right of the window (Ctrl+L or the now playing bar). Synced lyrics keep the sung
/// line in the middle, except for a few seconds after the user scrolled; a click on a line seeks there.
/// </summary>
internal sealed partial class LyricsPanel : UserControl
{
    /// <summary>How long following playback pauses after the user scrolled the lines.</summary>
    private static readonly TimeSpan ScrollPause = TimeSpan.FromSeconds(4);

    private ScrollViewer? _scroll;
    private DateTimeOffset _followAgainAt;

    public LyricsPanel()
    {
        ViewModel = App.Services.GetRequiredService<LyricsViewModel>();
        InitializeComponent();

        LinesList.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnUserScrolled), true);
        LinesList.AddHandler(PointerPressedEvent, new PointerEventHandler(OnUserScrolled), true);
        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public LyricsViewModel ViewModel { get; }

    /// <summary>Moves keyboard focus into the panel, e.g. after Ctrl+L opened it.</summary>
    public void FocusPanel()
    {
        if (!LinesList.Focus(FocusState.Keyboard))
        {
            CloseButton.Focus(FocusState.Keyboard);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroll ??= FindScrollViewer(LinesList);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyTextStyle();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LyricsViewModel.CurrentIndex):
                FollowCurrentLine();
                break;
            case nameof(LyricsViewModel.FontSize) or nameof(LyricsViewModel.Alignment):
                ApplyTextStyle();
                break;
        }
    }

    private void OnUserScrolled(object sender, PointerRoutedEventArgs e) => _followAgainAt = DateTimeOffset.UtcNow + ScrollPause;

    /// <summary>Scrolls the sung line to the middle of the pane.</summary>
    private void FollowCurrentLine()
    {
        var index = ViewModel.CurrentIndex;
        if (index < 0 || index >= ViewModel.Lines.Count || _scroll is null || DateTimeOffset.UtcNow < _followAgainAt)
        {
            return;
        }

        if (LinesList.ContainerFromIndex(index) is not FrameworkElement line)
        {
            // Not realized yet: bring it into view first; the next line change centers it.
            LinesList.ScrollIntoView(ViewModel.Lines[index]);
            return;
        }

        var top = line.TransformToVisual(_scroll).TransformPoint(default).Y + _scroll.VerticalOffset;
        var target = top - ((_scroll.ViewportHeight - line.ActualHeight) / 2);
        _scroll.ChangeView(null, Math.Max(0, target), null, false);
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is TextBlock text)
        {
            ApplyStyle(text);
        }
    }

    private void ApplyTextStyle()
    {
        for (var i = 0; i < ViewModel.Lines.Count; i++)
        {
            if (LinesList.ContainerFromIndex(i) is ListViewItem { ContentTemplateRoot: TextBlock text })
            {
                ApplyStyle(text);
            }
        }
    }

    private void ApplyStyle(TextBlock text)
    {
        text.FontSize = ViewModel.FontSize;
        text.TextAlignment = LyricsStyle.Alignment(ViewModel.Alignment);
    }

    private void OnLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLineItem line)
        {
            // Follow the music again right away.
            _followAgainAt = default;
            ViewModel.SeekToCommand.Execute(line);
        }
    }

    private void OnOptionsOpening(object sender, object e)
    {
        AlignLeftItem.IsChecked = ViewModel.Alignment == LyricsAlignment.Left;
        AlignCenterItem.IsChecked = ViewModel.Alignment == LyricsAlignment.Center;
        AlignRightItem.IsChecked = ViewModel.Alignment == LyricsAlignment.Right;
        OffsetItem.Text = ViewModel.OffsetText;
    }

    private void OnAlignmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<LyricsAlignment>(tag, out var alignment))
        {
            ViewModel.SetAlignmentCommand.Execute(alignment);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll)
            {
                return scroll;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
