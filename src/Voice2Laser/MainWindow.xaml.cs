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

    private void VerifyModel_Click(object sender, RoutedEventArgs e) => _viewModel.VerifyModel();
    private void Start_Click(object sender, RoutedEventArgs e) => _viewModel.Start();
    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Stop();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}