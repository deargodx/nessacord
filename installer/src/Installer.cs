// Nessacord installer: finding Discord installs, patching them and putting them back.
//
// Install puts Nessacord's injector in place of Discord's resources\app.asar (Discord's own is
// kept as _app.asar) and Nessacord's files in %APPDATA%\Nessacord\dist. Nessacord re-patches
// Discord's new version folders itself after Discord updates. Uninstall puts Discord's own
// app.asar back, removes Nessacord.exe and restores the shortcuts Nessacord branded.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Nessacord.Installer
{
    enum ModState { Discord, Nessacord, OtherMod, Broken }

    class DiscordInstall
    {
        public string Name;
        public string Root;
        public string AppFolder;
        public string ExeName;
        public ModState State;

        public string Resources { get { return Path.Combine(AppFolder, "resources"); } }
        public bool HasBrandedExe { get { return File.Exists(Path.Combine(AppFolder, ExeBrander.ExeName)); } }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case ModState.Nessacord: return "Nessacord installed";
                    case ModState.OtherMod: return "Vencord or another mod installed";
                    case ModState.Broken: return "Discord's files are missing";
                    default: return "Not installed";
                }
            }
        }
    }

    class InstallOptions
    {
        public string DataDir = Installer.DefaultDataDir;
        public string DistSource;       // a local dist folder instead of the latest GitHub release
        public bool TaskManagerName;
        public bool KeepTaskManagerSetting = true;
    }

    static class Installer
    {
        public const string AppName = "Nessacord";
        public const string InstallerExe = "NessacordInstaller.exe";

        public static readonly string DefaultDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

        static readonly string[][] Channels =
        {
            new[] { "Discord", "Discord" },
            new[] { "Discord PTB", "DiscordPTB" },
            new[] { "Discord Canary", "DiscordCanary" },
            new[] { "Discord Development", "DiscordDevelopment" }
        };

        static readonly Regex AppFolderName = new Regex(@"^app-(\d+(?:\.\d+)*)$");

        // ── Finding installs ────────────────────────────────────────────────────────────────

        public static List<DiscordInstall> FindInstalls()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var installs = new List<DiscordInstall>();
            foreach (string[] channel in Channels)
            {
                DiscordInstall install = Inspect(Path.Combine(local, channel[1]), channel[0]);
                if (install != null) installs.Add(install);
            }
            return installs;
        }

        /// What's chosen when nobody picks: Discord itself (or the only install), and any that already have Nessacord.
        /// Never another mod's install unless it's the only one.
        public static bool SelectedByDefault(DiscordInstall install, List<DiscordInstall> installs)
        {
            return installs.Count == 1 || install.Name == "Discord" || install.State == ModState.Nessacord;
        }

        /// A Discord install at root (the folder with Update.exe and the app-* folders), or null.
        public static DiscordInstall Inspect(string root, string name)
        {
            if (!Directory.Exists(root)) return null;

            // Discord's updater stages new versions as ".app-<version>.installing"; only finished ones count.
            string latest = null;
            Version latestVersion = null;
            foreach (string dir in Directory.GetDirectories(root))
            {
                Match match = AppFolderName.Match(Path.GetFileName(dir));
                if (!match.Success) continue;
                Version version = ParseVersion(match.Groups[1].Value);
                if (latestVersion == null || version > latestVersion)
                {
                    latest = dir;
                    latestVersion = version;
                }
            }
            if (latest == null) return null;

            string exe = Directory.GetFiles(latest, "Discord*.exe").Select(Path.GetFileName).FirstOrDefault();
            if (exe == null) return null;

            var install = new DiscordInstall { Name = name ?? Path.GetFileName(root), Root = root, AppFolder = latest, ExeName = exe };
            install.State = ReadState(install);
            return install;
        }

        static Version ParseVersion(string text)
        {
            string[] parts = text.Split('.');
            int[] numbers = new int[4];
            for (int i = 0; i < Math.Min(4, parts.Length); i++) int.TryParse(parts[i], out numbers[i]);
            return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        static ModState ReadState(DiscordInstall install)
        {
            string appAsar = Path.Combine(install.Resources, "app.asar");
            string original = Path.Combine(install.Resources, "_app.asar");
            if (!File.Exists(appAsar)) return ModState.Broken;
            if (!File.Exists(original)) return ModState.Discord;
            if (new FileInfo(appAsar).Length > 256 * 1024) return ModState.OtherMod;
            return Encoding.UTF8.GetString(File.ReadAllBytes(appAsar)).Contains("Nessacord injector")
                ? ModState.Nessacord
                : ModState.OtherMod;
        }

        // ── Install ─────────────────────────────────────────────────────────────────────────

        public static void Install(DiscordInstall install, InstallOptions options, Action<string> log)
        {
            if (install.State == ModState.Broken) throw new InvalidOperationException(install.Name + ": Discord's files are missing. Reinstall Discord first.");

            bool wasRunning = CloseDiscord(install, log);

            string patcher = Path.Combine(options.DataDir, "dist", "patcher.js");
            if (!File.Exists(patcher)) throw new InvalidOperationException("Nessacord's files are missing from " + Path.GetDirectoryName(patcher));

            string appAsar = Path.Combine(install.Resources, "app.asar");
            string original = Path.Combine(install.Resources, "_app.asar");
            string temp = appAsar + ".nessacord-tmp";

            File.WriteAllBytes(temp, MakeInjectorAsar(patcher));
            if (install.State == ModState.Discord) File.Move(appAsar, original);
            else if (install.State == ModState.OtherMod) log(install.Name + ": replacing the other mod's injector (Discord's own files stay as they are)");
            if (File.Exists(appAsar)) File.Delete(appAsar);
            File.Move(temp, appAsar);

            // Older Vencord installs used a resources\app folder, which Discord would load first.
            string appDir = Path.Combine(install.Resources, "app");
            if (Directory.Exists(appDir)) Directory.Delete(appDir, true);

            if (!options.KeepTaskManagerSetting) SetTaskManagerSetting(options.DataDir, options.TaskManagerName);
            bool branded = options.KeepTaskManagerSetting ? ReadTaskManagerSetting(options.DataDir) : options.TaskManagerName;
            if (branded)
            {
                log(install.Name + ": making Nessacord.exe for Task Manager");
                ExeBrander.Brand(install.AppFolder, install.ExeName, ReadResource("icon.ico"));
            }
            else if (install.HasBrandedExe)
            {
                File.Delete(Path.Combine(install.AppFolder, ExeBrander.ExeName));
            }

            install.State = ModState.Nessacord;
            log(install.Name + ": Nessacord installed");

            if (wasRunning) StartDiscord(install);
        }

        // ── Uninstall ───────────────────────────────────────────────────────────────────────

        public static void Uninstall(DiscordInstall install, string dataDir, Action<string> log)
        {
            bool wasRunning = CloseDiscord(install, log);

            // Every version folder, in case an older one is still around.
            foreach (string dir in Directory.GetDirectories(install.Root))
            {
                if (!AppFolderName.IsMatch(Path.GetFileName(dir))) continue;

                string resources = Path.Combine(dir, "resources");
                string appAsar = Path.Combine(resources, "app.asar");
                string original = Path.Combine(resources, "_app.asar");
                bool ours = File.Exists(appAsar) && new FileInfo(appAsar).Length < 256 * 1024
                    && Encoding.UTF8.GetString(File.ReadAllBytes(appAsar)).Contains("Nessacord injector");

                if (ours && File.Exists(original))
                {
                    File.Delete(appAsar);
                    File.Move(original, appAsar);
                }

                string brandedExe = Path.Combine(dir, ExeBrander.ExeName);
                if (File.Exists(brandedExe)) File.Delete(brandedExe);
            }

            int restored = Shortcuts.Restore(Path.Combine(dataDir, "shortcuts.json"), install.Root);
            if (restored > 0) log(install.Name + ": put back " + restored + " shortcut" + (restored == 1 ? "" : "s"));

            install.State = ModState.Discord;
            log(install.Name + ": Nessacord removed");

            if (wasRunning) StartDiscord(install);
        }

        // ── Task Manager (Nessacord.exe) ────────────────────────────────────────────────────

        /// Called by Nessacord after a Discord update: makes Nessacord.exe in the new version folder.
        public static void BrandExe(string appFolder)
        {
            string exe = Directory.GetFiles(appFolder, "Discord*.exe").Select(Path.GetFileName).First();
            ExeBrander.Brand(appFolder, exe, ReadResource("icon.ico"));
        }

        // The option is Nessacord's own setting (Nessacord plugin, "taskManagerName"), so it can also
        // be changed in Nessacord's settings.
        static string SettingsFile(string dataDir)
        {
            return Path.Combine(dataDir, "settings", "settings.json");
        }

        public static bool ReadTaskManagerSetting(string dataDir)
        {
            try
            {
                var settings = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsFile(dataDir)));
                var plugins = settings["plugins"] as Dictionary<string, object>;
                var plugin = plugins["Nessacord"] as Dictionary<string, object>;
                return plugin.ContainsKey("taskManagerName") && true.Equals(plugin["taskManagerName"]);
            }
            catch
            {
                return false;
            }
        }

        static void SetTaskManagerSetting(string dataDir, bool value)
        {
            string file = SettingsFile(dataDir);
            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> settings = null;
            try
            {
                settings = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (settings == null) settings = new Dictionary<string, object>();

            object plugins;
            if (!settings.TryGetValue("plugins", out plugins) || !(plugins is Dictionary<string, object>))
                settings["plugins"] = plugins = new Dictionary<string, object>();
            object plugin;
            if (!((Dictionary<string, object>)plugins).TryGetValue("Nessacord", out plugin) || !(plugin is Dictionary<string, object>))
                ((Dictionary<string, object>)plugins)["Nessacord"] = plugin = new Dictionary<string, object> { { "enabled", true } };
            ((Dictionary<string, object>)plugin)["taskManagerName"] = value;

            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, serializer.Serialize(settings));
        }

        // ── Discord processes ───────────────────────────────────────────────────────────────

        static List<Process> DiscordProcesses(DiscordInstall install)
        {
            string root = install.Root.TrimEnd('\\') + "\\";
            var found = new List<Process>();
            foreach (string name in new[] { Path.GetFileNameWithoutExtension(install.ExeName), "Nessacord" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (process.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase)) found.Add(process);
                    }
                    catch
                    {
                        // Another user's process, or it already exited.
                    }
                }
            }
            return found;
        }

        public static bool IsRunning(DiscordInstall install)
        {
            return DiscordProcesses(install).Count > 0;
        }

        static bool CloseDiscord(DiscordInstall install, Action<string> log)
        {
            List<Process> processes = DiscordProcesses(install);
            if (processes.Count == 0) return false;

            log(install.Name + ": closing Discord");
            foreach (Process process in processes)
            {
                try { process.Kill(); } catch { }
            }
            foreach (Process process in processes)
            {
                try { process.WaitForExit(5000); } catch { }
            }
            Thread.Sleep(500);
            return true;
        }

        public static void StartDiscord(DiscordInstall install)
        {
            // Through the shell, so Discord doesn't inherit (and hold open) the installer's console.
            string update = Path.Combine(install.Root, "Update.exe");
            if (File.Exists(update))
                Process.Start(new ProcessStartInfo(update, "--processStart " + install.ExeName) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(Path.Combine(install.AppFolder, install.ExeName)) { UseShellExecute = true });
        }

        // ── Injector ────────────────────────────────────────────────────────────────────────
        // Same bytes as makeInjectorAsar in src/main/injector.ts, which re-creates it after Discord updates.

        static byte[] MakeInjectorAsar(string patcherPath)
        {
            string template = Encoding.UTF8.GetString(ReadResource("injector.template.js"));
            string index = template.Replace("__NESSACORD_PATCHER__", JsonString(patcherPath)) + "\n";
            string package = "{\n\t\"name\": \"discord\",\n\t\"main\": \"index.js\"\n}";
            return MakeAsar(new[] { "index.js", "package.json" }, new[] { index, package });
        }

        static string JsonString(string text)
        {
            var json = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c < 0x20) json.AppendFormat("\\u{0:x4}", (int)c);
                else json.Append(c);
            }
            return json.Append('"').ToString();
        }

        static byte[] MakeAsar(string[] names, string[] files)
        {
            byte[][] contents = files.Select(f => Encoding.UTF8.GetBytes(f)).ToArray();
            var header = new StringBuilder("{\"files\":{");
            long offset = 0;
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) header.Append(',');
                header.AppendFormat("\"{0}\":{{\"size\":{1},\"offset\":\"{2}\"}}", names[i], contents[i].Length, offset);
                offset += contents[i].Length;
            }
            header.Append("}}");

            byte[] json = Encoding.UTF8.GetBytes(header.ToString());
            int padding = (4 - json.Length % 4) % 4;
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write(4);
            writer.Write(8 + json.Length + padding);
            writer.Write(4 + json.Length + padding);
            writer.Write(json.Length);
            writer.Write(json);
            writer.Write(new byte[padding]);
            foreach (byte[] content in contents) writer.Write(content);
            return stream.ToArray();
        }

        // ── Nessacord's own files ───────────────────────────────────────────────────────────

        /// Puts Nessacord's files in dataDir\dist: from a local build folder, or the latest release.
        public static void InstallFiles(InstallOptions options, Action<string> log)
        {
            string dist = Path.Combine(options.DataDir, "dist");
            if (options.DistSource != null)
            {
                log("Copying Nessacord from " + options.DistSource);
                Release.CopyFrom(options.DistSource, dist);
            }
            else
            {
                Release.Download(dist, log);
            }

            // A copy of the installer stays with Nessacord: it is the uninstaller, and Nessacord uses
            // it to make Nessacord.exe after Discord updates.
            string self = Assembly.GetExecutingAssembly().Location;
            string kept = Path.Combine(options.DataDir, InstallerExe);
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, kept, true);
        }

        public static void RegisterUninstaller(string dataDir)
        {
            string exe = Path.Combine(dataDir, InstallerExe);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayIcon", exe);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                key.SetValue("InstallLocation", dataDir);
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("ModifyPath", "\"" + exe + "\"");
                key.SetValue("NoRepair", 1);
            }
        }

        public static void UnregisterUninstaller()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false);
        }

        public static byte[] ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("The installer is missing " + name);
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
