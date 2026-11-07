// Nessacord installer. Run it to get the window, or from a command line:
//
//   NessacordInstaller.exe --install | --repair | --uninstall | --status
//       [--discord-root <folder>]   just this Discord install (otherwise: Discord, plus any that have Nessacord)
//       [--dist <folder>]           install a local build instead of the latest release
//       [--data-dir <folder>]       Nessacord's data folder (default %APPDATA%\Nessacord)
//       [--task-manager | --no-task-manager]
//       [--quiet]                   no windows or questions
//
//   --brand-exe <app folder>       used by Nessacord after Discord updates: makes Nessacord.exe

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Nessacord Installer")]
[assembly: AssemblyProduct("Nessacord")]
[assembly: AssemblyDescription("Installs Nessacord into Discord")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Nessacord.Installer
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var options = new InstallOptions();
            string action = null, discordRoot = null, brandFolder = null;
            bool quiet = false;
            bool? taskManager = null;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--install": case "--repair": action = "install"; break;
                        case "--uninstall": action = "uninstall"; break;
                        case "--status": action = "status"; break;
                        case "--brand-exe": brandFolder = args[++i]; break;
                        case "--discord-root": discordRoot = Path.GetFullPath(args[++i]); break;
                        case "--dist": options.DistSource = Path.GetFullPath(args[++i]); break;
                        case "--data-dir": options.DataDir = Path.GetFullPath(args[++i]); break;
                        case "--task-manager": taskManager = true; break;
                        case "--no-task-manager": taskManager = false; break;
                        case "--quiet": quiet = true; break;
                        default: throw new ArgumentException("Unknown option " + args[i]);
                    }
                }
            }
            catch (Exception err)
            {
                return Fail(err.Message, quiet || action != null);
            }

            if (brandFolder != null)
            {
                try
                {
                    Installer.BrandExe(brandFolder);
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            List<DiscordInstall> installs;
            if (discordRoot != null)
            {
                DiscordInstall install = Installer.Inspect(discordRoot, null);
                if (install == null) return Fail("No Discord install in " + discordRoot, quiet || action != null);
                installs = new List<DiscordInstall> { install };
            }
            else
            {
                installs = Installer.FindInstalls();
            }

            if (taskManager.HasValue)
            {
                options.TaskManagerName = taskManager.Value;
                options.KeepTaskManagerSetting = false;
            }

            if (action == null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerForm(options, installs));
                return 0;
            }

            // Apps & features runs "--uninstall" without --quiet: ask first, then say how it went.
            bool windowed = action == "uninstall" && !quiet;
            if (!windowed) AttachConsole(-1);

            if (windowed && MessageBox.Show("Remove Nessacord from Discord?\n\nYour Nessacord settings are kept.", "Nessacord", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return 1;

            var log = new List<string>();
            Action<string> write = line =>
            {
                log.Add(line);
                if (!windowed) Console.WriteLine(line);
            };

            try
            {
                switch (action)
                {
                    case "status":
                        foreach (DiscordInstall install in installs)
                            write(install.Name + ": " + install.StateText + (install.HasBrandedExe ? " (Nessacord.exe)" : "") + "  [" + install.AppFolder + "]");
                        if (installs.Count == 0) write("No Discord install found");
                        break;

                    case "install":
                        if (installs.Count == 0) throw new InvalidOperationException("No Discord install found");
                        Installer.InstallFiles(options, write);
                        foreach (DiscordInstall install in installs.Where(i => i.State != ModState.Broken && Installer.SelectedByDefault(i, installs))) Installer.Install(install, options, write);
                        if (discordRoot == null) Installer.RegisterUninstaller(options.DataDir);
                        break;

                    case "uninstall":
                        foreach (DiscordInstall install in installs.Where(i => i.State == ModState.Nessacord)) Installer.Uninstall(install, options.DataDir, write);
                        if (discordRoot == null) Installer.UnregisterUninstaller();
                        break;
                }
            }
            catch (Exception err)
            {
                return Fail(err.Message, !windowed);
            }

            if (windowed) MessageBox.Show(log.Count > 0 ? string.Join("\n", log) : "Nessacord wasn't installed.", "Nessacord", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        static int Fail(string message, bool console)
        {
            if (console)
            {
                AttachConsole(-1);
                Console.Error.WriteLine("Error: " + message);
            }
            else
            {
                MessageBox.Show(message, "Nessacord", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);
    }
}
