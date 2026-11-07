// Nessacord installer: the window.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Nessacord.Installer
{
    class InstallerForm : Form
    {
        static readonly Color Background = Color.FromArgb(8, 8, 8);
        static readonly Color Panel = Color.FromArgb(17, 17, 17);
        static readonly Color Line = Color.FromArgb(40, 40, 40);
        static readonly Color Ivory = Color.FromArgb(233, 230, 225);
        static readonly Color Muted = Color.FromArgb(140, 140, 140);

        readonly InstallOptions options;
        readonly List<DiscordInstall> installs;
        readonly List<CheckBox> installBoxes = new List<CheckBox>();
        readonly CheckBox taskManagerBox;
        readonly Button installButton, uninstallButton;
        readonly TextBox logBox;

        public InstallerForm(InstallOptions options, List<DiscordInstall> installs)
        {
            this.options = options;
            this.installs = installs;

            Text = "Nessacord Installer";
            Icon = new Icon(new MemoryStream(Installer.ReadResource("icon.ico")));
            BackColor = Background;
            ForeColor = Ivory;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 600);

            var mark = new PictureBox
            {
                Image = Image.FromStream(new MemoryStream(Installer.ReadResource("appicon.png"))),
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(32, 28, 64, 64)
            };
            var title = new Label { Text = "Nessacord", Font = new Font("Georgia", 26f), AutoSize = true, Location = new Point(106, 30) };
            var subtitle = new Label { Text = "for Discord", Font = new Font("Georgia", 11f, FontStyle.Italic), ForeColor = Muted, AutoSize = true, Location = new Point(110, 74) };
            Controls.AddRange(new Control[] { mark, title, subtitle });

            int y = 120;
            Controls.Add(SectionLabel("DISCORD", y));
            y += 26;

            if (installs.Count == 0)
            {
                Controls.Add(new Label { Text = "No Discord install found. Install Discord first, then run this again.", ForeColor = Muted, AutoSize = true, Location = new Point(32, y) });
                y += 30;
            }
            foreach (DiscordInstall install in installs)
            {
                var box = new CheckBox
                {
                    Text = install.Name + "   ·   " + install.StateText,
                    Checked = Installer.SelectedByDefault(install, installs),
                    Enabled = install.State != ModState.Broken,
                    Tag = install,
                    AutoSize = true,
                    Location = new Point(32, y),
                    FlatStyle = FlatStyle.Flat
                };
                installBoxes.Add(box);
                Controls.Add(box);
                y += 30;
            }

            y += 12;
            Controls.Add(SectionLabel("OPTIONS", y));
            y += 26;
            taskManagerBox = new CheckBox
            {
                Text = "Show Nessacord in Task Manager",
                Checked = Installer.ReadTaskManagerSetting(options.DataDir),
                AutoSize = true,
                Location = new Point(32, y),
                FlatStyle = FlatStyle.Flat
            };
            Controls.Add(taskManagerBox);
            Controls.Add(new Label
            {
                Text = "Runs Discord from a renamed copy of its program. Krisp noise suppression only runs from Discord's own program, so Discord's Standard noise suppression is used instead.",
                ForeColor = Muted,
                Font = new Font("Segoe UI", 9f),
                Bounds = new Rectangle(50, y + 26, 478, 36)
            });
            y += 76;

            installButton = MakeButton("Install", true, new Rectangle(32, y, 150, 40));
            uninstallButton = MakeButton("Uninstall", false, new Rectangle(194, y, 150, 40));
            installButton.Click += (s, e) => Run(true);
            uninstallButton.Click += (s, e) => Run(false);
            Controls.AddRange(new Control[] { installButton, uninstallButton });
            y += 56;

            logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Panel,
                ForeColor = Color.FromArgb(190, 190, 190),
                Font = new Font("Consolas", 9f),
                Bounds = new Rectangle(32, y, 496, 548 - y)
            };
            Controls.Add(logBox);
            logBox.HandleCreated += (s, e) => SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null);
            logBox.Text = installs.Count > 0 ? "Tick the Discord versions to put Nessacord in, then choose Install." : "";

            Controls.Add(new Label
            {
                Text = "By deargod  ·  Thank you bella, for being fucking amazing  ·  Based on Vencord (GPL-3.0)",
                ForeColor = Muted,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(32, 564)
            });

            UpdateButtons();
            foreach (CheckBox box in installBoxes) box.CheckedChanged += (s, e) => UpdateButtons();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Line))
                e.Graphics.DrawLine(pen, 32, 108, ClientSize.Width - 32, 108);
        }

        static Label SectionLabel(string text, int y)
        {
            return new Label { Text = text, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = true, Location = new Point(32, y) };
        }

        static Button MakeButton(string text, bool primary, Rectangle bounds)
        {
            var button = new Button
            {
                Text = text,
                Bounds = bounds,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Ivory : Background,
                ForeColor = primary ? Color.Black : Ivory,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Line;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.White : Panel;
            return button;
        }

        List<DiscordInstall> Selected()
        {
            return installBoxes.Where(b => b.Checked && b.Enabled).Select(b => (DiscordInstall)b.Tag).ToList();
        }

        void UpdateButtons()
        {
            List<DiscordInstall> selected = Selected();
            installButton.Enabled = selected.Count > 0;
            installButton.Text = selected.Count > 0 && selected.All(i => i.State == ModState.Nessacord) ? "Update / Repair" : "Install";
            uninstallButton.Visible = installs.Any(i => i.State == ModState.Nessacord);
            uninstallButton.Enabled = selected.Any(i => i.State == ModState.Nessacord);
        }

        void Log(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), line);
                return;
            }
            logBox.AppendText(line + Environment.NewLine);
        }

        void Run(bool install)
        {
            List<DiscordInstall> selected = Selected();
            if (!install) selected = selected.Where(i => i.State == ModState.Nessacord).ToList();

            List<DiscordInstall> running = selected.Where(Installer.IsRunning).ToList();
            if (running.Count > 0)
            {
                string names = string.Join(" and ", running.Select(i => i.Name));
                if (MessageBox.Show(this, names + " will be closed and opened again.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    return;
            }

            options.TaskManagerName = taskManagerBox.Checked;
            options.KeepTaskManagerSetting = false;

            foreach (Control control in Controls) if (control is Button || control is CheckBox) control.Enabled = false;
            logBox.Clear();

            var worker = new Thread(() =>
            {
                bool ok = true;
                try
                {
                    if (install)
                    {
                        Installer.InstallFiles(options, Log);
                        foreach (DiscordInstall target in selected) Installer.Install(target, options, Log);
                        Installer.RegisterUninstaller(options.DataDir);
                    }
                    else
                    {
                        foreach (DiscordInstall target in selected) Installer.Uninstall(target, options.DataDir, Log);
                        if (installs.All(i => i.State != ModState.Nessacord)) Installer.UnregisterUninstaller();
                        Log("Your Nessacord settings are kept in " + options.DataDir);
                    }
                }
                catch (Exception err)
                {
                    ok = false;
                    Log("Error: " + err.Message);
                }

                BeginInvoke(new Action(() =>
                {
                    foreach (CheckBox box in installBoxes)
                    {
                        var target = (DiscordInstall)box.Tag;
                        box.Text = target.Name + "   ·   " + target.StateText;
                        box.Enabled = target.State != ModState.Broken;
                    }
                    taskManagerBox.Enabled = true;
                    UpdateButtons();
                    if (ok) Log(install ? "Done. Open Discord to use Nessacord." : "Done.");
                }));
            }) { IsBackground = true };
            worker.Start();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
    }
}
