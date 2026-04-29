using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace VrcOscSender;

public class LogWindow : Window
{
    public LogWindow(ObservableCollection<string> log)
    {
        Title           = "Debug Log";
        Width           = 580;
        Height          = 400;
        MinWidth        = 400;
        MinHeight       = 200;
        Background      = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x1a));
        FontFamily      = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize        = 12;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Log list
        var listBox = new ListBox
        {
            ItemsSource     = log,
            Background      = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground      = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0xea, 0xea, 0xea)),
            FontFamily      = new System.Windows.Media.FontFamily("Consolas"),
            FontSize        = 12,
            Padding         = new Thickness(8),
        };
        Grid.SetRow(listBox, 0);

        // Clear button
        var clearBtn = new Button
        {
            Content         = "Clear Log",
            Margin          = new Thickness(8),
            Padding         = new Thickness(12, 6, 12, 6),
            Background      = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0x1e, 0x3a, 0x5f)),
            Foreground      = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor          = System.Windows.Input.Cursors.Hand,
        };
        clearBtn.Click += (_, _) => log.Clear();
        Grid.SetRow(clearBtn, 1);

        grid.Children.Add(listBox);
        grid.Children.Add(clearBtn);
        Content = grid;
    }
}
