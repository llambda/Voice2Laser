using System;
using System.Windows;
using System.Windows.Interop;

namespace EzCad2Simulator;

public partial class MainWindow : Window
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    private int _messageCount;
    private int _x;
    private int _y;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessageHook);
        };
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmKeyDown && message != WmKeyUp)
            return IntPtr.Zero;

        var virtualKey = unchecked((int)wParam.ToInt64());
        var keyName = GetKeyName(virtualKey);
        var isKeyDown = message == WmKeyDown;
        var rawLParam = unchecked((ulong)lParam.ToInt64());
        var scanCode = (rawLParam >> 16) & 0xFF;
        var extended = ((rawLParam >> 24) & 1) == 1;

        _messageCount++;
        MessageCountText.Text = _messageCount.ToString();
        LastKeyText.Text = $"Ultimo tasto: {keyName} ({(isKeyDown ? "KEYDOWN" : "KEYUP")})";

        MessageLog.Items.Insert(0, new ReceivedMessage
        {
            Time = DateTime.Now.ToString("HH:mm:ss.fff"),
            Message = isKeyDown ? "WM_KEYDOWN" : "WM_KEYUP",
            Key = keyName,
            VirtualKey = $"0x{virtualKey:X2}",
            ScanCode = $"0x{scanCode:X2}",
            Extended = extended ? "Sì" : "No"
        });

        // La simulazione dello stato avviene una sola volta, sul key-down.
        if (isKeyDown)
            ApplySimulatedCommand(virtualKey);

        // Il messaggio è stato ricevuto e registrato: non serve inoltrarlo a WPF.
        handled = true;
        return IntPtr.Zero;
    }

    private void ApplySimulatedCommand(int virtualKey)
    {
        switch (virtualKey)
        {
            case 0x70: // F1
                PreviewState.Text = "Richiesta ricevuta";
                StatusText.Text = "Anteprima simulata";
                break;
            case 0x71: // F2
                StartState.Text = "Ricevuto (solo test)";
                StatusText.Text = "START ricevuto: nessun laser avviato";
                break;
            case 0x1B: // ESC
                StopState.Text = "Ricevuto";
                StartState.Text = "Non richiesto";
                StatusText.Text = "STOP simulato";
                break;
            case 0x26: // Up
                _y++;
                StatusText.Text = "Movimento SU ricevuto";
                UpdatePosition();
                break;
            case 0x28: // Down
                _y--;
                StatusText.Text = "Movimento GIÙ ricevuto";
                UpdatePosition();
                break;
            case 0x25: // Left
                _x--;
                StatusText.Text = "Movimento SINISTRA ricevuto";
                UpdatePosition();
                break;
            case 0x27: // Right
                _x++;
                StatusText.Text = "Movimento DESTRA ricevuto";
                UpdatePosition();
                break;
        }
    }

    private void UpdatePosition() => PositionText.Text = $"X: {_x}    Y: {_y}";

    private static string GetKeyName(int virtualKey) => virtualKey switch
    {
        0x70 => "F1",
        0x71 => "F2",
        0x1B => "ESC",
        0x26 => "FRECCIA SU",
        0x28 => "FRECCIA GIÙ",
        0x25 => "FRECCIA SINISTRA",
        0x27 => "FRECCIA DESTRA",
        _ => $"VK 0x{virtualKey:X2}"
    };

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        MessageLog.Items.Clear();
        _messageCount = 0;
        MessageCountText.Text = "0";
        LastKeyText.Text = "Ultimo tasto: —";
        StatusText.Text = "Registro pulito; in attesa di comandi";
    }

    private sealed class ReceivedMessage
    {
        public string Time { get; init; } = "";
        public string Message { get; init; } = "";
        public string Key { get; init; } = "";
        public string VirtualKey { get; init; } = "";
        public string ScanCode { get; init; } = "";
        public string Extended { get; init; } = "";
    }
}
