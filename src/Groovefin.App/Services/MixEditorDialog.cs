using System.ComponentModel;
using Groovefin.Core.Mixes;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Services;

/// <summary>
/// The mix editor (#401): a dialog with the rules of <see cref="MixEditorViewModel"/>. Each control writes its
/// choice to the view model; Save is enabled while the choices can be saved.
/// </summary>
internal sealed class MixEditorDialog(ContentDialogService dialogs, IStringResources strings) : IMixEditor
{
    private const double ChoiceWidth = 360;

    public async Task<bool> ShowAsync(MixEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var dialog = dialogs.Create(editor.Title, strings.GetString("Dialog_Save"), strings.GetString("Dialog_Cancel"));

        var name = new TextBox { Header = strings.GetString("MixEditor_Name"), Text = editor.Name, Width = ChoiceWidth, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(name, "MixNameBox");
        name.TextChanged += (_, _) => editor.Name = name.Text;

        var from = Year(strings.GetString("MixEditor_FromYear"), editor.FromYear, "MixFromYearBox");
        from.ValueChanged += (_, e) => editor.FromYear = e.NewValue;
        var to = Year(strings.GetString("MixEditor_ToYear"), editor.ToYear, "MixToYearBox");
        to.ValueChanged += (_, e) => editor.ToYear = e.NewValue;
        var years = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { from, to } };

        var favourites = new CheckBox { Content = strings.GetString("MixEditor_FavouritesOnly"), IsChecked = editor.FavouritesOnly };
        favourites.Click += (_, _) => editor.FavouritesOnly = favourites.IsChecked == true;

        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                name,
                Choice("MixEditor_Genre", editor.GenreOptions, editor.GenreIndex, i => editor.GenreIndex = i),
                years,
                favourites,
                Choice("MixEditor_Plays", editor.PlaysOptions, editor.PlaysIndex, i => editor.PlaysIndex = i),
                Choice("MixEditor_NotPlayed", editor.NotPlayedOptions, editor.NotPlayedIndex, i => editor.NotPlayedIndex = i),
                Choice("MixEditor_Added", editor.AddedOptions, editor.AddedIndex, i => editor.AddedIndex = i),
                Choice("MixEditor_Order", editor.OrderOptions, editor.OrderIndex, i => editor.OrderIndex = i),
                Choice("MixEditor_Size", editor.SizeOptions, editor.SizeIndex, i => editor.SizeIndex = i),
            },
        };
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        dialog.IsPrimaryButtonEnabled = editor.CanSave;
        void OnChanged(object? sender, PropertyChangedEventArgs e) => dialog.IsPrimaryButtonEnabled = editor.CanSave;
        editor.PropertyChanged += OnChanged;
        dialog.Opened += (_, _) => name.Focus(FocusState.Programmatic);
        try
        {
            return await dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
        }
        finally
        {
            editor.PropertyChanged -= OnChanged;
        }
    }

    private static NumberBox Year(string header, double value, string automationId)
    {
        var box = new NumberBox
        {
            Header = header,
            Value = value,
            Minimum = MixEditorViewModel.MinYearValue,
            Maximum = MixEditorViewModel.MaxYearValue,
            SmallChange = 1,
            Width = (ChoiceWidth - 12) / 2,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private ComboBox Choice(string headerKey, IReadOnlyList<string> options, int selected, Action<int> choose)
    {
        var box = new ComboBox
        {
            Header = strings.GetString(headerKey),
            ItemsSource = options,
            SelectedIndex = selected,
            Width = ChoiceWidth,
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
            {
                choose(box.SelectedIndex);
            }
        };
        return box;
    }
}
