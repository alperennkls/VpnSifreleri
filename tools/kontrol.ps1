# End-to-end helpers for VPN Sifreleri (development only). Dot-source from Windows PowerShell 5.1 (STA):
#   . .\tools\kontrol.ps1 -Exe <path of the VpnSifreleri.exe under test>
# Saved as UTF-8 with BOM: it holds Turkish captions, and PS 5.1 reads BOM-less scripts as ANSI.
# The managed UI Automation client sees WinForms controls only as nameless panes, so the app is driven with window
# messages; UIA is used only to find top-level windows.
param([string] $Exe)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
if (-not ('KontrolNative' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class KontrolNative
{
    public class Child { public IntPtr Hwnd; public string Class; public string Text; }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, IntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, IntPtr size, uint type);
    [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr done);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr done);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    // A right-click on the tray icon: NotifyIcon's callback message (WM_USER + 1024) with WM_RBUTTONUP, posted to the
    // app's untitled WinForms windows (NotifyIcon's hidden window is one of them)
    public static void RightClickTray(uint processId)
    {
        List<IntPtr> targets = new List<IntPtr>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == processId && ClassOf(hwnd).StartsWith("WindowsForms10.") && GetText(hwnd).Length == 0) targets.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        foreach (IntPtr hwnd in targets) PostMessage(hwnd, 0x0400 + 1024, IntPtr.Zero, (IntPtr)0x0205);
    }

    public static List<Child> Children(IntPtr parent)
    {
        List<Child> found = new List<Child>();
        EnumChildWindows(parent, delegate(IntPtr hwnd, IntPtr unused)
        {
            Child child = new Child();
            child.Hwnd = hwnd;
            child.Class = ClassOf(hwnd);
            child.Text = GetText(hwnd);
            found.Add(child);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string ClassOf(IntPtr hwnd)
    {
        StringBuilder name = new StringBuilder(256);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    public static void SetText(IntPtr hwnd, string text) { SendMessage(hwnd, 0x000C, IntPtr.Zero, text); }

    public static string GetText(IntPtr hwnd)
    {
        int length = (int)SendMessage(hwnd, 0x000E, IntPtr.Zero, IntPtr.Zero);
        StringBuilder text = new StringBuilder(length + 1);
        SendMessage(hwnd, 0x000D, (IntPtr)(length + 1), text);
        return text.ToString();
    }

    // BM_CLICK and wait for the handler; PostClick does not wait (for buttons that open a message box)
    public static void Click(IntPtr button) { SendMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
    public static void PostClick(IntPtr button) { PostMessage(button, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
    // EM_GETPASSWORDCHAR: 0 while the box shows its text. (BM_GETCHECK cannot tell: WinForms draws its check boxes itself)
    public static int PasswordChar(IntPtr edit) { return (int)SendMessage(edit, 0x00D2, IntPtr.Zero, IntPtr.Zero); }

    public static void PressDialogButton(IntPtr dialog, IntPtr button)
    {
        SendMessage(dialog, 0x0111, (IntPtr)GetDlgCtrlID(button), button); // WM_COMMAND with IDYES / IDOK ...
    }

    public static string ForegroundTitle() { return GetText(GetForegroundWindow()); }

    // Texts of an open popup menu (#32768) by position, "" for a separator
    public static string[] MenuTexts(IntPtr menuWindow)
    {
        IntPtr menu = SendMessage(menuWindow, 0x01E1, IntPtr.Zero, IntPtr.Zero); // MN_GETHMENU
        string[] texts = new string[Math.Max(GetMenuItemCount(menu), 0)];
        for (int i = 0; i < texts.Length; i++)
        {
            StringBuilder text = new StringBuilder(256);
            GetMenuString(menu, (uint)i, text, text.Capacity, 0x0400);
            texts[i] = text.ToString();
        }
        return texts;
    }

    public static int HighlightedItem(IntPtr menuWindow)
    {
        IntPtr menu = SendMessage(menuWindow, 0x01E1, IntPtr.Zero, IntPtr.Zero);
        int count = GetMenuItemCount(menu);
        for (int i = 0; i < count; i++)
            if ((GetMenuState(menu, (uint)i, 0x0400) & 0x0080) != 0) return i;
        return -1;
    }

    public static void Key(IntPtr hwnd, int virtualKey)
    {
        PostMessage(hwnd, 0x0100, (IntPtr)virtualKey, (IntPtr)1);
        PostMessage(hwnd, 0x0101, (IntPtr)virtualKey, (IntPtr)unchecked((int)0xC0000001));
    }

    // Raw bytes of a registered clipboard format, like "00-00-00-00", or "yok"
    public static string ClipboardBytes(string format)
    {
        if (!OpenClipboard(IntPtr.Zero)) return "pano acilamadi";
        try
        {
            IntPtr mem = GetClipboardData(RegisterClipboardFormat(format));
            if (mem == IntPtr.Zero) return "yok";
            byte[] bytes = new byte[(int)GlobalSize(mem)];
            Marshal.Copy(GlobalLock(mem), bytes, 0, bytes.Length);
            GlobalUnlock(mem);
            return BitConverter.ToString(bytes);
        }
        finally { CloseClipboard(); }
    }

    // One column of a ListView in another process (LVITEMW and its text buffer live in that process)
    public static string[] ListColumn(IntPtr list, uint processId, int column)
    {
        int count = (int)SendMessage(list, 0x1004, IntPtr.Zero, IntPtr.Zero);
        string[] texts = new string[count];
        IntPtr process = OpenProcess(0x0038, false, processId);
        IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, (IntPtr)4096, 0x3000, 0x04);
        try
        {
            for (int i = 0; i < count; i++)
            {
                byte[] item = new byte[88];
                BitConverter.GetBytes(0x0001).CopyTo(item, 0);
                BitConverter.GetBytes(column).CopyTo(item, 8);
                BitConverter.GetBytes(IntPtr.Add(remote, 512).ToInt64()).CopyTo(item, 24);
                BitConverter.GetBytes(256).CopyTo(item, 32);
                IntPtr done;
                WriteProcessMemory(process, remote, item, (IntPtr)item.Length, out done);
                SendMessage(list, 0x1073, (IntPtr)i, remote);
                byte[] text = new byte[512];
                ReadProcessMemory(process, IntPtr.Add(remote, 512), text, (IntPtr)text.Length, out done);
                string s = Encoding.Unicode.GetString(text);
                texts[i] = s.Substring(0, s.IndexOf('\0'));
            }
        }
        finally
        {
            VirtualFreeEx(process, remote, IntPtr.Zero, 0x8000);
            CloseHandle(process);
        }
        return texts;
    }

    public static void SelectRow(IntPtr list, uint processId, int index)
    {
        IntPtr process = OpenProcess(0x0038, false, processId);
        IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, (IntPtr)4096, 0x3000, 0x04);
        try
        {
            byte[] item = new byte[88];
            BitConverter.GetBytes(0x0003).CopyTo(item, 12);
            BitConverter.GetBytes(0x0003).CopyTo(item, 16);
            IntPtr done;
            WriteProcessMemory(process, remote, item, (IntPtr)item.Length, out done);
            SendMessage(list, 0x102B, (IntPtr)index, remote);
        }
        finally
        {
            VirtualFreeEx(process, remote, IntPtr.Zero, 0x8000);
            CloseHandle(process);
        }
    }
}
'@
}
[void][KontrolNative]::SetProcessDPIAware()

$A = [Windows.Automation.AutomationElement]
$DataDir = Join-Path $env:APPDATA 'VpnSifreleri'
$RealDataAside = Join-Path $env:APPDATA 'VpnSifreleri.kontrol-yedek'
$Work = Join-Path $env:TEMP 'VpnSifreleri-kontrol'
New-Item -ItemType Directory -Force $Work | Out-Null

function Get-App { Get-Process -Name 'VpnSifreleri' -ErrorAction SilentlyContinue | Select-Object -First 1 }

function Wait-For([scriptblock] $probe, [int] $timeoutMs = 8000) {
    $deadline = [DateTime]::Now.AddMilliseconds($timeoutMs)
    do {
        $found = & $probe
        if ($found) { return $found }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::Now -lt $deadline)
    return $null
}

function New-Cond($property, $value) { New-Object Windows.Automation.PropertyCondition($property, $value) }

function Get-Cockpit {
    $app = Get-App
    if (-not $app) { return $null }
    $cond = New-Object Windows.Automation.AndCondition((New-Cond $A::ProcessIdProperty $app.Id), (New-Cond $A::NameProperty 'VPN Şifreleri'))
    foreach ($window in $A::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $cond)) {
        if ($window.Current.ClassName -ne '#32770') { return $window }
    }
    return $null
}

function Get-Dialog {
    $app = Get-App
    if (-not $app) { return $null }
    $cond = New-Object Windows.Automation.AndCondition((New-Cond $A::ProcessIdProperty $app.Id), (New-Cond $A::ClassNameProperty '#32770'))
    $dialog = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
    if (-not $dialog) {
        $cockpit = Get-Cockpit
        if ($cockpit) { $dialog = $cockpit.FindFirst([Windows.Automation.TreeScope]::Children, $cond) }
    }
    return $dialog
}

function Get-DialogText($dialog) {
    (@([KontrolNative]::Children([IntPtr]$dialog.Current.NativeWindowHandle) | Where-Object { $_.Class -eq 'Static' -and $_.Text } |
        ForEach-Object { $_.Text })) -join ' '
}

function Press-DialogButton($dialog, [string] $caption) {
    $hwnd = [IntPtr]$dialog.Current.NativeWindowHandle
    $button = [KontrolNative]::Children($hwnd) | Where-Object { $_.Class -eq 'Button' -and $_.Text.Replace('&', '') -eq $caption } | Select-Object -First 1
    if (-not $button) { throw "Dugme yok: $caption" }
    [KontrolNative]::PressDialogButton($hwnd, $button.Hwnd)
}

# Boxes in creation order (Müşteri, Kullanıcı adı, Şifre); the status line is the label that is not a caption
function Get-Parts($cockpit) {
    $kids = [KontrolNative]::Children([IntPtr]$cockpit.Current.NativeWindowHandle)
    $edits = @($kids | Where-Object { $_.Class -like 'WindowsForms10.EDIT*' })
    [pscustomobject]@{
        NameBox = $edits[0].Hwnd
        UsernameBox = $edits[1].Hwnd
        PasswordBox = $edits[2].Hwnd
        List = ($kids | Where-Object { $_.Class -like '*SysListView32*' } | Select-Object -First 1).Hwnd
        Buttons = @($kids | Where-Object { $_.Class -like 'WindowsForms10.BUTTON*' })
        Status = ($kids | Where-Object { $_.Class -like 'WindowsForms10.STATIC*' -and $_.Text -notin 'Müşteri', 'Kullanıcı adı', 'Şifre' } | Select-Object -First 1).Hwnd
    }
}

function Get-ButtonHwnd($cockpit, [string] $caption) {
    $button = (Get-Parts $cockpit).Buttons | Where-Object { $_.Text -eq $caption } | Select-Object -First 1
    if (-not $button) { throw "Dugme yok: $caption" }
    $button.Hwnd
}

function Click-Button($cockpit, [string] $caption) { [KontrolNative]::Click((Get-ButtonHwnd $cockpit $caption)) }
function Click-ButtonAsync($cockpit, [string] $caption) { [KontrolNative]::PostClick((Get-ButtonHwnd $cockpit $caption)) }
function Get-PasswordsShown($cockpit) { [KontrolNative]::PasswordChar((Get-Parts $cockpit).PasswordBox) -eq 0 }

function Fill($cockpit, [string] $name, [string] $username, [string] $password) {
    $parts = Get-Parts $cockpit
    [KontrolNative]::SetText($parts.NameBox, $name)
    [KontrolNative]::SetText($parts.UsernameBox, $username)
    [KontrolNative]::SetText($parts.PasswordBox, $password)
}

function Get-Rows($cockpit, [int] $column = 0) { @([KontrolNative]::ListColumn((Get-Parts $cockpit).List, [uint32](Get-App).Id, $column)) }
function Get-Status($cockpit) { [KontrolNative]::GetText((Get-Parts $cockpit).Status) }

function Select-Row($cockpit, [string] $name) {
    $index = [Array]::IndexOf((Get-Rows $cockpit), $name)
    if ($index -lt 0) { throw "Listede yok: $name" }
    [KontrolNative]::SelectRow((Get-Parts $cockpit).List, [uint32](Get-App).Id, $index)
}

function Open-TrayMenu {
    [KontrolNative]::RightClickTray([uint32](Get-App).Id)
    $menu = Wait-For { $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Cond $A::ClassNameProperty '#32768')) }
    if (-not $menu) { throw 'Tepsi menusu acilmadi' }
    return $menu
}

function Get-MenuNames($menu) { @([KontrolNative]::MenuTexts([IntPtr]$menu.Current.NativeWindowHandle) | Where-Object { $_ }) }

# Down arrow until the wanted item is highlighted, then Enter (disabled items are skipped by the menu itself)
function Invoke-MenuItem($menu, [string] $name) {
    $hwnd = [IntPtr]$menu.Current.NativeWindowHandle
    $target = [Array]::IndexOf([KontrolNative]::MenuTexts($hwnd), $name)
    if ($target -lt 0) { throw "Menude yok: $name" }
    for ($tries = 0; $tries -lt 15; $tries++) {
        if ([KontrolNative]::HighlightedItem($hwnd) -eq $target) {
            [KontrolNative]::Key($hwnd, 0x0D)
            Wait-For { -not $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Cond $A::ClassNameProperty '#32768')) } 3000 | Out-Null
            return
        }
        [KontrolNative]::Key($hwnd, 0x28)
        Start-Sleep -Milliseconds 150
    }
    [KontrolNative]::Key($hwnd, 0x1B)
    throw "Menude secilemedi: $name"
}

function Exit-App {
    if (-not (Get-App)) { return }
    Invoke-MenuItem (Open-TrayMenu) 'Çıkış'
    if (-not (Wait-For { -not (Get-App) } 5000)) { throw 'Uygulama kapanmadi' }
}

# The notice sits above the tray, bottom right of the primary screen; cropped to it, deleted after looking
function Save-ToastShot([string] $name) {
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $rect = New-Object System.Drawing.Rectangle(($area.Right - 440), ($area.Bottom - 110), 440, 110)
    $bmp = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Location, [System.Drawing.Point]::Empty, $rect.Size)
    $g.Dispose()
    $path = Join-Path $Work ($name + '.png')
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

function Get-FileDigests([string] $folder) {
    @(Get-ChildItem $folder -File | Sort-Object Name | ForEach-Object { $_.Name + '=' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash })
}

$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# The user's real data goes aside (one rename) while tests run, and comes back byte for byte. The "Windows ile başlat"
# value is kept too: a test exe started from elsewhere moves it to itself.
function Save-RealData {
    if (Get-App) { throw 'Once uygulama kapanmali' }
    if (Test-Path $RealDataAside) { throw "Onceki yedek duruyor: $RealDataAside" }
    [string](Get-ItemProperty $RunKey -ErrorAction SilentlyContinue).VpnSifreleri | Set-Content -Encoding UTF8 (Join-Path $Work 'gercek-run.txt')
    if (-not (Test-Path $DataDir)) { 'gercek veri yok'; return }
    Get-FileDigests $DataDir | Set-Content -Encoding UTF8 (Join-Path $Work 'gercek-ozet.txt')
    Move-Item $DataDir $RealDataAside
    'gercek veri kenarda: ' + (Get-ChildItem $RealDataAside -File).Count + ' dosya'
}

function Restore-RealData {
    if (Get-App) { throw 'Once uygulama kapanmali' }
    $run = [string](Get-Content -Encoding UTF8 (Join-Path $Work 'gercek-run.txt') -ErrorAction SilentlyContinue)
    if ($run) { Set-ItemProperty $RunKey -Name 'VpnSifreleri' -Value $run } else { Remove-ItemProperty $RunKey -Name 'VpnSifreleri' -ErrorAction SilentlyContinue }
    'Windows ile baslat geri kondu: [' + [string](Get-ItemProperty $RunKey -ErrorAction SilentlyContinue).VpnSifreleri + ']'
    if (-not (Test-Path $RealDataAside)) { 'geri konacak yedek yok'; return }
    if (Test-Path $DataDir) { Remove-Item $DataDir -Recurse -Force }
    Move-Item $RealDataAside $DataDir
    $before = @(Get-Content -Encoding UTF8 (Join-Path $Work 'gercek-ozet.txt'))
    $after = Get-FileDigests $DataDir
    'gercek veri geri kondu, birebir ayni: ' + (($before -join '|') -eq ($after -join '|'))
}
