using System.Windows.Input;
using Groovefin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Groovefin.App.Controls;

// Public: XAML creates it and sets its properties at run time; no setters are generated for an internal class (#140).
/// <summary>
/// "A–Z" in a list's toolbar (#390): a flyout with # and the letters A to Z, seven to a row. Choosing one runs
/// <see cref="Command"/> with the letter. The arrow keys move between the letters.
/// </summary>
public sealed partial class LetterJumpButton : Button
{
    public static readonly DependencyProperty LetterCommandProperty = DependencyProperty.Register(
        nameof(LetterCommand), typeof(ICommand), typeof(LetterJumpButton), new PropertyMetadata(null));

    private const int Columns = 7;

    public LetterJumpButton()
    {
        Content = "A–Z";
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
        for (var c = 0; c < Columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        }

        var flyout = new Flyout { Content = grid, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var letters = LetterJump.Letters;
        for (var i = 0; i < letters.Count; i++)
        {
            if (i % Columns == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var letter = letters[i];
            var button = new Button { Content = letter, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 6, 0, 6) };
            AutomationProperties.SetAutomationId(button, $"Letter_{letter}");
            Grid.SetRow(button, i / Columns);
            Grid.SetColumn(button, i % Columns);
            button.Click += (_, _) =>
            {
                flyout.Hide();
                if (LetterCommand is { } command && command.CanExecute(letter))
                {
                    command.Execute(letter);
                }
            };
            grid.Children.Add(button);
        }

        Flyout = flyout;
    }

    /// <summary>Gets or sets the command run with the chosen letter ("#" or "A" to "Z").</summary>
    public ICommand? LetterCommand
    {
        get => (ICommand?)GetValue(LetterCommandProperty);
        set => SetValue(LetterCommandProperty, value);
    }
}
