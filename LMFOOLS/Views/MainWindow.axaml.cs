using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LMFOOLS.ViewModels;

namespace LMFOOLS.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(); // For printing the version number.
    }

    public static string PackageVersion
    {
        get
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            if (version != null)
            {
                return version.ToString();
            }

            return "Error getting version number.";
        }
    }

    private void LicenseFileLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        
    }

    private void OptionsFileLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        
    }

    private async void ShowErrorWindow(string errorMessage)
    {
        OutputTextBlock.Text = string.Empty;
        ErrorWindow errorWindow = new();
        errorWindow.ErrorTextBlock.Text = errorMessage;

        // Check if VisualRoot is not null and is a Window before casting
        if (this.VisualRoot is Window window)
        {
            await errorWindow.ShowDialog(window);
        }
        else
        {
            OutputTextBlock.Text = "The error window broke somehow. Please make an issue for this in GitHub.";
        }
    }

    private async void LicenseFileBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            var mainWindow = desktop.MainWindow;

            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select a license file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("License Files") { Patterns = ["*.lic", "*.dat"] },
                    new FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            };

            // Open the file dialog and await for the user's response.
            var result = await mainWindow.StorageProvider.OpenFilePickerAsync(filePickerOptions);

            var selectedFile = result[0];

            // Read the file contents.
            string fileContents;
            using (var stream = await selectedFile.OpenReadAsync())
            using (var reader = new StreamReader(stream))
            {
                fileContents = await reader.ReadToEndAsync();
            }

            // Check the file size indirectly via its content length.
            long fileSizeInBytes = fileContents.Length;
            const long fiftyMegabytes = 50L * 1024L * 1024L;

            if (fileSizeInBytes > fiftyMegabytes)
            {
                ShowErrorWindow(
                    "There is an issue with the license file: it is over 50 MB and therefore, likely (hopefully) not a license file.");
                LicenseFileLocationTextBox.Text = string.Empty;
                return;
            }

            if (!fileContents.Contains("INCREMENT"))
            {
                ShowErrorWindow(
                    "There is an issue with the license file: it is either not a license file or it is corrupted.");
                LicenseFileLocationTextBox.Text = string.Empty;
                return;
            }

            if (fileContents.Contains("lo=IN") || fileContents.Contains("lo=DC") || fileContents.Contains("lo=CIN"))
            {
                ShowErrorWindow(
                    "There is an issue with the license file: it contains an Individual or Designated Computer license, " +
                    "which cannot use an options file.");
                LicenseFileLocationTextBox.Text = string.Empty;
                return;
            }

            if (fileContents.Contains("CONTRACT_ID="))
            {
                ShowErrorWindow(
                    "There is an issue with the license file: it contains at least 1 non-MathWorks product.");
                LicenseFileLocationTextBox.Text = string.Empty;
                return;
            }

            if (!fileContents.Contains("SERVER") || !fileContents.Contains("DAEMON"))
            {
                ShowErrorWindow(
                    "There is an issue with the license file: it is missing the SERVER and/or DAEMON line.");
                LicenseFileLocationTextBox.Text = string.Empty;
                return;
            }

            // Gotta convert some things, ya know?
            var rawFilePath = selectedFile.TryGetLocalPath;
            string? filePath = rawFilePath();
            LicenseFileLocationTextBox.Text = filePath;
        }
        else
        {
            ShowErrorWindow("idk man, you really broke something. Make an issue on GitHub for this.");
        }
    }
}