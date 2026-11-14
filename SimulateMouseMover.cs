using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class SimulateMouseMover {
    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool ClipCursor(ref RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left; public int top; public int right; public int bottom; }

    const uint MOUSEEVENTF_MOVE = 0x0001;

    static void Main(string[] args) {
        Console.WriteLine("========================================");
        Console.WriteLine("  模拟软件移动鼠标测试工具 (Test Mover)");
        Console.WriteLine("========================================");
        Console.WriteLine("请在数字键选择要测试的模拟行为：");
        Console.WriteLine("1. 测试【SendInput / mouse_event 模拟移动】（注入位移）");
        Console.WriteLine("2. 测试【SetCursorPos 强制瞬移】（篡改光标坐标）");
        Console.WriteLine("3. 测试【ClipCursor 囚禁鼠标】（锁定在 100x100 区域）");
        Console.WriteLine("0. 退出");
        Console.WriteLine("========================================");

        if (args.Length > 0) {
            RunAction(args[0]);
            return;
        }

        while (true) {
            Console.Write("\n请输入选项 [0-3]: ");
            string line = Console.ReadLine();
            if (string.IsNullOrEmpty(line) || line.Trim() == "0") break;
            RunAction(line.Trim());
        }
    }

    static void RunAction(string opt) {
        POINT p1, p2;
        GetCursorPos(out p1);
        Console.WriteLine(string.Format("当前鼠标位置: ({0}, {1})", p1.x, p1.y));

        if (opt == "1") {
            Console.WriteLine("--> 发送 mouse_event 移动相对 (+150, +150)...");
            mouse_event(MOUSEEVENTF_MOVE, 150, 150, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            GetCursorPos(out p2);
            Console.WriteLine(string.Format("操作后位置: ({0}, {1})", p2.x, p2.y));
            if (p1.x == p2.x && p1.y == p2.y) {
                Console.WriteLine(">>> 结果: 移动被 MouseGuard 彻底拦截，光标纹丝未动！");
            } else {
                Console.WriteLine(">>> 结果: 光标已移动 (MouseGuard 未开启或已暂停)。");
            }
        } else if (opt == "2") {
            int targetX = p1.x + 200;
            int targetY = p1.y + 200;
            Console.WriteLine(string.Format("--> 调用 SetCursorPos 强行跳转至 ({0}, {1})...", targetX, targetY));
            SetCursorPos(targetX, targetY);
            Thread.Sleep(60);
            GetCursorPos(out p2);
            Console.WriteLine(string.Format("操作后位置: ({0}, {1})", p2.x, p2.y));
            if (p2.x == p1.x && p2.y == p1.y) {
                Console.WriteLine(">>> 结果: 篡改被 MouseGuard 瞬时复位回原物理落点！");
            } else {
                Console.WriteLine(">>> 结果: 光标已被强行修改 (MouseGuard 未开启或已暂停)。");
            }
        } else if (opt == "3") {
            Console.WriteLine("--> 调用 ClipCursor 尝试将光标囚禁在左上角 (50, 50, 150, 150)...");
            RECT r;
            r.left = 50; r.top = 50; r.right = 150; r.bottom = 150;
            ClipCursor(ref r);
            Thread.Sleep(50);
            Console.WriteLine(">>> 执行完成，请观察鼠标是否被自由释放。");
        }
    }
}
