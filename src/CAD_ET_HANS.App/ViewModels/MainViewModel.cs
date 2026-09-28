using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CAD_ET_HANS.Core.Models;
using CAD_ET_HANS.Core.Services;
using Microsoft.Win32;

namespace CAD_ET_HANS.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DictionaryService _dictionary = new();
    private readonly BackupService _backup = new();
    private readonly SettingsService _settings = new();
    private readonly LocalizationEngine _engine;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly StringBuilder _log = new();
    private readonly object _logFileLock = new();
    private readonly string _logFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CAD_ET_HANS", "logs", "app.log");

    private AcadEnvironment? _selected;
    private string _searchText = string.Empty;
    private int _statusFilter;
    private bool _isBusy;
    private double _progressValue;
    private string _progressText = "就绪";
    private string _statusMessage = "正在检测 AutoCAD 环境...";
    private bool _localizeCuix = true;
    private bool _localizeDcl = true;
    private bool _localizeLsp = true;
    private bool _utf8WithBom;
    private bool _forceLocalize = true;
    private bool _autoScanOnStartup;
    private bool _localizeSourceTemplate = true;

    public MainViewModel()
    {
        _engine = new LocalizationEngine(_dictionary);
        Entries = new ObservableCollection<TranslationEntry>();
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;

        DetectCommand = new RelayCommand(_ => Detect());
        BrowseCommand = new RelayCommand(_ => Browse());
        ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsBusy);
        ApplyCommand = new RelayCommand(async _ => await ApplyAsync(), _ => !IsBusy && IsReady);
        RestoreCommand = new RelayCommand(_ => Restore(), _ => !IsBusy);
        ExportCommand = new RelayCommand(_ => Export(), _ => !IsBusy && Entries.Count > 0);
        ImportCommand = new RelayCommand(_ => Import(), _ => !IsBusy);
        OpenBackupCommand = new RelayCommand(_ => OpenBackupFolder());

        // 载入用户偏好
        var s = _settings.Load();
        _localizeCuix = s.LocalizeCuix;
        _localizeDcl = s.LocalizeDcl;
        _localizeLsp = s.LocalizeLsp;
        _utf8WithBom = s.Utf8WithBom;
        _forceLocalize = s.ForceLocalize;
        _autoScanOnStartup = s.AutoScanOnStartup;
        _localizeSourceTemplate = s.LocalizeSourceTemplate;

        try
        {
            var dir = Path.GetDirectoryName(_logFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch { /* 日志目录不可写时忽略 */ }

        Log($"内置离线词典载入 {_dictionary.BuiltInCount} 条。");
        Log($"日志文件：{_logFile}");
        Detect();
    }

    private void SaveSettings()
        => _settings.Save(new AppSettings
        {
            AutoScanOnStartup = AutoScanOnStartup,
            LocalizeSourceTemplate = LocalizeSourceTemplate,
            LocalizeCuix = LocalizeCuix,
            LocalizeDcl = LocalizeDcl,
            LocalizeLsp = LocalizeLsp,
            Utf8WithBom = Utf8WithBom,
            ForceLocalize = ForceLocalize
        });

    // ---------------- 绑定属性 ----------------

    public ObservableCollection<AcadEnvironment> Environments { get; } = new();

    public ObservableCollection<TranslationEntry> Entries { get; }

    public ICollectionView EntriesView { get; }

    public AcadEnvironment? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(InstallPath));
            OnPropertyChanged(nameof(ExpressPath));
            OnPropertyChanged(nameof(CuixPath));
            OnPropertyChanged(nameof(EnvSummary));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string InstallPath => _selected?.InstallPath ?? "未检测到";
    public string ExpressPath => _selected?.ExpressPath ?? "-";
    public string CuixPath => _selected?.EffectiveCuixPath ?? "-";
    public string SourceCuixPath => _selected?.SourceCuixPath ?? "-";
    public string EnvSummary => _selected is null
        ? "未检测到"
        : $"{_selected.DisplayName}　|　{_selected.InstallPath}";

    public bool IsReady => _selected is not null && File.Exists(_selected.EffectiveCuixPath);

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string LogText
    {
        get => _log.ToString();
        set { _log.Clear(); _log.Append(value); OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); EntriesView.Refresh(); }
    }

    public int StatusFilter
    {
        get => _statusFilter;
        set { _statusFilter = value; OnPropertyChanged(); EntriesView.Refresh(); }
    }

    public bool LocalizeCuix
    {
        get => _localizeCuix;
        set { _localizeCuix = value; OnPropertyChanged(); SaveSettings(); }
    }

    public bool LocalizeDcl
    {
        get => _localizeDcl;
        set { _localizeDcl = value; OnPropertyChanged(); SaveSettings(); }
    }

    public bool LocalizeLsp
    {
        get => _localizeLsp;
        set { _localizeLsp = value; OnPropertyChanged(); SaveSettings(); }
    }

    public bool Utf8WithBom
    {
        get => _utf8WithBom;
        set { _utf8WithBom = value; OnPropertyChanged(); SaveSettings(); }
    }

    public bool ForceLocalize
    {
        get => _forceLocalize;
        set { _forceLocalize = value; OnPropertyChanged(); SaveSettings(); }
    }

    /// <summary>
    /// 是否同时汉化 UserDataCache 下的 CUIX 源模板。
    /// AutoCAD 启动时会用它重建用户配置里的 CUIX，不改的话菜单会变回英文。
    /// </summary>
    public bool LocalizeSourceTemplate
    {
        get => _localizeSourceTemplate;
        set { _localizeSourceTemplate = value; OnPropertyChanged(); SaveSettings(); }
    }

    /// <summary>启动/重新检测后是否自动扫描。仅读取文件，不会写入任何内容。</summary>
    public bool AutoScanOnStartup
    {
        get => _autoScanOnStartup;
        set
        {
            _autoScanOnStartup = value;
            OnPropertyChanged();
            SaveSettings();
            Log($"启动自动扫描：{(value ? "已开启" : "已关闭")}（仅读取，不写入）");
        }
    }

    public string BackupRoot => _backup.Root;
    public int DictionaryCount => _dictionary.BuiltInCount;

    public string SummaryText =>
        $"共 {Entries.Count} 条　已翻译 {Entries.Count(e => e.Status == EntryStatus.Translated)}　" +
        $"已汉化 {Entries.Count(e => e.Status == EntryStatus.Localized)}　" +
        $"未翻译 {Entries.Count(e => e.Status == EntryStatus.Untranslated)}";

    // ---------------- 命令 ----------------

    public ICommand DetectCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand OpenBackupCommand { get; }

    // ---------------- 行为 ----------------

    public void Detect()
    {
        Environments.Clear();
        foreach (var env in AcadLocator.DetectAll())
        {
            Environments.Add(env);
        }

        if (Environments.Count == 0)
        {
            StatusMessage = "未检测到 AutoCAD，请手动选择安装目录。";
            Log("未检测到已安装且带 Express Tools 的 AutoCAD。");
            return;
        }

        var preferred = Environments.FirstOrDefault(e => e.Version == "2026") ?? Environments[0];
        Selected = preferred;
        StatusMessage = $"已检测到 {Environments.Count} 个版本，当前选择 {preferred.DisplayName}。";
        Log($"检测到 {Environments.Count} 个版本：{string.Join("、", Environments.Select(e => e.Version))}");
        Log($"Express 目录：{preferred.ExpressPath}（存在={Directory.Exists(preferred.ExpressPath)}）");
        Log($"生效 CUIX：{preferred.EffectiveCuixPath}（存在={File.Exists(preferred.EffectiveCuixPath)}）");

        if (AutoScanOnStartup)
        {
            // 仅读取目标文件并填入译文，不写入任何内容
            _ = ScanAsync();
        }
        else
        {
            StatusMessage = "就绪：点「扫描」读取词条，或直接点「开始汉化」（会自动先扫描）。";
            Log("未开启启动自动扫描，等待手动「扫描」或「开始汉化」。");
        }
    }

    private void Browse()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择 AutoCAD 安装目录（包含 Express 文件夹）"
        };
        if (dlg.ShowDialog() != true) return;

        var env = AcadLocator.FromInstallPath(dlg.FolderName);
        if (env is null || !env.IsValid)
        {
            MessageBox.Show("所选目录下未找到有效的 Express 目录。", "路径无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Environments.Add(env);
        Selected = env;
        StatusMessage = "已使用手动指定的安装目录。";
        Log($"手动指定：{env.InstallPath}");
        Log($"Express 目录：{env.ExpressPath}（存在={Directory.Exists(env.ExpressPath)}）");
        Log($"生效 CUIX：{env.EffectiveCuixPath}（存在={File.Exists(env.EffectiveCuixPath)}）");
        if (AutoScanOnStartup) _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (Selected is null)
        {
            MessageBox.Show("还没有选择 AutoCAD 安装目录。请点「重新检测」或用「手动选择」指定。",
                "无法扫描", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = BuildOptions();
        Log($"开始扫描：CUIX={options.LocalizeCuix}　DCL={options.LocalizeDcl}　LSP={options.LocalizeLsp}");

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "正在扫描...";
        StatusMessage = "正在扫描可汉化文本...";

        try
        {
            var entries = await Task.Run(() => _engine.Scan(options, Log));

            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            EntriesView.Refresh();

            ProgressValue = 1;
            ProgressText = "扫描完成";
            StatusMessage = SummaryText;
            OnPropertyChanged(nameof(SummaryText));
            Log($"扫描结果：{SummaryText}");

            if (entries.Count == 0) ReportEmptyScan();
        }
        catch (Exception ex)
        {
            Log("扫描失败：" + ex.Message);
            MessageBox.Show("扫描失败：\n" + ex.Message, "扫描失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    /// <summary>确保翻译表已填充；未填充时先自动扫描。返回是否可用。</summary>
    private async Task<bool> EnsureScannedAsync()
    {
        if (Entries.Count > 0) return true;
        await ScanAsync();
        if (Entries.Count > 0) return true;
        ReportEmptyScan();
        return false;
    }

    private void ReportEmptyScan()
    {
        MessageBox.Show(
            "没有扫描到任何可汉化文本，可能原因：\n\n" +
            $"1. Express 目录不存在或为空：{ExpressPath}\n" +
            $"2. 生效的 CUIX 不存在：{CuixPath}\n" +
            "3. 汉化范围三个选项都被取消勾选\n\n" +
            "请点「重新检测」或「手动选择」指定正确的 AutoCAD 安装目录（须包含 Express 文件夹）。",
            "扫描结果为空", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task ApplyAsync()
    {
        Log("点击「开始汉化」。");

        if (Selected is null)
        {
            Log("中止：未选择 AutoCAD 安装目录。");
            MessageBox.Show(
                "还没有选择 AutoCAD 安装目录。\n请点「重新检测」；若检测不到，用「手动选择」指定包含 Express 文件夹的安装目录。",
                "无法汉化", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 关键：确保翻译表已填充，未扫描时先自动扫描，否则会 0 替换
        if (!await EnsureScannedAsync())
        {
            Log("中止：翻译表为空。");
            return;
        }

        if (FileLockHelper.IsAutoCadRunning())
        {
            var procs = string.Join("、", FileLockHelper.RunningAutoCadProcesses());
            Log($"中止：AutoCAD 正在运行（{procs}）。");
            MessageBox.Show("检测到 AutoCAD 正在运行。请先关闭 AutoCAD 再执行汉化，否则文件被占用会写入失败。",
                "请先关闭 AutoCAD", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targets = new List<string>();
        if (LocalizeCuix && File.Exists(CuixPath)) targets.Add(CuixPath);
        var locked = targets.Where(FileLockHelper.IsFileLocked).ToList();
        if (locked.Count > 0)
        {
            Log("中止：目标文件被占用 - " + string.Join("、", locked));
            MessageBox.Show("以下文件被占用：\n" + string.Join("\n", locked), "文件被占用",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 写入前实测权限，避免逐个文件失败
        var blocked = new List<string>();
        if ((LocalizeDcl || LocalizeLsp) && !FileLockHelper.CanWriteToDirectory(ExpressPath))
        {
            blocked.Add(ExpressPath);
        }
        var templateDir = Path.GetDirectoryName(SourceCuixPath ?? string.Empty);
        if (LocalizeCuix && LocalizeSourceTemplate && !string.IsNullOrEmpty(templateDir) &&
            !FileLockHelper.CanWriteToDirectory(templateDir))
        {
            blocked.Add(templateDir);
        }

        if (blocked.Count > 0)
        {
            Log("中止：以下位置不可写 - " + string.Join("、", blocked));
            var r = MessageBox.Show(
                "以下位置位于 Program Files 下，当前权限无法写入：\n" +
                string.Join("\n", blocked) + "\n\n" +
                "这些文件（*.dcl / *.lsp / CUIX 源模板）的汉化需要管理员权限。\n" +
                "注意：不汉化 CUIX 源模板时，AutoCAD 启动会把菜单恢复成英文。\n\n" +
                "　【是】以管理员身份重新启动本程序（推荐）\n" +
                "　【否】只汉化当前可写的部分，跳过上述位置\n" +
                "　【取消】什么都不做",
                "需要管理员权限", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes)
            {
                RestartAsAdmin();
                return;
            }
            if (r != MessageBoxResult.No)
            {
                Log("中止：用户取消。");
                return;
            }

            if (!FileLockHelper.CanWriteToDirectory(ExpressPath))
            {
                LocalizeDcl = false;
                LocalizeLsp = false;
            }
            if (!string.IsNullOrEmpty(templateDir) && !FileLockHelper.CanWriteToDirectory(templateDir))
            {
                LocalizeSourceTemplate = false;
            }
            Log("用户选择：跳过无权限的位置。");
        }

        var enabled = Entries.Count(e => e.Enabled && e.IsTranslated);
        if (enabled == 0)
        {
            Log($"中止：可写入译文 0 条（表内共 {Entries.Count} 条）。");
            MessageBox.Show(
                "没有可写入的译文：翻译表为空，或所有条目都未填写译文/被禁用。\n\n" +
                "请在「翻译表」中确认“中文译文”列有内容且“启用”已勾选。",
                "无可写入内容", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Log($"准备写入：可写入译文 {enabled} 条。");

        if (MessageBox.Show(
                $"即将把 {enabled} 条译文写入 Express Tools。\n写入前会自动备份原文件，可随时一键还原。\n\n是否继续？",
                "确认汉化", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            Log("中止：用户在确认对话框选择“否”。");
            return;
        }

        var options = BuildOptions();
        var list = Entries.ToList();
        var version = Selected.Version;

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "正在写入...";
        StatusMessage = "正在汉化，请勿关闭 AutoCAD 或本程序...";

        var progress = new Progress<double>(p =>
        {
            ProgressValue = p;
            ProgressText = $"正在写入... {p:P0}";
        });

        PatchResult result;
        try
        {
            result = await Task.Run(() => _engine.Apply(options, list, _backup, version, Log, progress));
            _dictionary.SaveOverrides(list);
        }
        catch (Exception ex)
        {
            Log("汉化失败：" + ex.Message);
            MessageBox.Show("汉化失败：\n" + ex.Message + "\n\n可通过「一键还原」恢复备份。",
                "汉化失败", MessageBoxButton.OK, MessageBoxImage.Error);
            IsBusy = false;
            RefreshCommands();
            return;
        }

        ProgressValue = 1;
        ProgressText = "汉化完成";
        StatusMessage = result.Errors.Count == 0
            ? $"完成：修改 {result.FilesChanged} 个文件，替换 {result.StringsReplaced} 处。"
            : $"完成但有 {result.Errors.Count} 个错误，请查看日志。";

        IsBusy = false;
        RefreshCommands();

        Log($"结果：修改文件 {result.FilesChanged}，替换字符串 {result.StringsReplaced}，错误 {result.Errors.Count}");

        var detail = result.Errors.Count == 0
            ? ""
            : "\n\n错误详情（前 5 条）：\n" + string.Join("\n", result.Errors.Take(5)) +
              (result.Errors.Count > 5 ? $"\n… 其余 {result.Errors.Count - 5} 条见日志" : "") +
              "\n\n若是“拒绝访问”，请以管理员身份重新运行本程序。";

        MessageBox.Show(
            $"修改文件：{result.FilesChanged}\n替换字符串：{result.StringsReplaced}\n错误：{result.Errors.Count}" +
            detail + "\n\n重启 AutoCAD 后生效。",
            "汉化完成", MessageBoxButton.OK,
            result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Restore()
    {
        var backups = _backup.List();
        if (backups.Count == 0)
        {
            MessageBox.Show("没有找到任何备份。", "无备份", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var latest = backups[0];
        var tip = string.Join("\n", backups.Take(5).Select(b =>
            $"{b.Manifest.CreatedAt}　AutoCAD {b.Manifest.AcadVersion}　{b.Manifest.Files.Count} 个文件"));

        if (FileLockHelper.IsAutoCadRunning())
        {
            MessageBox.Show("请先关闭 AutoCAD 再还原。", "请先关闭 AutoCAD",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"将还原最近一次备份（{latest.Manifest.CreatedAt}）中的全部文件。\n\n备份列表：\n{tip}\n\n是否继续？",
                "确认还原", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var count = _backup.Restore(Path.Combine(latest.Directory, "manifest.json"), Log);
            MessageBox.Show($"已还原 {count} 个文件。", "还原完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Log($"还原完成：{count} 个文件。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "还原失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export()
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出翻译表",
            Filter = "CSV 表格 (*.csv)|*.csv|JSON 词典 (*.json)|*.json",
            FileName = "ExpressTools-zh-Hans.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (dlg.FilterIndex == 2) DictionaryService.ExportJson(dlg.FileName, Entries);
            else DictionaryService.ExportCsv(dlg.FileName, Entries);
            Log($"已导出：{dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import()
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入翻译表",
            Filter = "CSV 表格 (*.csv)|*.csv|JSON 词典 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var (updated, added) = DictionaryService.ImportInto(dlg.FileName, Entries, Log);
            _dictionary.SaveOverrides(Entries);
            EntriesView.Refresh();
            OnPropertyChanged(nameof(SummaryText));
            MessageBox.Show($"更新 {updated} 条，新增 {added} 条。", "导入完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RestartAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法以管理员身份重启：\n" + ex.Message +
                            "\n\n请手动右键 CAD_ET_HANS.exe，选择“以管理员身份运行”。",
                "提权失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(_backup.Root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _backup.Root,
            UseShellExecute = true
        });
    }

    private LocalizationOptions BuildOptions() => new()
    {
        CuixPath = CuixPath,
        SourceCuixPath = _selected?.SourceCuixPath ?? string.Empty,
        LocalizeSourceTemplate = LocalizeSourceTemplate,
        ExpressPath = ExpressPath,
        LocalizeCuix = LocalizeCuix,
        LocalizeDcl = LocalizeDcl,
        LocalizeLsp = LocalizeLsp,
        Utf8WithBom = Utf8WithBom,
        ForceLocalize = ForceLocalize
    };

    private bool FilterEntry(object obj)
    {
        if (obj is not TranslationEntry e) return false;

        if (_statusFilter switch
            {
                1 => e.Status != EntryStatus.Untranslated,   // 待翻译（英文）
                2 => e.Status != EntryStatus.Translated,     // 已翻译（待写入）
                3 => e.Status != EntryStatus.Localized,      // 已汉化
                4 => e.Status != EntryStatus.Disabled,       // 已禁用
                _ => false
            })
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        return e.Source.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || e.Target.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || e.OriginFile.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_dispatcher.CheckAccess())
        {
            _log.AppendLine(line);
            OnPropertyChanged(nameof(LogText));
        }
        else
        {
            _dispatcher.InvokeAsync(() =>
            {
                _log.AppendLine(line);
                OnPropertyChanged(nameof(LogText));
            });
        }

        // 同时落盘，便于排查问题：%LOCALAPPDATA%\CAD_ET_HANS\logs\app.log
        lock (_logFileLock)
        {
            try { File.AppendAllText(_logFile, line + Environment.NewLine); } catch { /* 忽略 */ }
        }
    }

    private void RefreshCommands()
    {
        foreach (var c in new[] { ScanCommand, ApplyCommand, RestoreCommand, ExportCommand, ImportCommand })
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
