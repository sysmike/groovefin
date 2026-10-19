using Groovefin.Core.Settings;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

/// <summary>
/// One row of the songs table: a cell per visible column, as wide as its header. The page hands each
/// realized row its song with <see cref="Show"/>; the row follows column changes by itself.
/// </summary>
internal sealed partial class SongTableRow : Grid
{
    /// <summary>Space on both sides of a cell's text; the header uses the same.</summary>
    public const double CellPadding = 8;

    private SongsViewModel? _table;
    private SongRow? _row;
    private Style? _primaryStyle;
    private Style? _secondaryStyle;
    private IReadOnlyList<SongColumnViewModel> _columns = [];

    /// <summary>Shows <paramref name="row"/>; the title uses <paramref name="primaryStyle"/>, other cells <paramref name="secondaryStyle"/>.</summary>
    public void Show(SongsViewModel table, SongRow row, Style primaryStyle, Style secondaryStyle)
    {
        if (!ReferenceEquals(_table, table))
        {
            if (_table is not null)
            {
                _table.LayoutChanged -= OnLayoutChanged;
            }

            _table = table;
            _primaryStyle = primaryStyle;
            _secondaryStyle = secondaryStyle;
            table.LayoutChanged += OnLayoutChanged;
            _columns = [];
        }

        _row = row;
        Layout();
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
            for (var i = 0; i < columns.Count; i++)
            {
                ColumnDefinitions.Add(new ColumnDefinition());
                var cell = new TextBlock
                {
                    Style = columns[i].Column == SongColumn.Title ? _primaryStyle : _secondaryStyle,
                    Margin = new Thickness(CellPadding, 0, CellPadding, 0),
                    HorizontalAlignment = columns[i].IsNumeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
                SetColumn(cell, i);
                Children.Add(cell);
            }
        }

        for (var i = 0; i < columns.Count; i++)
        {
            ColumnDefinitions[i].Width = new GridLength(columns[i].Width);
            ((TextBlock)Children[i]).Text = _row.Text(columns[i].Column);
        }
    }
}
