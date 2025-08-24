using System.Runtime.InteropServices;
using System.Text.Json;
using MobControlUI.Core.Logging;
using MobControlUI.Core.Mapping;
using MobControlUI.Core.Net;
using MobControlUI.Core.Storage;

namespace MobControlUI.Core.Input
{
    /// <summary>
    /// Bridges WS raw frames → mapping lookup → OS input using Win32 SendInput (no NuGet deps).
    /// </summary>
    public sealed class InputEventRouter : IDisposable
    {
        private readonly TokenWebSocketServer _ws;
        private readonly IActiveMappingService _active;
        private readonly IInputMappingStore _store;
        private readonly ILogService _log;

        private sealed record HoldState(List<ushort> Keys, string? MouseButton);
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Dictionary<string, HoldState>> _byDevice = new();

        public InputEventRouter(TokenWebSocketServer ws,
                                IActiveMappingService active,
                                IInputMappingStore store,
                                ILogService log)
        {
            _ws = ws;
            _active = active;
            _store = store;
            _log = log;

            _ws.OnRawMessage += HandleRaw;
            _ws.OnDeviceDisconnected += OnDeviceDisconnected;
        }

        public void Dispose()
        {
            _ws.OnRawMessage -= HandleRaw;
            _ws.OnDeviceDisconnected -= OnDeviceDisconnected;

            lock (_gate)
            {
                foreach (var id in _byDevice.Keys.ToList())
                    ReleaseAllForDevice(id);
                _byDevice.Clear();
            }
        }

        // ------------------- events -------------------

        private void HandleRaw(Guid deviceId, string token, string raw)
        {
            try
            {
                foreach (var (type, id, state) in ParseEvents(raw))
                {
                    var mappingName = _active.Get(deviceId);
                    if (string.IsNullOrWhiteSpace(mappingName))
                    {
                        _log.Add($"Input: No active mapping for device {deviceId}; ignoring {id}:{state}", "Warn");
                        continue;
                    }

                    var file = _store.LoadFlexibleAsync(mappingName!).GetAwaiter().GetResult();
                    if (file is null)
                    {
                        _log.Add($"Input: Mapping '{mappingName}' not found; ignoring {id}:{state}", "Warn");
                        continue;
                    }

                    if (!file.Bindings.TryGetValue(id, out var binding) || string.IsNullOrWhiteSpace(binding))
                    {
                        _log.Add($"Input: No binding for action '{id}' in '{mappingName}'", "Debug");
                        continue;
                    }

                    _log.Add($"Input Map: {id} ({state}) → '{binding}'", "Debug");
                    ExecuteBinding(deviceId, id, binding!, state);
                }
            }
            catch (Exception ex)
            {
                _log.Add($"Input: Error processing raw frame – {ex.Message}", "Warn");
            }
        }

        private void OnDeviceDisconnected(Guid deviceId, string _)
        {
            ReleaseAllForDevice(deviceId);
            _active.Clear(deviceId);
        }

        // ------------------- parsing -------------------

        private static IEnumerable<(string type, string id, string state)> ParseEvents(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                    if (TryRead(el, out var i)) yield return i;
            }
            else if (root.ValueKind == JsonValueKind.Object && TryRead(root, out var single))
            {
                yield return single;
            }
        }

        private static bool TryRead(JsonElement el, out (string type, string id, string state) item)
        {
            item = default;
            if (el.ValueKind != JsonValueKind.Object) return false;

            var type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? (t.GetString() ?? "button")
                : "button";

            if (!el.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) return false;
            if (!el.TryGetProperty("state", out var stEl) || stEl.ValueKind != JsonValueKind.String) return false;

            item = (type, idEl.GetString()!, stEl.GetString()!);
            return true;
        }

        // ------------------- execution -------------------

        private void ExecuteBinding(Guid deviceId, string actionId, string binding, string state)
        {
            // Mouse wheel: do on "down", ignore "up"
            if (binding.Equals("WheelUp", StringComparison.OrdinalIgnoreCase) ||
                binding.Equals("WheelDown", StringComparison.OrdinalIgnoreCase))
            {
                if (state.Equals("down", StringComparison.OrdinalIgnoreCase))
                    MouseWheel(binding.Equals("WheelUp", StringComparison.OrdinalIgnoreCase) ? 120 : -120);
                return;
            }

            // Mouse buttons
            if (IsMouseButton(binding))
            {
                if (state.Equals("down", StringComparison.OrdinalIgnoreCase))
                {
                    MouseButton(binding, down: true);
                    RememberHold(deviceId, actionId, new HoldState(new List<ushort>(), binding));
                }
                else if (state.Equals("up", StringComparison.OrdinalIgnoreCase))
                {
                    if (ForgetHold(deviceId, actionId, out var held) && held.MouseButton is not null)
                        MouseButton(held.MouseButton, down: false);
                    else
                        MouseButton(binding, down: false);
                }
                return;
            }

            // Keyboard hotkeys (e.g., "Ctrl+Shift+K", "Space", "F5", "A")
            var (mods, main) = ParseHotkey(binding);
            if (mods.Count == 0 && main is null)
            {
                _log.Add($"Input: Unsupported binding '{binding}'", "Warn");
                return;
            }

            if (state.Equals("down", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var m in mods) KeyDown(m);
                if (main is not null) KeyDown(main.Value);
                RememberHold(deviceId, actionId, new HoldState(new List<ushort>(mods.Concat(main is null ? Enumerable.Empty<ushort>() : new[] { main.Value })), null));
            }
            else if (state.Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                if (ForgetHold(deviceId, actionId, out var held))
                {
                    for (int i = held.Keys.Count - 1; i >= 0; i--) KeyUp(held.Keys[i]);
                }
                else
                {
                    if (main is not null) KeyUp(main.Value);
                    for (int i = mods.Count - 1; i >= 0; i--) KeyUp(mods[i]);
                }
            }
        }

        private static bool IsMouseButton(string s)
            => s.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase)
            || s.Equals("MouseRight", StringComparison.OrdinalIgnoreCase)
            || s.Equals("MouseMiddle", StringComparison.OrdinalIgnoreCase)
            || s.Equals("MouseX1", StringComparison.OrdinalIgnoreCase)
            || s.Equals("MouseX2", StringComparison.OrdinalIgnoreCase);

        // ------------------- Win32 SendInput helpers -------------------

        // VK constants (subset)
        private const ushort VK_LBUTTON = 0x01;
        private const ushort VK_RBUTTON = 0x02;
        private const ushort VK_CANCEL = 0x03;
        private const ushort VK_MBUTTON = 0x04;
        private const ushort VK_XBUTTON1 = 0x05;
        private const ushort VK_XBUTTON2 = 0x06;

        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_MENU = 0x12; // Alt
        private const ushort VK_LWIN = 0x5B;

        private static ushort VkFromLetter(char c) => (ushort)('A' <= c && c <= 'Z' ? (0x41 + (c - 'A')) : c);

        private static (List<ushort> mods, ushort? main) ParseHotkey(string hotkey)
        {
            var mods = new List<ushort>();
            ushort? main = null;

            foreach (var raw in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var token = raw.Trim();

                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) { mods.Add(VK_CONTROL); continue; }
                if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { mods.Add(VK_SHIFT); continue; }
                if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) { mods.Add(VK_MENU); continue; }
                if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)) { mods.Add(VK_LWIN); continue; }

                if (TryKey(token, out var vk)) main = vk;
            }

            return (mods, main);
        }

        private static bool TryKey(string token, out ushort vk)
        {
            vk = 0;

            // Letters A..Z
            if (token.Length == 1 && token[0] >= 'A' && token[0] <= 'Z')
            {
                vk = VkFromLetter(token[0]);
                return true;
            }

            // Digits 0..9
            if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
            {
                vk = (ushort)(0x30 + (token[0] - '0'));
                return true;
            }

            // Function keys F1..F24
            if ((token.StartsWith("F") || token.StartsWith("f")) && int.TryParse(token[1..], out var fn) && fn is >= 1 and <= 24)
            {
                vk = (ushort)(0x70 + (fn - 1)); // F1 = 0x70
                return true;
            }

            // Common names
            vk = token.ToLowerInvariant() switch
            {
                "space" or "spacebar" => (ushort)0x20,
                "enter" or "return" => (ushort)0x0D,
                "tab" => (ushort)0x09,
                "esc" or "escape" => (ushort)0x1B,
                "backspace" => (ushort)0x08,
                "delete" or "del" => (ushort)0x2E,
                "insert" or "ins" => (ushort)0x2D,
                "home" => (ushort)0x24,
                "end" => (ushort)0x23,
                "pageup" or "pgup" => (ushort)0x21,
                "pagedown" or "pgdn" => (ushort)0x22,
                "up" => (ushort)0x26,
                "down" => (ushort)0x28,
                "left" => (ushort)0x25,
                "right" => (ushort)0x27,
                _ => (ushort)0
            };
            return vk != 0;
        }

        private static void KeyDown(ushort vk) => SendKeyboard(vk, keyUp: false);
        private static void KeyUp(ushort vk) => SendKeyboard(vk, keyUp: true);

        private static void SendKeyboard(ushort vk, bool keyUp)
        {
            var inp = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }

        private static void MouseButton(string button, bool down)
        {
            uint flags = button.ToLowerInvariant() switch
            {
                "mouseleft" => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
                "mouseright" => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
                "mousemiddle" => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
                "mousex1" => down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
                "mousex2" => down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
                _ => 0
            };

            uint mouseData = 0;
            if (button.Equals("MouseX1", StringComparison.OrdinalIgnoreCase)) mouseData = XBUTTON1;
            else if (button.Equals("MouseX2", StringComparison.OrdinalIgnoreCase)) mouseData = XBUTTON2;

            var inp = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = mouseData,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }

        private static void MouseWheel(int delta)
        {
            var inp = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = (uint)delta,
                        dwFlags = MOUSEEVENTF_WHEEL,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }

        private void RememberHold(Guid deviceId, string actionId, HoldState hs)
        {
            lock (_gate)
            {
                if (!_byDevice.TryGetValue(deviceId, out var map))
                    _byDevice[deviceId] = map = new();
                map[actionId] = hs;
            }
        }

        private bool ForgetHold(Guid deviceId, string actionId, out HoldState hs)
        {
            lock (_gate)
            {
                hs = null!;
                if (_byDevice.TryGetValue(deviceId, out var map) && map.Remove(actionId, out var h))
                {
                    hs = h!;
                    return true;
                }
                return false;
            }
        }

        private void ReleaseAllForDevice(Guid deviceId)
        {
            lock (_gate)
            {
                if (!_byDevice.TryGetValue(deviceId, out var map)) return;

                foreach (var kv in map.ToList())
                {
                    var held = kv.Value;
                    if (held.MouseButton is not null) MouseButton(held.MouseButton, down: false);
                    if (held.Keys is { Count: > 0 })
                        for (int i = held.Keys.Count - 1; i >= 0; i--)
                            KeyUp(held.Keys[i]);
                }

                map.Clear();
            }
        }

        // ------------- Win32 interop -------------

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        private const uint XBUTTON1 = 0x0001;
        private const uint XBUTTON2 = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}