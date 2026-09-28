using System.Diagnostics;

namespace CAD_ET_HANS.Core.Services;

/// <summary>进程与文件占用检测，避免写入时损坏文件。</summary>
public static class FileLockHelper
{
    /// <summary>
    /// 只认 AutoCAD 主程序本身。Autodesk Desktop App / Access 等后台进程常驻，
    /// 不能计入，否则会误报“请先关闭 AutoCAD”。
    /// </summary>
    private static readonly string[] AcadProcessNames =
    {
        "acad", "accoreconsole"
    };

    public static bool IsAutoCadRunning()
        => Process.GetProcesses().Any(p =>
            AcadProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> RunningAutoCadProcesses()
        => Process.GetProcesses()
            .Where(p => AcadProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>以独占方式尝试打开，判断文件是否被占用。</summary>
    public static bool IsFileLocked(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>目标文件是否在需要提权的目录中。</summary>
    public static bool NeedsElevation(string path)
    {
        var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        return path.StartsWith(pf, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 实测是否能在该目录创建并删除文件。Program Files 下未提权时会返回 false，
    /// 用于在写入前提前发现权限问题，避免逐个文件失败。
    /// </summary>
    public static bool CanWriteToDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return false;

        var probe = Path.Combine(directory, ".hans-write-test.tmp");
        try
        {
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
