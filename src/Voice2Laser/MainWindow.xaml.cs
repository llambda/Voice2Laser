using System.Windows;
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

    private void Start_Click(object sender, RoutedEventArgs e) => _viewModel.Start();
    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Stop();
    private void CheckEzCad_Click(object sender, RoutedEventArgs e) => _viewModel.CheckEzCadConnection();
    private void RefreshAudioDevices_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshAudioDevices();
    private void AddCommand_Click(object sender, RoutedEventArgs e) => _viewModel.AddCommand();
    private void RemoveCommand_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedCommand();
    private void SaveCommands_Click(object sender, RoutedEventArgs e) => _viewModel.SaveCommandConfiguration();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}