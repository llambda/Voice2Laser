using System.Windows;
using Microsoft.Win32;
using Voice2Laser.ViewModels;

namespace Voice2Laser;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void BrowseModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleziona la cartella del modello Vosk",
            Multiselect = false,
            InitialDirectory = System.IO.Directory.Exists(_viewModel.ModelPath)
                ? _viewModel.ModelPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.ModelPath = dialog.FolderName;
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e) => _viewModel.AddCommand();
    private void RemoveCommand_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedCommand();

    private void Start_Click(object sender, RoutedEventArgs e) => _viewModel.Start();
    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Stop();
    private void CheckEzCad_Click(object sender, RoutedEventArgs e) => _viewModel.CheckEzCadConnection();
    private void RefreshAudioDevices_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshAudioDevices();
    private void Calibrate_Click(object sender, RoutedEventArgs e) => _viewModel.StartCalibration();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}