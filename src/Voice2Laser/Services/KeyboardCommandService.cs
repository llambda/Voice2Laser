using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voice2Laser.Services;

/// <summary>
/// Invio limitato di F1/F2/ESC e frecce direzionali alla finestra principale di EZCAD2.
/// Non invia input se EZCAD2 non è la finestra effettivamente in primo piano.
/// </summary>
public sealed class KeyboardCommandService
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkEscape = 0x1B;
    private const ushort VkF1 = 0x70;
    private const ushort VkF2 = 0x71;
    private const ushort VkUp = 0x26;
    private const ushort VkDown = 0x28;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;
    private const string TargetProcessName = "EzCad2";

    public bool CheckConnection(out string message)
    {
        try
        {
            var processes = Process.GetProcessesByName(TargetProcessName);
            if (processes.Length == 0)
            {
                message = "EZCAD2 non è in esecuzione.";
                return false;
            }

            try
            {
                foreach (var process in processes)
                {
                    using (process)
                    {
                        try
                        {
                            var processId = process.Id;
                            var windowHandle = process.MainWindowHandle;
                            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
                                continue;

                            GetWindowThreadProcessId(windowHandle, out var windowProcessId);
                            if (windowProcessId == (uint)processId)
                            {
                                message = $"EZCAD2 è in esecuzione e raggiungibile. Finestra: {process.MainWindowTitle}";
                                return true;
                            }
                        }
                        catch
                        {
                            // Il processo può terminare o negare l'accesso durante il controllo.
                        }
                    }
                }
            }
            finally
            {
                // I processi vengono rilasciati nel ciclo.
            }

            message = "EZCAD2 è in esecuzione, ma non è stata trovata una finestra principale accessibile.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Verifica EZCAD2 non riuscita: {ex.Message}";
            return false;
        }
    }

    public bool IsEzCadForeground(out string message)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(TargetProcessName))
            {
                using (process)
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero || !IsWindow(handle)) continue;
                        GetWindowThreadProcessId(handle, out var pid);
                        if (pid == (uint)process.Id && IsTargetForegroundWindow(handle, pid))
                        {
                            message = "EZCAD2 è la finestra attiva.";
                            return true;
                        }
                    }
                    catch { /* Il processo può chiudersi durante il controllo. */ }
                }
            }
        }
        catch (Exception ex)
        {
            message = $"Verifica focus EZCAD2 non riuscita: {ex.Message}";
            return false;
        }

        message = "EZCAD2 non è in primo piano. Nessun comando verrà inviato.";
        return false;
    }

    public static string GetKeyDescription(string actionId) => actionId switch
    {
        "preview" => "F1",
        "start_laser" => "F2",
        "stop_laser" => "ESC",
        "move_up" => "FRECCIA SU",
        "move_down" => "FRECCIA GIÙ",
        "move_left" => "FRECCIA SINISTRA",
        "move_right" => "FRECCIA DESTRA",
        _ => "NESSUN TASTO"
    };

    public bool TryExecute(string actionId, out string message)
    {
        var virtualKey = actionId switch
        {
            "preview" => VkF1,
            "start_laser" => VkF2,
            "stop_laser" => VkEscape,
            "move_up" => VkUp,
            "move_down" => VkDown,
            "move_left" => VkLeft,
            "move_right" => VkRight,
            _ => (ushort)0
        };

        if (virtualKey == 0)
        {
            message = "Azione riconosciuta; nessun tasto associato.";
            return false;
        }

        var keyName = GetKeyDescription(actionId);

        try
        {
            var windowHandle = IntPtr.Zero;
            uint targetProcessId = 0;

            foreach (var process in Process.GetProcessesByName(TargetProcessName))
            {
                using (process)
                {
                    try
                    {
                        var candidate = process.MainWindowHandle;
                        if (candidate == IntPtr.Zero || !IsWindow(candidate))
                            continue;

                        GetWindowThreadProcessId(candidate, out var candidateProcessId);
                        if (candidateProcessId != (uint)process.Id)
                            continue;

                        windowHandle = candidate;
                        targetProcessId = candidateProcessId;
                        break;
                    }
                    catch
                    {
                        // Il processo può terminare mentre viene enumerato.
                    }
                }
            }

            if (windowHandle == IntPtr.Zero || targetProcessId == 0)
            {
                message = "Finestra principale di EZCAD2 non trovata: nessun tasto inviato.";
                return true;
            }

            var inputs = new[]
            {
                CreateKeyboardInput(virtualKey, 0),
                CreateKeyboardInput(virtualKey, KeyUp)
            };

            // Non portare EZCAD2 in primo piano automaticamente: l'operatore deve
            // selezionarlo esplicitamente. Verifica il focus immediatamente prima di SendInput.
            if (!IsWindow(windowHandle) || !IsTargetForegroundWindow(windowHandle, targetProcessId))
            {
                message = "EZCAD2 non è la finestra attiva: tasto bloccato. Seleziona EZCAD2 e riprova.";
                return true;
            }

            // SendInput è globale: il controllo del focus riduce il rischio, ma non elimina
            // la brevissima race condition in cui Windows potrebbe cambiare finestra attiva.
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            var error = Marshal.GetLastWin32Error();
            if (sent == 1)
            {
                // Se Windows ha accettato solo il key-down, prova sempre a rilasciare il tasto.
                var releaseSent = SendInput(1, new[] { CreateKeyboardInput(virtualKey, KeyUp) }, Marshal.SizeOf<INPUT>());
                message = releaseSent == 1
                    ? $"Invio di {keyName} parziale; rilascio del tasto completato."
                    : $"Invio di {keyName} parziale: anche il rilascio non è stato confermato.";
            }
            else
            {
                message = sent == (uint)inputs.Length
                    ? $"Inviato {keyName} a EZCAD2."
                    : $"Invio di {keyName} non riuscito (errore Windows {error}). Nessun esito garantito.";
            }

            return true;
        }
        catch (Exception ex)
        {
            message = $"Invio tasto non riuscito: {ex.Message}";
            return true;
        }
    }

    private static bool IsTargetForegroundWindow(IntPtr expectedWindow, uint expectedProcessId)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero || foregroundWindow != expectedWindow)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        return foregroundProcessId == expectedProcessId;
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
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
