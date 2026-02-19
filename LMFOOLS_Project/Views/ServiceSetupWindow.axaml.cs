using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LMFOOLS_Project.Views;

public partial class ServiceSetupWindow : Window
{
    private readonly OSPlatform _platform;
    private readonly string _lmgrdPath;
    private readonly string _licensePath;
    private readonly string _lmLogPath;

    public ServiceSetupWindow(OSPlatform platform, string lmgrdPath, string licensePath, string lmLogPath, string? savedServiceName)
    {
        InitializeComponent();
        _platform = platform;
        _lmgrdPath = lmgrdPath;
        _licensePath = licensePath;
        _lmLogPath = lmLogPath;

        LmgrdPathTextBox.Text = lmgrdPath;
        LicensePathTextBox.Text = licensePath;

        if (!string.IsNullOrWhiteSpace(savedServiceName))
        {
            ServiceNameTextBox.Text = savedServiceName;
        }
        else
        {
            ServiceNameTextBox.Text = "lmgrd-flexlm";
        }

        if (platform == OSPlatform.Windows)
        {
            StatusTextBlock.Text = "Platform: Windows (sc.exe)";
        }
        else if (platform == OSPlatform.Linux)
        {
            StatusTextBlock.Text = "Platform: Linux (systemd)";
        }
        else if (platform == OSPlatform.OSX)
        {
            StatusTextBlock.Text = "Platform: macOS (launchd)";
        }
        else
        {
            StatusTextBlock.Text = "Unsupported platform for service creation.";
            CreateButton.IsEnabled = false;
        }

        this.Opened += ServiceSetupWindow_Opened;
    }

    private void ServiceSetupWindow_Opened(object? sender, EventArgs e)
    {
        DetectExistingService();
    }

    private void DetectExistingService()
    {
        string serviceName = ServiceNameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(serviceName)) return;

        try
        {
            if (_platform == OSPlatform.Linux)
            {
                string unitPath = $"/etc/systemd/system/{serviceName}.service";
                if (File.Exists(unitPath))
                {
                    StatusTextBlock.Text = $"Existing systemd service found at:\n{unitPath}";
                    RemoveButton.IsEnabled = true;
                    return;
                }
            }
            else if (_platform == OSPlatform.OSX)
            {
                string plistPath = $"/Library/LaunchDaemons/{serviceName}.plist";
                if (File.Exists(plistPath))
                {
                    StatusTextBlock.Text = $"Existing launchd service found at:\n{plistPath}";
                    RemoveButton.IsEnabled = true;
                    return;
                }
            }
            else if (_platform == OSPlatform.Windows)
            {
                try
                {
                    ProcessStartInfo info = new()
                    {
                        FileName = "sc.exe",
                        Arguments = $"query \"{serviceName}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using Process process = new() { StartInfo = info };
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusTextBlock.Text = $"Existing Windows Service found: {serviceName}";
                        RemoveButton.IsEnabled = true;
                        return;
                    }
                }
                catch
                {
                    // sc.exe query failed, service likely doesn't exist
                }
            }

            StatusTextBlock.Text += "\nNo existing service detected with that name.";
            RemoveButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text += $"\nError checking for existing service: {ex.Message}";
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string serviceName = ServiceNameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            StatusTextBlock.Text = "Please enter a service name.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_lmgrdPath) || !File.Exists(_lmgrdPath))
        {
            StatusTextBlock.Text = "lmgrd path is invalid. Please set it in the main window first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_licensePath) || !File.Exists(_licensePath))
        {
            StatusTextBlock.Text = "License file path is invalid. Please set it in the main window first.";
            return;
        }

        try
        {
            if (_platform == OSPlatform.Linux)
            {
                CreateSystemdService(serviceName);
            }
            else if (_platform == OSPlatform.OSX)
            {
                CreateLaunchdService(serviceName);
            }
            else if (_platform == OSPlatform.Windows)
            {
                CreateWindowsService(serviceName);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error creating service: {ex.Message}";
        }
    }

    private void CreateSystemdService(string serviceName)
    {
        bool startOnBoot = StartOnBootCheckBox.IsChecked == true;

        string unitContent = $"""
            [Unit]
            Description=FlexLM License Manager (lmgrd) managed by LMFOOLS
            After=network.target

            [Service]
            Type=simple
            ExecStart={_lmgrdPath} -z -c "{_licensePath}" -l +"{_lmLogPath}" -reuseaddr
            Restart=on-failure
            RestartSec=10

            [Install]
            WantedBy=multi-user.target
            """;

        // Write to a temp file, then use pkexec to copy it into place
        string tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, unitContent);

        string unitPath = $"/etc/systemd/system/{serviceName}.service";
        string enableCmd = startOnBoot ? $" && systemctl enable {serviceName}" : "";
        string script = $"cp \"{tempFile}\" \"{unitPath}\" && chmod 644 \"{unitPath}\" && systemctl daemon-reload{enableCmd}";

        // Check if pkexec is available
        bool hasPkexec = false;
        try
        {
            ProcessStartInfo whichInfo = new()
            {
                FileName = "which",
                Arguments = "pkexec",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process whichProcess = new() { StartInfo = whichInfo };
            whichProcess.Start();
            whichProcess.StandardOutput.ReadToEnd();
            whichProcess.WaitForExit();
            hasPkexec = whichProcess.ExitCode == 0;
        }
        catch
        {
            // pkexec not available
        }

        if (hasPkexec)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "pkexec",
                Arguments = $"bash -c '{script}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            Task<string> stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            process.StandardOutput.ReadToEnd();
            string error = stderrTask.Result;
            process.WaitForExit();

            // Clean up temp file
            try { File.Delete(tempFile); } catch { }

            if (process.ExitCode == 0)
            {
                StatusTextBlock.Text = $"systemd service created successfully at:\n{unitPath}";
                if (startOnBoot) StatusTextBlock.Text += "\nService enabled for start on boot.";
                RemoveButton.IsEnabled = true;
            }
            else
            {
                StatusTextBlock.Text = $"Failed to create service. Exit code: {process.ExitCode}";
                if (!string.IsNullOrWhiteSpace(error)) StatusTextBlock.Text += $"\n{error}";
            }
        }
        else
        {
            // pkexec not available — show manual instructions
            try { File.Delete(tempFile); } catch { }
            StatusTextBlock.Text = "pkexec is not available. Please run these commands manually with sudo:\n\n" +
                $"sudo bash -c '{script}'";
        }
    }

    private void CreateLaunchdService(string serviceName)
    {
        bool startOnBoot = StartOnBootCheckBox.IsChecked == true;

        StringBuilder plist = new();
        plist.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        plist.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        plist.AppendLine("<plist version=\"1.0\">");
        plist.AppendLine("<dict>");
        plist.AppendLine($"    <key>Label</key>");
        plist.AppendLine($"    <string>{serviceName}</string>");
        plist.AppendLine("    <key>ProgramArguments</key>");
        plist.AppendLine("    <array>");
        plist.AppendLine($"        <string>{_lmgrdPath}</string>");
        plist.AppendLine("        <string>-z</string>");
        plist.AppendLine("        <string>-c</string>");
        plist.AppendLine($"        <string>{_licensePath}</string>");
        plist.AppendLine("        <string>-l</string>");
        plist.AppendLine($"        <string>+{_lmLogPath}</string>");
        plist.AppendLine("        <string>-reuseaddr</string>");
        plist.AppendLine("    </array>");
        if (startOnBoot)
        {
            plist.AppendLine("    <key>RunAtLoad</key>");
            plist.AppendLine("    <true/>");
        }
        plist.AppendLine("    <key>KeepAlive</key>");
        plist.AppendLine("    <dict>");
        plist.AppendLine("        <key>SuccessfulExit</key>");
        plist.AppendLine("        <false/>");
        plist.AppendLine("    </dict>");
        plist.AppendLine("</dict>");
        plist.AppendLine("</plist>");

        string tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, plist.ToString());

        string plistPath = $"/Library/LaunchDaemons/{serviceName}.plist";
        string script = $"cp \\\"{tempFile}\\\" \\\"{plistPath}\\\" && chmod 644 \\\"{plistPath}\\\" && chown root:wheel \\\"{plistPath}\\\"";

        ProcessStartInfo startInfo = new()
        {
            FileName = "osascript",
            Arguments = $"-e 'do shell script \"{script}\" with administrator privileges'",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        Task<string> stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        process.StandardOutput.ReadToEnd();
        string error = stderrTask.Result;
        process.WaitForExit();

        // Clean up temp file
        try { File.Delete(tempFile); } catch { }

        if (process.ExitCode == 0)
        {
            StatusTextBlock.Text = $"launchd service created successfully at:\n{plistPath}";
            if (startOnBoot) StatusTextBlock.Text += "\nService configured to start on boot (RunAtLoad).";
            RemoveButton.IsEnabled = true;
        }
        else
        {
            StatusTextBlock.Text = $"Failed to create service. Exit code: {process.ExitCode}";
            if (!string.IsNullOrWhiteSpace(error)) StatusTextBlock.Text += $"\n{error}";
        }
    }

    private void CreateWindowsService(string serviceName)
    {
        string binPath = $"\\\"{_lmgrdPath}\\\" -c \\\"{_licensePath}\\\" -l +\\\"{_lmLogPath}\\\"";
        string startType = StartOnBootCheckBox.IsChecked == true ? "auto" : "demand";

        ProcessStartInfo startInfo = new()
        {
            FileName = "sc.exe",
            Arguments = $"create \"{serviceName}\" binPath= \"{binPath}\" start= {startType} DisplayName= \"FlexLM License Manager (LMFOOLS)\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            process?.WaitForExit();

            if (process?.ExitCode == 0)
            {
                StatusTextBlock.Text = $"Windows Service \"{serviceName}\" created successfully.";
                if (StartOnBootCheckBox.IsChecked == true) StatusTextBlock.Text += "\nService set to start automatically.";
                RemoveButton.IsEnabled = true;
            }
            else
            {
                StatusTextBlock.Text = $"sc.exe exited with code {process?.ExitCode}. The service may not have been created.";
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusTextBlock.Text = "Service creation was cancelled (UAC prompt was declined or not available).";
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        string serviceName = ServiceNameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            StatusTextBlock.Text = "Please enter a service name.";
            return;
        }

        try
        {
            if (_platform == OSPlatform.Linux)
            {
                RemoveSystemdService(serviceName);
            }
            else if (_platform == OSPlatform.OSX)
            {
                RemoveLaunchdService(serviceName);
            }
            else if (_platform == OSPlatform.Windows)
            {
                RemoveWindowsService(serviceName);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error removing service: {ex.Message}";
        }
    }

    private void RemoveSystemdService(string serviceName)
    {
        string unitPath = $"/etc/systemd/system/{serviceName}.service";
        string script = $"systemctl stop {serviceName} 2>/dev/null; systemctl disable {serviceName} 2>/dev/null; rm -f \"{unitPath}\" && systemctl daemon-reload";

        bool hasPkexec = false;
        try
        {
            ProcessStartInfo whichInfo = new()
            {
                FileName = "which",
                Arguments = "pkexec",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process whichProcess = new() { StartInfo = whichInfo };
            whichProcess.Start();
            whichProcess.StandardOutput.ReadToEnd();
            whichProcess.WaitForExit();
            hasPkexec = whichProcess.ExitCode == 0;
        }
        catch { }

        if (hasPkexec)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "pkexec",
                Arguments = $"bash -c '{script}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            Task<string> stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            process.StandardOutput.ReadToEnd();
            string error = stderrTask.Result;
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                StatusTextBlock.Text = $"systemd service \"{serviceName}\" removed successfully.";
                RemoveButton.IsEnabled = false;
            }
            else
            {
                StatusTextBlock.Text = $"Failed to remove service. Exit code: {process.ExitCode}";
                if (!string.IsNullOrWhiteSpace(error)) StatusTextBlock.Text += $"\n{error}";
            }
        }
        else
        {
            StatusTextBlock.Text = "pkexec is not available. Please run this command manually with sudo:\n\n" +
                $"sudo bash -c '{script}'";
        }
    }

    private void RemoveLaunchdService(string serviceName)
    {
        string plistPath = $"/Library/LaunchDaemons/{serviceName}.plist";
        string script = $"launchctl unload \\\"{plistPath}\\\" 2>/dev/null; rm -f \\\"{plistPath}\\\"";

        ProcessStartInfo startInfo = new()
        {
            FileName = "osascript",
            Arguments = $"-e 'do shell script \"{script}\" with administrator privileges'",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        Task<string> stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        process.StandardOutput.ReadToEnd();
        string error = stderrTask.Result;
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            StatusTextBlock.Text = $"launchd service \"{serviceName}\" removed successfully.";
            RemoveButton.IsEnabled = false;
        }
        else
        {
            StatusTextBlock.Text = $"Failed to remove service. Exit code: {process.ExitCode}";
            if (!string.IsNullOrWhiteSpace(error)) StatusTextBlock.Text += $"\n{error}";
        }
    }

    private void RemoveWindowsService(string serviceName)
    {
        // Stop the service first, then delete it
        ProcessStartInfo stopInfo = new()
        {
            FileName = "sc.exe",
            Arguments = $"stop \"{serviceName}\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };

        try
        {
            using Process? stopProcess = Process.Start(stopInfo);
            stopProcess?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusTextBlock.Text = "Service removal was cancelled (UAC prompt was declined or not available).";
            return;
        }

        ProcessStartInfo deleteInfo = new()
        {
            FileName = "sc.exe",
            Arguments = $"delete \"{serviceName}\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };

        try
        {
            using Process? deleteProcess = Process.Start(deleteInfo);
            deleteProcess?.WaitForExit();

            if (deleteProcess?.ExitCode == 0)
            {
                StatusTextBlock.Text = $"Windows Service \"{serviceName}\" removed successfully.";
                RemoveButton.IsEnabled = false;
            }
            else
            {
                StatusTextBlock.Text = $"sc.exe delete exited with code {deleteProcess?.ExitCode}. The service may not have been removed.";
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusTextBlock.Text = "Service removal was cancelled (UAC prompt was declined or not available).";
        }
    }

    /// <summary>
    /// Returns the service name entered by the user so the main window can save it.
    /// </summary>
    public string EnteredServiceName => ServiceNameTextBox.Text?.Trim() ?? "";

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
