using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.GUI.Converters;

/// <summary>
/// ブール値を可視性に変換するコンバーター
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool inverse = parameter?.ToString() == "Inverse";

        if (inverse) boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// ファイルノードタイプをアイコンに変換するコンバーター
/// </summary>
public class FileNodeTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FileNodeType nodeType)
        {
            return nodeType switch
            {
                FileNodeType.Directory => PackIconKind.Folder,
                FileNodeType.AssetsFile => PackIconKind.FileDocument,
                FileNodeType.AssetBundle => PackIconKind.PackageVariant,
                FileNodeType.ResourcesAssets => PackIconKind.Database,
                FileNodeType.ResSFile => PackIconKind.FileImage,
                FileNodeType.Assembly => PackIconKind.CodeBraces,
                FileNodeType.GlobalGameManagers => PackIconKind.Cog,
                _ => PackIconKind.File
            };
        }

        return PackIconKind.File;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// ファイルサイズを人間が読める形式に変換するコンバーター
/// </summary>
public class FileSizeConverter : IValueConverter
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long size && size > 0)
        {
            int order = 0;
            double adjustedSize = size;

            while (adjustedSize >= 1024 && order < SizeSuffixes.Length - 1)
            {
                order++;
                adjustedSize /= 1024;
            }

            return $"({adjustedSize:0.##} {SizeSuffixes[order]})";
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 抽出ソースを日本語に変換するコンバーター
/// </summary>
public class ExtractionSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ExtractionSource source)
        {
            return source switch
            {
                ExtractionSource.TextAsset => "テキストアセット",
                ExtractionSource.MonoBehaviour => "MonoBehaviour",
                ExtractionSource.ScriptableObject => "ScriptableObject",
                ExtractionSource.Assembly => "アセンブリ",
                ExtractionSource.IL2CPP => "IL2CPP",
                ExtractionSource.Binary => "バイナリ",
                ExtractionSource.ResS => "リソースストリーム",
                _ => "不明"
            };
        }

        return "不明";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
