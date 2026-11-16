using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voice2Laser.Services;

/// <summary>
/// Invio limitato di F1/F2/ESC alla finestra principale del processo EZCAD2.
/// Non invia input se EZCAD2 non è la finestra effettivamente in primo piano.
/// </summary>
public sealed class KeyboardCommandService
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const int SwRestore = 9;
    private const ushort VkEscape = 0x1B;
    private const ushort VkF1 = 0x70;
    private const ushort VkF2 = 0x71;
    private const string TargetProcessName = "EzCad2";

    public bool TryExecute(string actionId, out string message)
    {
        var virtualKey = actionId switch
        {
            "preview" => VkF1,
            "start_laser" => VkF2,
            "stop_laser" => VkEscape,
            _ => (ushort)0
        };

        if (virtualKey == 0)
        {
            message = "Azione riconosciuta; nessun tasto associato.";
            return false;
        }

        try
        {
            var windowHandle = IntPtr.Zero;
            foreach (var process in Process.GetProcessesByName(TargetProcessName))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            windowHandle = process.MainWindowHandle;
                            break;
                        }
                    }
                    catch
                    {
                        // Il processo può terminare mentre viene enumerato.
                    }
                }
            }

            if (windowHandle == IntPtr.Zero)
            {
                message = "EZCAD2 non trovato: nessun tasto inviato.";
                return true;
            }
            ShowWindowAsync(windowHandle, SwRestore);
            SetForegroundWindow(windowHandle);

            // Non inviare input se Windows non ha effettivamente attivato EZCAD2.
            if (GetForegroundWindow() != windowHandle)
            {
                message = "EZCAD2 non è diventato la finestra attiva: tasto non inviato.";
                return true;
            }

            var inputs = new[]
            {
                CreateKeyboardInput(virtualKey, 0),
                CreateKeyboardInput(virtualKey, KeyUp)
            };

            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            message = sent == inputs.Length
                ? $"Inviato {(virtualKey == VkF1 ? "F1" : virtualKey == VkF2 ? "F2" : "ESC")} a EZCAD2."
                : "Windows non ha accettato il comando da tastiera.";

            return true;
        }
        catch (Exception ex)
        {
            message = $"Invio tasto non riuscito: {ex.Message}";
            return true;
        }
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new INPUTUNION
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = virtualKey,
                ScanCode = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = UIntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
