using System.IO;
using System.Windows;
using Microsoft.Win32;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.GUI.ViewModels;

/// <summary>
/// 設定ウィンドウ
/// </summary>
public partial class SettingsWindow : Window
{
    public ExtractionOptions Options { get; private set; }
    public string OutputFolderPath { get; private set; }

    public SettingsWindow(ExtractionOptions currentOptions, string currentOutputPath)
    {
        InitializeComponent();

        Options = new ExtractionOptions
        {
            ExtractTextAsset = currentOptions.ExtractTextAsset,
            ExtractMonoBehaviour = currentOptions.ExtractMonoBehaviour,
            ExtractAssembly = currentOptions.ExtractAssembly,
            ProcessResSFiles = currentOptions.ProcessResSFiles,
            AttemptDecryption = currentOptions.AttemptDecryption,
            MinTextLength = currentOptions.MinTextLength,
            MaxTextLength = currentOptions.MaxTextLength,
            UseParallelProcessing = currentOptions.UseParallelProcessing,
            MaxDegreeOfParallelism = currentOptions.MaxDegreeOfParallelism
        };

        OutputFolderPath = currentOutputPath;

        // UIに反映
        OutputFolderTextBox.Text = OutputFolderPath;
        ExtractTextAssetCheckBox.IsChecked = Options.ExtractTextAsset;
        ExtractMonoBehaviourCheckBox.IsChecked = Options.ExtractMonoBehaviour;
        ExtractAssemblyCheckBox.IsChecked = Options.ExtractAssembly;
        ProcessResSCheckBox.IsChecked = Options.ProcessResSFiles;
        AttemptDecryptionCheckBox.IsChecked = Options.AttemptDecryption;
        MinLengthTextBox.Text = Options.MinTextLength.ToString();
        MaxLengthTextBox.Text = Options.MaxTextLength.ToString();
        UseParallelCheckBox.IsChecked = Options.UseParallelProcessing;
        ParallelismTextBox.Text = Options.MaxDegreeOfParallelism.ToString();
    }

    private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "出力フォルダを選択",
            InitialDirectory = OutputFolderPath
        };

        if (dialog.ShowDialog() == true)
        {
            OutputFolderPath = dialog.FolderName;
            OutputFolderTextBox.Text = OutputFolderPath;
        }
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(OutputFolderPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", OutputFolderPath);
        }
        else
        {
            MessageBox.Show("フォルダが存在しません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        // 設定を更新
        Options.ExtractTextAsset = ExtractTextAssetCheckBox.IsChecked ?? true;
        Options.ExtractMonoBehaviour = ExtractMonoBehaviourCheckBox.IsChecked ?? true;
        Options.ExtractAssembly = ExtractAssemblyCheckBox.IsChecked ?? true;
        Options.ProcessResSFiles = ProcessResSCheckBox.IsChecked ?? true;
        Options.AttemptDecryption = AttemptDecryptionCheckBox.IsChecked ?? true;

        if (int.TryParse(MinLengthTextBox.Text, out var minLen))
            Options.MinTextLength = minLen;
        if (int.TryParse(MaxLengthTextBox.Text, out var maxLen))
            Options.MaxTextLength = maxLen;

        Options.UseParallelProcessing = UseParallelCheckBox.IsChecked ?? true;
        if (int.TryParse(ParallelismTextBox.Text, out var parallelism))
            Options.MaxDegreeOfParallelism = parallelism;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
