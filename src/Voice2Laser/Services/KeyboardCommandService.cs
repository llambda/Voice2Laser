using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Voice2Laser.Services;

/// <summary>
/// Invio sperimentale di messaggi tastiera alla finestra principale di EZCAD2.
/// Non modifica il focus e non usa SendInput: i messaggi sono indirizzati
/// direttamente all'handle della finestra appartenente al processo EzCad2.
/// </summary>
public sealed class KeyboardCommandService
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint MapVkToVsc = 0;
    private const uint MapVkToVscEx = 4;

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
            foreach (var process in Process.GetProcessesByName(TargetProcessName))
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

            message = "EZCAD2 non è in esecuzione oppure la sua finestra principale non è accessibile.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Verifica EZCAD2 non riuscita: {ex.Message}";
            return false;
        }
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
                message = "Finestra principale di EZCAD2 non trovata: nessun messaggio inviato.";
                return true;
            }

            // Ricontrolla handle e PID immediatamente prima dell'invio, per evitare
            // di spedire messaggi a un handle riciclato appartenente a un'altra app.
            if (!IsWindow(windowHandle))
            {
                message = "La finestra di EZCAD2 non è più disponibile. Comando non inviato.";
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var currentProcessId);
            if (currentProcessId != targetProcessId)
            {
                message = "La finestra individuata non appartiene più a EZCAD2. Comando bloccato.";
                return true;
            }

            var scanCode = MapVirtualKey(virtualKey, MapVkToVsc);
            if (scanCode == 0)
            {
                message = $"Impossibile ricavare lo scan code per {keyName}.";
                return true;
            }

            var isExtendedKey = virtualKey is VkUp or VkDown or VkLeft or VkRight;
            var keyDownLParam = BuildKeyLParam(scanCode, isExtendedKey, keyUp: false);
            var keyUpLParam = BuildKeyLParam(scanCode, isExtendedKey, keyUp: true);

            // PostMessage accoda i messaggi alla coda della finestra di EZCAD2.
            // Il valore true conferma l'accodamento, NON che EZCAD2 abbia eseguito l'azione.
            var keyDownPosted = PostMessage(windowHandle, WmKeyDown, new IntPtr(virtualKey), keyDownLParam);
            if (!keyDownPosted)
            {
                var error = Marshal.GetLastWin32Error();
                message = $"Invio di {keyName} non riuscito (PostMessage, errore Windows {error}).";
                return true;
            }

            var keyUpPosted = PostMessage(windowHandle, WmKeyUp, new IntPtr(virtualKey), keyUpLParam);
            if (!keyUpPosted)
            {
                var error = Marshal.GetLastWin32Error();
                message = $"Pressione di {keyName} accodata, ma rilascio non confermato (errore Windows {error}).";
                return true;
            }

            message = $"Messaggi {keyName} accodati alla finestra di EZCAD2 (esito dell'azione da verificare).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Invio messaggio a EZCAD2 non riuscito: {ex.Message}";
            return true;
        }
    }

    private static IntPtr BuildKeyLParam(uint scanCode, bool extended, bool keyUp)
    {
        long value = 1L | ((long)(scanCode & 0xFF) << 16);
        if (extended)
            value |= 1L << 24;

        if (keyUp)
            value |= (1L << 30) | (1L << 31);

        return new IntPtr(value);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
