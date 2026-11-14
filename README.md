# MouseGuard (鼠标纯物理控制守护器)

<p align="center">
  <b>专注于杜绝一切外部软件与脚本非法移动鼠标，确保只有您的物理手操才能控制光标。</b><br>
  <i>A pure physical mouse guard for Windows to prevent third-party software, macros, and automation scripts from tampering with the cursor position.</i>
</p>

---

## 简体中文

### 概述

在日常使用 Windows 系统时，部分流氓软件、远程协助工具、自动化测试脚本或宏工具会未经用户许可擅自移动鼠标指针，打乱正常工作流。

**MouseGuard** 是一款专为 Windows 设计的轻量级纯物理鼠标防护工具。通过**底层独立线程硬件输入标记过滤**与**瞬移防篡改看门狗**双重引擎，在系统底层丢弃一切由软件模拟生成的位移指令，并瞬间复位任何绕过消息队列的非物理瞬移行为，保障用户对光标的绝对独占控制。

### 核心防御机制

1. **底层独立线程输入过滤（WH_MOUSE_LL）**
   - 运行在专属的高优先级独立线程中，享有纯净的 Win32 消息循环，绝不被 UI 渲染或窗口切换阻塞。
   - 实时解析全局鼠标消息结构体 `MSLLHOOKSTRUCT`。针对携带 `LLMHF_INJECTED`（0x01）或 `LLMHF_LOWER_IL_INJECTED`（0x02）标志的模拟移动指令（如 `SendInput`、`mouse_event`），在底层直接阻断丢弃，光标纹丝不动。
   - 纯物理硬件输入（标记为 0）以纳秒级极速无锁放行，对 1000Hz 高回报率电竞鼠标零延迟。
2. **坐标瞬移防篡改看门狗（Anti-SetCursorPos Watchdog）**
   - 针对直接调用 User32 API `SetCursorPos` 强行篡改屏幕坐标的软件。
   - 守护线程高频监控光标坐标。当检测到光标在手操静止状态下发生凭空异常跳变，且无任何物理硬件信号支持时，将在毫秒级瞬间将其拉回至原物理落点。
3. **反光标囚禁与限制解除（Anti-ClipCursor）**
   - 实时监测系统光标剪裁区域。若发现第三方软件调用 `ClipCursor` 将光标活动范围非法囚禁在特定窗口或微小区域，自动触发 `ClipCursor(NULL)` 释放限制，保障全屏幕自由移动。
4. **高分屏 Per-Monitor DPI V2 绝对对齐**
   - 进程级声明 `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`，确保所有 Win32 光标 API 与底层硬件钩子在 100%~200% 缩放比下坐标系 1:1 绝对对齐，彻底消除高分屏换算差。

### 功能亮点

- **现代 Windows 11 Fluent 界面**：采用 WPF 矢量自适应渲染架构，四宫格仪表盘直观展示拦截指标，界面清晰锐利，无视系统缩放比例。
- **系统托盘后台静默守护**：点击窗口关闭按钮自动最小化至右下角系统托盘，不占用任务栏。托盘右键菜单支持一键切换与退出。
- **开机自启**：主界面一键勾选即可写入当前用户自启项，开机以静默模式在后台静默运行。
- **单文件零依赖**：编译产物仅约 60 KB，单文件绿色独立运行，无需安装任何额外第三方运行库或内核驱动。

### 编译与构建

项目采用标准 C# 编写，完全依赖 Windows 自带的 .NET Framework 4.8 基础库，无需安装 Visual Studio 即可直接一键编译。

双击运行仓库根目录下的 `build.bat`，或在命令行中执行：

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll,System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll /out:MouseGuard.exe MouseGuard.cs
```

### 测试与验证

仓库内置了测试验证工具 `SimulateMouseMover.cs`：
1. 编译后运行 `SimulateMouseMover.exe`；
2. 输入 `1` 测试 `mouse_event` 模拟移动：MouseGuard 将在系统底层直接丢弃指令，控制台提示拦截成功，光标完全不动；
3. 输入 `2` 测试 `SetCursorPos` 瞬移篡改：光标将在数毫秒内被瞬间拉回原位；
4. 输入 `3` 测试 `ClipCursor` 区域囚禁：囚禁将被自动解除并释放。

---

## English

### Overview

On Windows systems, certain third-party software, remote assistance tools, macros, and automation scripts may move or snap the mouse cursor without user consent, interrupting normal workflows.

**MouseGuard** is a lightweight, zero-dependency pure physical mouse defense utility for Windows. Powered by dual defense engines—**isolated input-thread hardware flag filtering** and an **anti-teleport watchdog**—it intercepts and drops all synthetic cursor displacement at the lowest OS level and snaps back any unprivileged coordinate tampering, ensuring that **only your physical hand movement can control the cursor**.

### Core Defense Mechanisms

1. **Isolated Low-Level Input Filtering (`WH_MOUSE_LL`)**
   - Operates on a dedicated high-priority background thread with a pure Win32 message pump, completely immune to UI rendering or message loop delays.
   - Inspects the `MSLLHOOKSTRUCT` flags in real-time. Any injected move messages (such as `SendInput` or `mouse_event` flagged with `LLMHF_INJECTED` or `LLMHF_LOWER_IL_INJECTED`) are dropped immediately at the OS level (`return (IntPtr)1`), keeping the cursor completely stationary.
   - Genuine physical hardware reports (`flags == 0`) pass through with sub-microsecond latency, preserving full responsiveness for 1000Hz gaming mice.
2. **Anti-Teleport Watchdog (`Anti-SetCursorPos`)**
   - Targets applications that bypass the message queue by directly invoking `user32.dll!SetCursorPos`.
   - The watchdog monitors cursor coordinates and verifies physical activity timestamps. When the cursor suddenly teleports over a significant distance while physical input is stationary, it instantly resets the cursor back to the legitimate physical position.
3. **Anti-Cursor Confinement (`Anti-ClipCursor`)**
   - Periodically monitors cursor clipping bounds via `GetClipCursor`. If an application restricts cursor freedom to a specific window or rectangle, `ClipCursor(IntPtr.Zero)` is automatically invoked to restore full-screen movement.
4. **Per-Monitor DPI V2 Precision**
   - Declares `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` process-wide, ensuring that all Win32 cursor APIs and low-level hook coordinates remain 1:1 aligned to physical display pixels regardless of Windows display scaling (100%–200%).

### Key Features

- **Modern Windows 11 Fluent UI**: Built with scalable WPF vector rendering. Includes a 4-card metric dashboard and real-time event logs with clear visual status indicators.
- **System Tray Integration**: Closing the window minimizes the app to the system tray for silent background protection.
- **Run at Windows Startup**: Toggle startup registration with one click.
- **Zero External Dependencies**: Compiles into a single standalone executable (~60 KB) with zero third-party packages or kernel drivers required.

### Build Instructions

MouseGuard relies solely on standard Windows .NET Framework 4.8 components. You can build it without installing Visual Studio.

Simply run `build.bat` or execute the following command:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll,System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll /out:MouseGuard.exe MouseGuard.cs
```

### Verification & Testing

A companion diagnostic utility `SimulateMouseMover.cs` is included:
1. Run `SimulateMouseMover.exe`;
2. Press `1` to test simulated movement (`mouse_event`): MouseGuard drops the event at the OS level;
3. Press `2` to test forced teleportation (`SetCursorPos`): The watchdog snaps the cursor back immediately;
4. Press `3` to test cursor confinement (`ClipCursor`): Confinement is automatically dissolved.

---

## License

This project is open-sourced under the [MIT License](LICENSE).
