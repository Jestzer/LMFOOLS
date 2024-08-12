using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LMFOOLS.ViewModels;

namespace LMFOOLS.Views;

public partial class MainWindow : Window
{
    private bool _programWasJustLaunched = true;

    public MainWindow()
    {
        InitializeComponent();

        // Bar unsupported platforms.
        if (_platform == OSPlatform.FreeBSD || _platform == OSPlatform.Create("UNKNOWN"))
        {
            ShowErrorWindow("FreeBSD is not natively supported by FlexLM and therefore, neither will this program.");
            Close();
        }

        DataContext = new MainViewModel(); // For printing the version number.

        var settings = LoadSettings();
        LicenseFileLocationTextBox.Text = settings.LicenseFilePathSetting;
        LmgrdLocationTextBox.Text = settings.LmgrdPathSetting;
        LmutilLocationTextBox.Text = settings.LmutilPathSetting;

        // Let's open with a status check, if we can.
        if (!string.IsNullOrWhiteSpace(LicenseFileLocationTextBox.Text) && !string.IsNullOrWhiteSpace(LmgrdLocationTextBox.Text) &&
            !string.IsNullOrWhiteSpace(LmutilLocationTextBox.Text))
        {
            CheckStatus();
        }

        Closing += MainWindow_Closing;
    }

    private static readonly char[] LineSeparator = ['\r', '\n'];

    // Figure out your platform.
    readonly OSPlatform _platform = GetOperatingSystemPlatform();

    private static OSPlatform GetOperatingSystemPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OSPlatform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OSPlatform.OSX;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            return OSPlatform.FreeBSD;
        }

        return OSPlatform.Create("UNKNOWN");
    }

    private bool _flexLmIsAlreadyRunning;

    // It's bad because it's not a thorough check.
    private void BadFlexLmStatusCheckAndButtonUpdate()
    {
        if (OutputTextBlock.Text != null)
        {
            if (OutputTextBlock.Text == "FlexLM is up!" || OutputTextBlock.Text.Contains("LMGRD was able to start"))
            {
                _flexLmIsAlreadyRunning = true;
            }
            else
            {
                _flexLmIsAlreadyRunning = false;
            }
        }
        else
        {
            _flexLmIsAlreadyRunning = false;
        }

        if (LicenseFileLocationTextBox == null || LmgrdLocationTextBox == null || LmutilLocationTextBox == null) return;

        bool isLicenseFileValid = File.Exists(LicenseFileLocationTextBox.Text);
        bool isLmgrdValid = File.Exists(LmgrdLocationTextBox.Text);
        bool isLmutilValid = File.Exists(LmutilLocationTextBox.Text);

        StartButton.IsEnabled = !_flexLmIsAlreadyRunning && isLicenseFileValid && isLmgrdValid && isLmutilValid;
        StopButton.IsEnabled = _flexLmIsAlreadyRunning && isLicenseFileValid && isLmgrdValid && isLmutilValid;
        StatusButton.IsEnabled = isLicenseFileValid && isLmgrdValid && isLmutilValid;
    }

    private void LicenseFileLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        BadFlexLmStatusCheckAndButtonUpdate();
    }

    private void LmgrdLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        BadFlexLmStatusCheckAndButtonUpdate();
    }

    private void LmutilLocationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        BadFlexLmStatusCheckAndButtonUpdate();
    }

    [JsonSerializable(typeof(Settings))]
    internal partial class SettingsJsonContext : JsonSerializerContext { }

    public static (string settingsPath, string lmLogPath) DeterminePaths()
    {
        string settingsPath;
        string lmLogPath;

        if (OperatingSystem.IsWindows())
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            settingsPath = Path.Combine(appDataPath, "Jestzer.Programs", "LMFOOLS", "settings-lmfools.json");
            lmLogPath = Path.Combine(appDataPath, "Jestzer.Programs", "LMFOOLS", "lmlog.txt");
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            string homePath = Environment.GetEnvironmentVariable("HOME") ?? "/tmp";
            settingsPath = Path.Combine(homePath, ".config", "Jestzer.Programs", "LMFOOLS", "settings-lmfools.json");
            lmLogPath = Path.Combine(homePath, ".config", "Jestzer.Programs", "LMFOOLS", "lmlog.txt"); ;
        }
        else
        {
            settingsPath = "settings-lmfools.json";
            lmLogPath = "lmlog.txt";
            Console.WriteLine("Warning: your operating system has been detected as something other than Windows, Linux, or macOS. " +
            "Your settings and FlexLM log file will be saved in your current directory.");
        }

        string directoryPath = Path.GetDirectoryName(settingsPath) ?? "";
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (string.IsNullOrEmpty(directoryPath))
        {
            Console.WriteLine("Warning: your settings' directory path is null or empty. Settings may not work correctly and your log file may not load correctly.");
        }

        return (settingsPath, lmLogPath);
    }

    public static string SettingsPath()
    {
        var (settingsPath, _) = DeterminePaths();
        return settingsPath;
    }

    public static string LmLogPath()
    {
        var (_, lmLogPath) = DeterminePaths();
        return lmLogPath;
    }

    private static void SaveSettings(Settings settings)
    {
        string settingsPath = SettingsPath();
        string jsonString = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);
        File.WriteAllText(settingsPath, jsonString);
    }

    private static Settings LoadSettings()
    {
        string settingsPath = SettingsPath();
        if (!File.Exists(settingsPath))
        {
            return new Settings(); // Return default settings if file not found.
        }

        string jsonString = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize(jsonString, SettingsJsonContext.Default.Settings) ?? new Settings();
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

            if (LmutilLocationTextBox.Text != null)
            {
                settings.LmutilPathSetting = LmutilLocationTextBox.Text;
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
        OutputTextBlock.Text = errorMessage;
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
                Title = "Select lmgrd",
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

                    if (filePath != null)
                    {
                        if (filePath.Contains("/run/user/1000/doc/"))
                        {
                            ShowErrorWindow(
                                "There is an issue with lmgrd: it resides in a directory that this program does not have access to. Please move it elsewhere or choose a different file.");
                            LmgrdLocationTextBox.Text = string.Empty;
                            return;
                        }
                    }

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

                    if (filePath != null)
                    {
                        if (filePath.Contains("/run/user/1000/doc/"))
                        {
                            ShowErrorWindow(
                                "There is an issue with lmutil: it resides in a directory that this program does not have access to. Please move it elsewhere or choose a different file.");
                            LmutilLocationTextBox.Text = string.Empty;
                            return;
                        }
                    }

                    LmutilLocationTextBox.Text = filePath;
                }
            }
        }
        else
        {
            ShowErrorWindow("idk man, you really broke something. Make an issue on GitHub for this.");
        }
    }

    private bool _stopButtonWasJustUsed;

    private async void FlexLmCanStart()
    {
        StopButton.IsEnabled = false;

        if (_stopButtonWasJustUsed)
        {
            Dispatcher.UIThread.Post(() => OutputTextBlock.Text += " The start button will be available to use in 30 seconds. This is to ensure FlexLM has fully stopped " +
                                                                   "and the desired TCP ports are opened.");
            StatusButton.IsEnabled = false;
            await Task.Delay(30000); // Wait 30 seconds.
        }

        StatusButton.IsEnabled = true;
        StartButton.IsEnabled = true;
        _stopButtonWasJustUsed = false;
    }

    private void FlexLmCanStop()
    {
        StopButton.IsEnabled = true;
        StartButton.IsEnabled = false;
        StatusButton.IsEnabled = true;
    }

    private void FlexLmStatusUnknown()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        StatusButton.IsEnabled = true;
        OutputTextBlock.Text = "Status unknown.";
    }

    private void BusyWithFlexLm()
    {
        Dispatcher.UIThread.Post(() =>
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StatusButton.IsEnabled = false;
        });
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _stopButtonWasJustUsed = true;
        string? lmutilPath = LmutilLocationTextBox.Text;
        string? licenseFilePath = LicenseFileLocationTextBox.Text;
        // Setup file paths to executables.

        if (!string.IsNullOrWhiteSpace(lmutilPath) && !string.IsNullOrWhiteSpace(licenseFilePath))
        {
            BusyWithFlexLm();

            OutputTextBlock.Text = "Loading. Please wait.";

            // Ensure the UI updates before actually doing anything.
            await Task.Delay(100);

            // Perform the "actual code" asynchronously to make sure the UI message displayed first.
            await Task.Run(() => ExecuteStopCommand(lmutilPath, licenseFilePath));
        }
        else
        {
            ShowErrorWindow("You either left your license path or lmutil path empty.");
        }
    }

    private void ExecuteStopCommand(string lmutilPath, string licenseFilePath)
    {
        if (!File.Exists(lmutilPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The lmutil executable was not found at the specified path: {lmutilPath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        // Arguments for lmutil.
        string arguments = $"lmdown -c \"{licenseFilePath}\" -q";

        // Execute the full command!
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

            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            // Read the output and error streams for debugging.
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            // Check the exit code to determine if the command succeeded.
            if (process.ExitCode == 0)
            {
                // The command ran, but that doesn't mean FlexLM actually stopped.
                Console.WriteLine("The command to stop FlexLM executed successfully.");
                Console.WriteLine(output);
            }
            else
            {
                // Command absolutely failed.
                Console.WriteLine("The command to stop FlexLM failed to execute.");
                Console.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                string exceptionString = ex.ToString();
                if ((exceptionString.Contains("Could not find file") || exceptionString.Contains("Could not find a part of the path")) && exceptionString.Contains("lmlog.txt"))
                {
                    ShowErrorWindow("The log file couldn't be found. You either deleted it or haven't started the server from this program yet. :)");
                }
                else if ((exceptionString.Contains("lmutil") || exceptionString.Contains("lmgrd")) && exceptionString.Contains("No such file or directory") && _platform == OSPlatform.Linux)
                {
                    ShowErrorWindow($"The server could not be stopped because lmutil and/or lmgrd was not found. This is likely because you are using a version of FlexLM and/or MLM that requires LSB. " +
                    "Please obtain a newer copy of FlexLM and/or MLM that does not require LSB or install LSB on your system, if it's able to do so.");
                    return;
                }
                else
                {
                    ShowErrorWindow($"Something bad happened when you tried to stop FlexLM. Here's the automatic error message: {ex.Message}");

                }
                FlexLmStatusUnknown();
            });
        }
        Dispatcher.UIThread.Post(CheckStatus);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        string? lmgrdPath = LmgrdLocationTextBox.Text;
        string? licenseFilePath = LicenseFileLocationTextBox.Text;
        string? lmutilPath = LmutilLocationTextBox.Text;

        if (!string.IsNullOrWhiteSpace(lmgrdPath) && !string.IsNullOrWhiteSpace(licenseFilePath) && !string.IsNullOrWhiteSpace(lmutilPath))
        {
            BusyWithFlexLm();
            OutputTextBlock.Text = "Loading. Please wait.";

            // Same deal as the Stop button.
            await Task.Delay(100);
            await Task.Run(() => ExecuteStartCommand(lmgrdPath, licenseFilePath, lmutilPath));
        }
        else
        {
            ShowErrorWindow("You either left your license path or lmgrd path empty.");
        }
    }

    private async void ExecuteStartCommand(string lmgrdPath, string licenseFilePath, string lmutilPath)
    {
        // Setup file paths to executables.
        if (string.IsNullOrWhiteSpace(lmgrdPath) || string.IsNullOrWhiteSpace(licenseFilePath)) return;

        if (!File.Exists(lmgrdPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The lmgrd executable was not found at the specified path: {lmgrdPath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        if (!File.Exists(lmutilPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {lmutilPath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        string arguments;
        string lmLogPath = LmLogPath();

        // Arguments for lmgrd.
        if (_platform == OSPlatform.Windows)
        {
            arguments = $"-c \"{licenseFilePath}\" -l \"{lmLogPath}\"";
        }
        else
        {
            arguments = $"-c \"{licenseFilePath}\" -l \"{lmLogPath}\"";
        }

        // Execute the full command!
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = lmgrdPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new() { StartInfo = startInfo };
            process.Start();

            // Prevent hanging. Timeout after 3 seconds.
            // Start reading the output and error streams asynchronously as part of preventing hanging.
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            Task collectDebugInfo = await Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(3000));

            if (collectDebugInfo == outputTask)
            {
                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode == 0)
                {
                    Console.WriteLine(output);
                }
                else
                {
                    Console.WriteLine(error);
                }
            }
            else
            {
                Console.WriteLine("Getting startup output timed out to prevent hanging.");
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FlexLmStatusUnknown();
                string exceptionString = ex.ToString();

                if ((exceptionString.Contains("lmutil") || exceptionString.Contains("lmgrd")) && exceptionString.Contains("No such file or directory") && _platform == OSPlatform.Linux)
                {
                    ShowErrorWindow($"The server could not be started because lmutil and/or lmgrd was not found. This is likely because you are using a version of FlexLM and/or MLM that requires LSB. " +
                    "Please obtain a newer copy of FlexLM and/or MLM that does not require LSB or install LSB on your system, if it's able to do so.");
                    FlexLmCanStart();
                    return;
                }
                else
                {
                    ShowErrorWindow($"Something bad happened when you tried to start FlexLM. Here's the automatic error message: {ex.Message}");
                }
            });
        }

        await Task.Delay(1000);

        Dispatcher.UIThread.Post(CheckStatus);
    }

    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        CheckStatus();
    }

    private void LogButton_Click(object sender, RoutedEventArgs e)
    {
        string lmLogPath = LmLogPath();
        if (File.Exists(lmLogPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = lmLogPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ShowErrorWindow($"Failed to open log file: {ex.Message}"));
            }
        }
    }

    private async void CheckStatus()
    {
        OutputTextBlock.Text = "Loading. Please wait.";

        // Hopefully increasing this to 1000 will reduce status error -16s.
        await Task.Delay(1000);

        string? lmutilPath = LmutilLocationTextBox.Text;
        string? licenseFilePath = LicenseFileLocationTextBox.Text;
        string lmLogPath = LmLogPath();

        if (!string.IsNullOrWhiteSpace(lmutilPath) && !string.IsNullOrWhiteSpace(licenseFilePath))
        {
            BusyWithFlexLm();

            // Run the long-running code asynchronously
            await Task.Run(() => ExecuteCheckStatus(lmutilPath, licenseFilePath, lmLogPath));
        }
        else
        {
            ShowErrorWindow("You either left your license path or lmutil path empty.");
        }
    }

    private async Task ExecuteCheckStatus(string lmutilPath, string licenseFilePath, string lmLogPath)
    {
        if (!File.Exists(lmutilPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The lmutil executable was not found at the specified path: {lmutilPath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
            Dispatcher.UIThread.Post(FlexLmStatusUnknown);
            return;
        }

        // Arguments for lmutil.
        string arguments = $"lmstat -c \"{licenseFilePath}\" -a";

        // Execute the full command!
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = lmutilPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new() { StartInfo = startInfo };
            process.Start();

            // Read the output and error streams for debugging.
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            // Check the exit code to determine if the command succeeded.
            if (process.ExitCode == 0)
            {
                // The command ran, but that doesn't mean FlexLM actually stopped.
                Console.WriteLine("The command to check FlexLM's status executed successfully.");
                Console.WriteLine(output);

                // Find out why FlexLM is down, from the log file, if necessary.
                string fileContents;
                using (var stream = new FileStream(lmLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    fileContents = await reader.ReadToEndAsync();
                }

                var lines = fileContents.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries);
                var last20Lines = lines.Skip(Math.Max(0, lines.Length - 20));

                // Complete failure to launch.
                if (output.Contains("lmgrd is not running"))
                {
                    if (output.Contains("Cannot read data from license server system. (-16,287)"))
                    {
                        // This seems to mostly come up when you check the server's status too quickly. The delay I currently have seems to be enough time, so I'm not doing anything with this for now.
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (File.Exists(lmLogPath))
                        {

                            if (last20Lines.Any(line => line.Contains("Failed to open the TCP port number in the license.")))
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    ShowErrorWindow("FlexLM is down. One of the ports could not be opened. You either don't have permissions to open the port or it's being used by " +
                                                           "something else. You might need to just wait longer for the desired ports to reopen if you just stopped FlexLM. " +
                                                           "Otherwise, if you're on Linux, I recommend trying TCP port 27011.");
                                });
                            }
                            else if (last20Lines.Any(line => line.Contains("Not a valid server hostname, exiting.")) ||
                            (last20Lines.Any(line => line.Contains("Unknown Hostname: ")) && last20Lines.Any(line => line.Contains("license file is not available in the local network database"))))
                            {
                                ShowErrorWindow("FlexLM is down. The hostname you've specified in your license file's SERVER line is invalid.");
                            }
                            else
                            {
                                if (_stopButtonWasJustUsed || _programWasJustLaunched)
                                {
                                    OutputTextBlock.Text = "FlexLM is down.";
                                    _programWasJustLaunched = false;
                                }
                                else
                                {
                                    ShowErrorWindow("FlexLM is down. Check the log file for more information.");
                                }
                            }

                        }
                        else
                        {
                            if (_stopButtonWasJustUsed || _programWasJustLaunched)
                            {
                                OutputTextBlock.Text = "FlexLM is down.";
                                _programWasJustLaunched = false;
                            }
                            else
                            {
                                ShowErrorWindow("FlexLM is down. Check the log file for more information.");
                            }
                        }
                        FlexLmCanStart();
                    });
                }
                // Successfully launched.
                else if (output.Contains("license server UP (MASTER)") && output.Contains("MLM: UP"))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        OutputTextBlock.Text = "FlexLM is up!";
                        OutputLicenseUsageInfo(output, lmLogPath);
                    });
                    Dispatcher.UIThread.Post(FlexLmCanStop);
                }
                // LMGRD launched, but MLM couldn't launch. Let's see if we can find out why!
                else if (output.Contains("license server UP (MASTER)") && !output.Contains("MLM: UP"))
                {
                    if (output.Contains("MLM: No socket connection to license server manager."))
                    {
                        if (last20Lines.Any(line => line.Contains("Failed to open the TCP port number in the license.")))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. One of the ports could not be opened. You either don't have permissions to open the port or it's being used by " +
                                                       "something else. You might need to just wait longer for the desired ports to reopen if you just stopped FlexLM. " +
                                                       "Otherwise, if you're on Linux, I recommend trying TCP port 27011.";
                            });
                        }
                        else if (last20Lines.Any(line => line.Contains("MLM exited with status 36 (No features to serve)")))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. None of the products in your license file are valid (are you using a license for a different computer?) " +
                                                       "Check lmlog.txt in LMFOOLS's directory for more specific errors.";
                            });
                        }
                        else if (last20Lines.Any(line => line.Contains("(lmgrd) MLM exited with status 2 signal = 17")))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. MLM is likely corrupted. Please obtain a new copy of it. " +
                                                       "If you're using Linux, use 'unzip' instead of Ark to extract any ZIP archives with MLM in it.";
                            });
                        }
                        else if (last20Lines.Any(line => line.Contains("Cannot open license file /usr/local/flexlm/licenses/license.dat")))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                OutputTextBlock.Text = "MLM attempted to use a license file that does not exist. Please place MLM in a directory that this program can access. " +
                                                       "Hint: try placing it in the same directory as your license file or in a user-specific folder.";
                            });
                        }
                        else
                        {
                            Dispatcher.UIThread.Post(() => { OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. Please press the Stop button or manually end the process."; });
                        }
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() => { OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. Please press the Stop button or manually end the process."; });
                    }

                    Dispatcher.UIThread.Post(FlexLmCanStop);
                }
                // We will assume nothing if we can't definitively tell its status.
                else
                {
                    Dispatcher.UIThread.Post(FlexLmStatusUnknown);
                }
            }
            else
            {
                // Command absolutely failed.
                Console.WriteLine("Command failed to execute.");
                Console.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                string exceptionString = ex.ToString();

                if ((exceptionString.Contains("Could not find file") || exceptionString.Contains("Could not find a part of the path")) && exceptionString.Contains("lmlog.txt"))
                {
                    ShowErrorWindow("The log file couldn't be found. You either deleted it or haven't started the server from this program yet. :)");
                }
                else if ((exceptionString.Contains("Could not find file") || exceptionString.Contains("Could not find a part of the path")) &&
                (exceptionString.Contains("lmutil") || exceptionString.Contains("lmgrd") || exceptionString.Contains("MLM")))
                {
                    ShowErrorWindow("FlexLM and/or MLM could not be found. This could either be due to permissions, it being in a place that this program cannot access, " +
                                    "or you have a version of FlexLM and/or MLM that requires LSB and this system does not have LSB. Depending on the age of your system," +
                                    "you may be able to install it. Otherwise, find a newer version that does not require LSB.");
                }
                else if (exceptionString.Contains("lmlog.txt") && exceptionString.Contains("because it is being used by another process"))
                {
                    ShowErrorWindow("You have the log file opened in another program, which is preventing this program from retrieving the server's status.");
                }
                else if ((exceptionString.Contains("lmutil") || exceptionString.Contains("lmgrd")) && exceptionString.Contains("No such file or directory") && _platform == OSPlatform.Linux)
                {
                    FlexLmStatusUnknown();
                    ShowErrorWindow($"The server status could not be checked because lmutil and/or lmgrd was not found. This is likely because you are using a version of FlexLM and/or MLM that requires LSB. " +
                    "Please obtain a newer copy of FlexLM and/or MLM that does not require LSB or install LSB on your system, if it's able to do so.");
                    return;
                }
                else
                {
                    ShowErrorWindow($"Something bad happened when you tried to check FlexLM status. Here's the automatic error message: {ex.Message}");
                }
                FlexLmStatusUnknown();
            });
        }
    }

    private async void OutputLicenseUsageInfo(string output, string lmLogPath)
    {
        // Regex patterns used to parse needed info.
        string usagePattern = @"Users of (\w+):\s+\(Total of (\d+) license[s]? issued;\s+Total of (\d+) license[s]? in use\)";
        string errorPattern = @"Users of (\w+):\s+\(Error: (\d+) license[s]?, unsupported by licensed server\)";
        string formattedOutputText;
        string[] logLines;
        string logFileContents;
        IEnumerable<string> last50Lines;

        MatchCollection usageMatches = Regex.Matches(output, usagePattern);
        MatchCollection errorMatches = Regex.Matches(output, errorPattern);

        OutputTextBlock.Text += "\n"; // Give a bit of space.

        // Find out why your product is unavailable. Check for other errors too.
        try
        {
            using (var stream = new FileStream(lmLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                logFileContents = await reader.ReadToEndAsync();
            }

            logLines = logFileContents.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries);
            last50Lines = logLines.Skip(Math.Max(0, logLines.Length - 50));
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("lmlog.txt' because it is being used by another process."))
            {
                ShowErrorWindow("Another program is using the log file. Please close that program in order to proceed.");
                return;
            }
            else
            {
                ShowErrorWindow("Something bad happened when attempting to display the server's status: " + ex.Message);
                return;
            }
        }

        // Iterate through the error matches first.
        foreach (Match match in errorMatches)
        {
            if (match.Success)
            {
                string product = match.Groups[1].Value;

                if (File.Exists(lmLogPath))
                {
                    try
                    {
                        bool causeWasFound = false;

                        // Yes I'm counting the lines so I can jump between different ones in my if cases below.
                        for (int i = 0; i < logLines.Length; i++)
                        {
                            var line = logLines[i];

                            if (line.Contains($"EXPIRED: {product}"))
                            {
                                OutputTextBlock.Text += $"\n{product} is expired and cannot be used.";
                                causeWasFound = true;
                                break;
                            }
                            else if (line.Contains("Invalid license key (inconsistent authentication code)"))
                            {
                                if (i + 1 < logLines.Length && logLines[i + 1].Contains($"==>INCREMENT {product}"))
                                {
                                    OutputTextBlock.Text += $"\n{product} has an invalid authentication key and needs its license file to be regenerated.";
                                    causeWasFound = true;
                                    break;
                                }
                            }
                        }

                        if (!causeWasFound)
                        {
                            OutputTextBlock.Text += $"\n{product}: an error is preventing this product from being used.";
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorWindow($"Something bad happened when we tried to print out the server's status info. Here's the automatic error message: {ex.Message}");
                    }
                }
            }
        }

        if (last50Lines.Any(line => line.Contains("(MLM) CANNOT OPEN options file")) && !last50Lines.Any(line => line.Contains("options file \"License\"")))
        {
            OutputTextBlock.Text += "\nYour options file could not be opened. Make sure the path to it in your license file is correct.";
        }

        if (errorMatches.Count != 0)
        {
            OutputTextBlock.Text += "\n";
        }

        // Now iterate through the usage matches.
        foreach (Match match in usageMatches)
        {
            if (match.Success)
            {
                string product = match.Groups[1].Value;
                string totalSeats = match.Groups[2].Value;
                string seatsInUse = match.Groups[3].Value;

                // Stupid grammar.
                if (totalSeats == "1")
                {
                    formattedOutputText = $"\n{product}: {seatsInUse}/{totalSeats} seat in use.";
                }
                else
                {
                    formattedOutputText = $"\n{product}: {seatsInUse}/{totalSeats} seats in use.";
                }
                OutputTextBlock.Text += formattedOutputText;
            }
        }
    }
}