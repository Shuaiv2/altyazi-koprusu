// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace GameTranslator.App.Views;

public partial class AboutWindow : Window
{
    internal AboutWindow()
    {
        InitializeComponent();
        Version.Text = "Sürüm " + Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
    }

    public static void Open(Window? owner) => new AboutWindow { Owner = owner }.ShowDialog();

    private void OnLink(object sender, RequestNavigateEventArgs e) =>
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });

    private static void OpenBundled(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
        else
            MessageBox.Show(file + " bulunamadı: " + AppContext.BaseDirectory, "Ludora", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnToggleLicense(object sender, RoutedEventArgs e) =>
        LicensePanel.Visibility = LicensePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
