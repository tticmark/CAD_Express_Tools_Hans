using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CAD_ET_HANS.Core.Models;

/// <summary>
/// 可汉化文本的类别。词典与补丁器均以 (类别, 原文) 作为键，
/// 避免同一英文在不同语境下被误译。
/// </summary>
public enum EntryKind
{
    MenuItem,
    RibbonItem,
    ToolbarItem,
    CommandName,
    HelpString,
    DclLabel,
    LspPrompt
}

public enum EntryStatus
{
    Translated,
    Untranslated,
    Disabled,
    /// <summary>目标文件里已经是中文，无需再处理。</summary>
    Localized
}

public sealed class TranslationEntry : INotifyPropertyChanged
{
    private string _target = string.Empty;
    private bool _enabled = true;

    public TranslationEntry(EntryKind kind, string source, string originFile)
    {
        Kind = kind;
        Source = source;
        OriginFile = originFile;
    }

    /// <summary>英文原文。</summary>
    public string Source { get; }

    /// <summary>中文译文，可在界面中编辑。</summary>
    public string Target
    {
        get => _target;
        set
        {
            if (_target == value) return;
            _target = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public EntryKind Kind { get; }

    /// <summary>来源文件，如 MenuGroup.cui / tcase.dcl / tcase.lsp。</summary>
    public string OriginFile { get; set; } = string.Empty;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>原文已经是中文（此前已汉化过），无需再翻译。</summary>
    public bool AlreadyLocalized { get; set; }

    public bool IsTranslated =>
        !string.IsNullOrWhiteSpace(Target) && !string.Equals(Target, Source, StringComparison.Ordinal);

    public EntryStatus Status => AlreadyLocalized ? EntryStatus.Localized
        : !Enabled ? EntryStatus.Disabled
        : IsTranslated ? EntryStatus.Translated
        : EntryStatus.Untranslated;

    public string StatusText => Status switch
    {
        EntryStatus.Disabled => "已禁用",
        EntryStatus.Translated => "已翻译",
        EntryStatus.Localized => "已汉化",
        _ => "未翻译"
    };

    public string KindText => Kind switch
    {
        EntryKind.MenuItem => "菜单",
        EntryKind.RibbonItem => "功能区",
        EntryKind.ToolbarItem => "工具栏",
        EntryKind.CommandName => "命令名",
        EntryKind.HelpString => "命令说明",
        EntryKind.DclLabel => "对话框",
        EntryKind.LspPrompt => "命令提示",
        _ => Kind.ToString()
    };

    /// <summary>词典/导入导出使用的稳定键。</summary>
    public string Key => $"{(int)Kind}|{Source}";

    public TranslationEntry Clone() => new(Kind, Source, OriginFile)
    {
        Target = Target,
        Enabled = Enabled
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
