namespace CAD_ET_HANS.Core.Services;

public static class TextUtil
{
    /// <summary>是否包含中日韩统一表意文字（用于判断该文本是否已经是中文）。</summary>
    public static bool ContainsCjk(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u4E00' and <= '\u9FFF') return true;
            if (c is >= '\u3400' and <= '\u4DBF') return true;
        }
        return false;
    }
}
