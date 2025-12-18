using System.Globalization;
using System.Text;
using System.Windows;

namespace UnityStoryExtractor.GUI;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 日本語エンコーディングのサポートを登録
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // カルチャ設定
        var culture = new CultureInfo("ja-JP");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // 未処理例外のハンドリング
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show(
                $"予期しないエラーが発生しました:\n\n{ex?.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"予期しないエラーが発生しました:\n\n{args.Exception.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
