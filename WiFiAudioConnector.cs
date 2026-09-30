using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

    public class Settings
    {
        public string DeviceName = "小米 15 Pro";
        public string DeviceIp = "192.168.31.238";
        public int Port = 5555;
        public string Target = "192.168.31.238:5555";
        public string Codec = "raw"; // "raw", "opus320", "opus128"
        public bool MutePhone = true;
        public bool AutoConnect = true;

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
                        }
                    }
                }
            }
            catch { }
            return s;
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

        public async void ConnectAsync()
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

            string target = _settings.Target;
            if (string.IsNullOrEmpty(target) || target.Contains("."))
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

                    // 2. ensure media volume is set
                    var psiVol = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = string.Format("-s {0} shell cmd media_session volume --show --stream 3 --set 15", target),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var p = Process.Start(psiVol))
                    {
                        p.WaitForExit(2500);
                    }

                    // 3. build scrcpy arguments
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
                UpdateTrayIcon(true);
                string desc = _settings.Codec == "raw" ? "Raw PCM 无损" : "Opus 320K";
                _notifyIcon.Text = string.Format("WiFi 音频连接器 - {0} (已连接)", _settings.DeviceName);
                _notifyIcon.ShowBalloonTip(2500, "设备已连接", string.Format("{0} ({1})\n音频流已就绪，直通电脑播放", _settings.DeviceName, desc), ToolTipIcon.Info);
                _flyout.UpdateState(ConnectionState.Connected);

                // Watchdog task
                Task.Run(() =>
                {
                    try
                    {
                        _scrcpyProc.WaitForExit();
                    }
                    catch { }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateTrayIcon(false);
                        _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
                        _flyout.UpdateState(ConnectionState.Disconnected);
                    }));
                });
            }
            else
            {
                UpdateTrayIcon(false);
                _notifyIcon.Text = "WiFi 音频连接器 (未连接)";
                _flyout.UpdateState(ConnectionState.Disconnected);
                _notifyIcon.ShowBalloonTip(2500, "连接失败", "无法连接到手机，请确认手机已开机并处于同一局域网 Wi-Fi", ToolTipIcon.Error);
            }
        }

        public void Disconnect()
        {
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

        public void ExitApp()
        {
            Disconnect();
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
        private TextBox _tbIp;
        private TextBox _tbPort;
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
            Height = 525;
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
                IsChecked = _app.CurrentSettings.AutoConnect
            };
            _cbAutoConnect.Checked += (s, e) => { _app.CurrentSettings.AutoConnect = true; _app.CurrentSettings.Save(); };
            _cbAutoConnect.Unchecked += (s, e) => { _app.CurrentSettings.AutoConnect = false; _app.CurrentSettings.Save(); };
            optsPanel.Children.Add(_cbAutoConnect);

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
    }
}
