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
    /// <summary>
    /// Outputフォルダーのパス
    /// </summary>
    public static readonly string OutputFolder = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Output");

    /// <summary>
    /// エラーログファイルのパス
    /// </summary>
    public static readonly string LogFile = Path.Combine(
        OutputFolder,
        "UnityStoryExtractor_Error.log");

    public App()
    {
        // Outputフォルダーを確実に作成
        EnsureOutputFolderExists();

        // 最も早い段階でエラーハンドリングを設定
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void EnsureOutputFolderExists()
    {
        try
        {
            if (!Directory.Exists(OutputFolder))
            {
                Directory.CreateDirectory(OutputFolder);
            }
        }
        catch
        {
            // フォルダー作成失敗は無視（後続処理で対応）
        }
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
            WriteLog("=== アプリケーション起動開始 ===");
            WriteLog($"ログファイル: {LogFile}");
            WriteLog($"出力フォルダー: {OutputFolder}");

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
        WriteLog($"[FATAL] UnhandledException: {ex}");
        ShowError(ex);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog($"[ERROR] DispatcherUnhandledException: {e.Exception}");
        ShowError(e.Exception);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog($"[WARN] UnobservedTaskException: {e.Exception}");
        e.SetObserved();
    }

    private static void ShowError(Exception? ex)
    {
        var message = $"予期しないエラーが発生しました:\n\n{ex?.Message}\n\n詳細は以下のログファイルを確認してください:\n{LogFile}";
        MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// ログを書き込む（外部からも呼び出し可能）
    /// </summary>
    public static void WriteLog(string message)
    {
        try
        {
            EnsureOutputFolderExists();
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n";
            File.AppendAllText(LogFile, logMessage, Encoding.UTF8);
        }
        catch
        {
            // ログ書き込み失敗は無視
        }
    }

    /// <summary>
    /// エラーログを書き込む
    /// </summary>
    public static void WriteErrorLog(string context, Exception ex)
    {
        WriteLog($"[ERROR] {context}: {ex.GetType().Name} - {ex.Message}");
        WriteLog($"  StackTrace: {ex.StackTrace}");
        if (ex.InnerException != null)
        {
            WriteLog($"  InnerException: {ex.InnerException.Message}");
        }
    }
}
