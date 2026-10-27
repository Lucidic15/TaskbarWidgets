// The standalone host: runs a Windhawk mod (standalone/mods/*.wh.cpp) as an app of its own. It
// gives the mod Windhawk's API (windhawk_api.h): settings from a settings file, edited in a window
// made from the mod's settings block; storage in the registry; and a log. It starts and stops the
// mod as Windhawk would, and puts an icon in the notification area for the settings, starting with
// Windows, and exiting.

#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>

#include "windhawk_api.h"

// The mod's entry points (the same ones Windhawk calls): a tool mod's, or those of a mod Windhawk
// loads into Explorer.
#if WHHOST_TOOL_MOD
BOOL WhTool_ModInit();
void WhTool_ModSettingsChanged();
void WhTool_ModUninit();
#else
BOOL Wh_ModInit();
void Wh_ModSettingsChanged();
void Wh_ModUninit();
#endif

namespace {

bool StartMod() {
#if WHHOST_TOOL_MOD
    return WhTool_ModInit();
#else
    return Wh_ModInit();
#endif
}

void ModSettingsChanged() {
#if WHHOST_TOOL_MOD
    WhTool_ModSettingsChanged();
#else
    Wh_ModSettingsChanged();
#endif
}

void StopMod() {
#if WHHOST_TOOL_MOD
    WhTool_ModUninit();
#else
    Wh_ModUninit();
#endif
}

// ---------------------------------------------------------------------------
// Where the app keeps things: settings in %APPDATA%\TaskbarWidgets\<app>, files the mod keeps in
// %LOCALAPPDATA%\TaskbarWidgets\<app>, and its values in HKCU\Software\TaskbarWidgets\<app>.

const wchar_t kRegistryKey[] = L"Software\\TaskbarWidgets\\" WHHOST_APP;

std::wstring KnownFolder(REFKNOWNFOLDERID id) {
    PWSTR path = nullptr;
    std::wstring result;
    if (SUCCEEDED(SHGetKnownFolderPath(id, KF_FLAG_CREATE, nullptr, &path))) {
        result = path;
    }
    CoTaskMemFree(path);
    return result;
}

std::wstring AppFolder(REFKNOWNFOLDERID root) {
    std::wstring folder = KnownFolder(root) + L"\\TaskbarWidgets\\" WHHOST_APP;
    SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr);  // Fails harmlessly when it's there already.
    return folder;
}

const std::wstring& SettingsPath() {
    static const std::wstring path = AppFolder(FOLDERID_RoamingAppData) + L"\\settings.ini";
    return path;
}

std::wstring ExePath() {
    std::wstring path(MAX_PATH, L'\0');
    while (true) {
        DWORD length = GetModuleFileNameW(nullptr, path.data(), (DWORD)path.size());
        if (length < path.size()) {
            path.resize(length);
            return path;
        }
        path.resize(path.size() * 2);
    }
}

// ---------------------------------------------------------------------------
// The log: to the debugger always, and to log.txt next to the settings when started with --log.

bool g_logToFile = false;
CRITICAL_SECTION g_logLock;

}  // namespace

void WhHost_Log(PCWSTR format, ...) {
    WCHAR message[1024];
    va_list args;
    va_start(args, format);
    _vsnwprintf_s(message, _countof(message), _TRUNCATE, format, args);
    va_end(args);
    std::wstring line = std::wstring(L"[" WHHOST_APP L"] ") + message + L"\n";
    OutputDebugStringW(line.c_str());
    if (g_logToFile) {
        static const std::wstring path = AppFolder(FOLDERID_LocalAppData) + L"\\log.txt";
        SYSTEMTIME now;
        GetLocalTime(&now);
        WCHAR time[32];
        swprintf_s(time, L"%02d:%02d:%02d.%03d ", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds);
        std::wstring text = time + line;
        int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), (int)text.size(), nullptr, 0, nullptr, nullptr);
        std::string utf8(size, '\0');
        WideCharToMultiByte(CP_UTF8, 0, text.c_str(), (int)text.size(), utf8.data(), size, nullptr, nullptr);
        EnterCriticalSection(&g_logLock);
        HANDLE file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            DWORD written;
            WriteFile(file, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
            CloseHandle(file);
        }
        LeaveCriticalSection(&g_logLock);
    }
}

// ---------------------------------------------------------------------------
// Storage: the mod's values, in the registry.

int Wh_GetIntValue(PCWSTR valueName, int defaultValue) {
    DWORD value = 0;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, kRegistryKey, valueName, RRF_RT_REG_DWORD, nullptr, &value, &size) != ERROR_SUCCESS) {
        return defaultValue;
    }
    return (int)value;
}

BOOL Wh_SetIntValue(PCWSTR valueName, int value) {
    DWORD data = (DWORD)value;
    return RegSetKeyValueW(HKEY_CURRENT_USER, kRegistryKey, valueName, REG_DWORD, &data, sizeof(data)) == ERROR_SUCCESS;
}

size_t Wh_GetStringValue(PCWSTR valueName, PWSTR stringBuffer, size_t bufferChars) {
    if (bufferChars == 0) {
        return 0;
    }
    DWORD size = (DWORD)(bufferChars * sizeof(WCHAR));
    if (RegGetValueW(HKEY_CURRENT_USER, kRegistryKey, valueName, RRF_RT_REG_SZ, nullptr, stringBuffer, &size) != ERROR_SUCCESS) {
        stringBuffer[0] = L'\0';
        return 0;
    }
    return wcslen(stringBuffer);
}

BOOL Wh_SetStringValue(PCWSTR valueName, PCWSTR value) {
    return RegSetKeyValueW(HKEY_CURRENT_USER, kRegistryKey, valueName, REG_SZ, value, (DWORD)((wcslen(value) + 1) * sizeof(WCHAR))) ==
           ERROR_SUCCESS;
}

size_t Wh_GetBinaryValue(PCWSTR valueName, void* buffer, size_t bufferSize) {
    DWORD size = (DWORD)bufferSize;
    if (RegGetValueW(HKEY_CURRENT_USER, kRegistryKey, valueName, RRF_RT_REG_BINARY, nullptr, buffer, &size) != ERROR_SUCCESS) {
        return 0;
    }
    return size;
}

BOOL Wh_SetBinaryValue(PCWSTR valueName, const void* buffer, size_t bufferSize) {
    return RegSetKeyValueW(HKEY_CURRENT_USER, kRegistryKey, valueName, REG_BINARY, buffer, (DWORD)bufferSize) == ERROR_SUCCESS;
}

BOOL Wh_DeleteValue(PCWSTR valueName) {
    return RegDeleteKeyValueW(HKEY_CURRENT_USER, kRegistryKey, valueName) == ERROR_SUCCESS;
}

size_t Wh_GetModStoragePath(PWSTR pathBuffer, size_t bufferChars) {
    std::wstring folder = AppFolder(FOLDERID_LocalAppData) + L"\\Storage";
    SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr);
    if (bufferChars == 0 || folder.size() >= bufferChars) {
        if (bufferChars > 0) {
            pathBuffer[0] = L'\0';
        }
        return 0;
    }
    wcscpy_s(pathBuffer, bufferChars, folder.c_str());
    return folder.size();
}

BOOL Wh_SetFunctionHook(void* targetFunction, void* hookFunction, void** originalFunction) {
    return FALSE;
}

BOOL Wh_RemoveFunctionHook(void* targetFunction) {
    return FALSE;
}

BOOL Wh_ApplyHookOperations() {
    return FALSE;
}

// ---------------------------------------------------------------------------
// Settings: the mod's settings block (YAML, as Windhawk reads it) gives each setting's name,
// description, default and choices; the app's settings.ini has the values the user changed.

namespace {

struct Choice {
    std::wstring value;
    std::wstring label;
};

struct Setting {
    enum class Kind { Bool, Int, String };
    std::wstring key;
    Kind kind = Kind::String;
    std::wstring defaultValue;  // "true" or "false" for a Bool.
    std::wstring name;
    std::wstring description;
    std::vector<Choice> choices;  // When the value is one of them.
};

std::wstring Trim(const std::wstring& text) {
    size_t start = text.find_first_not_of(L" \t\r");
    size_t end = text.find_last_not_of(L" \t\r");
    return start == std::wstring::npos ? L"" : text.substr(start, end - start + 1);
}

// A YAML scalar: quotes taken off.
std::wstring Unquote(std::wstring text) {
    text = Trim(text);
    if (text.size() >= 2 && (text.front() == L'"' || text.front() == L'\'') && text.back() == text.front()) {
        std::wstring inner = text.substr(1, text.size() - 2);
        if (text.front() == L'"') {
            std::wstring unescaped;
            for (size_t i = 0; i < inner.size(); i++) {
                if (inner[i] == L'\\' && i + 1 < inner.size()) {
                    i++;
                }
                unescaped += inner[i];
            }
            return unescaped;
        }
        return inner;
    }
    return text;
}

// "key: value", split at the first ": ".
bool SplitKeyValue(const std::wstring& text, std::wstring& key, std::wstring& value) {
    size_t colon = text.find(L':');
    if (colon == std::wstring::npos) {
        return false;
    }
    key = Trim(text.substr(0, colon));
    value = Trim(text.substr(colon + 1));
    return !key.empty();
}

// The settings in the mod's settings block. Only the flat kind of setting these mods use: a top
// level "- key: default" with $name, $description and $options under it.
std::vector<Setting> ParseSettings(const wchar_t* yaml) {
    std::vector<Setting> settings;
    bool inOptions = false;
    const wchar_t* line = yaml;
    while (*line) {
        const wchar_t* end = wcschr(line, L'\n');
        std::wstring text = end ? std::wstring(line, end - line) : std::wstring(line);
        line = end ? end + 1 : line + text.size();
        size_t indent = text.find_first_not_of(L' ');
        if (indent == std::wstring::npos) {
            continue;
        }
        std::wstring body = Trim(text.substr(indent));
        std::wstring key;
        std::wstring value;
        if (indent == 0 && body.rfind(L"- ", 0) == 0 && SplitKeyValue(body.substr(2), key, value)) {
            Setting setting;
            setting.key = key;
            setting.name = key;
            std::wstring bare = Unquote(value);
            if (value == L"true" || value == L"false") {
                setting.kind = Setting::Kind::Bool;
            } else if (!value.empty() && value.find_first_not_of(L"-0123456789") == std::wstring::npos) {
                setting.kind = Setting::Kind::Int;
            }
            setting.defaultValue = bare;
            settings.push_back(setting);
            inOptions = false;
        } else if (!settings.empty() && body.rfind(L"- ", 0) == 0 && inOptions && SplitKeyValue(body.substr(2), key, value)) {
            settings.back().choices.push_back({Unquote(key), Unquote(value)});
        } else if (!settings.empty() && SplitKeyValue(body, key, value)) {
            inOptions = key == L"$options";
            if (key == L"$name") {
                settings.back().name = Unquote(value);
            } else if (key == L"$description") {
                settings.back().description = Unquote(value);
            }
        }
    }
    return settings;
}

const std::vector<Setting>& Settings() {
    static const std::vector<Setting> settings = ParseSettings(WHHOST_SETTINGS);
    return settings;
}

const Setting* FindSetting(const std::wstring& key) {
    for (const Setting& setting : Settings()) {
        if (setting.key == key) {
            return &setting;
        }
    }
    return nullptr;
}

const wchar_t kSettingsSection[] = L"settings";

// A setting's value: as saved, else its default.
std::wstring SettingValue(const std::wstring& key) {
    const Setting* setting = FindSetting(key);
    std::wstring fallback = setting ? setting->defaultValue : L"";
    std::wstring value(4096, L'\0');
    DWORD length = GetPrivateProfileStringW(kSettingsSection, key.c_str(), fallback.c_str(), value.data(), (DWORD)value.size(),
                                            SettingsPath().c_str());
    value.resize(length);
    return value;
}

void SaveSettingValue(const std::wstring& key, const std::wstring& value) {
    // Unicode (UTF-16) once the file starts that way, for folders with any letters in them.
    if (GetFileAttributesW(SettingsPath().c_str()) == INVALID_FILE_ATTRIBUTES) {
        HANDLE file = CreateFileW(SettingsPath().c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            const BYTE bom[] = {0xFF, 0xFE};
            DWORD written;
            WriteFile(file, bom, sizeof(bom), &written, nullptr);
            CloseHandle(file);
        }
    }
    // Quoted, so spaces at either end stay.
    std::wstring quoted = L"\"" + value + L"\"";
    WritePrivateProfileStringW(kSettingsSection, key.c_str(), quoted.c_str(), SettingsPath().c_str());
}

std::wstring FormatName(PCWSTR valueName, va_list args) {
    WCHAR name[512];
    _vsnwprintf_s(name, _countof(name), _TRUNCATE, valueName, args);
    return name;
}

}  // namespace

int Wh_GetIntSetting(PCWSTR valueName, ...) {
    va_list args;
    va_start(args, valueName);
    std::wstring value = SettingValue(FormatName(valueName, args));
    va_end(args);
    if (value == L"true") {
        return 1;
    }
    if (value == L"false") {
        return 0;
    }
    return _wtoi(value.c_str());
}

PCWSTR Wh_GetStringSetting(PCWSTR valueName, ...) {
    va_list args;
    va_start(args, valueName);
    std::wstring value = SettingValue(FormatName(valueName, args));
    va_end(args);
    return _wcsdup(value.c_str());
}

void Wh_FreeStringSetting(PCWSTR string) {
    free((void*)string);
}

namespace {

// ---------------------------------------------------------------------------
// Starting with Windows: the Run value the old app of the same name used, so this app takes its
// place.

const wchar_t kRunKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
const wchar_t kStartupApprovedKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

std::wstring StartupCommand() {
    return L"\"" + ExePath() + L"\"";
}

std::wstring RunValue() {
    WCHAR value[MAX_PATH * 2] = L"";
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, kRunKey, WHHOST_APP, RRF_RT_REG_SZ, nullptr, value, &size) != ERROR_SUCCESS) {
        return L"";
    }
    return value;
}

bool StartsWithWindows() {
    if (_wcsicmp(RunValue().c_str(), StartupCommand().c_str()) != 0) {
        return false;
    }
    // Task Manager's Startup apps can turn it off without removing it.
    BYTE state[12] = {};
    DWORD size = sizeof(state);
    if (RegGetValueW(HKEY_CURRENT_USER, kStartupApprovedKey, WHHOST_APP, RRF_RT_REG_BINARY, nullptr, state, &size) == ERROR_SUCCESS &&
        size > 0 && (state[0] & 1)) {
        return false;
    }
    return true;
}

void SetStartsWithWindows(bool start) {
    if (start) {
        std::wstring command = StartupCommand();
        RegSetKeyValueW(HKEY_CURRENT_USER, kRunKey, WHHOST_APP, REG_SZ, command.c_str(), (DWORD)((command.size() + 1) * sizeof(WCHAR)));
    } else {
        RegDeleteKeyValueW(HKEY_CURRENT_USER, kRunKey, WHHOST_APP);
    }
    RegDeleteKeyValueW(HKEY_CURRENT_USER, kStartupApprovedKey, WHHOST_APP);
}

// When Windows would start this app's name from where it's gone (moved, or the old app deleted),
// it starts this instead. One that's still there (the old app) is left until "Start with
// Windows" is chosen here.
void RepairStartup() {
    std::wstring value = RunValue();
    if (value.empty() || _wcsicmp(value.c_str(), StartupCommand().c_str()) == 0) {
        return;
    }
    std::wstring path = value;
    if (!path.empty() && path[0] == L'"') {
        size_t end = path.find(L'"', 1);
        path = path.substr(1, end == std::wstring::npos ? std::wstring::npos : end - 1);
    }
    if (GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) {
        std::wstring command = StartupCommand();
        RegSetKeyValueW(HKEY_CURRENT_USER, kRunKey, WHHOST_APP, REG_SZ, command.c_str(), (DWORD)((command.size() + 1) * sizeof(WCHAR)));
    }
}

// ---------------------------------------------------------------------------
// The settings window, made from the mod's settings block.

constexpr int kOkId = IDOK;
constexpr int kCancelId = IDCANCEL;
constexpr int kFirstControlId = 100;
constexpr int kFirstBrowseId = 500;

class SettingsWindow {
public:
    // Shows the window, or brings it to the front when it's open.
    static void Show() {
        if (instance_ && instance_->hwnd_) {
            SetForegroundWindow(instance_->hwnd_);
            return;
        }
        delete instance_;
        instance_ = new SettingsWindow();
        if (!instance_->Create()) {
            delete instance_;
            instance_ = nullptr;
        }
    }

    // For the message loop: Tab, Enter and Esc in the window.
    static bool HandleDialogMessage(MSG& message) {
        return instance_ && instance_->hwnd_ && IsDialogMessageW(instance_->hwnd_, &message);
    }

    static void Close() {
        if (instance_ && instance_->hwnd_) {
            DestroyWindow(instance_->hwnd_);
        }
    }

private:
    struct Row {
        const Setting* setting;
        HWND label = nullptr;  // For all but checkboxes, which have their own.
        HWND control = nullptr;
        HWND browse = nullptr;  // For folders.
        HWND description = nullptr;
    };

    static bool IsFolder(const Setting& setting) {
        return setting.kind == Setting::Kind::String && setting.choices.empty() && setting.key.size() > 6 &&
               setting.key.compare(setting.key.size() - 6, 6, L"Folder") == 0;
    }

    bool Create() {
        static const wchar_t kClass[] = L"TaskbarWidgets.Host.Settings";
        static bool registered = false;
        HINSTANCE instance = GetModuleHandleW(nullptr);
        if (!registered) {
            WNDCLASSEXW windowClass{sizeof(windowClass)};
            windowClass.lpfnWndProc = Proc;
            windowClass.hInstance = instance;
            windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            windowClass.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
            windowClass.lpszClassName = kClass;
            registered = RegisterClassExW(&windowClass) != 0;
        }
        // On the display with the mouse.
        POINT cursor;
        GetCursorPos(&cursor);
        HMONITOR monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        MONITORINFO info{sizeof(info)};
        GetMonitorInfoW(monitor, &info);
        std::wstring title = std::wstring(WHHOST_NAME) + L" settings";
        hwnd_ = CreateWindowExW(WS_EX_CONTROLPARENT, kClass, title.c_str(), WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
                                info.rcWork.left + 100, info.rcWork.top + 100, 100, 100, nullptr, nullptr, instance, this);
        if (!hwnd_) {
            return false;
        }
        HICON big = nullptr;
        HICON small = nullptr;
        LoadIconMetric(instance, MAKEINTRESOURCEW(1), LIM_LARGE, &big);
        LoadIconMetric(instance, MAKEINTRESOURCEW(1), LIM_SMALL, &small);
        SendMessageW(hwnd_, WM_SETICON, ICON_BIG, (LPARAM)big);
        SendMessageW(hwnd_, WM_SETICON, ICON_SMALL, (LPARAM)small);

        int id = kFirstControlId;
        int browseId = kFirstBrowseId;
        for (const Setting& setting : Settings()) {
            Row row{&setting};
            std::wstring value = SettingValue(setting.key);
            DWORD child = WS_CHILD | WS_VISIBLE;
            if (setting.kind == Setting::Kind::Bool) {
                row.control = CreateWindowExW(0, L"BUTTON", setting.name.c_str(), child | WS_TABSTOP | BS_AUTOCHECKBOX | BS_MULTILINE, 0, 0, 1,
                                              1, hwnd_, (HMENU)(INT_PTR)id++, instance, nullptr);
                Button_SetCheck(row.control, value == L"true" ? BST_CHECKED : BST_UNCHECKED);
            } else {
                row.label = CreateWindowExW(0, L"STATIC", setting.name.c_str(), child, 0, 0, 1, 1, hwnd_, nullptr, instance, nullptr);
                if (!setting.choices.empty()) {
                    row.control = CreateWindowExW(0, L"COMBOBOX", L"", child | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST, 0, 0, 1, 1, hwnd_,
                                                  (HMENU)(INT_PTR)id++, instance, nullptr);
                    for (size_t i = 0; i < setting.choices.size(); i++) {
                        ComboBox_AddString(row.control, setting.choices[i].label.c_str());
                        if (setting.choices[i].value == value) {
                            ComboBox_SetCurSel(row.control, (int)i);
                        }
                    }
                    if (ComboBox_GetCurSel(row.control) < 0) {
                        ComboBox_SetCurSel(row.control, 0);
                    }
                } else {
                    DWORD style = child | WS_TABSTOP | ES_AUTOHSCROLL | (setting.kind == Setting::Kind::Int ? ES_NUMBER : 0);
                    row.control = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", value.c_str(), style, 0, 0, 1, 1, hwnd_, (HMENU)(INT_PTR)id++,
                                                  instance, nullptr);
                    if (IsFolder(setting)) {
                        row.browse = CreateWindowExW(0, L"BUTTON", L"Browse…", child | WS_TABSTOP | BS_PUSHBUTTON, 0, 0, 1, 1, hwnd_,
                                                     (HMENU)(INT_PTR)browseId, instance, nullptr);
                    }
                }
            }
            browseId++;
            if (!setting.description.empty()) {
                row.description =
                    CreateWindowExW(0, L"STATIC", setting.description.c_str(), child, 0, 0, 1, 1, hwnd_, nullptr, instance, nullptr);
            }
            rows_.push_back(row);
        }
        ok_ = CreateWindowExW(0, L"BUTTON", L"OK", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON, 0, 0, 1, 1, hwnd_,
                              (HMENU)(INT_PTR)kOkId, instance, nullptr);
        cancel_ = CreateWindowExW(0, L"BUTTON", L"Cancel", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON, 0, 0, 1, 1, hwnd_,
                                  (HMENU)(INT_PTR)kCancelId, instance, nullptr);
        LayOut(GetDpiForWindow(hwnd_));
        ShowWindow(hwnd_, SW_SHOWNORMAL);
        SetForegroundWindow(hwnd_);
        if (!rows_.empty()) {
            SetFocus(rows_[0].control);
        }
        return true;
    }

    // Places everything for the display's scaling, and sizes the window to fit.
    void LayOut(UINT dpi) {
        dpi_ = dpi;
        auto px = [&](int value) { return MulDiv(value, (int)dpi, 96); };
        NONCLIENTMETRICSW metrics{sizeof(metrics)};
        SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, sizeof(metrics), &metrics, 0, dpi);
        if (font_) {
            DeleteObject(font_);
        }
        if (boldFont_) {
            DeleteObject(boldFont_);
        }
        font_ = CreateFontIndirectW(&metrics.lfMessageFont);
        LOGFONTW bold = metrics.lfMessageFont;
        bold.lfWeight = FW_SEMIBOLD;
        boldFont_ = CreateFontIndirectW(&bold);

        HDC dc = GetDC(hwnd_);
        HGDIOBJ previous = SelectObject(dc, font_);
        TEXTMETRICW text;
        GetTextMetricsW(dc, &text);
        int lineHeight = text.tmHeight;
        int margin = px(20);
        int width = px(460);
        int controlHeight = std::max(px(24), lineHeight + px(8));
        // How tall wrapped text is at the width.
        auto textHeight = [&](HWND control, int textWidth, HFONT font) {
            int length = GetWindowTextLengthW(control);
            std::wstring value(length + 1, L'\0');
            GetWindowTextW(control, value.data(), length + 1);
            SelectObject(dc, font);
            RECT bounds{0, 0, textWidth, 0};
            DrawTextW(dc, value.c_str(), length, &bounds, DT_CALCRECT | DT_WORDBREAK | DT_NOPREFIX);
            return (int)bounds.bottom;
        };
        int y = margin;
        for (Row& row : rows_) {
            if (row.label) {
                int height = textHeight(row.label, width, boldFont_);
                SetWindowPos(row.label, nullptr, margin, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
                SendMessageW(row.label, WM_SETFONT, (WPARAM)boldFont_, FALSE);
                y += height + px(6);
                int controlWidth = row.browse ? width - px(96) : width;
                // A drop-down list is as tall as its list when open.
                int height2 = row.setting->choices.empty() ? controlHeight : controlHeight + px(160);
                SetWindowPos(row.control, nullptr, margin, y, controlWidth, height2, SWP_NOZORDER | SWP_NOACTIVATE);
                if (row.browse) {
                    SetWindowPos(row.browse, nullptr, margin + width - px(88), y, px(88), controlHeight, SWP_NOZORDER | SWP_NOACTIVATE);
                    SendMessageW(row.browse, WM_SETFONT, (WPARAM)font_, FALSE);
                }
                y += controlHeight;
            } else {
                // A checkbox, with the setting's name beside it.
                int height = std::max(controlHeight, textHeight(row.control, width - px(24), boldFont_) + px(4));
                SetWindowPos(row.control, nullptr, margin, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
                SendMessageW(row.control, WM_SETFONT, (WPARAM)boldFont_, FALSE);
                y += height;
            }
            if (row.label) {
                SendMessageW(row.control, WM_SETFONT, (WPARAM)font_, FALSE);
            }
            if (row.description) {
                int indent = row.label ? 0 : px(20);
                int height = textHeight(row.description, width - indent, font_);
                y += px(4);
                SetWindowPos(row.description, nullptr, margin + indent, y, width - indent, height, SWP_NOZORDER | SWP_NOACTIVATE);
                SendMessageW(row.description, WM_SETFONT, (WPARAM)font_, FALSE);
                y += height;
            }
            y += px(18);
        }
        int buttonWidth = px(88);
        int buttonHeight = std::max(px(28), lineHeight + px(10));
        y += px(4);
        SetWindowPos(cancel_, nullptr, margin + width - buttonWidth, y, buttonWidth, buttonHeight, SWP_NOZORDER | SWP_NOACTIVATE);
        SetWindowPos(ok_, nullptr, margin + width - buttonWidth * 2 - px(8), y, buttonWidth, buttonHeight, SWP_NOZORDER | SWP_NOACTIVATE);
        SendMessageW(ok_, WM_SETFONT, (WPARAM)font_, FALSE);
        SendMessageW(cancel_, WM_SETFONT, (WPARAM)font_, FALSE);
        y += buttonHeight + margin;
        SelectObject(dc, previous);
        ReleaseDC(hwnd_, dc);

        RECT frame{0, 0, width + margin * 2, y};
        AdjustWindowRectExForDpi(&frame, (DWORD)GetWindowLongPtrW(hwnd_, GWL_STYLE), FALSE, (DWORD)GetWindowLongPtrW(hwnd_, GWL_EXSTYLE),
                                 dpi);
        SetWindowPos(hwnd_, nullptr, 0, 0, frame.right - frame.left, frame.bottom - frame.top, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        InvalidateRect(hwnd_, nullptr, TRUE);
    }

    void Save() {
        for (const Row& row : rows_) {
            const Setting& setting = *row.setting;
            std::wstring value;
            if (setting.kind == Setting::Kind::Bool) {
                value = Button_GetCheck(row.control) == BST_CHECKED ? L"true" : L"false";
            } else if (!setting.choices.empty()) {
                int choice = ComboBox_GetCurSel(row.control);
                value = choice >= 0 && choice < (int)setting.choices.size() ? setting.choices[choice].value : setting.defaultValue;
            } else {
                int length = GetWindowTextLengthW(row.control);
                value.assign(length + 1, L'\0');
                GetWindowTextW(row.control, value.data(), length + 1);
                value.resize(length);
            }
            SaveSettingValue(setting.key, value);
        }
        ModSettingsChanged();
    }

    void Browse(const Row& row) {
        IFileOpenDialog* dialog = nullptr;
        if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog)))) {
            return;
        }
        FILEOPENDIALOGOPTIONS options = 0;
        dialog->GetOptions(&options);
        dialog->SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
        dialog->SetTitle(row.setting->name.c_str());
        dialog->SetOkButtonLabel(L"Use this folder");
        int length = GetWindowTextLengthW(row.control);
        std::wstring current(length + 1, L'\0');
        GetWindowTextW(row.control, current.data(), length + 1);
        current.resize(length);
        if (!current.empty()) {
            WCHAR expanded[MAX_PATH * 2];
            ExpandEnvironmentStringsW(current.c_str(), expanded, _countof(expanded));
            IShellItem* folder = nullptr;
            if (SUCCEEDED(SHCreateItemFromParsingName(expanded, nullptr, IID_PPV_ARGS(&folder)))) {
                dialog->SetFolder(folder);
                folder->Release();
            }
        }
        IShellItem* chosen = nullptr;
        PWSTR path = nullptr;
        if (SUCCEEDED(dialog->Show(hwnd_)) && SUCCEEDED(dialog->GetResult(&chosen)) &&
            SUCCEEDED(chosen->GetDisplayName(SIGDN_FILESYSPATH, &path))) {
            SetWindowTextW(row.control, path);
            CoTaskMemFree(path);
        }
        if (chosen) {
            chosen->Release();
        }
        dialog->Release();
    }

    bool IsDescription(HWND control) const {
        for (const Row& row : rows_) {
            if (row.description == control) {
                return true;
            }
        }
        return false;
    }

    static LRESULT CALLBACK Proc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
        if (message == WM_NCCREATE) {
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, (LONG_PTR)((CREATESTRUCTW*)lParam)->lpCreateParams);
        }
        auto self = (SettingsWindow*)GetWindowLongPtrW(hwnd, GWLP_USERDATA);
        if (self) {
            switch (message) {
                case WM_COMMAND: {
                    int id = LOWORD(wParam);
                    if (id == kOkId) {
                        self->Save();
                        DestroyWindow(hwnd);
                    } else if (id == kCancelId) {
                        DestroyWindow(hwnd);
                    } else if (id >= kFirstBrowseId && id < kFirstBrowseId + (int)self->rows_.size()) {
                        self->Browse(self->rows_[id - kFirstBrowseId]);
                    }
                    return 0;
                }
                case WM_CLOSE:
                    DestroyWindow(hwnd);
                    return 0;
                case WM_CTLCOLORSTATIC: {
                    // On the window's own background; descriptions in gray.
                    HDC dc = (HDC)wParam;
                    SetBkColor(dc, GetSysColor(COLOR_WINDOW));
                    SetTextColor(dc, GetSysColor(self->IsDescription((HWND)lParam) ? COLOR_GRAYTEXT : COLOR_WINDOWTEXT));
                    return (LRESULT)GetSysColorBrush(COLOR_WINDOW);
                }
                case WM_DPICHANGED: {
                    auto suggested = (const RECT*)lParam;
                    SetWindowPos(hwnd, nullptr, suggested->left, suggested->top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    self->LayOut(HIWORD(wParam));
                    return 0;
                }
                case WM_NCDESTROY:
                    self->hwnd_ = nullptr;
                    if (self->font_) {
                        DeleteObject(self->font_);
                        self->font_ = nullptr;
                    }
                    if (self->boldFont_) {
                        DeleteObject(self->boldFont_);
                        self->boldFont_ = nullptr;
                    }
                    break;
            }
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    static inline SettingsWindow* instance_ = nullptr;
    HWND hwnd_ = nullptr;
    std::vector<Row> rows_;
    HWND ok_ = nullptr;
    HWND cancel_ = nullptr;
    HFONT font_ = nullptr;
    HFONT boldFont_ = nullptr;
    UINT dpi_ = 96;
};

// ---------------------------------------------------------------------------
// The icon in the notification area, on a hidden window that also hears from a second copy of the
// app.

const wchar_t kHostClass[] = L"TaskbarWidgets.Host." WHHOST_APP;
constexpr UINT kTrayMessage = WM_APP + 1;
constexpr UINT kOpenedAgainMessage = WM_APP + 2;
constexpr UINT kMenuSettings = 1;
constexpr UINT kMenuStartup = 2;
constexpr UINT kMenuExit = 3;

UINT g_taskbarCreatedMessage = 0;
HICON g_trayIcon = nullptr;

void UpdateTrayIcon(HWND hwnd, DWORD action, const wchar_t* balloon = nullptr) {
    NOTIFYICONDATAW data{sizeof(data)};
    data.hWnd = hwnd;
    data.uID = 1;
    data.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP | NIF_SHOWTIP;
    data.uCallbackMessage = kTrayMessage;
    data.hIcon = g_trayIcon;
    wcscpy_s(data.szTip, WHHOST_NAME);
    if (balloon) {
        data.uFlags |= NIF_INFO;
        wcscpy_s(data.szInfoTitle, WHHOST_NAME);
        wcscpy_s(data.szInfo, balloon);
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
    }
    Shell_NotifyIconW(action, &data);
    if (action == NIM_ADD) {
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, &data);
    }
}

void ShowTrayMenu(HWND hwnd, POINT point) {
    HMENU menu = CreatePopupMenu();
    std::wstring title = std::wstring(WHHOST_NAME) + L" " + WH_MOD_VERSION;
    AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, title.c_str());
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    if (!Settings().empty()) {
        AppendMenuW(menu, MF_STRING, kMenuSettings, L"Settings…");
    }
    AppendMenuW(menu, MF_STRING | (StartsWithWindows() ? MF_CHECKED : 0), kMenuStartup, L"Start with Windows");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, kMenuExit, L"Exit");
    SetForegroundWindow(hwnd);
    UINT command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.x, point.y, hwnd, nullptr);
    PostMessageW(hwnd, WM_NULL, 0, 0);
    DestroyMenu(menu);
    switch (command) {
        case kMenuSettings:
            SettingsWindow::Show();
            break;
        case kMenuStartup:
            SetStartsWithWindows(!StartsWithWindows());
            break;
        case kMenuExit:
            SettingsWindow::Close();
            PostQuitMessage(0);
            break;
    }
}

LRESULT CALLBACK HostProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case kTrayMessage:
            switch (LOWORD(lParam)) {
                case WM_CONTEXTMENU:
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    ShowTrayMenu(hwnd, {GET_X_LPARAM(wParam), GET_Y_LPARAM(wParam)});
                    break;
            }
            return 0;
        case kOpenedAgainMessage:
            // The app was opened again while running: show where it is.
            if (!Settings().empty()) {
                SettingsWindow::Show();
            } else {
                UpdateTrayIcon(hwnd, NIM_MODIFY, L"It's running. Right-click its icon here for its options.");
            }
            return 0;
        case WM_CLOSE:
            // Asked to close (by an installer, say): exit as the menu does.
            SettingsWindow::Close();
            PostQuitMessage(0);
            return 0;
        case WM_QUERYENDSESSION:
            return TRUE;
        case WM_ENDSESSION:
            if (wParam) {
                // Windows is shutting down: stop the mod the way exiting would.
                UpdateTrayIcon(hwnd, NIM_DELETE);
                StopMod();
                ExitProcess(0);
            }
            return 0;
    }
    if (message == g_taskbarCreatedMessage && g_taskbarCreatedMessage) {
        // Explorer restarted: the icon has to be added again.
        UpdateTrayIcon(hwnd, NIM_ADD);
        return 0;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

bool MutexExists(const wchar_t* name) {
    HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, name);
    if (mutex) {
        CloseHandle(mutex);
        return true;
    }
    return false;
}

// Whether Windhawk runs the same mod: a tool mod's process has a mutex named after it, and a mod
// loaded into Explorer has its widget window inside the taskbar.
bool ModRunningInWindhawk() {
    if (MutexExists(L"windhawk-tool-mod_" WH_MOD_ID)) {
        return true;
    }
    // In Explorer's taskbar, which isn't always the first Shell_TrayWnd (taskbar replacements such as
    // RetroBar make one too).
    for (HWND taskbar = FindWindowExW(nullptr, nullptr, L"Shell_TrayWnd", nullptr); *WHHOST_WIDGET_CLASS && taskbar;
         taskbar = FindWindowExW(nullptr, taskbar, L"Shell_TrayWnd", nullptr)) {
        if (FindWindowExW(taskbar, nullptr, WHHOST_WIDGET_CLASS, nullptr)) {
            return true;
        }
    }
    return false;
}

}  // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    // The mods work in physical pixels, as Windhawk does.
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    InitializeCriticalSection(&g_logLock);
    g_logToFile = wcsstr(GetCommandLineW(), L"--log") != nullptr;

    // One copy at a time: opened again, the running one shows itself.
    HANDLE single = CreateMutexW(nullptr, TRUE, L"Local\\TaskbarWidgets." WHHOST_APP);
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        if (HWND running = FindWindowW(kHostClass, nullptr)) {
            AllowSetForegroundWindow(ASFW_ANY);
            PostMessageW(running, kOpenedAgainMessage, 0, 0);
        }
        return 0;
    }
    // Not alongside the same widget from Windhawk, or from the old app.
    if (ModRunningInWindhawk()) {
        MessageBoxW(nullptr,
                    WHHOST_NAME L" is already running as a Windhawk mod. To use this app instead, turn the mod off in Windhawk, then "
                                L"open this again.",
                    WHHOST_NAME, MB_OK | MB_ICONINFORMATION);
        return 0;
    }
    if (*WHHOST_OLD_APP_MUTEX && MutexExists(WHHOST_OLD_APP_MUTEX)) {
        MessageBoxW(nullptr,
                    L"The old " WHHOST_APP L" app is running. Exit it (right-click its icon in the notification area, then Exit), "
                    L"then open this again. This one takes its place, settings and all.",
                    WHHOST_NAME, MB_OK | MB_ICONINFORMATION);
        return 0;
    }

    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES};
    InitCommonControlsEx(&controls);
    RepairStartup();

    WNDCLASSEXW windowClass{sizeof(windowClass)};
    windowClass.lpfnWndProc = HostProc;
    windowClass.hInstance = instance;
    windowClass.lpszClassName = kHostClass;
    RegisterClassExW(&windowClass);
    HWND host = CreateWindowExW(0, kHostClass, WHHOST_NAME, WS_POPUP, 0, 0, 0, 0, nullptr, nullptr, instance, nullptr);
    g_taskbarCreatedMessage = RegisterWindowMessageW(L"TaskbarCreated");
    ChangeWindowMessageFilterEx(host, g_taskbarCreatedMessage, MSGFLT_ALLOW, nullptr);

    if (!StartMod()) {
        MessageBoxW(nullptr, WHHOST_NAME L" couldn't start.", WHHOST_NAME, MB_OK | MB_ICONERROR);
        return 1;
    }
    LoadIconMetric(instance, MAKEINTRESOURCEW(1), LIM_SMALL, &g_trayIcon);
    UpdateTrayIcon(host, NIM_ADD);

    MSG message;
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        if (SettingsWindow::HandleDialogMessage(message)) {
            continue;
        }
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    UpdateTrayIcon(host, NIM_DELETE);
    StopMod();
    DestroyWindow(host);
    CoUninitialize();
    CloseHandle(single);
    return 0;
}
