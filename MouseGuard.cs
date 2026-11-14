using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace MouseGuard
{
    public class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                if (!SetProcessDpiAwarenessContext((IntPtr)(-4))) // Per-Monitor V2
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\MouseGuard_WPF_Mutex_2026", out createdNew))
            {
                if (!createdNew)
                {
                    Forms.MessageBox.Show("MouseGuard (鼠标防篡改守护器) 已在后台运行中。\n您可以在任务栏右下角系统托盘中找到它。",
                        "提示", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
                    return;
                }

                System.Windows.Application app = new System.Windows.Application();
                MainWindow win = new MainWindow(args);
                app.Run(win);
            }
        }
    }

    public class MainWindow : Window
    {
        // Win32 常量与结构
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;

        private const int LLMHF_INJECTED = 0x0001;
        private const int LLMHF_LOWER_IL_INJECTED = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClipCursor(out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // 防护状态
        private volatile bool _isProtectionActive = true;
        private volatile bool _blockInjectedMove = true;
        private volatile bool _blockSetCursorPos = true;
        private volatile bool _blockInjectedClick = false;
        private volatile bool _autoReleaseClip = true;

        // 计数统计
        private long _countInjectedMoveBlocked = 0;
        private long _countSetCursorPosBlocked = 0;
        private long _countInjectedClickBlocked = 0;
        private long _countClipReleased = 0;

        // 物理鼠标落点与时间戳跟踪（原子级更新）
        private static volatile int _physX = 0;
        private static volatile int _physY = 0;
        private static long _lastPhysicalTick = 0;

        // 独立输入处理线程与钩子
        private Thread _hookThread;
        private uint _hookThreadId = 0;
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelMouseProc _hookProc;

        // 看门狗线程
        private Thread _watchdogThread;
        private volatile bool _watchdogRunning = true;

        // 托盘与 UI 组件
        private Forms.NotifyIcon _notifyIcon;
        private Forms.ContextMenuStrip _trayMenu;

        // UI 元素引用
        private Border _heroBadge;
        private TextBlock _heroBadgeText;
        private TextBlock _heroTitle;
        private TextBlock _heroSubtitle;
        private Button _btnToggle;

        private TextBlock _txtStatMove;
        private TextBlock _txtStatSetPos;
        private TextBlock _txtStatClip;
        private TextBlock _txtStatClick;

        private CheckBox _chkMove;
        private CheckBox _chkSetPos;
        private CheckBox _chkClip;
        private CheckBox _chkClick;
        private CheckBox _chkAutoStart;

        private ListBox _lstLogs;
        private DispatcherTimer _uiTimer;

        public MainWindow(string[] args)
        {
            InitializeWpfWindow();
            InitLastPhysicalPosition();
            StartHookThread();
            StartWatchdog();
            InitTrayIcon();

            bool silent = false;
            foreach (var a in args)
            {
                if (a.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                    break;
                }
            }

            if (silent)
            {
                this.WindowState = WindowState.Minimized;
                this.Visibility = Visibility.Hidden;
            }
        }

        private void InitializeWpfWindow()
        {
            this.Title = "MouseGuard - 纯物理鼠标守护中心";
            this.Width = 720;
            this.Height = 690;
            this.MinWidth = 660;
            this.MinHeight = 620;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // #F1F5F9
            this.FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");

            Grid rootGrid = new Grid();
            rootGrid.Margin = new Thickness(20);
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Hero 状态卡片
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: 4 块统计卡片
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: 策略开关卡片
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: 日志面板

            // ================= 0. Hero 状态卡片 =================
            Border heroCard = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20, 18, 20, 18)
            };

            Grid heroGrid = new Grid();
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel heroLeft = new StackPanel { Orientation = Orientation.Vertical };

            _heroBadge = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(30, 16, 185, 129))
            };
            _heroBadgeText = new TextBlock
            {
                Text = "● 物理保护生效中",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105))
            };
            _heroBadge.Child = _heroBadgeText;
            heroLeft.ControlsAdd(_heroBadge);

            _heroTitle = new TextBlock
            {
                Text = "纯物理控制模式已就绪",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            heroLeft.ControlsAdd(_heroTitle);

            _heroSubtitle = new TextBlock
            {
                Text = "底层丢弃所有注入输入 | 强行复位瞬移坐标 | 物理手操独占鼠标",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
            };
            heroLeft.ControlsAdd(_heroSubtitle);

            heroGrid.Children.Add(heroLeft);
            Grid.SetColumn(heroLeft, 0);

            _btnToggle = new Button
            {
                Content = "暂停防护",
                Width = 110,
                Height = 42,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(225, 29, 72)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyRoundedButtonStyle(_btnToggle, 8);
            _btnToggle.Click += (s, e) => ToggleProtection();

            heroGrid.Children.Add(_btnToggle);
            Grid.SetColumn(_btnToggle, 1);

            heroCard.Child = heroGrid;
            rootGrid.Children.Add(heroCard);
            Grid.SetRow(heroCard, 0);

            // ================= 1. 4 块现代统计卡片 =================
            Grid statsGrid = new Grid { Margin = new Thickness(0, 14, 0, 14) };
            for (int i = 0; i < 4; i++)
            {
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            statsGrid.Children.Add(CreateStatCard("注入移动拦截", "SendInput / mouse_event", "已阻断", out _txtStatMove, 0));
            statsGrid.Children.Add(CreateStatCard("坐标瞬移复位", "SetCursorPos 篡改", "已拉回", out _txtStatSetPos, 1));
            statsGrid.Children.Add(CreateStatCard("锁定囚禁解除", "ClipCursor 范围限制", "已释放", out _txtStatClip, 2));
            statsGrid.Children.Add(CreateStatCard("注入点击拦截", "模拟鼠标按键点击", "按需", out _txtStatClick, 3));

            rootGrid.Children.Add(statsGrid);
            Grid.SetRow(statsGrid, 1);

            // ================= 2. 防护策略开关卡片 =================
            Border optionsCard = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 14)
            };

            StackPanel optStack = new StackPanel { Orientation = Orientation.Vertical };

            TextBlock optHeader = new TextBlock
            {
                Text = "防护策略与偏好设置",
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            optStack.ControlsAdd(optHeader);

            Grid chkGrid = new Grid();
            chkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            chkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chkGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chkGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chkGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _chkMove = CreateStyledCheckBox("拦截注入移动 (SendInput / mouse_event 核心防御)", true);
            _chkMove.Checked += (s, e) => _blockInjectedMove = true;
            _chkMove.Unchecked += (s, e) => _blockInjectedMove = false;
            chkGrid.AddControl(_chkMove, 0, 0);

            _chkSetPos = CreateStyledCheckBox("拦截瞬移篡改 (SetCursorPos 异常大位移拉回)", true);
            _chkSetPos.Checked += (s, e) => _blockSetCursorPos = true;
            _chkSetPos.Unchecked += (s, e) => _blockSetCursorPos = false;
            chkGrid.AddControl(_chkSetPos, 1, 0);

            _chkClip = CreateStyledCheckBox("自动解除光标锁定 (清除 ClipCursor 区域囚禁)", true);
            _chkClip.Checked += (s, e) => _autoReleaseClip = true;
            _chkClip.Unchecked += (s, e) => _autoReleaseClip = false;
            chkGrid.AddControl(_chkClip, 2, 0);

            _chkClick = CreateStyledCheckBox("连同软件模拟点击一并拦截", false);
            _chkClick.Checked += (s, e) => _blockInjectedClick = true;
            _chkClick.Unchecked += (s, e) => _blockInjectedClick = false;
            chkGrid.AddControl(_chkClick, 0, 1);

            _chkAutoStart = CreateStyledCheckBox("开机自动启动并在后台静默保护", CheckAutoStartStatus());
            _chkAutoStart.Checked += (s, e) => SetAutoStart(true);
            _chkAutoStart.Unchecked += (s, e) => SetAutoStart(false);
            chkGrid.AddControl(_chkAutoStart, 1, 1);

            optStack.ControlsAdd(chkGrid);
            optionsCard.Child = optStack;

            rootGrid.Children.Add(optionsCard);
            Grid.SetRow(optionsCard, 2);

            // ================= 3. 实时日志卡片 =================
            Border logCard = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 14, 18, 14)
            };

            Grid logGrid = new Grid();
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid logHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock logTitle = new TextBlock
            {
                Text = "实时拦截与防护事件日志",
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                VerticalAlignment = VerticalAlignment.Center
            };
            logHeader.Children.Add(logTitle);
            Grid.SetColumn(logTitle, 0);

            Button btnClear = new Button
            {
                Content = "清空日志与计数",
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonStyle(btnClear, 6);
            btnClear.Click += (s, e) =>
            {
                _lstLogs.Items.Clear();
                Interlocked.Exchange(ref _countInjectedMoveBlocked, 0);
                Interlocked.Exchange(ref _countSetCursorPosBlocked, 0);
                Interlocked.Exchange(ref _countClipReleased, 0);
                Interlocked.Exchange(ref _countInjectedClickBlocked, 0);
                UpdateUIStats();
            };
            logHeader.Children.Add(btnClear);
            Grid.SetColumn(btnClear, 1);

            logGrid.Children.Add(logHeader);
            Grid.SetRow(logHeader, 0);

            _lstLogs = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas, 'Cascadia Mono', monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Padding = new Thickness(6)
            };
            logGrid.Children.Add(_lstLogs);
            Grid.SetRow(_lstLogs, 1);

            logCard.Child = logGrid;
            rootGrid.Children.Add(logCard);
            Grid.SetRow(logCard, 3);

            this.Content = rootGrid;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _uiTimer.Tick += (s, e) => UpdateUIStats();
            _uiTimer.Start();
        }

        private UIElement CreateStatCard(string title, string subtitle, string badgeText, out TextBlock outNumText, int col)
        {
            Border card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(col == 0 ? 0 : 6, 0, col == 3 ? 0 : 6, 0)
            };

            StackPanel sp = new StackPanel { Orientation = Orientation.Vertical };

            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lblTitle = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
            };
            topRow.Children.Add(lblTitle);
            Grid.SetColumn(lblTitle, 0);

            Border badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Padding = new Thickness(6, 1, 6, 1)
            };
            badge.Child = new TextBlock
            {
                Text = badgeText,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
            };
            topRow.Children.Add(badge);
            Grid.SetColumn(badge, 1);

            sp.ControlsAdd(topRow);

            outNumText = new TextBlock
            {
                Text = "0",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 6, 0, 2)
            };
            sp.ControlsAdd(outNumText);

            TextBlock lblSub = new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
            sp.ControlsAdd(lblSub);

            card.Child = sp;
            Grid.SetColumn(card, col);
            return card;
        }

        private CheckBox CreateStyledCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 4, 0, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
        }

        private void ApplyRoundedButtonStyle(Button btn, int cornerRadius)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));

            border.AppendChild(content);
            template.VisualTree = border;
            btn.Template = template;
        }

        private void InitLastPhysicalPosition()
        {
            POINT pt;
            if (GetCursorPos(out pt))
            {
                _physX = pt.x;
                _physY = pt.y;
            }
            Interlocked.Exchange(ref _lastPhysicalTick, Stopwatch.GetTimestamp());
        }

        // ================= 独立高优先级输入线程安装 WH_MOUSE_LL 钩子 =================
        private void StartHookThread()
        {
            _hookThread = new Thread(() =>
            {
                _hookThreadId = GetCurrentThreadId();
                _hookProc = HookCallback;

                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);

                if (_hookId == IntPtr.Zero)
                {
                    AddLog("【错误】底层鼠标钩子安装失败，错误码: " + Marshal.GetLastWin32Error());
                }
                else
                {
                    AddLog("底层独立输入钩子已激活，100% 硬件级拦截所有模拟输入。");
                }

                // 独立线程专属纯净 Win32 消息循环，绝不被 UI 渲染阻塞，永不超时卸载
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    // 仅运转消息队列
                }

                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            _hookThread.Start();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isProtectionActive)
            {
                int msg = wParam.ToInt32();
                MSLLHOOKSTRUCT hook = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool isInjected = (hook.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;

                if (isInjected)
                {
                    if (msg == WM_MOUSEMOVE && _blockInjectedMove)
                    {
                        Interlocked.Increment(ref _countInjectedMoveBlocked);
                        AddLog(string.Format("【阻断模拟移动】坐标: ({0}, {1}) 标志位: 0x{2:X}", hook.pt.x, hook.pt.y, hook.flags));
                        return (IntPtr)1; // 丢弃模拟移动，光标纹丝未动
                    }
                    else if ((msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN ||
                             msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP) && _blockInjectedClick)
                    {
                        Interlocked.Increment(ref _countInjectedClickBlocked);
                        AddLog(string.Format("【阻断模拟点击】消息代码: 0x{0:X4} 坐标: ({1}, {2})", msg, hook.pt.x, hook.pt.y));
                        return (IntPtr)1;
                    }
                }
                else
                {
                    // 真实手操硬件事件：极速无锁记录坐标与时间戳（纳秒级耗时）
                    _physX = hook.pt.x;
                    _physY = hook.pt.y;
                    Interlocked.Exchange(ref _lastPhysicalTick, Stopwatch.GetTimestamp());
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // ================= 看门狗监控线程（专注于真实恶意瞬移检测） =================
        private void StartWatchdog()
        {
            _watchdogThread = new Thread(WatchdogLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Normal
            };
            _watchdogThread.Start();
        }

        private void WatchdogLoop()
        {
            while (_watchdogRunning)
            {
                try
                {
                    if (_isProtectionActive)
                    {
                        // 1. SetCursorPos 篡改检测
                        if (_blockSetCursorPos)
                        {
                            POINT cur;
                            if (GetCursorPos(out cur))
                            {
                                int tx = _physX;
                                int ty = _physY;
                                long lastTick = Interlocked.Read(ref _lastPhysicalTick);

                                long now = Stopwatch.GetTimestamp();
                                double elapsedMs = (now - lastTick) * 1000.0 / Stopwatch.Frequency;

                                int dx = Math.Abs(cur.x - tx);
                                int dy = Math.Abs(cur.y - ty);

                                // 严格防误判准则：
                                // 当且仅当手操完全停止已超过 300 毫秒（此时人手绝对静止），
                                // 光标却凭空产生超过 50 像素的巨幅跨越，才认定为外部应用调用 SetCursorPos 强制挪动
                                if (elapsedMs > 300.0 && (dx > 50 || dy > 50))
                                {
                                    SetCursorPos(tx, ty);
                                    Interlocked.Increment(ref _countSetCursorPosBlocked);
                                    AddLog(string.Format("【瞬移复位】非物理位移从 ({0}, {1}) 强行篡改，已拉回至 ({2}, {3})",
                                        cur.x, cur.y, tx, ty));
                                    Interlocked.Exchange(ref _lastPhysicalTick, Stopwatch.GetTimestamp());
                                }
                            }
                        }

                        // 2. ClipCursor 解锁检测
                        if (_autoReleaseClip)
                        {
                            RECT clip;
                            if (GetClipCursor(out clip))
                            {
                                System.Drawing.Rectangle screen = Forms.SystemInformation.VirtualScreen;
                                int clipW = clip.right - clip.left;
                                int clipH = clip.bottom - clip.top;

                                // 仅当限制区域严重缩水（小于全屏幕的 85%）且非全屏时判定为囚禁
                                if (clipW < screen.Width * 0.85 || clipH < screen.Height * 0.85)
                                {
                                    ClipCursor(IntPtr.Zero);
                                    Interlocked.Increment(ref _countClipReleased);
                                    AddLog(string.Format("【释放锁定】解除窗口区域囚禁: [{0},{1} - {2},{3}]",
                                        clip.left, clip.top, clip.right, clip.bottom));
                                }
                            }
                        }
                    }
                }
                catch { }

                Thread.Sleep(30);
            }
        }

        private void ToggleProtection()
        {
            _isProtectionActive = !_isProtectionActive;
            if (_isProtectionActive)
            {
                InitLastPhysicalPosition();
                _notifyIcon.ShowBalloonTip(1500, "MouseGuard", "防护已【开启】：仅允许物理手操控制鼠标", Forms.ToolTipIcon.Info);
                AddLog("=== 用户已开启鼠标物理防护 ===");
            }
            else
            {
                _notifyIcon.ShowBalloonTip(1500, "MouseGuard", "防护已【临时暂停】：允许应用控制鼠标", Forms.ToolTipIcon.Warning);
                AddLog("=== 用户已临时暂停鼠标防护 ===");
            }
            UpdateUIStatus();
        }

        private void UpdateUIStatus()
        {
            if (_isProtectionActive)
            {
                _heroBadge.Background = new SolidColorBrush(Color.FromArgb(30, 16, 185, 129));
                _heroBadgeText.Text = "● 物理保护生效中";
                _heroBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                _heroTitle.Text = "纯物理控制模式已就绪";
                _btnToggle.Content = "暂停防护";
                _btnToggle.Background = new SolidColorBrush(Color.FromRgb(225, 29, 72));
                _notifyIcon.Text = "MouseGuard: 物理守护中";
            }
            else
            {
                _heroBadge.Background = new SolidColorBrush(Color.FromArgb(30, 239, 68, 68));
                _heroBadgeText.Text = "○ 防护已暂停";
                _heroBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                _heroTitle.Text = "模拟与外部控制已放行";
                _btnToggle.Content = "恢复防护";
                _btnToggle.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                _notifyIcon.Text = "MouseGuard: 已暂停";
            }
        }

        private void UpdateUIStats()
        {
            _txtStatMove.Text = Interlocked.Read(ref _countInjectedMoveBlocked).ToString();
            _txtStatSetPos.Text = Interlocked.Read(ref _countSetCursorPosBlocked).ToString();
            _txtStatClip.Text = Interlocked.Read(ref _countClipReleased).ToString();
            _txtStatClick.Text = Interlocked.Read(ref _countInjectedClickBlocked).ToString();
        }

        private void AddLog(string msg)
        {
            if (!this.Dispatcher.CheckAccess())
            {
                this.Dispatcher.BeginInvoke(new Action<string>(AddLog), msg);
                return;
            }

            string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss.fff"), msg);
            _lstLogs.Items.Insert(0, line);
            if (_lstLogs.Items.Count > 300)
            {
                _lstLogs.Items.RemoveAt(_lstLogs.Items.Count - 1);
            }
        }

        private void InitTrayIcon()
        {
            System.Drawing.Icon appIcon = GenerateAppIcon();

            _trayMenu = new Forms.ContextMenuStrip();
            _trayMenu.Items.Add("打开主控制面板", null, (s, e) => RestoreWindow());
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var itemToggle = new Forms.ToolStripMenuItem("防护状态：开启中 (点击切换)", null, (s, e) => ToggleProtection());
            _trayMenu.Items.Add(itemToggle);
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add("退出 MouseGuard", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                _watchdogRunning = false;
                System.Windows.Application.Current.Shutdown();
            });

            _trayMenu.Opening += (s, e) =>
            {
                itemToggle.Text = _isProtectionActive ? "防护状态：开启中 (点击暂停)" : "防护状态：已暂停 (点击开启)";
            };

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = appIcon,
                Text = "MouseGuard - 纯物理鼠标守护器",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
        }

        private void RestoreWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            _notifyIcon.ShowBalloonTip(1500, "MouseGuard", "程序已最小化到系统托盘，持续守护您的鼠标。", Forms.ToolTipIcon.Info);
        }

        protected override void OnClosed(EventArgs e)
        {
            _watchdogRunning = false;
            try { _uiTimer.Stop(); } catch { }
            if (_hookThreadId != 0)
            {
                PostThreadMessage(_hookThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.OnClosed(e);
        }

        private static bool CheckAutoStartStatus()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("MouseGuard");
                        return val != null;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Assembly.GetExecutingAssembly().Location;
                            key.SetValue("MouseGuard", "\"" + exePath + "\" -silent");
                        }
                        else
                        {
                            key.DeleteValue("MouseGuard", false);
                        }
                    }
                }
            }
            catch { }
        }

        private static System.Drawing.Icon GenerateAppIcon()
        {
            using (Bitmap bmp = new Bitmap(48, 48))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                GraphicsPath shield = new GraphicsPath();
                shield.AddLine(24, 2, 44, 8);
                shield.AddLine(44, 8, 44, 26);
                shield.AddBezier(44, 26, 42, 38, 24, 46, 24, 46);
                shield.AddBezier(24, 46, 6, 38, 4, 26, 4, 26);
                shield.AddLine(4, 26, 4, 8);
                shield.CloseFigure();

                using (System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Point(0, 0), new System.Drawing.Point(48, 48),
                    System.Drawing.Color.FromArgb(14, 165, 233), System.Drawing.Color.FromArgb(37, 99, 235)))
                {
                    g.FillPath(brush, shield);
                }

                using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.White, 2))
                {
                    g.DrawPath(pen, shield);
                }

                GraphicsPath mouse = new GraphicsPath();
                mouse.AddArc(16, 14, 16, 16, 180, 180);
                mouse.AddLine(32, 22, 32, 28);
                mouse.AddArc(16, 20, 16, 16, 0, 180);
                mouse.AddLine(16, 28, 16, 22);
                mouse.CloseFigure();

                using (SolidBrush whiteBrush = new SolidBrush(System.Drawing.Color.White))
                {
                    g.FillPath(whiteBrush, mouse);
                }

                using (System.Drawing.Pen bluePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(37, 99, 235), 1.5f))
                {
                    g.DrawLine(bluePen, 24, 14, 24, 22);
                    g.DrawLine(bluePen, 16, 22, 32, 22);
                }

                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    public static class UiExtensions
    {
        public static void ControlsAdd(this StackPanel sp, UIElement el)
        {
            sp.Children.Add(el);
        }

        public static void AddControl(this Grid grid, UIElement el, int row, int col)
        {
            grid.Children.Add(el);
            Grid.SetRow(el, row);
            Grid.SetColumn(el, col);
        }
    }
}
