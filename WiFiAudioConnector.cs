using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using KeyInterop = System.Windows.Input.KeyInterop;
using Orientation = System.Windows.Controls.Orientation;

namespace WiFiAudioConnector
{
    public class DeviceItem
    {
        public string Name { get; set; }
        public string Target { get; set; } // e.g. "192.168.31.238:5555" or USB serial "abc1234"
        public string Ip { get; set; }
        public int Port { get; set; }
        public bool IsUsb { get; set; }
        public bool IsCustom { get; set; }

        public override string ToString()
        {
            if (IsCustom) return "➕ 手动输入设备 IP / 端口...";
            if (IsUsb) return string.Format("🔌 [USB] {0}", Name);
            return string.Format("📶 {0} ({1}:{2})", Name, Ip, Port);
        }
    }

    public class DeviceHotkeyBinding
    {
        public string Target { get; set; }        // "192.168.31.239:5555" or "a22280af"
        public string DeviceName { get; set; }    // "Xiaomi 15 Pro"
        public bool IsUsb { get; set; }           // true = USB 有线, false = Wi-Fi 无线
        public ModifierKeys Modifiers { get; set; }
        public Key Key { get; set; }
        public bool Enabled { get; set; }

        public string DisplayName
        {
            get
            {
                return string.Format("{0} [{1}]", DeviceName, IsUsb ? "USB 有线" : "Wi-Fi 无线");
            }
        }

        public string HotkeyString
        {
            get
            {
                if (!Enabled || Key == Key.None) return "(未设置)";
                return HotkeyManager.FormatHotkey(Modifiers, Key);
            }
        }

        public override string ToString()
        {
            string modeIcon = IsUsb ? "🔌" : "📶";
            string modeTag = IsUsb ? "USB 有线" : "Wi-Fi 无线";
            string hkTag = Enabled && Key != Key.None ? HotkeyManager.FormatHotkey(Modifiers, Key) : "未设置";
            return string.Format("{0}  {1} [{2}]  ({3})   ▶   快捷键: {4}", modeIcon, DeviceName, modeTag, Target, hkTag);
        }
    }

    #region Windows Core Audio Session COM Interop
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    internal enum EDataFlow { eRender, eCapture, eAll }
    internal enum ERole { eConsole, eMultimedia, eCommunications }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        int NotImpl1();
        int NotImpl2();
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int SessionCount);
        [PreserveSig]
        int GetSession(int SessionIndex, out IAudioSessionControl Session);
    }

    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int pRetVal);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
    }

    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int pRetVal);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetProcessId(out uint pRetVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float fLevel, ref Guid EventContext);
        [PreserveSig] int GetMasterVolume(out float pfLevel);
        [PreserveSig] int SetMute(bool bMute, ref Guid EventContext);
        [PreserveSig] int GetMute(out bool pbMute);
    }

    public static class WindowsAudioSessionController
    {
        public static bool SetProcessVolume(int pid, float volume, bool? mute = null)
        {
            if (pid <= 0) return false;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice dev;
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != 0 || dev == null)
                    return false;

                Guid iidManager = typeof(IAudioSessionManager2).GUID;
                object oMgr;
                if (dev.Activate(ref iidManager, 23, IntPtr.Zero, out oMgr) != 0 || oMgr == null)
                    return false;

                var mgr = (IAudioSessionManager2)oMgr;
                IAudioSessionEnumerator sessionEnum;
                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null)
                    return false;

                int count;
                sessionEnum.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    sessionEnum.GetSession(i, out ctl);
                    var ctl2 = ctl as IAudioSessionControl2;
                    var vol = ctl as ISimpleAudioVolume;
                    if (ctl2 != null && vol != null)
                    {
                        uint pId;
                        ctl2.GetProcessId(out pId);
                        if (pId == (uint)pid)
                        {
                            Guid empty = Guid.Empty;
                            float clamped = Math.Max(0.0f, Math.Min(1.0f, volume));
                            vol.SetMasterVolume(clamped, ref empty);
                            if (mute.HasValue)
                            {
                                vol.SetMute(mute.Value, ref empty);
                            }
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool GetProcessVolume(int pid, out float volume, out bool mute)
        {
            volume = 1.0f;
            mute = false;
            if (pid <= 0) return false;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice dev;
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != 0 || dev == null)
                    return false;

                Guid iidManager = typeof(IAudioSessionManager2).GUID;
                object oMgr;
                if (dev.Activate(ref iidManager, 23, IntPtr.Zero, out oMgr) != 0 || oMgr == null)
                    return false;

                var mgr = (IAudioSessionManager2)oMgr;
                IAudioSessionEnumerator sessionEnum;
                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null)
                    return false;

                int count;
                sessionEnum.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    sessionEnum.GetSession(i, out ctl);
                    var ctl2 = ctl as IAudioSessionControl2;
                    var vol = ctl as ISimpleAudioVolume;
                    if (ctl2 != null && vol != null)
                    {
                        uint pId;
                        ctl2.GetProcessId(out pId);
                        if (pId == (uint)pid)
                        {
                            vol.GetMasterVolume(out volume);
                            vol.GetMute(out mute);
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
    #endregion

    #region Tray Wheel Volume Controller
    public class TrayWheelVolumeController : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private NotifyIcon _notifyIcon;
        private Action<int> _onVolumeDelta;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public Guid guidItem;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int Shell_NotifyIconGetRect([In] ref NOTIFYICONIDENTIFIER identifier, [Out] out RECT iconLocation);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private DateTime _lastMouseMoveTime = DateTime.MinValue;
        private System.Drawing.Point _lastMousePos = System.Drawing.Point.Empty;

        public TrayWheelVolumeController(NotifyIcon notifyIcon, Action<int> onVolumeDelta)
        {
            _notifyIcon = notifyIcon;
            _onVolumeDelta = onVolumeDelta;

            _notifyIcon.MouseMove += (s, e) =>
            {
                _lastMouseMoveTime = DateTime.Now;
                _lastMousePos = System.Windows.Forms.Cursor.Position;
            };

            _proc = HookCallback;
            using (var curProc = Process.GetCurrentProcess())
            using (var curMod = curProc.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curMod.ModuleName), 0);
            }
        }

        private System.Drawing.Rectangle GetIconRect()
        {
            try
            {
                var windowField = typeof(NotifyIcon).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var idField = typeof(NotifyIcon).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (windowField != null && idField != null)
                {
                    var window = (NativeWindow)windowField.GetValue(_notifyIcon);
                    int id = (int)idField.GetValue(_notifyIcon);
                    var nid = new NOTIFYICONIDENTIFIER
                    {
                        cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)),
                        hWnd = window.Handle,
                        uID = id
                    };
                    RECT rect;
                    if (Shell_NotifyIconGetRect(ref nid, out rect) == 0)
                    {
                        return new System.Drawing.Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                    }
                }
            }
            catch { }
            return System.Drawing.Rectangle.Empty;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool isOverIcon = false;

                var rect = GetIconRect();
                if (!rect.IsEmpty && rect.Contains(hookStruct.pt.x, hookStruct.pt.y))
                {
                    isOverIcon = true;
                }
                else if ((DateTime.Now - _lastMouseMoveTime).TotalMilliseconds < 800)
                {
                    if (Math.Abs(hookStruct.pt.x - _lastMousePos.X) <= 24 && Math.Abs(hookStruct.pt.y - _lastMousePos.Y) <= 24)
                    {
                        isOverIcon = true;
                    }
                }

                if (isOverIcon)
                {
                    short delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                    int step = (delta > 0) ? 4 : -4;
                    if (_onVolumeDelta != null)
                    {
                        _onVolumeDelta(step);
                    }
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
    #endregion

    public class Settings
    {
        public string DeviceName = "Xiaomi 15 Pro";
        public string DeviceIp = "192.168.31.239";
        public int Port = 5555;
        public string Target = "192.168.31.239:5555";
        public string Codec = "raw"; // "raw", "opus320", "opus128"
        public bool MutePhone = true;
        public bool AutoConnect = true;
        public bool ShowNotifications = true;
        public int MasterVolume = 100;
        public bool IsMuted = false;
        public bool SyncPhoneVolume = true;
        public bool MuteOnDisconnect = true;
        public bool HotkeyEnabled = true;
        public ModifierKeys HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
        public Key HotkeyKey = Key.W;

        public Dictionary<string, DeviceHotkeyBinding> DeviceHotkeys = new Dictionary<string, DeviceHotkeyBinding>(StringComparer.OrdinalIgnoreCase);

        public static string GetConfigPath()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dir, "WiFiAudioConnector.cfg");
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("DeviceName=" + DeviceName);
                sb.AppendLine("DeviceIp=" + DeviceIp);
                sb.AppendLine("Port=" + Port);
                sb.AppendLine("Target=" + Target);
                sb.AppendLine("Codec=" + Codec);
                sb.AppendLine("MutePhone=" + (MutePhone ? "1" : "0"));
                sb.AppendLine("AutoConnect=" + (AutoConnect ? "1" : "0"));
                sb.AppendLine("ShowNotifications=" + (ShowNotifications ? "1" : "0"));
                sb.AppendLine("MasterVolume=" + MasterVolume);
                sb.AppendLine("IsMuted=" + (IsMuted ? "1" : "0"));
                sb.AppendLine("SyncPhoneVolume=" + (SyncPhoneVolume ? "1" : "0"));
                sb.AppendLine("MuteOnDisconnect=" + (MuteOnDisconnect ? "1" : "0"));
                sb.AppendLine("HotkeyEnabled=" + (HotkeyEnabled ? "1" : "0"));
                sb.AppendLine("HotkeyModifiers=" + (int)HotkeyModifiers);
                sb.AppendLine("HotkeyKey=" + (int)HotkeyKey);

                foreach (var kvp in DeviceHotkeys)
                {
                    var b = kvp.Value;
                    sb.AppendLine(string.Format("DeviceHotkey={0}|{1}|{2}|{3}|{4}|{5}",
                        b.Target,
                        (b.DeviceName ?? "").Replace("|", "_"),
                        b.IsUsb ? "1" : "0",
                        (int)b.Modifiers,
                        (int)b.Key,
                        b.Enabled ? "1" : "0"));
                }

                File.WriteAllText(GetConfigPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new char[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            string k = parts[0].Trim();
                            string v = parts[1].Trim();
                            if (k == "DeviceName") s.DeviceName = v;
                            else if (k == "DeviceIp") s.DeviceIp = v;
                            else if (k == "Port") int.TryParse(v, out s.Port);
                            else if (k == "Target") s.Target = v;
                            else if (k == "Codec") s.Codec = v;
                            else if (k == "MutePhone") s.MutePhone = (v == "1");
                            else if (k == "AutoConnect") s.AutoConnect = (v == "1");
                            else if (k == "ShowNotifications") s.ShowNotifications = (v != "0");
                            else if (k == "MasterVolume") { int vInt; if (int.TryParse(v, out vInt)) s.MasterVolume = Math.Max(0, Math.Min(100, vInt)); }
                            else if (k == "IsMuted") s.IsMuted = (v == "1");
                            else if (k == "SyncPhoneVolume") s.SyncPhoneVolume = (v != "0");
                            else if (k == "MuteOnDisconnect") s.MuteOnDisconnect = (v != "0");
                            else if (k == "HotkeyEnabled") s.HotkeyEnabled = (v == "1");
                            else if (k == "HotkeyModifiers") { int m; if (int.TryParse(v, out m)) s.HotkeyModifiers = (ModifierKeys)m; }
                            else if (k == "HotkeyKey") { int kCode; if (int.TryParse(v, out kCode)) s.HotkeyKey = (Key)kCode; }
                            else if (k == "DeviceHotkey")
                            {
                                var segs = v.Split('|');
                                if (segs.Length >= 6)
                                {
                                    var b = new DeviceHotkeyBinding();
                                    b.Target = segs[0].Trim();
                                    b.DeviceName = segs[1].Trim();
                                    b.IsUsb = (segs[2].Trim() == "1");
                                    int m; if (int.TryParse(segs[3].Trim(), out m)) b.Modifiers = (ModifierKeys)m;
                                    int kCode; if (int.TryParse(segs[4].Trim(), out kCode)) b.Key = (Key)kCode;
                                    b.Enabled = (segs[5].Trim() == "1");
                                    s.DeviceHotkeys[b.Target] = b;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (s.DeviceHotkeys.Count == 0)
            {
                s.SeedDefaultHotkeys();
            }

            return s;
        }

        public void SeedDefaultHotkeys()
        {
            string wifiTarget = string.IsNullOrEmpty(DeviceIp) ? "192.168.31.239:5555" : string.Format("{0}:{1}", DeviceIp, Port);
            DeviceHotkeys[wifiTarget] = new DeviceHotkeyBinding
            {
                Target = wifiTarget,
                DeviceName = DeviceName,
                IsUsb = false,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Key = Key.W,
                Enabled = true
            };

            DeviceHotkeys["a22280af"] = new DeviceHotkeyBinding
            {
                Target = "a22280af",
                DeviceName = DeviceName,
                IsUsb = true,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Key = Key.U,
                Enabled = true
            };
        }
    }

    public class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int BASE_HOTKEY_ID = 9000;

        private class MessageWindow : NativeWindow, IDisposable
        {
            private readonly Action<int> _callback;
            public MessageWindow(Action<int> callback)
            {
                _callback = callback;
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (_callback != null)
                    {
                        _callback(id);
                    }
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }

        private MessageWindow _msgWin;
        private Dictionary<int, string> _idToTarget = new Dictionary<int, string>();
        private Dictionary<string, int> _targetToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _nextId = BASE_HOTKEY_ID;

        public event Action<string> HotkeyPressed;

        public HotkeyManager()
        {
            _msgWin = new MessageWindow(new Action<int>(OnMessageReceived));
        }

        private void OnMessageReceived(int id)
        {
            string target;
            if (_idToTarget.TryGetValue(id, out target))
            {
                if (HotkeyPressed != null)
                {
                    HotkeyPressed(target);
                }
            }
        }

        public bool Register(string target, ModifierKeys modifiers, Key key)
        {
            Unregister(target);

            if (key == Key.None || string.IsNullOrEmpty(target)) return false;

            uint fsMod = 0x4000; // MOD_NOREPEAT
            if ((modifiers & ModifierKeys.Alt) != 0) fsMod |= 0x0001;
            if ((modifiers & ModifierKeys.Control) != 0) fsMod |= 0x0002;
            if ((modifiers & ModifierKeys.Shift) != 0) fsMod |= 0x0004;
            if ((modifiers & ModifierKeys.Windows) != 0) fsMod |= 0x0008;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            int newId = ++_nextId;
            bool success = RegisterHotKey(_msgWin.Handle, newId, fsMod, vk);
            if (success)
            {
                _idToTarget[newId] = target;
                _targetToId[target] = newId;
            }
            return success;
        }

        public void Unregister(string target)
        {
            int id;
            if (_targetToId.TryGetValue(target, out id))
            {
                if (_msgWin != null && _msgWin.Handle != IntPtr.Zero)
                {
                    UnregisterHotKey(_msgWin.Handle, id);
                }
                _idToTarget.Remove(id);
                _targetToId.Remove(target);
            }
        }

        public void UnregisterAll()
        {
            if (_msgWin != null && _msgWin.Handle != IntPtr.Zero)
            {
                foreach (int id in _idToTarget.Keys)
                {
                    UnregisterHotKey(_msgWin.Handle, id);
                }
            }
            _idToTarget.Clear();
            _targetToId.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_msgWin != null)
            {
                _msgWin.Dispose();
                _msgWin = null;
            }
        }

        public static string FormatHotkey(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None) return "未设置";
            List<string> parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(KeyToString(key));
            return string.Join(" + ", parts.ToArray());
        }

        public static string KeyToString(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return "Num" + ((int)(key - Key.NumPad0)).ToString();
            return key.ToString();
        }
    }

    public class HotkeyConfigWindow : Window
    {
        private App _app;
        private List<DeviceHotkeyBinding> _bindingsList = new List<DeviceHotkeyBinding>();
        private ListBox _lbDevices;
        private DeviceHotkeyBinding _selectedBinding = null;

        private TextBlock _tbSelectedTitle;
        private TextBlock _tbHotkeyDisplay;
        private Border _hotkeyBorder;
        private CheckBox _cbEnable;
        private TextBlock _tbStatus;
        private bool _isRecording = false;
        private ModifierKeys _tempMod;
        private Key _tempKey;

        public HotkeyConfigWindow(App app)
        {
            _app = app;
            LoadBindings();
            BuildUI();
        }

        private void LoadBindings()
        {
            _bindingsList.Clear();
            foreach (var b in _app.CurrentSettings.DeviceHotkeys.Values)
            {
                _bindingsList.Add(new DeviceHotkeyBinding
                {
                    Target = b.Target,
                    DeviceName = b.DeviceName,
                    IsUsb = b.IsUsb,
                    Modifiers = b.Modifiers,
                    Key = b.Key,
                    Enabled = b.Enabled
                });
            }
        }

        private void BuildUI()
        {
            Width = 560;
            Height = 580;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = false;

            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && !_isRecording)
                {
                    DragMove();
                }
            };

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(248, 30, 32, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            var root = new StackPanel();

            // Header Row
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var closeBtn = new Button
            {
                Content = "×",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) => Close();
            DockPanel.SetDock(closeBtn, Dock.Right);

            var title = new TextBlock
            {
                Text = "⌨ 设备专属快捷键设置",
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(closeBtn);
            header.Children.Add(title);
            root.Children.Add(header);

            // Subtitle
            var desc = new TextBlock
            {
                Text = "支持为每台设备以及同一设备的「无线 Wi-Fi」与「有线 USB」分别绑定快捷键。\n无论正在运行什么全屏程序或游戏，按下专属快捷键即可一键直连或切断。",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(170, 185, 195, 210)),
                LineHeight = 16,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(desc);

            // Device List Card
            var listCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var listCardPanel = new StackPanel();

            var listHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnScan = new Button
            {
                Content = "🔄 扫描在线设备",
                FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(180, 50, 55, 68)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnScan.Click += (s, e) => ScanAndAddDevices();
            DockPanel.SetDock(btnScan, Dock.Right);

            var listTitle = new TextBlock
            {
                Text = "设备与连接模式列表 (单击选择要配置的项目):",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            listHeader.Children.Add(btnScan);
            listHeader.Children.Add(listTitle);
            listCardPanel.Children.Add(listHeader);

            _lbDevices = new ListBox
            {
                Height = 115,
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI, Consolas, Segoe UI"),
                Padding = new Thickness(4),
                ItemsSource = _bindingsList
            };
            _lbDevices.SelectionChanged += (s, e) => OnDeviceSelected();
            listCardPanel.Children.Add(_lbDevices);
            listCard.Child = listCardPanel;
            root.Children.Add(listCard);

            // Editor Card for selected item
            var editCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var editCardPanel = new StackPanel();

            _tbSelectedTitle = new TextBlock
            {
                Text = "当前未选中项目，请在上表中点击选择",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 190, 255)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            editCardPanel.Children.Add(_tbSelectedTitle);

            var boxLabel = new TextBlock
            {
                Text = "专属快捷键 (点击方框直接在键盘上按下新按键):",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            editCardPanel.Children.Add(boxLabel);

            _hotkeyBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Height = 36,
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = true,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _tbHotkeyDisplay = new TextBlock
            {
                Text = "请先选择设备",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _hotkeyBorder.Child = _tbHotkeyDisplay;

            _hotkeyBorder.MouseDown += (s, e) =>
            {
                if (_selectedBinding == null) return;
                _isRecording = true;
                _hotkeyBorder.Focus();
                _hotkeyBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 20, 120, 240));
                _tbHotkeyDisplay.Text = "▶ 请直接在键盘上按下快捷键...";
                _tbHotkeyDisplay.Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 180, 50));
            };

            _hotkeyBorder.LostFocus += (s, e) =>
            {
                if (_isRecording)
                {
                    _isRecording = false;
                    UpdateDisplay();
                }
            };

            _hotkeyBorder.PreviewKeyDown += OnBorderPreviewKeyDown;
            editCardPanel.Children.Add(_hotkeyBorder);

            // Presets
            var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            presetRow.Children.Add(new TextBlock
            {
                Text = "常用预设: ",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 200, 205, 215)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            Action<string, ModifierKeys, Key> addPreset = (name, m, k) =>
            {
                var btn = new Button
                {
                    Content = name,
                    FontSize = 10,
                    Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(0, 0, 5, 0),
                    Background = new SolidColorBrush(Color.FromArgb(160, 50, 55, 68)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btn.Click += (s, e) =>
                {
                    if (_selectedBinding == null) return;
                    _isRecording = false;
                    _tempMod = m;
                    _tempKey = k;
                    UpdateDisplay();
                };
                presetRow.Children.Add(btn);
            };

            addPreset("Ctrl+Shift+W (无线常用)", ModifierKeys.Control | ModifierKeys.Shift, Key.W);
            addPreset("Ctrl+Shift+U (有线常用)", ModifierKeys.Control | ModifierKeys.Shift, Key.U);
            addPreset("Ctrl+Alt+1", ModifierKeys.Control | ModifierKeys.Alt, Key.D1);
            addPreset("Ctrl+Alt+2", ModifierKeys.Control | ModifierKeys.Alt, Key.D2);
            addPreset("Ctrl+Alt+U", ModifierKeys.Control | ModifierKeys.Alt, Key.U);
            addPreset("F9", ModifierKeys.None, Key.F9);

            editCardPanel.Children.Add(presetRow);

            // Enable check + Apply buttons row
            var actRow = new DockPanel();
            _cbEnable = new CheckBox
            {
                Content = "启用此设备的专属快捷键",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center
            };

            var btnApplyToItem = new Button
            {
                Content = "✔ 确认设定",
                Width = 84,
                Height = 26,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 30, 140, 90)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnApplyToItem.Click += (s, e) => ApplyToSelectedItem();
            DockPanel.SetDock(btnApplyToItem, Dock.Right);

            var btnClearItem = new Button
            {
                Content = "清除按键",
                Width = 68,
                Height = 26,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(140, 70, 75, 88)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btnClearItem.Click += (s, e) =>
            {
                if (_selectedBinding == null) return;
                _tempMod = ModifierKeys.None;
                _tempKey = Key.None;
                UpdateDisplay();
                ApplyToSelectedItem();
            };
            DockPanel.SetDock(btnClearItem, Dock.Right);

            actRow.Children.Add(btnApplyToItem);
            actRow.Children.Add(btnClearItem);
            actRow.Children.Add(_cbEnable);
            editCardPanel.Children.Add(actRow);

            editCard.Child = editCardPanel;
            root.Children.Add(editCard);

            // Status Text
            _tbStatus = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 120)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(_tbStatus);

            // Bottom action buttons
            var bottomRow = new DockPanel();
            var btnSaveAll = new Button
            {
                Content = "💾 保存全部配置并生效",
                Width = 160,
                Height = 32,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 120, 240)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnSaveAll.Click += (s, e) => OnSaveAllClicked();
            DockPanel.SetDock(btnSaveAll, Dock.Right);

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 64,
                Height = 32,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(160, 50, 55, 68)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnCancel.Click += (s, e) => Close();
            DockPanel.SetDock(btnCancel, Dock.Right);

            bottomRow.Children.Add(btnSaveAll);
            bottomRow.Children.Add(btnCancel);
            root.Children.Add(bottomRow);

            mainBorder.Child = root;
            Content = mainBorder;

            if (_lbDevices.Items.Count > 0)
            {
                _lbDevices.SelectedIndex = 0;
            }
        }

        private async void ScanAndAddDevices()
        {
            _tbStatus.Text = "正在扫描局域网与USB设备...";
            _tbStatus.Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 180, 50));

            var scanned = await _app.ScanDevicesAsync();
            int addedCount = 0;
            foreach (var d in scanned)
            {
                if (string.IsNullOrEmpty(d.Target)) continue;
                bool exists = false;
                foreach (var b in _bindingsList)
                {
                    if (string.Equals(b.Target, d.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    var newBinding = new DeviceHotkeyBinding
                    {
                        Target = d.Target,
                        DeviceName = d.Name,
                        IsUsb = d.IsUsb,
                        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                        Key = d.IsUsb ? Key.U : Key.W,
                        Enabled = true
                    };
                    _bindingsList.Add(newBinding);
                    addedCount++;
                }
            }

            _lbDevices.ItemsSource = null;
            _lbDevices.ItemsSource = _bindingsList;
            if (_lbDevices.Items.Count > 0) _lbDevices.SelectedIndex = _lbDevices.Items.Count - 1;

            _tbStatus.Text = addedCount > 0 ? string.Format("✓ 扫描完成，新增了 {0} 个连接方式！", addedCount) : "✓ 扫描完成，已连接设备均在列表中。";
            _tbStatus.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 120));
        }

        private void OnDeviceSelected()
        {
            _selectedBinding = _lbDevices.SelectedItem as DeviceHotkeyBinding;
            if (_selectedBinding == null)
            {
                _tbSelectedTitle.Text = "当前未选中项目，请在上表中点击选择";
                _tbHotkeyDisplay.Text = "请先选择设备";
                return;
            }

            string modeTag = _selectedBinding.IsUsb ? "USB 有线直连" : "Wi-Fi 无线网络";
            _tbSelectedTitle.Text = string.Format("正在设置: {0} [{1}] ({2})", _selectedBinding.DeviceName, modeTag, _selectedBinding.Target);
            _tempMod = _selectedBinding.Modifiers;
            _tempKey = _selectedBinding.Key;
            _cbEnable.IsChecked = _selectedBinding.Enabled;
            UpdateDisplay();
        }

        private void OnBorderPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isRecording || _selectedBinding == null) return;

            e.Handled = true;
            Key key = e.Key;
            if (key == Key.System) key = e.SystemKey;

            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            if (key == Key.Escape)
            {
                _isRecording = false;
                UpdateDisplay();
                return;
            }

            _tempMod = Keyboard.Modifiers;
            _tempKey = key;
            _isRecording = false;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            _hotkeyBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
            _tbHotkeyDisplay.Text = HotkeyManager.FormatHotkey(_tempMod, _tempKey);
            _tbHotkeyDisplay.Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 200, 255));
            _tbStatus.Text = "";
        }

        private void ApplyToSelectedItem()
        {
            if (_selectedBinding == null) return;

            _selectedBinding.Modifiers = _tempMod;
            _selectedBinding.Key = _tempKey;
            _selectedBinding.Enabled = (_cbEnable.IsChecked == true);

            _lbDevices.Items.Refresh();

            _tbStatus.Text = string.Format("✓ 已更新 {0} 的快捷键为 [{1}]！点击底部「保存全部配置」生效",
                _selectedBinding.DisplayName, HotkeyManager.FormatHotkey(_tempMod, _tempKey));
            _tbStatus.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 200, 120));
        }

        private void OnSaveAllClicked()
        {
            if (_selectedBinding != null)
            {
                _selectedBinding.Modifiers = _tempMod;
                _selectedBinding.Key = _tempKey;
                _selectedBinding.Enabled = (_cbEnable.IsChecked == true);
            }

            _app.CurrentSettings.DeviceHotkeys.Clear();
            foreach (var b in _bindingsList)
            {
                _app.CurrentSettings.DeviceHotkeys[b.Target] = b;
            }
            _app.CurrentSettings.Save();

            _app.ApplyAllHotkeys();
            _app.RefreshFlyoutHotkey();

            _app.ShowNotification("设备快捷键已保存", "所有设备的专属全局快捷键已生效！");
            Close();
        }
    }

    public class App : Application
    {
        private static Mutex _mutex = null;
        private NotifyIcon _notifyIcon;
        private FlyoutWindow _flyout;
        private Settings _settings;
        private Process _scrcpyProc = null;
        private bool _isConnecting = false;
        private HotkeyManager _hotkeyManager = null;
        private HotkeyConfigWindow _hotkeyWin = null;
        private ToolStripMenuItem _notifyMenuItem = null;
        private TrayWheelVolumeController _trayWheelController = null;
        private Process _logcatProc = null;
        private int _phoneMaxVolume = 150;
        private DateTime _lastSliderSetTime = DateTime.MinValue;
        public DateTime LastSliderSetTime { get { return _lastSliderSetTime; } }

        private readonly object _phoneSyncLock = new object();
        private int _pendingPhoneIndex = -1;
        private bool _pendingPhoneMute = false;
        private bool _hasPendingPhoneSync = false;
        private bool _isPhoneSyncWorkerRunning = false;

        public static void LogLine(string s)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n");
            }
            catch { }
        }

        [STAThread]
        public static void Main()
        {
            try
            {
                bool createdNew;
                _mutex = new Mutex(true, "WiFiAudioConnector_Universal_Mutex", out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("WiFi 音频连接器已经在后台运行中！\n请查看桌面右下角系统托盘图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var app = new App();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.Run();
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString());
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                LogLine("UnhandledException: " + ev.ExceptionObject);
            };
            DispatcherUnhandledException += (s, ev) =>
            {
                LogLine("DispatcherUnhandledException: " + ev.Exception);
            };

            try
            {
                _settings = Settings.Load();
                InitTrayIcon();
                InitHotkey();
                _flyout = new FlyoutWindow(this);
                MainWindow = _flyout;

                if (_settings.AutoConnect)
                {
                    ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString());
            }
        }

        private void InitTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            UpdateTrayIcon(false);
            _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
            _notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("连接当前设备", null, (s, e) => ConnectAsync());
            menu.Items.Add("断开连接", null, (s, e) => Disconnect());
            menu.Items.Add("扫描局域网与USB设备", null, (s, e) =>
            {
                ShowFlyout();
                _flyout.TriggerScan();
            });
            menu.Items.Add("快捷键设置...", null, (s, e) => ShowHotkeyConfigWindow());
            menu.Items.Add(new ToolStripSeparator());

            var autoStartItem = new ToolStripMenuItem("开机自动连接");
            autoStartItem.Checked = _settings.AutoConnect;
            autoStartItem.Click += (s, e) =>
            {
                _settings.AutoConnect = !_settings.AutoConnect;
                autoStartItem.Checked = _settings.AutoConnect;
                _settings.Save();
                SetStartupRegistry(_settings.AutoConnect);
            };
            menu.Items.Add(autoStartItem);

            _notifyMenuItem = new ToolStripMenuItem("显示连接提示通知");
            _notifyMenuItem.Checked = _settings.ShowNotifications;
            _notifyMenuItem.Click += (s, e) =>
            {
                _settings.ShowNotifications = !_settings.ShowNotifications;
                _notifyMenuItem.Checked = _settings.ShowNotifications;
                _settings.Save();
                SyncNotificationState();
            };
            menu.Items.Add(_notifyMenuItem);

            menu.Items.Add("打开主面板", null, (s, e) => ShowFlyout());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApp());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleFlyout();
                }
            };
            _trayWheelController = new TrayWheelVolumeController(_notifyIcon, OnTrayWheelVolumeDelta);
        }

        private void OnTrayWheelVolumeDelta(int delta)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int cur = _settings.MasterVolume;
                int newVol = Math.Max(0, Math.Min(100, cur + delta));
                SetVolumeFromUI(newVol, false);
                if (_flyout != null)
                {
                    _flyout.UpdateVolumeUI(newVol, false);
                }
                string connTag = IsConnected ? "已连接" : "未连接";
                _notifyIcon.Text = string.Format("WiFi 音频连接器 - 音量: {0}% ({1})", newVol, connTag);
            }));
        }

        public void UpdateTrayIcon(bool connected)
        {
            try
            {
                string iconName = connected ? "app.ico" : "tray_disconnected.ico";
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
                if (File.Exists(iconPath))
                {
                    using (var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read))
                    {
                        _notifyIcon.Icon = new Icon(fs, new System.Drawing.Size(32, 32));
                    }
                    return;
                }
            }
            catch { }

            int size = 32;
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                var rect = new RectangleF(2, 2, size - 4, size - 4);
                using (var brush = new SolidBrush(connected ? System.Drawing.Color.FromArgb(20, 120, 240) : System.Drawing.Color.FromArgb(90, 95, 105)))
                {
                    g.FillEllipse(brush, rect);
                }

                using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    if (connected)
                    {
                        var pts = new System.Drawing.PointF[] {
                            new System.Drawing.PointF(10, 13),
                            new System.Drawing.PointF(14, 13),
                            new System.Drawing.PointF(18, 9),
                            new System.Drawing.PointF(18, 23),
                            new System.Drawing.PointF(14, 19),
                            new System.Drawing.PointF(10, 19)
                        };
                        using (var fillBrush = new SolidBrush(System.Drawing.Color.White))
                        {
                            g.FillPolygon(fillBrush, pts);
                        }
                        g.DrawArc(pen, 16, 12, 8, 8, -45, 90);
                        g.DrawArc(pen, 18, 9, 14, 14, -45, 90);
                    }
                    else
                    {
                        var pts = new System.Drawing.PointF[] {
                            new System.Drawing.PointF(11, 13),
                            new System.Drawing.PointF(14, 13),
                            new System.Drawing.PointF(17, 10),
                            new System.Drawing.PointF(17, 22),
                            new System.Drawing.PointF(14, 19),
                            new System.Drawing.PointF(11, 19)
                        };
                        using (var fillBrush = new SolidBrush(System.Drawing.Color.White))
                        {
                            g.FillPolygon(fillBrush, pts);
                        }
                        g.DrawLine(pen, 20, 13, 24, 19);
                        g.DrawLine(pen, 24, 13, 20, 19);
                    }
                }

                var iconHandle = bmp.GetHicon();
                var icon = Icon.FromHandle(iconHandle);
                _notifyIcon.Icon = icon;
            }
        }

        public bool IsConnected
        {
            get { return _scrcpyProc != null && !_scrcpyProc.HasExited; }
        }

        public Settings CurrentSettings { get { return _settings; } }

        public string FindToolPath(string exeName)
        {
            string localDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(localDir, exeName);
            if (File.Exists(localPath)) return localPath;

            string wingetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages\Genymobile.scrcpy_Microsoft.Winget.Source_8wekyb3d8bbwe\scrcpy-win64-v4.1");
            string wingetPath = Path.Combine(wingetDir, exeName);
            if (File.Exists(wingetPath)) return wingetPath;

            return exeName;
        }

        public async Task<List<DeviceItem>> ScanDevicesAsync()
        {
            var list = new List<DeviceItem>();
            string adbPath = FindToolPath("adb.exe");

            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "devices -l",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(3000);

                        string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("List of") || line.StartsWith("*")) continue;

                            var tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length >= 2 && tokens[1] == "device")
                            {
                                string serial = tokens[0];
                                if (serial.StartsWith("emulator-")) continue; // ignore local PC emulators

                                bool isTcp = serial.Contains(":");
                                string ip = "";
                                int port = 5555;
                                if (isTcp)
                                {
                                    var sp = serial.Split(':');
                                    ip = sp[0];
                                    if (sp.Length > 1) int.TryParse(sp[1], out port);
                                }

                                // Query friendly model name
                                string friendlyName = QueryDeviceName(adbPath, serial);
                                if (string.IsNullOrEmpty(friendlyName))
                                {
                                    // Parse model:xxx from line
                                    var match = Regex.Match(line, @"model:(\S+)");
                                    friendlyName = match.Success ? match.Groups[1].Value : serial;
                                }

                                list.Add(new DeviceItem
                                {
                                    Name = friendlyName,
                                    Target = serial,
                                    Ip = ip,
                                    Port = port,
                                    IsUsb = !isTcp,
                                    IsCustom = false
                                });
                            }
                        }
                    }
                }
                catch { }
            });

            return list;
        }

        private string QueryDeviceName(string adbPath, string serial)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = string.Format("-s {0} shell getprop ro.product.marketname", serial),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    string res = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(1500);
                    if (!string.IsNullOrEmpty(res)) return res;
                }

                // Fallback to ro.product.model
                psi.Arguments = string.Format("-s {0} shell getprop ro.product.model", serial);
                using (var p = Process.Start(psi))
                {
                    string res = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(1500);
                    if (!string.IsNullOrEmpty(res)) return res;
                }
            }
            catch { }
            return "";
        }

        public async Task<string> SwitchUsbToTcpipAsync(string usbSerial)
        {
            string adbPath = FindToolPath("adb.exe");
            string detectedIp = "";

            await Task.Run(() =>
            {
                try
                {
                    // 1. Query device WLAN IP
                    var psiIp = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = string.Format("-s {0} shell \"ip -o -4 addr show wlan0\"", usbSerial),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var p = Process.Start(psiIp))
                    {
                        string outIp = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(2000);
                        var match = Regex.Match(outIp, @"inet\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                        if (match.Success)
                        {
                            detectedIp = match.Groups[1].Value;
                        }
                    }

                    // 2. adb tcpip 5555
                    var psiTcp = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = string.Format("-s {0} tcpip 5555", usbSerial),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var p = Process.Start(psiTcp))
                    {
                        p.WaitForExit(3000);
                    }

                    // 3. connect if IP found
                    if (!string.IsNullOrEmpty(detectedIp))
                    {
                        Thread.Sleep(1000);
                        var psiConn = new ProcessStartInfo
                        {
                            FileName = adbPath,
                            Arguments = "connect " + detectedIp + ":5555",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (var p = Process.Start(psiConn))
                        {
                            p.WaitForExit(3000);
                        }
                    }
                }
                catch { }
            });

            return detectedIp;
        }

        public string CurrentActiveTarget { get { return _currentActiveTarget; } }
        private string _currentActiveTarget = null;

        public async void ConnectAsync(string specificTarget = null)
        {
            if (IsConnected || _isConnecting) return;
            _isConnecting = true;
            _flyout.UpdateState(ConnectionState.Connecting);
            _notifyIcon.Text = "WiFi 音频连接器 (正在连接...)";

            string adbPath = FindToolPath("adb.exe");
            string scrcpyPath = FindToolPath("scrcpy.exe");

            if (string.IsNullOrEmpty(adbPath) || string.IsNullOrEmpty(scrcpyPath))
            {
                _isConnecting = false;
                _flyout.UpdateState(ConnectionState.Disconnected);
                MessageBox.Show("未能找到 scrcpy 或 adb 组件！请确保程序目录下存在 scrcpy.exe 与 adb.exe。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string target = !string.IsNullOrEmpty(specificTarget) ? specificTarget : _settings.Target;
            if (string.IsNullOrEmpty(target) || (target.Contains(".") && !target.Contains(":")))
            {
                target = string.Format("{0}:{1}", _settings.DeviceIp, _settings.Port);
                _settings.Target = target;
            }

            bool isTcp = target.Contains(":");

            bool ok = await Task.Run<bool>(() =>
            {
                try
                {
                    // 1. adb connect if TCP/IP
                    if (isTcp)
                    {
                        var psiAdb = new ProcessStartInfo
                        {
                            FileName = adbPath,
                            Arguments = "connect " + target,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (var p = Process.Start(psiAdb))
                        {
                            p.WaitForExit(4000);
                        }
                    }

                    // 2. build scrcpy arguments
                    string codecArg = "--audio-codec=raw";
                    if (_settings.Codec == "opus320") codecArg = "--audio-codec=opus --audio-bit-rate=320K";
                    else if (_settings.Codec == "opus128") codecArg = "--audio-codec=opus --audio-bit-rate=128K";

                    string modeArg = _settings.MutePhone ? "--audio-source=playback" : "--audio-source=playback --audio-dup";
                    string scrcpyArgs = string.Format("-s {0} --no-video --no-window {1} {2} --audio-buffer=50", target, codecArg, modeArg);

                    var psiScrcpy = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = scrcpyArgs,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    _scrcpyProc = Process.Start(psiScrcpy);
                    Thread.Sleep(1500);

                    return _scrcpyProc != null && !_scrcpyProc.HasExited;
                }
                catch (Exception ex)
                {
                    LogLine("Connect Exception: " + ex);
                    return false;
                }
            });

            _isConnecting = false;

            if (ok)
            {
                _currentActiveTarget = target;
                UpdateTrayIcon(true);
                string desc = _settings.Codec == "raw" ? "Raw PCM 无损" : "Opus 320K";
                string modeTag = isTcp ? "Wi-Fi 无线" : "USB 有线";
                _notifyIcon.Text = string.Format("WiFi 音频连接器 - {0} [{1}] (已连接)", _settings.DeviceName, modeTag);
                ShowNotification("设备已连接", string.Format("{0} [{1}]\n音频流已就绪 ({2})，直通电脑播放", _settings.DeviceName, modeTag, desc), ToolTipIcon.Info);
                _flyout.UpdateState(ConnectionState.Connected);
                StartPhoneVolumeSync(target, _scrcpyProc.Id);

                // Watchdog task
                Task.Run(() =>
                {
                    try
                    {
                        _scrcpyProc.WaitForExit();
                    }
                    catch { }

                    string targetToMute = _currentActiveTarget;
                    _currentActiveTarget = null;
                    StopPhoneVolumeSync();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateTrayIcon(false);
                        _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
                        _flyout.UpdateState(ConnectionState.Disconnected);
                    }));

                    if (_settings.SyncPhoneVolume && _settings.MuteOnDisconnect && !string.IsNullOrEmpty(targetToMute))
                    {
                        MutePhoneMedia(targetToMute);
                    }
                });
            }
            else
            {
                _currentActiveTarget = null;
                UpdateTrayIcon(false);
                _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
                _flyout.UpdateState(ConnectionState.Disconnected);
                ShowNotification("连接失败", "无法连接到设备，请确认手机已开机且处于连接状态", ToolTipIcon.Error);
            }
        }

        public void Disconnect()
        {
            string targetToMute = _currentActiveTarget;
            _currentActiveTarget = null;
            StopPhoneVolumeSync();

            if (_settings.SyncPhoneVolume && _settings.MuteOnDisconnect && !string.IsNullOrEmpty(targetToMute))
            {
                MutePhoneMedia(targetToMute);
            }

            try
            {
                if (_scrcpyProc != null && !_scrcpyProc.HasExited)
                {
                    _scrcpyProc.Kill();
                }
            }
            catch { }
            _scrcpyProc = null;
            UpdateTrayIcon(false);
            _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
            _flyout.UpdateState(ConnectionState.Disconnected);
        }

        public void ToggleFlyout()
        {
            if (_flyout.IsVisible)
            {
                _flyout.Hide();
            }
            else
            {
                ShowFlyout();
            }
        }

        public void ShowFlyout()
        {
            PositionFlyoutAboveTray();
            if (_flyout != null)
            {
                _flyout.SyncNotificationCheckbox();
            }
            _flyout.Show();
            _flyout.Activate();
        }

        private void PositionFlyoutAboveTray()
        {
            var workingArea = Screen.PrimaryScreen.WorkingArea;
            _flyout.Left = workingArea.Right - _flyout.Width - 12;
            _flyout.Top = workingArea.Bottom - _flyout.Height - 12;
        }

        private void SetStartupRegistry(bool enable)
        {
            try
            {
                string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (enable)
                    {
                        string appPath = Process.GetCurrentProcess().MainModule.FileName;
                        k.SetValue("WiFiAudioConnector", "\"" + appPath + "\"");
                    }
                    else
                    {
                        k.DeleteValue("WiFiAudioConnector", false);
                    }
                }
            }
            catch { }
        }

        private void InitHotkey()
        {
            try
            {
                _hotkeyManager = new HotkeyManager();
                _hotkeyManager.HotkeyPressed += OnDeviceHotkeyPressed;
                ApplyAllHotkeys();
            }
            catch (Exception ex)
            {
                LogLine("InitHotkey Exception: " + ex);
            }
        }

        public void ApplyAllHotkeys()
        {
            if (_hotkeyManager == null) return;
            _hotkeyManager.UnregisterAll();

            foreach (var b in _settings.DeviceHotkeys.Values)
            {
                if (b.Enabled && b.Key != Key.None && !string.IsNullOrEmpty(b.Target))
                {
                    bool ok = _hotkeyManager.Register(b.Target, b.Modifiers, b.Key);
                    LogLine(string.Format("Registered hotkey for {0} ({1}): {2} -> {3}",
                        b.DisplayName, b.Target, HotkeyManager.FormatHotkey(b.Modifiers, b.Key), ok));
                }
            }
        }

        private void OnDeviceHotkeyPressed(string target)
        {
            if (_isConnecting) return;

            DeviceHotkeyBinding binding = null;
            _settings.DeviceHotkeys.TryGetValue(target, out binding);
            string devName = (binding != null) ? binding.DisplayName : target;

            // If currently connected to THIS exact target -> Toggle Disconnect!
            if (IsConnected && string.Equals(_currentActiveTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                Disconnect();
                ShowNotification("快捷键已触发", string.Format("已断开: {0}", devName), ToolTipIcon.Info);
            }
            else
            {
                // If currently connected to another target/mode -> Disconnect first then switch!
                if (IsConnected)
                {
                    Disconnect();
                    Thread.Sleep(300);
                }

                _settings.Target = target;
                if (binding != null)
                {
                    _settings.DeviceName = binding.DeviceName;
                    if (!binding.IsUsb && target.Contains(":"))
                    {
                        var sp = target.Split(':');
                        _settings.DeviceIp = sp[0];
                        if (sp.Length > 1) int.TryParse(sp[1], out _settings.Port);
                    }
                }
                _settings.Save();

                if (_flyout != null)
                {
                    _flyout.SyncCurrentDeviceToUI();
                }

                ShowNotification("快捷键已触发", string.Format("正在快速直连: {0}...", devName), ToolTipIcon.Info);
                ConnectAsync(target);
            }
        }

        public void ShowHotkeyConfigWindow()
        {
            if (_hotkeyWin != null && _hotkeyWin.IsLoaded)
            {
                _hotkeyWin.Activate();
                return;
            }
            _hotkeyWin = new HotkeyConfigWindow(this);
            _hotkeyWin.Closed += (s, e) => { _hotkeyWin = null; };
            _hotkeyWin.Show();
            _hotkeyWin.Activate();
        }

        public void RefreshFlyoutHotkey()
        {
            if (_flyout != null)
            {
                _flyout.UpdateHotkeyText();
            }
        }

        public void SyncNotificationState()
        {
            if (_notifyMenuItem != null)
            {
                _notifyMenuItem.Checked = _settings.ShowNotifications;
            }
            if (_flyout != null)
            {
                _flyout.SyncNotificationCheckbox();
            }
        }

        public void ShowNotification(string title, string msg)
        {
            ShowNotification(title, msg, ToolTipIcon.Info);
        }

        public void ShowNotification(string title, string msg, ToolTipIcon icon)
        {
            if (!_settings.ShowNotifications) return;
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(2000, title, msg, icon);
            }
        }

        public void StartPhoneVolumeSync(string target, int scrcpyPid)
        {
            StopPhoneVolumeSync();

            Task.Run(() =>
            {
                try
                {
                    string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");

                    // 1. Query initial volume & max range
                    var psiGet = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = string.Format("-s {0} shell cmd media_session volume --stream 3 --get", target),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    using (var p = Process.Start(psiGet))
                    {
                        string outStr = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(2000);
                        var m = Regex.Match(outStr, @"volume is (\d+) in range \[(\d+)\.\.(\d+)\]");
                        if (m.Success)
                        {
                            int cur = int.Parse(m.Groups[1].Value);
                            int max = int.Parse(m.Groups[3].Value);
                            if (max > 0) _phoneMaxVolume = max;

                            // Always preserve and sync the last saved MasterVolume to phone
                            if (_settings.SyncPhoneVolume)
                            {
                                int targetIndex = (int)Math.Round((_settings.MasterVolume / 100.0f) * _phoneMaxVolume);
                                string cmdArgs;
                                if (_settings.IsMuted)
                                {
                                    cmdArgs = string.Format("-s {0} shell \"cmd audio set-volume 3 0; cmd audio adj-mute 3; cmd media_session volume --stream 3 --set 0\"", target);
                                }
                                else
                                {
                                    cmdArgs = string.Format("-s {0} shell \"cmd audio adj-unmute 3; cmd audio set-volume 3 {1}; cmd media_session volume --stream 3 --set {1}\"", target, targetIndex);
                                }
                                var psiRestore = new ProcessStartInfo
                                {
                                    FileName = adbPath,
                                    Arguments = cmdArgs,
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };
                                using (var pRestore = Process.Start(psiRestore)) { pRestore.WaitForExit(1500); }
                            }
                            _lastSliderSetTime = DateTime.Now;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_flyout != null) _flyout.UpdateVolumeUI(_settings.MasterVolume, _settings.IsMuted);
                            }));
                        }
                    }

                    // Apply to scrcpy session
                    float initialRatio = _settings.MasterVolume / 100.0f;
                    for (int i = 0; i < 6; i++)
                    {
                        if (WindowsAudioSessionController.SetProcessVolume(scrcpyPid, initialRatio, _settings.IsMuted))
                            break;
                        Thread.Sleep(300);
                    }

                    // Clear logcat buffer so past mute events are never replayed
                    try
                    {
                        var psiClear = new ProcessStartInfo
                        {
                            FileName = adbPath,
                            Arguments = string.Format("-s {0} shell logcat -c", target),
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var pClear = Process.Start(psiClear)) { pClear.WaitForExit(1000); }
                    }
                    catch { }

                    // 2. Start streaming logcat for real-time volume key events (-T 1 ensures only new events)
                    var psiLogcat = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = string.Format("-s {0} shell logcat -T 1 -v raw -s vol.Events:I VolumeSliderController:D", target),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    _logcatProc = Process.Start(psiLogcat);
                    _lastSliderSetTime = DateTime.Now;
                    DateTime connectStartTime = DateTime.Now;

                    // Companion polling task for non-key volume changes
                    Task.Run(() =>
                    {
                        while (_logcatProc != null && !_logcatProc.HasExited)
                        {
                            Thread.Sleep(1500);
                            if (!_settings.SyncPhoneVolume) continue;
                            if ((DateTime.Now - connectStartTime).TotalMilliseconds < 3500) continue;
                            if ((DateTime.Now - _lastSliderSetTime).TotalMilliseconds < 2000) continue;

                            try
                            {
                                var psiPoll = new ProcessStartInfo
                                {
                                    FileName = adbPath,
                                    Arguments = string.Format("-s {0} shell cmd media_session volume --stream 3 --get", target),
                                    CreateNoWindow = true,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true
                                };
                                using (var p = Process.Start(psiPoll))
                                {
                                    string outStr = p.StandardOutput.ReadToEnd();
                                    p.WaitForExit(1000);
                                    var m = Regex.Match(outStr, @"volume is (\d+) in range \[(\d+)\.\.(\d+)\]");
                                    if (m.Success)
                                    {
                                        int cur = int.Parse(m.Groups[1].Value);
                                        int pct = (int)Math.Round((float)cur * 100 / _phoneMaxVolume);
                                        pct = Math.Max(0, Math.Min(100, pct));
                                        if (pct == 0 && _settings.MasterVolume > 15) continue;
                                        if (Math.Abs(pct - _settings.MasterVolume) >= 2)
                                        {
                                            _settings.MasterVolume = pct;
                                            if (_scrcpyProc != null && !_scrcpyProc.HasExited)
                                            {
                                                WindowsAudioSessionController.SetProcessVolume(_scrcpyProc.Id, pct / 100.0f, _settings.IsMuted);
                                            }
                                            Dispatcher.BeginInvoke(new Action(() =>
                                            {
                                                if (_flyout != null) _flyout.UpdateVolumeUI(pct, _settings.IsMuted);
                                            }));
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    });

                    string line;
                    while (_logcatProc != null && !_logcatProc.HasExited && (line = _logcatProc.StandardOutput.ReadLine()) != null)
                    {
                        if (!_settings.SyncPhoneVolume) continue;
                        if ((DateTime.Now - connectStartTime).TotalMilliseconds < 3500) continue;
                        if ((DateTime.Now - _lastSliderSetTime).TotalMilliseconds < 1500) continue;

                        if (line.Contains("STREAM_MUSIC"))
                        {
                            var m = Regex.Match(line, @"STREAM_MUSIC\s+(\d+)");
                            if (m.Success)
                            {
                                int val = int.Parse(m.Groups[1].Value);
                                int pct = (int)Math.Round((float)val * 100 / _phoneMaxVolume);
                                pct = Math.Max(0, Math.Min(100, pct));
                                if (pct == 0 && _settings.MasterVolume > 15) continue;

                                _settings.MasterVolume = pct;
                                if (_scrcpyProc != null && !_scrcpyProc.HasExited)
                                {
                                    WindowsAudioSessionController.SetProcessVolume(_scrcpyProc.Id, pct / 100.0f, _settings.IsMuted);
                                }
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (_flyout != null) _flyout.UpdateVolumeUI(pct, _settings.IsMuted);
                                }));
                            }
                        }
                    }
                }
                catch { }
            });
        }

        public void StopPhoneVolumeSync()
        {
            try
            {
                if (_logcatProc != null && !_logcatProc.HasExited)
                {
                    _logcatProc.Kill();
                }
            }
            catch { }
            _logcatProc = null;
        }

        public void QueuePhoneVolumeSync(float ratio, bool isMuted)
        {
            int targetIndex = (int)Math.Round(ratio * _phoneMaxVolume);
            lock (_phoneSyncLock)
            {
                _pendingPhoneIndex = targetIndex;
                _pendingPhoneMute = isMuted;
                _hasPendingPhoneSync = true;
                if (_isPhoneSyncWorkerRunning) return;
                _isPhoneSyncWorkerRunning = true;
            }

            Task.Run(() =>
            {
                while (true)
                {
                    int indexToSync;
                    bool muteToSync;
                    lock (_phoneSyncLock)
                    {
                        if (!_hasPendingPhoneSync)
                        {
                            _isPhoneSyncWorkerRunning = false;
                            break;
                        }
                        indexToSync = _pendingPhoneIndex;
                        muteToSync = _pendingPhoneMute;
                        _hasPendingPhoneSync = false;
                    }

                    try
                    {
                        string target = _currentActiveTarget;
                        if (string.IsNullOrEmpty(target) || !IsConnected) break;
                        string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
                        string cmdArgs;
                        if (muteToSync)
                        {
                            cmdArgs = string.Format("-s {0} shell \"cmd audio set-volume 3 0; cmd audio adj-mute 3; cmd media_session volume --stream 3 --set 0\"", target);
                        }
                        else
                        {
                            cmdArgs = string.Format("-s {0} shell \"cmd audio adj-unmute 3; cmd audio set-volume 3 {1}; cmd media_session volume --stream 3 --set {1}\"", target, indexToSync);
                        }
                        var psi = new ProcessStartInfo
                        {
                            FileName = adbPath,
                            Arguments = cmdArgs,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var p = Process.Start(psi))
                        {
                            p.WaitForExit(1000);
                        }
                    }
                    catch { }

                    _lastSliderSetTime = DateTime.Now;
                    Thread.Sleep(50);
                }
            });
        }

        public void SetVolumeFromUI(int volumePercent, bool isMuted)
        {
            _settings.MasterVolume = volumePercent;
            _settings.IsMuted = isMuted;
            _settings.Save();
            _lastSliderSetTime = DateTime.Now;

            float ratio = volumePercent / 100.0f;
            if (_scrcpyProc != null && !_scrcpyProc.HasExited)
            {
                WindowsAudioSessionController.SetProcessVolume(_scrcpyProc.Id, ratio, isMuted);
            }

            // Sync to phone through serial throttled queue
            if (_settings.SyncPhoneVolume && IsConnected && !string.IsNullOrEmpty(_currentActiveTarget))
            {
                QueuePhoneVolumeSync(ratio, isMuted);
            }
        }

        public void MutePhoneMedia(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            try
            {
                string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
                // 1. Direct AudioManager stream 3 volume 0 and mute
                // 2. Fallback to cmd media_session
                // 3. Dispatch media session pause and KEYCODE_MEDIA_PAUSE (127)
                string shellCmd = "cmd audio set-volume 3 0; cmd audio adj-mute 3; cmd media_session volume --stream 3 --set 0; cmd media_session dispatch pause; input keyevent 127";
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = string.Format("-s {0} shell \"{1}\"", target, shellCmd),
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(1500);
                }
            }
            catch { }
        }

        public void ExitApp()
        {
            Disconnect();
            if (_trayWheelController != null)
            {
                _trayWheelController.Dispose();
                _trayWheelController = null;
            }
            if (_hotkeyManager != null)
            {
                _hotkeyManager.Dispose();
                _hotkeyManager = null;
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Environment.Exit(0);
        }
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public class FlyoutWindow : Window
    {
        private App _app;
        private Border _statusBadge;
        private TextBlock _statusText;
        private Button _btnConnect;
        private ComboBox _cbDevices;
        private Button _btnScan;
        private Button _btnSwitchUsb;
        private RadioButton _rbRaw;
        private RadioButton _rbOpus320;
        private RadioButton _rbOpus128;
        private CheckBox _cbMutePhone;
        private CheckBox _cbAutoConnect;
        private CheckBox _cbNotifications;
        private Slider _sliderVolume;
        private TextBlock _txtVolumePercent;
        private Button _btnMute;
        private CheckBox _cbSyncPhoneVolume;
        private CheckBox _cbMuteOnDisconnect;
        private bool _isUpdatingVolumeUI = false;
        private bool _isUserDragging = false;
        private TextBox _tbIp;
        private TextBox _tbPort;
        private TextBlock _txtHotkeyDisplay;
        private List<DeviceItem> _deviceList = new List<DeviceItem>();

        public FlyoutWindow(App app)
        {
            _app = app;
            BuildUI();
            Loaded += (s, e) => TriggerScan();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void BuildUI()
        {
            Width = 370;
            Height = 705;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            Deactivated += (s, e) => Hide();

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(246, 30, 32, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.45,
                    Color = Colors.Black
                }
            };

            var root = new StackPanel();

            // Header Title
            var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

            string appPngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.png");
            if (File.Exists(appPngPath))
            {
                try
                {
                    var iconImg = new System.Windows.Controls.Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(appPngPath)),
                        Width = 24,
                        Height = 24,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    DockPanel.SetDock(iconImg, Dock.Left);
                    headerPanel.Children.Add(iconImg);
                }
                catch { }
            }

            var titleText = new TextBlock
            {
                Text = "手机无线音频连接器",
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            var closeBtn = new Button
            {
                Content = "×",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) => Hide();
            DockPanel.SetDock(closeBtn, Dock.Right);
            headerPanel.Children.Add(closeBtn);
            headerPanel.Children.Add(titleText);
            root.Children.Add(headerPanel);

            // Device Card
            var deviceCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var devicePanel = new StackPanel();

            var row1 = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var devTitle = new TextBlock
            {
                Text = "选择音频推流设备",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _statusText = new TextBlock
            {
                Text = "未连接",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.White
            };
            _statusBadge.Child = _statusText;
            DockPanel.SetDock(_statusBadge, Dock.Right);
            row1.Children.Add(_statusBadge);
            row1.Children.Add(devTitle);
            devicePanel.Children.Add(row1);

            // Device Dropdown + Scan Button Row
            var comboRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            _btnScan = new Button
            {
                Content = "🔄 扫描",
                Width = 60,
                Height = 26,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(180, 50, 55, 68)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0)
            };
            _btnScan.Click += (s, e) => TriggerScan();
            DockPanel.SetDock(_btnScan, Dock.Right);

            _cbDevices = new ComboBox
            {
                Height = 26,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(200, 25, 27, 32)),
                Foreground = System.Windows.Media.Brushes.Black
            };
            _cbDevices.SelectionChanged += OnDeviceSelectionChanged;

            comboRow.Children.Add(_btnScan);
            comboRow.Children.Add(_cbDevices);
            devicePanel.Children.Add(comboRow);

            // Switch USB to TCP/IP button
            _btnSwitchUsb = new Button
            {
                Content = "⚡ 将此 USB 设备一键切换为无线 Wi-Fi 模式",
                Height = 24,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(200, 30, 140, 90)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            _btnSwitchUsb.Click += async (s, e) =>
            {
                var sel = _cbDevices.SelectedItem as DeviceItem;
                if (sel != null && sel.IsUsb)
                {
                    _btnSwitchUsb.Content = "正在开启无线模式...";
                    string ip = await _app.SwitchUsbToTcpipAsync(sel.Target);
                    if (!string.IsNullOrEmpty(ip))
                    {
                        MessageBox.Show(string.Format("已成功为 {0} 开启无线模式！\nIP地址: {1}:5555\n现在可以拔掉 USB 数据线了！", sel.Name, ip), "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    TriggerScan();
                }
            };
            devicePanel.Children.Add(_btnSwitchUsb);

            // Target IP:Port row
            var ipRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var ipLabel = new TextBlock
            {
                Text = "连接目标:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 200, 205, 215)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _tbPort = new TextBox
            {
                Text = _app.CurrentSettings.Port.ToString(),
                Width = 52,
                Height = 22,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(200, 25, 27, 32)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            _tbPort.TextChanged += (s, e) =>
            {
                int p;
                if (int.TryParse(_tbPort.Text, out p))
                {
                    _app.CurrentSettings.Port = p;
                    _app.CurrentSettings.Target = string.Format("{0}:{1}", _app.CurrentSettings.DeviceIp, p);
                    _app.CurrentSettings.Save();
                }
            };

            var colon = new TextBlock
            {
                Text = " : ",
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(_tbPort, Dock.Right);
            DockPanel.SetDock(colon, Dock.Right);

            _tbIp = new TextBox
            {
                Text = _app.CurrentSettings.DeviceIp,
                Width = 115,
                Height = 22,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(200, 25, 27, 32)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            _tbIp.TextChanged += (s, e) =>
            {
                _app.CurrentSettings.DeviceIp = _tbIp.Text.Trim();
                _app.CurrentSettings.Target = string.Format("{0}:{1}", _tbIp.Text.Trim(), _app.CurrentSettings.Port);
                _app.CurrentSettings.Save();
            };
            DockPanel.SetDock(_tbIp, Dock.Right);

            ipRow.Children.Add(_tbPort);
            ipRow.Children.Add(colon);
            ipRow.Children.Add(_tbIp);
            ipRow.Children.Add(ipLabel);
            devicePanel.Children.Add(ipRow);

            // Connect button
            _btnConnect = new Button
            {
                Content = "一键连接",
                Height = 34,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(255, 20, 120, 240)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _btnConnect.Click += (s, e) =>
            {
                if (_app.IsConnected) _app.Disconnect();
                else _app.ConnectAsync();
            };
            devicePanel.Children.Add(_btnConnect);

            deviceCard.Child = devicePanel;
            root.Children.Add(deviceCard);

            // Volume Control Card
            var volumeCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var volumePanel = new StackPanel();

            var volHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var lblVolTitle = new TextBlock
            {
                Text = "音频输出音量",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(lblVolTitle, Dock.Left);
            volHeader.Children.Add(lblVolTitle);

            _txtVolumePercent = new TextBlock
            {
                Text = _app.CurrentSettings.IsMuted ? "静音" : (_app.CurrentSettings.MasterVolume + "%"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 150, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtVolumePercent, Dock.Right);
            volHeader.Children.Add(_txtVolumePercent);
            volumePanel.Children.Add(volHeader);

            var sliderRow = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
            _btnMute = new Button
            {
                Content = _app.CurrentSettings.IsMuted ? "🔇" : "🔊",
                FontSize = 13,
                Width = 28,
                Height = 26,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _btnMute.Click += (s, e) =>
            {
                bool newMute = !_app.CurrentSettings.IsMuted;
                _btnMute.Content = newMute ? "🔇" : "🔊";
                int val = (int)Math.Round(_sliderVolume.Value);
                _txtVolumePercent.Text = newMute ? "静音" : (val + "%");
                _app.SetVolumeFromUI(val, newMute);
            };
            DockPanel.SetDock(_btnMute, Dock.Left);
            sliderRow.Children.Add(_btnMute);

            string sliderXaml = @"<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Slider"">
  <Setter Property=""IsMoveToPointEnabled"" Value=""True""/>
  <Setter Property=""Height"" Value=""26""/>
  <Setter Property=""Background"" Value=""Transparent""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""Slider"">
        <Grid VerticalAlignment=""Center"">
          <Track x:Name=""PART_Track"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""{x:Static Slider.DecreaseLarge}"">
                <RepeatButton.Template>
                  <ControlTemplate TargetType=""RepeatButton"">
                    <Border Height=""6"" CornerRadius=""3,0,0,3"" Background=""#3B82F6""/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""{x:Static Slider.IncreaseLarge}"">
                <RepeatButton.Template>
                  <ControlTemplate TargetType=""RepeatButton"">
                    <Border Height=""6"" CornerRadius=""0,3,3,0"" Background=""#2E3342""/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb Focusable=""False"">
                <Thumb.Template>
                  <ControlTemplate TargetType=""Thumb"">
                    <Grid Width=""16"" Height=""16"">
                      <Ellipse Fill=""#FFFFFF"">
                        <Ellipse.Effect>
                          <DropShadowEffect BlurRadius=""6"" ShadowDepth=""1"" Opacity=""0.45"" Color=""#000000""/>
                        </Ellipse.Effect>
                      </Ellipse>
                      <Ellipse Width=""8"" Height=""8"" Fill=""#2563EB""/>
                    </Grid>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

            _sliderVolume = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = _app.CurrentSettings.MasterVolume,
                IsMoveToPointEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            try
            {
                _sliderVolume.Style = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse(sliderXaml);
            }
            catch { }

            _sliderVolume.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent, new System.Windows.Controls.Primitives.DragStartedEventHandler((s, e) => { _isUserDragging = true; }));
            _sliderVolume.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, e) => { _isUserDragging = false; }));
            _sliderVolume.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _isUserDragging = true;
                System.Windows.Point pt = e.GetPosition(_sliderVolume);
                double w = _sliderVolume.ActualWidth;
                if (w > 16)
                {
                    double clickX = pt.X - 8;
                    double ratio = Math.Max(0.0, Math.Min(1.0, clickX / (w - 16)));
                    int targetVal = (int)Math.Round(ratio * 100.0);
                    _sliderVolume.Value = targetVal;
                }
            };
            _sliderVolume.PreviewMouseUp += (s, e) => { _isUserDragging = false; };
            _sliderVolume.ValueChanged += (s, e) =>
            {
                if (_isUpdatingVolumeUI) return;
                int val = (int)Math.Round(_sliderVolume.Value);
                _txtVolumePercent.Text = _app.CurrentSettings.IsMuted ? "静音" : (val + "%");
                _app.SetVolumeFromUI(val, _app.CurrentSettings.IsMuted);
            };
            sliderRow.Children.Add(_sliderVolume);
            volumePanel.Children.Add(sliderRow);

            _cbSyncPhoneVolume = new CheckBox
            {
                Content = "手机按键实时联动 (按手机物理音量键调节电脑声音)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                IsChecked = _app.CurrentSettings.SyncPhoneVolume
            };

            _cbMuteOnDisconnect = new CheckBox
            {
                Content = "└ 仅开启联动时生效：断开后手机自动静音 (防声音外放)",
                Foreground = new SolidColorBrush(Color.FromArgb(220, 210, 220, 235)),
                FontSize = 10.5,
                Margin = new Thickness(14, 4, 0, 0),
                IsChecked = _app.CurrentSettings.MuteOnDisconnect,
                IsEnabled = _app.CurrentSettings.SyncPhoneVolume
            };

            _cbSyncPhoneVolume.Checked += (s, e) =>
            {
                _app.CurrentSettings.SyncPhoneVolume = true;
                _app.CurrentSettings.Save();
                if (_cbMuteOnDisconnect != null) _cbMuteOnDisconnect.IsEnabled = true;
            };
            _cbSyncPhoneVolume.Unchecked += (s, e) =>
            {
                _app.CurrentSettings.SyncPhoneVolume = false;
                _app.CurrentSettings.Save();
                if (_cbMuteOnDisconnect != null) _cbMuteOnDisconnect.IsEnabled = false;
            };

            _cbMuteOnDisconnect.Checked += (s, e) =>
            {
                _app.CurrentSettings.MuteOnDisconnect = true;
                _app.CurrentSettings.Save();
            };
            _cbMuteOnDisconnect.Unchecked += (s, e) =>
            {
                _app.CurrentSettings.MuteOnDisconnect = false;
                _app.CurrentSettings.Save();
            };

            volumePanel.Children.Add(_cbSyncPhoneVolume);
            volumePanel.Children.Add(_cbMuteOnDisconnect);

            volumeCard.Child = volumePanel;
            root.Children.Add(volumeCard);

            // Audio Quality Settings Card
            var qualityCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var qualityPanel = new StackPanel();
            qualityPanel.Children.Add(new TextBlock
            {
                Text = "传输音质设置",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _rbRaw = new RadioButton
            {
                Content = "Raw PCM (16-bit 48kHz 原生无损直通 - 推荐)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
                IsChecked = (_app.CurrentSettings.Codec == "raw")
            };
            _rbRaw.Checked += (s, e) => { _app.CurrentSettings.Codec = "raw"; _app.CurrentSettings.Save(); };
            qualityPanel.Children.Add(_rbRaw);

            _rbOpus320 = new RadioButton
            {
                Content = "Opus 320K (高码率广播级，极低带宽占用)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
                IsChecked = (_app.CurrentSettings.Codec == "opus320")
            };
            _rbOpus320.Checked += (s, e) => { _app.CurrentSettings.Codec = "opus320"; _app.CurrentSettings.Save(); };
            qualityPanel.Children.Add(_rbOpus320);

            _rbOpus128 = new RadioButton
            {
                Content = "Opus 128K (极限低延迟与省电)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = (_app.CurrentSettings.Codec == "opus128")
            };
            _rbOpus128.Checked += (s, e) => { _app.CurrentSettings.Codec = "opus128"; _app.CurrentSettings.Save(); };
            qualityPanel.Children.Add(_rbOpus128);

            qualityCard.Child = qualityPanel;
            root.Children.Add(qualityCard);

            // Output Options
            var optsCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var optsPanel = new StackPanel();

            _cbMutePhone = new CheckBox
            {
                Content = "手机扬声器静音 (仅电脑端音箱/耳机播放)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = _app.CurrentSettings.MutePhone
            };
            _cbMutePhone.Checked += (s, e) => { _app.CurrentSettings.MutePhone = true; _app.CurrentSettings.Save(); };
            _cbMutePhone.Unchecked += (s, e) => { _app.CurrentSettings.MutePhone = false; _app.CurrentSettings.Save(); };
            optsPanel.Children.Add(_cbMutePhone);

            _cbAutoConnect = new CheckBox
            {
                Content = "开机自启并自动连接当前设备",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = _app.CurrentSettings.AutoConnect
            };
            _cbAutoConnect.Checked += (s, e) => { _app.CurrentSettings.AutoConnect = true; _app.CurrentSettings.Save(); };
            _cbAutoConnect.Unchecked += (s, e) => { _app.CurrentSettings.AutoConnect = false; _app.CurrentSettings.Save(); };
            optsPanel.Children.Add(_cbAutoConnect);

            _cbNotifications = new CheckBox
            {
                Content = "显示连接与断开桌面提示通知",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                IsChecked = _app.CurrentSettings.ShowNotifications
            };
            _cbNotifications.Checked += (s, e) =>
            {
                _app.CurrentSettings.ShowNotifications = true;
                _app.CurrentSettings.Save();
                _app.SyncNotificationState();
            };
            _cbNotifications.Unchecked += (s, e) =>
            {
                _app.CurrentSettings.ShowNotifications = false;
                _app.CurrentSettings.Save();
                _app.SyncNotificationState();
            };
            optsPanel.Children.Add(_cbNotifications);

            // Hotkey row
            var hotkeyRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var btnConfigHotkey = new Button
            {
                Content = "⚙ 快捷键",
                Width = 62,
                Height = 22,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(180, 50, 55, 68)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnConfigHotkey.Click += (s, e) => _app.ShowHotkeyConfigWindow();
            DockPanel.SetDock(btnConfigHotkey, Dock.Right);

            _txtHotkeyDisplay = new TextBlock
            {
                Text = GetHotkeySummary(),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 225)),
                VerticalAlignment = VerticalAlignment.Center
            };
            hotkeyRow.Children.Add(btnConfigHotkey);
            hotkeyRow.Children.Add(_txtHotkeyDisplay);
            optsPanel.Children.Add(hotkeyRow);

            optsCard.Child = optsPanel;
            root.Children.Add(optsCard);

            // Footer info
            var footerText = new TextBlock
            {
                Text = "输出通道: 电脑默认声卡 (支持任意安卓11+设备)",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 160, 170, 185)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            root.Children.Add(footerText);

            mainBorder.Child = root;
            Content = mainBorder;

            UpdateState(ConnectionState.Disconnected);
        }

        public async void TriggerScan()
        {
            _btnScan.IsEnabled = false;
            _btnScan.Content = "扫描中..";

            var discovered = await _app.ScanDevicesAsync();

            _deviceList.Clear();

            // Add remembered current device if valid
            bool hasCurrent = false;
            foreach (var d in discovered)
            {
                _deviceList.Add(d);
                if (d.Target == _app.CurrentSettings.Target || (d.Ip == _app.CurrentSettings.DeviceIp && d.Port == _app.CurrentSettings.Port))
                {
                    hasCurrent = true;
                }
            }

            if (!hasCurrent && !string.IsNullOrEmpty(_app.CurrentSettings.DeviceIp))
            {
                _deviceList.Insert(0, new DeviceItem
                {
                    Name = _app.CurrentSettings.DeviceName,
                    Target = _app.CurrentSettings.Target,
                    Ip = _app.CurrentSettings.DeviceIp,
                    Port = _app.CurrentSettings.Port,
                    IsUsb = !_app.CurrentSettings.Target.Contains(":"),
                    IsCustom = false
                });
            }

            // Custom entry
            _deviceList.Add(new DeviceItem
            {
                Name = "手动输入...",
                Target = "",
                Ip = "",
                Port = 5555,
                IsUsb = false,
                IsCustom = true
            });

            _cbDevices.ItemsSource = null;
            _cbDevices.ItemsSource = _deviceList;

            // Select active device
            int selIdx = 0;
            for (int i = 0; i < _deviceList.Count; i++)
            {
                if (_deviceList[i].Target == _app.CurrentSettings.Target)
                {
                    selIdx = i;
                    break;
                }
            }
            _cbDevices.SelectedIndex = selIdx;

            _btnScan.IsEnabled = true;
            _btnScan.Content = "🔄 扫描";
        }

        private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _cbDevices.SelectedItem as DeviceItem;
            if (item == null) return;

            if (item.IsCustom)
            {
                _tbIp.IsEnabled = true;
                _tbPort.IsEnabled = true;
                _btnSwitchUsb.Visibility = Visibility.Collapsed;
            }
            else
            {
                _app.CurrentSettings.DeviceName = item.Name;
                _app.CurrentSettings.Target = item.Target;

                if (!item.IsUsb && !string.IsNullOrEmpty(item.Ip))
                {
                    _app.CurrentSettings.DeviceIp = item.Ip;
                    _app.CurrentSettings.Port = item.Port;
                    _tbIp.Text = item.Ip;
                    _tbPort.Text = item.Port.ToString();
                    _btnSwitchUsb.Visibility = Visibility.Collapsed;
                }
                else if (item.IsUsb)
                {
                    _btnSwitchUsb.Visibility = Visibility.Visible;
                }
                else
                {
                    _btnSwitchUsb.Visibility = Visibility.Collapsed;
                }

                _app.CurrentSettings.Save();
                UpdateHotkeyText();
            }
        }

        public void UpdateState(ConnectionState state)
        {
            Action act = () =>
            {
                if (state == ConnectionState.Connected)
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(200, 35, 170, 75));
                    string desc = _app.CurrentSettings.Codec == "raw" ? "已连接 (Raw PCM 无损)" : "已连接 (Opus)";
                    _statusText.Text = desc;
                    _btnConnect.Content = "断开连接";
                    _btnConnect.Background = new SolidColorBrush(Color.FromArgb(220, 215, 60, 60));
                }
                else if (state == ConnectionState.Connecting)
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(200, 215, 150, 20));
                    _statusText.Text = "正在连接...";
                    _btnConnect.Content = "连接中...";
                    _btnConnect.Background = new SolidColorBrush(Color.FromArgb(180, 120, 120, 120));
                }
                else
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100));
                    _statusText.Text = "未连接";
                    _btnConnect.Content = "一键连接";
                    _btnConnect.Background = new SolidColorBrush(Color.FromArgb(255, 20, 120, 240));
                }
            };

            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }

        public void SyncCurrentDeviceToUI()
        {
            Action act = () =>
            {
                if (_cbDevices != null && _deviceList != null)
                {
                    for (int i = 0; i < _deviceList.Count; i++)
                    {
                        if (_deviceList[i].Target == _app.CurrentSettings.Target)
                        {
                            _cbDevices.SelectedIndex = i;
                            break;
                        }
                    }
                }
                if (_tbIp != null) _tbIp.Text = _app.CurrentSettings.DeviceIp;
                if (_tbPort != null) _tbPort.Text = _app.CurrentSettings.Port.ToString();
                UpdateHotkeyText();
            };
            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }

        public void UpdateHotkeyText()
        {
            if (_txtHotkeyDisplay != null)
            {
                _txtHotkeyDisplay.Text = GetHotkeySummary();
            }
        }

        private string GetHotkeySummary()
        {
            string target = _app.CurrentSettings.Target;
            DeviceHotkeyBinding b = null;
            if (!string.IsNullOrEmpty(target) && _app.CurrentSettings.DeviceHotkeys.TryGetValue(target, out b))
            {
                if (b.Enabled && b.Key != Key.None)
                {
                    return string.Format("当前模式快捷键: {0}", HotkeyManager.FormatHotkey(b.Modifiers, b.Key));
                }
                return "当前模式快捷键: (未设置)";
            }
            return "专属快捷键: 点击设置";
        }

        public void SyncNotificationCheckbox()
        {
            Action act = () =>
            {
                if (_cbNotifications != null && _cbNotifications.IsChecked != _app.CurrentSettings.ShowNotifications)
                {
                    _cbNotifications.IsChecked = _app.CurrentSettings.ShowNotifications;
                }
            };
            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }

        public void UpdateVolumeUI(int volumePercent, bool isMuted)
        {
            Action act = () =>
            {
                if (_isUserDragging) return;
                if ((DateTime.Now - _app.LastSliderSetTime).TotalMilliseconds < 1500) return;

                _isUpdatingVolumeUI = true;
                try
                {
                    if (_sliderVolume != null) _sliderVolume.Value = volumePercent;
                    if (_txtVolumePercent != null)
                    {
                        _txtVolumePercent.Text = isMuted ? "静音" : (volumePercent + "%");
                    }
                    if (_btnMute != null)
                    {
                        _btnMute.Content = isMuted ? "🔇" : "🔊";
                    }
                }
                finally
                {
                    _isUpdatingVolumeUI = false;
                }
            };
            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }
    }
}
