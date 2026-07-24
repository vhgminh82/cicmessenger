using System.Diagnostics;
using System.Runtime.InteropServices;

// Tiny bootstrapper that lives alone in the install root and launches the real,
// self-contained app in the app\ subfolder. Keeps the root a single clickable file
// while the ~400 runtime files stay tucked away and remain differentially updatable.

var root = AppContext.BaseDirectory;
var appDir = Path.Combine(root, "app");
var target = Path.Combine(appDir, "CICMessenger.exe");

if (!File.Exists(target))
{
    MessageBox(IntPtr.Zero,
        $"Không tìm thấy:\n{target}\n\nVui lòng giải nén đầy đủ thư mục CICMessenger.",
        "CICMessenger", 0x10 /* MB_ICONERROR */);
    return;
}

try
{
    Process.Start(new ProcessStartInfo
    {
        FileName = target,
        WorkingDirectory = appDir,
        UseShellExecute = false
    });
}
catch (Exception ex)
{
    MessageBox(IntPtr.Zero, "Không khởi động được ứng dụng:\n" + ex.Message,
        "CICMessenger", 0x10);
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
