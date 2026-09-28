using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.App.Converters;

/// <summary>把翻译状态映射为行前景色：已翻译中性、未翻译琥珀色、已禁用灰显。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value switch
        {
            EntryStatus s => s,
            TranslationEntry e => e.Status,
            _ => EntryStatus.Untranslated
        };

        var resourceKey = status switch
        {
            EntryStatus.Translated => "TextPrimaryBrush",
            EntryStatus.Disabled => "TextSecondaryBrush",
            _ => "WarningBrush"
        };

        return Application.Current.TryFindResource(resourceKey) as Brush
               ?? new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
