using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class MigrationTo133
{
    private const string TargetVersion = "1.33";
    private const string InstallerUrl =
        "https://github.com/raidboxinformatique/assistance-raidbox/releases/download/v1.33/Assistance-Raidbox-Setup-1.33.exe";
    private const string InstallerSha256 =
        "7c18fd2afca728df2233dfee4fbc8eaf4f56dbe54e316ae2f70e5feabdb7ea83";
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/raidboxinformatique/assistance-raidbox/main/latest.json";

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

            if (!IsTargetVersionInstalled(configPath, launcherPath))
            {
                InstallTargetVersion();
            }

            PatchUpdateConfiguration(configPath);
            StartLauncher(launcherPath);
            WriteLog("Migration vers la version 1.33 terminee.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog("ERREUR: " + ex);
            MessageBox.Show(
                "La mise a jour automatique vers Assistance RAIDBOX 1.33 a echoue.\r\n\r\n" + ex.Message,
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
                "{\"ApplicationVersion\":\"1.33\",\"AllowedDownloadHosts\":[\"raidbox.info\"]}",
                new UTF8Encoding(false));
            PatchUpdateConfiguration(testConfig);
            Dictionary<string, object> patched = ReadJson(testConfig);
            object configuredManifest;
            if (!patched.TryGetValue("ManifestUrl", out configuredManifest)
                || !string.Equals(Convert.ToString(configuredManifest), ManifestUrl, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Le manifeste GitHub n'est pas applique.");
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

    private static bool IsTargetVersionInstalled(string configPath, string launcherPath)
    {
        if (!File.Exists(configPath) || !File.Exists(launcherPath))
        {
            return false;
        }

        try
        {
            Dictionary<string, object> config = ReadJson(configPath);
            object version;
            return config.TryGetValue("ApplicationVersion", out version)
                && string.Equals(Convert.ToString(version), TargetVersion, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void InstallTargetVersion()
    {
        string installerPath = Path.Combine(
            Path.GetTempPath(),
            "Assistance-Raidbox-Setup-" + TargetVersion + ".exe");

        WriteLog("Telechargement de " + InstallerUrl);
        using (WebClient client = new WebClient())
        {
            client.Headers.Add(HttpRequestHeader.UserAgent, "Assistance-RAIDBOX-Migration/1.33");
            client.DownloadFile(InstallerUrl, installerPath);
        }

        string actualHash = GetSha256(installerPath);
        if (!string.Equals(actualHash, InstallerSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(installerPath);
            throw new InvalidDataException("Le controle SHA-256 de l'installeur a echoue.");
        }

        WriteLog("Installation silencieuse de la version 1.33.");
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

    private static void StartLauncher(string launcherPath)
    {
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException("AssistanceRaidbox.exe introuvable apres installation.", launcherPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = Path.GetDirectoryName(launcherPath),
            UseShellExecute = true
        });
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
                Path.Combine(logDir, "migration-1.33.log"),
                DateTime.UtcNow.ToString("u") + " " + message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
