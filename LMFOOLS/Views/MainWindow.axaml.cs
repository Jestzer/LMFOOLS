using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        
        var settings = LoadSettings();
        LicenseFileLocationTextBox.Text = settings.LicenseFilePathSetting;
        LmgrdLocationTextBox.Text = settings.LmgrdPathSetting;
        
        Closing += MainWindow_Closing;
    }

    private void LicenseFileLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        
    }

    private void LmgrdLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
    }
    
    private void LmutilLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
    }
    
    private static void SaveSettings(Settings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(settings, options);
        File.WriteAllText("settings.json", jsonString);
    }
    
    private static Settings LoadSettings()
    {
        if (!File.Exists("settings.json"))
        {
            return new Settings(); // Return default settings if file not found.
        }
        string jsonString = File.ReadAllText("settings.json");
        return JsonSerializer.Deserialize<Settings>(jsonString) ?? new Settings();
    }
    
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var settings = LoadSettings();
            if (LicenseFileLocationTextBox.Text != null)
            {
                settings.LicenseFilePathSetting = LicenseFileLocationTextBox.Text;
            }

            if (LmgrdLocationTextBox.Text != null)
            {
                settings.LmgrdPathSetting = LmgrdLocationTextBox.Text;
            }

            SaveSettings(settings);
        }
        catch (Exception ex)
        {
            ShowErrorWindow("There was error when attempting to save your settings: " + ex.Message);
        }
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

    private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var updateWindow = new UpdateWindow();

        await updateWindow.ShowDialog(this); // Putting this here, otherwise it won't center it on the MainWindow. Sorryyyyy.
    }

    private async void LicenseFileBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
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
            if (result != null && result.Any()) // No, Rider, this is not always true because it's possible for a file to not be picked.
            {
                var selectedFile = result[0];
                if (selectedFile != null)
                {
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
                            "There is an issue with the license file: it contains an Individual or Designated Computer license.");
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
            }
        }
        else
        {
            ShowErrorWindow("idk man, you really broke something. Make an issue on GitHub for this.");
        }
    }

    private async void LmgrdBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
        {
            var mainWindow = desktop.MainWindow;

            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select an options file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("lmgrd executable") { Patterns = ["lmgrd.exe", "lmgrd"] },
                    new FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            };

            // Open the file dialog.
            var result = await mainWindow.StorageProvider.OpenFilePickerAsync(filePickerOptions);

            if (result != null && result.Any()) // No, Rider, this is not always true because it's possible for a file to not be picked.
            {
                var selectedFile = result[0];
                if (selectedFile != null)
                {
                    // Read the file contents.
                    string fileContents;
                    using (var stream = await selectedFile.OpenReadAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        fileContents = await reader.ReadToEndAsync();
                    }

                    // Check the file size indirectly via its content length.
                    long fileSizeInBytes = fileContents.Length;
                    const long twoMegabytes = 2L * 1024L * 1024L;

                    if (fileSizeInBytes > twoMegabytes)
                    {
                        ShowErrorWindow("The selected file is over 2 MB, which is unexpectedly large for lmgrd. I will assume is this not lmgrd.");
                        LmgrdLocationTextBox.Text = string.Empty;
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(fileContents))
                    {
                        ShowErrorWindow("There is an issue with lmgrd: it is either empty or is read-only.");
                        LmgrdLocationTextBox.Text = string.Empty;
                        return;
                    }

                    // Gotta convert some things, ya know?
                    var rawFilePath = selectedFile.TryGetLocalPath;
                    string? filePath = rawFilePath();
                    LmgrdLocationTextBox.Text = filePath;
                }
            }
        }
        else
        {
            ShowErrorWindow("idk man, you really broke something. Make an issue on GitHub for this.");
        }
    }
    
    private async void LmutilBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
        {
            var mainWindow = desktop.MainWindow;

            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select the lmutil executable",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("lmutil executable") { Patterns = ["lmutil.exe", "lmutil"] },
                    new FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            };

            // Open the file dialog.
            var result = await mainWindow.StorageProvider.OpenFilePickerAsync(filePickerOptions);

            if (result != null && result.Any()) // No, Rider, this is not always true because it's possible for a file to not be picked.
            {
                var selectedFile = result[0];
                if (selectedFile != null)
                {
                    // Read the file contents.
                    string fileContents;
                    using (var stream = await selectedFile.OpenReadAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        fileContents = await reader.ReadToEndAsync();
                    }

                    // Check the file size indirectly via its content length.
                    long fileSizeInBytes = fileContents.Length;
                    const long twoMegabytes = 2L * 1024L * 1024L;

                    if (fileSizeInBytes > twoMegabytes)
                    {
                        ShowErrorWindow("The selected file is over 2 MB, which is unexpectedly large for lmutil. I will assume is this not lmutil.");
                        LmutilLocationTextBox.Text = string.Empty;
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(fileContents))
                    {
                        ShowErrorWindow("There is an issue with lmutil: it is either empty or is read-only.");
                        LmutilLocationTextBox.Text = string.Empty;
                        return;
                    }

                    // Gotta convert some things, ya know?
                    var rawFilePath = selectedFile.TryGetLocalPath;
                    string? filePath = rawFilePath();
                    LmutilLocationTextBox.Text = filePath;
                }
            }
        }
        else
        {
            ShowErrorWindow("idk man, you really broke something. Make an issue on GitHub for this.");
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Retrieve the paths from the text boxes
        string lmutilPath = LmutilLocationTextBox.Text;
        string licenseFilePath = LicenseFileLocationTextBox.Text;
        
        if (!File.Exists(lmutilPath))
        {
            ShowErrorWindow($"The lmutil executable was not found at the specified path: {lmutilPath}");
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}");
            return;
        }

        // Build the command arguments
        string arguments = $"-c \"{licenseFilePath}\" -q";

        // Execute the command
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = lmutilPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();

                // Optionally, read the output and error streams
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                // Check the exit code to determine if the command succeeded
                if (process.ExitCode == 0)
                {
                    // Command succeeded
                    Console.WriteLine("FlexLM stopped successfully.");
                    Console.WriteLine(output);
                }
                else
                {
                    // Command failed
                    Console.WriteLine("Failed to stop FlexLM.");
                    Console.WriteLine(error);
                }
            }
        }
        catch (Exception ex)
        {
            // Handle any exceptions that occur during process start
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }
}