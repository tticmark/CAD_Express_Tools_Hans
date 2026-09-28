using System.Text.Json.Serialization;

namespace CAD_ET_HANS.Core.Models;

public sealed class BackupManifest
{
    public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public string AcadVersion { get; set; } = string.Empty;

    public string CuixPath { get; set; } = string.Empty;

    public List<BackupFileItem> Files { get; set; } = new();

    public sealed class BackupFileItem
    {
        /// <summary>文件在原位置的完整路径。</summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>在备份目录中的相对路径。</summary>
        public string RelativePath { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;

        public long Length { get; set; }
    }
}

[JsonSerializable(typeof(BackupManifest))]
internal partial class ManifestJsonContext : JsonSerializerContext;
