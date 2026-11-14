# MouseGuard

Windows 平台物理鼠标守护工具。在系统底层拦截非硬件生成的鼠标位移指令，防止第三方程序、自动化脚本或远程控制接管光标。

A Windows utility to block synthetic mouse movements and prevent unauthorized cursor control.

[下载最新版本 (Download Latest Release)](https://github.com/Kirosca/MouseGuard/releases/latest)

---

## 简体中文

### 功能特性

- **硬件输入过滤**：在独立底层钩子线程（`WH_MOUSE_LL`）中检查输入来源，丢弃携带注入标记（`LLMHF_INJECTED`）的鼠标位移与点击指令（如 `SendInput`、`mouse_event`）。
- **坐标瞬移防篡改**：监控非物理硬件驱动的光标突变（如直接调用 `SetCursorPos`），并在手操静止时将其拉回复位。
- **光标锁定解除**：检测并自动解除第三方程序通过 `ClipCursor` 对光标活动范围的限制。
- **高分屏适配**：启用 Per-Monitor DPI V2 模式，确保物理像素坐标与 API 绝对对齐。
- **系统托盘驻留**：窗口关闭后最小化至托盘静默运行，支持开机自启。

### 运行环境

- Windows 10 / Windows 11
- .NET Framework 4.8（系统内置，无额外依赖）

### 构建方法

使用系统自带的 C# 编译器直接构建，无需安装 Visual Studio：

```cmd
build.bat
```

或手动执行命令：

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll,System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll /out:MouseGuard.exe MouseGuard.cs
```

### 验证工具

仓库包含测试工具 `SimulateMouseMover.cs`，用于验证防护行为：
- `1`：测试拦截 `mouse_event` 模拟位移；
- `2`：测试复位 `SetCursorPos` 坐标瞬移；
- `3`：测试解除 `ClipCursor` 区域锁定。

---

## English

### Features

- **Hardware Input Filtering**: Inspects mouse events via an isolated low-level hook (`WH_MOUSE_LL`) and drops synthetic movements or clicks flagged with `LLMHF_INJECTED` (e.g., `SendInput`, `mouse_event`).
- **Anti-Teleport Watchdog**: Detects unnatural cursor displacements caused by direct `SetCursorPos` calls and resets the cursor when physical input is idle.
- **Cursor Confinement Release**: Automatically dissolves restrictive bounding boxes enforced by `ClipCursor`.
- **High-DPI Alignment**: Enables Per-Monitor DPI V2 to align Win32 coordinates with raw physical pixels.
- **Tray Resident**: Minimizes to the system tray on close; supports running at Windows startup.

### Requirements

- Windows 10 / Windows 11
- .NET Framework 4.8 (preinstalled with Windows)

### Build

Compile using the built-in .NET compiler without Visual Studio:

```cmd
build.bat
```

Or run manually:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Xaml.dll,System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll /out:MouseGuard.exe MouseGuard.cs
```

### Verification

`SimulateMouseMover.cs` is provided to test interception behaviors:
- `1`: Test blocking `mouse_event` displacement;
- `2`: Test resetting `SetCursorPos` teleportation;
- `3`: Test releasing `ClipCursor` bounds.

---

## License

[MIT License](LICENSE)
