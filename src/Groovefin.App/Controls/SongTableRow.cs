using System.ComponentModel;
using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>
/// One row of the songs table: a cell per visible column, as wide as its header. The page hands each
/// realized row its song with <see cref="Show"/>; the row follows column changes, and marks its song while
/// it plays (#247), by itself.
/// </summary>
internal sealed partial class SongTableRow : Grid
{
    /// <summary>Space on both sides of a cell's text; the header uses the same.</summary>
    public const double CellPadding = 8;

    private SongsViewModel? _table;
    private SongRow? _row;
    private Style? _secondaryStyle;
    private IReadOnlyList<SongColumnViewModel> _columns = [];

    /// <summary>Shows <paramref name="row"/>; cells other than the title and the number use <paramref name="secondaryStyle"/>.</summary>
    public void Show(SongsViewModel table, SongRow row, Style secondaryStyle)
    {
        if (!ReferenceEquals(_table, table))
        {
            if (_table is not null)
            {
                _table.LayoutChanged -= OnLayoutChanged;
            }

            _table = table;
            _secondaryStyle = secondaryStyle;
            table.LayoutChanged += OnLayoutChanged;
            _columns = [];
        }

        if (!ReferenceEquals(_row, row))
        {
            if (_row is not null)
            {
                _row.PropertyChanged -= OnRowChanged;
            }

            row.PropertyChanged += OnRowChanged;
        }

        _row = row;
        Layout();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SongRow.IsCurrent) && ReferenceEquals(sender, _row))
        {
            Layout();
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => Layout();

    /// <summary>Rebuilds the cells when columns were shown, hidden or moved; otherwise only sets widths and texts.</summary>
    private void Layout()
    {
        if (_table is null || _row is null)
        {
            return;
        }

        var columns = _table.VisibleColumns;
        if (!columns.SequenceEqual(_columns))
        {
            _columns = columns;
            ColumnDefinitions.Clear();
            Children.Clear();
            // The playing song's speaker replaces its number, or sits before its title without a # column.
            // Its title, artists and album turn the accent colour (#249); the numbers stay grey.
            var hasNumber = columns.Any(c => c.Column == SongColumn.TrackNumber);
            for (var i = 0; i < columns.Count; i++)
            {
                ColumnDefinitions.Add(new ColumnDefinition());
                FrameworkElement cell = columns[i].Column switch
                {
                    SongColumn.Title => new TrackText { ShowsIcon = !hasNumber },
                    SongColumn.Artist or SongColumn.Album or SongColumn.AlbumArtist => new TrackText { IsSecondary = true },
                    SongColumn.TrackNumber => new TrackNumber(),
                    _ => new TextBlock { Style = _secondaryStyle },
                };
                cell.Margin = new Thickness(CellPadding, 0, CellPadding, 0);
                cell.VerticalAlignment = VerticalAlignment.Center;
                cell.HorizontalAlignment = columns[i].IsNumeric ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                SetColumn(cell, i);
                Children.Add(cell);
            }
        }

        for (var i = 0; i < columns.Count; i++)
        {
            ColumnDefinitions[i].Width = new GridLength(columns[i].Width);
            var text = _row.Text(columns[i].Column);
            switch (Children[i])
            {
                case TrackText track:
                    track.Text = text;
                    track.IsPlaying = _row.IsCurrent;
                    break;
                case TrackNumber number:
                    number.Text = text;
                    number.IsPlaying = _row.IsCurrent;
                    break;
                case TextBlock cell:
                    cell.Text = text;
                    break;
            }
        }
    }
}
