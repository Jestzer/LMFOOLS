using System;
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
            return OSPlatform.Windows;
        }

        return OSPlatform.Create("UNKNOWN");
    }

    private bool _flexLmIsAlreadyRunning;

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
        else { _flexLmIsAlreadyRunning = false; }

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
    internal partial class SettingsJsonContext : JsonSerializerContext
    {
    }

    private static void SaveSettings(Settings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);
        File.WriteAllText("settings.json", jsonString);
    }

    private static Settings LoadSettings()
    {
        if (!File.Exists("settings.json"))
        {
            return new Settings(); // Return default settings if file not found.
        }

        string jsonString = File.ReadAllText("settings.json");
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

                    if (filePath != null)
                    {
                        if (filePath.Contains("/run/user/1000/doc/"))
                        {
                            ShowErrorWindow(
                                "There is an issue with the license file: it resides in a directory that this program does not have access to. Please move it elsewhere or choose a different file.");
                            LicenseFileLocationTextBox.Text = string.Empty;
                            return;
                        }
                    }

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
        StatusButton.IsEnabled = true;

        if (_stopButtonWasJustUsed)
        {
            Dispatcher.UIThread.Post(() => OutputTextBlock.Text += " The start button will be available to use in 5 seconds. This is to ensure FlexLM has fully stopped.");
            await Task.Delay(5000); // Wait 5 seconds.
        }
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
        OutputTextBlock.Text = "Status unknown";

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
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
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
                Console.WriteLine("Command executed successfully.");
                Console.WriteLine(output);
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
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"Something bad happened when you tried to stop FlexLM. Here's the automatic error message: {ex.Message}"));
            FlexLmStatusUnknown();
        }

        Dispatcher.UIThread.Post(CheckStatus);
    }

    private readonly string _currentDirectory = Environment.CurrentDirectory;

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        string? lmgrdPath = LmgrdLocationTextBox.Text;
        string? licenseFilePath = LicenseFileLocationTextBox.Text;

        if (!string.IsNullOrWhiteSpace(lmgrdPath) && !string.IsNullOrWhiteSpace(licenseFilePath))
        {
            BusyWithFlexLm();
            OutputTextBlock.Text = "Loading. Please wait.";

            // Same deal as the Stop button.
            await Task.Delay(100);
            await Task.Run(() => ExecuteStartCommand(lmgrdPath, licenseFilePath));
        }
        else
        {
            ShowErrorWindow("You either left your license path or lmgrd path empty.");
        }
    }

    private async void ExecuteStartCommand(string lmgrdPath, string licenseFilePath)
    {
        // Setup file paths to executables.
        if (string.IsNullOrWhiteSpace(lmgrdPath) || string.IsNullOrWhiteSpace(licenseFilePath)) return;

        if (!File.Exists(lmgrdPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The lmgrd executable was not found at the specified path: {lmgrdPath}"));
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
            return;
        }

        string arguments;

        // Arguments for lmgrd.
        if (_platform == OSPlatform.Windows)
        {
            arguments = $"-c \"{licenseFilePath}\" -l \"{_currentDirectory}\\lmlog.txt\"";
        }
        else
        {
            arguments = $"-c \"{licenseFilePath}\" -l \"{_currentDirectory}/lmlog.txt\"";
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

            // Read the output and error streams for debugging.
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            if (process.ExitCode == 0)
            {
                Console.WriteLine(output);
            }
            else
            {
                Console.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"Something bad happened when you tried to start FlexLM. Here's the automatic error message: {ex.Message}"));
        }

        await Task.Delay(1000);

        Dispatcher.UIThread.Post(CheckStatus);
    }

    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        CheckStatus();
    }

    private async void CheckStatus()
    {
        OutputTextBlock.Text = "Loading. Please wait.";

        await Task.Delay(100);

        string? lmutilPath = LmutilLocationTextBox.Text;
        string? licenseFilePath = LicenseFileLocationTextBox.Text;
        string lmLogPath;

        if (!string.IsNullOrWhiteSpace(lmutilPath) && !string.IsNullOrWhiteSpace(licenseFilePath))
        {
            BusyWithFlexLm();

            if (_platform == OSPlatform.Windows)
            {
                lmLogPath = _currentDirectory + "\\lmlog.txt";
            }
            else
            {
                lmLogPath = _currentDirectory + "/lmlog.txt";
            }

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
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
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
                Console.WriteLine("Command executed successfully.");
                Console.WriteLine(output);

                // Complete failure to launch.
                if (output.Contains("lmgrd is not running"))
                {
                    if (File.Exists(lmLogPath))
                    {
                        // Find out why FlexLM is down.
                        string fileContents;
                        using (var stream = File.OpenRead(lmLogPath))
                        using (var reader = new StreamReader(stream))
                        {
                            fileContents = await reader.ReadToEndAsync();
                        }

                        var lines = fileContents.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries);
                        var last20Lines = lines.Skip(Math.Max(0, lines.Length - 20));

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (last20Lines.Any(line => line.Contains("Failed to open the TCP port number in the license.")))
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    OutputTextBlock.Text = "FlexLM is down. One of the ports could not be opened. You either don't have permissions to open the port or it's being used by " +
                                                       "something else.";
                                });
                            }
                            else
                            {
                                Dispatcher.UIThread.Post(() => { OutputTextBlock.Text = "FlexLM is down."; });
                            }
                        });
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() => { OutputTextBlock.Text = "FlexLM is down."; });
                    }
                    Dispatcher.UIThread.Post(FlexLmCanStart);
                }
                // Successfully launched.
                else if (output.Contains("license server UP (MASTER)") && output.Contains("MLM: UP"))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        OutputTextBlock.Text = "FlexLM is up!";
                        FlexLmCanStop();
                        OutputLicenseUsageInfo(output, lmLogPath);
                    });
                }
                // LMGRD launched, but MLM couldn't launch. Let's see if we can find out why!
                else if (output.Contains("license server UP (MASTER)") && !output.Contains("MLM: UP"))
                {
                    if (output.Contains("MLM: No socket connection to license server manager."))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            OutputTextBlock.Text = "LMGRD was able to start, but MLM could not due it being unable to use the port it attempted to use. Please press the Stop button or " +
                                                   "manually end the process. Then, please manually specify a port this program is allowed to use (try 27011, for example.)";
                            FlexLmCanStop();
                        });
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. Please press the Stop button or manually end the process.";
                            FlexLmCanStop();
                        });
                    }
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
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"Something bad happened when you tried to check FlexLM status. Here's the automatic error message: {ex.Message}"));
            FlexLmStatusUnknown();
        }
    }

    private async void OutputLicenseUsageInfo(string output, string lmLogPath)
    {
        // Regex patterns used to parse needed info.
        string usagePattern = @"Users of (\w+):\s+\(Total of (\d+) license[s]? issued;\s+Total of (\d+) license[s]? in use\)";
        string errorPattern = @"Users of (\w+):\s+\(Error: (\d+) license[s]?, unsupported by licensed server\)";
        string formattedOutputText;

        MatchCollection usageMatches = Regex.Matches(output, usagePattern);
        MatchCollection errorMatches = Regex.Matches(output, errorPattern);

        OutputTextBlock.Text += "\n"; // Give a bit of space.

        // Iterate through the usage matches.
        foreach (Match match in usageMatches)
        {
            if (match.Success)
            {
                string product = match.Groups[1].Value;
                string totalSeats = match.Groups[2].Value;
                string seatsInUse = match.Groups[3].Value;

                formattedOutputText = $"\n{product}: {seatsInUse}/{totalSeats} seats in use.";
                OutputTextBlock.Text += formattedOutputText;
            }
        }

        // Iterate through the error matches.
        foreach (Match match in errorMatches)
        {
            if (match.Success)
            {
                string product = match.Groups[1].Value;

                if (File.Exists(lmLogPath))
                {
                    try
                    {
                        // Find out why your product is unavailable.
                        string fileContents;
                        using (var stream = new FileStream(lmLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(stream))
                        {
                            fileContents = await reader.ReadToEndAsync();
                        }

                        var lines = fileContents.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries);
                        bool causeWasFound = false;

                        // Yes I'm counting the lines so I can jump between different ones in my if cases below.
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i];

                            if (line.Contains($"EXPIRED: {product}"))
                            {
                                formattedOutputText = $"\n{product}: is expired and cannot be used.";
                                OutputTextBlock.Text += formattedOutputText;
                                causeWasFound = true;
                                break;
                            }
                            else if (line.Contains("Invalid license key (inconsistent authentication code)"))
                            {
                                if (i + 1 < lines.Length && lines[i + 1].Contains($"==>INCREMENT {product}"))
                                {
                                    formattedOutputText = $"\n{product}: has an invalid authentication key and needs its license file to be regenerated.";
                                    OutputTextBlock.Text += formattedOutputText;
                                    causeWasFound = true;
                                    break;
                                }
                            }
                        }

                        if (!causeWasFound)
                        {
                            formattedOutputText = $"\n{product}: an error is preventing this product from being used.";
                            OutputTextBlock.Text += formattedOutputText;
                        }
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
                            ShowErrorWindow("There was an error when attempting to display the server's status: " + ex.Message);
                            return;
                        } 
                    }
                }
            }
        }
    }
}