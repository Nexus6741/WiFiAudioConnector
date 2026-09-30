# WiFiAudioConnector (无线音频连接器)

> **极简、高保真、低延迟的 Android 无线/USB 有线音频直通与电脑多维联动控制中心**

---

## 📖 项目简介

`WiFiAudioConnector` 是一款专为 Windows 设计的高性能音频直通与设备管理客户端。基于 `scrcpy` 与 `adb` 底层流媒体管道，配合 Windows WASAPI Core Audio 会话控制技术，将 Android 设备的系统声音以极低延迟、无损音质无缝串流至电脑扬声器播放。

深度整合了**手机物理按键双向音量联动**、**多设备独立全局快捷键**、**智能防外放断开静音**、**托盘滚轮调音**等高级交互体验，提供如同原生硬件级别的顺畅操控。

---

## ✨ 核心特性

- ⚡ **极致低延迟与无损音质**：
  - 支持 **Raw PCM (16-bit 48kHz)** 原生无损直通（推荐体验，零压缩零失真）。
  - 支持 **Opus 320K** 广播级高码率编码与 **Opus 128K** 极限省电低带宽模式。
  - **三档音频缓冲区延迟调优**：
    - ⚡ **电竞极速档** (Wi-Fi 30ms / USB 10ms)：音画近乎完全同步，手游与竞技大片最佳首选。
    - ⚖ **均衡推荐档** (50ms - 默认)：兼顾音画低延迟与网络抗波动。
    - 🛡 **穿墙防卡顿档** (80ms)：针对 2.4G Wi-Fi、复杂路由或长距离弱网环境。
  - 🔄 **已连接状态无缝热重载 (Hot-Reload)**：处于连接状态下直接点击切换音质编码、延迟档位或扬声器静音，程序自动在后台平滑秒级重启推流管道应用新参数，桌面 OSD 悬浮胶囊实时反馈生效状态，无需手动断开重连！
- 📶 **三维立体连接模式 (Wi-Fi / USB / 蓝牙 A2DP)**：
  - 局域网 Wi-Fi 与 USB 自动探测扫描，支持在无线与有线模式间一键切换。
  - **原生集成 Windows 蓝牙 A2DP 音频直通**：参考并融合经典 `AudioPlaybackConnection` 架构，免开 USB 调试或 Wi-Fi 即可直接将已配对手机作为蓝牙音频源推流至电脑！
- 📱 **通用设备支持与即插即用**：
  - 完美适配小米/HyperOS/MIUI、OPPO/ColorOS、vivo/OriginOS、华为/荣耀及各类原生 Android 设备与模拟器。
- ⌨️ **多设备专属全局快捷键**：
  - 独立专属热键引擎：支持为每台设备甚至同一设备的「Wi-Fi」、「USB」及「蓝牙」不同连接方式分别绑定独立的系统级全局快捷键（如 `Ctrl+1`、`Ctrl+2`、`Ctrl+Shift+B`）。
  - 键盘一键极速连接与断开，彻底解放双手。
- 🔊 **物理按键双向音量联动 (WASAPI)**：
  - 集成 Windows Core Audio COM 接口，针对音频播放会话实施 0 延迟硬件级精准音量调度。
  - 实时捕获手机侧边物理音量按键事件，手机按键与电脑主面板音量双向 100% 同步。
- 🔇 **智能防外放断开静音 (防声音炸裂)**：
  - 仅在开启按键联动时生效（带独立开关选项）。
  - 断开瞬间先由底层原生 `AudioManager` 与 `media_session` 执行静音与媒体暂停，再平滑切断传输流，杜绝断开后手机喇叭爆音外放。
- 🎚️ **现代化电光蓝极简高精度滑块**：
  - 全新流线型暗黑质感设计（发光电光蓝 `#3B82F6` 进度轨 + 纯白悬浮圆钮）。
  - 像素级精准点击瞬移（Click-to-Seek），支持任意位置点按即达。
  - 串行异步防抖队列（`QueuePhoneVolumeSync`），彻底杜绝调音抽搐抖动。
  - 重连自动恢复上一次记忆音量，无缝衔接。
- 🖱️ **任务栏托盘图标滚轮调音 + 桌面电光蓝 OSD 悬浮胶囊**：
  - 鼠标悬停在任务栏托盘图标滚动滚轮即可毫秒级无感调节音量。
  - **桌面 OSD 悬浮胶囊**：调音时在屏幕右下角自动弹出极简半透明发光胶囊（如 `🔊 65%`），附带电光蓝发光霓虹边框与实时进度条，停止调节 1 秒后优雅淡出。
  - 全屏看电影或玩游戏时盲操滚轮无需切屏也能对当前音量一目了然。
- 🔕 **静音与通知免打扰**：
  - 支持一键开启/关闭系统弹窗气泡通知，保持桌面清爽整洁。
  - 开机自启与后台静默最小化。

---

## 🛠️ 构建与编译 (Zero-Dependency)

本项目采用纯原生 C# 编写，完全依赖 Windows 系统内置的 .NET Framework 4.5+ 运行库与编译工具，**无需安装 Visual Studio 等大型开发环境**。

### 一键编译

双击运行根目录下的 `build.bat` 脚本即可快速构建：

```cmd
build.bat
```

或在终端中直接调用系统编译器：

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll /out:WiFiAudioConnector.exe WiFiAudioConnector.cs
```

---

## 🚀 快速上手

1. **准备工作**：
   - 手机开启「开发者选项」并允许「USB 调试」以及「无线调试 / 允许通过 Wi-Fi 调试」。
   - 确保手机与电脑处于同一局域网（同一 Wi-Fi 或路由器下）。
2. **运行程序**：
   - 双击启动 `WiFiAudioConnector.exe`，程序自动驻留系统托盘。
   - 左键点击托盘图标弹出暗黑半透明控制面板。
3. **连接设备**：
   - 点击面板上的「扫描」按钮，软件将自动探测在线的局域网与 USB 设备。
   - 选择您的设备后点击「连接」，音频将立即直通至电脑扬声器！

---

## 📄 架构说明

- **UI 呈现**：WPF 硬件加速半透明 Fluent 设计与 WinForms 托盘交互混编。
- **音频捕获与转发**：`scrcpy` 无头模式（Headless Audio Forwarding）。
- **音量核心**：Windows WASAPI `ISimpleAudioVolume`、`IAudioSessionControl2`。
- **底层通信**：Android `adb`、`AudioManager` 原生服务通信通道与 Logcat 异步事件监听流。
- **全局拦截**：Win32 `RegisterHotKey` 与 `WH_MOUSE_LL` 底层鼠标滚轮钩子。

---

## 📜 开源协议

本项目遵循 [MIT License](LICENSE.txt) 开源协议。
