using System.Windows;
using System.Windows.Controls;
using UnityStoryExtractor.GUI.ViewModels;

namespace UnityStoryExtractor.GUI.Views;

/// <summary>
/// MainWindow.xaml の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        try
        {
            InitializeComponent();

            // 著作権警告を表示
            Loaded += (s, e) => ShowCopyrightWarning();

            // ツリービュー選択イベント
            FileTreeView.SelectedItemChanged += FileTreeView_SelectedItemChanged;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初期化エラー:\n{ex.Message}\n\n{ex.StackTrace}", 
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNodeViewModel selectedNode && DataContext is MainViewModel vm)
        {
            vm.SelectedTreeNode = selectedNode;
        }
    }

    private void ShowCopyrightWarning()
    {
        var result = MessageBox.Show(
            "【著作権に関する注意】\n\n" +
            "このツールは教育・研究目的で設計されています。\n" +
            "ゲームデータの抽出は、著作権法で保護されている\n" +
            "コンテンツに対して行う場合、法的問題が発生する\n" +
            "可能性があります。\n\n" +
            "使用者は、自身の行為に対する法的責任を負います。\n" +
            "正規にライセンスされたコンテンツのみを対象とし、\n" +
            "著作権者の権利を尊重してください。\n\n" +
            "同意しますか？",
            "Unity Story Extractor - 著作権警告",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (paths.Length > 0 && DataContext is MainViewModel vm)
                {
                    vm.LoadPath(paths[0]);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ドロップエラー:\n{ex.Message}", "エラー", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
}
