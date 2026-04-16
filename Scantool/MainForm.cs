using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Scantool;

public partial class MainForm : Form
{
    private const string defaultPortalUrl = "https://feetf1rst.tech";
    private const string defaultRocketExePath = @"C:\XPOD 3D Full Foot Scan\Bin\XPOD_Rocket.exe";
    private const string defaultScannerEncryption = "000000000000";
    private const string defaultApiUrl = "https://backend.feetf1rst.tech";

    private bool isRocketRunning = false;
    private bool isHandlingSourceChange = false;
    private string portalBaseUrl = string.Empty;
    private string lastRocketStatus = string.Empty;
    private int lastRocketExitCode = -1;
    private string lastHandledScannerCommand = string.Empty;
    private DateTime lastHandledScannerCommandAtUtc = DateTime.MinValue;
    private DateTime lastScannerExitAtUtc = DateTime.MinValue;
    private string rocketExePath = string.Empty;
    private readonly string scannerEncryption;
    private readonly string networkLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "scantool-network.log"
    );
    private readonly string rocketPathConfigFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Feetf1rst",
        "Scantool",
        "rocket-path.txt"
    );

    public MainForm()
    {
        InitializeComponent();
        rocketExePath = ResolveRocketExePath();
        scannerEncryption = ResolveScannerEncryption();
        webView21.CoreWebView2InitializationCompleted += WebView21_CoreWebView2InitializationCompleted;
        _ = webView21.EnsureCoreWebView2Async();
        InitializePortalUrl();
    }

    private void WriteNetworkLog(string line)
    {
        try
        {
            File.AppendAllText(networkLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // Do not break the app if logging fails.
        }
    }

    private void WebView21_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || webView21.CoreWebView2 == null)
        {
            WriteNetworkLog("WebView2 init failed.");
            return;
        }

        WriteNetworkLog($"WebView2 init OK. Portal={portalBaseUrl}, API default={defaultApiUrl}");
        webView21.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        webView21.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
    }

    private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri ?? string.Empty;
            if (
                uri.Contains("/v2/auth/system-login", StringComparison.OrdinalIgnoreCase) ||
                uri.Contains("backend.feetf1rst.tech", StringComparison.OrdinalIgnoreCase)
            )
            {
                WriteNetworkLog($"[{e.Request.Method}] {uri}");
            }
        }
        catch
        {
            // Best-effort logging only.
        }
    }

    private static string ResolveRocketExePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("SCANTOOL_ROCKET_EXE_PATH");
        return string.IsNullOrWhiteSpace(configuredPath) ? string.Empty : configuredPath.Trim();
    }

    private static string ResolveScannerEncryption()
    {
        var configuredEncryption = Environment.GetEnvironmentVariable("SCANTOOL_SCANNER_ENCRYPTION");
        return string.IsNullOrWhiteSpace(configuredEncryption) ? defaultScannerEncryption : configuredEncryption.Trim();
    }

    private bool IsHardwareConnectionError(int exitCode) => exitCode is 6 or 7 or 8;

    private async Task<string> RunDetectDiagnosticAsync()
    {
        try
        {
            var detectExe = Path.Combine(Path.GetDirectoryName(rocketExePath) ?? string.Empty, "Detect.exe");
            if (!File.Exists(detectExe))
            {
                return "Detect.exe not found.";
            }

            var info = new ProcessStartInfo
            {
                FileName = detectExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(detectExe) ?? string.Empty
            };

            using var process = Process.Start(info);
            if (process == null)
            {
                return "Detect.exe failed to start.";
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(cts.Token);
            return $"Detect.exe exit code: {process.ExitCode}.";
        }
        catch (OperationCanceledException)
        {
            return "Detect.exe timed out after 20 seconds.";
        }
        catch (Exception ex)
        {
            return $"Detect.exe failed: {ex.Message}";
        }
    }

    private void InitializePortalUrl()
    {
        // Safety: custom endpoints are ignored unless explicitly allowed.
        var configuredUrl = Environment.GetEnvironmentVariable("SCANTOOL_PORTAL_URL");
        var allowCustomEndpoints = string.Equals(
            Environment.GetEnvironmentVariable("SCANTOOL_ALLOW_CUSTOM_ENDPOINTS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
        var candidateUrl = (!allowCustomEndpoints || string.IsNullOrWhiteSpace(configuredUrl))
            ? defaultPortalUrl
            : configuredUrl;

        if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            portalBaseUrl = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            webView21.Source = uri;
            return;
        }

        webView21.Source = new Uri("about:blank", UriKind.Absolute);
        MessageBox.Show(
            "Portal URL is not configured correctly.\nSet SCANTOOL_PORTAL_URL to a valid http/https URL.",
            "Configuration Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning
        );
    }

    private async Task HandleScannerStartAsync()
    {
        try
        {
            if (!isRocketRunning)
            {
                //Ask Rocket to shutdown gracefully if it was left open due to exceptions or other failures
                await RunProcessAsync("-exit", "Exit");

                var started = await RunProcessAsync("-start", "Rocket-Start");
                if (!started && lastRocketExitCode is 3 or 4)
                {
                    await Task.Delay(1200);
                    started = await RunProcessAsync("-start", "Rocket-Start-Retry");
                }

                if (!started)
                {
                    if (IsHardwareConnectionError(lastRocketExitCode))
                    {
                        var detectStatus = await RunDetectDiagnosticAsync();
                        throw new Exception(
                            $"Rocket hardware not detected. {lastRocketStatus} {detectStatus} " +
                            "Please check scanner power, USB cable/port, and camera/control-board drivers."
                        );
                    }

                    throw new Exception($"Rocket konnte nicht gestartet werden. {lastRocketStatus}");
                }

                isRocketRunning = true;
            }
        }
        catch (Exception ex)
        {
            LogError($"cannot start Rocket {ex.Message}");
        }
    }

    private async Task HandleScannerExitAsync()
    {
        try
        {
            if (isRocketRunning)
            {
                isRocketRunning = false;
                if (await RunProcessAsync("-exit", "Exit"))
                {
                    lastScannerExitAtUtc = DateTime.UtcNow;
                }
            }
            else
            {
                throw new Exception("No instance of Rocket open to exit");
            }
        }
        catch (Exception ex)
        {
            LogError($"cannot start Rocket {ex.Message}");
        }
    }

    private async Task HandleSingleFootScanAsync()
    {
        try
        {
            StartLoading(isScanning: true);

            //Start Scanner (should already be running, this is a double-check)
            await HandleScannerStartAsync();

            if (await RunProcessAsync($"-scan -l Single Scan 0 {scannerEncryption}", "Scan"))
            {
                //If its a single foot => true (no redirect)
                await HandleSaveAndExitAsync(true);
            }
            else
            {
                throw new Exception(string.IsNullOrWhiteSpace(lastRocketStatus)
                    ? "[ERROR] Full scan sequence failed."
                    : lastRocketStatus);
            }
        }
        catch (Exception ex)
        {
            LogError($"unable to scan single foot : {ex.Message}");
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task HandleLeftScanAsync()
    {
        try
        {

            StartLoading(isScanning: true);

            var userInfo = await GetUserInfoAsync();
            string name = $"\"{userInfo.FirstName}\" \"{userInfo.LastName}\"";
            string gender = $"{userInfo.Gender}";

            //Start Scanner (should already be running, this is a double-check)
            await HandleScannerStartAsync();

            if (await RunProcessAsync($"-scan -l {name} {gender} {scannerEncryption}", "Scan-Left"))
            {
                await webView21.CoreWebView2.ExecuteScriptAsync("setScanLeftFinished('true')");
            }
            else
            {
                throw new Exception(
                    string.IsNullOrWhiteSpace(lastRocketStatus)
                        ? $"Left scan process failed. UserInfo: {JsonSerializer.Serialize(userInfo)}"
                        : $"Left scan process failed. {lastRocketStatus}. UserInfo: {JsonSerializer.Serialize(userInfo)}"
                );
            }
        }
        catch (Exception ex)
        {
            await webView21.CoreWebView2.ExecuteScriptAsync("setScanLeftFinished('false')");
            LogError($"Linker Scan Fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task HandleRightScanAsync()
    {
        try
        {
            StartLoading(isScanning: true);

            var userInfo = await GetUserInfoAsync();
            string name = $"\"{userInfo.FirstName}\" \"{userInfo.LastName}\"";
            string gender = $"{userInfo.Gender}";

            //Start Scanner (should already be running, this is a double-check)
            await HandleScannerStartAsync();

            if (await RunProcessAsync($"-scan -r {name} {gender} {scannerEncryption}", "Scan-Right"))
            {
                await webView21.CoreWebView2.ExecuteScriptAsync("setScanRightFinished('true')");
            }
            else
            {
                throw new Exception(
                    string.IsNullOrWhiteSpace(lastRocketStatus)
                        ? $"Right scan process failed. UserInfo: {JsonSerializer.Serialize(userInfo)}"
                        : $"Right scan process failed. {lastRocketStatus}. UserInfo: {JsonSerializer.Serialize(userInfo)}"
                );
            }
        }
        catch (Exception ex)
        {
            await webView21.CoreWebView2.ExecuteScriptAsync("setScanRightFinished('false')");
            LogError($"Rechter Scan Fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task HandleSaveAndExitAsync(bool isSingleFoot)
    {
        try
        {

            StartLoading(isScanning: false);
            if (await RunProcessAsync($"-save", "Save"))
            {

                try
                {
                    if (!isSingleFoot)
                    {
                        await UploadFiles();
                        await webView21.CoreWebView2.ExecuteScriptAsync("setSaveFinished('true')");
                        EndLoading(); //must happen before MessageBox in order to prevent stucking UI
                        MessageBox.Show("Dateien erfolgreich hochgeladen!!", "Scan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Fehler beim Hochladen der Dateien: {ex.Message}");
                    if (!isSingleFoot)
                    {
                        await webView21.CoreWebView2.ExecuteScriptAsync("setSaveFinished('false')");
                        OpenLatestScan();
                    }
                }
                finally
                {
                    await HandleScannerExitAsync();
                }
            }
            else
            {
                throw new Exception(string.IsNullOrWhiteSpace(lastRocketStatus)
                    ? "Scan kann nicht gespeichert werden!"
                    : lastRocketStatus);
            }
        }
        catch (Exception ex)
        {
            await webView21.CoreWebView2.ExecuteScriptAsync("setSaveFinished()");
            LogError($"Scan kann nicht gespeichert werden: {ex.Message}");
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task<UserInfo> GetUserInfoAsync()
    {
        const string script = "sessionStorage.getItem('formSubmissionData')";

        string rawResult = await webView21.ExecuteScriptAsync(script);

        if (string.IsNullOrWhiteSpace(rawResult) || rawResult == "null")
        {
            throw new Exception("Keine Benutzerinformationen in sessionStorage gefunden.");
        }

        string cleanJson = JsonSerializer.Deserialize<string>(rawResult)!;

        var parsedUser = UserInfo.FromJson(cleanJson);

        if (parsedUser == null)
            throw new Exception("Benutzerinformationen konnten nicht geparsed werden!");
        return parsedUser;
    }

    private async Task<bool> RunProcessAsync(string arguments, string task)
    {
        try
        {
            if (!EnsureRocketExecutableConfigured())
            {
                throw new Exception(
                    "XPOD_Rocket.exe not configured. Please select XPOD_Rocket.exe once, then retry the scan."
                );
            }

            if (!File.Exists(rocketExePath))
            {
                throw new Exception($"XPOD executable not found: {rocketExePath}");
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = rocketExePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(rocketExePath) ?? string.Empty
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new Exception($"[ERROR] Der Startvorgang f�r die Aufgabe ist fehlgeschlagen: {task}");
            }

            await process.WaitForExitAsync();
            lastRocketExitCode = process.ExitCode;

            string message = process.ExitCode switch
            {
                0 => "Fatal System Error",
                1 => "Run without Arguments",
                2 => "Run with invalid Arguments",
                3 => "Rocket is busy",
                4 => "Another Rocket busy",
                5 => "No Rocket is Running",
                6 => "No control board and cameras found (check scanner power, USB cable, and driver)",
                7 => "No control board found (check board driver/connection)",
                8 => "No cameras found (check camera driver/connection)",
                9 => "Rocket Started Successfully",
                10 => "Request Rocket to scan foot",
                11 => "Scan not successful",
                12 => "Scan foot failed due to camera frame dropping",
                13 => "Scan successful",
                14 => "Request Rocket to save file",
                15 => "Save Path does not exist",
                16 => "Save Data does not exist",
                17 => "Save successful",
                18 => "Request Rocket to exit",
                19 => "Rocket exited successfully",
                20 => "Request Rocket to get serial nr",
                21 => "Request Rocket to scan left/right foot",
                22 => "Set Rocket to scan left/right foot",
                _ => "Unknown Error"
            };

            lastRocketStatus = $"{task}: {message} (Code {process.ExitCode}, Args: {arguments})";

            bool success = process.ExitCode switch
            {
                9 or 10 or 13 or 14 or 17 or 18 or 19 or 20 or 21 or 22 => true,
                _ => false
            };

            Debug.WriteLine($"Successful={success} {lastRocketStatus}");
            return success;
        }
        catch (Exception ex)
        {
            lastRocketExitCode = -1;
            lastRocketStatus = $"{task}: {ex.Message}";
            LogError($"{task}: {ex.Message}");
            return false;
        }
    }

    private async void LogError(string message)
    {

        await webView21.CoreWebView2.ExecuteScriptAsync($"reportError('{message}')");
        MessageBox.Show(message, "Info", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private async void WebView21_SourceChanged(object sender, Microsoft.Web.WebView2.Core.CoreWebView2SourceChangedEventArgs e)
    {
        if (isHandlingSourceChange)
        {
            return;
        }

        isHandlingSourceChange = true;
        try
        {
            await webView21.CoreWebView2.ExecuteScriptAsync("localStorage.setItem('startedFromScantool', 'true');");

            var uri = webView21.Source?.AbsoluteUri ?? string.Empty;
            var nowUtc = DateTime.UtcNow;

            if (uri.StartsWith($"{portalBaseUrl}/dauerschleife"))
            {
                //Scan only one foot (separate on website)
                await HandleSingleFootScanAsync();
            }
            else if (uri.StartsWith($"{portalBaseUrl}/shoes-finder"))
            {
                //Start scanner early, but avoid immediate restart right after a completed flow.
                var recentlyExited = (nowUtc - lastScannerExitAtUtc).TotalSeconds < 20;
                if (!recentlyExited)
                {
                    await HandleScannerStartAsync();
                }
            }
            else if (uri.Contains("#scanLeft"))
            {
                if (lastHandledScannerCommand == "#scanLeft" &&
                    (nowUtc - lastHandledScannerCommandAtUtc).TotalSeconds < 3)
                {
                    return;
                }

                lastHandledScannerCommand = "#scanLeft";
                lastHandledScannerCommandAtUtc = nowUtc;
                await HandleLeftScanAsync();
            }
            else if (uri.Contains("#scanRight"))
            {
                if (lastHandledScannerCommand == "#scanRight" &&
                    (nowUtc - lastHandledScannerCommandAtUtc).TotalSeconds < 3)
                {
                    return;
                }

                lastHandledScannerCommand = "#scanRight";
                lastHandledScannerCommandAtUtc = nowUtc;
                await HandleRightScanAsync();
            }
            else if (uri.Contains("#save"))
            {
                if (lastHandledScannerCommand == "#save" &&
                    (nowUtc - lastHandledScannerCommandAtUtc).TotalSeconds < 3)
                {
                    return;
                }

                lastHandledScannerCommand = "#save";
                lastHandledScannerCommandAtUtc = nowUtc;
                //If its both feet => false (redirect after scan)
                await HandleSaveAndExitAsync(false);
            }
            else
            {
                //What is the current URL (if unhandled) DEBUG
                Debug.WriteLine("[INFO] Unhandled URL: " + uri);
            }
        }
        catch (Exception ex)
        {
            LogError($"webView21SourceChanged: {ex.Message}");
        }
        finally
        {
            isHandlingSourceChange = false;
        }
    }

    private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (isRocketRunning)
        {
            await HandleScannerExitAsync();
        }
    }

    private string GetScanBaseDirectory()
    {
        //Check if directory with scans exists
        var scanFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "scan"
        );

        if (Directory.Exists(scanFolder))
        {
            return scanFolder;
        }
        else
        {
            throw new Exception($"Ordner existiert nicht: {scanFolder}");
        }
    }

    private string? GetNewestScanFolder(string scanFolder)
    {
        if (Directory.Exists(scanFolder))
        {
            //Get newest directory from scanFolder
            return Directory.GetDirectories(scanFolder)
                .OrderByDescending(d => Directory.GetCreationTime(d))
                .FirstOrDefault();
        }
        else
        {
            throw new Exception($"Ordner existiert nicht: {scanFolder}");
        }
    }

    private void OpenLatestScan()
    {
        var baseScanDirectory = GetScanBaseDirectory();
        var newestFolder = GetNewestScanFolder(baseScanDirectory);

        if (newestFolder != null)
        {
            // �ffnet den neuesten Unterordner
            Process.Start("explorer.exe", newestFolder);
        }
        else
        {
            // Keine Unterordner vorhanden, �ffne den Hauptordner
            Process.Start("explorer.exe", baseScanDirectory);
        }
    }

    private async Task UploadFiles()
    {
        //Get user info
        UserInfo userInfo = await GetUserInfoAsync();
        if (string.IsNullOrWhiteSpace(userInfo.Id))
        {
            throw new Exception("Customer ID missing in session data.");
        }

        var configuredApiUrl = Environment.GetEnvironmentVariable("SCANTOOL_API_URL");
        var allowCustomEndpoints = string.Equals(
            Environment.GetEnvironmentVariable("SCANTOOL_ALLOW_CUSTOM_ENDPOINTS"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
        var apiURL = (!allowCustomEndpoints || string.IsNullOrWhiteSpace(configuredApiUrl))
            ? defaultApiUrl
            : configuredApiUrl;
        var requestURL = $"{apiURL}/customers/screener-file/{userInfo.Id}";

        var scanFolder = GetScanBaseDirectory();
        //Get newest directory from scanFolder
        var newestFolder = GetNewestScanFolder(scanFolder) ?? throw new Exception($"Keine Scans im Ordner vorhanden: {scanFolder}");

        //The directory name is composed as follows: <FirstName>_<LastName>_<ScannerId>_<CurrentScan> for example: Sebastian_Hofer_100849_000141
        //The directory name is also used as part of the name of some files.
        //Therefore, the folder name is stored as the variable fileNamePrefix.
        var fileNamePrefix = newestFolder.Split("\\").Last() ?? "";

        Debug.WriteLine($"[INFO] newest folder : {newestFolder}");

        using var client = new HttpClient();

        string jsCode = "localStorage.getItem('token');";
        string result = await webView21.CoreWebView2.ExecuteScriptAsync(jsCode);

        result = result.Trim('"');
        if (string.IsNullOrWhiteSpace(result) || string.Equals(result, "null", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Login token missing from frontend localStorage.");
        }

        client.DefaultRequestHeaders.Add("Authorization", result);

        using var formData = new MultipartFormDataContent();

        var fileMappings = new Dictionary<string, string>
                        {
                            { "csvFile", Path.Combine(newestFolder, $"{fileNamePrefix}_Mesurement.csv") },
                            { "threed_model_left", $$"""{{newestFolder}}\{{fileNamePrefix}}_L.stl""" },
                            { "threed_model_right", $$"""{{newestFolder}}\{{fileNamePrefix}}_R.stl""" },
                            { "picture_10", $$"""{{newestFolder}}\Resource_Pictures\10.jpg""" },
                            { "picture_11", $$"""{{newestFolder}}\Resource_Pictures\11.jpg""" },
                            { "picture_16", $$"""{{newestFolder}}\Resource_Pictures\16.jpg""" },
                            { "picture_17", $$"""{{newestFolder}}\Resource_Pictures\17.jpg""" },
                            { "picture_23", $$"""{{newestFolder}}\Resource_Pictures\23.jpg""" },
                            { "picture_24", $$"""{{newestFolder}}\Resource_Pictures\24.jpg""" },
                        };

        foreach (var mapping in fileMappings)
        {
            string fieldName = mapping.Key;
            string filePath = mapping.Value;

            Debug.WriteLine($"[INFO] file path : {filePath}");

            if (File.Exists(filePath))
            {
                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var fileName = Path.GetFileName(filePath);
                var fileContent = new ByteArrayContent(fileBytes);

                formData.Add(fileContent, fieldName, fileName);
            }
            else
            {
                throw new Exception($"Datei nicht gefunden: {filePath}");
            }
        }


        var request = new HttpRequestMessage(HttpMethod.Post, requestURL)
        {
            Content = formData
        };
        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            var backendMessage = responseBody;
            try
            {
                using var errorDoc = JsonDocument.Parse(responseBody);
                if (errorDoc.RootElement.TryGetProperty("message", out var messageNode))
                {
                    backendMessage = messageNode.GetString() ?? backendMessage;
                }
            }
            catch
            {
                // Keep raw response body when it is not valid JSON.
            }

            throw new Exception(
                $"Upload failed ({(int)response.StatusCode} {response.StatusCode}) at {requestURL}. Backend says: {backendMessage}"
            );
        }
    }

    private void StartLoading(bool isScanning)
    {
        scanInProgressLabel.Visible = isScanning;
        saveInProgressLabel.Visible = !isScanning;

        pnlLoadingContainer.Visible = true;
        string jsCode = "document.getElementById('scantool-overlay').style.display = 'block';";
        webView21.ExecuteScriptAsync(jsCode);
    }

    private void EndLoading()
    {
        pnlLoadingContainer.Visible = false;
        string jsCode = "document.getElementById('scantool-overlay').style.display = 'none';";
        webView21.ExecuteScriptAsync(jsCode);
    }

    private bool EnsureRocketExecutableConfigured()
    {
        if (!string.IsNullOrWhiteSpace(rocketExePath) && File.Exists(rocketExePath))
        {
            return true;
        }

        rocketExePath = LoadSavedRocketPath();
        if (!string.IsNullOrWhiteSpace(rocketExePath) && File.Exists(rocketExePath))
        {
            return true;
        }

        foreach (var candidate in GetRocketPathCandidates())
        {
            if (File.Exists(candidate))
            {
                rocketExePath = candidate;
                SaveRocketPath(candidate);
                return true;
            }
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Select XPOD_Rocket.exe",
            Filter = "XPOD Rocket (XPOD_Rocket.exe)|XPOD_Rocket.exe|Executable Files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK && File.Exists(dialog.FileName))
        {
            rocketExePath = dialog.FileName;
            SaveRocketPath(dialog.FileName);
            return true;
        }

        return false;
    }

    private string LoadSavedRocketPath()
    {
        try
        {
            if (File.Exists(rocketPathConfigFile))
            {
                return File.ReadAllText(rocketPathConfigFile).Trim();
            }
        }
        catch
        {
            // Ignore persisted-path read failure.
        }

        return string.Empty;
    }

    private void SaveRocketPath(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(rocketPathConfigFile);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(rocketPathConfigFile, path);
        }
        catch
        {
            // Ignore persisted-path write failure.
        }
    }

    private static IEnumerable<string> GetRocketPathCandidates()
    {
        var candidates = new List<string>
        {
            defaultRocketExePath,
            @"C:\XPOD 3D Full Foot Scan\Bin\XPOD_Rocket.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "XPOD 3D Full Foot Scan",
                "Bin",
                "XPOD_Rocket.exe"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "XPOD 3D Full Foot Scan",
                "Bin",
                "XPOD_Rocket.exe"
            )
        };

        return candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
