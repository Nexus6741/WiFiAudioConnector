using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
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
using XamlReader = System.Windows.Markup.XamlReader;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using KeyInterop = System.Windows.Input.KeyInterop;
using Orientation = System.Windows.Controls.Orientation;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace WiFiAudioConnector
{
    public class DeviceItem
    {
        public string Name { get; set; }
        public string Target { get; set; } // e.g. "192.168.31.238:5555" or USB serial "abc1234" or Bluetooth ID
        public string Ip { get; set; }
        public int Port { get; set; }
        public bool IsUsb { get; set; }
        public bool IsBluetooth { get; set; }
        public bool IsCustom { get; set; }

        public string ModeIcon
        {
            get
            {
                if (IsCustom) return "➕";
                if (IsBluetooth) return "\uE702";
                if (IsUsb) return "🔌";
                return "\uE701";
            }
        }

        public string ModeIconFont
        {
            get
            {
                if (IsBluetooth || (!IsCustom && !IsUsb)) return "Segoe MDL2 Assets";
                return "Segoe UI Emoji";
            }
        }

        public System.Windows.Media.Brush ModeIconBrush
        {
            get
            {
                if (IsBluetooth || (!IsCustom && !IsUsb)) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                return System.Windows.Media.Brushes.DimGray;
            }
        }

        public string StatusTag
        {
            get
            {
                if (App.Instance != null && App.Instance.IsTargetConnected(Target))
                {
                    return "● 已连接";
                }
                return "";
            }
        }

        public string DisplayText
        {
            get
            {
                if (IsCustom) return "手动输入设备 IP / 端口...";
                if (IsBluetooth) return string.Format("[蓝牙] {0}", Name);
                if (IsUsb) return string.Format("[USB] {0}", Name);
                return string.Format("{0} ({1}:{2})", Name, Ip, Port);
            }
        }

        public override string ToString()
        {
            if (IsCustom) return "➕ 手动输入设备 IP / 端口...";
            if (IsBluetooth) return string.Format("\uE702 [蓝牙] {0}", Name);
            if (IsUsb) return string.Format("🔌 [USB] {0}", Name);
            return string.Format("\uE701 {0} ({1}:{2})", Name, Ip, Port);
        }
    }

    public class BatteryInfo
    {
        public int Level = -1;
        public bool IsCharging = false;
        public string ChargeType = "";
    }

    public class DeviceHotkeyBinding
    {
        public string Target { get; set; }        // "192.168.31.239:5555" or "a22280af" or Bluetooth ID
        public string DeviceName { get; set; }    // "Xiaomi 15 Pro"
        public bool IsUsb { get; set; }           // true = USB 有线, false = Wi-Fi 无线
        public bool IsBluetooth { get; set; }     // true = 蓝牙 A2DP
        public ModifierKeys Modifiers { get; set; }
        public Key Key { get; set; }
        public bool Enabled { get; set; }

        public string DisplayName
        {
            get
            {
                string mode = IsBluetooth ? "蓝牙 A2DP" : (IsUsb ? "USB 有线" : "Wi-Fi 无线");
                return string.Format("{0} [{1}]", DeviceName, mode);
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

        public string ModeIcon
        {
            get
            {
                if (IsBluetooth) return "\uE702";
                if (IsUsb) return "🔌";
                return "\uE701";
            }
        }

        public string ModeIconFont
        {
            get
            {
                if (IsBluetooth || !IsUsb) return "Segoe MDL2 Assets";
                return "Segoe UI Emoji";
            }
        }

        public System.Windows.Media.Brush ModeIconBrush
        {
            get
            {
                if (IsBluetooth || !IsUsb) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
                return System.Windows.Media.Brushes.LightGray;
            }
        }

        public string DisplayDetail
        {
            get
            {
                string modeTag = IsBluetooth ? "蓝牙 A2DP" : (IsUsb ? "USB 有线" : "Wi-Fi 无线");
                string hkTag = Enabled && Key != Key.None ? HotkeyManager.FormatHotkey(Modifiers, Key) : "未设置";
                string displayTarget = IsBluetooth ? "蓝牙原生配对" : Target;
                return string.Format("{0} [{1}]  ({2})   ▶   快捷键: {3}", DeviceName, modeTag, displayTarget, hkTag);
            }
        }

        public override string ToString()
        {
            return string.Format("{0}  {1}", ModeIcon, DisplayDetail);
        }
    }

    #region Windows Core Audio Session COM Interop
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    internal enum EDataFlow { eRender, eCapture, eAll }
    internal enum ERole { eConsole, eMultimedia, eCommunications }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);
        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig]
        int GetState(out int pdwState);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);
        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig]
        int Commit();
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

        private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        public static string FindVirtualAudioRenderDevice(out string matchingMicName)
        {
            matchingMicName = null;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDeviceCollection collRender;
                var renderNames = new List<string>();
                if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1, out collRender) == 0 && collRender != null)
                {
                    uint count;
                    collRender.GetCount(out count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (collRender.Item(i, out dev) == 0 && dev != null)
                        {
                            IPropertyStore store;
                            if (dev.OpenPropertyStore(0, out store) == 0 && store != null)
                            {
                                PROPERTYKEY key = PKEY_Device_FriendlyName;
                                PROPVARIANT val;
                                if (store.GetValue(ref key, out val) == 0)
                                {
                                    if (val.vt == 31 && val.pwszVal != IntPtr.Zero)
                                    {
                                        string name = Marshal.PtrToStringUni(val.pwszVal);
                                        if (!string.IsNullOrEmpty(name)) renderNames.Add(name);
                                    }
                                    PropVariantClear(ref val);
                                }
                            }
                        }
                    }
                }

                IMMDeviceCollection collCapture;
                var captureNames = new List<string>();
                if (enumerator.EnumAudioEndpoints(EDataFlow.eCapture, 1, out collCapture) == 0 && collCapture != null)
                {
                    uint count;
                    collCapture.GetCount(out count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (collCapture.Item(i, out dev) == 0 && dev != null)
                        {
                            IPropertyStore store;
                            if (dev.OpenPropertyStore(0, out store) == 0 && store != null)
                            {
                                PROPERTYKEY key = PKEY_Device_FriendlyName;
                                PROPVARIANT val;
                                if (store.GetValue(ref key, out val) == 0)
                                {
                                    if (val.vt == 31 && val.pwszVal != IntPtr.Zero)
                                    {
                                        string name = Marshal.PtrToStringUni(val.pwszVal);
                                        if (!string.IsNullOrEmpty(name)) captureNames.Add(name);
                                    }
                                    PropVariantClear(ref val);
                                }
                            }
                        }
                    }
                }

                string[] virtualKeywords = new string[] { "手机麦克风", "网易虚拟", "虚拟音频", "Virtual Audio", "CABLE Input", "VoiceMeeter Input", "VB-Audio", "Virtual Cable" };
                foreach (var kw in virtualKeywords)
                {
                    foreach (var rName in renderNames)
                    {
                        if (rName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foreach (var cName in captureNames)
                            {
                                if (cName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (kw == "CABLE Input" && cName.IndexOf("CABLE Output", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    matchingMicName = cName;
                                    break;
                                }
                            }
                            if (string.IsNullOrEmpty(matchingMicName) && captureNames.Count > 0)
                            {
                                matchingMicName = captureNames[0];
                            }
                            return rName;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // Master endpoint volume control intentionally removed so this app NEVER affects Windows system master volume.
    }
    #endregion

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    #region Bluetooth Audio Connector (A2DP Audio Playback via Windows Runtime)
    public class BluetoothAudioDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class BluetoothAudioConnector : IDisposable
    {
        private static Type _tConn;
        private static Type _tDevInfo;
        private static Type _tOpenResult;
        private static MethodInfo _mAsTask;
        private static bool _initialized = false;
        private static bool _supported = false;

        private object _activeConnection = null;
        private Thread _watchdogThread = null;
        private volatile bool _stopWatchdog = false;

        public event Action ConnectionLost;

        public static bool IsSupported
        {
            get
            {
                EnsureInit();
                return _supported;
            }
        }

        public bool IsConnected
        {
            get
            {
                if (_activeConnection == null) return false;
                try
                {
                    object state = _tConn.GetProperty("State").GetValue(_activeConnection, null);
                    // 1 = Opened
                    return state != null && state.ToString() == "Opened";
                }
                catch { return false; }
            }
        }

        private static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                _tConn = Type.GetType("Windows.Media.Audio.AudioPlaybackConnection, Windows.Media, ContentType=WindowsRuntime");
                _tDevInfo = Type.GetType("Windows.Devices.Enumeration.DeviceInformation, Windows.Devices, ContentType=WindowsRuntime");
                _tOpenResult = Type.GetType("Windows.Media.Audio.AudioPlaybackConnectionOpenResult, Windows.Media, ContentType=WindowsRuntime");

                if (_tConn != null && _tDevInfo != null && _tOpenResult != null)
                {
                    Assembly asmWinRuntime = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                    Type tExt = asmWinRuntime.GetType("System.WindowsRuntimeSystemExtensions");
                    foreach (var m in tExt.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                        {
                            var p = m.GetParameters()[0];
                            if (p.ParameterType.Name == "IAsyncOperation`1")
                            {
                                _mAsTask = m;
                                break;
                            }
                        }
                    }
                    if (_mAsTask != null)
                    {
                        _supported = true;
                    }
                }
            }
            catch
            {
                _supported = false;
            }
        }

        public static async Task<List<BluetoothAudioDevice>> ScanDevicesAsync()
        {
            var list = new List<BluetoothAudioDevice>();
            EnsureInit();
            if (!_supported) return list;

            try
            {
                string selector = (string)_tConn.GetMethod("GetDeviceSelector", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                MethodInfo mFind = _tDevInfo.GetMethod("FindAllAsync", new Type[] { typeof(string) });
                object op = mFind.Invoke(null, new object[] { selector });

                Type tCollection = mFind.ReturnType.GetGenericArguments()[0];
                MethodInfo generic = _mAsTask.MakeGenericMethod(tCollection);
                Task task = (Task)generic.Invoke(null, new object[] { op });
                await task;

                object resultCollection = task.GetType().GetProperty("Result").GetValue(task, null);
                var enumerable = (System.Collections.IEnumerable)resultCollection;

                PropertyInfo pId = null;
                PropertyInfo pName = null;

                foreach (var item in enumerable)
                {
                    if (pId == null)
                    {
                        Type tItem = item.GetType();
                        pId = tItem.GetProperty("Id");
                        pName = tItem.GetProperty("Name");
                    }
                    string id = (string)pId.GetValue(item, null);
                    string name = (string)pName.GetValue(item, null);
                    list.Add(new BluetoothAudioDevice { Id = id, Name = name });
                }
            }
            catch { }

            return list;
        }

        public async Task<bool> ConnectAsync(string deviceId)
        {
            EnsureInit();
            if (!_supported) return false;

            Disconnect();

            try
            {
                MethodInfo mTryCreate = _tConn.GetMethod("TryCreateFromId", new Type[] { typeof(string) });
                object conn = mTryCreate.Invoke(null, new object[] { deviceId });
                if (conn == null) return false;

                _activeConnection = conn;
                _tConn.GetMethod("Start").Invoke(conn, null);

                object openOp = _tConn.GetMethod("OpenAsync").Invoke(conn, null);
                MethodInfo generic = _mAsTask.MakeGenericMethod(_tOpenResult);
                Task task = (Task)generic.Invoke(null, new object[] { openOp });
                await task;

                object openResult = task.GetType().GetProperty("Result").GetValue(task, null);
                PropertyInfo pStatus = _tOpenResult.GetProperty("Status");
                object statusVal = pStatus.GetValue(openResult, null);
                string statusStr = statusVal != null ? statusVal.ToString() : "";

                if (statusStr == "Success")
                {
                    StartWatchdog();
                    return true;
                }

                Disconnect();
                return false;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        private void StartWatchdog()
        {
            _stopWatchdog = false;
            _watchdogThread = new Thread(() =>
            {
                while (!_stopWatchdog)
                {
                    Thread.Sleep(1000);
                    if (_stopWatchdog) break;
                    if (!IsConnected)
                    {
                        if (!_stopWatchdog)
                        {
                            var lost = ConnectionLost;
                            if (lost != null) lost();
                        }
                        break;
                    }
                }
            })
            { IsBackground = true, Name = "BluetoothWatchdog" };
            _watchdogThread.Start();
        }

        public void Disconnect()
        {
            _stopWatchdog = true;
            if (_activeConnection != null)
            {
                try
                {
                    var disp = _activeConnection as IDisposable;
                    if (disp != null) disp.Dispose();
                }
                catch { }
                _activeConnection = null;
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
    #endregion

    #region Tray Wheel Volume Controller
    public class TrayWheelVolumeController : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelProc _mouseProc;
        private LowLevelProc _kbdProc;
        private IntPtr _hookId = IntPtr.Zero;
        private IntPtr _kbdHookId = IntPtr.Zero;
        private NotifyIcon _notifyIcon;
        private Action<int> _onVolumeDelta;
        private Action<int, string> _onMediaKey;
        private DateTime _lastMediaKeyTime = DateTime.MinValue;

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
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private DateTime _lastHoverTime = DateTime.MinValue;
        private System.Drawing.Point _lastMousePos = System.Drawing.Point.Empty;
        private bool _isHoveringIcon = false;

        public TrayWheelVolumeController(NotifyIcon notifyIcon, Action<int> onVolumeDelta, Action<int, string> onMediaKey)
        {
            _notifyIcon = notifyIcon;
            _onVolumeDelta = onVolumeDelta;
            _onMediaKey = onMediaKey;

            _notifyIcon.MouseMove += (s, e) =>
            {
                _lastHoverTime = DateTime.Now;
                _lastMousePos = System.Windows.Forms.Cursor.Position;
                _isHoveringIcon = true;
            };

            _mouseProc = HookCallback;
            _kbdProc = KeyboardHookCallback;
            using (var curProc = Process.GetCurrentProcess())
            using (var curMod = curProc.MainModule)
            {
                IntPtr hMod = GetModuleHandle(curMod.ModuleName);
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
                _kbdHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, hMod, 0);
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

        private bool IsCursorOverTrayIcon()
        {
            try
            {
                var pt = System.Windows.Forms.Cursor.Position;
                var rect = GetIconRect();
                if (!rect.IsEmpty)
                {
                    var expanded = new System.Drawing.Rectangle(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
                    if (expanded.Contains(pt)) return true;
                }
                if (_isHoveringIcon)
                {
                    int dx = Math.Abs(pt.X - _lastMousePos.X);
                    int dy = Math.Abs(pt.Y - _lastMousePos.Y);
                    if (dx <= 4 && dy <= 4)
                    {
                        return true;
                    }
                    if (dx <= 36 && dy <= 36 && (DateTime.Now - _lastHoverTime).TotalSeconds < 15)
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg == WM_MOUSEMOVE)
                {
                    if (_isHoveringIcon)
                    {
                        MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        int dx = Math.Abs(hookStruct.pt.x - _lastMousePos.X);
                        int dy = Math.Abs(hookStruct.pt.y - _lastMousePos.Y);
                        if (dx > 38 || dy > 38)
                        {
                            _isHoveringIcon = false;
                        }
                    }
                }
                else if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    int dx = Math.Abs(hookStruct.pt.x - _lastMousePos.X);
                    int dy = Math.Abs(hookStruct.pt.y - _lastMousePos.Y);
                    if (dx > 38 || dy > 38)
                    {
                        _isHoveringIcon = false;
                    }
                }
                else if (msg == WM_MOUSEWHEEL)
                {
                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    bool isOverIcon = false;

                    var rect = GetIconRect();
                    if (!rect.IsEmpty && rect.Contains(hookStruct.pt.x, hookStruct.pt.y))
                    {
                        isOverIcon = true;
                    }
                    else if (_isHoveringIcon)
                    {
                        int dx = Math.Abs(hookStruct.pt.x - _lastMousePos.X);
                        int dy = Math.Abs(hookStruct.pt.y - _lastMousePos.Y);
                        if (dx <= 38 && dy <= 38 && (DateTime.Now - _lastHoverTime).TotalSeconds < 15)
                        {
                            isOverIcon = true;
                        }
                    }

                    if (isOverIcon)
                    {
                        _lastHoverTime = DateTime.Now;
                        _isHoveringIcon = true;
                        short delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                        int step = (delta > 0) ? 4 : -4;
                        if (_onVolumeDelta != null)
                        {
                            _onVolumeDelta(step);
                        }
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    if (vkCode == 0x41 || vkCode == 0x44 || vkCode == 0x20) // A (0x41), D (0x44), Space (0x20)
                    {
                        bool hasModifier = ((GetKeyState(0x10) & 0x8000) != 0) || // Shift
                                           ((GetKeyState(0x11) & 0x8000) != 0) || // Ctrl
                                           ((GetKeyState(0x12) & 0x8000) != 0) || // Alt
                                           ((GetKeyState(0x5B) & 0x8000) != 0) || // LWin
                                           ((GetKeyState(0x5C) & 0x8000) != 0);   // RWin

                        if (!hasModifier && IsCursorOverTrayIcon())
                        {
                            if ((DateTime.Now - _lastMediaKeyTime).TotalMilliseconds >= 280)
                            {
                                _lastMediaKeyTime = DateTime.Now;
                                if (vkCode == 0x41) // A -> 上一曲
                                {
                                    if (_onMediaKey != null) _onMediaKey(88, "⏮ 上一首");
                                }
                                else if (vkCode == 0x44) // D -> 下一曲
                                {
                                    if (_onMediaKey != null) _onMediaKey(87, "⏭ 下一首");
                                }
                                else if (vkCode == 0x20) // Space -> 播放/暂停
                                {
                                    if (_onMediaKey != null) _onMediaKey(85, "⏯ 播放 / 暂停");
                                }
                            }
                            return (IntPtr)1; // Consume key event when hovering over tray icon
                        }
                    }
                }
            }
            return CallNextHookEx(_kbdHookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            if (_kbdHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_kbdHookId);
                _kbdHookId = IntPtr.Zero;
            }
        }
    }
    #endregion

    #region Volume OSD Window (Electric Blue Floating Capsule)
    public class VolumeOsdWindow : Window
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;
        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private TextBlock _iconText;
        private TextBlock _volText;
        private TextBlock _tagText;
        private Border _trackBorder;
        private Border _fillBorder;
        private StackPanel _hintPanel;
        private TextBlock _hintTitle;
        private TextBlock _hintSubtitle;
        private StackPanel _volumeContent;
        private DispatcherTimer _fadeTimer;

        public VolumeOsdWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Focusable = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            BuildUI();

            _fadeTimer = new DispatcherTimer();
            _fadeTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _fadeTimer.Tick += OnFadeTimerTick;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
            catch { }
        }

        private void BuildUI()
        {
            var capsuleBorder = new Border
            {
                CornerRadius = new CornerRadius(24),
                Background = new SolidColorBrush(Color.FromArgb(235, 15, 23, 42)),
                BorderBrush = new LinearGradientBrush(
                    Color.FromArgb(255, 56, 189, 248),
                    Color.FromArgb(255, 37, 99, 235),
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 1)),
                BorderThickness = new Thickness(1.8),
                Padding = new Thickness(14, 8, 16, 8),
                MinWidth = 210,
                MinHeight = 54,
                MaxWidth = 350,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 2,
                    Opacity = 0.55,
                    Color = Color.FromRgb(56, 189, 248)
                }
            };

            var rootDock = new DockPanel();

            _iconText = new TextBlock
            {
                Text = "🔊",
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(_iconText, Dock.Left);
            rootDock.Children.Add(_iconText);

            var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // Volume Content
            _volumeContent = new StackPanel();

            var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            _volText = new TextBlock
            {
                Text = "100%",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI")
            };
            DockPanel.SetDock(_volText, Dock.Left);
            topRow.Children.Add(_volText);

            _tagText = new TextBlock
            {
                Text = "Wi-Fi 直通",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 2, 0, 0)
            };
            DockPanel.SetDock(_tagText, Dock.Right);
            topRow.Children.Add(_tagText);
            _volumeContent.Children.Add(topRow);

            _trackBorder = new Border
            {
                Width = 120,
                Height = 4.5,
                CornerRadius = new CornerRadius(2.25),
                Background = new SolidColorBrush(Color.FromArgb(120, 51, 65, 85)),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _fillBorder = new Border
            {
                Height = 4.5,
                Width = 120,
                CornerRadius = new CornerRadius(2.25),
                Background = new LinearGradientBrush(
                    Color.FromRgb(56, 189, 248),
                    Color.FromRgb(59, 130, 246),
                    0.0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _trackBorder.Child = _fillBorder;
            _volumeContent.Children.Add(_trackBorder);
            rightStack.Children.Add(_volumeContent);

            // Hint Panel (Title + Subtitle)
            _hintPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _hintTitle = new TextBlock
            {
                Text = "",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                Margin = new Thickness(0, 0, 0, 2)
            };
            _hintSubtitle = new TextBlock
            {
                Text = "",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240)),
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 250
            };
            _hintPanel.Children.Add(_hintTitle);
            _hintPanel.Children.Add(_hintSubtitle);
            rightStack.Children.Add(_hintPanel);

            rootDock.Children.Add(rightStack);
            capsuleBorder.Child = rootDock;
            Content = capsuleBorder;
        }

        private void PositionBottomRight()
        {
            UpdateLayout();
            var workArea = SystemParameters.WorkArea;
            double w = ActualWidth > 0 ? ActualWidth : 210;
            double h = ActualHeight > 0 ? ActualHeight : 54;
            Left = workArea.Right - w - 24;
            Top = workArea.Bottom - h - 24;
        }

        public void ShowVolume(int volumePercent, bool isMuted, string tag)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _volumeContent.Visibility = Visibility.Visible;
                _hintPanel.Visibility = Visibility.Collapsed;

                if (isMuted)
                {
                    _iconText.Text = "🔇";
                    _volText.Text = "静音";
                    _fillBorder.Width = 0;
                }
                else
                {
                    if (volumePercent == 0) _iconText.Text = "🔈";
                    else if (volumePercent < 50) _iconText.Text = "🔉";
                    else _iconText.Text = "🔊";

                    _volText.Text = volumePercent + "%";
                    _fillBorder.Width = Math.Max(0, Math.Min(120, (volumePercent / 100.0) * 120.0));
                }

                _tagText.Text = tag ?? "";

                PositionBottomRight();

                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;

                if (!IsVisible)
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(this).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                        }
                    }
                    catch { }
                    Show();
                }

                _fadeTimer.Stop();
                _fadeTimer.Interval = TimeSpan.FromMilliseconds(1000);
                _fadeTimer.Start();
            }));
        }

        public void ShowHint(string title, string hint)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _volumeContent.Visibility = Visibility.Collapsed;
                _hintPanel.Visibility = Visibility.Visible;

                _hintTitle.Text = title ?? "";
                _hintTitle.Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;

                _hintSubtitle.Text = hint ?? "";
                _hintSubtitle.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;

                string combined = ((title ?? "") + " " + (hint ?? "")).ToLowerInvariant();
                if (combined.Contains("已就绪") || combined.Contains("已连接") || combined.Contains("成功"))
                {
                    _iconText.Text = "⚡";
                }
                else if (combined.Contains("正在连接") || combined.Contains("直连") || combined.Contains("重载"))
                {
                    _iconText.Text = "🔄";
                }
                else if (combined.Contains("断开") || combined.Contains("停止"))
                {
                    _iconText.Text = "🔌";
                }
                else if (combined.Contains("快捷键"))
                {
                    _iconText.Text = "⌨️";
                }
                else if (combined.Contains("媒体") || combined.Contains("播放") || combined.Contains("上一首") || combined.Contains("下一首"))
                {
                    _iconText.Text = "🎵";
                }
                else if (combined.Contains("电量"))
                {
                    _iconText.Text = "🔋";
                }
                else if (combined.Contains("失败") || combined.Contains("错误"))
                {
                    _iconText.Text = "⚠️";
                }
                else
                {
                    _iconText.Text = "ℹ️";
                }

                PositionBottomRight();

                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;

                if (!IsVisible)
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(this).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                        }
                    }
                    catch { }
                    Show();
                }

                _fadeTimer.Stop();
                _fadeTimer.Interval = TimeSpan.FromMilliseconds(1800);
                _fadeTimer.Start();
            }));
        }

        private void OnFadeTimerTick(object sender, EventArgs e)
        {
            _fadeTimer.Stop();
            var anim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(250)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, args) =>
            {
                if (Opacity == 0.0)
                {
                    Hide();
                }
            };
            BeginAnimation(OpacityProperty, anim);
        }
    }
    #endregion

    #region AdbLanScanner
    public static class AdbLanScanner
    {
        public static string LastScannedSubnet { get; private set; }
        public static DateTime LastScanTime { get; private set; }

        public static bool IsRfc1918(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            var parts = ip.Split('.');
            if (parts.Length != 4) return false;
            int b0, b1;
            if (!int.TryParse(parts[0], out b0) || !int.TryParse(parts[1], out b1)) return false;
            if (b0 == 10) return true;
            if (b0 == 172 && (b1 >= 16 && b1 <= 31)) return true;
            if (b0 == 192 && b1 == 168) return true;
            return false;
        }

        public static string GetPrimaryLocalIPv4()
        {
            string bestIp = null;
            int bestScore = -1;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string name = (ni.Name + " " + ni.Description).ToLower();

                    bool isVirtual = name.Contains("vethernet") || name.Contains("wsl") || name.Contains("hyper-v") ||
                                     name.Contains("vmware") || name.Contains("virtual") || name.Contains("bluetooth") ||
                                     name.Contains("tap") || name.Contains("vpn") || name.Contains("tailscale") ||
                                     name.Contains("zerotier") || name.Contains("clash") || name.Contains("meta") ||
                                     name.Contains("npcap") || name.Contains("docker");

                    var ipProps = ni.GetIPProperties();
                    bool hasGateway = ipProps.GatewayAddresses != null && ipProps.GatewayAddresses.Count > 0;

                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;

                        int score = 0;
                        if (IsRfc1918(ip)) score += 10;
                        if (hasGateway) score += 8;
                        if (!isVirtual) score += 4;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 2;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestIp = ip;
                        }
                    }
                }
            }
            catch { }

            // Fallback UDP probe if interface iteration found nothing
            if (string.IsNullOrEmpty(bestIp))
            {
                string[] probeHosts = new string[] { "223.5.5.5", "114.114.114.114", "8.8.8.8" };
                foreach (var host in probeHosts)
                {
                    try
                    {
                        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                        {
                            s.Connect(host, 80);
                            string ip = ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                            if (!string.IsNullOrEmpty(ip) && !ip.StartsWith("127."))
                            {
                                bestIp = ip;
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            return bestIp;
        }

        public static Dictionary<string, string> ParseArpTable()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1200);
                    var reg = new Regex(@"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+([0-9a-fA-F:-]{11,17})");
                    foreach (Match m in reg.Matches(text))
                    {
                        string ip = m.Groups[1].Value;
                        string mac = m.Groups[2].Value.Replace(':', '-').ToUpperInvariant();
                        if (IsValidMac(mac))
                        {
                            map[ip] = mac;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        public static bool IsValidMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return false;
            if (mac == "00-00-00-00-00-00" || mac == "FF-FF-FF-FF-FF-FF") return false;
            return true;
        }

        public static List<string> QueryMdnsServices(string adbPath)
        {
            var list = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "mdns services",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1500);
                    var reg = new Regex(@"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d+)");
                    foreach (Match m in reg.Matches(outStr))
                    {
                        string target = m.Groups[1].Value + ":" + m.Groups[2].Value;
                        if (!list.Contains(target)) list.Add(target);
                    }
                }
            }
            catch { }
            return list;
        }

        public static void DisconnectOfflineDevices(string adbPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1500);
                    var lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && parts[1] == "offline")
                        {
                            string target = parts[0];
                            if (target.Contains(":"))
                            {
                                try
                                {
                                    using (var discP = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = adbPath,
                                        Arguments = "disconnect " + target,
                                        CreateNoWindow = true,
                                        UseShellExecute = false,
                                        WindowStyle = ProcessWindowStyle.Hidden
                                    }))
                                    {
                                        discP.WaitForExit(1000);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static bool ProbeTcpPort(string ip, int port, int timeoutMs)
        {
            using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    var ar = sock.BeginConnect(ip, port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(timeoutMs, false))
                    {
                        sock.EndConnect(ar);
                        return sock.Connected;
                    }
                }
                catch { }
                return false;
            }
        }

        public static async Task<List<string>> DiscoverLanTargetsAsync(string adbPath, List<int> scanPorts, int timeoutMs = 200, int maxConcurrency = 128)
        {
            return await Task.Run(() =>
            {
                var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. Clean up dead/offline devices in ADB daemon
                DisconnectOfflineDevices(adbPath);

                // 2. Query mDNS services in background (finds Android 11+ dynamic ports!)
                var mdnsTask = Task.Run(() => QueryMdnsServices(adbPath));

                // 3. Parallel TCP subnet scan
                string localIp = GetPrimaryLocalIPv4();
                if (!string.IsNullOrEmpty(localIp))
                {
                    int lastDot = localIp.LastIndexOf('.');
                    if (lastDot > 0)
                    {
                        string prefix = localIp.Substring(0, lastDot);
                        LastScannedSubnet = prefix + ".1 ~ 254";

                        var arpMap = ParseArpTable();
                        var activePorts = (scanPorts != null && scanPorts.Count > 0) ? new List<int>(scanPorts) : new List<int> { 5555 };
                        if (!activePorts.Contains(5555)) activePorts.Insert(0, 5555);

                        var bag = new ConcurrentBag<string>();
                        foreach (var port in activePorts)
                        {
                            Parallel.For(1, 255, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency }, i =>
                            {
                                string ip = prefix + "." + i;
                                if (ip == localIp) return;
                                if (ProbeTcpPort(ip, port, timeoutMs))
                                {
                                    // Cross-check against ARP table to eliminate virtual/TUN proxies (Clash, WSL, etc.)
                                    if (arpMap.Count == 0 || arpMap.ContainsKey(ip))
                                    {
                                        bag.Add(ip + ":" + port);
                                    }
                                }
                            });
                        }

                        foreach (var item in bag)
                        {
                            results.Add(item);
                        }
                    }
                }

                // 4. Merge mDNS targets
                try
                {
                    if (mdnsTask.Wait(1500))
                    {
                        foreach (var target in mdnsTask.Result)
                        {
                            results.Add(target);
                        }
                    }
                }
                catch { }

                LastScanTime = DateTime.Now;
                return results.ToList();
            });
        }
    }
    #endregion

    public class Settings
    {
        public string DeviceName = "安卓手机";
        public string DeviceIp = "";
        public int Port = 5555;
        public string Target = "";
        public string ScanPorts = "5555";
        public string Codec = "raw"; // "raw", "opus320", "opus128"
        public string LatencyMode = "balanced"; // "game", "balanced", "smooth"
        public bool MutePhone = true;
        public bool MicDirectMode = false;
        public string CameraFacing = "back"; // "back", "front"
        public string CameraSize = "1920x1080"; // "1920x1080", "3840x2160", "1280x720"
        public int CameraFps = 30; // 30, 60
        public bool CameraAlwaysOnTop = true;
        public bool AutoConnect = true;
        public string NotificationMode = "osd"; // "osd", "windows", "none"
        public bool ShowNotifications
        {
            get { return NotificationMode != "none"; }
            set { NotificationMode = value ? (NotificationMode == "none" ? "osd" : NotificationMode) : "none"; }
        }
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
                sb.AppendLine("LatencyMode=" + LatencyMode);
                sb.AppendLine("MutePhone=" + (MutePhone ? "1" : "0"));
                sb.AppendLine("MicDirectMode=" + (MicDirectMode ? "1" : "0"));
                sb.AppendLine("CameraFacing=" + CameraFacing);
                sb.AppendLine("CameraSize=" + CameraSize);
                sb.AppendLine("CameraFps=" + CameraFps);
                sb.AppendLine("CameraAlwaysOnTop=" + (CameraAlwaysOnTop ? "1" : "0"));
                sb.AppendLine("AutoConnect=" + (AutoConnect ? "1" : "0"));
                sb.AppendLine("NotificationMode=" + NotificationMode);
                sb.AppendLine("ShowNotifications=" + (ShowNotifications ? "1" : "0"));
                sb.AppendLine("MasterVolume=" + MasterVolume);
                sb.AppendLine("IsMuted=" + (IsMuted ? "1" : "0"));
                sb.AppendLine("SyncPhoneVolume=" + (SyncPhoneVolume ? "1" : "0"));
                sb.AppendLine("MuteOnDisconnect=" + (MuteOnDisconnect ? "1" : "0"));
                sb.AppendLine("HotkeyEnabled=" + (HotkeyEnabled ? "1" : "0"));
                sb.AppendLine("HotkeyModifiers=" + (int)HotkeyModifiers);
                sb.AppendLine("HotkeyKey=" + (int)HotkeyKey);
                sb.AppendLine("ScanPorts=" + ScanPorts);

                foreach (var kvp in DeviceHotkeys)
                {
                    var b = kvp.Value;
                    sb.AppendLine(string.Format("DeviceHotkey={0}|{1}|{2}|{3}|{4}|{5}|{6}",
                        b.Target,
                        (b.DeviceName ?? "").Replace("|", "_"),
                        b.IsUsb ? "1" : "0",
                        (int)b.Modifiers,
                        (int)b.Key,
                        b.Enabled ? "1" : "0",
                        b.IsBluetooth ? "1" : "0"));
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
                            else if (k == "LatencyMode") s.LatencyMode = v;
                            else if (k == "MutePhone") s.MutePhone = (v == "1");
                            else if (k == "MicDirectMode") s.MicDirectMode = (v == "1");
                            else if (k == "CameraFacing") s.CameraFacing = v;
                            else if (k == "CameraSize") s.CameraSize = v;
                            else if (k == "CameraFps") int.TryParse(v, out s.CameraFps);
                            else if (k == "CameraAlwaysOnTop") s.CameraAlwaysOnTop = (v == "1");
                            else if (k == "AutoConnect") s.AutoConnect = (v == "1");
                            else if (k == "NotificationMode") s.NotificationMode = v.ToLowerInvariant();
                            else if (k == "ShowNotifications")
                            {
                                if (string.IsNullOrEmpty(s.NotificationMode))
                                {
                                    s.NotificationMode = (v != "0") ? "osd" : "none";
                                }
                            }
                            else if (k == "MasterVolume") { int vInt; if (int.TryParse(v, out vInt)) s.MasterVolume = Math.Max(0, Math.Min(100, vInt)); }
                            else if (k == "IsMuted") s.IsMuted = (v == "1");
                            else if (k == "SyncPhoneVolume") s.SyncPhoneVolume = (v != "0");
                            else if (k == "MuteOnDisconnect") s.MuteOnDisconnect = (v != "0");
                            else if (k == "HotkeyEnabled") s.HotkeyEnabled = (v == "1");
                            else if (k == "HotkeyModifiers") { int m; if (int.TryParse(v, out m)) s.HotkeyModifiers = (ModifierKeys)m; }
                            else if (k == "HotkeyKey") { int kCode; if (int.TryParse(v, out kCode)) s.HotkeyKey = (Key)kCode; }
                            else if (k == "ScanPorts") s.ScanPorts = v;
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
                                    if (segs.Length >= 7) b.IsBluetooth = (segs[6].Trim() == "1");
                                    s.DeviceHotkeys[b.Target] = b;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (s.NotificationMode != "osd" && s.NotificationMode != "windows" && s.NotificationMode != "none")
            {
                s.NotificationMode = s.ShowNotifications ? "osd" : "none";
            }

            if (s.Target == "Bluetooth A2DP:5555" || s.Target == "Bluetooth A2DP" || s.DeviceIp == "Bluetooth A2DP")
            {
                s.Target = "";
                s.DeviceIp = "";
                s.Port = 5555;
            }
            if (s.DeviceHotkeys.ContainsKey("Bluetooth A2DP:5555"))
            {
                s.DeviceHotkeys.Remove("Bluetooth A2DP:5555");
            }

            if (s.DeviceHotkeys.Count == 0)
            {
                s.SeedDefaultHotkeys();
            }

            return s;
        }

        public List<int> GetScanPortsList()
        {
            var list = new List<int> { 5555 };
            if (!string.IsNullOrEmpty(ScanPorts))
            {
                var parts = ScanPorts.Split(new char[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    int port;
                    if (int.TryParse(p.Trim(), out port) && port > 0 && port <= 65535)
                    {
                        if (!list.Contains(port)) list.Add(port);
                    }
                }
            }
            if (Port > 0 && Port <= 65535 && !list.Contains(Port))
            {
                list.Add(Port);
            }
            return list;
        }

        public void AddScanPort(int port)
        {
            if (port <= 0 || port > 65535) return;
            var list = GetScanPortsList();
            if (!list.Contains(port))
            {
                list.Add(port);
                ScanPorts = string.Join(",", list);
                Save();
            }
        }

        public void SeedDefaultHotkeys()
        {
            if (!string.IsNullOrEmpty(Target))
            {
                DeviceHotkeys[Target] = new DeviceHotkeyBinding
                {
                    Target = Target,
                    DeviceName = DeviceName,
                    IsUsb = !Target.Contains(":"),
                    Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                    Key = Key.W,
                    Enabled = true
                };
            }
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
                if (string.Equals(b.Target, "Bluetooth A2DP:5555", StringComparison.OrdinalIgnoreCase)) continue;
                _bindingsList.Add(new DeviceHotkeyBinding
                {
                    Target = b.Target,
                    DeviceName = b.DeviceName,
                    IsUsb = b.IsUsb,
                    IsBluetooth = b.IsBluetooth,
                    Modifiers = b.Modifiers,
                    Key = b.Key,
                    Enabled = b.Enabled
                });
            }

            // Immediately pull any devices cached in App.LastDiscoveredDevices
            if (_app.LastDiscoveredDevices != null)
            {
                foreach (var d in _app.LastDiscoveredDevices)
                {
                    if (string.IsNullOrEmpty(d.Target) || d.IsCustom) continue;
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
                        _bindingsList.Add(new DeviceHotkeyBinding
                        {
                            Target = d.Target,
                            DeviceName = d.Name,
                            IsUsb = d.IsUsb,
                            IsBluetooth = d.IsBluetooth,
                            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                            Key = d.IsBluetooth ? Key.D3 : (d.IsUsb ? Key.D2 : Key.D1),
                            Enabled = true
                        });
                    }
                }
            }

            // If current active target is not yet in bindings, add it
            string curTarget = _app.CurrentSettings.Target;
            if (!string.IsNullOrEmpty(curTarget) && !string.Equals(curTarget, "Bluetooth A2DP:5555", StringComparison.OrdinalIgnoreCase))
            {
                bool found = false;
                foreach (var b in _bindingsList)
                {
                    if (string.Equals(b.Target, curTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    bool isBt = curTarget.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || curTarget.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase);
                    _bindingsList.Add(new DeviceHotkeyBinding
                    {
                        Target = curTarget,
                        DeviceName = _app.CurrentSettings.DeviceName,
                        IsUsb = !isBt && !curTarget.Contains(":"),
                        IsBluetooth = isBt,
                        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                        Key = isBt ? Key.D3 : (curTarget.Contains(":") ? Key.D1 : Key.D2),
                        Enabled = true
                    });
                }
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
            string lbTemplateXaml = @"
                <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <DockPanel LastChildFill=""True"" Margin=""2"">
                        <TextBlock Text=""{Binding ModeIcon}"" FontFamily=""{Binding ModeIconFont}"" Foreground=""{Binding ModeIconBrush}"" FontSize=""12"" VerticalAlignment=""Center"" Margin=""0,0,8,0"" Width=""16"" TextAlignment=""Center""/>
                        <TextBlock Text=""{Binding DisplayDetail}"" FontFamily=""Microsoft YaHei UI, Consolas, Segoe UI"" Foreground=""#FFFFFF"" FontSize=""11"" VerticalAlignment=""Center""/>
                    </DockPanel>
                </DataTemplate>";
            _lbDevices.ItemTemplate = (DataTemplate)XamlReader.Parse(lbTemplateXaml);
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
            addPreset("Ctrl+Shift+B (蓝牙常用)", ModifierKeys.Control | ModifierKeys.Shift, Key.B);
            addPreset("Ctrl+Alt+1", ModifierKeys.Control | ModifierKeys.Alt, Key.D1);
            addPreset("Ctrl+Alt+2", ModifierKeys.Control | ModifierKeys.Alt, Key.D2);
            addPreset("Ctrl+Alt+3 (蓝牙)", ModifierKeys.Control | ModifierKeys.Alt, Key.D3);
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

            int defaultSel = 0;
            string selTarget = _app.CurrentSettings.Target;
            for (int i = 0; i < _bindingsList.Count; i++)
            {
                if (string.Equals(_bindingsList[i].Target, selTarget, StringComparison.OrdinalIgnoreCase))
                {
                    defaultSel = i;
                    break;
                }
            }
            if (_lbDevices.Items.Count > 0)
            {
                _lbDevices.SelectedIndex = defaultSel;
            }

            Loaded += (s, e) => ScanAndAddDevices();
        }

        private async void ScanAndAddDevices()
        {
            _tbStatus.Text = "正在扫描局域网、USB与蓝牙设备...";
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
                        b.DeviceName = d.Name;
                        b.IsUsb = d.IsUsb;
                        b.IsBluetooth = d.IsBluetooth;
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
                        IsBluetooth = d.IsBluetooth,
                        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                        Key = d.IsBluetooth ? Key.D3 : (d.IsUsb ? Key.D2 : Key.D1),
                        Enabled = true
                    };
                    _bindingsList.Add(newBinding);
                    addedCount++;
                }
            }

            _lbDevices.Items.Refresh();

            // Pre-select current target if nothing selected
            if (_lbDevices.SelectedItem == null && _bindingsList.Count > 0)
            {
                string curTarget = _app.CurrentSettings.Target;
                int sel = 0;
                for (int i = 0; i < _bindingsList.Count; i++)
                {
                    if (string.Equals(_bindingsList[i].Target, curTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        sel = i;
                        break;
                    }
                }
                _lbDevices.SelectedIndex = sel;
            }

            _tbStatus.Text = addedCount > 0 ? string.Format("✓ 扫描完成，新增了 {0} 个连接方式（含蓝牙/USB/无线）！", addedCount) : "✓ 扫描完成，所有设备与连接模式（含蓝牙）均在列表中。";
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

            string modeTag = _selectedBinding.IsBluetooth ? "蓝牙 A2DP 直通" : (_selectedBinding.IsUsb ? "USB 有线直连" : "Wi-Fi 无线网络");
            string targetLabel = _selectedBinding.IsBluetooth ? "蓝牙原生配对" : _selectedBinding.Target;
            _tbSelectedTitle.Text = string.Format("正在设置: {0} [{1}] ({2})", _selectedBinding.DeviceName, modeTag, targetLabel);
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
                if (string.Equals(b.Target, "Bluetooth A2DP:5555", StringComparison.OrdinalIgnoreCase)) continue;
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
        public static App Instance { get; private set; }
        private static Mutex _mutex = null;
        private NotifyIcon _notifyIcon;
        private FlyoutWindow _flyout;
        private Settings _settings;
        private Process _scrcpyProc = null;
        private Process _cameraProc = null;
        private Process _micProc = null;
        private ToolStripMenuItem _trayCameraItem = null;
        private BluetoothAudioConnector _btConnector = new BluetoothAudioConnector();
        private bool _isBluetoothConnected = false;
        private string _currentScrcpyTarget = null;
        private string _currentScrcpyDeviceName = null;
        private string _currentBtTarget = null;
        private string _currentBtDeviceName = null;
        private bool _isConnectingScrcpy = false;
        private bool _isConnectingBt = false;

        public bool IsScrcpyConnected { get { return _scrcpyProc != null && !_scrcpyProc.HasExited; } }
        public bool IsCameraRunning { get { return _cameraProc != null && !_cameraProc.HasExited; } }
        public bool IsMicRunning { get { return _micProc != null && !_micProc.HasExited; } }
        public bool IsBluetoothConnected { get { return _isBluetoothConnected && _btConnector != null && _btConnector.IsConnected; } }
        public bool IsConnected { get { return IsScrcpyConnected || IsBluetoothConnected; } }

        public string CurrentScrcpyTarget { get { return _currentScrcpyTarget; } }
        public string CurrentScrcpyDeviceName { get { return _currentScrcpyDeviceName; } }
        public string CurrentBtTarget { get { return _currentBtTarget; } }
        public string CurrentBtDeviceName { get { return _currentBtDeviceName; } }
        public string CurrentActiveTarget { get { return _currentScrcpyTarget ?? _currentBtTarget; } }

        public bool IsTargetConnected(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            bool isBt = target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase);
            if (isBt)
            {
                return IsBluetoothConnected && string.Equals(_currentBtTarget, target, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                if (IsScrcpyConnected && !string.IsNullOrEmpty(_currentScrcpyTarget))
                {
                    if (string.Equals(_currentScrcpyTarget, target, StringComparison.OrdinalIgnoreCase)) return true;
                    if (_currentScrcpyTarget.StartsWith(target + ":", StringComparison.OrdinalIgnoreCase)) return true;
                    if (target.Contains(":") && _currentScrcpyTarget.Split(':')[0] == target.Split(':')[0]) return true;
                }
                if (IsMicRunning || IsCameraRunning)
                {
                    string curTarget = !string.IsNullOrEmpty(_currentScrcpyTarget) ? _currentScrcpyTarget : _settings.Target;
                    if (!string.IsNullOrEmpty(curTarget))
                    {
                        if (string.Equals(curTarget, target, StringComparison.OrdinalIgnoreCase)) return true;
                        if (curTarget.StartsWith(target + ":", StringComparison.OrdinalIgnoreCase)) return true;
                        if (target.Contains(":") && curTarget.Split(':')[0] == target.Split(':')[0]) return true;
                    }
                }
                return false;
            }
        }

        public App()
        {
            Instance = this;
        }

        public List<DeviceItem> LastDiscoveredDevices = new List<DeviceItem>();
        private HotkeyManager _hotkeyManager = null;
        private HotkeyConfigWindow _hotkeyWin = null;
        private ToolStripMenuItem _notifyMenuOsd = null;
        private ToolStripMenuItem _notifyMenuWin = null;
        private ToolStripMenuItem _notifyMenuNone = null;
        private TrayWheelVolumeController _trayWheelController = null;
        private VolumeOsdWindow _volumeOsd = null;
        private Process _logcatProc = null;
        private int _phoneMaxVolume = 150;
        private DateTime _lastSliderSetTime = DateTime.MinValue;
        public DateTime LastSliderSetTime { get { return _lastSliderSetTime; } }

        private readonly object _phoneSyncLock = new object();
        private int _pendingPhoneIndex = -1;
        private bool _pendingPhoneMute = false;
        private bool _hasPendingPhoneSync = false;
        private bool _isPhoneSyncWorkerRunning = false;

        private CancellationTokenSource _batteryCts = null;
        private BatteryInfo _lastBatteryInfo = null;
        public BatteryInfo LastBatteryInfo { get { return _lastBatteryInfo; } set { _lastBatteryInfo = value; } }
        private bool _hasAlertedLowBattery = false;

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
                LogLine("Main entered");
                bool createdNew;
                _mutex = new Mutex(true, "WiFiAudioConnector_Universal_Mutex", out createdNew);
                LogLine("Mutex createdNew: " + createdNew);
                if (!createdNew)
                {
                    LogLine("Exiting because not createdNew");
                    MessageBox.Show("WiFi 音频连接器已经在后台运行中！\n请查看桌面右下角系统托盘图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                LogLine("Creating App instance");
                var app = new App();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                LogLine("Calling app.Run()");
                app.Run();
                LogLine("app.Run() returned");
            }
            catch (Exception ex)
            {
                LogLine("Main Exception: " + ex);
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

            LogLine("OnStartup entered");
            try
            {
                LogLine("Loading settings");
                _settings = Settings.Load();
                LogLine("Creating VolumeOsdWindow");
                _volumeOsd = new VolumeOsdWindow();
                LogLine("InitTrayIcon");
                InitTrayIcon();
                LogLine("InitHotkey");
                InitHotkey();
                _btConnector.ConnectionLost += () =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isBluetoothConnected)
                        {
                            DisconnectBluetooth(false);
                            ShowNotification("蓝牙音频已断开", "蓝牙音频连接已中断或设备超出配对范围", ToolTipIcon.Warning);
                        }
                    }));
                };
                LogLine("Creating FlyoutWindow");
                _flyout = new FlyoutWindow(this);
                MainWindow = _flyout;
                LogLine("FlyoutWindow initialized");

                if (_settings.AutoConnect)
                {
                    LogLine("AutoConnect starting");
                    ConnectAsync();
                }
                LogLine("OnStartup completed");
            }
            catch (Exception ex)
            {
                LogLine("OnStartup Exception: " + ex);
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
            menu.Items.Add("⚡ 一键连接 / 断开 (中键点击)", null, (s, e) =>
            {
                if (IsConnected) Disconnect();
                else ConnectAsync();
            });
            menu.Items.Add("⏯ 播放 / 暂停 (悬浮按空格)", null, (s, e) => SendMediaKey(85, "⏯ 播放 / 暂停"));
            menu.Items.Add("⏮ 上一首 (悬浮按 A)", null, (s, e) => SendMediaKey(88, "⏮ 上一首"));
            menu.Items.Add("⏭ 下一首 (悬浮按 D)", null, (s, e) => SendMediaKey(87, "⏭ 下一首"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("连接当前设备", null, (s, e) => ConnectAsync());
            menu.Items.Add("断开连接", null, (s, e) => Disconnect());
            _trayCameraItem = new ToolStripMenuItem("📷 开启无线摄像头", null, (s, e) => ToggleCamera());
            menu.Items.Add(_trayCameraItem);
            menu.Items.Add("扫描局域网、USB与蓝牙设备", null, (s, e) =>
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

            var notifySubMenu = new ToolStripMenuItem("提示通知方式");
            _notifyMenuOsd = new ToolStripMenuItem("桌面 OSD 悬浮胶囊", null, (s, e) => SetNotificationMode("osd"));
            _notifyMenuWin = new ToolStripMenuItem("Windows 系统气泡", null, (s, e) => SetNotificationMode("windows"));
            _notifyMenuNone = new ToolStripMenuItem("关闭提示通知", null, (s, e) => SetNotificationMode("none"));
            notifySubMenu.DropDownItems.Add(_notifyMenuOsd);
            notifySubMenu.DropDownItems.Add(_notifyMenuWin);
            notifySubMenu.DropDownItems.Add(_notifyMenuNone);
            menu.Items.Add(notifySubMenu);
            SyncNotificationState();

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
                else if (e.Button == MouseButtons.Middle)
                {
                    OnTrayMiddleClick();
                }
            };
            _trayWheelController = new TrayWheelVolumeController(_notifyIcon, OnTrayWheelVolumeDelta, (key, desc) => SendMediaKey(key, desc));
        }

        private void OnTrayMiddleClick()
        {
            var sel = (_flyout != null) ? _flyout.GetSelectedDevice() : null;

            // 1. Dual/concurrent mode optimization:
            // When Bluetooth is connected and scrcpy is disconnected, middle click connects scrcpy
            if (IsBluetoothConnected && !IsScrcpyConnected)
            {
                string scrcpyTarget = (sel != null && !sel.IsBluetooth) ? sel.Target : _settings.Target;
                string scrcpyName = (sel != null && !sel.IsBluetooth) ? sel.Name : _settings.DeviceName;
                if (!string.IsNullOrEmpty(scrcpyTarget) && !scrcpyTarget.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) && !scrcpyTarget.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase))
                {
                    ConnectAsync(scrcpyTarget, scrcpyName);
                    return;
                }
            }

            // 2. Target resolution
            string target = (sel != null && !string.IsNullOrEmpty(sel.Target)) ? sel.Target : _settings.Target;
            string devName = (sel != null && !string.IsNullOrEmpty(sel.Name)) ? sel.Name : _settings.DeviceName;

            bool isBt = target != null && (target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase));

            if (isBt)
            {
                if (IsBluetoothConnected) DisconnectBluetooth(true);
                else ConnectAsync(target, devName);
            }
            else
            {
                if (IsScrcpyConnected) DisconnectScrcpy(true);
                else ConnectAsync(target, devName);
            }
        }

        private void OnTrayWheelVolumeDelta(int delta)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsScrcpyConnected && _scrcpyProc != null && !_scrcpyProc.HasExited)
                {
                    int cur = _settings.MasterVolume;
                    int newVol = Math.Max(0, Math.Min(100, cur + delta));

                    SetVolumeFromUI(newVol, false);
                    if (_flyout != null)
                    {
                        _flyout.UpdateVolumeUI(newVol, false);
                    }
                    _notifyIcon.Text = string.Format("WiFi 音频连接器 - 音量: {0}% (已连接)", newVol);
                    if (_volumeOsd != null)
                    {
                        bool isUsb = !string.IsNullOrEmpty(_currentScrcpyTarget) && !_currentScrcpyTarget.Contains(":");
                        string modeTag = isUsb ? "USB 有线" : "Wi-Fi 直通";
                        _volumeOsd.ShowVolume(newVol, false, modeTag);
                    }
                }
                else if (IsBluetoothConnected)
                {
                    if (_volumeOsd != null)
                    {
                        _volumeOsd.ShowHint("蓝牙直通模式", "蓝牙模式请直接在手机端调节音量");
                    }
                    return;
                }
                else
                {
                    int cur = _settings.MasterVolume;
                    int newVol = Math.Max(0, Math.Min(100, cur + delta));

                    _settings.MasterVolume = newVol;
                    _settings.IsMuted = false;
                    _settings.Save();
                    if (_flyout != null)
                    {
                        _flyout.UpdateVolumeUI(newVol, false);
                    }
                    _notifyIcon.Text = string.Format("WiFi 音频连接器 - 音量: {0}% (未连接)", newVol);
                    if (_volumeOsd != null)
                    {
                        _volumeOsd.ShowVolume(newVol, false, "预设音量 (未连接)");
                    }
                }
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

            // 1. Run dynamic LAN scanner (Subnet parallel TCP scan + ARP validation + mDNS discovery)
            var scanPorts = _settings.GetScanPortsList();
            var lanTargets = await AdbLanScanner.DiscoverLanTargetsAsync(adbPath, scanPorts);

            // 2. Connect to newly discovered LAN targets
            if (lanTargets != null && lanTargets.Count > 0)
            {
                await Task.Run(() =>
                {
                    foreach (var target in lanTargets)
                    {
                        try
                        {
                            var psiConn = new ProcessStartInfo
                            {
                                FileName = adbPath,
                                Arguments = "connect " + target,
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            using (var p = Process.Start(psiConn))
                            {
                                p.WaitForExit(1500);
                            }
                        }
                        catch { }
                    }
                });
            }

            // 3. Enumerate ADB devices (USB + connected TCP)
            var adbTask = Task.Run(() =>
            {
                var adbList = new List<DeviceItem>();
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

                                adbList.Add(new DeviceItem
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
                return adbList;
            });

            Task<List<BluetoothAudioDevice>> btTask = null;
            if (BluetoothAudioConnector.IsSupported)
            {
                try
                {
                    btTask = BluetoothAudioConnector.ScanDevicesAsync();
                }
                catch { }
            }

            var adbRes = await adbTask;
            list.AddRange(adbRes);

            if (btTask != null)
            {
                try
                {
                    var btDevices = await btTask;
                    foreach (var bt in btDevices)
                    {
                        list.Add(new DeviceItem
                        {
                            Name = bt.Name,
                            Target = bt.Id,
                            Ip = "",
                            Port = 0,
                            IsUsb = false,
                            IsBluetooth = true,
                            IsCustom = false
                        });
                    }
                }
                catch { }
            }

            LastDiscoveredDevices = new List<DeviceItem>(list);

            // 4. Smart Target Migration:
            // If current settings target was a Wi-Fi device that was NOT found,
            // but an active Wi-Fi device was discovered on the LAN:
            var wifiDevices = list.Where(d => !d.IsBluetooth && !d.IsUsb).ToList();
            bool currentTargetFound = list.Any(d => string.Equals(d.Target, _settings.Target, StringComparison.OrdinalIgnoreCase));
            if (!currentTargetFound && !string.IsNullOrEmpty(_settings.Target) && _settings.Target.Contains(":") && wifiDevices.Count > 0)
            {
                var matched = wifiDevices.FirstOrDefault(d => string.Equals(d.Name, _settings.DeviceName, StringComparison.OrdinalIgnoreCase));
                if (matched == null && wifiDevices.Count == 1)
                {
                    matched = wifiDevices[0];
                }

                if (matched != null)
                {
                    LogLine(string.Format("检测到局域网设备 IP 变动: {0} -> {1}", _settings.Target, matched.Target));
                    _settings.Target = matched.Target;
                    _settings.DeviceIp = matched.Ip;
                    _settings.Port = matched.Port;
                    _settings.DeviceName = matched.Name;
                    _settings.Save();
                }
            }

            if (string.IsNullOrEmpty(_settings.Target) && list.Count > 0)
            {
                var firstDev = list.FirstOrDefault(d => !d.IsBluetooth) ?? list[0];
                _settings.Target = firstDev.Target;
                _settings.DeviceIp = firstDev.Ip;
                _settings.Port = firstDev.Port;
                _settings.DeviceName = firstDev.Name;
                _settings.Save();
            }

            // Ensure newly discovered devices exist in settings hotkeys dictionary
            bool modified = false;
            foreach (var d in list)
            {
                if (string.IsNullOrEmpty(d.Target)) continue;
                if (!_settings.DeviceHotkeys.ContainsKey(d.Target))
                {
                    _settings.DeviceHotkeys[d.Target] = new DeviceHotkeyBinding
                    {
                        Target = d.Target,
                        DeviceName = d.Name,
                        IsUsb = d.IsUsb,
                        IsBluetooth = d.IsBluetooth,
                        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                        Key = d.IsBluetooth ? Key.D3 : (d.IsUsb ? Key.D2 : Key.D1),
                        Enabled = true
                    };
                    modified = true;
                }
            }
            if (modified)
            {
                _settings.Save();
                ApplyAllHotkeys();
            }

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

        public static string TruncateNotifyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length > 63) return text.Substring(0, 60) + "...";
            return text;
        }

        public void UpdateOverallState()
        {
            bool scrcpyOn = IsScrcpyConnected;
            bool btOn = IsBluetoothConnected;
            bool micOn = IsMicRunning;
            bool camOn = IsCameraRunning;
            UpdateTrayIcon(scrcpyOn || btOn || micOn || camOn);

            if (scrcpyOn && btOn)
            {
                _notifyIcon.Text = TruncateNotifyText(string.Format("WiFi 音频连接器 (双设备并发: {0} + {1})", _currentScrcpyDeviceName ?? "scrcpy", _currentBtDeviceName ?? "蓝牙"));
            }
            else if (scrcpyOn)
            {
                string modeTag = (_currentScrcpyTarget != null && _currentScrcpyTarget.Contains(":")) ? "Wi-Fi" : "USB";
                string roleTag = micOn ? "音频+麦克风" : (camOn ? "音频+摄像头" : "已连接");
                _notifyIcon.Text = TruncateNotifyText(string.Format("WiFi 音频连接器 - {0} [{1}] ({2})", _currentScrcpyDeviceName ?? _settings.DeviceName, modeTag, roleTag));
            }
            else if (btOn)
            {
                _notifyIcon.Text = TruncateNotifyText(string.Format("WiFi 音频连接器 - {0} [蓝牙] (已连接)", _currentBtDeviceName ?? _settings.DeviceName));
            }
            else if (micOn || camOn)
            {
                string tag = (micOn && camOn) ? "麦克风+摄像头" : (micOn ? "麦克风直连" : "无线摄像头");
                _notifyIcon.Text = TruncateNotifyText(string.Format("WiFi 音频连接器 - {0} ({1})", _settings.DeviceName, tag));
            }
            else
            {
                _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
            }

            if (_flyout != null)
            {
                _flyout.UpdateUIState();
            }
        }

        public async Task<bool> ConnectAsync(string specificTarget = null, string specificName = null)
        {
            string target = !string.IsNullOrEmpty(specificTarget) ? specificTarget : _settings.Target;
            string devName = !string.IsNullOrEmpty(specificName) ? specificName : _settings.DeviceName;
            bool isBt = target != null && (target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase));

            if (isBt)
            {
                if (_isConnectingBt) return false;
                if (IsBluetoothConnected && string.Equals(_currentBtTarget, target, StringComparison.OrdinalIgnoreCase)) return true;

                _isConnectingBt = true;
                _flyout.UpdateState(ConnectionState.Connecting);
                _notifyIcon.Text = TruncateNotifyText(string.Format("WiFi 音频连接器 (正在连接蓝牙: {0}...)", devName));
                ShowNotification("正在连接蓝牙", string.Format("正在连接 {0}...", devName));

                // If connecting Bluetooth to the SAME device currently on scrcpy, smoothly disconnect scrcpy
                if (IsScrcpyConnected && !string.IsNullOrEmpty(_currentScrcpyDeviceName) &&
                    string.Equals(_currentScrcpyDeviceName.Trim(), devName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    DisconnectScrcpy(false);
                }

                // If another Bluetooth device was connected, disconnect it
                if (IsBluetoothConnected)
                {
                    DisconnectBluetooth(false);
                }

                bool btOk = await _btConnector.ConnectAsync(target);
                _isConnectingBt = false;

                if (btOk)
                {
                    _isBluetoothConnected = true;
                    _currentBtTarget = target;
                    _currentBtDeviceName = devName;
                    UpdateOverallState();
                    ShowNotification("蓝牙音频已连接", string.Format("{0} [蓝牙]\n已开启 Windows 蓝牙 A2DP 音频直通", devName), ToolTipIcon.Info);
                    _flyout.SyncCurrentDeviceToUI();
                }
                else
                {
                    _isBluetoothConnected = false;
                    _currentBtTarget = null;
                    _currentBtDeviceName = null;
                    UpdateOverallState();
                    ShowNotification("蓝牙连接失败", "无法连接到该蓝牙音频设备，请确认手机已开机、开启蓝牙并处于配对范围", ToolTipIcon.Error);
                }
                return btOk;
            }

            // Scrcpy (Wi-Fi or USB)
            if (_isConnectingScrcpy) return false;
            if (IsScrcpyConnected && string.Equals(_currentScrcpyTarget, target, StringComparison.OrdinalIgnoreCase)) return true;

            string adbPath = FindToolPath("adb.exe");
            string scrcpyPath = FindToolPath("scrcpy.exe");

            if (string.IsNullOrEmpty(adbPath) || string.IsNullOrEmpty(scrcpyPath))
            {
                _flyout.UpdateState(ConnectionState.Disconnected);
                MessageBox.Show("未能找到 scrcpy 或 adb 组件！请确保程序目录下存在 scrcpy.exe 与 adb.exe。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            _isConnectingScrcpy = true;
            _flyout.UpdateState(ConnectionState.Connecting);
            _notifyIcon.Text = TruncateNotifyText(string.Format("WiFi 音频连接器 (正在连接 {0}...)", devName));
            ShowNotification("正在连接", string.Format("正在连接 {0}...", devName));

            // If connecting scrcpy to the SAME device currently on Bluetooth, smoothly disconnect Bluetooth
            if (IsBluetoothConnected && !string.IsNullOrEmpty(_currentBtDeviceName) &&
                string.Equals(_currentBtDeviceName.Trim(), devName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                DisconnectBluetooth(false);
            }

            // If another scrcpy device was connected, disconnect it
            if (IsScrcpyConnected)
            {
                DisconnectScrcpy(false);
            }

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

                    // 2. build scrcpy arguments for system/media audio
                    string codecArg = "--audio-codec=raw";
                    if (_settings.Codec == "opus320") codecArg = "--audio-codec=opus --audio-bit-rate=320K";
                    else if (_settings.Codec == "opus128") codecArg = "--audio-codec=opus --audio-bit-rate=128K";

                    int bufferMs = 50;
                    if (_settings.LatencyMode == "game") bufferMs = 30;
                    else if (_settings.LatencyMode == "smooth") bufferMs = 80;

                    string modeArg = _settings.MutePhone ? "--audio-source=output" : "--audio-source=playback --audio-dup";
                    string scrcpyArgs = string.Format("-s {0} --no-video --no-window {1} {2} --audio-buffer={3}", target, codecArg, modeArg, bufferMs);

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

            _isConnectingScrcpy = false;

            if (ok)
            {
                _currentScrcpyTarget = target;
                _currentScrcpyDeviceName = devName;
                UpdateOverallState();

                string modeTag = isTcp ? "Wi-Fi 无线" : "USB 有线";
                string desc = _settings.Codec == "raw" ? "Raw PCM 无损" : "Opus 320K";
                int curBuffer = (_settings.LatencyMode == "game") ? 30 : ((_settings.LatencyMode == "smooth") ? 80 : 50);
                ShowNotification("音频流已就绪", string.Format("{0} [{1}]\n{2} | {3}ms 延迟缓冲", devName, modeTag, desc, curBuffer));

                StartPhoneVolumeSync(target, _scrcpyProc.Id);
                StartBatteryMonitor(target);

                if (_settings.MicDirectMode)
                {
                    StartMicStreamAsync(target, devName);
                }

                // Watchdog task
                Task.Run(() =>
                {
                    try
                    {
                        _scrcpyProc.WaitForExit();
                    }
                    catch { }

                    if (_isReloading)
                    {
                        return;
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DisconnectScrcpy(false);
                    }));
                });
                return true;
            }
            else
            {
                _scrcpyProc = null;
                _currentScrcpyTarget = null;
                _currentScrcpyDeviceName = null;
                UpdateOverallState();
                ShowNotification("连接失败", "无法连接到设备，请确认手机已开机且处于连接状态", ToolTipIcon.Error);
                return false;
            }
        }

        private bool _isReloading = false;
        public bool IsReloading { get { return _isReloading; } }

        public async void ReloadAudioStreamAsync(string noticeTag = null)
        {
            if (!IsScrcpyConnected || _isConnectingScrcpy || _isReloading) return;

            string target = _currentScrcpyTarget;
            if (string.IsNullOrEmpty(target)) target = _settings.Target;
            string devName = _currentScrcpyDeviceName ?? _settings.DeviceName;

            _isReloading = true;
            try
            {
                ShowNotification("参数热重载", noticeTag != null ? (noticeTag + "\n正在平滑热重载音频流...") : "正在平滑热重载音频流...");

                StopPhoneVolumeSync();
                StopBatteryMonitor();

                // 1. 断连前先静音手机媒体音量，彻底消除重连间隙可能发生的设备扬声器声音外露
                if (!string.IsNullOrEmpty(target) && !_settings.MicDirectMode)
                {
                    await Task.Run(() => MutePhoneMediaQuick(target));
                }

                // 2. 终止当前 scrcpy 音频流进程
                if (_scrcpyProc != null && !_scrcpyProc.HasExited)
                {
                    try
                    {
                        _scrcpyProc.Kill();
                        _scrcpyProc.WaitForExit(500);
                    }
                    catch { }
                }
                _scrcpyProc = null;

                // 3. 短暂平滑等待
                await Task.Delay(250);

                // 4. 重建连接并在连接就绪后自动恢复设备音量
                _isReloading = false;
                ConnectAsync(target, devName);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_flyout != null && _flyout.IsVisible)
                    {
                        _flyout.Activate();
                    }
                }));
            }
            catch (Exception ex)
            {
                LogLine("ReloadAudioStreamAsync Exception: " + ex);
                _isReloading = false;
            }
        }

        public void Disconnect(string specificTarget = null)
        {
            if (!string.IsNullOrEmpty(specificTarget))
            {
                bool isBt = specificTarget.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || specificTarget.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase);
                if (isBt)
                {
                    DisconnectBluetooth(true);
                }
                else
                {
                    DisconnectScrcpy(true);
                    StopMicStream();
                    StopCamera();
                }
                return;
            }

            var sel = (_flyout != null) ? _flyout.GetSelectedDevice() : null;
            if (sel != null && !string.IsNullOrEmpty(sel.Target))
            {
                if (sel.IsBluetooth)
                {
                    DisconnectBluetooth(true);
                }
                else
                {
                    DisconnectScrcpy(true);
                    StopMicStream();
                    StopCamera();
                }
            }
            else
            {
                if (IsBluetoothConnected) DisconnectBluetooth(true);
                if (IsScrcpyConnected) DisconnectScrcpy(true);
                StopMicStream();
                StopCamera();
            }
        }

        public void DisconnectBluetooth(bool showNotice = true)
        {
            if (!_isBluetoothConnected && _btConnector == null) return;
            string devName = _currentBtDeviceName ?? _settings.DeviceName;
            _isBluetoothConnected = false;
            _currentBtTarget = null;
            _currentBtDeviceName = null;
            try { _btConnector.Disconnect(); } catch { }
            UpdateOverallState();
            if (_flyout != null) _flyout.SyncCurrentDeviceToUI();
            if (showNotice) ShowNotification("已断开蓝牙连接", string.Format("{0} [蓝牙]\n音频直通已关闭", devName), ToolTipIcon.Info);
        }

        public void DisconnectScrcpy(bool showNotice = true)
        {
            if (!IsScrcpyConnected && _scrcpyProc == null && string.IsNullOrEmpty(_currentScrcpyTarget)) return;

            string targetToMute = _currentScrcpyTarget;
            string devName = _currentScrcpyDeviceName ?? _settings.DeviceName;
            _currentScrcpyTarget = null;
            _currentScrcpyDeviceName = null;
            StopPhoneVolumeSync();
            StopBatteryMonitor();
            _lastBatteryInfo = null;
            if (_flyout != null) _flyout.UpdateBatteryUI(null);

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
            UpdateOverallState();
            if (_flyout != null) _flyout.SyncCurrentDeviceToUI();
            if (showNotice) ShowNotification("已断开连接", string.Format("{0}\n音频传输已停止", devName), ToolTipIcon.Info);
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
                _flyout.RefreshBatteryForSelectedDevice();
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
            if (string.IsNullOrEmpty(target)) return;

            DeviceHotkeyBinding binding = null;
            _settings.DeviceHotkeys.TryGetValue(target, out binding);
            string devName = (binding != null) ? binding.DisplayName : target;
            bool isBt = target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase);

            // If currently connected to THIS exact target -> Toggle Disconnect!
            if (IsTargetConnected(target))
            {
                Disconnect(target);
                ShowNotification("快捷键已触发", string.Format("已断开: {0}", devName), ToolTipIcon.Info);
            }
            else
            {
                // If connecting Bluetooth and another Bluetooth is connected -> disconnect that Bluetooth
                if (isBt && IsBluetoothConnected)
                {
                    DisconnectBluetooth(false);
                    Thread.Sleep(200);
                }
                // If connecting Scrcpy and another Scrcpy is connected -> disconnect that Scrcpy
                else if (!isBt && IsScrcpyConnected)
                {
                    DisconnectScrcpy(false);
                    Thread.Sleep(200);
                }

                _settings.Target = target;
                if (binding != null)
                {
                    _settings.DeviceName = binding.DeviceName;
                    if (!binding.IsUsb && !binding.IsBluetooth && target.Contains(":"))
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
                ConnectAsync(target, binding != null ? binding.DeviceName : null);
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

        public void SetNotificationMode(string mode)
        {
            if (mode != "osd" && mode != "windows" && mode != "none") mode = "osd";
            _settings.NotificationMode = mode;
            _settings.Save();
            SyncNotificationState();

            if (mode == "osd")
            {
                if (_volumeOsd != null) _volumeOsd.ShowHint("提示通知模式", "已切换为: 桌面 OSD 悬浮胶囊");
            }
            else if (mode == "windows")
            {
                if (_notifyIcon != null) _notifyIcon.ShowBalloonTip(2000, "提示通知模式", "已切换为: Windows 系统气泡通知", ToolTipIcon.Info);
            }
        }

        public void SyncNotificationState()
        {
            string mode = _settings.NotificationMode;
            if (_notifyMenuOsd != null) _notifyMenuOsd.Checked = (mode == "osd");
            if (_notifyMenuWin != null) _notifyMenuWin.Checked = (mode == "windows");
            if (_notifyMenuNone != null) _notifyMenuNone.Checked = (mode == "none");

            if (_flyout != null)
            {
                _flyout.UpdateNotificationSegmentUI(mode);
            }
        }

        public void ShowNotification(string title, string msg)
        {
            ShowNotification(title, msg, ToolTipIcon.Info);
        }

        public void ShowNotification(string title, string msg, ToolTipIcon icon)
        {
            if (_settings.NotificationMode == "none") return;

            // When Flyout panel is currently open and being interacted with, suppress popup notifications to avoid focus loss
            if (_flyout != null && _flyout.IsVisible) return;

            if (_settings.NotificationMode == "osd")
            {
                if (_volumeOsd != null)
                {
                    _volumeOsd.ShowHint(title, msg);
                }
            }
            else if (_settings.NotificationMode == "windows")
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(2000, title, msg, icon);
                }
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
                        }
                    }

                    // Always restore and sync the last saved MasterVolume to phone
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

                    _lastSliderSetTime = DateTime.Now;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_flyout != null) _flyout.UpdateVolumeUI(_settings.MasterVolume, _settings.IsMuted);
                    }));

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
                        string target = _currentScrcpyTarget;
                        if (string.IsNullOrEmpty(target) || !IsScrcpyConnected) break;
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

            // Sync to phone through serial throttled queue (only for ADB streaming)
            if (_settings.SyncPhoneVolume && IsScrcpyConnected && !string.IsNullOrEmpty(_currentScrcpyTarget))
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

        public void MutePhoneMediaQuick(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            try
            {
                string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
                // Mute and set volume to 0 without pausing media playback (for seamless hot-reloading)
                string shellCmd = "cmd audio set-volume 3 0; cmd audio adj-mute 3; cmd media_session volume --stream 3 --set 0";
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = string.Format("-s {0} shell \"{1}\"", target, shellCmd),
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(1000);
                }
            }
            catch { }
        }

        public BatteryInfo QueryPhoneBattery(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            if (target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase) ||
                target.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }
            try
            {
                string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = string.Format("-s {0} shell dumpsys battery", target),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1500);

                    var info = new BatteryInfo();
                    var mLevel = Regex.Match(outStr, @"level:\s*(\d+)");
                    if (mLevel.Success)
                    {
                        info.Level = int.Parse(mLevel.Groups[1].Value);
                    }

                    var mStatus = Regex.Match(outStr, @"status:\s*(\d+)");
                    int status = mStatus.Success ? int.Parse(mStatus.Groups[1].Value) : 0;

                    bool ac = Regex.IsMatch(outStr, @"AC powered:\s*true", RegexOptions.IgnoreCase);
                    bool usb = Regex.IsMatch(outStr, @"USB powered:\s*true", RegexOptions.IgnoreCase);
                    bool wireless = Regex.IsMatch(outStr, @"Wireless powered:\s*true", RegexOptions.IgnoreCase);

                    info.IsCharging = (status == 2) || ac || usb || wireless;
                    if (status == 5 || info.Level == 100) info.ChargeType = "充满";
                    else if (wireless) info.ChargeType = "无线";
                    else if (ac) info.ChargeType = "快充";
                    else if (usb) info.ChargeType = "USB";
                    else if (info.IsCharging) info.ChargeType = "充电中";

                    return info;
                }
            }
            catch { }
            return null;
        }

        public void StartBatteryMonitor(string target)
        {
            StopBatteryMonitor();
            if (string.IsNullOrEmpty(target) || !IsScrcpyConnected) return;

            _batteryCts = new CancellationTokenSource();
            var token = _batteryCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && IsScrcpyConnected)
                {
                    try
                    {
                        var info = QueryPhoneBattery(target);
                        if (info != null && info.Level >= 0)
                        {
                            _lastBatteryInfo = info;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_flyout != null)
                                {
                                    _flyout.UpdateBatteryUI(info);
                                }
                                UpdateTrayTooltipWithBattery();
                            }));

                            if (info.Level <= 20 && !info.IsCharging && !_hasAlertedLowBattery)
                            {
                                _hasAlertedLowBattery = true;
                                ShowNotification("手机低电量提醒", string.Format("手机当前电量为 {0}% (未充电)，请及时充电以防音频推流中断", info.Level), ToolTipIcon.Warning);
                            }
                            else if (info.Level > 25 || info.IsCharging)
                            {
                                _hasAlertedLowBattery = false;
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        await Task.Delay(30000, token);
                    }
                    catch { break; }
                }
            });
        }

        public void StopBatteryMonitor()
        {
            try
            {
                if (_batteryCts != null)
                {
                    _batteryCts.Cancel();
                    _batteryCts.Dispose();
                }
            }
            catch { }
            _batteryCts = null;
        }

        public void UpdateTrayTooltipWithBattery()
        {
            if (!IsConnected || _notifyIcon == null) return;
            try
            {
                bool scrcpyOn = IsScrcpyConnected;
                bool btOn = IsBluetoothConnected;

                if (scrcpyOn && btOn)
                {
                    string text = string.Format("WiFi 音频连接器 (双设备并发: {0} + {1})", _currentScrcpyDeviceName ?? "Wi-Fi", _currentBtDeviceName ?? "蓝牙");
                    _notifyIcon.Text = TruncateNotifyText(text);
                    return;
                }

                if (btOn)
                {
                    string btText = string.Format("WiFi 音频连接器 (已连接 [蓝牙]: {0})", _currentBtDeviceName ?? _settings.DeviceName);
                    _notifyIcon.Text = TruncateNotifyText(btText);
                    return;
                }

                if (scrcpyOn)
                {
                    string modeTag = (!string.IsNullOrEmpty(_currentScrcpyTarget) && _currentScrcpyTarget.Contains(":")) ? "Wi-Fi" : "USB";
                    string batStr = "";
                    if (_lastBatteryInfo != null && _lastBatteryInfo.Level >= 0)
                    {
                        batStr = string.Format(" {0}{1}%", _lastBatteryInfo.IsCharging ? "⚡" : "🔋", _lastBatteryInfo.Level);
                    }
                    string roleTag = _settings.MicDirectMode ? "手机麦克风设备" : "已连接";
                    string text = string.Format("WiFi 音频连接器 - {0} [{1}]{2} ({3})", _currentScrcpyDeviceName ?? _settings.DeviceName, modeTag, batStr, roleTag);
                    _notifyIcon.Text = TruncateNotifyText(text);
                }
            }
            catch { }
        }

        public void SendMediaKey(int keyCode, string actionName)
        {
            if (!IsScrcpyConnected)
            {
                if (_volumeOsd != null)
                {
                    _volumeOsd.ShowHint("媒体控制", IsBluetoothConnected ? "蓝牙直通模式请直接在手机端操作" : "未连接设备");
                }
                return;
            }

            string target = _currentScrcpyTarget;
            if (string.IsNullOrEmpty(target)) target = _settings.Target;
            if (string.IsNullOrEmpty(target)) return;

            if (_volumeOsd != null)
            {
                _volumeOsd.ShowHint("媒体控制", actionName);
            }

            Task.Run(() =>
            {
                try
                {
                    string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
                    string shellCmd = string.Format("input keyevent {0}", keyCode);
                    var psi = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = string.Format("-s {0} shell \"{1}\"", target, shellCmd),
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(1000);
                    }
                }
                catch { }
            });
        }

        public async Task<bool> ToggleCameraAsync()
        {
            if (IsCameraRunning)
            {
                StopCamera();
                return false;
            }
            else
            {
                return await StartCameraAsync();
            }
        }

        public async void ToggleCamera()
        {
            await ToggleCameraAsync();
        }

        public async Task<bool> StartCameraAsync(string specificTarget = null, string specificName = null)
        {
            if (IsCameraRunning)
            {
                StopCamera();
                await Task.Delay(200);
            }

            var sel = (_flyout != null) ? _flyout.GetSelectedDevice() : null;
            string target = !string.IsNullOrEmpty(specificTarget) ? specificTarget :
                (!string.IsNullOrEmpty(_currentScrcpyTarget) ? _currentScrcpyTarget :
                ((sel != null && !sel.IsBluetooth) ? sel.Target : _settings.Target));
            string devName = !string.IsNullOrEmpty(specificName) ? specificName :
                (!string.IsNullOrEmpty(_currentScrcpyDeviceName) ? _currentScrcpyDeviceName :
                ((sel != null && !sel.IsBluetooth) ? sel.Name : _settings.DeviceName));

            if (string.IsNullOrEmpty(target) || target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase))
            {
                ShowNotification("无法启动摄像头", "无线摄像头需要 Wi-Fi 或 USB 连接的安卓设备，当前选中的是蓝牙设备", ToolTipIcon.Warning);
                return false;
            }

            bool isTcp = target.Contains(":");
            string scrcpyPath = FindToolPath("scrcpy.exe");
            string adbPath = FindToolPath("adb.exe");

            // Ensure ADB connected if TCP/IP
            if (isTcp)
            {
                await Task.Run(() =>
                {
                    try
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
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                });
            }

            string facing = _settings.CameraFacing ?? "back";
            string size = _settings.CameraSize ?? "1920x1080";
            int fps = _settings.CameraFps > 0 ? _settings.CameraFps : 30;
            string topArg = _settings.CameraAlwaysOnTop ? "--always-on-top" : "";
            string titleArg = string.Format("--window-title=\"📷 手机无线摄像头 - [{0}]\"", devName);

            string args = string.Format("-s {0} --video-source=camera --camera-facing={1} --camera-size={2} --camera-fps={3} {4} --no-audio --window-width=640 --window-height=360 {5}",
                target, facing, size, fps, topArg, titleArg);

            bool ok = await Task.Run<bool>(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    };

                    _cameraProc = Process.Start(psi);
                    Thread.Sleep(1000);

                    // If resolution fails on some older phones, fallback to 720P automatically
                    if (_cameraProc == null || _cameraProc.HasExited)
                    {
                        string fallbackArgs = string.Format("-s {0} --video-source=camera --camera-facing={1} --camera-size=1280x720 --camera-fps=30 {2} --no-audio --window-width=640 --window-height=360 {3}",
                            target, facing, topArg, titleArg);
                        psi.Arguments = fallbackArgs;
                        _cameraProc = Process.Start(psi);
                        Thread.Sleep(1000);
                    }

                    return _cameraProc != null && !_cameraProc.HasExited;
                }
                catch (Exception ex)
                {
                    LogLine("StartCamera Exception: " + ex);
                    return false;
                }
            });

            if (ok)
            {
                UpdateCameraUI();
                ShowNotification("无线摄像头已启动", string.Format("{0}\n已开启无线摄像头画面 (1080P 30FPS)\n支持置顶/变焦/OBS采集", devName), ToolTipIcon.Info);

                var proc = _cameraProc;
                // Watchdog task
                Task.Run(() =>
                {
                    try
                    {
                        if (proc != null) proc.WaitForExit();
                    }
                    catch { }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_cameraProc == proc)
                        {
                            _cameraProc = null;
                            UpdateCameraUI();
                        }
                    }));
                });
                return true;
            }
            else
            {
                _cameraProc = null;
                UpdateCameraUI();
                ShowNotification("摄像头启动失败", "未能打开手机摄像头，请确认手机已解锁且相机权限正常", ToolTipIcon.Error);
                return false;
            }
        }

        public void StopCamera()
        {
            try
            {
                if (_cameraProc != null && !_cameraProc.HasExited)
                {
                    _cameraProc.Kill();
                    _cameraProc.WaitForExit(500);
                }
            }
            catch { }
            _cameraProc = null;
            UpdateCameraUI();
            ShowNotification("无线摄像头已关闭", "手机摄像头已停止工作", ToolTipIcon.Info);
        }

        public void UpdateCameraUI()
        {
            if (_flyout != null)
            {
                _flyout.UpdateCameraStateUI(IsCameraRunning);
            }
            if (_trayCameraItem != null)
            {
                _trayCameraItem.Text = IsCameraRunning ? "⏹ 关闭无线摄像头" : "📷 开启无线摄像头";
            }
            UpdateOverallState();
        }

        public async Task<bool> StartMicStreamAsync(string specificTarget = null, string specificName = null)
        {
            if (IsMicRunning)
            {
                return true;
            }

            var sel = (_flyout != null) ? _flyout.GetSelectedDevice() : null;
            string target = !string.IsNullOrEmpty(specificTarget) ? specificTarget :
                (!string.IsNullOrEmpty(_currentScrcpyTarget) ? _currentScrcpyTarget :
                ((sel != null && !sel.IsBluetooth) ? sel.Target : _settings.Target));
            string devName = !string.IsNullOrEmpty(specificName) ? specificName :
                (!string.IsNullOrEmpty(_currentScrcpyDeviceName) ? _currentScrcpyDeviceName :
                ((sel != null && !sel.IsBluetooth) ? sel.Name : _settings.DeviceName));

            if (string.IsNullOrEmpty(target) || target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool isTcp = target.Contains(":");
            string scrcpyPath = FindToolPath("scrcpy.exe");
            string adbPath = FindToolPath("adb.exe");

            string matchingMic = null;
            string vRender = WindowsAudioSessionController.FindVirtualAudioRenderDevice(out matchingMic);

            bool ok = await Task.Run<bool>(() =>
            {
                try
                {
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
                            p.WaitForExit(3000);
                        }
                    }

                    int bufferMs = 30;
                    string codecArg = "--audio-codec=raw";
                    string micArgs = string.Format("-s {0} --no-video --no-window {1} --audio-source=mic-voice-communication --audio-buffer={2}", target, codecArg, bufferMs);

                    var psi = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = micArgs,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    if (!string.IsNullOrEmpty(vRender))
                    {
                        psi.EnvironmentVariables["SDL_AUDIO_DEVICE_NAME"] = vRender;
                    }

                    _micProc = Process.Start(psi);
                    Thread.Sleep(1200);

                    if (_micProc == null || _micProc.HasExited)
                    {
                        string fallbackArgs = string.Format("-s {0} --no-video --no-window {1} --audio-source=mic --audio-buffer={2}", target, codecArg, bufferMs);
                        psi.Arguments = fallbackArgs;
                        _micProc = Process.Start(psi);
                        Thread.Sleep(1200);
                    }

                    return _micProc != null && !_micProc.HasExited;
                }
                catch (Exception ex)
                {
                    LogLine("StartMicStream Exception: " + ex);
                    return false;
                }
            });

            if (ok)
            {
                var proc = _micProc;
                Task.Run(() =>
                {
                    try
                    {
                        if (proc != null) proc.WaitForExit();
                    }
                    catch { }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_micProc == proc)
                        {
                            _micProc = null;
                            UpdateOverallState();
                        }
                    }));
                });

                string recTip = !string.IsNullOrEmpty(matchingMic) ? "\n录音输入请选择: 「手机麦克风设备」" : "\n电脑耳机监听输出 (可配合虚拟声卡开黑)";
                ShowNotification("手机麦克风已就绪", string.Format("{0}\n已开启手机麦克风直连电脑{1}\n(系统音频与麦克风同时工作，手机不外放)", devName, recTip), ToolTipIcon.Info);
                UpdateOverallState();
                return true;
            }
            else
            {
                _micProc = null;
                UpdateOverallState();
                ShowNotification("麦克风启动失败", "未能连接手机麦克风，请检查手机录音权限或连接状态", ToolTipIcon.Warning);
                return false;
            }
        }

        public void StopMicStream(bool showNotice = false)
        {
            try
            {
                if (_micProc != null && !_micProc.HasExited)
                {
                    _micProc.Kill();
                    _micProc.WaitForExit(500);
                }
            }
            catch { }
            _micProc = null;
            UpdateOverallState();
            if (showNotice)
            {
                ShowNotification("手机麦克风已断开", "手机麦克风直连已停止，手机系统音频正常传输", ToolTipIcon.Info);
            }
        }

        public void ExitApp()
        {
            Disconnect();
            StopCamera();
            StopMicStream();
            if (_btConnector != null)
            {
                _btConnector.Dispose();
                _btConnector = null;
            }

            if (_trayWheelController != null)
            {
                _trayWheelController.Dispose();
                _trayWheelController = null;
            }
            if (_volumeOsd != null)
            {
                _volumeOsd.Close();
                _volumeOsd = null;
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
        private Border _batteryBadge;
        private TextBlock _batteryText;
        private Button _btnConnect;
        private ComboBox _cbDevices;
        private Button _btnScan;
        private Button _btnSwitchUsb;
        private RadioButton _rbRaw;
        private RadioButton _rbOpus320;
        private RadioButton _rbOpus128;
        private RadioButton _rbLatencyGame;
        private RadioButton _rbLatencyBalanced;
        private RadioButton _rbLatencySmooth;
        private CheckBox _cbMutePhone;
        private Button _btnTriAudio;
        private Button _btnTriMic;
        private Button _btnTriCamera;
        private CheckBox _cbAutoConnect;
        private Button _btnNotifyOsd;
        private Button _btnNotifyWin;
        private Button _btnNotifyNone;
        private Slider _sliderVolume;
        private TextBlock _txtVolumePercent;
        private Button _btnMute;
        private Button _btnMediaPrev;
        private Button _btnMediaPlayPause;
        private Button _btnMediaNext;
        private bool _isUpdatingVolumeUI = false;
        private bool _isUserDragging = false;
        private TextBox _tbIp;
        private TextBox _tbPort;
        private TextBlock _tbColon;
        private Border _volumeCard;
        private Border _qualityCard;
        private bool _isProgrammaticTextChange = false;
        private TextBlock _txtHotkeyDisplay;
        private List<DeviceItem> _deviceList = new List<DeviceItem>();
        private bool _isAudioToggling = false;
        private bool _isMicToggling = false;
        private bool _isCameraToggling = false;
        private bool _isConnecting = false;
        private bool _isActionInProgress = false;

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
            Width = 380;
            Height = Math.Min(840, Math.Max(600, SystemParameters.WorkArea.Height - 40));
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            Deactivated += (s, e) =>
            {
                if (_app != null && _app.IsReloading) return;
                if (_isActionInProgress || _isAudioToggling || _isMicToggling || _isCameraToggling || _isConnecting) return;

                try
                {
                    var pt = System.Windows.Forms.Cursor.Position;
                    if (pt.X >= Left && pt.X <= Left + ActualWidth &&
                        pt.Y >= Top && pt.Y <= Top + ActualHeight)
                    {
                        return;
                    }
                }
                catch { }

                Hide();
            };

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

            _batteryBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(140, 20, 130, 75)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Collapsed
            };
            _batteryText = new TextBlock
            {
                Text = "🔋 --%",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White
            };
            _batteryBadge.Child = _batteryText;
            DockPanel.SetDock(_batteryBadge, Dock.Left);

            row1.Children.Add(_statusBadge);
            row1.Children.Add(_batteryBadge);
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
            string cbTemplateXaml = @"
                <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <DockPanel LastChildFill=""True"" Margin=""1"">
                        <TextBlock DockPanel.Dock=""Right"" Text=""{Binding StatusTag}"" Foreground=""#4ade80"" FontSize=""10"" FontWeight=""Bold"" Margin=""4,0,2,0"" VerticalAlignment=""Center""/>
                        <TextBlock Text=""{Binding ModeIcon}"" FontFamily=""{Binding ModeIconFont}"" Foreground=""{Binding ModeIconBrush}"" FontSize=""12"" VerticalAlignment=""Center"" Margin=""0,0,6,0"" Width=""16"" TextAlignment=""Center""/>
                        <TextBlock Text=""{Binding DisplayText}"" FontFamily=""Microsoft YaHei UI, Segoe UI"" FontSize=""11"" VerticalAlignment=""Center""/>
                    </DockPanel>
                </DataTemplate>";
            _cbDevices.ItemTemplate = (DataTemplate)XamlReader.Parse(cbTemplateXaml);
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
                if (_isProgrammaticTextChange) return;
                int p;
                if (int.TryParse(_tbPort.Text, out p))
                {
                    _app.CurrentSettings.Port = p;
                    var sel = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                    if (sel == null || (!sel.IsBluetooth && !sel.IsUsb))
                    {
                        _app.CurrentSettings.Target = string.Format("{0}:{1}", _app.CurrentSettings.DeviceIp, p);
                        _app.CurrentSettings.Save();
                    }
                }
            };

            _tbColon = new TextBlock
            {
                Text = " : ",
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(_tbPort, Dock.Right);
            DockPanel.SetDock(_tbColon, Dock.Right);

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
                if (_isProgrammaticTextChange) return;
                string ip = _tbIp.Text.Trim();
                _app.CurrentSettings.DeviceIp = ip;
                var sel = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                if (sel == null || (!sel.IsBluetooth && !sel.IsUsb))
                {
                    _app.CurrentSettings.Target = string.Format("{0}:{1}", ip, _app.CurrentSettings.Port);
                    _app.CurrentSettings.Save();
                }
            };
            DockPanel.SetDock(_tbIp, Dock.Right);

            ipRow.Children.Add(_tbPort);
            ipRow.Children.Add(_tbColon);
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
            _btnConnect.Click += async (s, e) =>
            {
                if (_isConnecting) return;
                _isConnecting = true;
                _isActionInProgress = true;
                _btnConnect.IsEnabled = false;

                var sel = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                string target = (sel != null && !string.IsNullOrEmpty(sel.Target)) ? sel.Target : _app.CurrentSettings.Target;
                string devName = (sel != null && !string.IsNullOrEmpty(sel.Name)) ? sel.Name : _app.CurrentSettings.DeviceName;

                bool isConnected = _app.IsTargetConnected(target);
                _btnConnect.Content = isConnected ? "断开中..." : "连接中...";
                _btnConnect.Background = new SolidColorBrush(Color.FromArgb(235, 217, 119, 6)); // Amber #D97706

                try
                {
                    if (isConnected)
                    {
                        _app.Disconnect(target);
                    }
                    else
                    {
                        await _app.ConnectAsync(target, devName);
                    }
                    await Task.Delay(400);
                }
                catch (Exception ex)
                {
                    App.LogLine("Connect button error: " + ex.Message);
                }
                finally
                {
                    _isConnecting = false;
                    _isActionInProgress = false;
                    _btnConnect.IsEnabled = true;
                    UpdateUIState();
                    try
                    {
                        Topmost = true;
                        Activate();
                    }
                    catch { }
                }
            };
            devicePanel.Children.Add(_btnConnect);

            // Three-channel quick control buttons: Audio, Mic, Camera
            var triGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            triGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            triGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
            triGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            triGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
            triGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnTriAudio = new Button
            {
                Content = "🔊 音频推流",
                Height = 30,
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI"),
                Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65)),
                Foreground = new SolidColorBrush(Color.FromArgb(220, 209, 213, 219)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "点击开启/关闭手机系统音频推流\n手机扬声器静音，电脑音箱同步播放"
            };
            _btnTriAudio.Click += async (s, e) =>
            {
                if (_isAudioToggling) return;
                _isAudioToggling = true;
                _isActionInProgress = true;
                _btnTriAudio.IsEnabled = false;

                bool isAudioOn = _app.IsScrcpyConnected || _app.IsBluetoothConnected;
                _btnTriAudio.Content = isAudioOn ? "🔊 关闭中..." : "🔊 开启中...";
                _btnTriAudio.Background = new SolidColorBrush(Color.FromArgb(235, 217, 119, 6)); // Amber #D97706
                _btnTriAudio.Foreground = System.Windows.Media.Brushes.White;

                try
                {
                    if (_app.IsScrcpyConnected)
                    {
                        _app.DisconnectScrcpy(true);
                    }
                    else if (_app.IsBluetoothConnected)
                    {
                        _app.DisconnectBluetooth(true);
                    }
                    else
                    {
                        var sel = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                        string target = (sel != null && !string.IsNullOrEmpty(sel.Target)) ? sel.Target : _app.CurrentSettings.Target;
                        string devName = (sel != null && !string.IsNullOrEmpty(sel.Name)) ? sel.Name : _app.CurrentSettings.DeviceName;
                        await _app.ConnectAsync(target, devName);
                    }
                    await Task.Delay(400);
                }
                catch (Exception ex)
                {
                    App.LogLine("TriAudio toggle error: " + ex.Message);
                }
                finally
                {
                    _isAudioToggling = false;
                    _isActionInProgress = false;
                    _btnTriAudio.IsEnabled = true;
                    UpdateTriButtonStates();
                    try
                    {
                        Topmost = true;
                        Activate();
                    }
                    catch { }
                }
            };

            _btnTriMic = new Button
            {
                Content = "🎙️ 麦克风直连",
                Height = 30,
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI"),
                Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65)),
                Foreground = new SolidColorBrush(Color.FromArgb(220, 209, 213, 219)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "点击开启/关闭手机麦克风直连电脑\n驱动已智能绑定: 手机麦克风设备\n开黑/会议录音输入请选: 「手机麦克风设备」"
            };
            _btnTriMic.Click += async (s, e) =>
            {
                if (_isMicToggling) return;
                _isMicToggling = true;
                _isActionInProgress = true;
                _btnTriMic.IsEnabled = false;

                bool micOn = _app.IsMicRunning;
                _btnTriMic.Content = micOn ? "🎙️ 关闭中..." : "🎙️ 开启中...";
                _btnTriMic.Background = new SolidColorBrush(Color.FromArgb(235, 217, 119, 6)); // Amber #D97706
                _btnTriMic.Foreground = System.Windows.Media.Brushes.White;

                try
                {
                    if (micOn)
                    {
                        _app.CurrentSettings.MicDirectMode = false;
                        _app.CurrentSettings.Save();
                        _app.StopMicStream(true);
                    }
                    else
                    {
                        _app.CurrentSettings.MicDirectMode = true;
                        _app.CurrentSettings.Save();
                        await _app.StartMicStreamAsync();
                    }
                    await Task.Delay(400);
                }
                catch (Exception ex)
                {
                    App.LogLine("TriMic toggle error: " + ex.Message);
                }
                finally
                {
                    _isMicToggling = false;
                    _isActionInProgress = false;
                    _btnTriMic.IsEnabled = true;
                    UpdateTriButtonStates();
                    try
                    {
                        Topmost = true;
                        Activate();
                    }
                    catch { }
                }
            };

            _btnTriCamera = new Button
            {
                Content = "📷 无线摄像头",
                Height = 30,
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI"),
                Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65)),
                Foreground = new SolidColorBrush(Color.FromArgb(220, 209, 213, 219)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "左键: 开关无线摄像头 (默认 1080P 30FPS)\n右键: 配置镜头方向、分辨率与帧率"
            };
            _btnTriCamera.Click += async (s, e) =>
            {
                if (_isCameraToggling) return;
                _isCameraToggling = true;
                _isActionInProgress = true;
                _btnTriCamera.IsEnabled = false;

                bool camOn = _app.IsCameraRunning;
                _btnTriCamera.Content = camOn ? "📷 关闭中..." : "📷 开启中...";
                _btnTriCamera.Background = new SolidColorBrush(Color.FromArgb(235, 217, 119, 6)); // Amber #D97706
                _btnTriCamera.Foreground = System.Windows.Media.Brushes.White;

                try
                {
                    if (camOn)
                    {
                        _app.StopCamera();
                    }
                    else
                    {
                        await _app.StartCameraAsync();
                    }
                    await Task.Delay(400);
                }
                catch (Exception ex)
                {
                    App.LogLine("TriCamera toggle error: " + ex.Message);
                }
                finally
                {
                    _isCameraToggling = false;
                    _isActionInProgress = false;
                    _btnTriCamera.IsEnabled = true;
                    UpdateTriButtonStates();
                    try
                    {
                        Topmost = true;
                        Activate();
                    }
                    catch { }
                }
            };

            // Setup right-click ContextMenu on Camera Button for resolution, lens, fps
            var camMenu = new System.Windows.Controls.ContextMenu();
            var itemFacing = new System.Windows.Controls.MenuItem { Header = _app.CurrentSettings.CameraFacing == "front" ? "📱 镜头: 前置自拍" : "📷 镜头: 后置主摄" };
            itemFacing.Click += async (s, e) =>
            {
                _app.CurrentSettings.CameraFacing = (_app.CurrentSettings.CameraFacing == "front") ? "back" : "front";
                _app.CurrentSettings.Save();
                itemFacing.Header = _app.CurrentSettings.CameraFacing == "front" ? "📱 镜头: 前置自拍" : "📷 镜头: 后置主摄";
                if (_app.IsCameraRunning) await _app.StartCameraAsync();
            };
            camMenu.Items.Add(itemFacing);

            var resMenu = new System.Windows.Controls.MenuItem { Header = "🖥️ 分辨率设置" };
            var r1080 = new System.Windows.Controls.MenuItem { Header = "1080P 推荐 (默认)", IsChecked = _app.CurrentSettings.CameraSize == "1920x1080" };
            var r4k = new System.Windows.Controls.MenuItem { Header = "4K 极清", IsChecked = _app.CurrentSettings.CameraSize == "3840x2160" };
            var r720 = new System.Windows.Controls.MenuItem { Header = "720P 极速", IsChecked = _app.CurrentSettings.CameraSize == "1280x720" };
            r1080.Click += async (s, e) => { _app.CurrentSettings.CameraSize = "1920x1080"; _app.CurrentSettings.Save(); r1080.IsChecked = true; r4k.IsChecked = false; r720.IsChecked = false; if (_app.IsCameraRunning) await _app.StartCameraAsync(); };
            r4k.Click += async (s, e) => { _app.CurrentSettings.CameraSize = "3840x2160"; _app.CurrentSettings.Save(); r4k.IsChecked = true; r1080.IsChecked = false; r720.IsChecked = false; if (_app.IsCameraRunning) await _app.StartCameraAsync(); };
            r720.Click += async (s, e) => { _app.CurrentSettings.CameraSize = "1280x720"; _app.CurrentSettings.Save(); r720.IsChecked = true; r1080.IsChecked = false; r4k.IsChecked = false; if (_app.IsCameraRunning) await _app.StartCameraAsync(); };
            resMenu.Items.Add(r1080);
            resMenu.Items.Add(r4k);
            resMenu.Items.Add(r720);
            camMenu.Items.Add(resMenu);

            var fpsMenu = new System.Windows.Controls.MenuItem { Header = "⚡ 帧率设置" };
            var fps30 = new System.Windows.Controls.MenuItem { Header = "30 FPS (默认推荐)", IsChecked = _app.CurrentSettings.CameraFps == 30 };
            var fps60 = new System.Windows.Controls.MenuItem { Header = "60 FPS 极速", IsChecked = _app.CurrentSettings.CameraFps == 60 };
            fps30.Click += async (s, e) => { _app.CurrentSettings.CameraFps = 30; _app.CurrentSettings.Save(); fps30.IsChecked = true; fps60.IsChecked = false; if (_app.IsCameraRunning) await _app.StartCameraAsync(); };
            fps60.Click += async (s, e) => { _app.CurrentSettings.CameraFps = 60; _app.CurrentSettings.Save(); fps60.IsChecked = true; fps30.IsChecked = false; if (_app.IsCameraRunning) await _app.StartCameraAsync(); };
            fpsMenu.Items.Add(fps30);
            fpsMenu.Items.Add(fps60);
            camMenu.Items.Add(fpsMenu);

            var itemTop = new System.Windows.Controls.MenuItem { Header = "📌 窗口置顶", IsChecked = _app.CurrentSettings.CameraAlwaysOnTop };
            itemTop.Click += async (s, e) =>
            {
                _app.CurrentSettings.CameraAlwaysOnTop = !_app.CurrentSettings.CameraAlwaysOnTop;
                _app.CurrentSettings.Save();
                itemTop.IsChecked = _app.CurrentSettings.CameraAlwaysOnTop;
                if (_app.IsCameraRunning) await _app.StartCameraAsync();
            };
            camMenu.Items.Add(itemTop);

            _btnTriCamera.ContextMenu = camMenu;

            Grid.SetColumn(_btnTriAudio, 0);
            Grid.SetColumn(_btnTriMic, 2);
            Grid.SetColumn(_btnTriCamera, 4);

            triGrid.Children.Add(_btnTriAudio);
            triGrid.Children.Add(_btnTriMic);
            triGrid.Children.Add(_btnTriCamera);
            devicePanel.Children.Add(triGrid);

            deviceCard.Child = devicePanel;
            root.Children.Add(deviceCard);

            // Volume Control Card
            _volumeCard = new Border
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

            // Media Control Quick Buttons
            var mediaRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            mediaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mediaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mediaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnMediaPrev = CreateMediaButton("⏮ 上一首", () => _app.SendMediaKey(88, "⏮ 上一首"));
            _btnMediaPlayPause = CreateMediaButton("⏯ 播放/暂停", () => _app.SendMediaKey(85, "⏯ 播放 / 暂停"));
            _btnMediaNext = CreateMediaButton("⏭ 下一首", () => _app.SendMediaKey(87, "⏭ 下一首"));

            Grid.SetColumn(_btnMediaPrev, 0);
            Grid.SetColumn(_btnMediaPlayPause, 1);
            Grid.SetColumn(_btnMediaNext, 2);

            mediaRow.Children.Add(_btnMediaPrev);
            mediaRow.Children.Add(_btnMediaPlayPause);
            mediaRow.Children.Add(_btnMediaNext);
            volumePanel.Children.Add(mediaRow);

            _volumeCard.Child = volumePanel;
            root.Children.Add(_volumeCard);

            // Audio Quality & Latency Settings Card
            _qualityCard = new Border
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
                GroupName = "CodecGroup",
                Content = "Raw PCM (16-bit 48kHz 原生无损直通 - 推荐)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
                IsChecked = (_app.CurrentSettings.Codec == "raw")
            };
            _rbRaw.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.Codec == "raw") return;
                _app.CurrentSettings.Codec = "raw";
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已切换音质: Raw PCM 原生无损");
                }
            };
            qualityPanel.Children.Add(_rbRaw);

            _rbOpus320 = new RadioButton
            {
                GroupName = "CodecGroup",
                Content = "Opus 320K (高码率广播级，极低带宽占用)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
                IsChecked = (_app.CurrentSettings.Codec == "opus320")
            };
            _rbOpus320.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.Codec == "opus320") return;
                _app.CurrentSettings.Codec = "opus320";
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已切换音质: Opus 320K 广播级");
                }
            };
            qualityPanel.Children.Add(_rbOpus320);

            _rbOpus128 = new RadioButton
            {
                GroupName = "CodecGroup",
                Content = "Opus 128K (极限低延迟与省电)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = (_app.CurrentSettings.Codec == "opus128")
            };
            _rbOpus128.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.Codec == "opus128") return;
                _app.CurrentSettings.Codec = "opus128";
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已切换音质: Opus 128K 极限省电");
                }
            };
            qualityPanel.Children.Add(_rbOpus128);

            qualityPanel.Children.Add(new Separator
            {
                Margin = new Thickness(0, 7, 0, 7),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
            });

            qualityPanel.Children.Add(new TextBlock
            {
                Text = "音频缓冲延迟档位",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _rbLatencyGame = new RadioButton
            {
                GroupName = "LatencyGroup",
                Content = "⚡ 电竞极速档 (30ms - 音画近乎完全同步)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
                IsChecked = (_app.CurrentSettings.LatencyMode == "game")
            };
            _rbLatencyGame.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.LatencyMode == "game") return;
                _app.CurrentSettings.LatencyMode = "game";
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已切换延迟: 电竞极速档 (30ms)");
                }
            };
            qualityPanel.Children.Add(_rbLatencyGame);

            _rbLatencyBalanced = new RadioButton
            {
                GroupName = "LatencyGroup",
                Content = "⚖ 均衡模式 (50ms - 兼顾流畅与抗波动 - 推荐默认)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
                IsChecked = (_app.CurrentSettings.LatencyMode == "balanced" || string.IsNullOrEmpty(_app.CurrentSettings.LatencyMode))
            };
            _rbLatencyBalanced.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.LatencyMode == "balanced") return;
                _app.CurrentSettings.LatencyMode = "balanced";
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已切换延迟: 均衡推荐档 (50ms)");
                }
            };
            qualityPanel.Children.Add(_rbLatencyBalanced);

            _rbLatencySmooth = new RadioButton
            {
                GroupName = "LatencyGroup",
                Content = "🛡 穿墙防卡顿档 (80ms - 针对 2.4G Wi-Fi 与弱网环境)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = (_app.CurrentSettings.LatencyMode == "smooth")
            };
            _rbLatencySmooth.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.LatencyMode == "smooth") return;
                _app.CurrentSettings.LatencyMode = "smooth";
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已切换延迟: 穿墙防卡顿档 (80ms)");
                }
            };
            qualityPanel.Children.Add(_rbLatencySmooth);

            qualityPanel.Children.Add(new Separator
            {
                Margin = new Thickness(0, 7, 0, 7),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
            });

            _cbMutePhone = new CheckBox
            {
                Content = "手机扬声器静音 (仅电脑端音箱/耳机播放)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 0),
                IsChecked = _app.CurrentSettings.MutePhone
            };
            _cbMutePhone.Checked += (s, e) =>
            {
                if (_app.CurrentSettings.MutePhone) return;
                _app.CurrentSettings.MutePhone = true;
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已开启: 手机扬声器静音");
                }
            };
            _cbMutePhone.Unchecked += (s, e) =>
            {
                if (!_app.CurrentSettings.MutePhone) return;
                _app.CurrentSettings.MutePhone = false;
                _app.CurrentSettings.Save();
                if (_app.IsScrcpyConnected)
                {
                    _app.ReloadAudioStreamAsync("已开启: 手机与电脑同时发声");
                }
            };
            qualityPanel.Children.Add(_cbMutePhone);

            _qualityCard.Child = qualityPanel;
            root.Children.Add(_qualityCard);

            // Output Options / Notification & General Settings Card (Directly below Camera!)
            var optsCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 42, 45, 54)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var optsPanel = new StackPanel();

            // 提示通知模式 (连体开关: 桌面 OSD / Windows / 关闭)
            var notifyHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var tbNotifyTitle = new TextBlock
            {
                Text = "提示通知方式:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 220, 225, 235)),
                VerticalAlignment = VerticalAlignment.Center
            };
            notifyHeader.Children.Add(tbNotifyTitle);
            optsPanel.Children.Add(notifyHeader);

            var segBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 25, 28, 36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var segGrid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 3 };
            _btnNotifyOsd = CreateNotifySegmentButton("桌面 OSD", "osd");
            _btnNotifyWin = CreateNotifySegmentButton("Windows", "windows");
            _btnNotifyNone = CreateNotifySegmentButton("关闭", "none");

            segGrid.Children.Add(_btnNotifyOsd);
            segGrid.Children.Add(_btnNotifyWin);
            segGrid.Children.Add(_btnNotifyNone);
            segBorder.Child = segGrid;
            optsPanel.Children.Add(segBorder);

            UpdateNotificationSegmentUI(_app.CurrentSettings.NotificationMode);

            _cbAutoConnect = new CheckBox
            {
                Content = "开机自启并自动连接当前设备",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = _app.CurrentSettings.AutoConnect
            };
            _cbAutoConnect.Checked += (s, e) => { _app.CurrentSettings.AutoConnect = true; _app.CurrentSettings.Save(); };
            _cbAutoConnect.Unchecked += (s, e) => { _app.CurrentSettings.AutoConnect = false; _app.CurrentSettings.Save(); };
            optsPanel.Children.Add(_cbAutoConnect);

            // Hotkey row
            var hotkeyRow = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
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

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Content = root
            };
            mainBorder.Child = scroll;
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

            if (!hasCurrent && !string.IsNullOrEmpty(_app.CurrentSettings.Target))
            {
                bool isBtTarget = _app.CurrentSettings.Target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || _app.CurrentSettings.Target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase);
                _deviceList.Add(new DeviceItem
                {
                    Name = _app.CurrentSettings.DeviceName + " (离线)",
                    Target = _app.CurrentSettings.Target,
                    Ip = isBtTarget ? "" : _app.CurrentSettings.DeviceIp,
                    Port = isBtTarget ? 0 : _app.CurrentSettings.Port,
                    IsUsb = !isBtTarget && !_app.CurrentSettings.Target.Contains(":"),
                    IsBluetooth = isBtTarget,
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
            int selIdx = -1;
            for (int i = 0; i < _deviceList.Count; i++)
            {
                if (_deviceList[i].Target == _app.CurrentSettings.Target)
                {
                    selIdx = i;
                    break;
                }
            }
            if (selIdx < 0 && _deviceList.Count > 0)
            {
                // Prefer first online device (not custom, not offline)
                for (int i = 0; i < _deviceList.Count; i++)
                {
                    if (!_deviceList[i].IsCustom && !_deviceList[i].Name.Contains("(离线)"))
                    {
                        selIdx = i;
                        break;
                    }
                }
                if (selIdx < 0) selIdx = 0;
            }
            _cbDevices.SelectedIndex = Math.Max(0, selIdx);
            var curSel = _cbDevices.SelectedItem as DeviceItem;
            if (curSel != null)
            {
                UpdateScrcpyControlsState(curSel.IsBluetooth, curSel.IsUsb);
                RefreshBatteryForSelectedDevice(curSel);
            }

            _btnScan.IsEnabled = true;
            _btnScan.Content = "🔄 扫描";
            _btnScan.ToolTip = string.Format("上次扫描: 发现 {0} 台设备\n局域网网段: {1}\n扫描端口: {2}",
                discovered.Count,
                AdbLanScanner.LastScannedSubnet ?? "未检测到",
                string.Join(", ", _app.CurrentSettings.GetScanPortsList()));
        }

        public void UpdateScrcpyControlsState(bool isBluetooth, bool isUsb)
        {
            bool isScrcpy = !isBluetooth;
            if (_volumeCard != null)
            {
                _volumeCard.IsEnabled = isScrcpy;
                _volumeCard.Opacity = isScrcpy ? 1.0 : 0.35;
            }
            if (_qualityCard != null)
            {
                _qualityCard.IsEnabled = isScrcpy;
                _qualityCard.Opacity = isScrcpy ? 1.0 : 0.35;
            }
            if (_cbMutePhone != null)
            {
                _cbMutePhone.IsEnabled = isScrcpy;
                _cbMutePhone.Opacity = isScrcpy ? 1.0 : 0.35;
            }
            if (_btnTriMic != null)
            {
                _btnTriMic.IsEnabled = isScrcpy;
                _btnTriMic.Opacity = isScrcpy ? 1.0 : 0.4;
            }
            if (_btnTriCamera != null)
            {
                _btnTriCamera.IsEnabled = isScrcpy;
                _btnTriCamera.Opacity = isScrcpy ? 1.0 : 0.4;
            }
            if (_tbColon != null)
            {
                _tbColon.Visibility = (isBluetooth || isUsb) ? Visibility.Collapsed : Visibility.Visible;
            }
            if (_tbPort != null)
            {
                _tbPort.Visibility = (isBluetooth || isUsb) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public DeviceItem GetSelectedDevice()
        {
            return _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
        }

        private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = _cbDevices.SelectedItem as DeviceItem;
            if (item == null) return;

            UpdateScrcpyControlsState(item.IsBluetooth, item.IsUsb);
            RefreshBatteryForSelectedDevice(item);

            // Update connect button text and color based on this specific device's connection status
            bool isSelConn = _app.IsTargetConnected(item.Target);
            if (isSelConn)
            {
                _btnConnect.Content = "断开此设备";
                _btnConnect.Background = new SolidColorBrush(Color.FromArgb(220, 215, 60, 60));
            }
            else
            {
                _btnConnect.Content = (_app.IsScrcpyConnected || _app.IsBluetoothConnected) ? "连接此设备 (双路并发)" : "一键连接";
                _btnConnect.Background = new SolidColorBrush(Color.FromArgb(255, 20, 120, 240));
            }

            if (item.IsCustom)
            {
                _isProgrammaticTextChange = true;
                try
                {
                    _tbIp.Text = _app.CurrentSettings.DeviceIp;
                    _tbPort.Text = _app.CurrentSettings.Port.ToString();
                }
                finally { _isProgrammaticTextChange = false; }

                _tbIp.IsEnabled = true;
                _tbPort.IsEnabled = true;
                _btnSwitchUsb.Visibility = Visibility.Collapsed;
            }
            else if (item.IsBluetooth)
            {
                _isProgrammaticTextChange = true;
                try
                {
                    _tbIp.Text = "Bluetooth A2DP";
                    _tbPort.Text = "";
                }
                finally { _isProgrammaticTextChange = false; }

                _tbIp.IsEnabled = false;
                _tbPort.IsEnabled = false;
                _btnSwitchUsb.Visibility = Visibility.Collapsed;

                _app.CurrentSettings.DeviceName = item.Name;
                _app.CurrentSettings.Target = item.Target;

                if (!_app.CurrentSettings.DeviceHotkeys.ContainsKey(item.Target))
                {
                    _app.CurrentSettings.DeviceHotkeys[item.Target] = new DeviceHotkeyBinding
                    {
                        Target = item.Target,
                        DeviceName = item.Name,
                        IsUsb = false,
                        IsBluetooth = true,
                        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                        Key = Key.D3,
                        Enabled = true
                    };
                    _app.ApplyAllHotkeys();
                }

                _app.CurrentSettings.Save();
                UpdateHotkeyText();
            }
            else
            {
                _isProgrammaticTextChange = true;
                try
                {
                    if (item.IsUsb)
                    {
                        _tbIp.Text = item.Target;
                        _tbPort.Text = "";
                    }
                    else
                    {
                        _tbIp.Text = item.Ip;
                        _tbPort.Text = item.Port.ToString();
                    }
                }
                finally { _isProgrammaticTextChange = false; }

                _tbIp.IsEnabled = !item.IsUsb;
                _tbPort.IsEnabled = !item.IsUsb;
                _btnSwitchUsb.Visibility = item.IsUsb ? Visibility.Visible : Visibility.Collapsed;

                _app.CurrentSettings.DeviceName = item.Name;
                _app.CurrentSettings.Target = item.Target;

                if (!item.IsUsb && !string.IsNullOrEmpty(item.Ip))
                {
                    _app.CurrentSettings.DeviceIp = item.Ip;
                    _app.CurrentSettings.Port = item.Port;
                }

                _app.CurrentSettings.Save();
                UpdateHotkeyText();
            }
        }

        public void UpdateUIState()
        {
            Action act = () =>
            {
                bool scrcpyOn = _app.IsScrcpyConnected;
                bool btOn = _app.IsBluetoothConnected;
                bool micOn = _app.IsMicRunning;
                bool camOn = _app.IsCameraRunning;

                if (scrcpyOn && btOn)
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(220, 16, 185, 129));
                    _statusText.Text = string.Format("双路并发中 ({0} + {1})", _app.CurrentScrcpyDeviceName ?? "Wi-Fi", _app.CurrentBtDeviceName ?? "蓝牙");
                }
                else if (scrcpyOn)
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(200, 35, 170, 75));
                    string extra = micOn ? " + 麦克风" : (camOn ? " + 摄像头" : "");
                    string codecStr = _app.CurrentSettings.Codec == "raw" ? "Raw PCM 无损" : "Opus";
                    _statusText.Text = string.Format("已连接 ({0}{1})", codecStr, extra);
                }
                else if (btOn)
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(200, 35, 170, 75));
                    _statusText.Text = string.Format("已连接 (蓝牙 A2DP: {0})", _app.CurrentBtDeviceName ?? "设备");
                }
                else if (micOn || camOn)
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(200, 35, 170, 75));
                    string modes = (micOn && camOn) ? "麦克风 + 摄像头" : (micOn ? "麦克风直连" : "无线摄像头");
                    _statusText.Text = string.Format("已开启 ({0})", modes);
                }
                else
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100));
                    _statusText.Text = "未连接";
                }

                var sel = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                bool isSelConnected = (sel != null && !string.IsNullOrEmpty(sel.Target) && _app.IsTargetConnected(sel.Target));

                if (!_isConnecting)
                {
                    if (isSelConnected)
                    {
                        _btnConnect.Content = "断开此设备";
                        _btnConnect.Background = new SolidColorBrush(Color.FromArgb(220, 215, 60, 60));
                    }
                    else
                    {
                        _btnConnect.Content = (scrcpyOn || btOn) ? "连接此设备 (双路并发)" : "一键连接";
                        _btnConnect.Background = new SolidColorBrush(Color.FromArgb(255, 20, 120, 240));
                    }
                }

                if (sel != null)
                {
                    UpdateScrcpyControlsState(sel.IsBluetooth, sel.IsUsb);
                    RefreshBatteryForSelectedDevice(sel);
                }
                else
                {
                    if (_batteryBadge != null) _batteryBadge.Visibility = Visibility.Collapsed;
                }

                if (_btnMediaPrev != null && _btnMediaPlayPause != null && _btnMediaNext != null)
                {
                    bool mediaEnabled = scrcpyOn;
                    _btnMediaPrev.IsEnabled = mediaEnabled;
                    _btnMediaPlayPause.IsEnabled = mediaEnabled;
                    _btnMediaNext.IsEnabled = mediaEnabled;
                    double op = mediaEnabled ? 1.0 : 0.4;
                    _btnMediaPrev.Opacity = op;
                    _btnMediaPlayPause.Opacity = op;
                    _btnMediaNext.Opacity = op;
                }

                if (_cbDevices != null && _deviceList != null)
                {
                    try
                    {
                        var curSel = _cbDevices.SelectedItem;
                        _cbDevices.Items.Refresh();
                        _cbDevices.SelectedItem = curSel;
                    }
                    catch { }
                }

                UpdateTriButtonStates();
            };

            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }

        public void UpdateState(ConnectionState state)
        {
            if (state == ConnectionState.Connecting)
            {
                Action act = () =>
                {
                    _statusBadge.Background = new SolidColorBrush(Color.FromArgb(200, 215, 150, 20));
                    _statusText.Text = "正在连接...";
                    _btnConnect.Content = "连接中...";
                    _btnConnect.Background = new SolidColorBrush(Color.FromArgb(180, 120, 120, 120));
                    if (_batteryBadge != null) _batteryBadge.Visibility = Visibility.Collapsed;
                };
                if (CheckAccess()) act();
                else Dispatcher.BeginInvoke(act);
            }
            else
            {
                UpdateUIState();
            }
        }

        private Button CreateMediaButton(string text, Action onClick)
        {
            var btn = new Button
            {
                Content = text,
                Height = 26,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0)
            };
            btn.Click += (s, e) => onClick();
            return btn;
        }

        public void RefreshBatteryForSelectedDevice(DeviceItem item = null)
        {
            if (item == null)
            {
                item = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
            }

            if (item == null || item.IsBluetooth || item.IsCustom || string.IsNullOrEmpty(item.Target))
            {
                if (_batteryBadge != null) _batteryBadge.Visibility = Visibility.Collapsed;
                return;
            }

            if (_app.IsConnected && string.Equals(_app.CurrentActiveTarget, item.Target, StringComparison.OrdinalIgnoreCase) &&
                _app.LastBatteryInfo != null && _app.LastBatteryInfo.Level >= 0)
            {
                UpdateBatteryUI(_app.LastBatteryInfo);
            }

            string queryTarget = item.Target;
            Task.Run(() =>
            {
                var info = _app.QueryPhoneBattery(queryTarget);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var cur = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                    if (cur != null && string.Equals(cur.Target, queryTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        if (info != null && info.Level >= 0)
                        {
                            UpdateBatteryUI(info);
                            if (_app.IsConnected && string.Equals(_app.CurrentActiveTarget, queryTarget, StringComparison.OrdinalIgnoreCase))
                            {
                                _app.LastBatteryInfo = info;
                                _app.UpdateTrayTooltipWithBattery();
                            }
                        }
                        else
                        {
                            if (_batteryBadge != null) _batteryBadge.Visibility = Visibility.Collapsed;
                        }
                    }
                }));
            });
        }

        public void UpdateBatteryUI(BatteryInfo info)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_batteryBadge == null || _batteryText == null) return;
                var cur = _cbDevices != null ? _cbDevices.SelectedItem as DeviceItem : null;
                if (cur != null && (cur.IsBluetooth || cur.IsCustom || string.IsNullOrEmpty(cur.Target)))
                {
                    _batteryBadge.Visibility = Visibility.Collapsed;
                    return;
                }
                if (info == null || info.Level < 0)
                {
                    _batteryBadge.Visibility = Visibility.Collapsed;
                    return;
                }

                _batteryBadge.Visibility = Visibility.Visible;
                string icon = info.IsCharging ? "⚡" : (info.Level <= 20 ? "🪫" : "🔋");
                string chargeDesc = "未充电";
                if (info.IsCharging)
                {
                    if (info.ChargeType == "充满") chargeDesc = "已充满";
                    else if (info.ChargeType == "无线") chargeDesc = "无线充电中";
                    else if (info.ChargeType == "快充") chargeDesc = "快充中";
                    else if (info.ChargeType == "USB") chargeDesc = "USB充电中";
                    else chargeDesc = "充电中";
                }
                else if (info.Level <= 20)
                {
                    chargeDesc = "电量偏低";
                }

                _batteryText.Text = string.Format("{0} {1}% ({2})", icon, info.Level, chargeDesc);

                if (info.IsCharging)
                {
                    _batteryBadge.Background = new SolidColorBrush(Color.FromArgb(140, 20, 130, 75));
                }
                else if (info.Level <= 20)
                {
                    _batteryBadge.Background = new SolidColorBrush(Color.FromArgb(180, 200, 50, 45));
                }
                else
                {
                    _batteryBadge.Background = new SolidColorBrush(Color.FromArgb(130, 40, 65, 95));
                }
            }));
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
                            UpdateScrcpyControlsState(_deviceList[i].IsBluetooth, _deviceList[i].IsUsb);
                            RefreshBatteryForSelectedDevice(_deviceList[i]);
                            break;
                        }
                    }
                }

                _isProgrammaticTextChange = true;
                try
                {
                    bool isBt = _app.IsBluetoothConnected || (_app.CurrentSettings.Target != null && (_app.CurrentSettings.Target.StartsWith(@"\\?\BTHENUM", StringComparison.OrdinalIgnoreCase) || _app.CurrentSettings.Target.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase)));
                    if (isBt)
                    {
                        if (_tbIp != null) _tbIp.Text = "Bluetooth A2DP";
                        if (_tbPort != null) _tbPort.Text = "";
                        UpdateScrcpyControlsState(true, false);
                    }
                    else
                    {
                        bool isUsb = !string.IsNullOrEmpty(_app.CurrentSettings.Target) && !_app.CurrentSettings.Target.Contains(":");
                        if (_tbIp != null) _tbIp.Text = isUsb ? _app.CurrentSettings.Target : _app.CurrentSettings.DeviceIp;
                        if (_tbPort != null) _tbPort.Text = isUsb ? "" : _app.CurrentSettings.Port.ToString();
                        UpdateScrcpyControlsState(false, isUsb);
                    }
                }
                finally
                {
                    _isProgrammaticTextChange = false;
                }

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

        private Button CreateNotifySegmentButton(string text, string mode)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 160, 170, 190)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Height = 22
            };
            btn.Click += (s, e) => _app.SetNotificationMode(mode);
            return btn;
        }

        public void UpdateNotificationSegmentUI(string mode)
        {
            Action act = () =>
            {
                if (_btnNotifyOsd == null || _btnNotifyWin == null || _btnNotifyNone == null) return;

                var activeBg = new SolidColorBrush(Color.FromArgb(240, 20, 120, 240));
                var activeFg = System.Windows.Media.Brushes.White;
                var inactiveBg = System.Windows.Media.Brushes.Transparent;
                var inactiveFg = new SolidColorBrush(Color.FromArgb(180, 160, 170, 190));

                _btnNotifyOsd.Background = (mode == "osd") ? activeBg : inactiveBg;
                _btnNotifyOsd.Foreground = (mode == "osd") ? activeFg : inactiveFg;
                _btnNotifyOsd.FontWeight = (mode == "osd") ? FontWeights.Bold : FontWeights.Normal;

                _btnNotifyWin.Background = (mode == "windows") ? activeBg : inactiveBg;
                _btnNotifyWin.Foreground = (mode == "windows") ? activeFg : inactiveFg;
                _btnNotifyWin.FontWeight = (mode == "windows") ? FontWeights.Bold : FontWeights.Normal;

                _btnNotifyNone.Background = (mode == "none") ? activeBg : inactiveBg;
                _btnNotifyNone.Foreground = (mode == "none") ? activeFg : inactiveFg;
                _btnNotifyNone.FontWeight = (mode == "none") ? FontWeights.Bold : FontWeights.Normal;
            };
            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }

        public void SyncNotificationCheckbox()
        {
            UpdateNotificationSegmentUI(_app.CurrentSettings.NotificationMode);
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

        public void UpdateCameraStateUI(bool isRunning)
        {
            Action act = () =>
            {
                UpdateTriButtonStates();
            };
            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }

        public void UpdateTriButtonStates()
        {
            Action act = () =>
            {
                bool audioOn = _app.IsScrcpyConnected || _app.IsBluetoothConnected;
                bool micOn = _app.IsMicRunning;
                bool camOn = _app.IsCameraRunning;

                // 1. Audio Button
                if (_btnTriAudio != null && !_isAudioToggling)
                {
                    if (audioOn)
                    {
                        _btnTriAudio.Content = "🔊 音频已开";
                        _btnTriAudio.Background = new SolidColorBrush(Color.FromArgb(235, 37, 99, 235)); // #2563EB Vibrant Blue
                        _btnTriAudio.Foreground = System.Windows.Media.Brushes.White;
                        _btnTriAudio.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        _btnTriAudio.Content = "🔊 音频推流";
                        _btnTriAudio.Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65)); // Neutral Dark Slate
                        _btnTriAudio.Foreground = new SolidColorBrush(Color.FromArgb(220, 209, 213, 219));
                        _btnTriAudio.FontWeight = FontWeights.Normal;
                    }
                }

                // 2. Microphone Button
                if (_btnTriMic != null && !_isMicToggling)
                {
                    if (micOn)
                    {
                        _btnTriMic.Content = "🎙️ 麦克风已开";
                        _btnTriMic.Background = new SolidColorBrush(Color.FromArgb(235, 16, 185, 129)); // #10B981 Emerald Green
                        _btnTriMic.Foreground = System.Windows.Media.Brushes.White;
                        _btnTriMic.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        _btnTriMic.Content = "🎙️ 麦克风直连";
                        _btnTriMic.Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65));
                        _btnTriMic.Foreground = new SolidColorBrush(Color.FromArgb(220, 209, 213, 219));
                        _btnTriMic.FontWeight = FontWeights.Normal;
                    }

                    string matchingMic;
                    string vRender = WindowsAudioSessionController.FindVirtualAudioRenderDevice(out matchingMic);
                    if (!string.IsNullOrEmpty(vRender))
                    {
                        _btnTriMic.ToolTip = "点击开启/关闭手机麦克风直连电脑\n驱动已智能绑定: 手机麦克风设备\n开黑/会议录音输入请选: 「手机麦克风设备」";
                    }
                    else
                    {
                        _btnTriMic.ToolTip = "点击开启/关闭手机麦克风直连电脑\n未检测到手机麦克风驱动 (当前由电脑耳机监听播放)\n安装虚拟驱动即可在微信/会议/游戏内直接录音";
                    }
                }

                // 3. Camera Button
                if (_btnTriCamera != null && !_isCameraToggling)
                {
                    if (camOn)
                    {
                        _btnTriCamera.Content = "📷 摄像头已开";
                        _btnTriCamera.Background = new SolidColorBrush(Color.FromArgb(235, 147, 51, 234)); // #9333EA Purple
                        _btnTriCamera.Foreground = System.Windows.Media.Brushes.White;
                        _btnTriCamera.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        _btnTriCamera.Content = "📷 无线摄像头";
                        _btnTriCamera.Background = new SolidColorBrush(Color.FromArgb(160, 48, 52, 65));
                        _btnTriCamera.Foreground = new SolidColorBrush(Color.FromArgb(220, 209, 213, 219));
                        _btnTriCamera.FontWeight = FontWeights.Normal;
                    }
                }
            };

            if (CheckAccess()) act();
            else Dispatcher.BeginInvoke(act);
        }
    }
}
