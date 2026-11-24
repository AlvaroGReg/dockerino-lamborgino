using System.Windows;

namespace DockerinoLamborgino.App;

public partial class ApplicationOptionsWindow : Window
{
    private readonly WindowsStartupSetting startupSetting = new();

    public ApplicationOptionsWindow()
    {
        InitializeComponent();
        try
        {
            StartWithWindowsCheckBox.IsChecked = startupSetting.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or
                                   InvalidOperationException or System.IO.IOException)
        {
            StatusText.Text = $"Could not read the Windows startup setting: {ex.Message}";
            StartWithWindowsCheckBox.IsEnabled = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            startupSetting.SetEnabled(StartWithWindowsCheckBox.IsChecked == true);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or
                                   InvalidOperationException or System.IO.IOException)
        {
            StatusText.Text = $"Could not save the Windows startup setting: {ex.Message}";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
