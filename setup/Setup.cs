// Planora Setup: a per-user installer for Planora. Developed by Adeeb.
//
// Built by build-installer.ps1 with the C# compiler that ships with Windows (.NET Framework 4.x), so it runs
// on any Windows 10 or 11 PC without anything else installed. The published Planora program is embedded as
// a zip resource ("payload.zip"). Setup installs into %LOCALAPPDATA%\Programs\Planora (no administrator
// rights needed), adds Start menu and desktop shortcuts, opens .planora files with Planora, and registers
// an entry in Windows' Installed apps list whose Uninstall runs "Planora.exe --uninstall".
//
// The C# here is kept to C# 5, the language version of that compiler.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Planora Setup")]
[assembly: AssemblyProduct("Planora")]
[assembly: AssemblyCompany("Adeeb")]
[assembly: AssemblyCopyright("© 2026 Adeeb. Developed by Adeeb.")]
[assembly: AssemblyDescription("Installs Planora, project scheduling and control software. Developed by Adeeb.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PlanoraSetup
{
    internal static class Program
    {
        public const string Version = "1.0.0";
        public const string MarkerFile = "planora.install";

        [STAThread]
        private static int Main(string[] args)
        {
            // "/selftest <folder>": unpack into a scratch folder and make one shortcut there, without touching the
            // Start menu, desktop or registry (used to check a freshly built Setup).
            if (args.Length == 2 && args[0].Equals("/selftest", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    SetupForm.Unpack(args[1], null);
                    SetupForm.CreateShortcut(Path.Combine(args[1], "Planora (test).lnk"), Path.Combine(args[1], "Planora.exe"), args[1]);
                    return File.Exists(Path.Combine(args[1], "Planora.exe")) ? 0 : 2;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "planora-setup-selftest.txt"), ex.ToString());
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }
    }

    internal sealed class SetupForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(0x1B, 0x23, 0x33);
        private static readonly Color Indigo = Color.FromArgb(0x8F, 0x9A, 0xEA);
        private static readonly Color Accent = Color.FromArgb(0x43, 0x53, 0xD1);
        private static readonly Color TextPrimary = Color.FromArgb(0x14, 0x1A, 0x26);
        private static readonly Color TextMuted = Color.FromArgb(0x8A, 0x93, 0xA2);
        private static readonly Color Border = Color.FromArgb(0xE4, 0xE6, 0xEB);

        private readonly Panel _welcome = new Panel();
        private readonly Panel _progressPage = new Panel();
        private readonly Panel _done = new Panel();
        private readonly TextBox _folder = new TextBox();
        private readonly CheckBox _desktop = new CheckBox();
        private readonly CheckBox _associate = new CheckBox();
        private readonly CheckBox _launch = new CheckBox();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _status = new Label();
        private readonly Button _primary = new Button();
        private readonly Button _cancel = new Button();
        private readonly BackgroundWorker _worker = new BackgroundWorker();
        private bool _installed;

        public SetupForm()
        {
            Text = "Planora Setup";
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(640, 420);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
            }

            var side = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Navy };
            side.Paint += PaintSide;
            Controls.Add(side);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            footer.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.DrawLine(new Pen(Border), 0, 0, footer.Width, 0); };
            _primary.Text = "Install";
            _primary.Size = new Size(110, 32);
            _primary.Location = new Point(footer.Width - 0, 14);
            _primary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _primary.FlatStyle = FlatStyle.Flat;
            _primary.FlatAppearance.BorderSize = 0;
            _primary.BackColor = Navy;
            _primary.ForeColor = Color.White;
            _primary.Click += OnPrimary;
            _cancel.Text = "Cancel";
            _cancel.Size = new Size(96, 32);
            _cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cancel.FlatStyle = FlatStyle.Flat;
            _cancel.FlatAppearance.BorderColor = Border;
            _cancel.BackColor = Color.White;
            _cancel.Click += delegate { Close(); };
            footer.Controls.Add(_primary);
            footer.Controls.Add(_cancel);
            footer.Resize += delegate
            {
                _primary.Location = new Point(footer.ClientSize.Width - _primary.Width - 18, 14);
                _cancel.Location = new Point(_primary.Left - _cancel.Width - 8, 14);
            };
            Controls.Add(footer);

            BuildWelcome();
            BuildProgress();
            BuildDone();
            foreach (var page in new[] { _welcome, _progressPage, _done })
            {
                page.Dock = DockStyle.Fill;
                page.Padding = new Padding(28, 26, 28, 10);
                page.BackColor = Color.White;
                Controls.Add(page);
                page.BringToFront();
            }
            Show(_welcome);
            AcceptButton = _primary;

            _worker.WorkerReportsProgress = true;
            _worker.DoWork += Install;
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                _progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                if (e.UserState is string)
                    _status.Text = (string)e.UserState;
            };
            _worker.RunWorkerCompleted += Installed;
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (_worker.IsBusy)
                    e.Cancel = true;
            };
        }

        private static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Planora"); }
        }

        // ------------------------------------------------------------------ pages

        private void BuildWelcome()
        {
            var title = Heading("Install Planora " + Program.Version);
            var intro = Body("Project scheduling and control software: Gantt charts, critical path, resources, tracking, reports, and Microsoft Project files.", 46);
            var credit = new Label
            {
                Text = "Developed by Adeeb",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10f),
                ForeColor = Navy,
                BackColor = Color.FromArgb(0xEE, 0xF0, 0xFC),
                Padding = new Padding(10, 6, 10, 6),
                Location = new Point(0, 104),
            };

            var folderLabel = Body("Install to", 150);
            folderLabel.Height = 20;
            _folder.Text = DefaultFolder;
            _folder.Location = new Point(0, 172);
            _folder.Width = 300;
            _folder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var browse = new Button { Text = "Browse...", Size = new Size(90, 26), Location = new Point(308, 171), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.FlatAppearance.BorderColor = Border;
            browse.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog { Description = "Choose where to install Planora", SelectedPath = _folder.Text })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        _folder.Text = dialog.SelectedPath.EndsWith("Planora", StringComparison.OrdinalIgnoreCase) ? dialog.SelectedPath : Path.Combine(dialog.SelectedPath, "Planora");
                }
            };

            Option(_desktop, "Create a desktop shortcut", 214, true);
            Option(_associate, "Open .planora files with Planora", 240, true);
            Option(_launch, "Start Planora when Setup finishes", 266, true);

            var note = Body("No administrator rights are needed. Planora is installed for your Windows account only.", 302);
            note.ForeColor = TextMuted;
            note.Height = 36;
            _welcome.Controls.AddRange(new Control[] { title, intro, credit, folderLabel, _folder, browse, _desktop, _associate, _launch, note });
        }

        private void BuildProgress()
        {
            var title = Heading("Installing Planora");
            _status.Text = "Preparing...";
            _status.AutoSize = false;
            _status.Location = new Point(0, 56);
            _status.Size = new Size(380, 22);
            _status.ForeColor = TextMuted;
            _progress.Location = new Point(0, 84);
            _progress.Size = new Size(390, 8);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _progressPage.Controls.AddRange(new Control[] { title, _status, _progress });
        }

        private void BuildDone()
        {
            var title = Heading("Planora is installed");
            var body = Body("You can open Planora from the Start menu" + " or from its desktop shortcut. To remove it later, use Settings > Apps > Installed apps.", 50);
            body.Height = 48;
            var credit = Body("Thank you for using Planora. Developed by Adeeb.", 116);
            credit.ForeColor = TextMuted;
            _done.Controls.AddRange(new Control[] { title, body, credit });
        }

        private static Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), ForeColor = TextPrimary, Location = new Point(0, 0) };
        }

        private static Label Body(string text, int top)
        {
            return new Label { Text = text, AutoSize = false, Size = new Size(400, 44), Location = new Point(0, top), ForeColor = Color.FromArgb(0x54, 0x5D, 0x6C), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        }

        private void Option(CheckBox box, string text, int top, bool isChecked)
        {
            box.Text = text;
            box.Checked = isChecked;
            box.AutoSize = true;
            box.Location = new Point(0, top);
            box.ForeColor = TextPrimary;
        }

        private void Show(Panel page)
        {
            _welcome.Visible = page == _welcome;
            _progressPage.Visible = page == _progressPage;
            _done.Visible = page == _done;
        }

        private static void PaintSide(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // The Planora mark: three staggered schedule bars.
            Bar(g, Brushes.White, 28, 40, 34, 10);
            using (var indigo = new SolidBrush(Indigo))
                Bar(g, indigo, 40, 56, 44, 10);
            using (var soft = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                Bar(g, soft, 54, 72, 28, 10);
            using (var title = new Font("Segoe UI Semibold", 15f))
                g.DrawString("PLANORA", title, Brushes.White, 24, 100);
            using (var small = new Font("Segoe UI", 9f))
            using (var muted = new SolidBrush(Color.FromArgb(0xB3, 0xBA, 0xC6)))
            {
                g.DrawString("Project Management", small, muted, 26, 130);
                g.DrawString("Version " + Program.Version, small, muted, 26, 360);
                g.DrawString("Developed by Adeeb", small, muted, 26, 380);
            }
        }

        private static void Bar(Graphics g, Brush brush, float x, float y, float width, float height)
        {
            using (var path = new GraphicsPath())
            {
                float d = height;
                path.AddArc(x, y, d, d, 90, 180);
                path.AddArc(x + width - d, y, d, d, 270, 180);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        // ------------------------------------------------------------------ install

        private sealed class Choices
        {
            public string Folder;
            public bool Desktop;
            public bool Associate;
        }

        private void OnPrimary(object sender, EventArgs e)
        {
            if (_installed)
            {
                if (_launch.Checked)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Path.Combine(_folder.Text, "Planora.exe")) { WorkingDirectory = _folder.Text });
                    }
                    catch (Win32Exception)
                    {
                    }
                }
                Close();
                return;
            }

            string folder = _folder.Text.Trim();
            if (folder.Length == 0 || !Path.IsPathRooted(folder))
            {
                MessageBox.Show(this, "Choose a full folder path to install Planora to.", "Planora Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !File.Exists(Path.Combine(folder, Program.MarkerFile)))
            {
                MessageBox.Show(this, "That folder already contains other files. Choose an empty folder, or the folder of an earlier Planora installation.",
                    "Planora Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Process.GetProcessesByName("Planora").Any(p => IsIn(p, folder)))
            {
                MessageBox.Show(this, "Planora is running. Close it, then choose Install again.", "Planora Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Show(_progressPage);
            _primary.Enabled = false;
            _cancel.Enabled = false;
            _worker.RunWorkerAsync(new Choices { Folder = folder, Desktop = _desktop.Checked, Associate = _associate.Checked });
        }

        private static bool IsIn(Process process, string folder)
        {
            try
            {
                return process.MainModule.FileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void Install(object sender, DoWorkEventArgs e)
        {
            var choices = (Choices)e.Argument;
            string folder = choices.Folder;

            _worker.ReportProgress(2, "Removing the previous version...");
            if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, Program.MarkerFile)))
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            Unpack(folder, _worker);
            File.WriteAllText(Path.Combine(folder, Program.MarkerFile), "Installed by Planora Setup " + Program.Version + ". Developed by Adeeb.");

            string exe = Path.Combine(folder, "Planora.exe");
            _worker.ReportProgress(92, "Creating shortcuts...");
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Planora.lnk"), exe, folder);
            if (choices.Desktop)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Planora.lnk"), exe, folder);

            _worker.ReportProgress(96, "Registering Planora with Windows...");
            if (choices.Associate)
            {
                using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.planora"))
                    ext.SetValue("", "Planora.Project");
                using (var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Planora.Project"))
                {
                    type.SetValue("", "Planora Project");
                    using (var icon = type.CreateSubKey("DefaultIcon"))
                        icon.SetValue("", "\"" + exe + "\",0");
                    using (var command = type.CreateSubKey(@"shell\open\command"))
                        command.SetValue("", "\"" + exe + "\" \"%1\"");
                }
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Planora"))
            {
                key.SetValue("DisplayName", "Planora");
                key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("Publisher", "Adeeb");
                key.SetValue("Comments", "Project scheduling and control software. Developed by Adeeb.");
                key.SetValue("DisplayIcon", exe);
                key.SetValue("InstallLocation", folder);
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(FolderSize(folder) / 1024), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Today.ToString("yyyyMMdd"));
            }
            _worker.ReportProgress(100, "Done");
        }

        /// <summary>Extracts the embedded Planora program into a folder, reporting progress to the worker if given.</summary>
        internal static void Unpack(string folder, BackgroundWorker worker)
        {
            Directory.CreateDirectory(folder);
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (payload == null)
                    throw new InvalidOperationException("This Setup file is damaged: the Planora program is missing from it.");
                using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    int total = zip.Entries.Count, done = 0;
                    string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(target);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            entry.ExtractToFile(target, true);
                        }
                        done++;
                        if (worker != null && (done % 20 == 0 || done == total))
                            worker.ReportProgress(5 + 85 * done / Math.Max(1, total), "Copying files... " + done + " of " + total);
                    }
                }
            }
        }

        private void Installed(object sender, RunWorkerCompletedEventArgs e)
        {
            _cancel.Enabled = true;
            _primary.Enabled = true;
            if (e.Error != null)
            {
                MessageBox.Show(this, "Planora could not be installed: " + e.Error.Message, "Planora Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Show(_welcome);
                return;
            }
            _installed = true;
            _primary.Text = "Finish";
            _cancel.Visible = false;
            Show(_done);
        }

        private static long FolderSize(string folder)
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        /// <summary>A .lnk shortcut through Windows Script Host (present on every Windows PC).</summary>
        internal static void CreateShortcut(string path, string target, string workingFolder)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workingFolder });
                linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Planora: project scheduling and control. Developed by Adeeb." });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.FinalReleaseComObject(link);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }
}
