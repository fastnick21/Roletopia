using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Roletopia.Installer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }

    internal sealed class LauncherForm : Form
    {
        private const string SteamAppId = "945360";
        private readonly LauncherService _launcher = new LauncherService();
        private readonly Label _status = new Label();
        private readonly Label _path = new Label();
        private readonly Label _version = new Label();
        private readonly Button _play = new Button();
        private readonly Button _install = new Button();
        private readonly Button _toggle = new Button();
        private readonly Button _repair = new Button();
        private readonly Button _browse = new Button();
        private readonly ProgressBar _progress = new ProgressBar();
        private string _gamePath;

        public LauncherForm()
        {
            Text = "Roletopia Launcher";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 500);
            MinimumSize = new Size(760, 460);
            BackColor = Color.FromArgb(14, 18, 31);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10F);
            Icon = SystemIcons.Application;

            BuildInterface();
            Shown += async (_, __) => await RefreshStateAsync();
        }

        private void BuildInterface()
        {
            var title = new Label
            {
                Text = "ROLETOPIA",
                Font = new Font("Segoe UI", 28F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(42, 35)
            };

            var subtitle = new Label
            {
                Text = "Among Us role expansion",
                ForeColor = Color.FromArgb(170, 180, 205),
                AutoSize = true,
                Location = new Point(47, 91)
            };

            _version.Text = "Launcher 1.0.0";
            _version.ForeColor = Color.FromArgb(125, 138, 170);
            _version.AutoSize = true;
            _version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _version.Location = new Point(680, 48);

            var card = new Panel
            {
                BackColor = Color.FromArgb(25, 31, 50),
                Location = new Point(42, 140),
                Size = new Size(736, 270),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _status.Text = "Checking your Steam installation...";
            _status.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            _status.AutoSize = true;
            _status.Location = new Point(28, 25);

            _path.Text = "Among Us path not detected yet";
            _path.ForeColor = Color.FromArgb(165, 175, 198);
            _path.AutoEllipsis = true;
            _path.Location = new Point(31, 66);
            _path.Size = new Size(650, 25);
            _path.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _progress.Location = new Point(32, 105);
            _progress.Size = new Size(670, 8);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 22;
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            ConfigureButton(_play, "PLAY ROLETOPIA", new Point(32, 143), new Size(280, 64), true);
            ConfigureButton(_install, "INSTALL / UPDATE", new Point(330, 143), new Size(170, 64), false);
            ConfigureButton(_toggle, "DISABLE MOD", new Point(518, 143), new Size(184, 64), false);
            ConfigureButton(_repair, "Repair", new Point(32, 220), new Size(130, 34), false);
            ConfigureButton(_browse, "Choose game folder", new Point(175, 220), new Size(180, 34), false);

            _play.Click += async (_, __) => await RunOperationAsync(async () => await _launcher.PlayAsync(_gamePath));
            _install.Click += async (_, __) => await RunOperationAsync(async () => await _launcher.InstallOrUpdateAsync(_gamePath));
            _toggle.Click += async (_, __) => await RunOperationAsync(async () => await _launcher.ToggleAsync(_gamePath));
            _repair.Click += async (_, __) => await RunOperationAsync(async () => await _launcher.RepairAsync(_gamePath));
            _browse.Click += async (_, __) => await ChooseFolderAsync();

            card.Controls.AddRange(new Control[] { _status, _path, _progress, _play, _install, _toggle, _repair, _browse });

            var note = new Label
            {
                Text = "Launches the Steam edition of Among Us. Roletopia can be disabled without uninstalling the game.",
                ForeColor = Color.FromArgb(130, 142, 170),
                AutoSize = true,
                Location = new Point(47, 438)
            };

            Controls.AddRange(new Control[] { title, subtitle, _version, card, note });
        }

        private static void ConfigureButton(Button button, string text, Point location, Size size, bool primary)
        {
            button.Text = text;
            button.Location = location;
            button.Size = size;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(79, 92, 125);
            button.BackColor = primary ? Color.FromArgb(113, 70, 255) : Color.FromArgb(35, 43, 67);
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", primary ? 12F : 9.5F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
        }

        private async Task RefreshStateAsync()
        {
            SetBusy(true, "Checking your Steam installation...");
            try
            {
                _gamePath = await Task.Run(() => _launcher.FindAmongUs());
                UpdateState();
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void UpdateState()
        {
            var state = _launcher.GetState(_gamePath);
            _path.Text = string.IsNullOrWhiteSpace(_gamePath) ? "Among Us was not found automatically." : _gamePath;
            _status.Text = state.Message;
            _status.ForeColor = state.Ready ? Color.FromArgb(103, 232, 164) : Color.FromArgb(255, 197, 92);
            _play.Enabled = state.Ready;
            _install.Enabled = !string.IsNullOrWhiteSpace(_gamePath);
            _repair.Enabled = state.Installed;
            _toggle.Enabled = state.Installed;
            _toggle.Text = state.Enabled ? "DISABLE MOD" : "ENABLE MOD";
        }

        private async Task RunOperationAsync(Func<Task> operation)
        {
            if (string.IsNullOrWhiteSpace(_gamePath))
            {
                await ChooseFolderAsync();
                if (string.IsNullOrWhiteSpace(_gamePath)) return;
            }

            SetBusy(true, "Working...");
            try
            {
                await operation();
                UpdateState();
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Windows blocked access to the Among Us folder. Restart the launcher as administrator and try again.", "Permission required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task ChooseFolderAsync()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the folder containing Among Us.exe", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!_launcher.IsAmongUsFolder(dialog.SelectedPath))
                {
                    MessageBox.Show("That folder does not contain Among Us.exe.", "Invalid folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _gamePath = dialog.SelectedPath;
                _launcher.SaveGamePath(_gamePath);
                await Task.Yield();
                UpdateState();
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _progress.Visible = busy;
            _play.Enabled = !busy;
            _install.Enabled = !busy;
            _toggle.Enabled = !busy;
            _repair.Enabled = !busy;
            _browse.Enabled = !busy;
            if (!string.IsNullOrWhiteSpace(message)) _status.Text = message;
        }

        private void ShowFailure(Exception ex)
        {
            _status.Text = "An operation failed";
            _status.ForeColor = Color.FromArgb(255, 115, 115);
            MessageBox.Show(ex.Message, "Roletopia Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal sealed class LauncherState
    {
        public bool Installed { get; set; }
        public bool Enabled { get; set; }
        public bool Ready { get; set; }
        public string Message { get; set; }
    }

    internal sealed class LauncherService
    {
        private const string AppId = "945360";
        private const string ConfigKey = @"Software\Roletopia";
        private const string PluginFolder = @"BepInEx\plugins\Roletopia";
        private const string DisabledPluginFolder = @"BepInEx\plugins\Roletopia.disabled";

        public string FindAmongUs()
        {
            var saved = ReadSavedGamePath();
            if (IsAmongUsFolder(saved)) return saved;

            foreach (var library in FindSteamLibraries())
            {
                var path = Path.Combine(library, "steamapps", "common", "Among Us");
                if (IsAmongUsFolder(path))
                {
                    SaveGamePath(path);
                    return path;
                }
            }

            return null;
        }

        public LauncherState GetState(string gamePath)
        {
            if (!IsAmongUsFolder(gamePath))
                return new LauncherState { Message = "Among Us not found", Ready = false };

            var enabledPath = Path.Combine(gamePath, PluginFolder);
            var disabledPath = Path.Combine(gamePath, DisabledPluginFolder);
            var installed = Directory.Exists(enabledPath) || Directory.Exists(disabledPath);
            var enabled = Directory.Exists(enabledPath);
            var loaderInstalled = File.Exists(Path.Combine(gamePath, "winhttp.dll")) && Directory.Exists(Path.Combine(gamePath, "BepInEx"));

            if (!installed)
                return new LauncherState { Message = "Ready to install Roletopia", Installed = false, Enabled = false, Ready = false };
            if (!loaderInstalled)
                return new LauncherState { Message = "Installation needs repair", Installed = true, Enabled = enabled, Ready = false };
            if (!enabled)
                return new LauncherState { Message = "Roletopia is disabled", Installed = true, Enabled = false, Ready = false };

            return new LauncherState { Message = "Ready to play", Installed = true, Enabled = true, Ready = true };
        }

        public async Task InstallOrUpdateAsync(string gamePath)
        {
            ValidateGamePath(gamePath);
            await Task.Run(() =>
            {
                var payload = GetPayloadPath();
                if (!Directory.Exists(payload))
                    throw new DirectoryNotFoundException("The launcher payload is missing. Re-download the complete Roletopia launcher package.");

                CopyDirectory(payload, gamePath, true);
                var disabled = Path.Combine(gamePath, DisabledPluginFolder);
                var enabled = Path.Combine(gamePath, PluginFolder);
                if (Directory.Exists(disabled) && !Directory.Exists(enabled)) Directory.Move(disabled, enabled);
                WriteInstallMarker(gamePath);
            });
        }

        public async Task RepairAsync(string gamePath)
        {
            ValidateGamePath(gamePath);
            await Task.Run(() =>
            {
                var payload = GetPayloadPath();
                if (!Directory.Exists(payload))
                    throw new DirectoryNotFoundException("The launcher payload is missing.");
                CopyDirectory(payload, gamePath, true);
                WriteInstallMarker(gamePath);
            });
        }

        public async Task ToggleAsync(string gamePath)
        {
            ValidateGamePath(gamePath);
            await Task.Run(() =>
            {
                var enabled = Path.Combine(gamePath, PluginFolder);
                var disabled = Path.Combine(gamePath, DisabledPluginFolder);
                if (Directory.Exists(enabled))
                {
                    if (Directory.Exists(disabled)) Directory.Delete(disabled, true);
                    Directory.Move(enabled, disabled);
                }
                else if (Directory.Exists(disabled))
                {
                    Directory.Move(disabled, enabled);
                }
                else
                {
                    throw new InvalidOperationException("Roletopia is not installed yet.");
                }
            });
        }

        public async Task PlayAsync(string gamePath)
        {
            var state = GetState(gamePath);
            if (!state.Ready) throw new InvalidOperationException(state.Message + ". Install or repair Roletopia first.");
            await Task.Run(() => Process.Start(new ProcessStartInfo("steam://run/" + AppId) { UseShellExecute = true }));
        }

        public bool IsAmongUsFolder(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && Directory.Exists(path)
                && File.Exists(Path.Combine(path, "Among Us.exe"))
                && Directory.Exists(Path.Combine(path, "Among Us_Data"));
        }

        public void SaveGamePath(string path)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(ConfigKey)) key?.SetValue("AmongUsPath", path ?? string.Empty);
        }

        private static string ReadSavedGamePath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(ConfigKey)) return key?.GetValue("AmongUsPath") as string;
        }

        private static IEnumerable<string> FindSteamLibraries()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSteamRegistryPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddSteamRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddSteamRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(pf86)) roots.Add(Path.Combine(pf86, "Steam"));

            foreach (var root in roots.Where(Directory.Exists))
            {
                yield return root;
                var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFile)) continue;
                string text;
                try { text = File.ReadAllText(libraryFile); }
                catch { continue; }

                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    var library = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(library)) yield return library;
                }
            }
        }

        private static void AddSteamRegistryPath(ISet<string> paths, RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using (var key = root.OpenSubKey(subKey))
                {
                    var value = key?.GetValue(valueName) as string;
                    if (!string.IsNullOrWhiteSpace(value)) paths.Add(value.Replace('/', '\\'));
                }
            }
            catch { }
        }

        private static string GetPayloadPath()
        {
            var payload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload");
            if (Directory.Exists(payload)) return payload;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roletopia");
        }

        private static void CopyDirectory(string source, string destination, bool overwrite)
        {
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite);
            }
        }

        private static void WriteInstallMarker(string gamePath)
        {
            var marker = Path.Combine(gamePath, "BepInEx", "plugins", "Roletopia", "launcher-install.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(marker));
            File.WriteAllText(marker, "Installed by Roletopia Launcher at " + DateTime.UtcNow.ToString("O"));
        }

        private void ValidateGamePath(string gamePath)
        {
            if (!IsAmongUsFolder(gamePath)) throw new DirectoryNotFoundException("The selected Among Us installation is invalid.");
        }
    }
}