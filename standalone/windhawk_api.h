// Windhawk's mod API, as the standalone host provides it (host.cpp). tools/build.py includes this
// in place of Windhawk's own header when it compiles a mod (mods/*.wh.cpp) into an app of its own,
// so the mod's code is the same in both.
#pragma once

#include <windows.h>

// WH_MOD_ID and the app's name, version and settings, written by tools/build.py for each mod.
#include "mod_info.h"

void WhHost_Log(PCWSTR format, ...);
#define Wh_Log(message, ...) WhHost_Log(L"[%d:%S]: " message, __LINE__, __FUNCTION__, ##__VA_ARGS__)

// The mod's storage: values kept in the registry.
int Wh_GetIntValue(PCWSTR valueName, int defaultValue);
BOOL Wh_SetIntValue(PCWSTR valueName, int value);
size_t Wh_GetStringValue(PCWSTR valueName, PWSTR stringBuffer, size_t bufferChars);
BOOL Wh_SetStringValue(PCWSTR valueName, PCWSTR value);
size_t Wh_GetBinaryValue(PCWSTR valueName, void* buffer, size_t bufferSize);
BOOL Wh_SetBinaryValue(PCWSTR valueName, const void* buffer, size_t bufferSize);
BOOL Wh_DeleteValue(PCWSTR valueName);
size_t Wh_GetModStoragePath(PWSTR pathBuffer, size_t bufferChars);

// The mod's settings: from the app's settings file, else the defaults in the mod's settings block.
int Wh_GetIntSetting(PCWSTR valueName, ...);
PCWSTR Wh_GetStringSetting(PCWSTR valueName, ...);
void Wh_FreeStringSetting(PCWSTR string);

// Hooking other code isn't needed by tool mods run on their own; only the Windhawk boilerplate
// refers to it.
BOOL Wh_SetFunctionHook(void* targetFunction, void* hookFunction, void** originalFunction);
BOOL Wh_RemoveFunctionHook(void* targetFunction);
BOOL Wh_ApplyHookOperations();
