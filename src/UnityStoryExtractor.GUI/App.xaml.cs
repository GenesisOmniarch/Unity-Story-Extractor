using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace UnityStoryExtractor.GUI;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "UnityStoryExtractor_Error.log");

    public App()
    {
        // 最も早い段階でエラーハンドリングを設定
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            WriteLog("Application_Startup開始");
        }
        catch (Exception ex)
        {
            WriteLog($"Application_Startupエラー: {ex}");
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            WriteLog("アプリケーション起動開始");
            
            // 日本語エンコーディングのサポートを登録
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // カルチャ設定
            var culture = new CultureInfo("ja-JP");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            WriteLog("base.OnStartup呼び出し");
            base.OnStartup(e);
            WriteLog("OnStartup完了");
        }
        catch (Exception ex)
        {
            WriteLog($"OnStartupエラー: {ex}");
            ShowError(ex);
            Shutdown(1);
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        WriteLog($"UnhandledException: {ex}");
        ShowError(ex);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog($"DispatcherUnhandledException: {e.Exception}");
        ShowError(e.Exception);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog($"UnobservedTaskException: {e.Exception}");
        e.SetObserved();
    }

    private static void ShowError(Exception? ex)
    {
        var message = $"予期しないエラーが発生しました:\n\n{ex?.Message}\n\n詳細はデスクトップの\nUnityStoryExtractor_Error.log\nを確認してください。";
        MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void WriteLog(string message)
    {
        try
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n";
            File.AppendAllText(LogFile, logMessage);
        }
        catch
        {
            // ログ書き込み失敗は無視
        }
    }
}
