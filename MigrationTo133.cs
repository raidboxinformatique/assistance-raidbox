using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class MigrationTo133
{
    private const string TargetVersion = "1.37";
    private const string InstallerUrl =
        "https://github.com/raidboxinformatique/assistance-raidbox/releases/download/v" + TargetVersion
        + "/Assistance-Raidbox-Setup-" + TargetVersion + ".exe";
    private const string InstallerSha256 =
        "af0c37c8afb674504004b3e22dfe4cd3d291a826a5d55b6b07bf18f267ece7ae";
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/raidboxinformatique/assistance-raidbox/main/latest.json";
    private const string InstallerAppId = "8B0E7258-FB30-41F7-8E12-D0BD8EF62525";
    private const string ApplicationDisplayName = "Assistance RAIDBOX";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                RunSelfTest();
                return 0;
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Raidbox",
                "Assistance");
            string launcherPath = Path.Combine(installDir, "AssistanceRaidbox.exe");
            string configPath = Path.Combine(installDir, "appsettings.json");
            string legacyCheckoutDir = GetLegacyCheckoutDirectory(installDir);
            Version installedVersion = GetInstalledVersion(configPath, launcherPath);
            Version targetVersion = new Version(TargetVersion);
            bool targetRegistered = IsTargetVersionOrNewerRegistered();

            if (installedVersion == null
                || installedVersion < targetVersion
                || (installedVersion == targetVersion && !targetRegistered))
            {
                InstallTargetVersion();
            }

            installedVersion = GetInstalledVersion(configPath, launcherPath);
            if (installedVersion == null || installedVersion < targetVersion)
            {
                throw new InvalidOperationException(
                    "La version installee n'a pas pu etre confirmee apres la mise a jour.");
            }
            if (installedVersion == targetVersion && !IsTargetVersionOrNewerRegistered())
            {
                throw new InvalidOperationException(
                    "Windows n'a pas enregistre Assistance RAIDBOX " + TargetVersion
                    + " dans Programmes et fonctionnalites.");
            }

            PatchUpdateConfiguration(configPath);
            StopInstalledLauncherInstances(launcherPath);
            StartLauncher(launcherPath, legacyCheckoutDir);
            WriteLog("Migration vers la version " + TargetVersion + " terminee.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog("ERREUR: " + ex);
            MessageBox.Show(
                "La mise a jour automatique vers Assistance RAIDBOX " + TargetVersion
                + " a echoue.\r\n\r\n" + ex.Message,
                "Assistance RAIDBOX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void RunSelfTest()
    {
        Uri installerUri;
        Uri manifestUri;
        if (!Uri.TryCreate(InstallerUrl, UriKind.Absolute, out installerUri) || installerUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("URL installeur invalide.");
        }
        if (!Uri.TryCreate(ManifestUrl, UriKind.Absolute, out manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("URL manifest invalide.");
        }
        if (InstallerSha256.Length != 64)
        {
            throw new InvalidOperationException("SHA-256 installeur invalide.");
        }

        string testDir = Path.Combine(Path.GetTempPath(), "Raidbox-Migration-SelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            string testConfig = Path.Combine(testDir, "appsettings.json");
            File.WriteAllText(
                testConfig,
                "{\"ApplicationVersion\":\"" + TargetVersion
                + "\",\"AllowedDownloadHosts\":[\"raidbox.info\"]}",
                new UTF8Encoding(false));
            PatchUpdateConfiguration(testConfig);
            Dictionary<string, object> patched = ReadJson(testConfig);
            object configuredManifest;
            if (!patched.TryGetValue("ManifestUrl", out configuredManifest)
                || !string.Equals(Convert.ToString(configuredManifest), ManifestUrl, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Le manifeste GitHub n'est pas applique.");
            }

            string legacyDir = Path.Combine(testDir, "legacy");
            Directory.CreateDirectory(Path.Combine(legacyDir, ".git"));
            Directory.CreateDirectory(Path.Combine(legacyDir, "git"));
            File.WriteAllText(Path.Combine(legacyDir, "teamviewer.bat"), "@echo off");
            File.WriteAllText(Path.Combine(legacyDir, "TeamViewerQS.exe"), "test");
            File.WriteAllText(
                Path.Combine(legacyDir, ".git", "config"),
                "[remote \"origin\"]\r\nurl = https://github.com/raidboxinformatique/assistance-raidbox.git");
            if (!IsLegacyCheckoutDirectory(legacyDir))
            {
                throw new InvalidOperationException("L'ancien dossier RAIDBOX n'est pas reconnu.");
            }
            if (IsLegacyCheckoutDirectory(testDir))
            {
                throw new InvalidOperationException("Un dossier non RAIDBOX a ete reconnu a tort.");
            }

            string cleanupArguments = BuildLegacyCleanupArguments(legacyDir);
            string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyDir));
            if (cleanupArguments.IndexOf(encodedPath, StringComparison.Ordinal) < 0
                || cleanupArguments.IndexOf(
                    Process.GetCurrentProcess().Id.ToString(),
                    StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Les parametres de nettoyage sont invalides.");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(testDir, true);
            }
            catch
            {
            }
        }
    }

    private static Version GetInstalledVersion(string configPath, string launcherPath)
    {
        if (!File.Exists(configPath) || !File.Exists(launcherPath))
        {
            return null;
        }

        try
        {
            Dictionary<string, object> config = ReadJson(configPath);
            object configuredVersion;
            Version installed;
            if (config.TryGetValue("ApplicationVersion", out configuredVersion)
                && Version.TryParse(Convert.ToString(configuredVersion), out installed))
            {
                return installed;
            }
        }
        catch
        {
        }
        return null;
    }

    private static bool IsTargetVersionOrNewerRegistered()
    {
        Version targetVersion = new Version(TargetVersion);
        RegistryHive[] hives = { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstallKey = baseKey.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstallKey == null)
                        {
                            continue;
                        }

                        foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                        {
                            using (RegistryKey applicationKey = uninstallKey.OpenSubKey(subKeyName))
                            {
                                if (applicationKey == null)
                                {
                                    continue;
                                }

                                string displayName = Convert.ToString(applicationKey.GetValue("DisplayName"));
                                bool matchingApp = subKeyName.IndexOf(
                                        InstallerAppId,
                                        StringComparison.OrdinalIgnoreCase) >= 0
                                    || string.Equals(
                                        displayName,
                                        ApplicationDisplayName,
                                        StringComparison.OrdinalIgnoreCase);
                                if (!matchingApp)
                                {
                                    continue;
                                }

                                Version registeredVersion;
                                if (Version.TryParse(
                                        Convert.ToString(applicationKey.GetValue("DisplayVersion")),
                                        out registeredVersion)
                                    && registeredVersion >= targetVersion)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        return false;
    }

    private static void InstallTargetVersion()
    {
        string installerPath = Path.Combine(
            Path.GetTempPath(),
            "Assistance-Raidbox-Setup-" + TargetVersion + ".exe");

        WriteLog("Telechargement de " + InstallerUrl);
        using (WebClient client = new WebClient())
        {
            client.Headers.Add(HttpRequestHeader.UserAgent, "Assistance-RAIDBOX-Migration/" + TargetVersion);
            client.DownloadFile(InstallerUrl, installerPath);
        }

        string actualHash = GetSha256(installerPath);
        if (!string.Equals(actualHash, InstallerSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(installerPath);
            throw new InvalidDataException("Le controle SHA-256 de l'installeur a echoue.");
        }

        WriteLog("Installation silencieuse de la version " + TargetVersion + ".");
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)
        };

        using (Process process = Process.Start(startInfo))
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("L'installeur a retourne le code " + process.ExitCode + ".");
            }
        }

        try
        {
            File.Delete(installerPath);
        }
        catch
        {
        }
    }

    private static void PatchUpdateConfiguration(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Configuration Assistance RAIDBOX introuvable apres installation.", configPath);
        }

        Dictionary<string, object> config = ReadJson(configPath);
        config["ManifestUrl"] = ManifestUrl;

        List<object> hosts = new List<object>();
        object configuredHosts;
        if (config.TryGetValue("AllowedDownloadHosts", out configuredHosts))
        {
            IEnumerable enumerable = configuredHosts as IEnumerable;
            if (enumerable != null)
            {
                foreach (object item in enumerable)
                {
                    AddHost(hosts, Convert.ToString(item));
                }
            }
        }

        AddHost(hosts, "www.raidbox.info");
        AddHost(hosts, "raidbox.info");
        AddHost(hosts, "raw.githubusercontent.com");
        AddHost(hosts, "github.com");
        config["AllowedDownloadHosts"] = hosts;

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        string json = serializer.Serialize(config);
        File.WriteAllText(configPath, json, new UTF8Encoding(false));
        WriteLog("Configuration des mises a jour GitHub appliquee.");
    }

    private static Dictionary<string, object> ReadJson(string path)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<string, object> result =
            serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
        if (result == null)
        {
            throw new InvalidDataException("Configuration JSON invalide.");
        }
        return result;
    }

    private static void AddHost(List<object> hosts, string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }
        foreach (object existing in hosts)
        {
            if (string.Equals(Convert.ToString(existing), host, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        hosts.Add(host);
    }

    private static string GetSha256(string path)
    {
        using (SHA256 sha256 = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] hash = sha256.ComputeHash(stream);
            StringBuilder result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                result.Append(value.ToString("x2"));
            }
            return result.ToString();
        }
    }

    private static void StopInstalledLauncherInstances(string launcherPath)
    {
        string processName = Path.GetFileNameWithoutExtension(launcherPath);
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        DateTime noInstanceDeadline = DateTime.UtcNow.AddSeconds(4);
        DateTime quietSince = DateTime.UtcNow;
        bool foundMatchingProcess = false;

        while (DateTime.UtcNow < deadline)
        {
            bool stoppedProcess = false;
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        string processPath = process.MainModule == null
                            ? string.Empty
                            : process.MainModule.FileName;
                        if (!string.Equals(
                                Path.GetFullPath(processPath),
                                Path.GetFullPath(launcherPath),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foundMatchingProcess = true;
                        stoppedProcess = true;
                        WriteLog("Fermeture de l'instance lancee par l'installeur.");
                        if (process.CloseMainWindow())
                        {
                            process.WaitForExit(3000);
                        }
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }

            if (stoppedProcess)
            {
                quietSince = DateTime.UtcNow;
            }
            else if (foundMatchingProcess && DateTime.UtcNow - quietSince >= TimeSpan.FromSeconds(1))
            {
                return;
            }
            else if (!foundMatchingProcess && DateTime.UtcNow >= noInstanceDeadline)
            {
                return;
            }

            Thread.Sleep(200);
        }
    }

    private static void StartLauncher(string launcherPath, string legacyCheckoutDir)
    {
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException("AssistanceRaidbox.exe introuvable apres installation.", launcherPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            Arguments = string.IsNullOrWhiteSpace(legacyCheckoutDir)
                ? string.Empty
                : BuildLegacyCleanupArguments(legacyCheckoutDir),
            WorkingDirectory = Path.GetDirectoryName(launcherPath),
            UseShellExecute = true
        });
    }

    private static string GetLegacyCheckoutDirectory(string installDir)
    {
        string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedInstallDir = Path.GetFullPath(installDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(baseDir, normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return IsLegacyCheckoutDirectory(baseDir) ? baseDir : null;
    }

    private static bool IsLegacyCheckoutDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(Path.Combine(directory, ".git"))
            || !Directory.Exists(Path.Combine(directory, "git"))
            || !File.Exists(Path.Combine(directory, "teamviewer.bat"))
            || !File.Exists(Path.Combine(directory, "TeamViewerQS.exe")))
        {
            return false;
        }

        string gitConfigPath = Path.Combine(directory, ".git", "config");
        if (!File.Exists(gitConfigPath))
        {
            return false;
        }

        try
        {
            return File.ReadAllText(gitConfigPath).IndexOf(
                "github.com/raidboxinformatique/assistance-raidbox",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildLegacyCleanupArguments(string legacyCheckoutDir)
    {
        string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyCheckoutDir));
        return "--cleanup-legacy=" + encodedPath
            + " --wait-pid=" + Process.GetCurrentProcess().Id;
    }

    private static void WriteLog(string message)
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Raidbox",
                "Assistance",
                "Logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "migration-" + TargetVersion + ".log"),
                DateTime.UtcNow.ToString("u") + " " + message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
