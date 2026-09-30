using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace WiFiAudioConnector
{
    public class Settings
    {
        public string DeviceIp = "192.168.31.238";
        public int Port = 5555;
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
                sb.AppendLine("DeviceIp=" + DeviceIp);
                sb.AppendLine("Port=" + Port);
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
                            if (k == "DeviceIp") s.DeviceIp = v;
                            else if (k == "Port") int.TryParse(v, out s.Port);
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
                File.AppendAllText("D:\\AudioPlaybackConnector\\WiFiAudioConnector\\run.log", DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n");
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
                _mutex = new Mutex(true, "WiFiAudioConnector_App_Mutex", out createdNew);
                LogLine("Mutex createdNew: " + createdNew);
                if (!createdNew)
                {
                    LogLine("Exiting because not createdNew");
                    return;
                }

                LogLine("Creating App instance");
                var app = new App();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                LogLine("Calling app.Run()");
                app.Run();
                LogLine("app.Run() finished");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString());
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            LogLine("OnStartup entered");
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
                LogLine("Loading settings");
                _settings = Settings.Load();
                LogLine("InitTrayIcon");
                InitTrayIcon();
                LogLine("Init FlyoutWindow");
                _flyout = new FlyoutWindow(this);
                MainWindow = _flyout;
                LogLine("FlyoutWindow initialized");

                if (_settings.AutoConnect)
                {
                    LogLine("AutoConnect starting");
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
            menu.Items.Add("连接手机", null, (s, e) => ConnectAsync());
            menu.Items.Add("断开连接", null, (s, e) => Disconnect());
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

        public async void ConnectAsync()
        {
            LogLine("ConnectAsync entered");
            if (IsConnected || _isConnecting)
            {
                LogLine("Already connected or connecting");
                return;
            }
            _isConnecting = true;
            LogLine("Updating state to Connecting");
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

            string target = string.Format("{0}:{1}", _settings.DeviceIp, _settings.Port);

            LogLine("ConnectAsync: Starting Task.Run");
            bool ok = await Task.Run<bool>(() =>
            {
                try
                {
                    LogLine("Task.Run: Calling adb connect " + target);
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
                    LogLine("Task.Run: adb connect exited");

                    // 2. ensure media volume is set
                    LogLine("Task.Run: Setting media volume");
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
                        p.WaitForExit(3000);
                    }
                    LogLine("Task.Run: Volume set exited");

                    // 3. build scrcpy arguments
                    string codecArg = "--audio-codec=raw";
                    if (_settings.Codec == "opus320") codecArg = "--audio-codec=opus --audio-bit-rate=320K";
                    else if (_settings.Codec == "opus128") codecArg = "--audio-codec=opus --audio-bit-rate=128K";

                    string modeArg = _settings.MutePhone ? "--audio-source=playback" : "--audio-source=playback --audio-dup";
                    string scrcpyArgs = string.Format("-s {0} --no-video {1} {2} --audio-buffer=50", target, codecArg, modeArg);

                    LogLine("Task.Run: Starting scrcpy with args: " + scrcpyArgs);
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

                    bool running = _scrcpyProc != null && !_scrcpyProc.HasExited;
                    LogLine("Task.Run: scrcpy running=" + running);
                    return running;
                }
                catch (Exception ex)
                {
                    LogLine("Task.Run Exception: " + ex);
                    return false;
                }
            });

            LogLine("ConnectAsync: Task.Run completed with ok=" + ok);

            _isConnecting = false;

            if (ok)
            {
                LogLine("Updating TrayIcon connected");
                UpdateTrayIcon(true);
                LogLine("Updated TrayIcon connected");

                string desc = _settings.Codec == "raw" ? "Raw PCM 无损" : "Opus 320K";
                _notifyIcon.Text = "WiFi 音频连接器 (已连接)";
                LogLine("Updated NotifyIcon text");

                try
                {
                    _notifyIcon.ShowBalloonTip(2000, "设备已连接", "小米 15 Pro 音频流已就绪", ToolTipIcon.Info);
                    LogLine("ShowBalloonTip called");
                }
                catch (Exception ex)
                {
                    LogLine("ShowBalloonTip exception: " + ex);
                }

                _flyout.UpdateState(ConnectionState.Connected);
                LogLine("UpdateState Connected called");

                // Watchdog task
                Task.Run(() =>
                {
                    try
                    {
                        LogLine("Watchdog: waiting for scrcpy exit");
                        _scrcpyProc.WaitForExit();
                        LogLine("Watchdog: scrcpy exited with code: " + _scrcpyProc.ExitCode);
                    }
                    catch (Exception ex)
                    {
                        LogLine("Watchdog exception: " + ex);
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogLine("Watchdog: Dispatcher updating disconnected");
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
                _notifyIcon.ShowBalloonTip(3000, "连接失败", "无法连接到手机，请确认手机已开机并处于同一局域网 Wi-Fi", ToolTipIcon.Error);
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

        private string FindToolPath(string exeName)
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
            Shutdown();
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
        private RadioButton _rbRaw;
        private RadioButton _rbOpus320;
        private RadioButton _rbOpus128;
        private CheckBox _cbMutePhone;
        private CheckBox _cbAutoConnect;
        private TextBox _tbIp;
        private TextBox _tbPort;

        public FlyoutWindow(App app)
        {
            _app = app;
            BuildUI();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void BuildUI()
        {
            Width = 360;
            Height = 490;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            Deactivated += (s, e) => Hide();

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(246, 32, 34, 40)),
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
            var titleText = new TextBlock
            {
                Text = "手机音频连接器",
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
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
                Background = new SolidColorBrush(Color.FromArgb(160, 45, 48, 56)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var devicePanel = new StackPanel();

            var row1 = new DockPanel();
            var devName = new TextBlock
            {
                Text = "小米 15 Pro",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White
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
            row1.Children.Add(devName);
            devicePanel.Children.Add(row1);

            // Target IP row
            var ipRow = new DockPanel { Margin = new Thickness(0, 8, 0, 10) };
            var ipLabel = new TextBlock
            {
                Text = "目标地址:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 200, 205, 215)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _tbPort = new TextBox
            {
                Text = _app.CurrentSettings.Port.ToString(),
                Width = 52,
                Height = 22,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(200, 25, 27, 32)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            _tbPort.TextChanged += (s, e) => { int p; if (int.TryParse(_tbPort.Text, out p)) { _app.CurrentSettings.Port = p; _app.CurrentSettings.Save(); } };

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
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(200, 25, 27, 32)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            _tbIp.TextChanged += (s, e) => { _app.CurrentSettings.DeviceIp = _tbIp.Text.Trim(); _app.CurrentSettings.Save(); };
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
                Background = new SolidColorBrush(Color.FromArgb(160, 45, 48, 56)),
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
                Content = "Raw PCM (16-bit 48kHz 原生无损直通)",
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
                Background = new SolidColorBrush(Color.FromArgb(160, 45, 48, 56)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var optsPanel = new StackPanel();

            _cbMutePhone = new CheckBox
            {
                Content = "手机扬声器静音 (仅电脑端音箱播放)",
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
                Content = "开机自启并自动连接",
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
                Text = "输出通道: 电脑默认声卡 (山灵 UA2 DAC)",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 160, 170, 185)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            root.Children.Add(footerText);

            mainBorder.Child = root;
            Content = mainBorder;

            UpdateState(ConnectionState.Disconnected);
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
