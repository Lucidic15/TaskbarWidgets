// ==WindhawkMod==
// @id              taskbar-snipper
// @name            Taskbar Snipper
// @description     Print Screen, or a button on the taskbar, selects part of the screen with a toolbox right above it to add arrows, boxes and text, then copies or saves it
// @version         1.0.0
// @author          Lucidic15
// @github          https://github.com/Lucidic15
// @include         windhawk.exe
// @compilerOptions -lgdiplus -lgdi32 -luser32 -lshell32 -lshcore -ldwmapi -ladvapi32 -lole32 -luuid -lcomdlg32 -lmsimg32
// @license         MIT
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
# Taskbar Snipper

Press **Print Screen**, or click the screenshot button on the taskbar, to select part of the
screen. A toolbox opens right above the selection to mark it up with a pencil, lines, arrows, boxes
and text, in any color and thickness. When you're done, click anywhere outside the selection (or press **Enter**),
and the screenshot is copied to the clipboard, saved to a folder, or both, as you choose in the
mod's settings.

## Selecting

- **Drag** to select an area. The screen stays as it was when you pressed Print Screen.
- **Click** a window to select all of it, or the desktop to select the whole display.
- **Esc** or **right-click** cancels.

## The toolbox

- **Select** (V), **Pencil** (P), **Line** (L), **Arrow** (A), **Box** (B) and **Text** (T). Drag
  inside the selection to draw; hold **Shift** to keep a line or arrow to 45° steps, or a box
  square. A click with the pencil makes a dot. With Text, click where the text goes and type;
  **Enter** starts a new line, and **Esc** or a click elsewhere finishes it.
- **Colors**: eight at hand, and the last swatch picks any color.
- **Thickness**: **−** and **+**, or the mouse wheel. Text gets bigger with it too.
- **Undo** (Ctrl+Z), **Copy** (Ctrl+C), **Save** (Ctrl+S) and **Close** (Esc).

## Moving and changing marks

- Whatever tool is in hand, **drag** anything already drawn to move it; the pointer turns into a
  four-way arrow over it. The **Select** tool only selects and moves, for when you'd rather not
  draw by accident.
- **Click** a mark to select it. Its color and thickness show in the toolbox, and changing them
  changes the mark. The **arrow keys** nudge it (with **Shift**, 10 pixels at a time), **Delete**
  removes it, and **Esc** lets go of it.
- To change text, click it with the Text tool, or click it again once it's selected.
- Undo takes back moves and changes too.

**Copy** and **Save** do just that. Clicking outside the selection or pressing Enter does what the
**When you're done** setting says: copy, save, or both. Screenshots are saved as PNG files named by
the date and time, in the folder from the settings, or Windows' Screenshots folder (in Pictures).
A note in the corner of the screen says what happened; click it to show a saved screenshot in File
Explorer.

## The screenshot button

The button sits on the taskbar like the Windows weather widget: click it to take a screenshot,
right-click it for its menu, and drag it to move it along the taskbar. Turn it off with **Screenshot
button on the taskbar** in the mod's settings; Print Screen works either way.

With the Taskbar Audio Flipper or Taskbar RDP Launcher mod running too, their widgets can be joined
with the button into one: right-click any of them, open **Link widgets**, and choose **Link with** the
other. Linked, they sit side by side with a divider between them, and dragging any of them moves
them all; **Move this widget left** and **right** change their order.

## Good to know

- Only Print Screen on its own is taken over. Alt+Print Screen, Win+Print Screen and Win+Shift+S
  work as usual, and turning the mod off gives Print Screen back to Windows.
- While a program running as administrator is in front, Windows doesn't let the mod see Print
  Screen, so Windows handles it as usual.
- The tool, color and thickness you used last are remembered.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- showButton: true
  $name: Screenshot button on the taskbar
  $description: A button on the taskbar that takes a screenshot, as Print Screen does. Right-click it to link it with the Taskbar Audio Flipper and Taskbar RDP Launcher widgets.
- afterCapture: both
  $name: When you're done
  $description: What clicking outside the selection, or pressing Enter, does with the screenshot. The toolbox's Copy and Save buttons do just one of them.
  $options:
  - copy: Copy it to the clipboard
  - save: Save it to the folder
  - both: Copy it and save it
- saveFolder: ""
  $name: Folder to save screenshots in
  $description: Leave it empty for Windows' Screenshots folder, in Pictures. Environment variables like %USERPROFILE% work, and the folder is created if it isn't there.
*/
// ==/WindhawkModSettings==

#include <windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shellscalingapi.h>
#include <dwmapi.h>
#include <sddl.h>
#include <commdlg.h>
#include <gdiplus.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cwctype>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace gp = Gdiplus;

namespace {

using ARGB = gp::ARGB;

const wchar_t kSelfApp[] = L"TaskbarSnipper";
const wchar_t kWidgetClassName[] = L"TaskbarSnipper.Mod.Widget";
const wchar_t kWidgetTitle[] = L"Taskbar Snipper";
// This widget's place among the widgets that can link (WidgetLink::kMembers). It was never linked
// as a pair, as the other two were before there were three.
constexpr int kSelfMember = 2;
constexpr int kLegacyPartner = -1;

// ---------------------------------------------------------------------------
// Strings, files and folders

std::wstring Trim(const std::wstring& text) {
    size_t start = 0;
    size_t end = text.size();
    while (start < end && iswspace(text[start])) {
        start++;
    }
    while (end > start && iswspace(text[end - 1])) {
        end--;
    }
    return text.substr(start, end - start);
}

std::wstring ExpandEnvironment(const std::wstring& text) {
    DWORD length = ExpandEnvironmentStringsW(text.c_str(), nullptr, 0);
    if (length == 0) {
        return text;
    }
    std::wstring expanded(length, L'\0');
    ExpandEnvironmentStringsW(text.c_str(), expanded.data(), length);
    expanded.resize(wcslen(expanded.c_str()));
    return expanded;
}

std::wstring KnownFolder(REFKNOWNFOLDERID folder) {
    PWSTR path = nullptr;
    std::wstring result;
    if (SUCCEEDED(SHGetKnownFolderPath(folder, KF_FLAG_DEFAULT, nullptr, &path))) {
        result = path;
    }
    CoTaskMemFree(path);
    return result;
}

bool FileExists(const std::wstring& path) {
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
}

// Menu text: "&" would underline the next letter.
std::wstring MenuText(const std::wstring& text) {
    std::wstring escaped;
    for (wchar_t c : text) {
        if (c == L'&') {
            escaped += L'&';
        }
        escaped += c;
    }
    return escaped;
}

// ---------------------------------------------------------------------------
// The mod's storage, for the tool, color and thickness used last. Windhawk keeps one for the whole
// PC, while the mod runs separately for each signed-in Windows user, so each user's values are kept
// apart under their SID.

namespace Storage {

std::wstring ReadUserSid() {
    std::wstring sid;
    HANDLE token = nullptr;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        alignas(TOKEN_USER) BYTE buffer[sizeof(TOKEN_USER) + SECURITY_MAX_SID_SIZE];
        DWORD size = 0;
        PWSTR text = nullptr;
        if (GetTokenInformation(token, TokenUser, buffer, sizeof(buffer), &size) &&
            ConvertSidToStringSidW(((TOKEN_USER*)buffer)->User.Sid, &text)) {
            sid = text;
            LocalFree(text);
        }
        CloseHandle(token);
    }
    return sid.empty() ? L"User" : sid;
}

// The user's SID, which stays the same for as long as the mod runs.
const std::wstring& UserSid() {
    static const std::wstring sid = ReadUserSid();
    return sid;
}

std::wstring ValueName(const wchar_t* name) {
    return UserSid() + L"." + name;
}

int GetInt(const wchar_t* name, int fallback) {
    return Wh_GetIntValue(ValueName(name).c_str(), fallback);
}

void SetInt(const wchar_t* name, int value) {
    Wh_SetIntValue(ValueName(name).c_str(), value);
}

}  // namespace Storage

// ---------------------------------------------------------------------------
// Settings

enum class AfterCapture { Copy, Save, Both };

struct ModSettings {
    bool showButton = true;
    AfterCapture afterCapture = AfterCapture::Both;
    std::wstring saveFolder;

    static ModSettings Load() {
        ModSettings settings;
        settings.showButton = Wh_GetIntSetting(L"showButton") != 0;
        PCWSTR after = Wh_GetStringSetting(L"afterCapture");
        if (wcscmp(after, L"copy") == 0) {
            settings.afterCapture = AfterCapture::Copy;
        } else if (wcscmp(after, L"save") == 0) {
            settings.afterCapture = AfterCapture::Save;
        }
        Wh_FreeStringSetting(after);
        PCWSTR folder = Wh_GetStringSetting(L"saveFolder");
        std::wstring path = Trim(folder);
        Wh_FreeStringSetting(folder);
        // Quotes around a pasted path, and a backslash at the end, are fine.
        if (path.size() >= 2 && path.front() == L'"' && path.back() == L'"') {
            path = Trim(path.substr(1, path.size() - 2));
        }
        while (path.size() > 3 && (path.back() == L'\\' || path.back() == L'/')) {
            path.pop_back();
        }
        settings.saveFolder = path.empty() ? KnownFolder(FOLDERID_Screenshots) : ExpandEnvironment(path);
        return settings;
    }
};

// ---------------------------------------------------------------------------
// Windows settings and displays

bool ReadUserFlag(const wchar_t* key, const wchar_t* valueName, bool fallback) {
    DWORD value = 0;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, key, valueName, RRF_RT_REG_DWORD, nullptr, &value, &size) != ERROR_SUCCESS) {
        return fallback;
    }
    return value != 0;
}

// The Windows settings the widget and popups follow. They're read once, and again when Windows
// says a setting changed (WM_SETTINGCHANGE), rather than on every placement update or paint.
struct ShellSettings {
    bool lightTaskbar = false;  // "Choose your default Windows mode", which the taskbar follows.
    bool lightApps = true;      // "Choose your default app mode", which the popups follow.

    static ShellSettings Read() {
        const wchar_t* personalize = L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
        ShellSettings settings;
        settings.lightTaskbar = ReadUserFlag(personalize, L"SystemUsesLightTheme", false);
        settings.lightApps = ReadUserFlag(personalize, L"AppsUseLightTheme", true);
        return settings;
    }
};

ShellSettings& CurrentShellSettings() {
    static ShellSettings settings = ShellSettings::Read();
    return settings;
}

void RefreshShellSettings() {
    CurrentShellSettings() = ShellSettings::Read();
}

bool TaskbarUsesLightTheme() {
    return CurrentShellSettings().lightTaskbar;
}

bool AppsUseLightTheme() {
    return CurrentShellSettings().lightApps;
}

// The scale (1 at 100%) of the display a screen rectangle is mostly on.
float ScaleForScreenRect(const RECT& rect) {
    UINT dpiX = 96;
    UINT dpiY = 96;
    if (FAILED(GetDpiForMonitor(MonitorFromRect(&rect, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, &dpiX, &dpiY))) {
        return 1;
    }
    return dpiX / 96.0f;
}

RECT WorkAreaForScreenRect(const RECT& rect) {
    MONITORINFO info{sizeof(info)};
    GetMonitorInfoW(MonitorFromRect(&rect, MONITOR_DEFAULTTONEAREST), &info);
    return info.rcWork;
}

RECT OffsetRectBy(RECT rect, int dx, int dy) {
    OffsetRect(&rect, dx, dy);
    return rect;
}

RECT InflatedRect(RECT rect, int by) {
    InflateRect(&rect, by, by);
    return rect;
}

// ---------------------------------------------------------------------------
// Drawing

gp::Color Argb(int a, int r, int g, int b) {
    return gp::Color((BYTE)a, (BYTE)r, (BYTE)g, (BYTE)b);
}

gp::Color WithAlpha(int alpha, const gp::Color& color) {
    return gp::Color((BYTE)alpha, color.GetR(), color.GetG(), color.GetB());
}

gp::RectF ToRectF(const RECT& rect) {
    return gp::RectF((float)rect.left, (float)rect.top, (float)(rect.right - rect.left), (float)(rect.bottom - rect.top));
}

void AddRoundedRect(gp::GraphicsPath& path, const gp::RectF& rect, float radius) {
    float diameter = std::max(0.1f, std::min(radius * 2, std::min(rect.Width, rect.Height)));
    path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
    path.AddArc(rect.GetRight() - diameter, rect.Y, diameter, diameter, 270, 90);
    path.AddArc(rect.GetRight() - diameter, rect.GetBottom() - diameter, diameter, diameter, 0, 90);
    path.AddArc(rect.X, rect.GetBottom() - diameter, diameter, diameter, 90, 90);
    path.CloseFigure();
}

void FillRoundedRect(gp::Graphics& graphics, const gp::Color& color, const gp::RectF& rect, float radius) {
    if (rect.Width <= 0 || rect.Height <= 0) {
        return;
    }
    gp::GraphicsPath path;
    AddRoundedRect(path, rect, radius);
    gp::SolidBrush brush(color);
    graphics.FillPath(&brush, &path);
}

void DrawRoundedRect(gp::Graphics& graphics, const gp::Color& color, const gp::RectF& rect, float radius, float width) {
    if (rect.Width <= 0 || rect.Height <= 0) {
        return;
    }
    gp::GraphicsPath path;
    AddRoundedRect(path, rect, radius);
    gp::Pen pen(color, width);
    graphics.DrawPath(&pen, &path);
}

// The Windows icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10.
const wchar_t* IconFontName() {
    static const wchar_t* name = []() -> const wchar_t* {
        for (const wchar_t* candidate : {L"Segoe Fluent Icons", L"Segoe MDL2 Assets"}) {
            gp::FontFamily family(candidate);
            if (family.IsAvailable()) {
                return candidate;
            }
        }
        return nullptr;
    }();
    return name;
}

// Glyphs from the Windows icon font for the toolbox.
const wchar_t kGlyphText[] = {0xE8D2, 0};    // Font
const wchar_t kGlyphPencil[] = {0xE70F, 0};  // Edit
const wchar_t kGlyphColor[] = {0xE790, 0};   // Color
const wchar_t kGlyphMinus[] = {0xE738, 0};   // Remove
const wchar_t kGlyphPlus[] = {0xE710, 0};    // Add
const wchar_t kGlyphUndo[] = {0xE7A7, 0};    // Undo
const wchar_t kGlyphCopy[] = {0xE8C8, 0};    // Copy
const wchar_t kGlyphSave[] = {0xE74E, 0};    // Save
const wchar_t kGlyphClose[] = {0xE711, 0};   // Cancel

// Draws an icon-font glyph at one font size, centered in the box by its ink.
void DrawGlyph(gp::Graphics& graphics, const wchar_t* glyph, float emSize, const gp::RectF& box, const gp::Color& color) {
    const wchar_t* fontName = IconFontName();
    if (!fontName) {
        return;
    }
    gp::FontFamily family(fontName);
    gp::GraphicsPath path;
    path.AddString(glyph, -1, &family, gp::FontStyleRegular, emSize, gp::PointF(0, 0), gp::StringFormat::GenericTypographic());
    gp::RectF ink;
    path.GetBounds(&ink);
    if (ink.Width <= 0 || ink.Height <= 0) {
        return;
    }
    gp::Matrix transform;
    transform.Translate(box.X + (box.Width - ink.Width) / 2 - ink.X, box.Y + (box.Height - ink.Height) / 2 - ink.Y);
    path.Transform(&transform);
    gp::SolidBrush brush(color);
    graphics.FillPath(&brush, &path);
}

enum class TextAlign { Left, Center };

// One line of text, vertically centered in the rectangle and cut short with "…" if it doesn't fit.
void DrawTextLine(gp::Graphics& graphics, const std::wstring& text, const gp::Font& font, const gp::Color& color,
                  const gp::RectF& rect, TextAlign align = TextAlign::Left) {
    gp::StringFormat format(gp::StringFormatFlagsNoWrap);
    format.SetTrimming(gp::StringTrimmingEllipsisCharacter);
    format.SetLineAlignment(gp::StringAlignmentCenter);
    format.SetAlignment(align == TextAlign::Center ? gp::StringAlignmentCenter : gp::StringAlignmentNear);
    gp::SolidBrush brush(color);
    graphics.DrawString(text.c_str(), (INT)text.size(), &font, rect, &format, &brush);
}

float MeasureTextWidth(gp::Graphics& graphics, const std::wstring& text, const gp::Font& font) {
    gp::StringFormat format(gp::StringFormatFlagsNoWrap | gp::StringFormatFlagsNoClip | gp::StringFormatFlagsMeasureTrailingSpaces);
    gp::RectF bounds;
    graphics.MeasureString(text.c_str(), (INT)text.size(), &font, gp::PointF(0, 0), &format, &bounds);
    return bounds.Width;
}

// A small rounded label with a line of text, like a tooltip, at the point (its top-left).
gp::RectF LabelRect(gp::Graphics& graphics, const std::wstring& text, const gp::Font& font, float x, float y, float scale) {
    float width = MeasureTextWidth(graphics, text, font) + 20 * scale;
    return gp::RectF(x, y, width, 28 * scale);
}

void DrawLabel(gp::Graphics& graphics, const std::wstring& text, const gp::Font& font, const gp::RectF& rect, float scale) {
    FillRoundedRect(graphics, Argb(225, 32, 32, 32), rect, 6 * scale);
    DrawRoundedRect(graphics, Argb(255, 70, 70, 70), rect, 6 * scale, 1);
    DrawTextLine(graphics, text, font, Argb(255, 255, 255, 255), rect, TextAlign::Center);
}

// Colors for the toolbox and notes, for the "default app mode" (light or dark) chosen in Windows.
struct Palette {
    gp::Color background, foreground, secondary, border, hover, pressed, accent;

    static Palette Current() {
        if (AppsUseLightTheme()) {
            return {Argb(250, 249, 249, 249), Argb(255, 27, 27, 27), Argb(255, 96, 96, 96), Argb(255, 213, 213, 213),
                    Argb(18, 0, 0, 0), Argb(32, 0, 0, 0), Argb(255, 0, 95, 184)};
        }
        return {Argb(250, 44, 44, 44), Argb(255, 255, 255, 255), Argb(255, 197, 197, 197), Argb(255, 70, 70, 70),
                Argb(22, 255, 255, 255), Argb(36, 255, 255, 255), Argb(255, 96, 205, 255)};
    }
};

// The glow around the selection.
const gp::Color kSelectionColor = Argb(255, 76, 194, 255);

// ---------------------------------------------------------------------------
// Window classes

// The window procedure of RegisterForwardingClass: forwards messages to the object of type T
// passed as the creation parameter, calling T::HandleMessage.
template <typename T>
LRESULT CALLBACK ForwardingWindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_NCCREATE) {
        auto create = (CREATESTRUCTW*)lParam;
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, (LONG_PTR)create->lpCreateParams);
        ((T*)create->lpCreateParams)->hwnd_ = hwnd;
    }
    auto self = (T*)GetWindowLongPtrW(hwnd, GWLP_USERDATA);
    if (self) {
        LRESULT result = 0;
        if (self->HandleMessage(message, wParam, lParam, result)) {
            return result;
        }
    }
    if (message == WM_NCDESTROY) {
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
        if (self) {
            self->hwnd_ = nullptr;
        }
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

// The mod's own module (its DLL), which its window classes belong to.
HINSTANCE ModuleInstance() {
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCWSTR)&ModuleInstance, &module);
    return module;
}

// The window classes registered so far, to unregister when the UI thread ends.
std::vector<std::wstring>& RegisteredClasses() {
    static std::vector<std::wstring> classes;
    return classes;
}

// Registers a window class whose windows forward their messages to an object of type T, passed
// as the creation parameter. Returns its name, or null if it couldn't be registered.
template <typename T>
const wchar_t* RegisterForwardingClass(const wchar_t* name, LPCWSTR cursor = IDC_ARROW) {
    std::vector<std::wstring>& classes = RegisteredClasses();
    if (std::find(classes.begin(), classes.end(), name) != classes.end()) {
        return name;
    }
    WNDCLASSEXW windowClass{sizeof(windowClass)};
    windowClass.hInstance = ModuleInstance();
    windowClass.hCursor = LoadCursorW(nullptr, cursor);
    windowClass.lpszClassName = name;
    windowClass.lpfnWndProc = ForwardingWindowProc<T>;
    if (!RegisterClassExW(&windowClass)) {
        Wh_Log(L"Couldn't register window class %s: error %u", name, GetLastError());
        return nullptr;
    }
    classes.push_back(name);
    return name;
}

void UnregisterWindowClasses() {
    for (const std::wstring& name : RegisteredClasses()) {
        UnregisterClassW(name.c_str(), ModuleInstance());
    }
    RegisteredClasses().clear();
}

// A hidden window for messages from the keyboard hook and from Windhawk's thread.
class MessageWindow {
public:
    std::function<void(UINT message, WPARAM wParam, LPARAM lParam)> onMessage;

    bool Create(const wchar_t* className) {
        if (const wchar_t* registered = RegisterForwardingClass<MessageWindow>(className)) {
            CreateWindowExW(0, registered, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, ModuleInstance(), this);
        }
        return hwnd_ != nullptr;
    }

    ~MessageWindow() {
        if (hwnd_) {
            DestroyWindow(hwnd_);
        }
    }

    HWND Handle() const { return hwnd_; }

    bool HandleMessage(UINT message, WPARAM wParam, LPARAM lParam, LRESULT& result) {
        if (message >= WM_APP && message < 0xC000) {
            if (onMessage) {
                onMessage(message, wParam, lParam);
            }
            return true;
        }
        return false;
    }

    HWND hwnd_ = nullptr;
};

// Messages to the app's message window.
constexpr UINT kPrintScreenMessage = WM_APP + 1;
constexpr UINT kEscapeMessage = WM_APP + 2;
constexpr UINT kSettingsChangedMessage = WM_APP + 3;
constexpr UINT kOverlayClosedMessage = WM_APP + 4;

// ---------------------------------------------------------------------------
// Taskbar

HWND TaskbarWindow() {
    return FindWindowW(L"Shell_TrayWnd", nullptr);
}

// The main taskbar and its monitor, when the taskbar is horizontal and on screen (not slid away
// by auto-hide).
bool TryGetVisibleTaskbar(RECT& bar, RECT& monitor) {
    HWND window = TaskbarWindow();
    if (!window || !IsWindowVisible(window) || !GetWindowRect(window, &bar)) {
        return false;
    }
    MONITORINFO info{sizeof(info)};
    if (!GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &info)) {
        return false;
    }
    monitor = info.rcMonitor;
    int width = bar.right - bar.left;
    int height = bar.bottom - bar.top;
    RECT visible{};
    IntersectRect(&visible, &bar, &monitor);
    return width > height && height > 0 && visible.bottom - visible.top >= height - 2;
}

// Whether a full-screen app (game, video, presentation) is in front on the monitor.
// shellReportsFullScreen is Windows' own verdict, the notification that makes the taskbar step
// aside; screen overlays such as screenshot tools cover the screen too, but Windows doesn't
// count them, so neither does this.
bool IsFullScreenAppActive(const RECT& monitor, HWND ignore, bool shellReportsFullScreen) {
    QUERY_USER_NOTIFICATION_STATE state;
    if (SUCCEEDED(SHQueryUserNotificationState(&state)) && state == QUNS_RUNNING_D3D_FULL_SCREEN) {
        return true;
    }
    if (!shellReportsFullScreen) {
        return false;
    }
    // The notification doesn't say which monitor, so also check the app in front covers this one.
    HWND window = GetForegroundWindow();
    RECT rect;
    if (!window || window == ignore || !GetWindowRect(window, &rect)) {
        return false;
    }
    WCHAR className[256] = {};
    GetClassNameW(window, className, ARRAYSIZE(className));
    for (const wchar_t* shellClass : {L"Progman", L"WorkerW", L"Shell_TrayWnd", L"Shell_SecondaryTrayWnd"}) {
        if (wcscmp(className, shellClass) == 0) {
            return false;
        }
    }
    return rect.left <= monitor.left && rect.top <= monitor.top && rect.right >= monitor.right &&
           rect.bottom >= monitor.bottom;
}

UINT DpiForPoint(POINT point) {
    UINT dpiX = 96;
    UINT dpiY = 96;
    if (FAILED(GetDpiForMonitor(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, &dpiX, &dpiY))) {
        return 96;
    }
    return dpiX;
}

// ---------------------------------------------------------------------------
// Menus

// A popup menu built from items with actions; Show runs the chosen item's action.
class Menu {
public:
    Menu() : menu_(CreatePopupMenu()), actions_(std::make_shared<std::vector<std::function<void()>>>()) {}

    ~Menu() {
        if (owner_ && menu_) {
            DestroyMenu(menu_);
        }
    }

    Menu(const Menu&) = delete;
    Menu& operator=(const Menu&) = delete;

    void Add(const std::wstring& text, std::function<void()> action, bool checked = false, bool enabled = true) {
        actions_->push_back(std::move(action));
        UINT flags = MF_STRING | (checked ? MF_CHECKED : 0) | (enabled ? 0 : MF_GRAYED);
        AppendMenuW(menu_, flags, actions_->size(), MenuText(text).c_str());
    }

    void AddSeparator() { AppendMenuW(menu_, MF_SEPARATOR, 0, nullptr); }

    // A submenu; items added to it share this menu's actions.
    Menu& AddSubmenu(const std::wstring& text, bool enabled = true) {
        submenus_.push_back(std::unique_ptr<Menu>(new Menu(actions_)));
        Menu& submenu = *submenus_.back();
        AppendMenuW(menu_, MF_POPUP | (enabled ? 0 : MF_GRAYED), (UINT_PTR)submenu.menu_, MenuText(text).c_str());
        return submenu;
    }

    // Shows the menu above the point (the widget's top edge) and runs the chosen action.
    void Show(HWND owner, POINT point) {
        // Like a tray icon does, take the foreground first so the menu closes when clicking elsewhere.
        SetForegroundWindow(owner);
        UINT command = TrackPopupMenuEx(menu_, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN,
                                        point.x, point.y, owner, nullptr);
        PostMessageW(owner, WM_NULL, 0, 0);
        if (command > 0 && command <= actions_->size() && (*actions_)[command - 1]) {
            auto actions = actions_;  // The action may rebuild menus.
            (*actions)[command - 1]();
        }
    }

private:
    explicit Menu(std::shared_ptr<std::vector<std::function<void()>>> actions)
        : menu_(CreatePopupMenu()), actions_(std::move(actions)), owner_(false) {}

    HMENU menu_;
    std::shared_ptr<std::vector<std::function<void()>>> actions_;
    std::vector<std::unique_ptr<Menu>> submenus_;
    bool owner_ = true;  // Submenus are destroyed with the menu they're in.
};

// ---------------------------------------------------------------------------
// Linking the Taskbar Audio Flipper, Taskbar RDP Launcher and Taskbar Snipper widgets (the same
// code is in all three mods). Linked widgets sit side by side as one, in the order chosen, with a
// divider between them, and dragging any of them moves them all.
//
// Each mod keeps its own copy of the group (which widgets are in it, left to right, and where it
// is) in its storage, and shows it to the others as properties of its widget window, stamped with
// when it last changed: the newest copy wins. Each widget finds the others by their window class
// and posts them a message whenever its group, size or place changes.

namespace WidgetLink {

struct Member {
    const wchar_t* name;         // As the menus show it.
    const wchar_t* windowClass;  // Of its widget window.
};

constexpr Member kMembers[] = {
    {L"Audio Flipper", L"AudioFlipper.Mod.Widget"},
    {L"RDP Launcher", L"RDPLauncher.Mod.Widget"},
    {L"Snipper", L"TaskbarSnipper.Mod.Widget"},
};
constexpr int kMemberCount = ARRAYSIZE(kMembers);

struct Group {
    std::vector<int> order;  // The widgets in it, by their place in kMembers, left to right.
    int offset = -1;         // Its left edge from the taskbar's left edge; negative for the default.
    int stamp = 0;           // When it last changed, in seconds since 2020: the newest copy wins.

    bool Has(int member) const { return std::find(order.begin(), order.end(), member) != order.end(); }

    // Whether this widget is linked with another.
    bool Linked() const { return order.size() >= 2 && Has(kSelfMember); }

    // As one number, the way it's stored and shown to the other widgets: two bits for each widget,
    // left to right (its place plus one), and the offset above them.
    int Pack() const {
        int packed = 0;
        for (size_t i = 0; i < order.size() && i < (size_t)kMemberCount; i++) {
            packed |= (order[i] + 1) << (2 * i);
        }
        return packed | (std::max(-1, offset) + 1) << (2 * kMemberCount);
    }

    static Group Unpack(int packed, int stamp) {
        Group group;
        for (int i = 0; i < kMemberCount; i++) {
            int member = ((packed >> (2 * i)) & 3) - 1;
            if (member >= 0 && member < kMemberCount && !group.Has(member)) {
                group.order.push_back(member);
            }
        }
        group.offset = (packed >> (2 * kMemberCount)) - 1;
        group.stamp = stamp;
        return group;
    }
};

const wchar_t kGroupProperty[] = L"TaskbarWidgetLink.Mod.Group";
const wchar_t kStampProperty[] = L"TaskbarWidgetLink.Mod.GroupStamp";

// Posted to the other widgets when the group, or this widget's size or place, changes.
UINT ChangedMessage() {
    static UINT message = RegisterWindowMessageW(L"TaskbarWidgetLink.Mod.GroupChanged");
    return message;
}

// A stamp for a change made now, newer than the given one even within the same second.
int NewStamp(int newerThan) {
    SYSTEMTIME epoch{2020, 1, 3, 1};
    FILETIME epochTime;
    FILETIME now;
    SystemTimeToFileTime(&epoch, &epochTime);
    GetSystemTimeAsFileTime(&now);
    ULARGE_INTEGER start{{epochTime.dwLowDateTime, epochTime.dwHighDateTime}};
    ULARGE_INTEGER current{{now.dwLowDateTime, now.dwHighDateTime}};
    int seconds = current.QuadPart > start.QuadPart ? (int)((current.QuadPart - start.QuadPart) / 10000000) : 0;
    return std::max(seconds, newerThan + 1);
}

Group Load() {
    int stamp = Storage::GetInt(L"LinkGroupStamp", 0);
    if (stamp != 0) {
        return Group::Unpack(Storage::GetInt(L"LinkGroup", 0), stamp);
    }
    // Before there were three, two widgets linked as a pair, kept as Link: on or off, whether this
    // widget was on the left, and the offset.
    Group group;
    int pair = kLegacyPartner >= 0 ? Storage::GetInt(L"Link", 0) : 0;
    if (pair & 1) {
        group.order = (pair & 2) ? std::vector<int>{kSelfMember, kLegacyPartner} : std::vector<int>{kLegacyPartner, kSelfMember};
        group.offset = (pair >> 2) - 1;
        group.stamp = Storage::GetInt(L"LinkStamp", 0);
    }
    return group;
}

void Save(const Group& group) {
    Storage::SetInt(L"LinkGroup", group.Pack());
    Storage::SetInt(L"LinkGroupStamp", group.stamp);
}

// Shows this widget's copy of the group on its window, for the other widgets to read.
void Publish(HWND window, const Group& group) {
    SetPropW(window, kGroupProperty, (HANDLE)(INT_PTR)group.Pack());
    SetPropW(window, kStampProperty, (HANDLE)(INT_PTR)group.stamp);
}

void Unpublish(HWND window) {
    RemovePropW(window, kGroupProperty);
    RemovePropW(window, kStampProperty);
}

HWND FindMember(int member) {
    return FindWindowW(kMembers[member].windowClass, nullptr);
}

// Whether the widget's mod is running (this one always is).
bool Running(int member) {
    return member == kSelfMember || FindMember(member) != nullptr;
}

// The newest copy of the group among the other widgets that are running.
std::optional<Group> ReadNewest() {
    std::optional<Group> newest;
    for (int member = 0; member < kMemberCount; member++) {
        HWND window = member == kSelfMember ? nullptr : FindMember(member);
        int stamp = window ? (int)(INT_PTR)GetPropW(window, kStampProperty) : 0;
        if (stamp != 0 && (!newest || stamp > newest->stamp)) {
            newest = Group::Unpack((int)(INT_PTR)GetPropW(window, kGroupProperty), stamp);
        }
    }
    return newest;
}

// The widget, when it's on the taskbar right now.
bool TryGetBounds(int member, RECT& bounds) {
    HWND window = FindMember(member);
    return window && IsWindowVisible(window) && GetWindowRect(window, &bounds) && bounds.right > bounds.left;
}

void NotifyOthers() {
    for (int member = 0; member < kMemberCount; member++) {
        if (member == kSelfMember) {
            continue;
        }
        if (HWND window = FindMember(member)) {
            PostMessageW(window, ChangedMessage(), 0, 0);
        }
    }
}

}  // namespace WidgetLink

// ---------------------------------------------------------------------------
// The taskbar widget

// A taskbar button in the style of the Windows weather widget. Windows 11 has no API for adding
// things to the taskbar, so this is a transparent, always-on-top window kept just above the
// taskbar. It moves back above the taskbar when the taskbar is clicked, and hides when the
// taskbar auto-hides or a full-screen app is in front. To know about full-screen apps it registers
// as an app bar (without reserving any screen space), the same way the taskbar learns to step
// aside for them. Drag it to move it along the taskbar.
//
// Subclasses lay out and draw the content and handle clicks.
class TaskbarWidget {
public:
    virtual ~TaskbarWidget() {
        if (hwnd_) {
            DestroyWindow(hwnd_);
        }
        if (foregroundHook_) {
            UnhookWinEvent(foregroundHook_);
        }
        if (widgetForHook_ == this) {
            widgetForHook_ = nullptr;
        }
    }

    // Called with the widget's new offset after it's been dragged (while not linked).
    std::function<void(int offset)> onOffsetChanged;

    bool Create(int offset) {
        offset_ = offset;
        link_ = WidgetLink::Load();
        if (const wchar_t* registered = RegisterForwardingClass<TaskbarWidget>(kWidgetClassName)) {
            CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE, registered, kWidgetTitle, WS_POPUP,
                            0, 0, 1, 1, nullptr, nullptr, ModuleInstance(), this);
        }
        if (!hwnd_) {
            return false;
        }
        // Only needed when Windhawk itself runs as administrator (a portable Windhawk started that
        // way): lets the other widgets' and the shell's messages through to the widget.
        ChangeWindowMessageFilterEx(hwnd_, WidgetLink::ChangedMessage(), MSGFLT_ALLOW, nullptr);
        appBarMessage_ = RegisterWindowMessageW((std::wstring(kSelfApp) + L".AppBarNotification").c_str());
        ChangeWindowMessageFilterEx(hwnd_, appBarMessage_, MSGFLT_ALLOW, nullptr);
        taskbarCreatedMessage_ = RegisterWindowMessageW(L"TaskbarCreated");
        ChangeWindowMessageFilterEx(hwnd_, taskbarCreatedMessage_, MSGFLT_ALLOW, nullptr);
        RegisterAppBar();
        // Take the other widgets' copy of the group if it changed more recently, and let them know
        // this one is here.
        WidgetLink::Publish(hwnd_, link_);
        SyncLink();
        WidgetLink::NotifyOthers();
        return true;
    }

    void Destroy() {
        if (!hwnd_) {
            return;
        }
        SetShown(false);
        if (linkSavePending_) {
            WidgetLink::Save(link_);
        }
        APPBARDATA data{sizeof(data)};
        data.hWnd = hwnd_;
        SHAppBarMessage(ABM_REMOVE, &data);
        WidgetLink::Unpublish(hwnd_);
        DestroyWindow(hwnd_);
        WidgetLink::NotifyOthers();  // So they close the gap.
    }

    HWND Handle() const { return hwnd_; }

    bool Visible() const { return hwnd_ && IsWindowVisible(hwnd_); }

    RECT Bounds() const {
        RECT rect{};
        GetWindowRect(hwnd_, &rect);
        return rect;
    }

    // Shows or hides the widget; it only watches the taskbar while it's meant to be shown.
    void SetShown(bool shown) {
        if (shown == shown_) {
            return;
        }
        shown_ = shown;
        if (shown) {
            SetTimer(hwnd_, kPlacementTimer, 250, nullptr);
            if (!foregroundHook_) {
                widgetForHook_ = this;
                foregroundHook_ = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, nullptr,
                                                  OnForegroundChanged, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
        } else {
            KillTimer(hwnd_, kPlacementTimer);
            if (foregroundHook_) {
                UnhookWinEvent(foregroundHook_);
                foregroundHook_ = nullptr;
            }
        }
        UpdatePlacement(true);
    }

    // Re-renders after the content changed (and resizes if its width did).
    void ContentChanged() { UpdatePlacement(true); }

    const WidgetLink::Group& Link() const { return link_; }

    // Links another mod's widget with this one (joining its group if it's in one), or takes it out
    // of the group.
    void SetMemberLinked(int member, bool linked) {
        WidgetLink::Group group = link_;
        if (linked) {
            if (group.order.size() < 2) {
                // A new group starts where the leftmost of the two widgets is now, in the order
                // they're in.
                RECT bounds = Bounds();
                int left = Visible() ? bounds.left : taskbar_.left + std::max(0, offset_);
                RECT other;
                bool otherFirst = false;
                if (WidgetLink::TryGetBounds(member, other)) {
                    otherFirst = other.left < left;
                    left = std::min(left, (int)other.left);
                }
                group.order = otherFirst ? std::vector<int>{member, kSelfMember} : std::vector<int>{kSelfMember, member};
                group.offset = std::max(0, left - (int)taskbar_.left);
            } else {
                // Joining the group, at its right end.
                if (!group.Has(kSelfMember)) {
                    group.order.push_back(kSelfMember);
                }
                if (!group.Has(member)) {
                    group.order.push_back(member);
                }
            }
        } else {
            group.order.erase(std::remove(group.order.begin(), group.order.end(), member), group.order.end());
            if (group.order.size() < 2) {
                group.order.clear();
            }
        }
        ChangeLink(group);
        UpdatePlacement(true);
        WidgetLink::NotifyOthers();
    }

    // Moves this widget one place left (-1) or right (1) in its group.
    void MoveInGroup(int direction) {
        WidgetLink::Group group = link_;
        auto self = std::find(group.order.begin(), group.order.end(), kSelfMember);
        int index = (int)(self - group.order.begin());
        int target = index + direction;
        if (self == group.order.end() || target < 0 || target >= (int)group.order.size()) {
            return;
        }
        std::swap(group.order[index], group.order[target]);
        ChangeLink(group);
        UpdatePlacement(true);
        WidgetLink::NotifyOthers();
    }

    float Scale() const { return scale_; }

    int Px(float value) const { return (int)std::nearbyint(value * scale_); }  // Halves to even, like the apps' Math.Round.

    // Screen bounds of a part of the widget, from client x and width.
    RECT ScreenBoundsOf(int x, int width) const {
        RECT bounds = Bounds();
        return {bounds.left + x, bounds.top, bounds.left + x + width, bounds.bottom};
    }

    bool HandleMessage(UINT message, WPARAM wParam, LPARAM lParam, LRESULT& result) {
        if (message == WidgetLink::ChangedMessage()) {
            // Another widget moved, changed size, or changed the group.
            SyncLink();
            UpdatePlacement(false);
            return true;
        }
        if (taskbarCreatedMessage_ && message == taskbarCreatedMessage_) {
            // Explorer started (again, or after the mod at sign-in) and knows no app bars yet.
            shellReportsFullScreen_ = false;
            RegisterAppBar();
            UpdatePlacement(true);
            return true;
        }
        if (appBarMessage_ && message == appBarMessage_) {
            // Windows says a full-screen app opened or closed (lParam is nonzero while one is open).
            if (wParam == ABN_FULLSCREENAPP) {
                shellReportsFullScreen_ = lParam != 0;
                UpdatePlacement(false);
            }
            return true;
        }
        switch (message) {
            case WM_MOUSEACTIVATE:
                result = MA_NOACTIVATE;
                return true;
            case WM_TIMER:
                if (wParam == kPlacementTimer) {
                    UpdatePlacement(false);
                } else if (wParam == kLinkSaveTimer) {
                    KillTimer(hwnd_, kLinkSaveTimer);
                    linkSavePending_ = false;
                    WidgetLink::Save(link_);
                } else {
                    OnTimer(wParam);
                }
                return true;
            case WM_MOUSEMOVE: {
                POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
                if (!trackingMouse_) {
                    TRACKMOUSEEVENT track{sizeof(track), TME_LEAVE, hwnd_, 0};
                    trackingMouse_ = TrackMouseEvent(&track);
                }
                if (!pressed_) {
                    OnHover(point);
                    return true;
                }
                POINT cursor;
                GetCursorPos(&cursor);
                int distance = cursor.x - dragStartCursorX_;
                if (!dragging_ && std::abs(distance) < GetSystemMetrics(SM_CXDRAG)) {
                    return true;
                }
                if (!dragging_) {
                    dragging_ = true;
                    OnDragStart();
                }
                int dragged = std::max(0, dragStartOffset_ + distance);
                if (wasLinked_) {
                    WidgetLink::Group moved = link_;
                    moved.offset = dragged;
                    ChangeLink(moved);  // Moves the group; the other widgets follow when told.
                } else {
                    offset_ = dragged;
                }
                UpdatePlacement(true);
                return true;
            }
            case WM_MOUSELEAVE:
                trackingMouse_ = false;
                OnHoverLeave();
                return true;
            case WM_LBUTTONDOWN: {
                SetCapture(hwnd_);
                POINT cursor;
                GetCursorPos(&cursor);
                pressed_ = true;
                dragging_ = false;
                dragStartCursorX_ = cursor.x;
                dragStartOffset_ = groupLeft_ - taskbar_.left;
                return true;
            }
            case WM_LBUTTONUP: {
                bool wasPressed = pressed_;
                bool wasDragging = dragging_;
                pressed_ = false;
                dragging_ = false;
                if (GetCapture() == hwnd_) {
                    ReleaseCapture();
                }
                if (wasDragging) {
                    EndDrag();
                } else if (wasPressed) {
                    POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
                    RECT client;
                    GetClientRect(hwnd_, &client);
                    // Pressing and sliding off the widget before letting go doesn't click it.
                    if (PtInRect(&client, point)) {
                        OnClick(point, MouseButton::Left);
                    }
                }
                return true;
            }
            case WM_CAPTURECHANGED:
                // Something else took the mouse mid-press (a menu opened by a right-click): the press
                // ends there, and a drag stays where it got to.
                if ((HWND)lParam != hwnd_ && pressed_) {
                    bool wasDragging = dragging_;
                    pressed_ = false;
                    dragging_ = false;
                    if (wasDragging) {
                        EndDrag();
                    }
                }
                return false;
            case WM_MBUTTONUP:
            case WM_RBUTTONUP:
                OnClick({GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)},
                        message == WM_MBUTTONUP ? MouseButton::Middle : MouseButton::Right);
                return true;
            case WM_MOUSEWHEEL: {
                // Precision touchpads send fractions of a notch; add them up.
                wheelRemainder_ += GET_WHEEL_DELTA_WPARAM(wParam);
                int notches = wheelRemainder_ / WHEEL_DELTA;
                wheelRemainder_ -= notches * WHEEL_DELTA;
                if (notches != 0) {
                    POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
                    ScreenToClient(hwnd_, &point);
                    OnWheel(point, notches);
                }
                return true;
            }
            case WM_DISPLAYCHANGE:
                UpdatePlacement(true);
                return false;
            case WM_SETTINGCHANGE:
                RefreshShellSettings();
                UpdatePlacement(true);
                return false;
        }
        return false;
    }

    HWND hwnd_ = nullptr;

protected:
    enum class MouseButton { Left, Middle, Right };

    // The widget's content width, in pixels, for the current scale.
    virtual int LayOut() = 0;
    // Draws the content (the background, drag highlight and link divider are drawn already).
    virtual void Draw(gp::Graphics& graphics, int width, int height, bool lightTaskbar) = 0;
    // Where the widget goes when it hasn't been dragged anywhere yet.
    virtual int DefaultOffset(int barWidth, int width) = 0;
    virtual void OnScaleChanged() {}
    virtual void OnHover(POINT point) {}
    virtual void OnHoverLeave() {}
    virtual void OnClick(POINT point, MouseButton button) {}
    virtual void OnWheel(POINT point, int notches) {}
    virtual void OnDragStart() {}
    virtual void OnTimer(UINT_PTR timer) {}
    // After the widget has been placed (to move popups anchored to it), or hidden.
    virtual void OnPlaced() {}
    virtual void OnHidden() {}

    bool Dragging() const { return dragging_; }

    bool Pressed() const { return pressed_; }

    // Moves the widget onto the taskbar and above it, or hides it when it shouldn't show.
    void UpdatePlacement(bool contentChanged) {
        if (!hwnd_) {
            return;
        }
        RECT bar;
        RECT monitor;
        if (!shown_ || !TryGetVisibleTaskbar(bar, monitor) || IsFullScreenAppActive(monitor, hwnd_, shellReportsFullScreen_)) {
            if (IsWindowVisible(hwnd_)) {
                ShowWindow(hwnd_, SW_HIDE);
                if (link_.Linked()) {
                    WidgetLink::NotifyOthers();  // So they close the gap.
                }
            }
            OnHidden();
            return;
        }

        // Scale with the taskbar's display.
        UINT dpi = GetDpiForWindow(TaskbarWindow());
        if (dpi == 0) {
            dpi = DpiForPoint({(bar.left + bar.right) / 2, (bar.top + bar.bottom) / 2});
        }
        float scale = dpi / 96.0f;
        if (scale > 0 && scale != scale_) {
            scale_ = scale;
            OnScaleChanged();
            contentChanged = true;
        }
        bool light = TaskbarUsesLightTheme();
        if (light != lightTaskbar_) {
            lightTaskbar_ = light;
            contentChanged = true;
        }

        int barWidth = bar.right - bar.left;
        int width = std::min(LayOut(), barWidth);
        taskbar_ = bar;

        RECT bounds = Bounds();
        bool linked = link_.Linked();
        if (wasLinked_ && !linked && IsWindowVisible(hwnd_)) {
            // Just unlinked: stay where the group left this widget.
            offset_ = std::max(0, (int)(bounds.left - bar.left));
            if (onOffsetChanged) {
                onOffsetChanged(offset_);
            }
        }
        wasLinked_ = linked;

        int x;
        bool divider = false;
        if (linked) {
            // Side by side with the other widgets in the group that are showing, in its order,
            // wherever the group was put. Every widget works it out the same way from the same
            // numbers, so they meet without gaps.
            int before = 0;
            int total = 0;
            bool reachedSelf = false;
            for (int member : link_.order) {
                int memberWidth = 0;
                RECT other;
                if (member == kSelfMember) {
                    memberWidth = width;
                    reachedSelf = true;
                } else if (WidgetLink::TryGetBounds(member, other)) {
                    memberWidth = other.right - other.left;
                }
                if (!reachedSelf) {
                    before += memberWidth;
                }
                total += memberWidth;
            }
            total = std::min(total, barWidth);
            int groupOffset = link_.offset >= 0 ? link_.offset : DefaultOffset(barWidth, total);
            groupLeft_ = bar.left + std::max(0, std::min(groupOffset, barWidth - total));
            x = std::min(groupLeft_ + before, (int)bar.left + barWidth - width);
            divider = before > 0;
        } else {
            int preferredOffset = offset_ >= 0 ? offset_ : DefaultOffset(barWidth, width);
            x = bar.left + std::max(0, std::min(preferredOffset, barWidth - width));
            groupLeft_ = x;
        }
        RECT target{x, bar.top, x + width, bar.bottom};

        bool moved = !EqualRect(&target, &bounds) || !IsWindowVisible(hwnd_);
        if (contentChanged || moved || divider != dividerAtLeft_) {
            dividerAtLeft_ = divider;
            Render(target);
        }
        if (!IsWindowVisible(hwnd_)) {
            ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
        }
        if (moved && linked) {
            WidgetLink::NotifyOthers();  // So they move up against this widget.
        }
        OnPlaced();
        StayAboveTaskbar();
    }

    void Render() { Render(Bounds()); }

private:
    static constexpr UINT_PTR kPlacementTimer = 1;
    static constexpr UINT_PTR kLinkSaveTimer = 3;

    // Registers the widget as an app bar (without reserving any space), for Windows' notice that a
    // full-screen app opened or closed. Fails harmlessly when it's registered already, and when the
    // taskbar isn't there yet; it's done again when the taskbar is created.
    void RegisterAppBar() {
        APPBARDATA data{sizeof(data)};
        data.hWnd = hwnd_;
        data.uCallbackMessage = appBarMessage_;
        SHAppBarMessage(ABM_NEW, &data);
    }

    // A change to the group made here: it's stamped as the newest copy, shown to the other widgets,
    // and saved once it settles (a drag changes it on every mouse move).
    void ChangeLink(WidgetLink::Group group) {
        int newerThan = link_.stamp;
        if (std::optional<WidgetLink::Group> newest = WidgetLink::ReadNewest()) {
            newerThan = std::max(newerThan, newest->stamp);
        }
        group.stamp = WidgetLink::NewStamp(newerThan);
        link_ = group;
        WidgetLink::Publish(hwnd_, link_);
        SaveLinkSoon();
    }

    // Takes the other widgets' copy of the group when it changed more recently than this one's.
    void SyncLink() {
        std::optional<WidgetLink::Group> newest = WidgetLink::ReadNewest();
        if (newest && newest->stamp > link_.stamp) {
            link_ = *newest;
            WidgetLink::Publish(hwnd_, link_);
            SaveLinkSoon();
        }
    }

    void SaveLinkSoon() {
        linkSavePending_ = true;
        SetTimer(hwnd_, kLinkSaveTimer, 1000, nullptr);
    }

    // Keeps where a drag left the widget (or the group).
    void EndDrag() {
        if (wasLinked_) {
            WidgetLink::Group moved = link_;
            moved.offset = groupLeft_ - taskbar_.left;  // Where the group ended up after clamping.
            ChangeLink(moved);
            Render();
            WidgetLink::NotifyOthers();
        } else {
            offset_ = Bounds().left - taskbar_.left;  // Where it ended up after clamping.
            Render();
            if (onOffsetChanged) {
                onOffsetChanged(offset_);
            }
        }
    }

    void Render(const RECT& bounds) {
        int width = bounds.right - bounds.left;
        int height = bounds.bottom - bounds.top;
        if (width <= 0 || height <= 0) {
            return;
        }
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(info.bmiHeader);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr;
        HDC screen = GetDC(nullptr);
        HDC memory = CreateCompatibleDC(screen);
        HBITMAP bitmap = CreateDIBSection(screen, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (bitmap && bits) {
            HGDIOBJ previous = SelectObject(memory, bitmap);
            {
                gp::Bitmap canvas(width, height, width * 4, PixelFormat32bppPARGB, (BYTE*)bits);
                gp::Graphics graphics(&canvas);
                // Nearly invisible, but fully transparent pixels would let clicks fall through to the taskbar.
                graphics.Clear(Argb(1, 0, 0, 0));
                graphics.SetSmoothingMode(gp::SmoothingModeAntiAlias);
                graphics.SetTextRenderingHint(gp::TextRenderingHintAntiAliasGridFit);
                gp::Color foreground = lightTaskbar_ ? Argb(255, 28, 28, 28) : Argb(255, 255, 255, 255);
                if (dragging_) {
                    FillRoundedRect(graphics, WithAlpha(lightTaskbar_ ? 18 : 24, foreground),
                                    gp::RectF((float)Px(2), (float)Px(4), (float)(width - Px(4)), (float)(height - Px(8))), (float)Px(4));
                }
                if (dividerAtLeft_) {
                    // Linked, with another widget on the left: the divider between them.
                    gp::Pen pen(WithAlpha(lightTaskbar_ ? 50 : 60, foreground), 1);
                    graphics.DrawLine(&pen, 1.0f, (float)Px(14), 1.0f, (float)(height - Px(14)));
                }
                Draw(graphics, width, height, lightTaskbar_);
            }
            POINT position{bounds.left, bounds.top};
            SIZE size{width, height};
            POINT source{0, 0};
            BLENDFUNCTION blend{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};
            UpdateLayeredWindow(hwnd_, screen, &position, &size, memory, &source, 0, &blend, ULW_ALPHA);
            SelectObject(memory, previous);
        }
        if (bitmap) {
            DeleteObject(bitmap);
        }
        DeleteDC(memory);
        ReleaseDC(nullptr, screen);
    }

    // The taskbar comes to the front whenever it's clicked, so move back above it. Only just above
    // it, though: windows that belong over the taskbar, like a screenshot tool's overlay or an
    // always-on-top window, stay over the widget too.
    void StayAboveTaskbar() {
        HWND taskbar = TaskbarWindow();
        if (!taskbar) {
            return;
        }
        for (HWND window = GetWindow(taskbar, GW_HWNDPREV); window; window = GetWindow(window, GW_HWNDPREV)) {
            if (window == hwnd_) {
                return;  // Already above the taskbar.
            }
        }
        // Placing the widget after (below) the window just above the taskbar puts it directly over the taskbar.
        HWND justAbove = GetWindow(taskbar, GW_HWNDPREV);
        SetWindowPos(hwnd_, justAbove ? justAbove : HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // Another app came to the front, which may have put the taskbar over the widget.
    static void CALLBACK OnForegroundChanged(HWINEVENTHOOK, DWORD, HWND, LONG, LONG, DWORD, DWORD) {
        if (widgetForHook_) {
            widgetForHook_->UpdatePlacement(false);
        }
    }

    static inline TaskbarWidget* widgetForHook_ = nullptr;

    bool shown_ = false;
    int offset_ = -1;  // Pixels from the taskbar's left edge; negative means the default position.
    RECT taskbar_{};
    float scale_ = 0;
    bool lightTaskbar_ = false;
    bool shellReportsFullScreen_ = false;
    UINT appBarMessage_ = 0;
    UINT taskbarCreatedMessage_ = 0;
    HWINEVENTHOOK foregroundHook_ = nullptr;
    bool trackingMouse_ = false;
    bool pressed_ = false;
    bool dragging_ = false;
    int dragStartCursorX_ = 0;
    int dragStartOffset_ = 0;
    int wheelRemainder_ = 0;
    int groupLeft_ = 0;  // Left edge of the linked group on screen (or of this widget alone).
    WidgetLink::Group link_;
    bool linkSavePending_ = false;
    bool wasLinked_ = false;
    bool dividerAtLeft_ = false;  // Linked, with another widget on the left.
};

// The Link widgets submenu: links this widget with the other mods' widgets that are running (or
// linked already), and moves it left or right among them.
void AddLinkMenu(Menu& menu, TaskbarWidget& widget) {
    const WidgetLink::Group& group = widget.Link();
    std::vector<int> others;
    for (int member = 0; member < WidgetLink::kMemberCount; member++) {
        if (member != kSelfMember && (WidgetLink::Running(member) || (group.Linked() && group.Has(member)))) {
            others.push_back(member);
        }
    }
    if (others.empty()) {
        return;
    }
    menu.AddSeparator();
    Menu& submenu = menu.AddSubmenu(L"Link widgets");
    for (int member : others) {
        bool linked = group.Linked() && group.Has(member);
        submenu.Add(std::wstring(L"Link with ") + WidgetLink::kMembers[member].name,
                    [&widget, member, linked]() { widget.SetMemberLinked(member, !linked); }, linked);
    }
    if (group.Linked()) {
        auto self = std::find(group.order.begin(), group.order.end(), kSelfMember);
        submenu.AddSeparator();
        submenu.Add(L"Move this widget left", [&widget]() { widget.MoveInGroup(-1); }, false, self != group.order.begin());
        submenu.Add(L"Move this widget right", [&widget]() { widget.MoveInGroup(1); }, false, self + 1 != group.order.end());
    }
}

// ---------------------------------------------------------------------------
// The screenshot button on the taskbar: click it to take a screenshot (as Print Screen does),
// right-click it for its menu, and drag it to move it along the taskbar.

// A screenshot icon: the corners of a frame, with a crosshair in the middle.
void DrawScreenshotIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color) {
    float stroke = std::max(1.25f, box.Width * 0.08f);
    gp::Pen pen(color, stroke);
    pen.SetStartCap(gp::LineCapRound);
    pen.SetEndCap(gp::LineCapRound);
    pen.SetLineJoin(gp::LineJoinRound);
    gp::RectF frame = box;
    frame.Inflate(-stroke / 2, -stroke / 2);
    float corner = frame.Width * 0.3f;
    float left = frame.X;
    float top = frame.Y;
    float right = frame.GetRight();
    float bottom = frame.GetBottom();
    gp::PointF topLeft[] = {{left, top + corner}, {left, top}, {left + corner, top}};
    gp::PointF topRight[] = {{right - corner, top}, {right, top}, {right, top + corner}};
    gp::PointF bottomRight[] = {{right, bottom - corner}, {right, bottom}, {right - corner, bottom}};
    gp::PointF bottomLeft[] = {{left + corner, bottom}, {left, bottom}, {left, bottom - corner}};
    for (const gp::PointF* corners : {topLeft, topRight, bottomRight, bottomLeft}) {
        graphics.DrawLines(&pen, corners, 3);
    }
    float middleX = (left + right) / 2;
    float middleY = (top + bottom) / 2;
    float arm = frame.Width * 0.16f;
    graphics.DrawLine(&pen, middleX - arm, middleY, middleX + arm, middleY);
    graphics.DrawLine(&pen, middleX, middleY - arm, middleX, middleY + arm);
}

class SnipperWidget : public TaskbarWidget {
public:
    std::function<void()> onSnip;
    std::function<void()> onMenu;

protected:
    int DefaultOffset(int barWidth, int width) override {
        // Toward the right, short of the notification area, where the taskbar is free whether its
        // icons are centered or on the left.
        return barWidth - width - Px(400);
    }

    int LayOut() override { return Px(44); }

    void Draw(gp::Graphics& graphics, int width, int height, bool light) override {
        gp::Color foreground = light ? Argb(255, 28, 28, 28) : Argb(255, 255, 255, 255);
        if (hovered_ && !Dragging()) {
            FillRoundedRect(graphics, WithAlpha(light ? 18 : 24, foreground),
                            gp::RectF((float)Px(2), (float)Px(4), (float)(width - Px(4)), (float)(height - Px(8))), (float)Px(4));
        }
        float size = (float)Px(20);
        DrawScreenshotIcon(graphics, gp::RectF((width - size) / 2, (height - size) / 2, size, size), foreground);
    }

    void OnHover(POINT point) override {
        if (!hovered_) {
            hovered_ = true;
            Render();
        }
    }

    void OnHoverLeave() override {
        if (hovered_) {
            hovered_ = false;
            Render();
        }
    }

    void OnClick(POINT point, MouseButton button) override {
        if (button == MouseButton::Left && onSnip) {
            // Not highlighted in the screenshot.
            hovered_ = false;
            Render();
            onSnip();
        } else if (button == MouseButton::Right && onMenu) {
            onMenu();
        }
    }

private:
    bool hovered_ = false;
};

// ---------------------------------------------------------------------------
// The keyboard hook: catches Print Screen (on its own, without Alt, Ctrl, Shift or Win) before
// Windows acts on it, and tells the app. It runs on a thread of its own, so a busy UI thread never
// holds up typing. While the overlay is open but another window is in front (before the first
// click on the overlay), it also catches Esc, which would otherwise go to that window.

class KeyboardHook {
public:
    bool Start(HWND notify) {
        notify_ = notify;
        instance_ = this;
        ready_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        thread_ = CreateThread(nullptr, 0, ThreadProc, this, 0, &threadId_);
        if (!thread_) {
            return false;
        }
        WaitForSingleObject(ready_, 5000);
        return hooked_;
    }

    void Stop() {
        if (thread_) {
            PostThreadMessageW(threadId_, WM_QUIT, 0, 0);
            WaitForSingleObject(thread_, 5000);
            CloseHandle(thread_);
            thread_ = nullptr;
        }
        if (ready_) {
            CloseHandle(ready_);
            ready_ = nullptr;
        }
        instance_ = nullptr;
    }

    ~KeyboardHook() { Stop(); }

    void SetOverlayOpen(bool open) { overlayOpen_ = open; }

private:
    static DWORD WINAPI ThreadProc(void* parameter) {
        auto self = (KeyboardHook*)parameter;
        MSG message;
        PeekMessageW(&message, nullptr, WM_USER, WM_USER, PM_NOREMOVE);  // Creates the message queue.
        HHOOK hook = SetWindowsHookExW(WH_KEYBOARD_LL, HookProc, ModuleInstance(), 0);
        self->hooked_ = hook != nullptr;
        if (!hook) {
            Wh_Log(L"Couldn't install the keyboard hook: error %u", GetLastError());
        }
        SetEvent(self->ready_);
        if (hook) {
            while (GetMessageW(&message, nullptr, 0, 0) > 0) {
                DispatchMessageW(&message);
            }
            UnhookWindowsHookEx(hook);
        }
        return 0;
    }

    static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    static bool OwnWindowInFront() {
        DWORD processId = 0;
        GetWindowThreadProcessId(GetForegroundWindow(), &processId);
        return processId == GetCurrentProcessId();
    }

    static LRESULT CALLBACK HookProc(int code, WPARAM wParam, LPARAM lParam) {
        KeyboardHook* self = instance_;
        if (code == HC_ACTION && self) {
            auto key = (const KBDLLHOOKSTRUCT*)lParam;
            bool down = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            if (key->vkCode == VK_SNAPSHOT) {
                if (down) {
                    bool modified = IsDown(VK_MENU) || IsDown(VK_CONTROL) || IsDown(VK_SHIFT) || IsDown(VK_LWIN) ||
                                    IsDown(VK_RWIN);
                    if (!modified) {
                        // Held down, the key repeats; one press is one screenshot.
                        if (!self->printScreenHeld_) {
                            self->printScreenHeld_ = true;
                            PostMessageW(self->notify_, kPrintScreenMessage, 0, 0);
                        }
                        return 1;
                    }
                } else if (self->printScreenHeld_) {
                    self->printScreenHeld_ = false;
                    return 1;
                }
            } else if (key->vkCode == VK_ESCAPE && down && self->overlayOpen_ && !OwnWindowInFront()) {
                PostMessageW(self->notify_, kEscapeMessage, 0, 0);
                return 1;
            }
        }
        return CallNextHookEx(nullptr, code, wParam, lParam);
    }

    static inline KeyboardHook* instance_ = nullptr;
    HWND notify_ = nullptr;
    HANDLE thread_ = nullptr;
    HANDLE ready_ = nullptr;
    DWORD threadId_ = 0;
    bool hooked_ = false;
    bool printScreenHeld_ = false;  // Only used on the hook's thread.
    std::atomic<bool> overlayOpen_{false};
};

// ---------------------------------------------------------------------------
// The screen: a copy of every display, taken when Print Screen is pressed, and where its windows are.

class ScreenCopy {
public:
    ScreenCopy() = default;
    ScreenCopy(const ScreenCopy&) = delete;
    ScreenCopy& operator=(const ScreenCopy&) = delete;

    ~ScreenCopy() {
        if (dc_) {
            SelectObject(dc_, previous_);
            DeleteDC(dc_);
        }
        if (bitmap_) {
            DeleteObject(bitmap_);
        }
    }

    // Copies the whole desktop, across every display, in physical pixels.
    bool Take() {
        bounds_ = {GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN), 0, 0};
        bounds_.right = bounds_.left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
        bounds_.bottom = bounds_.top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (Width() <= 0 || Height() <= 0) {
            return false;
        }
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(info.bmiHeader);
        info.bmiHeader.biWidth = Width();
        info.bmiHeader.biHeight = -Height();
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        HDC screen = GetDC(nullptr);
        dc_ = CreateCompatibleDC(screen);
        bitmap_ = CreateDIBSection(screen, &info, DIB_RGB_COLORS, &bits_, nullptr, 0);
        bool ok = false;
        if (dc_ && bitmap_ && bits_) {
            previous_ = SelectObject(dc_, bitmap_);
            ok = BitBlt(dc_, 0, 0, Width(), Height(), screen, bounds_.left, bounds_.top, SRCCOPY | CAPTUREBLT);
        }
        ReleaseDC(nullptr, screen);
        return ok;
    }

    const RECT& Bounds() const { return bounds_; }
    int Width() const { return bounds_.right - bounds_.left; }
    int Height() const { return bounds_.bottom - bounds_.top; }
    HDC Dc() const { return dc_; }
    const BYTE* Bits() const { return (const BYTE*)bits_; }

private:
    RECT bounds_{};
    HDC dc_ = nullptr;
    HBITMAP bitmap_ = nullptr;
    HGDIOBJ previous_ = nullptr;
    void* bits_ = nullptr;
};

struct WindowSearch {
    DWORD ownProcessId;
    std::vector<RECT> rects;
};

BOOL CALLBACK CollectWindow(HWND window, LPARAM data) {
    auto search = (WindowSearch*)data;
    if (!IsWindowVisible(window) || IsIconic(window)) {
        return TRUE;
    }
    DWORD processId = 0;
    GetWindowThreadProcessId(window, &processId);
    if (processId == search->ownProcessId) {
        return TRUE;
    }
    // Windows hidden by the shell (other virtual desktops, suspended apps), click-through overlays,
    // fully transparent windows and the desktop itself don't count.
    BOOL cloaked = FALSE;
    if (SUCCEEDED(DwmGetWindowAttribute(window, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && cloaked) {
        return TRUE;
    }
    LONG_PTR exStyle = GetWindowLongPtrW(window, GWL_EXSTYLE);
    if (exStyle & WS_EX_TRANSPARENT) {
        return TRUE;
    }
    BYTE alpha = 255;
    DWORD flags = 0;
    if ((exStyle & WS_EX_LAYERED) && GetLayeredWindowAttributes(window, nullptr, &alpha, &flags) && (flags & LWA_ALPHA) &&
        alpha == 0) {
        return TRUE;
    }
    WCHAR className[64] = {};
    GetClassNameW(window, className, ARRAYSIZE(className));
    if (wcscmp(className, L"Progman") == 0 || wcscmp(className, L"WorkerW") == 0) {
        return TRUE;
    }
    RECT rect{};
    if (FAILED(DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, sizeof(rect)))) {
        GetWindowRect(window, &rect);
    }
    if (rect.right - rect.left > 0 && rect.bottom - rect.top > 0) {
        search->rects.push_back(rect);
    }
    return TRUE;
}

// The visible windows' outlines on screen, from the frontmost back.
std::vector<RECT> VisibleWindowRects() {
    WindowSearch search{GetCurrentProcessId(), {}};
    EnumWindows(CollectWindow, (LPARAM)&search);
    return search.rects;
}

// ---------------------------------------------------------------------------
// Marks: what the tools draw over the selection. Each kind of mark is a class that draws,
// measures, hit-tests, moves and copies itself, so the overlay handles every kind the same way,
// and a new kind needs nothing changed anywhere else.

constexpr int kMinThickness = 1;
constexpr int kMaxThickness = 20;

gp::RectF BoxRect(const gp::PointF& a, const gp::PointF& b) {
    return gp::RectF(std::min(a.X, b.X), std::min(a.Y, b.Y), std::abs(b.X - a.X), std::abs(b.Y - a.Y));
}

float DistanceToSegment(const gp::PointF& point, const gp::PointF& a, const gp::PointF& b) {
    float dx = b.X - a.X;
    float dy = b.Y - a.Y;
    float lengthSquared = dx * dx + dy * dy;
    float t = lengthSquared > 0 ? std::clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0.0f, 1.0f) : 0;
    return std::hypot(point.X - (a.X + t * dx), point.Y - (a.Y + t * dy));
}

// The distance from the point to the rectangle, or 0 inside it.
float DistanceToRect(const gp::PointF& point, const gp::RectF& rect) {
    float dx = std::max({rect.X - point.X, 0.0f, point.X - rect.GetRight()});
    float dy = std::max({rect.Y - point.Y, 0.0f, point.Y - rect.GetBottom()});
    return std::hypot(dx, dy);
}

// The point, moved to the nearest 45° direction from the start (Shift held while drawing a line).
gp::PointF AtStepAngle(const gp::PointF& start, const gp::PointF& point) {
    float dx = point.X - start.X;
    float dy = point.Y - start.Y;
    float length = std::hypot(dx, dy);
    float step = 3.14159265f / 4;
    float angle = std::round(std::atan2(dy, dx) / step) * step;
    return gp::PointF(start.X + length * std::cos(angle), start.Y + length * std::sin(angle));
}

void DrawArrow(gp::Graphics& graphics, const gp::PointF& from, const gp::PointF& to, const gp::Color& color, float width) {
    float dx = to.X - from.X;
    float dy = to.Y - from.Y;
    float length = std::hypot(dx, dy);
    if (length < 1) {
        return;
    }
    float ux = dx / length;
    float uy = dy / length;
    float head = std::min(length, std::max(width * 3.5f, 12.0f));
    gp::PointF base(to.X - ux * head, to.Y - uy * head);
    gp::Pen pen(color, width);
    pen.SetStartCap(gp::LineCapRound);
    pen.SetEndCap(gp::LineCapFlat);
    // The shaft runs a little into the head, so no gap shows between them.
    graphics.DrawLine(&pen, from, gp::PointF(base.X + ux * head * 0.3f, base.Y + uy * head * 0.3f));
    float half = head * 0.55f;
    gp::PointF points[] = {to, {base.X - uy * half, base.Y + ux * half}, {base.X + uy * half, base.Y - ux * half}};
    gp::SolidBrush brush(color);
    graphics.FillPolygon(&brush, points, 3);
}

class Mark {
public:
    virtual ~Mark() = default;

    ARGB color = 0;
    int thickness = 4;  // The toolbox's thickness, 1 to 20: pixels at 100% scaling.
    float scale = 1;    // Of the display the selection is on.

    virtual std::unique_ptr<Mark> Clone() const = 0;
    // Starts it at the point, where the mouse was pressed (or clicked, for text).
    virtual void StartAt(const gp::PointF& point) = 0;
    // While it's being drawn, the mouse is at the point. constrain (Shift held) keeps lines to 45°
    // steps and boxes square.
    virtual void DragTo(const gp::PointF& point, bool constrain) {}
    // Whether there's enough of it to keep, once it's drawn (or typed).
    virtual bool Worthwhile() const = 0;
    // Draws it; caret draws a text cursor, for text being typed.
    virtual void Draw(gp::Graphics& graphics, bool caret) const = 0;
    // The area it covers, leaving out the width of its lines.
    virtual gp::RectF Outline() const = 0;
    // The distance from the point to what it draws (0 on it, and inside text).
    virtual float DistanceTo(const gp::PointF& point) const = 0;
    virtual void MoveBy(float dx, float dy) = 0;
    // Its text, for a mark that can be typed into, or null.
    virtual std::wstring* Text() { return nullptr; }

    // The width of its lines, in pixels.
    float Width() const { return thickness * scale; }

    // Whether the point is on it, as far as clicking it goes.
    bool Hit(const gp::PointF& point) const { return DistanceTo(point) <= Width() / 2 + 5 * scale; }

    // Where it draws, with room to spare, in whole pixels: to repaint.
    RECT Bounds() const {
        gp::RectF outline = Outline();
        float margin = Width() * 4 + 8 * scale;
        return {(LONG)std::floor(outline.X - margin), (LONG)std::floor(outline.Y - margin),
                (LONG)std::ceil(outline.GetRight() + margin), (LONG)std::ceil(outline.GetBottom() + margin)};
    }
};

// A freehand line, following the mouse.
class PencilMark : public Mark {
public:
    std::unique_ptr<Mark> Clone() const override { return std::make_unique<PencilMark>(*this); }

    void StartAt(const gp::PointF& point) override { points_ = {point}; }

    void DragTo(const gp::PointF& point, bool constrain) override {
        const gp::PointF& last = points_.back();
        if (std::hypot(point.X - last.X, point.Y - last.Y) >= 1.5f * scale) {
            points_.push_back(point);
        }
    }

    bool Worthwhile() const override { return !points_.empty(); }

    void Draw(gp::Graphics& graphics, bool caret) const override {
        gp::Color ink(color);
        if (points_.size() == 1) {
            // A click makes a dot.
            gp::SolidBrush brush(ink);
            graphics.FillEllipse(&brush, points_[0].X - Width() / 2, points_[0].Y - Width() / 2, Width(), Width());
            return;
        }
        gp::Pen pen(ink, Width());
        pen.SetStartCap(gp::LineCapRound);
        pen.SetEndCap(gp::LineCapRound);
        pen.SetLineJoin(gp::LineJoinRound);
        graphics.DrawLines(&pen, points_.data(), (INT)points_.size());
    }

    gp::RectF Outline() const override {
        float left = points_[0].X, top = points_[0].Y, right = left, bottom = top;
        for (const gp::PointF& point : points_) {
            left = std::min(left, point.X);
            top = std::min(top, point.Y);
            right = std::max(right, point.X);
            bottom = std::max(bottom, point.Y);
        }
        return gp::RectF(left, top, right - left, bottom - top);
    }

    float DistanceTo(const gp::PointF& point) const override {
        float nearest = std::hypot(point.X - points_[0].X, point.Y - points_[0].Y);
        for (size_t i = 1; i < points_.size(); i++) {
            nearest = std::min(nearest, DistanceToSegment(point, points_[i - 1], points_[i]));
        }
        return nearest;
    }

    void MoveBy(float dx, float dy) override {
        for (gp::PointF& point : points_) {
            point.X += dx;
            point.Y += dy;
        }
    }

private:
    std::vector<gp::PointF> points_;
};

// A straight line from where the mouse was pressed to where it's let go: the base of lines and
// arrows.
class SegmentMark : public Mark {
public:
    void StartAt(const gp::PointF& point) override { from_ = to_ = point; }

    void DragTo(const gp::PointF& point, bool constrain) override { to_ = constrain ? AtStepAngle(from_, point) : point; }

    bool Worthwhile() const override { return std::hypot(to_.X - from_.X, to_.Y - from_.Y) >= 3 * scale; }

    gp::RectF Outline() const override { return BoxRect(from_, to_); }

    float DistanceTo(const gp::PointF& point) const override { return DistanceToSegment(point, from_, to_); }

    void MoveBy(float dx, float dy) override {
        from_.X += dx;
        from_.Y += dy;
        to_.X += dx;
        to_.Y += dy;
    }

protected:
    gp::PointF from_;
    gp::PointF to_;
};

class LineMark : public SegmentMark {
public:
    std::unique_ptr<Mark> Clone() const override { return std::make_unique<LineMark>(*this); }

    void Draw(gp::Graphics& graphics, bool caret) const override {
        gp::Pen pen(gp::Color(color), Width());
        pen.SetStartCap(gp::LineCapRound);
        pen.SetEndCap(gp::LineCapRound);
        graphics.DrawLine(&pen, from_, to_);
    }
};

class ArrowMark : public SegmentMark {
public:
    std::unique_ptr<Mark> Clone() const override { return std::make_unique<ArrowMark>(*this); }

    void Draw(gp::Graphics& graphics, bool caret) const override { DrawArrow(graphics, from_, to_, gp::Color(color), Width()); }
};

class BoxMark : public Mark {
public:
    std::unique_ptr<Mark> Clone() const override { return std::make_unique<BoxMark>(*this); }

    void StartAt(const gp::PointF& point) override { from_ = to_ = point; }

    void DragTo(const gp::PointF& point, bool constrain) override {
        to_ = point;
        if (constrain) {
            float side = std::max(std::abs(point.X - from_.X), std::abs(point.Y - from_.Y));
            to_ = gp::PointF(from_.X + (point.X < from_.X ? -side : side), from_.Y + (point.Y < from_.Y ? -side : side));
        }
    }

    bool Worthwhile() const override { return std::hypot(to_.X - from_.X, to_.Y - from_.Y) >= 3 * scale; }

    void Draw(gp::Graphics& graphics, bool caret) const override {
        gp::Pen pen(gp::Color(color), Width());
        pen.SetLineJoin(gp::LineJoinRound);
        graphics.DrawRectangle(&pen, Outline());
    }

    gp::RectF Outline() const override { return BoxRect(from_, to_); }

    // On its edges only, so arrows and text can go inside a box.
    float DistanceTo(const gp::PointF& point) const override {
        gp::RectF rect = Outline();
        gp::PointF corners[] = {{rect.X, rect.Y}, {rect.GetRight(), rect.Y}, {rect.GetRight(), rect.GetBottom()}, {rect.X, rect.GetBottom()}};
        float nearest = DistanceToSegment(point, corners[3], corners[0]);
        for (int i = 1; i < 4; i++) {
            nearest = std::min(nearest, DistanceToSegment(point, corners[i - 1], corners[i]));
        }
        return nearest;
    }

    void MoveBy(float dx, float dy) override {
        from_.X += dx;
        from_.Y += dy;
        to_.X += dx;
        to_.Y += dy;
    }

private:
    gp::PointF from_;
    gp::PointF to_;
};

class TextMark : public Mark {
public:
    std::unique_ptr<Mark> Clone() const override { return std::make_unique<TextMark>(*this); }

    // The click is where the middle of the first line goes.
    void StartAt(const gp::PointF& point) override { topLeft_ = gp::PointF(point.X, point.Y - FontSize() * 0.7f); }

    bool Worthwhile() const override { return !Trim(text_).empty(); }

    void Draw(gp::Graphics& graphics, bool caret) const override {
        gp::Font font(L"Segoe UI Semibold", FontSize(), gp::FontStyleRegular, gp::UnitPixel);
        gp::StringFormat format(gp::StringFormat::GenericTypographic());
        format.SetFormatFlags(format.GetFormatFlags() | gp::StringFormatFlagsMeasureTrailingSpaces);
        gp::SolidBrush brush{gp::Color(color)};
        float lineHeight = font.GetHeight(&graphics);
        std::vector<std::wstring> lines = Lines();
        for (size_t i = 0; i < lines.size(); i++) {
            graphics.DrawString(lines[i].c_str(), (INT)lines[i].size(), &font, gp::PointF(topLeft_.X, topLeft_.Y + lineHeight * i),
                                &format, &brush);
        }
        if (caret) {
            gp::RectF last;
            graphics.MeasureString(lines.back().c_str(), (INT)lines.back().size(), &font, gp::PointF(0, 0), &format, &last);
            float x = topLeft_.X + last.Width + FontSize() * 0.05f;
            float y = topLeft_.Y + lineHeight * (lines.size() - 1);
            gp::Pen pen(gp::Color(color), std::max(1.5f, FontSize() / 14));
            graphics.DrawLine(&pen, x, y + lineHeight * 0.12f, x, y + lineHeight * 0.92f);
        }
    }

    gp::RectF Outline() const override {
        gp::Bitmap measuring(1, 1, PixelFormat32bppARGB);
        gp::Graphics graphics(&measuring);
        gp::Font font(L"Segoe UI Semibold", FontSize(), gp::FontStyleRegular, gp::UnitPixel);
        gp::StringFormat format(gp::StringFormat::GenericTypographic());
        format.SetFormatFlags(format.GetFormatFlags() | gp::StringFormatFlagsMeasureTrailingSpaces);
        std::vector<std::wstring> lines = Lines();
        float width = FontSize() * 0.3f;  // Room for the caret.
        for (const std::wstring& line : lines) {
            gp::RectF bounds;
            graphics.MeasureString(line.c_str(), (INT)line.size(), &font, gp::PointF(0, 0), &format, &bounds);
            width = std::max(width, bounds.Width + FontSize() * 0.3f);
        }
        return gp::RectF(topLeft_.X, topLeft_.Y, width, font.GetHeight(&graphics) * lines.size());
    }

    float DistanceTo(const gp::PointF& point) const override { return DistanceToRect(point, Outline()); }

    void MoveBy(float dx, float dy) override {
        topLeft_.X += dx;
        topLeft_.Y += dy;
    }

    std::wstring* Text() override { return &text_; }

private:
    // Text gets bigger with the thickness.
    float FontSize() const { return (12.0f + 2.0f * thickness) * scale; }

    std::vector<std::wstring> Lines() const {
        std::vector<std::wstring> lines(1);
        for (wchar_t c : text_) {
            if (c == L'\n') {
                lines.emplace_back();
            } else {
                lines.back() += c;
            }
        }
        return lines;
    }

    gp::PointF topLeft_;
    std::wstring text_;
};

// ---------------------------------------------------------------------------
// The tools. Each is one entry in kTools: its name and shortcut, its cursor, its toolbox icon and
// the mark it makes. The toolbox, its tooltips, the shortcuts and the overlay all go by this list,
// so adding a tool is adding an entry (and its mark).

struct Tool {
    const wchar_t* name;  // For its tooltip.
    wchar_t key;          // Its shortcut, a capital letter; also how the last tool used is remembered.
    LPCWSTR cursor;       // Over the selection.
    // Draws its icon in the toolbox.
    void (*drawIcon)(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale);
    // Makes the mark it draws, or null for a tool that draws none (selecting and moving).
    std::unique_ptr<Mark> (*makeMark)();
    // A click places the mark and typing fills it (text), rather than a drag drawing it.
    bool typed;
};

// A mouse pointer.
void DrawSelectIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale) {
    auto at = [&](float x, float y) { return gp::PointF(box.X + box.Width * x, box.Y + box.Height * y); };
    gp::PointF outline[] = {at(0.26f, 0.06f), at(0.26f, 0.84f), at(0.44f, 0.67f), at(0.57f, 0.95f),
                            at(0.69f, 0.89f), at(0.56f, 0.62f), at(0.80f, 0.62f)};
    gp::Pen pen(color, 1.5f * scale);
    pen.SetLineJoin(gp::LineJoinRound);
    graphics.DrawPolygon(&pen, outline, ARRAYSIZE(outline));
}

void DrawPencilIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale) {
    DrawGlyph(graphics, kGlyphPencil, 16 * scale, box, color);
}

void DrawLineIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale) {
    gp::Pen pen(color, 1.6f * scale);
    pen.SetStartCap(gp::LineCapRound);
    pen.SetEndCap(gp::LineCapRound);
    graphics.DrawLine(&pen, box.X + 2 * scale, box.GetBottom() - 2 * scale, box.GetRight() - 2 * scale, box.Y + 2 * scale);
}

void DrawArrowIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale) {
    DrawArrow(graphics, gp::PointF(box.X + 1 * scale, box.GetBottom() - 1 * scale), gp::PointF(box.GetRight() - 1 * scale, box.Y + 1 * scale),
              color, 1.6f * scale);
}

void DrawBoxIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale) {
    gp::Pen pen(color, 1.6f * scale);
    gp::RectF square = box;
    square.Inflate(-2 * scale, -3 * scale);
    graphics.DrawRectangle(&pen, square);
}

void DrawTextIcon(gp::Graphics& graphics, const gp::RectF& box, const gp::Color& color, float scale) {
    DrawGlyph(graphics, kGlyphText, 16 * scale, box, color);
}

template <typename T>
std::unique_ptr<Mark> MakeMark() {
    return std::make_unique<T>();
}

const Tool kTools[] = {
    {L"Select and move", L'V', IDC_ARROW, DrawSelectIcon, nullptr, false},
    {L"Pencil", L'P', IDC_CROSS, DrawPencilIcon, MakeMark<PencilMark>, false},
    {L"Line", L'L', IDC_CROSS, DrawLineIcon, MakeMark<LineMark>, false},
    {L"Arrow", L'A', IDC_CROSS, DrawArrowIcon, MakeMark<ArrowMark>, false},
    {L"Box", L'B', IDC_CROSS, DrawBoxIcon, MakeMark<BoxMark>, false},
    {L"Text", L'T', IDC_IBEAM, DrawTextIcon, MakeMark<TextMark>, true},
};
constexpr int kToolCount = ARRAYSIZE(kTools);
constexpr wchar_t kDefaultToolKey = L'A';

// The tool with the shortcut, or -1.
int ToolForKey(wchar_t key) {
    for (int i = 0; i < kToolCount; i++) {
        if (kTools[i].key == key) {
            return i;
        }
    }
    return -1;
}

// ---------------------------------------------------------------------------
// The finished screenshot: the selected part of the screen with its marks.

std::unique_ptr<gp::Bitmap> ComposeScreenshot(const ScreenCopy& screen, const RECT& selection,
                                              const std::vector<std::unique_ptr<Mark>>& marks) {
    int width = selection.right - selection.left;
    int height = selection.bottom - selection.top;
    auto image = std::make_unique<gp::Bitmap>(width, height, PixelFormat32bppARGB);
    gp::Rect all(0, 0, width, height);
    gp::BitmapData data;
    if (image->LockBits(&all, gp::ImageLockModeWrite, PixelFormat32bppARGB, &data) != gp::Ok) {
        return nullptr;
    }
    // The screen copy's pixels, made opaque (the screen's alpha bits mean nothing).
    for (int y = 0; y < height; y++) {
        auto source = (const DWORD*)(screen.Bits() + ((size_t)(selection.top + y) * screen.Width() + selection.left) * 4);
        auto target = (DWORD*)((BYTE*)data.Scan0 + (size_t)y * data.Stride);
        for (int x = 0; x < width; x++) {
            target[x] = source[x] | 0xFF000000;
        }
    }
    image->UnlockBits(&data);
    gp::Graphics graphics(image.get());
    graphics.SetSmoothingMode(gp::SmoothingModeAntiAlias);
    graphics.SetTextRenderingHint(gp::TextRenderingHintAntiAliasGridFit);
    graphics.TranslateTransform((float)-selection.left, (float)-selection.top);
    for (const std::unique_ptr<Mark>& mark : marks) {
        mark->Draw(graphics, false);
    }
    return image;
}

bool PngEncoder(CLSID& clsid) {
    UINT count = 0;
    UINT size = 0;
    if (gp::GetImageEncodersSize(&count, &size) != gp::Ok || size == 0) {
        return false;
    }
    std::vector<BYTE> buffer(size);
    auto encoders = (gp::ImageCodecInfo*)buffer.data();
    if (gp::GetImageEncoders(count, size, encoders) != gp::Ok) {
        return false;
    }
    for (UINT i = 0; i < count; i++) {
        if (wcscmp(encoders[i].MimeType, L"image/png") == 0) {
            clsid = encoders[i].Clsid;
            return true;
        }
    }
    return false;
}

std::vector<BYTE> EncodePng(gp::Bitmap& image) {
    std::vector<BYTE> bytes;
    CLSID png;
    IStream* stream = nullptr;
    if (!PngEncoder(png) || FAILED(CreateStreamOnHGlobal(nullptr, TRUE, &stream))) {
        return bytes;
    }
    STATSTG stat{};
    HGLOBAL memory = nullptr;
    if (image.Save(stream, &png, nullptr) == gp::Ok && SUCCEEDED(stream->Stat(&stat, STATFLAG_NONAME)) &&
        SUCCEEDED(GetHGlobalFromStream(stream, &memory))) {
        if (const void* data = GlobalLock(memory)) {
            bytes.assign((const BYTE*)data, (const BYTE*)data + stat.cbSize.QuadPart);
            GlobalUnlock(memory);
        }
    }
    stream->Release();
    return bytes;
}

HGLOBAL GlobalCopy(const void* data, size_t size) {
    HGLOBAL memory = GlobalAlloc(GMEM_MOVEABLE, size);
    if (memory) {
        memcpy(GlobalLock(memory), data, size);
        GlobalUnlock(memory);
    }
    return memory;
}

// Puts the screenshot on the clipboard, as a bitmap and as PNG (which many apps prefer).
bool CopyToClipboard(HWND owner, gp::Bitmap& image) {
    UINT width = image.GetWidth();
    UINT height = image.GetHeight();
    gp::Rect all(0, 0, (INT)width, (INT)height);
    gp::BitmapData data;
    if (image.LockBits(&all, gp::ImageLockModeRead, PixelFormat32bppARGB, &data) != gp::Ok) {
        return false;
    }
    // A DIB is stored bottom row first.
    std::vector<BYTE> dib(sizeof(BITMAPINFOHEADER) + (size_t)width * height * 4);
    auto header = (BITMAPINFOHEADER*)dib.data();
    header->biSize = sizeof(BITMAPINFOHEADER);
    header->biWidth = (LONG)width;
    header->biHeight = (LONG)height;
    header->biPlanes = 1;
    header->biBitCount = 32;
    header->biCompression = BI_RGB;
    header->biSizeImage = width * height * 4;
    for (UINT y = 0; y < height; y++) {
        memcpy(dib.data() + sizeof(BITMAPINFOHEADER) + (size_t)(height - 1 - y) * width * 4,
               (const BYTE*)data.Scan0 + (size_t)y * data.Stride, (size_t)width * 4);
    }
    image.UnlockBits(&data);
    std::vector<BYTE> png = EncodePng(image);

    // Another app may have the clipboard open for a moment.
    bool opened = false;
    for (int attempt = 0; attempt < 10 && !(opened = OpenClipboard(owner)); attempt++) {
        Sleep(20);
    }
    if (!opened) {
        return false;
    }
    EmptyClipboard();
    bool ok = false;
    if (HGLOBAL memory = GlobalCopy(dib.data(), dib.size())) {
        ok = SetClipboardData(CF_DIB, memory) != nullptr;
        if (!ok) {
            GlobalFree(memory);
        }
    }
    if (!png.empty()) {
        if (HGLOBAL memory = GlobalCopy(png.data(), png.size())) {
            if (!SetClipboardData(RegisterClipboardFormatW(L"PNG"), memory)) {
                GlobalFree(memory);
            }
        }
    }
    CloseClipboard();
    return ok;
}

// Saves the screenshot in the folder as "Screenshot <date> <time>.png". Returns its path, or
// nothing if it couldn't be saved.
std::optional<std::wstring> SaveScreenshot(gp::Bitmap& image, const std::wstring& folder) {
    CLSID png;
    if (folder.empty() || !PngEncoder(png)) {
        return std::nullopt;
    }
    SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr);  // Fails harmlessly when it's there already.
    SYSTEMTIME now;
    GetLocalTime(&now);
    WCHAR name[64];
    swprintf_s(name, L"Screenshot %04d-%02d-%02d %02d%02d%02d", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute,
               now.wSecond);
    std::wstring path = folder + L"\\" + name + L".png";
    for (int copy = 2; FileExists(path) && copy < 1000; copy++) {
        path = folder + L"\\" + name + L" (" + std::to_wstring(copy) + L").png";
    }
    if (image.Save(path.c_str(), &png, nullptr) != gp::Ok) {
        return std::nullopt;
    }
    return path;
}

// Shows the file in File Explorer, selected.
void ShowInExplorer(const std::wstring& path) {
    PIDLIST_ABSOLUTE item = ILCreateFromPathW(path.c_str());
    if (item) {
        SHOpenFolderAndSelectItems(item, 0, nullptr, 0);
        ILFree(item);
    }
}

// ---------------------------------------------------------------------------
// The note in the corner of the screen after a screenshot: a thumbnail, what happened, and where
// it was saved. Clicking it shows a saved screenshot in File Explorer.

class Notice {
public:
    ~Notice() {
        if (hwnd_) {
            DestroyWindow(hwnd_);
        }
    }

    bool Create() {
        if (const wchar_t* registered = RegisterForwardingClass<Notice>(L"TaskbarSnipper.Mod.Notice", IDC_HAND)) {
            CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, registered, L"", WS_POPUP, 0, 0, 1, 1, nullptr,
                            nullptr, ModuleInstance(), this);
        }
        if (!hwnd_) {
            return false;
        }
        DWORD corners = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd_, DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(corners));
        return true;
    }

    // Shows the note on the display of the screen rectangle, with a thumbnail of the image if given.
    void Show(const std::wstring& title, const std::wstring& detail, const std::wstring& savedPath, gp::Bitmap* image,
              const RECT& nearRect) {
        title_ = title;
        detail_ = detail;
        savedPath_ = savedPath;
        thumbnail_.reset();
        scale_ = ScaleForScreenRect(nearRect);
        if (image && image->GetWidth() > 0 && image->GetHeight() > 0) {
            float fit = std::min(Px(88) / (float)image->GetWidth(), Px(56) / (float)image->GetHeight());
            int width = std::max(1, (int)(image->GetWidth() * std::min(fit, 1.0f)));
            int height = std::max(1, (int)(image->GetHeight() * std::min(fit, 1.0f)));
            thumbnail_ = std::make_unique<gp::Bitmap>(width, height, PixelFormat32bppARGB);
            gp::Graphics graphics(thumbnail_.get());
            graphics.SetInterpolationMode(gp::InterpolationModeHighQualityBicubic);
            graphics.DrawImage(image, 0, 0, width, height);
        }
        RECT work = WorkAreaForScreenRect(nearRect);
        int width = Px(380);
        int height = Px(80);
        SetWindowPos(hwnd_, HWND_TOPMOST, work.right - width - Px(12), work.bottom - height - Px(12), width, height,
                     SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(hwnd_, nullptr, FALSE);
        SetTimer(hwnd_, 1, 5000, nullptr);
    }

    bool HandleMessage(UINT message, WPARAM wParam, LPARAM lParam, LRESULT& result) {
        switch (message) {
            case WM_MOUSEACTIVATE:
                result = MA_NOACTIVATE;
                return true;
            case WM_ERASEBKGND:
                result = 1;
                return true;
            case WM_TIMER:
                KillTimer(hwnd_, 1);
                ShowWindow(hwnd_, SW_HIDE);
                return true;
            case WM_LBUTTONUP:
                KillTimer(hwnd_, 1);
                ShowWindow(hwnd_, SW_HIDE);
                if (!savedPath_.empty()) {
                    ShowInExplorer(savedPath_);
                }
                return true;
            case WM_PAINT:
                Paint();
                return true;
        }
        return false;
    }

    HWND hwnd_ = nullptr;

private:
    int Px(float value) const { return (int)std::lround(value * scale_); }

    void Paint() {
        PAINTSTRUCT paint;
        HDC dc = BeginPaint(hwnd_, &paint);
        RECT client;
        GetClientRect(hwnd_, &client);
        HDC memory = CreateCompatibleDC(dc);
        HBITMAP bitmap = CreateCompatibleBitmap(dc, client.right, client.bottom);
        HGDIOBJ previous = SelectObject(memory, bitmap);
        {
            Palette colors = Palette::Current();
            gp::Graphics graphics(memory);
            graphics.SetSmoothingMode(gp::SmoothingModeAntiAlias);
            graphics.SetTextRenderingHint(gp::TextRenderingHintClearTypeGridFit);
            graphics.Clear(WithAlpha(255, colors.background));
            float textLeft = (float)Px(16);
            if (thumbnail_) {
                gp::RectF box((float)Px(12), (float)Px(12), (float)Px(88), (float)Px(56));
                float x = box.X + (box.Width - thumbnail_->GetWidth()) / 2;
                float y = box.Y + (box.Height - thumbnail_->GetHeight()) / 2;
                graphics.DrawImage(thumbnail_.get(), x, y);
                gp::Pen pen(colors.border, 1);
                graphics.DrawRectangle(&pen, x - 0.5f, y - 0.5f, (float)thumbnail_->GetWidth(), (float)thumbnail_->GetHeight());
                textLeft = box.GetRight() + Px(14);
            }
            gp::Font titleFont(L"Segoe UI Semibold", 14.0f * scale_, gp::FontStyleRegular, gp::UnitPixel);
            gp::Font detailFont(L"Segoe UI", 12.0f * scale_, gp::FontStyleRegular, gp::UnitPixel);
            float textWidth = client.right - textLeft - Px(14);
            DrawTextLine(graphics, title_, titleFont, colors.foreground, gp::RectF(textLeft, (float)Px(16), textWidth, (float)Px(22)));
            DrawTextLine(graphics, detail_, detailFont, colors.secondary, gp::RectF(textLeft, (float)Px(40), textWidth, (float)Px(22)));
            gp::Pen border(colors.border, 1);
            graphics.DrawRectangle(&border, 0.5f, 0.5f, client.right - 1.0f, client.bottom - 1.0f);
        }
        BitBlt(dc, 0, 0, client.right, client.bottom, memory, 0, 0, SRCCOPY);
        SelectObject(memory, previous);
        DeleteObject(bitmap);
        DeleteDC(memory);
        EndPaint(hwnd_, &paint);
    }

    std::wstring title_;
    std::wstring detail_;
    std::wstring savedPath_;
    std::unique_ptr<gp::Bitmap> thumbnail_;
    float scale_ = 1;
};

// ---------------------------------------------------------------------------
// The overlay: the frozen screen over every display, dimmed except for the selection, with the
// toolbox above the selection once it's made.

// What finishing does with the screenshot.
enum class Finish { Default, Copy, Save };

// The tool, color and thickness, remembered between screenshots.
struct Pen {
    int tool = ToolForKey(kDefaultToolKey);  // In kTools.
    ARGB color = 0xFFE81123;
    int thickness = 4;  // 1 to 20, in pixels at 100% scaling.
};

const ARGB kSwatches[] = {0xFFE81123, 0xFFFF8C00, 0xFFFFD60A, 0xFF16C60C, 0xFF0078D4, 0xFF8E44EC, 0xFFFFFFFF, 0xFF000000};
const wchar_t* const kSwatchNames[] = {L"Red", L"Orange", L"Yellow", L"Green", L"Blue", L"Purple", L"White", L"Black"};
constexpr int kSwatchCount = ARRAYSIZE(kSwatches);

class Overlay {
public:
    // Called when the overlay is done: with what to do with the screenshot, or nothing when it was
    // cancelled. The overlay is hidden by then and can be deleted after the call returns.
    std::function<void(std::optional<Finish> finish)> onDone;
    // Called when the tool, color or thickness changes.
    std::function<void(const Pen& pen)> onPenChanged;

    Overlay(std::unique_ptr<ScreenCopy> screen, std::vector<RECT> windows, const Pen& pen)
        : screen_(std::move(screen)), pen_(pen) {
        if (pen_.tool < 0 || pen_.tool >= kToolCount) {
            pen_.tool = ToolForKey(kDefaultToolKey);
        }
        // The windows' outlines, in the overlay's coordinates (the top-left of the desktop is 0, 0).
        for (const RECT& window : windows) {
            windows_.push_back(ToClient(window));
        }
    }

    ~Overlay() {
        if (hwnd_) {
            DestroyWindow(hwnd_);
        }
        if (black_) {
            SelectObject(blackDc_, blackPrevious_);
            DeleteObject(black_);
            DeleteDC(blackDc_);
        }
    }

    bool Create() {
        const RECT& bounds = screen_->Bounds();
        if (const wchar_t* registered = RegisterForwardingClass<Overlay>(L"TaskbarSnipper.Mod.Overlay", IDC_CROSS)) {
            CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, registered, L"Taskbar Snipper", WS_POPUP, bounds.left, bounds.top,
                            screen_->Width(), screen_->Height(), nullptr, nullptr, ModuleInstance(), this);
        }
        if (!hwnd_) {
            return false;
        }
        // No fade: the screen should look frozen the moment Print Screen is pressed.
        BOOL disable = TRUE;
        DwmSetWindowAttribute(hwnd_, DWMWA_TRANSITIONS_FORCEDISABLED, &disable, sizeof(disable));
        // A 1×1 black bitmap, stretched with transparency to dim the screen outside the selection.
        HDC screen = GetDC(nullptr);
        blackDc_ = CreateCompatibleDC(screen);
        black_ = CreateCompatibleBitmap(screen, 1, 1);
        ReleaseDC(nullptr, screen);
        blackPrevious_ = SelectObject(blackDc_, black_);
        SetPixel(blackDc_, 0, 0, RGB(0, 0, 0));
        lightTheme_ = AppsUseLightTheme();
        POINT cursor;
        GetCursorPos(&cursor);
        UpdateHover(ToClient(cursor));
        ShowWindow(hwnd_, SW_SHOW);
        SetForegroundWindow(hwnd_);  // Windows may not allow it; the first click brings it to the front anyway.
        return true;
    }

    // Esc: finishes the text being typed, stops a drawing or a move, or deselects the selected
    // mark, or else cancels.
    void Escape() {
        if (typing_) {
            FinishTyping();
        } else if (drawing_) {
            CancelDrawing();
        } else if (moving_ >= 0) {
            EndMove();
        } else if (selected_ >= 0) {
            SelectMark(-1);
        } else {
            Done(std::nullopt);
        }
    }

    // The selection and marks, for making the screenshot once the overlay is done.
    const ScreenCopy& Screen() const { return *screen_; }
    RECT SelectionOnScreen() const { return ToScreen(selection_); }
    RECT SelectionInOverlay() const { return selection_; }
    const std::vector<std::unique_ptr<Mark>>& Marks() const { return marks_; }

    bool HandleMessage(UINT message, WPARAM wParam, LPARAM lParam, LRESULT& result) {
        switch (message) {
            case WM_ERASEBKGND:
                result = 1;
                return true;
            case WM_PAINT: {
                PAINTSTRUCT paint;
                HDC dc = BeginPaint(hwnd_, &paint);
                if (!IsRectEmpty(&paint.rcPaint)) {
                    Paint(dc, paint.rcPaint);
                }
                EndPaint(hwnd_, &paint);
                return true;
            }
            case WM_SETCURSOR:
                if (LOWORD(lParam) == HTCLIENT) {
                    POINT cursor;
                    GetCursorPos(&cursor);
                    SetCursor(LoadCursorW(nullptr, CursorAt(ToClient(cursor))));
                    result = TRUE;
                    return true;
                }
                return false;
            case WM_MOUSEMOVE:
                OnMouseMove({GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)});
                return true;
            case WM_LBUTTONDOWN:
                SetCapture(hwnd_);
                OnMouseDown({GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)});
                return true;
            case WM_LBUTTONUP:
                OnMouseUp({GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)});
                return true;
            case WM_RBUTTONUP:
                // Cancels while selecting; once there are marks to lose, only Esc or Close does.
                if (mode_ == Mode::Selecting) {
                    Done(std::nullopt);
                }
                return true;
            case WM_MOUSEWHEEL:
                if (mode_ == Mode::Editing) {
                    wheelRemainder_ += GET_WHEEL_DELTA_WPARAM(wParam);
                    int notches = wheelRemainder_ / WHEEL_DELTA;
                    wheelRemainder_ -= notches * WHEEL_DELTA;
                    if (notches != 0) {
                        SetThickness(pen_.thickness + notches);
                    }
                }
                return true;
            case WM_KEYDOWN:
                OnKeyDown((UINT)wParam);
                return true;
            case WM_CHAR:
                OnChar((wchar_t)wParam);
                return true;
            case WM_TIMER:
                if (wParam == kCaretTimer && typing_) {
                    caretOn_ = !caretOn_;
                    InvalidateClient(marks_[selected_]->Bounds());
                }
                return true;
            case WM_DISPLAYCHANGE:
                // The displays changed under the frozen picture of them.
                Done(std::nullopt);
                return true;
        }
        return false;
    }

    HWND hwnd_ = nullptr;

private:
    enum class Mode { Selecting, Editing };

    enum class Item {
        None,
        Tool,    // index says which, in kTools.
        Swatch,  // index says which.
        CustomColor,
        Thinner,
        Thickness,
        Thicker,
        Undo,
        Copy,
        Save,
        Close,
    };

    struct ToolboxItem {
        Item item;
        int index;  // For tools and swatches.
        RECT rect;  // In the overlay's coordinates.
    };

    // A run of the same change to the same mark (turning the mouse wheel, pressing an arrow key
    // again and again) is undone in one step.
    enum class Change { Other, Restyle, Nudge };

    using MarkList = std::vector<std::unique_ptr<Mark>>;

    static constexpr UINT_PTR kCaretTimer = 1;

    // Coordinates: the overlay covers the whole desktop, with its own 0, 0 at the desktop's top-left.
    POINT ToClient(POINT point) const { return {point.x - screen_->Bounds().left, point.y - screen_->Bounds().top}; }
    RECT ToClient(const RECT& rect) const { return OffsetRectBy(rect, -screen_->Bounds().left, -screen_->Bounds().top); }
    RECT ToScreen(const RECT& rect) const { return OffsetRectBy(rect, screen_->Bounds().left, screen_->Bounds().top); }

    float Scale() const { return scale_; }
    int Px(float value) const { return (int)std::lround(value * scale_); }

    void InvalidateClient(const RECT& rect) { InvalidateRect(hwnd_, &rect, FALSE); }

    void ReleaseMouse() {
        if (GetCapture() == hwnd_) {
            ReleaseCapture();
        }
    }

    // The part of the screen shown bright: the selection, or while choosing, the window under the
    // mouse or the area being dragged.
    RECT Highlight() const {
        if (mode_ == Mode::Editing || dragging_) {
            return selection_;
        }
        return hover_;
    }

    void SetHighlightChanged(const RECT& before) {
        RECT after = Highlight();
        if (!EqualRect(&before, &after)) {
            // The glow, size label and corner dimming change around both, more so on a scaled display.
            float scale = std::max(ScaleForScreenRect(ToScreen(before)), ScaleForScreenRect(ToScreen(after)));
            int margin = (int)std::ceil(60 * scale);
            InvalidateClient(InflatedRect(before, margin));
            InvalidateClient(InflatedRect(after, margin));
        }
    }

    // The window (or else the display) under the point, for choosing by clicking.
    void UpdateHover(POINT point) {
        RECT before = Highlight();
        hover_ = {};
        for (const RECT& window : windows_) {
            if (PtInRect(&window, point)) {
                hover_ = window;
                break;
            }
        }
        if (IsRectEmpty(&hover_)) {
            POINT onScreen{point.x + screen_->Bounds().left, point.y + screen_->Bounds().top};
            MONITORINFO info{sizeof(info)};
            if (GetMonitorInfoW(MonitorFromPoint(onScreen, MONITOR_DEFAULTTONEAREST), &info)) {
                hover_ = ToClient(info.rcMonitor);
            }
        }
        RECT all{0, 0, screen_->Width(), screen_->Height()};
        IntersectRect(&hover_, &hover_, &all);
        // The hint follows the mouse from display to display.
        RECT hint = HintRect(point);
        if (!EqualRect(&hint, &hintRect_)) {
            InvalidateClient(hintRect_);
            hintRect_ = hint;
            InvalidateClient(hintRect_);
        }
        SetHighlightChanged(before);
    }

    // Where the "drag to select" hint goes: the top middle of the display the point is on.
    RECT HintRect(POINT point) const {
        POINT onScreen{point.x + screen_->Bounds().left, point.y + screen_->Bounds().top};
        MONITORINFO info{sizeof(info)};
        GetMonitorInfoW(MonitorFromPoint(onScreen, MONITOR_DEFAULTTONEAREST), &info);
        RECT monitor = ToClient(info.rcMonitor);
        float scale = ScaleForScreenRect(info.rcMonitor);
        int width = (int)(470 * scale);
        int height = (int)(40 * scale);
        int left = (monitor.left + monitor.right - width) / 2;
        int top = monitor.top + (int)(24 * scale);
        return {left, top, left + width, top + height};
    }

    // The point, kept inside the selection: marks are drawn there.
    gp::PointF InSelection(POINT point) const {
        return gp::PointF((float)std::clamp(point.x, selection_.left, selection_.right - 1),
                          (float)std::clamp(point.y, selection_.top, selection_.bottom - 1));
    }

    // The topmost mark under the point, or -1.
    int MarkAt(POINT point) const {
        if (mode_ != Mode::Editing || !PtInRect(&selection_, point)) {
            return -1;
        }
        gp::PointF at((float)point.x, (float)point.y);
        for (int i = (int)marks_.size() - 1; i >= 0; i--) {
            if (marks_[i]->Hit(at)) {
                return i;
            }
        }
        return -1;
    }

    void OnMouseMove(POINT point) {
        if (mode_ == Mode::Selecting) {
            if (!dragging_) {
                UpdateHover(point);
                return;
            }
            RECT before = Highlight();
            current_ = point;
            selection_ = {std::min(anchor_.x, point.x), std::min(anchor_.y, point.y), std::max(anchor_.x, point.x) + 1,
                          std::max(anchor_.y, point.y) + 1};
            SetHighlightChanged(before);
            return;
        }
        if (drawing_) {
            RECT before = drawing_->Bounds();
            drawing_->DragTo(InSelection(point), (GetKeyState(VK_SHIFT) & 0x8000) != 0);
            InvalidateClient(before);
            InvalidateClient(drawing_->Bounds());
            return;
        }
        if (moving_ >= 0) {
            MoveTo(point);
            return;
        }
        ToolboxItem item = ItemAt(point);
        SetHoverItem(item);
        SetHoverMark(item.item == Item::None ? MarkAt(point) : -1);
    }

    void OnMouseDown(POINT point) {
        if (mode_ == Mode::Selecting) {
            dragging_ = true;
            anchor_ = point;
            current_ = point;
            return;
        }
        pressedItem_ = ItemAt(point);
        pressedOutside_ = false;
        if (pressedItem_.item != Item::None) {
            return;
        }
        if (!PtInRect(&selection_, point)) {
            pressedOutside_ = true;
            return;
        }
        // Any tool moves a mark it's pressed on.
        int hit = MarkAt(point);
        if (typing_ && hit != selected_) {
            FinishTyping();  // May take away empty text, so look again.
            hit = MarkAt(point);
        }
        if (hit >= 0) {
            pressedSelected_ = hit == selected_;
            SelectMark(hit);
            SetHoverMark(-1);
            moving_ = hit;
            movePressed_ = point;
            moveLast_ = InSelection(point);
            moved_ = false;
            return;
        }
        SelectMark(-1);
        const Tool& tool = kTools[pen_.tool];
        if (!tool.makeMark) {
            ReleaseMouse();
            return;
        }
        std::unique_ptr<Mark> mark = tool.makeMark();
        mark->color = pen_.color;
        mark->thickness = pen_.thickness;
        mark->scale = Scale();
        mark->StartAt(InSelection(point));
        SetHoverMark(-1);
        if (tool.typed) {
            PushHistory();
            marks_.push_back(std::move(mark));
            SelectMark((int)marks_.size() - 1);
            StartTyping(true);
            ReleaseMouse();
            return;
        }
        drawing_ = std::move(mark);
        InvalidateClient(drawing_->Bounds());
    }

    void OnMouseUp(POINT point) {
        ReleaseMouse();
        if (mode_ == Mode::Selecting) {
            if (!dragging_) {
                return;
            }
            dragging_ = false;
            // A click (no drag) picks the window or display under the mouse.
            if (std::abs(point.x - anchor_.x) < GetSystemMetrics(SM_CXDRAG) &&
                std::abs(point.y - anchor_.y) < GetSystemMetrics(SM_CYDRAG)) {
                selection_ = hover_;
            }
            if (selection_.right - selection_.left < 2 || selection_.bottom - selection_.top < 2) {
                selection_ = {};
                InvalidateRect(hwnd_, nullptr, FALSE);
                return;
            }
            StartEditing();
            return;
        }
        if (drawing_) {
            std::unique_ptr<Mark> mark = std::move(drawing_);
            InvalidateClient(mark->Bounds());
            if (mark->Worthwhile()) {
                PushHistory();
                marks_.push_back(std::move(mark));
            }
            return;
        }
        if (moving_ >= 0) {
            int index = moving_;
            bool moved = moved_;
            EndMove();
            // A click on text, with the text tool or once it's selected, types into it.
            if (!moved && !typing_ && marks_[index]->Text() && (kTools[pen_.tool].typed || pressedSelected_)) {
                StartTyping(false);
            }
            SetHoverMark(MarkAt(point));
            return;
        }
        Item released = ItemAt(point).item;
        if (pressedItem_.item != Item::None && released == pressedItem_.item) {
            Activate(pressedItem_);
        } else if (pressedOutside_ && !PtInRect(&selection_, point) && released == Item::None) {
            Done(Finish::Default);
        }
        pressedItem_ = {Item::None, 0, {}};
        pressedOutside_ = false;
    }

    void OnKeyDown(UINT key) {
        bool control = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
        bool shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
        switch (key) {
            case VK_ESCAPE:
                Escape();
                return;
            case VK_RETURN:
                if (mode_ == Mode::Editing && (!typing_ || control)) {
                    Done(Finish::Default);
                }
                return;
        }
        if (mode_ != Mode::Editing) {
            return;
        }
        if (control) {
            switch (key) {
                case 'Z':
                    Undo();
                    return;
                case 'C':
                    Done(Finish::Copy);
                    return;
                case 'S':
                    Done(Finish::Save);
                    return;
            }
            return;
        }
        if (typing_) {
            return;  // Letters are typed, through WM_CHAR.
        }
        int step = shift ? 10 : 1;
        switch (key) {
            case VK_BACK:
            case VK_DELETE:
                if (!DeleteSelected()) {
                    Undo();
                }
                return;
            case VK_LEFT:
                Nudge(-step, 0);
                return;
            case VK_RIGHT:
                Nudge(step, 0);
                return;
            case VK_UP:
                Nudge(0, -step);
                return;
            case VK_DOWN:
                Nudge(0, step);
                return;
        }
        int tool = ToolForKey((wchar_t)key);
        if (tool >= 0) {
            SetTool(tool);
        }
    }

    void OnChar(wchar_t c) {
        if (!typing_) {
            return;
        }
        Mark& mark = *marks_[selected_];
        std::wstring& text = *mark.Text();
        RECT before = mark.Bounds();
        if (c == L'\b') {
            if (!text.empty()) {
                text.pop_back();
            }
        } else if (c == L'\r') {
            text += L'\n';
        } else if (c >= 32 && c != 127) {
            text += c;
        } else {
            return;
        }
        typingChanged_ = true;
        caretOn_ = true;
        SetTimer(hwnd_, kCaretTimer, 530, nullptr);  // Restarts the blink, so the caret shows while typing.
        InvalidateClient(before);
        InvalidateClient(mark.Bounds());
    }

    // ---- Marks

    MarkList CopyOfMarks() const {
        MarkList copy;
        for (const std::unique_ptr<Mark>& mark : marks_) {
            copy.push_back(mark->Clone());
        }
        return copy;
    }

    // Remembers the marks as they are, before a change, for undo.
    void PushHistory(Change change = Change::Other, int mark = -1) {
        if (change != Change::Other && change == lastChange_ && mark == lastChangeMark_ && !history_.empty()) {
            return;
        }
        history_.push_back(CopyOfMarks());
        lastChange_ = change;
        lastChangeMark_ = mark;
        InvalidateToolbox();  // Undo is available.
    }

    // Before changing a mark. Text is undone as a whole, back to before it was typed into.
    void BeforeChange(Change change, int mark) {
        if (typing_) {
            typingChanged_ = true;
        } else {
            PushHistory(change, mark);
        }
    }

    // Selects the mark, or none with -1. The toolbox takes on its color and thickness, and
    // changing them changes the mark.
    void SelectMark(int index) {
        if (index == selected_) {
            return;
        }
        if (selected_ >= 0) {
            InvalidateClient(marks_[selected_]->Bounds());
        }
        selected_ = index;
        lastChange_ = Change::Other;
        if (index >= 0) {
            const Mark& mark = *marks_[index];
            InvalidateClient(mark.Bounds());
            if (pen_.color != mark.color || pen_.thickness != mark.thickness) {
                pen_.color = mark.color;
                pen_.thickness = mark.thickness;
                PenChanged();
            }
        }
        InvalidateToolbox();
    }

    // The mark under the mouse gets a faint outline, to show it can be moved.
    void SetHoverMark(int index) {
        if (index == hoverMark_) {
            return;
        }
        if (hoverMark_ >= 0) {
            InvalidateClient(marks_[hoverMark_]->Bounds());
        }
        hoverMark_ = index;
        if (hoverMark_ >= 0) {
            InvalidateClient(marks_[hoverMark_]->Bounds());
        }
    }

    // Moves the mark being dragged with the mouse, once the mouse has moved far enough to be a drag
    // rather than a click.
    void MoveTo(POINT point) {
        if (!moved_) {
            if (std::abs(point.x - movePressed_.x) < GetSystemMetrics(SM_CXDRAG) &&
                std::abs(point.y - movePressed_.y) < GetSystemMetrics(SM_CYDRAG)) {
                return;
            }
            moved_ = true;
            BeforeChange(Change::Other, moving_);
        }
        Mark& mark = *marks_[moving_];
        gp::PointF to = InSelection(point);  // The part held stays in the selection.
        RECT before = mark.Bounds();
        mark.MoveBy(to.X - moveLast_.X, to.Y - moveLast_.Y);
        moveLast_ = to;
        InvalidateClient(before);
        InvalidateClient(mark.Bounds());
    }

    void EndMove() {
        if (moving_ >= 0) {
            moving_ = -1;
            ReleaseMouse();
            InvalidateToolbox();
        }
    }

    void CancelDrawing() {
        if (drawing_) {
            InvalidateClient(drawing_->Bounds());
            drawing_.reset();
            ReleaseMouse();
        }
    }

    // Moves the selected mark by whole pixels (arrow keys).
    void Nudge(int dx, int dy) {
        if (selected_ < 0 || drawing_ || moving_ >= 0) {
            return;
        }
        Mark& mark = *marks_[selected_];
        BeforeChange(Change::Nudge, selected_);
        RECT before = mark.Bounds();
        mark.MoveBy((float)dx, (float)dy);
        InvalidateClient(before);
        InvalidateClient(mark.Bounds());
    }

    // Deletes the selected mark, if there is one.
    bool DeleteSelected() {
        if (selected_ < 0) {
            return false;
        }
        EndMove();
        PushHistory();
        InvalidateClient(marks_[selected_]->Bounds());
        marks_.erase(marks_.begin() + selected_);
        selected_ = -1;
        hoverMark_ = -1;
        InvalidateToolbox();
        return true;
    }

    void StartTyping(bool isNew) {
        if (!isNew) {
            PushHistory();
        }
        typing_ = true;
        typingNew_ = isNew;
        typingChanged_ = false;
        caretOn_ = true;
        SetTimer(hwnd_, kCaretTimer, 530, nullptr);
        InvalidateClient(marks_[selected_]->Bounds());
        InvalidateToolbox();
    }

    // Stops typing, and deselects the text. Text left empty goes.
    void FinishTyping() {
        if (!typing_) {
            return;
        }
        EndMove();  // The text may go.
        typing_ = false;
        KillTimer(hwnd_, kCaretTimer);
        int index = selected_;
        InvalidateClient(marks_[index]->Bounds());
        selected_ = -1;
        lastChange_ = Change::Other;
        if (!marks_[index]->Worthwhile()) {
            marks_.erase(marks_.begin() + index);
            hoverMark_ = -1;
            if (typingNew_) {
                history_.pop_back();  // As if it was never there.
            }
        } else if (!typingNew_ && !typingChanged_) {
            history_.pop_back();  // Nothing changed.
        }
        InvalidateToolbox();
    }

    void Undo() {
        FinishTyping();
        CancelDrawing();
        EndMove();
        if (history_.empty()) {
            return;
        }
        marks_ = std::move(history_.back());
        history_.pop_back();
        selected_ = -1;
        hoverMark_ = -1;
        lastChange_ = Change::Other;
        InvalidateClient(selection_);  // Marks are only drawn there.
        InvalidateToolbox();
    }

    void SetTool(int tool) {
        FinishTyping();
        if (kTools[tool].makeMark) {
            SelectMark(-1);  // A drawing tool is for new marks.
        }
        pen_.tool = tool;
        InvalidateToolbox();
        PenChanged();
    }

    void SetColor(ARGB color) {
        pen_.color = color;
        if (selected_ >= 0 && marks_[selected_]->color != color) {
            Mark& mark = *marks_[selected_];
            BeforeChange(Change::Restyle, selected_);
            mark.color = color;
            InvalidateClient(mark.Bounds());
        }
        InvalidateToolbox();
        PenChanged();
    }

    void SetThickness(int thickness) {
        thickness = std::clamp(thickness, kMinThickness, kMaxThickness);
        if (thickness == pen_.thickness) {
            return;
        }
        pen_.thickness = thickness;
        if (selected_ >= 0 && marks_[selected_]->thickness != thickness) {
            Mark& mark = *marks_[selected_];
            BeforeChange(Change::Restyle, selected_);
            RECT before = mark.Bounds();
            mark.thickness = thickness;
            InvalidateClient(before);
            InvalidateClient(mark.Bounds());
        }
        InvalidateToolbox();
        PenChanged();
    }

    void PenChanged() {
        if (onPenChanged) {
            onPenChanged(pen_);
        }
    }

    // Lets Windows' color dialog pick any color.
    void PickColor() {
        static COLORREF customColors[16] = {};
        gp::Color current(pen_.color);
        CHOOSECOLORW choose{sizeof(choose)};
        choose.hwndOwner = hwnd_;
        choose.rgbResult = RGB(current.GetR(), current.GetG(), current.GetB());
        choose.lpCustColors = customColors;
        choose.Flags = CC_FULLOPEN | CC_RGBINIT;
        if (ChooseColorW(&choose)) {
            SetColor(gp::Color::MakeARGB(255, GetRValue(choose.rgbResult), GetGValue(choose.rgbResult), GetBValue(choose.rgbResult)));
        }
    }

    void Activate(const ToolboxItem& item) {
        switch (item.item) {
            case Item::Tool:
                SetTool(item.index);
                break;
            case Item::Swatch:
                SetColor(kSwatches[item.index]);
                break;
            case Item::CustomColor:
                PickColor();
                break;
            case Item::Thinner:
                SetThickness(pen_.thickness - 1);
                break;
            case Item::Thicker:
                SetThickness(pen_.thickness + 1);
                break;
            case Item::Undo:
                Undo();
                break;
            case Item::Copy:
                Done(Finish::Copy);
                break;
            case Item::Save:
                Done(Finish::Save);
                break;
            case Item::Close:
                Done(std::nullopt);
                break;
            case Item::Thickness:
            case Item::None:
                break;
        }
    }

    void Done(std::optional<Finish> finish) {
        if (done_) {
            return;
        }
        FinishTyping();
        CancelDrawing();
        EndMove();
        done_ = true;
        ReleaseMouse();
        KillTimer(hwnd_, kCaretTimer);
        ShowWindow(hwnd_, SW_HIDE);
        if (onDone) {
            onDone(finish);
        }
    }

    // ---- The toolbox

    void StartEditing() {
        mode_ = Mode::Editing;
        scale_ = ScaleForScreenRect(ToScreen(selection_));
        LayOutToolbox();
        InvalidateRect(hwnd_, nullptr, FALSE);
    }

    // The toolbox goes centered above the selection; below it when there's no room above, or else
    // inside its top edge. It stays on the selection's display.
    void LayOutToolbox() {
        toolbox_.clear();
        separators_.clear();
        int padding = Px(6);
        int button = Px(36);
        int swatch = Px(30);
        int separator = Px(17);
        struct Slot {
            Item item;
            int index;
            int width;
        };
        std::vector<Slot> slots;
        auto add = [&](Item item, int width, int index = 0) { slots.push_back({item, index, width}); };
        for (int i = 0; i < kToolCount; i++) {
            add(Item::Tool, button, i);
        }
        add(Item::None, separator);
        for (int i = 0; i < kSwatchCount; i++) {
            add(Item::Swatch, swatch, i);
        }
        add(Item::CustomColor, swatch);
        add(Item::None, separator);
        add(Item::Thinner, button);
        add(Item::Thickness, Px(52));
        add(Item::Thicker, button);
        add(Item::None, separator);
        add(Item::Undo, button);
        add(Item::Copy, button);
        add(Item::Save, button);
        add(Item::Close, button);
        int width = padding * 2;
        for (const Slot& slot : slots) {
            width += slot.width;
        }
        int height = padding * 2 + button;

        RECT work = ToClient(WorkAreaForScreenRect(ToScreen(selection_)));
        int gap = Px(10);
        int left = (selection_.left + selection_.right - width) / 2;
        left = std::max((int)work.left + gap, std::min(left, (int)work.right - width - gap));
        int top = selection_.top - gap - height;
        toolboxAbove_ = true;
        if (top < work.top + gap) {
            toolboxAbove_ = false;
            top = selection_.bottom + gap;
            if (top + height > work.bottom - gap) {
                top = std::max((int)work.top + gap, (int)selection_.top + gap);
            }
        }
        toolboxRect_ = {left, top, left + width, top + height};

        int x = left + padding;
        for (const Slot& slot : slots) {
            if (slot.item != Item::None) {
                toolbox_.push_back({slot.item, slot.index, {x, top + padding, x + slot.width, top + padding + button}});
            } else {
                separators_.push_back(x + slot.width / 2);
            }
            x += slot.width;
        }
    }

    ToolboxItem ItemAt(POINT point) const {
        if (mode_ == Mode::Editing) {
            for (const ToolboxItem& item : toolbox_) {
                if (PtInRect(&item.rect, point)) {
                    return item;
                }
            }
        }
        return {Item::None, 0, {}};
    }

    void SetHoverItem(const ToolboxItem& item) {
        if (item.item == hoverItem_.item && item.index == hoverItem_.index) {
            return;
        }
        hoverItem_ = item;
        InvalidateToolbox();
    }

    // The toolbox and its tooltip.
    void InvalidateToolbox() {
        if (mode_ == Mode::Editing) {
            InvalidateClient(InflatedRect(toolboxRect_, Px(48)));
        }
    }

    std::wstring TooltipFor(const ToolboxItem& item) const {
        switch (item.item) {
            case Item::Tool:
                return std::wstring(kTools[item.index].name) + L" (" + kTools[item.index].key + L")";
            case Item::Swatch:
                return kSwatchNames[item.index];
            case Item::CustomColor:
                return L"Any color…";
            case Item::Thinner:
                return L"Thinner (mouse wheel)";
            case Item::Thickness:
                return L"Thickness " + std::to_wstring(pen_.thickness) + L" (mouse wheel)";
            case Item::Thicker:
                return L"Thicker (mouse wheel)";
            case Item::Undo:
                return L"Undo (Ctrl+Z)";
            case Item::Copy:
                return L"Copy and close (Ctrl+C)";
            case Item::Save:
                return L"Save and close (Ctrl+S)";
            case Item::Close:
                return L"Close (Esc)";
            case Item::None:
                break;
        }
        return {};
    }

    LPCWSTR CursorAt(POINT point) const {
        if (mode_ == Mode::Selecting) {
            return IDC_CROSS;
        }
        const Tool& tool = kTools[pen_.tool];
        if (drawing_) {
            return tool.cursor;
        }
        if (moving_ >= 0) {
            return IDC_SIZEALL;
        }
        if (ItemAt(point).item != Item::None) {
            return IDC_HAND;
        }
        if (PtInRect(&selection_, point)) {
            int mark = MarkAt(point);
            if (mark >= 0) {
                // Text can be clicked to type into with the text tool, and dragged to move it.
                return tool.typed && marks_[mark]->Text() ? IDC_IBEAM : IDC_SIZEALL;
            }
            return tool.cursor;
        }
        return IDC_ARROW;  // Clicking out here finishes.
    }

    // ---- Painting

    // Dims the parts of the area outside the bright rectangle, drawing into a buffer whose top-left
    // is at the area's top-left.
    void DimOutside(HDC buffer, const RECT& area, const RECT& bright) {
        BLENDFUNCTION blend{AC_SRC_OVER, 0, 120, 0};
        std::vector<RECT> dim;
        if (IsRectEmpty(&bright)) {
            dim.push_back(area);
        } else {
            dim.push_back({area.left, area.top, area.right, bright.top});
            dim.push_back({area.left, bright.bottom, area.right, area.bottom});
            dim.push_back({area.left, bright.top, bright.left, bright.bottom});
            dim.push_back({bright.right, bright.top, area.right, bright.bottom});
        }
        for (RECT part : dim) {
            if (!IntersectRect(&part, &part, &area)) {
                continue;
            }
            AlphaBlend(buffer, part.left - area.left, part.top - area.top, part.right - part.left, part.bottom - part.top, blackDc_,
                       0, 0, 1, 1, blend);
        }
    }

    void Paint(HDC dc, const RECT& area) {
        int width = area.right - area.left;
        int height = area.bottom - area.top;
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(info.bmiHeader);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr;
        HDC buffer = CreateCompatibleDC(dc);
        HBITMAP bitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (!buffer || !bitmap) {
            if (bitmap) {
                DeleteObject(bitmap);
            }
            if (buffer) {
                DeleteDC(buffer);
            }
            return;
        }
        HGDIOBJ previous = SelectObject(buffer, bitmap);
        BitBlt(buffer, 0, 0, width, height, screen_->Dc(), area.left, area.top, SRCCOPY);
        RECT bright = Highlight();
        DimOutside(buffer, area, bright);
        {
            gp::Graphics graphics(buffer);
            graphics.TranslateTransform((float)-area.left, (float)-area.top);
            graphics.SetSmoothingMode(gp::SmoothingModeAntiAlias);
            graphics.SetTextRenderingHint(gp::TextRenderingHintAntiAliasGridFit);
            if (!IsRectEmpty(&bright)) {
                graphics.SetClip(ToRectF(bright));
                for (size_t i = 0; i < marks_.size(); i++) {
                    marks_[i]->Draw(graphics, typing_ && (int)i == selected_ && caretOn_);
                }
                if (drawing_) {
                    drawing_->Draw(graphics, false);
                }
                if (hoverMark_ >= 0 && hoverMark_ != selected_) {
                    DrawMarkFrame(graphics, *marks_[hoverMark_], false);
                }
                if (selected_ >= 0) {
                    DrawMarkFrame(graphics, *marks_[selected_], true);
                }
                graphics.ResetClip();
                DrawSelectionFrame(graphics, bright);
            }
            if (mode_ == Mode::Selecting) {
                if (dragging_) {
                    DrawSizeLabel(graphics, bright);
                } else {
                    DrawHint(graphics);
                }
            } else {
                DrawToolbox(graphics);
            }
        }
        BitBlt(dc, area.left, area.top, width, height, buffer, 0, 0, SRCCOPY);
        SelectObject(buffer, previous);
        DeleteObject(bitmap);
        DeleteDC(buffer);
    }

    // A dashed outline around the selected mark, or a faint one around the mark under the mouse.
    void DrawMarkFrame(gp::Graphics& graphics, const Mark& mark, bool selected) {
        float scale = Scale();
        gp::RectF frame = mark.Outline();
        float spread = mark.Width() / 2 + 4 * scale;
        frame.Inflate(spread, spread);
        if (!selected) {
            gp::Pen pen(WithAlpha(150, kSelectionColor), std::max(1.0f, scale));
            graphics.DrawRectangle(&pen, frame);
            return;
        }
        // Dashes over a white line, to show on light and dark pictures alike.
        gp::Pen under(Argb(220, 255, 255, 255), 1.5f * scale);
        graphics.DrawRectangle(&under, frame);
        gp::Pen pen(Argb(255, 0, 120, 212), 1.5f * scale);
        const gp::REAL dashes[] = {3, 2};  // In pen widths.
        pen.SetDashPattern(dashes, ARRAYSIZE(dashes));
        graphics.DrawRectangle(&pen, frame);
    }

    // A crisp line just outside the selection with a soft glow around it.
    void DrawSelectionFrame(gp::Graphics& graphics, const RECT& rect) {
        float scale = mode_ == Mode::Editing ? Scale() : ScaleForScreenRect(ToScreen(rect));
        gp::RectF frame = ToRectF(rect);
        for (int ring = 3; ring >= 1; ring--) {
            gp::RectF glow = frame;
            float spread = ring * 2.5f * scale;
            glow.Inflate(spread, spread);
            gp::Pen pen(WithAlpha(30 + (3 - ring) * 25, kSelectionColor), 2.5f * scale);
            graphics.DrawRectangle(&pen, glow);
        }
        gp::RectF line = frame;
        float width = std::max(1.5f, 1.5f * scale);
        line.Inflate(width / 2, width / 2);
        gp::Pen pen(kSelectionColor, width);
        graphics.DrawRectangle(&pen, line);
    }

    // The selection's size in pixels, next to it while dragging.
    void DrawSizeLabel(gp::Graphics& graphics, const RECT& rect) {
        float scale = ScaleForScreenRect(ToScreen(rect));
        gp::Font font(L"Segoe UI", 12.0f * scale, gp::FontStyleRegular, gp::UnitPixel);
        std::wstring text = std::to_wstring(rect.right - rect.left) + L" × " + std::to_wstring(rect.bottom - rect.top);
        float y = rect.top - 36 * scale;
        if (y < 4 * scale) {
            y = rect.top + 8 * scale;
        }
        DrawLabel(graphics, text, font, LabelRect(graphics, text, font, (float)rect.left, y, scale), scale);
    }

    void DrawHint(gp::Graphics& graphics) {
        if (IsRectEmpty(&hintRect_)) {
            return;
        }
        float scale = (hintRect_.bottom - hintRect_.top) / 40.0f;
        gp::Font font(L"Segoe UI", 13.0f * scale, gp::FontStyleRegular, gp::UnitPixel);
        DrawLabel(graphics, L"Drag to select an area, or click a window  ·  Esc to cancel", font, ToRectF(hintRect_), scale);
    }

    void DrawToolbox(gp::Graphics& graphics) {
        Palette colors = Palette::Current();
        float scale = Scale();
        gp::RectF box = ToRectF(toolboxRect_);
        // A soft shadow under it.
        for (int ring = 1; ring <= 4; ring++) {
            gp::RectF shadow = box;
            shadow.Inflate(ring * scale * 1.5f, ring * scale * 1.5f);
            shadow.Offset(0, 2 * scale);
            FillRoundedRect(graphics, Argb(14, 0, 0, 0), shadow, 10 * scale);
        }
        FillRoundedRect(graphics, colors.background, box, 8 * scale);
        DrawRoundedRect(graphics, colors.border, box, 8 * scale, 1);
        for (int x : separators_) {
            gp::Pen pen(colors.border, 1);
            graphics.DrawLine(&pen, (float)x, box.Y + 12 * scale, (float)x, box.GetBottom() - 12 * scale);
        }
        gp::Font numberFont(L"Segoe UI", 12.0f * scale, gp::FontStyleRegular, gp::UnitPixel);
        bool customColor = std::find(std::begin(kSwatches), std::end(kSwatches), pen_.color) == std::end(kSwatches);
        for (const ToolboxItem& item : toolbox_) {
            gp::RectF rect = ToRectF(item.rect);
            bool hovered = item.item == hoverItem_.item && item.index == hoverItem_.index && item.item != Item::Thickness;
            bool pressed = hovered && item.item == pressedItem_.item && item.index == pressedItem_.index;
            bool active = item.item == Item::Tool && item.index == pen_.tool;
            gp::RectF button = rect;
            button.Inflate(-2 * scale, -2 * scale);
            if (active) {
                FillRoundedRect(graphics, WithAlpha(lightTheme_ ? 40 : 50, colors.accent), button, 4 * scale);
            } else if (pressed || hovered) {
                FillRoundedRect(graphics, pressed ? colors.pressed : colors.hover, button, 4 * scale);
            }
            gp::Color ink = active ? colors.accent : colors.foreground;
            if (item.item == Item::Undo && history_.empty()) {
                ink = WithAlpha(90, colors.foreground);
            }
            gp::RectF icon(rect.X + (rect.Width - 18 * scale) / 2, rect.Y + (rect.Height - 18 * scale) / 2, 18 * scale, 18 * scale);
            switch (item.item) {
                case Item::Tool:
                    kTools[item.index].drawIcon(graphics, icon, ink, scale);
                    break;
                case Item::Swatch:
                case Item::CustomColor: {
                    float diameter = 18 * scale;
                    gp::RectF circle(rect.X + (rect.Width - diameter) / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);
                    bool selected = item.item == Item::Swatch ? pen_.color == kSwatches[item.index] : customColor;
                    if (item.item == Item::Swatch || customColor) {
                        gp::SolidBrush fill(gp::Color(item.item == Item::Swatch ? kSwatches[item.index] : pen_.color));
                        graphics.FillEllipse(&fill, circle);
                        gp::Pen edge(WithAlpha(80, colors.foreground), 1);
                        graphics.DrawEllipse(&edge, circle);
                    } else {
                        DrawGlyph(graphics, kGlyphColor, 16 * scale, rect, colors.foreground);
                    }
                    if (selected) {
                        gp::RectF ring = circle;
                        ring.Inflate(3.5f * scale, 3.5f * scale);
                        gp::Pen pen(colors.foreground, 1.6f * scale);
                        graphics.DrawEllipse(&pen, ring);
                    }
                    break;
                }
                case Item::Thinner:
                    DrawGlyph(graphics, kGlyphMinus, 13 * scale, rect, pen_.thickness > kMinThickness ? ink : WithAlpha(90, ink));
                    break;
                case Item::Thicker:
                    DrawGlyph(graphics, kGlyphPlus, 13 * scale, rect, pen_.thickness < kMaxThickness ? ink : WithAlpha(90, ink));
                    break;
                case Item::Thickness: {
                    // A sample line in the color and thickness, and the number.
                    float line = std::min((float)pen_.thickness, 12.0f) * scale;
                    gp::Pen pen(gp::Color(pen_.color), line);
                    pen.SetStartCap(gp::LineCapRound);
                    pen.SetEndCap(gp::LineCapRound);
                    float middle = rect.Y + rect.Height * 0.42f;
                    graphics.DrawLine(&pen, rect.X + 8 * scale, middle, rect.GetRight() - 8 * scale, middle);
                    gp::RectF number(rect.X, rect.GetBottom() - 15 * scale, rect.Width, 14 * scale);
                    DrawTextLine(graphics, std::to_wstring(pen_.thickness), numberFont, colors.secondary, number, TextAlign::Center);
                    break;
                }
                case Item::Undo:
                    DrawGlyph(graphics, kGlyphUndo, 16 * scale, rect, ink);
                    break;
                case Item::Copy:
                    DrawGlyph(graphics, kGlyphCopy, 16 * scale, rect, ink);
                    break;
                case Item::Save:
                    DrawGlyph(graphics, kGlyphSave, 16 * scale, rect, ink);
                    break;
                case Item::Close:
                    DrawGlyph(graphics, kGlyphClose, 13 * scale, rect, ink);
                    break;
                case Item::None:
                    break;
            }
        }
        // The hovered item's name and shortcut, on the side away from the selection.
        std::wstring tooltip = TooltipFor(hoverItem_);
        if (!tooltip.empty()) {
            gp::Font font(L"Segoe UI", 12.0f * scale, gp::FontStyleRegular, gp::UnitPixel);
            gp::RectF label = LabelRect(graphics, tooltip, font, 0, 0, scale);
            label.X = std::max(box.X, std::min((hoverItem_.rect.left + hoverItem_.rect.right) / 2.0f - label.Width / 2,
                                               box.GetRight() - label.Width));
            label.Y = toolboxAbove_ ? box.Y - label.Height - 6 * scale : box.GetBottom() + 6 * scale;
            DrawLabel(graphics, tooltip, font, label, scale);
        }
    }

    std::unique_ptr<ScreenCopy> screen_;
    std::vector<RECT> windows_;
    Pen pen_;
    Mode mode_ = Mode::Selecting;
    RECT hover_{};
    RECT hintRect_{};
    RECT selection_{};
    POINT anchor_{};
    POINT current_{};
    bool dragging_ = false;
    MarkList marks_;
    std::vector<MarkList> history_;  // The marks before each change, for undo.
    Change lastChange_ = Change::Other;
    int lastChangeMark_ = -1;
    std::unique_ptr<Mark> drawing_;  // The mark being drawn, until the mouse is let go.
    int selected_ = -1;              // The selected mark, in marks_.
    int hoverMark_ = -1;             // The mark under the mouse.
    int moving_ = -1;                // The mark being dragged.
    bool pressedSelected_ = false;   // It was selected already when the mouse was pressed on it.
    POINT movePressed_{};
    gp::PointF moveLast_;
    bool moved_ = false;
    bool typing_ = false;  // The selected mark is text being typed.
    bool typingNew_ = false;
    bool typingChanged_ = false;
    bool caretOn_ = false;
    float scale_ = 1;  // Of the selection's display.
    std::vector<ToolboxItem> toolbox_;
    std::vector<int> separators_;
    RECT toolboxRect_{};
    bool toolboxAbove_ = true;
    ToolboxItem hoverItem_{Item::None, 0, {}};
    ToolboxItem pressedItem_{Item::None, 0, {}};
    bool pressedOutside_ = false;
    int wheelRemainder_ = 0;
    bool lightTheme_ = false;
    bool done_ = false;
    HDC blackDc_ = nullptr;
    HBITMAP black_ = nullptr;
    HGDIOBJ blackPrevious_ = nullptr;
};

// ---------------------------------------------------------------------------
// The app: waits for Print Screen or a click on the button, runs the overlay, and copies or saves
// the screenshot.

class SnipApp {
public:
    bool Start() {
        settings_ = ModSettings::Load();
        int tool = ToolForKey((wchar_t)Storage::GetInt(L"Tool", kDefaultToolKey));
        if (tool >= 0) {
            pen_.tool = tool;
        }
        pen_.color = (ARGB)Storage::GetInt(L"Color", (int)pen_.color);
        pen_.thickness = std::clamp(Storage::GetInt(L"Thickness", pen_.thickness), kMinThickness, kMaxThickness);
        if (!messages_.Create(L"TaskbarSnipper.Mod.Messages") || !notice_.Create()) {
            return false;
        }
        messages_.onMessage = [this](UINT message, WPARAM wParam, LPARAM lParam) { OnMessage(message); };
        // The button's window is there even while it's hidden, so the other widgets still see it,
        // and it still hears about changes to the Windows settings.
        if (!widget_.Create(Storage::GetInt(L"TaskbarOffset", -1))) {
            return false;
        }
        widget_.onOffsetChanged = [](int offset) { Storage::SetInt(L"TaskbarOffset", offset); };
        widget_.onSnip = [this]() { StartCaptureFromWidget(); };
        widget_.onMenu = [this]() { ShowMenu(); };
        widget_.SetShown(settings_.showButton);
        return hook_.Start(messages_.Handle());
    }

    ~SnipApp() {
        hook_.Stop();
        overlay_.reset();
        widget_.Destroy();
    }

    HWND MessageHandle() const { return messages_.Handle(); }

private:
    void OnMessage(UINT message) {
        switch (message) {
            case kPrintScreenMessage:
                StartCapture();
                break;
            case kEscapeMessage:
                if (overlay_) {
                    overlay_->Escape();
                }
                break;
            case kSettingsChangedMessage:
                settings_ = ModSettings::Load();
                widget_.SetShown(settings_.showButton);
                break;
            case kOverlayClosedMessage:
                overlay_.reset();
                hook_.SetOverlayOpen(false);
                break;
        }
    }

    void StartCapture() {
        if (overlay_) {
            return;
        }
        // Where the windows are, before the overlay covers them.
        std::vector<RECT> windows = VisibleWindowRects();
        auto screen = std::make_unique<ScreenCopy>();
        if (!screen->Take()) {
            Wh_Log(L"Couldn't copy the screen");
            return;
        }
        overlay_ = std::make_unique<Overlay>(std::move(screen), std::move(windows), pen_);
        overlay_->onPenChanged = [this](const Pen& pen) { pen_ = pen; };
        overlay_->onDone = [this](std::optional<Finish> finish) { OnOverlayDone(finish); };
        if (!overlay_->Create()) {
            Wh_Log(L"Couldn't open the overlay");
            overlay_.reset();
            return;
        }
        hook_.SetOverlayOpen(true);
    }

    // A screenshot from the button or its menu: waits for the screen to show the button as it is
    // when it isn't hovered or pressed, so the screenshot shows it that way too.
    void StartCaptureFromWidget() {
        DwmFlush();
        StartCapture();
    }

    void OpenSaveFolder() {
        SHCreateDirectoryExW(nullptr, settings_.saveFolder.c_str(), nullptr);  // Fails harmlessly when it's there already.
        ShellExecuteW(nullptr, L"open", settings_.saveFolder.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
    }

    void ShowMenu() {
        Menu menu;
        menu.Add(L"Take a screenshot\tPrint Screen", [this]() { StartCaptureFromWidget(); });
        menu.Add(L"Open the screenshots folder", [this]() { OpenSaveFolder(); });
        AddLinkMenu(menu, widget_);
        RECT bounds = widget_.Bounds();
        POINT cursor;
        GetCursorPos(&cursor);
        // Above the widget, which keeps itself on top and would otherwise cover the menu.
        menu.Show(widget_.Handle(), {cursor.x, bounds.top});
    }

    void OnOverlayDone(std::optional<Finish> finish) {
        // The overlay is deleted once its window has finished with this message.
        PostMessageW(messages_.Handle(), kOverlayClosedMessage, 0, 0);
        Storage::SetInt(L"Tool", kTools[pen_.tool].key);  // By its shortcut, which stays the same as tools are added.
        Storage::SetInt(L"Color", (int)pen_.color);
        Storage::SetInt(L"Thickness", pen_.thickness);
        if (!finish) {
            return;
        }
        RECT selection = overlay_->SelectionInOverlay();
        RECT onScreen = overlay_->SelectionOnScreen();
        std::unique_ptr<gp::Bitmap> image = ComposeScreenshot(overlay_->Screen(), selection, overlay_->Marks());
        if (!image) {
            notice_.Show(L"Couldn't make the screenshot", L"Windows ran out of memory for it.", L"", nullptr, onScreen);
            return;
        }
        bool copy = *finish == Finish::Copy || (*finish == Finish::Default && settings_.afterCapture != AfterCapture::Save);
        bool save = *finish == Finish::Save || (*finish == Finish::Default && settings_.afterCapture != AfterCapture::Copy);
        bool copied = copy && CopyToClipboard(messages_.Handle(), *image);
        std::optional<std::wstring> saved = save ? SaveScreenshot(*image, settings_.saveFolder) : std::nullopt;

        std::wstring size = std::to_wstring(image->GetWidth()) + L" × " + std::to_wstring(image->GetHeight());
        std::wstring fileName = saved ? saved->substr(saved->find_last_of(L'\\') + 1) : L"";
        if (save && !saved) {
            notice_.Show(copied ? L"Copied, but couldn't save it" : L"Couldn't save the screenshot", settings_.saveFolder, L"",
                         image.get(), onScreen);
        } else if (copy && !copied) {
            notice_.Show(saved ? L"Saved, but couldn't copy it" : L"Couldn't copy the screenshot",
                         saved ? fileName : L"Another app is holding the clipboard.", saved.value_or(L""), image.get(), onScreen);
        } else if (copied && saved) {
            notice_.Show(L"Copied and saved", fileName + L" · click to show it", *saved, image.get(), onScreen);
        } else if (saved) {
            notice_.Show(L"Screenshot saved", fileName + L" · click to show it", *saved, image.get(), onScreen);
        } else {
            notice_.Show(L"Copied to the clipboard", size, L"", image.get(), onScreen);
        }
    }

    ModSettings settings_;
    Pen pen_;
    MessageWindow messages_;
    KeyboardHook hook_;
    Notice notice_;
    SnipperWidget widget_;
    std::unique_ptr<Overlay> overlay_;
};

// ---------------------------------------------------------------------------
// Runs the UI on its own thread with a message loop, for WhTool_ModInit/WhTool_ModUninit.

class UiThread {
public:
    // run is called on the new thread (with COM and GDI+ set up) and returns an object that lives
    // until the mod is unloaded, or null if it couldn't start. Returns whether it started.
    bool Start(std::function<std::shared_ptr<void>()> run) {
        run_ = std::move(run);
        ready_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        thread_ = CreateThread(nullptr, 0, ThreadProc, this, 0, &threadId_);
        if (!thread_) {
            return false;
        }
        return WaitForSingleObject(ready_, 10000) == WAIT_OBJECT_0 && started_;
    }

    void Stop() {
        if (!thread_) {
            return;
        }
        PostThreadMessageW(threadId_, WM_QUIT, 0, 0);
        WaitForSingleObject(thread_, 10000);
        CloseHandle(thread_);
        thread_ = nullptr;
        CloseHandle(ready_);
    }

private:
    static DWORD WINAPI ThreadProc(void* parameter) {
        auto self = (UiThread*)parameter;
        HRESULT com = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        ULONG_PTR gdiplusToken = 0;
        gp::GdiplusStartupInput startup;
        gp::GdiplusStartup(&gdiplusToken, &startup, nullptr);
        MSG message;
        PeekMessageW(&message, nullptr, WM_USER, WM_USER, PM_NOREMOVE);  // Creates the message queue.
        {
            std::shared_ptr<void> app = self->run_();
            self->started_ = app != nullptr;
            SetEvent(self->ready_);
            if (app) {
                while (GetMessageW(&message, nullptr, 0, 0) > 0) {
                    TranslateMessage(&message);
                    DispatchMessageW(&message);
                }
            }
        }
        UnregisterWindowClasses();
        gp::GdiplusShutdown(gdiplusToken);
        if (SUCCEEDED(com)) {
            CoUninitialize();
        }
        return 0;
    }

    std::function<std::shared_ptr<void>()> run_;
    HANDLE thread_ = nullptr;
    HANDLE ready_ = nullptr;
    DWORD threadId_ = 0;
    bool started_ = false;
};

UiThread g_uiThread;
std::atomic<HWND> g_messageWindow{nullptr};  // For telling the app the settings changed.

}  // namespace

BOOL WhTool_ModInit() {
    Wh_Log(L">");
    return g_uiThread.Start([]() -> std::shared_ptr<void> {
        auto app = std::make_shared<SnipApp>();
        if (!app->Start()) {
            Wh_Log(L"Couldn't start");
            return nullptr;
        }
        g_messageWindow = app->MessageHandle();
        return app;
    });
}

void WhTool_ModSettingsChanged() {
    Wh_Log(L">");
    if (HWND window = g_messageWindow) {
        PostMessageW(window, kSettingsChangedMessage, 0, 0);
    }
}

void WhTool_ModUninit() {
    Wh_Log(L">");
    g_messageWindow = nullptr;
    g_uiThread.Stop();
}

////////////////////////////////////////////////////////////////////////////////
// Windhawk tool mod implementation for mods which don't need to inject to other
// processes or hook other functions. Context:
// https://github.com/ramensoftware/windhawk/wiki/Mods-as-tools:-Running-mods-in-a-dedicated-process
//
// The mod will load and run in a dedicated windhawk.exe process.
//
// Paste the code below as part of the mod code, and use these callbacks:
// * WhTool_ModInit
// * WhTool_ModSettingsChanged
// * WhTool_ModUninit
//
// Currently, other callbacks are not supported.

bool g_isToolModProcessLauncher;
HANDLE g_toolModProcessMutex;

void WINAPI EntryPoint_Hook() {
    Wh_Log(L">");
    ExitThread(0);
}

BOOL Wh_ModInit() {
    DWORD sessionId;
    if (ProcessIdToSessionId(GetCurrentProcessId(), &sessionId) &&
        sessionId == 0) {
        return FALSE;
    }

    bool isExcluded = false;
    bool isToolModProcess = false;
    bool isCurrentToolModProcess = false;
    int argc;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLine(), &argc);
    if (!argv) {
        Wh_Log(L"CommandLineToArgvW failed");
        return FALSE;
    }

    for (int i = 1; i < argc; i++) {
        if (wcscmp(argv[i], L"-service") == 0 ||
            wcscmp(argv[i], L"-service-start") == 0 ||
            wcscmp(argv[i], L"-service-stop") == 0) {
            isExcluded = true;
            break;
        }
    }

    for (int i = 1; i < argc - 1; i++) {
        if (wcscmp(argv[i], L"-tool-mod") == 0) {
            isToolModProcess = true;
            if (wcscmp(argv[i + 1], WH_MOD_ID) == 0) {
                isCurrentToolModProcess = true;
            }
            break;
        }
    }

    LocalFree(argv);

    if (isExcluded) {
        return FALSE;
    }

    if (isCurrentToolModProcess) {
        g_toolModProcessMutex =
            CreateMutex(nullptr, TRUE, L"windhawk-tool-mod_" WH_MOD_ID);
        if (!g_toolModProcessMutex) {
            Wh_Log(L"CreateMutex failed");
            ExitProcess(1);
        }

        if (GetLastError() == ERROR_ALREADY_EXISTS) {
            Wh_Log(L"Tool mod already running (%s)", WH_MOD_ID);
            ExitProcess(1);
        }

        if (!WhTool_ModInit()) {
            ExitProcess(1);
        }

        IMAGE_DOS_HEADER* dosHeader =
            (IMAGE_DOS_HEADER*)GetModuleHandle(nullptr);
        IMAGE_NT_HEADERS* ntHeaders =
            (IMAGE_NT_HEADERS*)((BYTE*)dosHeader + dosHeader->e_lfanew);

        DWORD entryPointRVA = ntHeaders->OptionalHeader.AddressOfEntryPoint;
        void* entryPoint = (BYTE*)dosHeader + entryPointRVA;

        Wh_SetFunctionHook(entryPoint, (void*)EntryPoint_Hook, nullptr);
        return TRUE;
    }

    if (isToolModProcess) {
        return FALSE;
    }

    g_isToolModProcessLauncher = true;
    return TRUE;
}

void Wh_ModAfterInit() {
    if (!g_isToolModProcessLauncher) {
        return;
    }

    WCHAR currentProcessPath[MAX_PATH];
    switch (GetModuleFileName(nullptr, currentProcessPath,
                              ARRAYSIZE(currentProcessPath))) {
        case 0:
        case ARRAYSIZE(currentProcessPath):
            Wh_Log(L"GetModuleFileName failed");
            return;
    }

    WCHAR
    commandLine[MAX_PATH + 2 +
                (sizeof(L" -tool-mod \"" WH_MOD_ID "\"") / sizeof(WCHAR)) - 1];
    swprintf_s(commandLine, L"\"%s\" -tool-mod \"%s\"", currentProcessPath,
               WH_MOD_ID);

    HMODULE kernelModule = GetModuleHandle(L"kernelbase.dll");
    if (!kernelModule) {
        kernelModule = GetModuleHandle(L"kernel32.dll");
        if (!kernelModule) {
            Wh_Log(L"No kernelbase.dll/kernel32.dll");
            return;
        }
    }

    using CreateProcessInternalW_t = BOOL(WINAPI*)(
        HANDLE hUserToken, LPCWSTR lpApplicationName, LPWSTR lpCommandLine,
        LPSECURITY_ATTRIBUTES lpProcessAttributes,
        LPSECURITY_ATTRIBUTES lpThreadAttributes, WINBOOL bInheritHandles,
        DWORD dwCreationFlags, LPVOID lpEnvironment, LPCWSTR lpCurrentDirectory,
        LPSTARTUPINFOW lpStartupInfo,
        LPPROCESS_INFORMATION lpProcessInformation,
        PHANDLE hRestrictedUserToken);
    CreateProcessInternalW_t pCreateProcessInternalW =
        (CreateProcessInternalW_t)GetProcAddress(kernelModule,
                                                 "CreateProcessInternalW");
    if (!pCreateProcessInternalW) {
        Wh_Log(L"No CreateProcessInternalW");
        return;
    }

    STARTUPINFO si{
        .cb = sizeof(STARTUPINFO),
        .dwFlags = STARTF_FORCEOFFFEEDBACK,
    };
    PROCESS_INFORMATION pi;
    if (!pCreateProcessInternalW(nullptr, currentProcessPath, commandLine,
                                 nullptr, nullptr, FALSE, NORMAL_PRIORITY_CLASS,
                                 nullptr, nullptr, &si, &pi, nullptr)) {
        Wh_Log(L"CreateProcess failed");
        return;
    }

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
}

void Wh_ModSettingsChanged() {
    if (g_isToolModProcessLauncher) {
        return;
    }

    WhTool_ModSettingsChanged();
}

void Wh_ModUninit() {
    if (g_isToolModProcessLauncher) {
        return;
    }

    WhTool_ModUninit();
    ExitProcess(0);
}
