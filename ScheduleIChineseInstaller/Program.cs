using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScheduleIChineseInstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--install", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                InstallerService.Install(args[1]);
                return 0;
            }
            catch { return 1; }
        }

        if (args.Length >= 3 &&
            args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase) &&
            args[2].Equals("--yes", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                InstallerService.Uninstall(args[1]);
                return 0;
            }
            catch { return 1; }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
        return 0;
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox _path = new();
    private readonly Label _status = new();
    private readonly Button _install = new();
    private readonly Button _uninstall = new();

    public InstallerForm()
    {
        Text = "Schedule I 简体中文安全汉化一键安装器 v1.3.36";
        ClientSize = new Size(720, 390);
        MinimumSize = MaximumSize = Size;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(245, 247, 250);

        var title = new Label
        {
            Text = "Schedule I 简体中文汉化",
            Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 41, 55),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        var version = new Label
        {
            Text = "v1.3.36 · 完全离线 · 人名、品种和自定义名称保留",
            ForeColor = Color.FromArgb(75, 85, 99),
            AutoSize = true,
            Location = new Point(32, 72)
        };
        var pathLabel = new Label
        {
            Text = "游戏根目录（应包含 Schedule I.exe）",
            AutoSize = true,
            Location = new Point(32, 118)
        };
        _path.SetBounds(32, 145, 550, 32);
        _path.Text = InstallerService.DetectGameDirectory() ?? "";

        var browse = new Button
        {
            Text = "浏览…",
            Location = new Point(594, 143),
            Size = new Size(92, 34)
        };
        browse.Click += (_, _) => Browse();

        _install.Text = "安装 / 更新汉化";
        _install.SetBounds(32, 205, 205, 48);
        _install.BackColor = Color.FromArgb(34, 197, 94);
        _install.ForeColor = Color.White;
        _install.FlatStyle = FlatStyle.Flat;
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += (_, _) => Install();

        _uninstall.Text = "卸载汉化";
        _uninstall.SetBounds(251, 205, 145, 48);
        _uninstall.FlatStyle = FlatStyle.Flat;
        _uninstall.Click += (_, _) => Uninstall();

        var note = new Label
        {
            Text = "适配 v0.4.5f2 / Build 22829923；不修改游戏资源、metadata 或存档。",
            ForeColor = Color.FromArgb(75, 85, 99),
            AutoSize = true,
            Location = new Point(32, 278)
        };
        _status.Text = "已自动检测游戏目录。点击“安装 / 更新汉化”即可。";
        _status.ForeColor = Color.FromArgb(37, 99, 235);
        _status.AutoEllipsis = true;
        _status.SetBounds(32, 320, 650, 42);

        Controls.AddRange(new Control[]
        {
            title, version, pathLabel, _path, browse, _install, _uninstall, note, _status
        });
    }

    private void Browse()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "请选择 Schedule I 游戏根目录",
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(_path.Text))
            picker.InitialDirectory = _path.Text;
        if (picker.ShowDialog(this) == DialogResult.OK)
            _path.Text = picker.SelectedPath;
    }

    private void Install()
    {
        SetBusy(true, "正在安装并校验文件……");
        try
        {
            int count = InstallerService.Install(_path.Text);
            _status.Text = $"安装成功：已写入并校验 {count} 个文件。动态文本仅在离线显示层补译。";
            _status.ForeColor = Color.FromArgb(22, 101, 52);
            MessageBox.Show(this, "汉化安装成功！请通过 Steam 启动游戏。", "安装完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败：" + ex.Message;
            _status.ForeColor = Color.FromArgb(185, 28, 28);
            MessageBox.Show(this, ex.Message, "安装失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false, _status.Text); }
    }

    private void Uninstall()
    {
        if (MessageBox.Show(this,
            "将删除汉化插件和配置。不会删除其他模组、BepInEx 或存档。继续吗？",
            "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusy(true, "正在卸载……");
        try
        {
            InstallerService.Uninstall(_path.Text);
            _status.Text = "卸载完成：汉化插件和配置已删除。";
            _status.ForeColor = Color.FromArgb(22, 101, 52);
        }
        catch (Exception ex)
        {
            _status.Text = "卸载失败：" + ex.Message;
            _status.ForeColor = Color.FromArgb(185, 28, 28);
        }
        finally { SetBusy(false, _status.Text); }
    }

    private void SetBusy(bool busy, string text)
    {
        _install.Enabled = _uninstall.Enabled = !busy;
        _status.Text = text;
        _status.Refresh();
    }
}

internal static class InstallerService
{
    private const string PayloadResource = "ScheduleIChinese.Payload.zip";

    public static string? DetectGameDirectory()
    {
        foreach (var steam in SteamRoots())
        {
            foreach (var library in SteamLibraries(steam))
            {
                var game = Path.Combine(library, "steamapps", "common", "Schedule I");
                if (File.Exists(Path.Combine(game, "Schedule I.exe")))
                    return game;
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string?[] candidates =
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")
        };
        foreach (var path in candidates)
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && seen.Add(path))
                yield return path;
    }

    private static IEnumerable<string> SteamLibraries(string steamRoot)
    {
        yield return steamRoot;
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); }
        catch { yield break; }
        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path)) yield return path;
        }
    }

    public static int Install(string root)
    {
        root = ValidateRoot(root);
        EnsureGameClosed();
        using var payload = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("安装包内的汉化数据缺失。");
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);

        int written = 0;
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destination = SafePath(root, relative);

            // The package includes BepInEx for a clean one-click install. Preserve
            // an existing loader installation and only replace our own plugin files.
            // The user's config is never overwritten; BepInEx adds new keys itself.
            bool isConfig =
                relative.Equals(
                    Path.Combine("BepInEx", "config", "com.schedulei.chinesemod.cfg"),
                    StringComparison.OrdinalIgnoreCase);
            if (isConfig && File.Exists(destination))
                continue;
            bool isChineseFile =
                isConfig ||
                relative.StartsWith(
                    Path.Combine("BepInEx", "plugins", "ScheduleIChinese") +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            if (!isChineseFile && File.Exists(destination))
                continue;

            WriteEntryVerified(entry, destination);
            written++;
        }
        return written;
    }

    public static void Uninstall(string root)
    {
        root = ValidateRoot(root);
        EnsureGameClosed();
        var plugin = Path.Combine(root, "BepInEx", "plugins", "ScheduleIChinese");
        var config = Path.Combine(root, "BepInEx", "config", "com.schedulei.chinesemod.cfg");
        if (Directory.Exists(plugin)) Directory.Delete(plugin, true);
        if (File.Exists(config)) File.Delete(config);
    }

    private static void EnsureGameClosed()
    {
        if (System.Diagnostics.Process.GetProcessesByName("Schedule I").Length > 0)
            throw new InvalidOperationException("请先退出 Schedule I 游戏，再安装或卸载汉化。");
    }

    private static string SafePath(string root, string relative)
    {
        var destination = Path.GetFullPath(Path.Combine(root, relative));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包中发现不安全的文件路径。");
        return destination;
    }

    private static void WriteEntryVerified(ZipArchiveEntry entry, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".installing";
        try
        {
            using (var input = entry.Open())
            using (var output = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            File.Copy(temporary, destination, true);
            if (new FileInfo(destination).Length != entry.Length)
                throw new IOException("文件长度校验失败：" + entry.FullName);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new DirectoryNotFoundException("未找到游戏目录，请手动选择。");
        root = Path.GetFullPath(root.Trim().Trim('"'));
        if (!File.Exists(Path.Combine(root, "Schedule I.exe")))
            throw new FileNotFoundException("所选目录中没有 Schedule I.exe。");
        return root;
    }
}
