using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
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
using LMFOOLS_Project.ViewModels;
using Microsoft.Win32;

namespace LMFOOLS_Project.Views;

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
    private sealed record WindowsServiceInfo(string ServiceName, string DisplayName, string ImagePath);

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

    private static string? NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        string trimmed = rawPath.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static string? ExtractExecutablePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string expanded = Environment.ExpandEnvironmentVariables(imagePath).Trim();

        if (expanded.StartsWith("\"", StringComparison.Ordinal))
        {
            int endQuote = expanded.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return expanded.Substring(1, endQuote - 1);
            }
        }

        int firstSpace = expanded.IndexOf(' ');
        if (firstSpace > 0)
        {
            return expanded.Substring(0, firstSpace);
        }

        return expanded;
    }

    private WindowsServiceInfo? TryGetLmgrdWindowsService(string? lmgrdPath)
    {
        if (_platform != OSPlatform.Windows)
        {
            return null;
        }

        try
        {
            string? normalizedLmgrdPath = NormalizePath(lmgrdPath);
            using RegistryKey? servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (servicesKey == null) return null;

            WindowsServiceInfo? fallbackMatch = null;

            foreach (string serviceName in servicesKey.GetSubKeyNames())
            {
                using RegistryKey? serviceKey = servicesKey.OpenSubKey(serviceName);
                string? imagePath = serviceKey?.GetValue("ImagePath") as string;
                if (string.IsNullOrWhiteSpace(imagePath)) continue;

                string? executablePath = ExtractExecutablePath(imagePath);
                if (string.IsNullOrWhiteSpace(executablePath)) continue;

                string displayName = serviceKey?.GetValue("DisplayName") as string ?? serviceName;

                if (!string.IsNullOrWhiteSpace(normalizedLmgrdPath))
                {
                    string? normalizedExecutablePath = NormalizePath(executablePath);
                    if (!string.IsNullOrWhiteSpace(normalizedExecutablePath) &&
                        string.Equals(normalizedExecutablePath, normalizedLmgrdPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return new WindowsServiceInfo(serviceName, displayName, imagePath);
                    }
                }

                if (fallbackMatch == null && executablePath.Contains("lmgrd", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackMatch = new WindowsServiceInfo(serviceName, displayName, imagePath);
                }
            }

            return fallbackMatch;
        }
        catch
        {
            return null;
        }
    }

    private static string WindowsServiceDetectedMessage(WindowsServiceInfo info)
    {
        return $"Detected Windows Service: {info.DisplayName} (service name: {info.ServiceName}).";
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Parses the -l flag from an lmgrd command line string to find the log file path.
    private static string? ParseLogPathFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;

        // Handle /proc/pid/cmdline format (null-byte separated).
        if (commandLine.Contains('\0'))
        {
            string[] args = commandLine.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-l")
                {
                    string logPath = args[i + 1].TrimStart('+').Trim('"');
                    if (!string.IsNullOrWhiteSpace(logPath)) return logPath;
                }
            }

            return null;
        }

        // Handle regular command line format (space-separated, possibly with quotes).
        int index = 0;
        while (index < commandLine.Length)
        {
            index = commandLine.IndexOf("-l", index, StringComparison.Ordinal);
            if (index < 0) return null;

            // Verify -l is a standalone flag (not part of another word).
            bool validStart = index == 0 || char.IsWhiteSpace(commandLine[index - 1]);
            bool validEnd = index + 2 < commandLine.Length && char.IsWhiteSpace(commandLine[index + 2]);

            if (validStart && validEnd)
            {
                // Skip past "-l" and whitespace to find the path.
                int pathStart = index + 2;
                while (pathStart < commandLine.Length && char.IsWhiteSpace(commandLine[pathStart])) pathStart++;
                if (pathStart >= commandLine.Length) return null;

                string remaining = commandLine[pathStart..];

                // Strip leading + (append mode indicator).
                if (remaining.StartsWith('+')) remaining = remaining[1..];

                if (remaining.StartsWith('"'))
                {
                    int endQuote = remaining.IndexOf('"', 1);
                    if (endQuote > 1) return remaining[1..endQuote];
                }
                else
                {
                    int end = remaining.IndexOf(' ');
                    return end > 0 ? remaining[..end] : remaining;
                }
            }

            index += 2;
        }

        return null;
    }

    // Retrieves the command line of a running process by PID (platform-specific).
    private string? GetProcessCommandLine(int pid)
    {
        try
        {
            if (_platform == OSPlatform.Linux)
            {
                string cmdlinePath = $"/proc/{pid}/cmdline";
                if (File.Exists(cmdlinePath))
                {
                    return File.ReadAllText(cmdlinePath);
                }
            }
            else if (_platform == OSPlatform.OSX)
            {
                using Process psProcess = new();
                psProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = $"-p {pid} -o args=",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psProcess.Start();
                string output = psProcess.StandardOutput.ReadToEnd();
                psProcess.WaitForExit();
                if (psProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)) return output.Trim();
            }
            else if (_platform == OSPlatform.Windows)
            {
                using Process wmicProcess = new();
                wmicProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = $"process where \"processid={pid}\" get CommandLine /value",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                wmicProcess.Start();
                string output = wmicProcess.StandardOutput.ReadToEnd();
                wmicProcess.WaitForExit();
                if (wmicProcess.ExitCode == 0)
                {
                    foreach (string line in output.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("CommandLine=", StringComparison.OrdinalIgnoreCase))
                        {
                            return trimmed["CommandLine=".Length..];
                        }
                    }
                }
            }
        }
        catch
        {
            // If we can't get the command line, fall through to return null.
        }

        return null;
    }

    // Tries to detect the actual log file path that lmgrd is using.
    // Returns null if it can't be determined (caller should fall back to LmLogPath()).
    private string? TryDetectLmgrdLogPath(WindowsServiceInfo? serviceInfo, string? lmgrdPath)
    {
        // 1. If a Windows Service is detected, parse -l from the service's ImagePath.
        if (serviceInfo != null)
        {
            string? logPath = ParseLogPathFromCommandLine(serviceInfo.ImagePath);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                // Try to resolve relative paths against lmgrd's directory.
                try
                {
                    if (!Path.IsPathRooted(logPath))
                    {
                        string? serviceExePath = ExtractExecutablePath(serviceInfo.ImagePath);
                        string? serviceDir = !string.IsNullOrWhiteSpace(serviceExePath) ? Path.GetDirectoryName(serviceExePath) : null;
                        if (!string.IsNullOrWhiteSpace(serviceDir))
                        {
                            logPath = Path.GetFullPath(Path.Combine(serviceDir, logPath));
                        }
                    }
                }
                catch
                {
                    // If path resolution fails, try the path as-is.
                }

                if (File.Exists(logPath)) return logPath;
            }
        }

        // 2. Try to find a running lmgrd process and parse its command line.
        try
        {
            Process[] processes = Process.GetProcessesByName("lmgrd");
            foreach (var proc in processes)
            {
                try
                {
                    string? cmdLine = GetProcessCommandLine(proc.Id);
                    if (!string.IsNullOrWhiteSpace(cmdLine))
                    {
                        string? logPath = ParseLogPathFromCommandLine(cmdLine);
                        if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
                        {
                            return logPath;
                        }
                    }
                }
                catch
                {
                    // Continue to the next process.
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Process enumeration failed, fall through.
        }

        // 3. Check for lmlog.txt in lmgrd's directory. If it has newer entries than the
        //    program's default log, it's likely the one being actively used.
        try
        {
            string? lmgrdDir = !string.IsNullOrWhiteSpace(lmgrdPath) ? Path.GetDirectoryName(lmgrdPath) : null;
            if (!string.IsNullOrWhiteSpace(lmgrdDir))
            {
                string candidateLogPath = Path.Combine(lmgrdDir, "lmlog.txt");
                if (File.Exists(candidateLogPath))
                {
                    string defaultLogPath = LmLogPath();
                    if (!File.Exists(defaultLogPath))
                    {
                        // The program's default log doesn't exist, so the one next to lmgrd is our best bet.
                        return candidateLogPath;
                    }

                    // Both exist. Prefer whichever was written to more recently.
                    DateTime candidateLastWrite = File.GetLastWriteTimeUtc(candidateLogPath);
                    DateTime defaultLastWrite = File.GetLastWriteTimeUtc(defaultLogPath);
                    if (candidateLastWrite > defaultLastWrite)
                    {
                        return candidateLogPath;
                    }
                }
            }
        }
        catch
        {
            // If the heuristic fails, fall through.
        }

        return null;
    }

    private bool TryStartWindowsService(WindowsServiceInfo info, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            using ServiceController controller = new(info.ServiceName);
            controller.Refresh();

            if (controller.Status == ServiceControllerStatus.Running)
            {
                return true;
            }

            if (controller.Status == ServiceControllerStatus.StartPending)
            {
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                controller.Refresh();
                return controller.Status == ServiceControllerStatus.Running;
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            controller.Refresh();
            return controller.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private bool TryStopWindowsService(WindowsServiceInfo info, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            using ServiceController controller = new(info.ServiceName);
            controller.Refresh();

            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                return true;
            }

            if (controller.Status == ServiceControllerStatus.StopPending)
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                controller.Refresh();
                return controller.Status == ServiceControllerStatus.Stopped;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            controller.Refresh();
            return controller.Status == ServiceControllerStatus.Stopped;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private async void ExecuteWindowsServiceStatus(WindowsServiceInfo info, string lmLogPath)
    {
        try
        {
            using ServiceController controller = new(info.ServiceName);
            controller.Refresh();
            ServiceControllerStatus status = controller.Status;
            bool isAdmin = IsRunningAsAdministrator();

            // If the service is running, try to get seat info via lmstat.
            string? lmstatOutput = null;
            if (status == ServiceControllerStatus.Running)
            {
                string? lmutilPath = null;
                string? licenseFilePath = null;
                Dispatcher.UIThread.Post(() =>
                {
                    lmutilPath = LmutilLocationTextBox.Text;
                    licenseFilePath = LicenseFileLocationTextBox.Text;
                });

                // Small delay to let the UI thread process the above.
                await Task.Delay(50);

                if (!string.IsNullOrWhiteSpace(lmutilPath) && !string.IsNullOrWhiteSpace(licenseFilePath) &&
                    File.Exists(lmutilPath) && File.Exists(licenseFilePath))
                {
                    try
                    {
                        ProcessStartInfo startInfo = new()
                        {
                            FileName = lmutilPath,
                            Arguments = $"lmstat -c \"{licenseFilePath}\" -a",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using Process process = new() { StartInfo = startInfo };
                        process.Start();
                        lmstatOutput = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (process.ExitCode != 0)
                        {
                            lmstatOutput = null;
                        }
                    }
                    catch
                    {
                        lmstatOutput = null;
                    }
                }
            }

            string capturedLmstatOutput = lmstatOutput ?? "";
            Dispatcher.UIThread.Post(() =>
            {
                string statusText = status switch
                {
                    ServiceControllerStatus.Running => "running",
                    ServiceControllerStatus.Stopped => "stopped",
                    ServiceControllerStatus.StartPending => "starting",
                    ServiceControllerStatus.StopPending => "stopping",
                    ServiceControllerStatus.ContinuePending => "continuing",
                    ServiceControllerStatus.PausePending => "pausing",
                    ServiceControllerStatus.Paused => "paused",
                    _ => "unknown"
                };

                OutputTextBlock.Text = $"{WindowsServiceDetectedMessage(info)}\nFlexLM Windows Service is {statusText}.";

                if (!isAdmin)
                {
                    OutputTextBlock.Text += "\nThis program is not running as administrator. Start and Stop are disabled for Windows Services.";
                }

                switch (status)
                {
                    case ServiceControllerStatus.Running:
                        if (isAdmin)
                        {
                            FlexLmCanStop();
                        }
                        else
                        {
                            StartButton.IsEnabled = false;
                            StopButton.IsEnabled = false;
                            StatusButton.IsEnabled = true;
                            _programWasJustLaunched = false;
                        }

                        // Show seat info if lmstat succeeded.
                        if (!string.IsNullOrWhiteSpace(capturedLmstatOutput) &&
                            capturedLmstatOutput.Contains("license server UP (MASTER)") &&
                            capturedLmstatOutput.Contains("MLM: UP"))
                        {
                            OutputLicenseUsageInfo(capturedLmstatOutput, lmLogPath);
                        }

                        break;
                    case ServiceControllerStatus.Stopped:
                        if (isAdmin)
                        {
                            FlexLmCanStart();
                        }
                        else
                        {
                            StartButton.IsEnabled = false;
                            StopButton.IsEnabled = false;
                            StatusButton.IsEnabled = true;
                            _programWasJustLaunched = false;
                        }
                        break;
                    case ServiceControllerStatus.StartPending:
                    case ServiceControllerStatus.StopPending:
                        FlexLmIsAttemptingToRestart();
                        break;
                    default:
                        FlexLmStatusUnknown();
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ShowErrorWindow($"An error occurred when attempting to check Windows Service status. Here is the automatic error message: {ex.Message}");
                FlexLmStatusUnknown();
            });
        }
    }

    [JsonSerializable(typeof(Settings))]
    internal partial class SettingsJsonContext : JsonSerializerContext { }

    private static (string settingsPath, string lmLogPath) DeterminePaths()
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

    private static string SettingsPath()
    {
        var (settingsPath, _) = DeterminePaths();
        return settingsPath;
    }

    private static string LmLogPath()
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
        try
        {
            OutputTextBlock.Text = errorMessage;
            ErrorWindow errorWindow = new();
            errorWindow.ErrorTextBlock.Text = errorMessage;

            // Check if VisualRoot is not null and is a Window before casting
            if (VisualRoot is Window window)
            {
                await errorWindow.ShowDialog(window);
            }
            else
            {
                OutputTextBlock.Text = "The error window broke somehow. Please make an issue for this in GitHub.";
            }
        }
        catch (Exception ex)
        {
            OutputTextBlock.Text = "In an attempt to show the Error Window, an error occurred. " +
            $"This is the automatic error message that was generated: {ex.Message}";
        }
    }

    private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var updateWindow = new UpdateWindow();

            await updateWindow.ShowDialog(this); // Putting this here, otherwise it won't center it on the MainWindow. Sorryyyyy.
        }
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to check for updates. Here is the automatic error message: {ex.Message}");
        }
    }

    private async void LicenseFileBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
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
                if (!result.Any()) return;
                var selectedFile = result[0];
                {
                    // Get file properties.
                    var properties = await selectedFile.GetBasicPropertiesAsync();
                    if (properties.Size != null)
                    {
                        long fileSizeInBytes = (long)properties.Size;
                        const long fiftyMegabytes = 50L * 1024L * 1024L;

                        // Check the file size.
                        if (fileSizeInBytes > fiftyMegabytes)
                        {
                            ShowErrorWindow("There is an issue with the license file: it is over 50 MB and therefore, (hopefully) not a license file.");
                            LicenseFileLocationTextBox.Text = string.Empty;
                            return;
                        }

                        // If size is within the limit, read the contents.
                        string fileContents;
                        await using (var stream = await selectedFile.OpenReadAsync())
                        using (var reader = new StreamReader(stream))
                        {
                            fileContents = await reader.ReadToEndAsync();
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
                                "There is an issue with the license file: it contains an Individual, Designated Computer, or Counted Individual license.");
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
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to browse for a license file. Here is the automatic error message: {ex.Message}");
        }
    }

    private async void LmgrdBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
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
                        new FilePickerFileType("lmgrd executable") { Patterns = ["lmgr*"] },
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
                        const long twoMegabytes = 4L * 1024L * 1024L;

                        if (fileSizeInBytes > twoMegabytes)
                        {
                            ShowErrorWindow("Error: the selected file is over 4 MB, which is unexpectedly large for lmgrd. I will assume is this not lmgrd.");
                            LmgrdLocationTextBox.Text = string.Empty;
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(fileContents))
                        {
                            ShowErrorWindow("Error: there is an issue with lmgrd: it is either empty or is read-only.");
                            LmgrdLocationTextBox.Text = string.Empty;
                            return;
                        }

                        // Gotta convert some things, ya know?
                        var rawFilePath = selectedFile.TryGetLocalPath;
                        string? filePath = rawFilePath();

                        if (filePath != null)
                        {
                            if (filePath.EndsWith("lmutil") || filePath.EndsWith("lmutil.exe"))
                            {
                                ShowErrorWindow("Error: you selected lmutil instead of lmgrd.");
                                LmgrdLocationTextBox.Text = string.Empty;
                                return;
                            }
                            else if (!filePath.EndsWith("lmgrd") && !filePath.EndsWith("lmgrd.exe"))
                            {
                                ShowErrorWindow("Error: you selected a file other than lmgrd.");
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
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to browse for lmgrd. Here is the automatic error message: {ex.Message}");
        }
    }

    private async void LmutilBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
            {
                var mainWindow = desktop.MainWindow;

                var filePickerOptions = new FilePickerOpenOptions
                {
                    Title = "Select lmutil",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("lmutil") { Patterns = ["lmu*"] },
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
                        const long twoMegabytes = 4L * 1024L * 1024L;

                        if (fileSizeInBytes > twoMegabytes)
                        {
                            ShowErrorWindow("The selected file is over 4 MB, which is unexpectedly large for lmutil. I will assume is this not lmutil.");
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
                            if (filePath.EndsWith("lmgrd") || filePath.EndsWith("lmgrd.exe"))
                            {
                                ShowErrorWindow("Error: you selected lmgrd instead of lmutil.");
                                LmgrdLocationTextBox.Text = string.Empty;
                                return;
                            }
                            else if (!filePath.EndsWith("lmutil") && !filePath.EndsWith("lmutil.exe"))
                            {
                                ShowErrorWindow("Error: you selected a file other than lmutil.");
                                LmgrdLocationTextBox.Text = string.Empty;
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
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to browse for lmutil. Here is the automatic error message: {ex.Message}");
        }
    }

    private bool _stopButtonWasJustUsed;

    private async void FlexLmCanStart()
    {
        try
        {
            StopButton.IsEnabled = false;

            if (_stopButtonWasJustUsed)
            {
                StatusButton.IsEnabled = false;

                // At one point, I tried using a method that detected if the ports were opened, but it was always reporting them as opened after shutting down FlexLM.
                // netstat commands suggested the same thing: the ports were opened, but FlexLM insisted they weren't, and I had to wait longer.
                // This suggests that there's no reliable way of telling if FlexLM thinks the ports are opened without actually starting FlexLM. Rather than doing that,
                // I've elected to just make the user wait. For whatever reason, Unix-based/like platforms seem to take longer than Windows. My guess is that
                // FlexLM just hates non-Windows users. I mean, they don't even get a shitty GUI to use!
                if (_platform == OSPlatform.Windows)
                {
                    Dispatcher.UIThread.Post(() => OutputTextBlock.Text += " The start button will be available to use in 5 seconds. This is to ensure FlexLM has fully stopped " +
                                                                           "and the desired TCP ports are opened.");
                    await Task.Delay(5000); // Wait 5 seconds.
                }
                else // macOS and Linux seem to take longer. The time below doesn't guarantee anything, but it'll hopefully do the trick most of the time.
                {
                    Dispatcher.UIThread.Post(() => OutputTextBlock.Text += " The start button will be available to use in 60 seconds. This is to better ensure FlexLM has fully stopped " +
                                                                           "and the desired TCP ports are opened.");
                    await Task.Delay(60000); // Wait 60 seconds.
                }
            }

            StatusButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            _stopButtonWasJustUsed = false;
            _programWasJustLaunched = false;
        }
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to change FlexLM's status to say it can start. Here is the automatic error message: {ex.Message}");
        }
    }

    private void FlexLmCanStop()
    {
        StopButton.IsEnabled = true;
        StartButton.IsEnabled = false;
        StatusButton.IsEnabled = true;
        _programWasJustLaunched = false;
    }

    private void FlexLmIsAttemptingToRestart()
    {
        StopButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        StatusButton.IsEnabled = true;
        _programWasJustLaunched = false;
    }

    private void FlexLmStatusUnknown()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        StatusButton.IsEnabled = true;
        OutputTextBlock.Text = "Status unknown.";
        _programWasJustLaunched = false;
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
    private bool _wantToAttemptForcedShutdown = false;
    private bool _firstAttemptToForceShutdown = true;

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _stopButtonWasJustUsed = true;

            // Setup file paths to executables.
            string? lmutilPath = LmutilLocationTextBox.Text;
            string? licenseFilePath = LicenseFileLocationTextBox.Text;
            string? lmgrdPath = LmgrdLocationTextBox.Text;

            if (!string.IsNullOrWhiteSpace(lmutilPath) && !string.IsNullOrWhiteSpace(licenseFilePath))
            {
                BusyWithFlexLm();

                OutputTextBlock.Text = "Loading. Please wait.";

                // Ensure the UI updates before actually doing anything.
                await Task.Delay(100);

                // Perform the "actual code" asynchronously to make sure the UI message displayed first.
                await Task.Run(() => ExecuteStopCommand(lmutilPath, licenseFilePath, lmgrdPath));
            }
            else
            {
                ShowErrorWindow("You either left your license path or lmutil path empty.");
            }
        }
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to use the Stop button. Here is the automatic error message: {ex.Message}");
        }
    }

    private void ExecuteStopCommand(string lmutilPath, string licenseFilePath, string? lmgrdPath)
    {
        WindowsServiceInfo? serviceInfo = TryGetLmgrdWindowsService(lmgrdPath);
        if (serviceInfo != null)
        {
            if (!IsRunningAsAdministrator())
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ShowErrorWindow("Cannot stop a Windows Service without administrator privileges. Please restart this program as administrator.");
                    CheckStatus();
                });
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                OutputTextBlock.Text = $"{WindowsServiceDetectedMessage(serviceInfo)}\nStopping Windows Service. Please wait.";
            });

            if (TryStopWindowsService(serviceInfo, out string? errorMessage))
            {
                Dispatcher.UIThread.Post(CheckStatus);
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ShowErrorWindow($"Failed to stop Windows Service \"{serviceInfo.DisplayName}\" ({serviceInfo.ServiceName}). {errorMessage}");
                    FlexLmStatusUnknown();
                });
            }

            return;
        }

        if (!File.Exists(lmutilPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"lmutil was not found at the specified path: {lmutilPath}"));
            Dispatcher.UIThread.Post(BusyWithFlexLm);
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
            Dispatcher.UIThread.Post(BusyWithFlexLm);
            return;
        }

        // Arguments for lmutil.
        string arguments = $"lmdown -c \"{licenseFilePath}\" -q";

        if (_wantToAttemptForcedShutdown)
        { arguments = $"lmdown -c \"{licenseFilePath}\" -q -force"; }

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
        try
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
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to use the Start button. Here is the automatic error message: {ex.Message}");
        }
    }

    private async void ExecuteStartCommand(string lmgrdPath, string licenseFilePath, string lmutilPath)
    {
        try
        {
            // Setup file paths to executables.
            if (string.IsNullOrWhiteSpace(lmgrdPath) || string.IsNullOrWhiteSpace(licenseFilePath)) return;

            WindowsServiceInfo? serviceInfo = TryGetLmgrdWindowsService(lmgrdPath);
            if (serviceInfo != null)
            {
                if (!IsRunningAsAdministrator())
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ShowErrorWindow("Cannot start a Windows Service without administrator privileges. Please restart this program as administrator.");
                        CheckStatus();
                    });
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    OutputTextBlock.Text = $"{WindowsServiceDetectedMessage(serviceInfo)}\nStarting Windows Service. Please wait.";
                });

                if (TryStartWindowsService(serviceInfo, out string? errorMessage))
                {
                    Dispatcher.UIThread.Post(CheckStatus);
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ShowErrorWindow($"Failed to start Windows Service \"{serviceInfo.DisplayName}\" ({serviceInfo.ServiceName}). {errorMessage}");
                        FlexLmStatusUnknown();
                    });
                }

                return;
            }

            if (!File.Exists(lmgrdPath))
            {
                Dispatcher.UIThread.Post(() => ShowErrorWindow($"lmgrd was not found at the specified path: {lmgrdPath}"));
                Dispatcher.UIThread.Post(BusyWithFlexLm);
                return;
            }

            if (!File.Exists(licenseFilePath))
            {
                Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
                Dispatcher.UIThread.Post(BusyWithFlexLm);
                return;
            }

            if (!File.Exists(lmutilPath))
            {
                Dispatcher.UIThread.Post(() => ShowErrorWindow($"lmutil was not found at the specified path: {lmutilPath}"));
                Dispatcher.UIThread.Post(BusyWithFlexLm);
                return;
            }

            string arguments;
            string lmLogPath = LmLogPath();

            // Arguments for lmgrd.
            if (_platform == OSPlatform.Windows)
            {
                arguments = $"-c \"{licenseFilePath}\" -l +\"{lmLogPath}\"";
            }
            else
            {
                arguments = $"-c \"{licenseFilePath}\" -l +\"{lmLogPath}\"";
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
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to execute the start command. Here is the automatic error message: {ex.Message}");
        }
    }

    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        CheckStatus();
    }

    private async void LogButton_Click(object sender, RoutedEventArgs e)
    {
        // Try to detect the actual log file lmgrd is using.
        string? lmgrdPath = LmgrdLocationTextBox.Text;
        WindowsServiceInfo? serviceInfo = TryGetLmgrdWindowsService(lmgrdPath);
        string? detectedLogPath = await Task.Run(() => TryDetectLmgrdLogPath(serviceInfo, lmgrdPath));
        string lmLogPath = detectedLogPath ?? LmLogPath();

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
                ShowErrorWindow($"Failed to open log file: {ex.Message}");
            }
        }
    }

    private async void CheckStatus()
    {
        try
        {
            OutputTextBlock.Text = "Loading. Please wait.";

            // Hopefully increasing this to 1500 will reduce status error -16s.
            await Task.Delay(1500);

            string? lmutilPath = LmutilLocationTextBox.Text;
            string? licenseFilePath = LicenseFileLocationTextBox.Text;

            if (!string.IsNullOrWhiteSpace(lmutilPath) && !string.IsNullOrWhiteSpace(licenseFilePath))
            {
                BusyWithFlexLm();

                string? lmgrdPath = LmgrdLocationTextBox.Text;
                WindowsServiceInfo? serviceInfo = TryGetLmgrdWindowsService(lmgrdPath);

                // Try to detect the actual log file path lmgrd is using.
                string? detectedLogPath = await Task.Run(() => TryDetectLmgrdLogPath(serviceInfo, lmgrdPath));
                string lmLogPath = detectedLogPath ?? LmLogPath();

                if (serviceInfo != null)
                {
                    await Task.Run(() => ExecuteWindowsServiceStatus(serviceInfo, lmLogPath));
                }
                else
                {
                    // Run the long-running code asynchronously.
                    await Task.Run(() => ExecuteCheckStatus(lmutilPath, licenseFilePath, lmLogPath));
                }
            }
            else
            {
                ShowErrorWindow("You either left your license path or lmutil path empty.");
            }
        }
        catch (Exception ex)
        {
            ShowErrorWindow($"An error occurred when attempting to check FlexLM's status. Here is the automatic error message: {ex.Message}");
        }
    }

    private async Task ExecuteCheckStatus(string lmutilPath, string licenseFilePath, string lmLogPath)
    {
        if (!File.Exists(lmutilPath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"lmutil was not found at the specified path: {lmutilPath}"));
            Dispatcher.UIThread.Post(BusyWithFlexLm);
            return;
        }

        if (!File.Exists(licenseFilePath))
        {
            Dispatcher.UIThread.Post(() => ShowErrorWindow($"The license file was not found at the specified path: {licenseFilePath}"));
            Dispatcher.UIThread.Post(BusyWithFlexLm);
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

            // Try to read the log file for detailed error analysis. If it's unavailable, lmstat output is still analyzed.
            string[] lines = [];
            IEnumerable<string> last20Lines = [];

            if (File.Exists(lmLogPath))
            {
                try
                {
                    string fileContents;
                    using (var stream = new FileStream(lmLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream)) { fileContents = await reader.ReadToEndAsync(); }

                    lines = fileContents.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries);
                    last20Lines = lines.Skip(Math.Max(0, lines.Length - 20));
                }
                catch (Exception logEx)
                {
                    if (logEx.Message.Contains("being used by another process"))
                    {
                        Dispatcher.UIThread.Post(() => ShowErrorWindow("You have the log file opened in another program, which is preventing this program from reading it."));
                        return;
                    }
                    // For other errors, continue without log data.
                }
            }

            bool statusIsUnknown = false;

            // Check the exit code to determine if the command succeeded.
            if (process.ExitCode == 0)
            {
                // The command ran, but that doesn't mean FlexLM actually stopped.
                Console.WriteLine("The command to check FlexLM's status executed successfully.");
                Console.WriteLine(output);

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
                            if (!_programWasJustLaunched)
                            {
                                if (last20Lines.Any(line => line.Contains("Failed to open the TCP port number in the license.")))
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        ShowErrorWindow("FlexLM is down. lmgrd's port could not be opened. You either just recently shutdown the server and the port has not freed yet, don't have permissions to open the port, or it's being used by " +
                                            "something else. You might need to just wait longer for the desired ports to reopen if you just stopped FlexLM. Otherwise, if you're on Linux, I recommend trying TCP port 27011.");
                                    });
                                }
                                else if (last20Lines.Any(line => line.Contains("Not a valid server hostname, exiting.")) ||
                                (last20Lines.Any(line => line.Contains("Unknown Hostname: ")) && last20Lines.Any(line => line.Contains("license file is not available in the local network database"))))
                                {
                                    ShowErrorWindow("FlexLM is down. The hostname you've specified in your license file's SERVER line is invalid.");
                                }
                                else if (last20Lines.Any(line => line.Contains("(There are no VENDOR (or DAEMON) lines in the license file)")))
                                {
                                    ShowErrorWindow("FlexLM did not start because you are missing your DAEMON line from your license file.");
                                }
                                else if (last20Lines.Any(line => line.Contains("((lmgrd) The TCP port number in the license, ")) && last20Lines.Any(line => line.Contains(", is already in use.")))
                                {
                                    ShowErrorWindow("FlexLM is down. The primary port number is still in use. This is likely because it still needs to be freed from the server running earlier." +
                                    "Please attempt to start the server again in 30 seconds.");
                                }
                                else if (last20Lines.Any(line => line.Contains("(MLM) Listener Thread: running")))
                                {
                                    ShowErrorWindow("FlexLM may be running, but lmstat is reporting it as down. It is likely your network is misconfigured in such a way that your computer cannot " +
                                    "resolve its own hostname and/or its IP address is not being set or read correctly. Please view the log file for more details.");
                                    statusIsUnknown = true;
                                }
                                else
                                {
                                    if (_stopButtonWasJustUsed || _programWasJustLaunched || (last20Lines.Any(line => line.Contains("(lmgrd) EXITING DUE TO SIGNAL 15")) && last20Lines.Any(line => line.Contains("(MLM) daemon shutdown requested - shutting down"))))
                                    {
                                        OutputTextBlock.Text = "FlexLM is down.";
                                    }
                                    else
                                    {
                                        ShowErrorWindow("FlexLM is down. Check the log file for more information.");
                                    }
                                }
                            }
                            else
                            {
                                OutputTextBlock.Text = "FlexLM is down.";
                            }
                        }
                        else
                        {
                            if (_stopButtonWasJustUsed || _programWasJustLaunched)
                            {
                                OutputTextBlock.Text = "FlexLM is down.";
                            }
                            else
                            {
                                ShowErrorWindow("FlexLM is down. Check the log file for more information.");
                            }
                        }
                        if (!statusIsUnknown)
                        {
                            FlexLmCanStart();
                        }
                        else
                        {
                            FlexLmStatusUnknown();
                        }                        
                    });
                }
                // Successfully launched.
                else if (output.Contains("license server UP (MASTER)") && output.Contains("MLM: UP"))
                {
                    // Attempt to force a shutdown, if necessary, and it's our first time trying to do so.
                    if (last20Lines.Any(line => line.Contains("(lmgrd) Redo lmdown with '-force' arg.")) && last20Lines.Any(line => line.Contains("(lmgrd) Cannot lmdown the server when licenses are borrowed. (-120,567")) && _stopButtonWasJustUsed && _firstAttemptToForceShutdown)
                    {
                        _wantToAttemptForcedShutdown = true;
                        await Task.Run(() => ExecuteStopCommand(lmutilPath, licenseFilePath, null));
                        _firstAttemptToForceShutdown = false;
                        return;
                    }
                    else
                    {
                        _wantToAttemptForcedShutdown = false;
                    }

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
                    Dispatcher.UIThread.Post(() =>
                    {
                        bool flexLmIsAttemptingRestart = false;

                        if (output.Contains("MLM: No socket connection to license server manager."))
                        {

                            if (last20Lines.Any(line => line.Contains("((lmgrd) The TCP port number in the license, ")) && last20Lines.Any(line => line.Contains(", is already in use.")) && last20Lines.Any(line => line.Contains("Retrying for about 5 more minutes")))
                            {
                                ShowErrorWindow("FlexLM is down. The primary port number is still in use. FlexLM has recognized this and will attempt to launch the server again for the next 5 minutes." +
                                "Please check the server's status again in 30 seconds.");
                                flexLmIsAttemptingRestart = true;
                            }
                            else if (last20Lines.Any(line => line.Contains("Failed to open the TCP port number in the license.")))
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. lmgrd's port could not be opened. You either just recently shutdown the server and the port has not freed yet, " +
                                "don't have permissions to open the port, or it's being used by something else. You might need to just wait longer for the desired ports to reopen if you just stopped FlexLM. " +
                                "Otherwise, if you're on Linux, I recommend trying TCP port 27011.";
                            }
                            else if (last20Lines.Any(line => line.Contains("MLM exited with status 36 (No features to serve)")))
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. None of the products in your license file are valid (are you using a license for a different computer?) " +
                                "Check lmlog.txt in LMFOOLS's directory for more specific errors.";
                            }
                            else if (last20Lines.Any(line => line.Contains("MLM exited with status 27 (No features to serve)")))
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. This could be caused by a syntax error in the license file, MLM's port is being used by something else (likely another instance of MLM), " +
                                "the license file has expired/the system clock is set incorrectly, accidentally pointing to an Individual license, or you combined the Enterprise and non-Enterprise license manager installation files into 1 folder.";
                            }
                            else if (last20Lines.Any(line => line.Contains("(lmgrd) MLM exited with status 2 signal = 17")))
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. MLM is likely corrupted. Please obtain a new copy of it. " +
                                "If you're using Linux, use 'unzip' instead of Ark to extract any ZIP archives with MLM in it.";

                            }
                            else if (last20Lines.Any(line => line.Contains("Cannot open license file /usr/local/flexlm/licenses/license.dat")))
                            {

                                OutputTextBlock.Text = "MLM attempted to use a license file that does not exist. Please place MLM in a directory that this program can access. " +
                                "Hint: try placing it in the same directory as your license file or in a user-specific folder.";

                            }
                            else
                            {
                                OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. Please press the Stop button or manually end the process.";
                            }
                        }
                        else
                        {
                            OutputTextBlock.Text = "LMGRD was able to start, but MLM could not. Please press the Stop button or manually end the process.";
                        }

                        if (!flexLmIsAttemptingRestart)
                        { FlexLmCanStop(); }
                        else { FlexLmIsAttemptingToRestart(); }
                    });
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
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_programWasJustLaunched)
                    {
                        if (last20Lines.Any(line => line.Contains("license manager: can't initialize:Invalid license file syntax.")))
                        {
                            ShowErrorWindow("Failed to execute the status check command. Your license file is improperly formatted. Check the SERVER and DAEMON lines for any formatting errors. " +
                                            "Additionally, please make you have permissions to execute lmgrd, MLM, and lmutil.");
                            FlexLmCanStart();
                        }
                        else
                        {
                            ShowErrorWindow("Failed to execute the status check command. Please check your license and log file for errors, such as (but definitely not limited to) a missing or misformatted SERVER line. " +
                                            "Additionally, please make you have permissions to execute lmgrd, MLM, and lmutil.");
                            FlexLmStatusUnknown();
                        }
                    }
                    else
                    {
                        FlexLmStatusUnknown();
                    }
                });
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
        const string usagePattern = @"Users of ([\w\-\.]+):\s+\(Total of (\d+) license[s]? issued;\s+Total of (\d+) license[s]? in use\)";
        const string errorPattern = @"Users of ([\w\-\.]+):\s+\(Error: (\d+) license[s]?, unsupported by licensed server\)";
        const string dcLicensePattern = @"Users of ([\w\-\.]+):\s+\(Uncounted, node-locked\)";

        // Don't listen to Rider's lies. You never know what'll come out as null when the program is published.
        string formattedOutputText = "blankFormattedOutputText";
        string[] logLines = [];
        IEnumerable<string> last50Lines = [];

        MatchCollection usageMatches = Regex.Matches(output, usagePattern);
        MatchCollection errorMatches = Regex.Matches(output, errorPattern);
        MatchCollection dcLicenseMatches = Regex.Matches(output, dcLicensePattern);

        OutputTextBlock.Text += "\n"; // Give a bit of space.

        // Try to read the log file for detailed error analysis. If unavailable, seat info still displays.
        if (File.Exists(lmLogPath))
        {
            try
            {
                string logFileContents;
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
                if (ex.Message.Contains("being used by another process"))
                {
                    ShowErrorWindow("Another program is using the log file. Some license details may be unavailable.");
                }
                // Continue without log data — seat info from lmstat will still display.
            }
        }

        // Iterate through the error matches first.
        foreach (Match match in errorMatches)
        {
            if (!match.Success) continue;
            string product = match.Groups[1].Value;

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
                    else if (line.Contains($"(MLM) USER_BASED license error for {product} (INCLUDE missing)"))
                    {
                        OutputTextBlock.Text += $"\n{product} is from an NNU license and does not have a valid INCLUDE setup. Therefore, it cannot be used.";
                        causeWasFound = true;
                        break;
                    }
                    else if (line.Contains($"(MLM) USER_BASED license error for {product} --"))
                    {
                        if (i + 1 < logLines.Length && logLines[i + 1].Contains($"Number of INCLUDE names (") && logLines[i + 1].Contains($") exceeds limit of"))
                        {
                            OutputTextBlock.Text += $"\n{product} has too many users included from the options file and therefore, cannot be used.";
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

        bool optionsFileCannotBeUsed = false;
        bool warningMessageDisplayed = false;

        if (last50Lines.Any(line => line.Contains("(MLM) CANNOT OPEN options file")) && !last50Lines.Any(line => line.Contains("options file \"License\"")))
        {
            OutputTextBlock.Text += "\nYour options file could not be opened. Make sure the path to it in your license file is correct.";
            optionsFileCannotBeUsed = true;
        }

        if (logLines.Any(line => line.Contains("(MLM) NOTE: Some features are USER_BASED or HOST_BASED")))
        {
            if (errorMatches.Count != 0 || optionsFileCannotBeUsed == true)
            {
                OutputTextBlock.Text += "\n";
            }

            OutputTextBlock.Text += "\nWarning: your license file contains at least 1 NNU license. Products on an NNU license will seemingly have their seat count halved/doubled since " +
            "each user specified gets 2 seats per product.";
            warningMessageDisplayed = true;
        }

        if (errorMatches.Count != 0 || warningMessageDisplayed)
        {
            OutputTextBlock.Text += "\n";
        }

        // You can technically put a Designated Computer license on a license server, but there's really no reason to and therefore, we will assume this is an error.
        foreach (Match match in dcLicenseMatches)
        {
            if (!match.Success) continue;
            string product = match.Groups[1].Value;

            formattedOutputText = $"\nThe product {product} is from a Designated Computer license.";

            OutputTextBlock.Text += formattedOutputText;
        }

        // Now iterate through the usage matches.
        foreach (Match match in usageMatches)
        {
            if (!match.Success) continue;
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
