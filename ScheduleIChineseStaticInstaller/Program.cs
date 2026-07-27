using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScheduleIChineseStaticInstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--install", StringComparison.OrdinalIgnoreCase))
        {
            try { InstallerService.Install(args[1]); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Length >= 2 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            try { InstallerService.Uninstall(args[1]); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
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
        Text = "Schedule I 简体中文静态版安装器 v1.4.18";
        ClientSize = new Size(760, 420);
        MinimumSize = MaximumSize = Size;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(245, 247, 250);

        var title = new Label
        {
            Text = "Schedule I 简体中文静态资源版",
            Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 41, 55),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        var version = new Label
        {
            Text = "v1.4.18 · 无 BepInEx 运行时 · 完全离线 · 保留人名和自定义名称",
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
        _path.SetBounds(32, 145, 590, 32);
        _path.Text = InstallerService.DetectGameDirectory() ?? "";

        var browse = new Button { Text = "浏览…", Location = new Point(634, 143), Size = new Size(92, 34) };
        browse.Click += (_, _) => Browse();

        _install.Text = "安装 / 更新静态汉化";
        _install.SetBounds(32, 205, 230, 48);
        _install.BackColor = Color.FromArgb(34, 197, 94);
        _install.ForeColor = Color.White;
        _install.FlatStyle = FlatStyle.Flat;
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += (_, _) => Install();

        _uninstall.Text = "恢复原版";
        _uninstall.SetBounds(276, 205, 145, 48);
        _uninstall.FlatStyle = FlatStyle.Flat;
        _uninstall.Click += (_, _) => Uninstall();

        var note = new Label
        {
            Text = "适配 v0.4.5f2 / Build 22829923。安装器会先校验并备份 8 个资源文件，停用 winhttp/BepInEx；不修改存档。",
            ForeColor = Color.FromArgb(75, 85, 99),
            AutoSize = false,
            Location = new Point(32, 276),
            Size = new Size(690, 50)
        };
        _status.Text = "请选择游戏目录后安装。游戏更新后请先恢复原版或让 Steam 验证文件。";
        _status.ForeColor = Color.FromArgb(37, 99, 235);
        _status.AutoEllipsis = true;
        _status.SetBounds(32, 340, 690, 48);

        Controls.AddRange(new Control[] { title, version, pathLabel, _path, browse, _install, _uninstall, note, _status });
    }

    private void Browse()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "请选择 Schedule I 游戏根目录",
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(_path.Text)) picker.InitialDirectory = _path.Text;
        if (picker.ShowDialog(this) == DialogResult.OK) _path.Text = picker.SelectedPath;
    }

    private void Install()
    {
        SetBusy(true, "正在校验、备份并安装静态资源……");
        try
        {
            var result = InstallerService.Install(_path.Text);
            _status.Text = $"安装成功：写入 {result.WrittenFiles} 个资源；BepInEx 已停用。";
            _status.ForeColor = Color.FromArgb(22, 101, 52);
            MessageBox.Show(this,
                "静态汉化安装成功。请直接通过 Steam 启动游戏并手动检查联系人、消息、效果词和存档加载。",
                "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败：" + ex.Message;
            _status.ForeColor = Color.FromArgb(185, 28, 28);
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false, _status.Text); }
    }

    private void Uninstall()
    {
        if (MessageBox.Show(this,
            "将从安装器备份恢复 8 个原版资源，并恢复安装前的 winhttp/BepInEx 状态。不会修改存档。继续吗？",
            "确认恢复原版", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        SetBusy(true, "正在恢复原版资源……");
        try
        {
            InstallerService.Uninstall(_path.Text);
            _status.Text = "恢复完成：原版资源和安装前的加载器状态已恢复。";
            _status.ForeColor = Color.FromArgb(22, 101, 52);
        }
        catch (Exception ex)
        {
            _status.Text = "恢复失败：" + ex.Message;
            _status.ForeColor = Color.FromArgb(185, 28, 28);
            MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

internal sealed record InstallResult(int WrittenFiles, bool LoaderDisabled);

internal sealed class PayloadManifest
{
    [JsonPropertyName("game_build")] public string GameBuild { get; set; } = "";
    [JsonPropertyName("edition")] public string Edition { get; set; } = "";
    [JsonPropertyName("files")] public Dictionary<string, FileRecord> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class FileRecord
{
    [JsonPropertyName("original_sha256")] public string OriginalSha256 { get; set; } = "";
    [JsonPropertyName("patched_sha256")] public string PatchedSha256 { get; set; } = "";
}

internal sealed class InstallState
{
    public string Edition { get; set; } = "";
    public bool LoaderWasPresent { get; set; }
    public string? LoaderSha256 { get; set; }
    public DateTime InstalledUtc { get; set; }
}

internal static class InstallerService
{
    private const string PayloadResource = "ScheduleIChineseStatic.Payload.zip";
    private const string BackupFolderName = "ScheduleIChinese_Static_Backup";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string? DetectGameDirectory()
    {
        foreach (var steam in SteamRoots())
        foreach (var library in SteamLibraries(steam))
        {
            var game = Path.Combine(library, "steamapps", "common", "Schedule I");
            if (File.Exists(Path.Combine(game, "Schedule I.exe"))) return game;
        }
        return null;
    }

    public static InstallResult Install(string root)
    {
        root = ValidateRoot(root);
        EnsureGameClosed();
        using var payload = OpenPayload();
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        var manifest = ReadManifest(zip);
        if (manifest.GameBuild != "22829923")
            throw new InvalidDataException("安装包版本标识错误。");

        var backupRoot = Path.Combine(root, BackupFolderName, "Build22829923");
        var backupData = Path.Combine(backupRoot, "Schedule I_Data");
        Directory.CreateDirectory(backupData);

        var originals = new List<(string Name, string Destination, string Backup, FileRecord Record)>();
        foreach (var item in manifest.Files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var destination = SafePath(root, Path.Combine("Schedule I_Data", item.Key));
            if (!File.Exists(destination)) throw new FileNotFoundException("缺少游戏资源：" + item.Key);
            var current = Sha256(destination);
            var backup = Path.Combine(backupData, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (current.Equals(item.Value.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(backup)) File.Copy(destination, backup);
                if (!Sha256(backup).Equals(item.Value.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("原版备份校验失败：" + item.Key);
            }
            else if (current.Equals(item.Value.PatchedSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(backup) ||
                    !Sha256(backup).Equals(item.Value.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("资源已汉化，但缺少可验证的原版备份：" + item.Key);
            }
            else
            {
                throw new InvalidOperationException(
                    $"{item.Key} 与支持的 Build 22829923 不匹配。请先在 Steam 验证游戏文件后重试。");
            }
            originals.Add((item.Key, destination, backup, item.Value));
        }

        int written = 0;
        try
        {
            foreach (var item in originals)
            {
                if (Sha256(item.Destination).Equals(item.Record.PatchedSha256, StringComparison.OrdinalIgnoreCase))
                    continue;
                var entry = zip.GetEntry("Schedule I_Data/" + item.Name)
                    ?? throw new InvalidDataException("安装包缺少资源：" + item.Name);
                WriteEntryVerified(entry, item.Destination, item.Record.PatchedSha256);
                written++;
            }
            var loaderDisabled = DisableLoader(root, backupRoot, manifest.Edition);
            return new InstallResult(written, loaderDisabled);
        }
        catch
        {
            foreach (var item in originals)
                if (File.Exists(item.Backup) &&
                    Sha256(item.Backup).Equals(item.Record.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    File.Copy(item.Backup, item.Destination, true);
            var loader = Path.Combine(root, "winhttp.dll");
            var loaderBackup = Path.Combine(backupRoot, "Loader", "winhttp.dll");
            if (!File.Exists(loader) && File.Exists(loaderBackup))
                File.Copy(loaderBackup, loader);
            throw;
        }
    }

    public static void Uninstall(string root)
    {
        root = ValidateRoot(root);
        EnsureGameClosed();
        using var payload = OpenPayload();
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        var manifest = ReadManifest(zip);
        var backupRoot = Path.Combine(root, BackupFolderName, "Build22829923");
        var backupData = Path.Combine(backupRoot, "Schedule I_Data");

        foreach (var item in manifest.Files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var destination = SafePath(root, Path.Combine("Schedule I_Data", item.Key));
            var backup = Path.Combine(backupData, item.Key);
            if (!File.Exists(backup) ||
                !Sha256(backup).Equals(item.Value.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("缺少有效原版备份：" + item.Key);
            var current = File.Exists(destination) ? Sha256(destination) : "";
            if (!current.Equals(item.Value.PatchedSha256, StringComparison.OrdinalIgnoreCase) &&
                !current.Equals(item.Value.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("资源已被其他更新修改，拒绝覆盖：" + item.Key);
            File.Copy(backup, destination, true);
            if (!Sha256(destination).Equals(item.Value.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("恢复后校验失败：" + item.Key);
        }
        RestoreLoader(root, backupRoot);
    }

    private static bool DisableLoader(string root, string backupRoot, string edition)
    {
        var loader = Path.Combine(root, "winhttp.dll");
        var loaderBackup = Path.Combine(backupRoot, "Loader", "winhttp.dll");
        var statePath = Path.Combine(backupRoot, "install-state.json");
        bool wasPresent = File.Exists(loader);
        string? loaderHash = wasPresent ? Sha256(loader) : null;
        if (wasPresent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(loaderBackup)!);
            if (File.Exists(loaderBackup) && !Sha256(loaderBackup).Equals(loaderHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("已有不同的 winhttp.dll 备份，拒绝覆盖。");
            if (!File.Exists(loaderBackup)) File.Copy(loader, loaderBackup);
            File.Delete(loader);
        }
        var state = new InstallState
        {
            Edition = edition,
            LoaderWasPresent = wasPresent || File.Exists(loaderBackup),
            LoaderSha256 = loaderHash ?? (File.Exists(loaderBackup) ? Sha256(loaderBackup) : null),
            InstalledUtc = DateTime.UtcNow
        };
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOptions));
        return !File.Exists(loader);
    }

    private static void RestoreLoader(string root, string backupRoot)
    {
        var statePath = Path.Combine(backupRoot, "install-state.json");
        if (!File.Exists(statePath)) return;
        var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(statePath), JsonOptions)
            ?? throw new InvalidDataException("安装状态文件损坏。");
        if (!state.LoaderWasPresent) return;
        var loader = Path.Combine(root, "winhttp.dll");
        var backup = Path.Combine(backupRoot, "Loader", "winhttp.dll");
        if (!File.Exists(backup)) throw new FileNotFoundException("缺少 winhttp.dll 备份。");
        if (File.Exists(loader) && !Sha256(loader).Equals(state.LoaderSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("游戏目录已有不同的 winhttp.dll，拒绝覆盖。");
        File.Copy(backup, loader, true);
        if (state.LoaderSha256 is not null &&
            !Sha256(loader).Equals(state.LoaderSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("winhttp.dll 恢复校验失败。");
    }

    private static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
        ?? throw new InvalidOperationException("安装包内的静态资源缺失。");

    private static PayloadManifest ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("安装包缺少 manifest.json。");
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<PayloadManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException("manifest.json 无法读取。");
    }

    private static void WriteEntryVerified(ZipArchiveEntry entry, string destination, string expectedHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".scheduleichinese-installing";
        try
        {
            using (var input = entry.Open())
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            if (!Sha256(temporary).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包内资源校验失败：" + entry.FullName);
            File.Copy(temporary, destination, true);
            if (!Sha256(destination).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("写入后校验失败：" + entry.FullName);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Sha256(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void EnsureGameClosed()
    {
        if (Process.GetProcessesByName("Schedule I").Length > 0)
            throw new InvalidOperationException("请先退出 Schedule I 游戏，再安装或恢复。");
    }

    private static string SafePath(string root, string relative)
    {
        var destination = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包中发现不安全的路径。");
        return destination;
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
}
