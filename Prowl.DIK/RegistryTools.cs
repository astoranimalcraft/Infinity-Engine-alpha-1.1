// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Infinity.DIK;

/// <summary>Root keys available to <see cref="RegistryTools"/>.</summary>
public enum RegistryHive
{
    ClassesRoot,
    CurrentUser,
    LocalMachine,
    Users,
    CurrentConfig,
}

/// <summary>Value types used by the registry helpers.</summary>
public enum RegistryValueType
{
    String,
    ExpandString,
    DWord,
    QWord,
    Binary,
    MultiString,
    Unknown,
}

/// <summary>One registry value.</summary>
public sealed record RegistryValue(string Name, RegistryValueType Type, object? Data);
/// <summary>One program that starts with Windows. Read-only.</summary>
public sealed record RegistryStartupEntry(RegistryHive Hive, string Key, string Name, string Value);


/// <summary>
/// Reads and writes the Windows registry through advapi32, so the library does not need a Windows-only package and
/// still compiles on Linux. On other platforms every method reports <see cref="IsSupported"/> false and returns a
/// safe failure.
/// </summary>
public static class RegistryTools
{
    private const int KeyRead = 0x20019;
    private const int KeyWrite = 0x20006;
    private const int KeyWow6464 = 0x0100;
    private const int RegSz = 1;
    private const int RegExpandSz = 2;
    private const int RegBinary = 3;
    private const int RegDword = 4;
    private const int RegMultiSz = 7;
    private const int RegQword = 11;

    private static readonly IntPtr HkeyClassesRoot = new(unchecked((int)0x80000000));
    private static readonly IntPtr HkeyCurrentUser = new(unchecked((int)0x80000001));
    private static readonly IntPtr HkeyLocalMachine = new(unchecked((int)0x80000002));
    private static readonly IntPtr HkeyUsers = new(unchecked((int)0x80000003));
    private static readonly IntPtr HkeyCurrentConfig = new(unchecked((int)0x80000005));

    /// <summary>True on Windows, where the registry exists.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Reads a string value, or null.</summary>
    public static string? ReadString(RegistryHive hive, string subKey, string valueName)
    {
        object? value = ReadValue(hive, subKey, valueName);
        return value as string;
    }

    /// <summary>Reads a DWORD value, or null.</summary>
    public static int? ReadInt(RegistryHive hive, string subKey, string valueName)
    {
        object? value = ReadValue(hive, subKey, valueName);
        return value is int number ? number : null;
    }

    /// <summary>Reads a QWORD value, or null.</summary>
    public static long? ReadLong(RegistryHive hive, string subKey, string valueName)
    {
        object? value = ReadValue(hive, subKey, valueName);
        return value is long number ? number : null;
    }

    /// <summary>Reads any supported value, or null when the key or value is missing.</summary>
    public static object? ReadValue(RegistryHive hive, string subKey, string valueName)
    {
        if (!IsSupported) return null;
        IntPtr key = IntPtr.Zero;
        try
        {
            if (RegOpenKeyExW(HiveHandle(hive), subKey, 0, KeyRead | KeyWow6464, out key) != 0) return null;
            return ReadValueCore(key, valueName);
        }
        catch { return null; }
        finally { if (key != IntPtr.Zero) RegCloseKey(key); }
    }

    /// <summary>Writes a string value.</summary>
    public static bool WriteString(RegistryHive hive, string subKey, string valueName, string value, bool expand = false)
        => WriteBytes(hive, subKey, valueName, Encoding.Unicode.GetBytes(value + "\0"), expand ? RegExpandSz : RegSz);

    /// <summary>Writes a DWORD value.</summary>
    public static bool WriteInt(RegistryHive hive, string subKey, string valueName, int value)
        => WriteBytes(hive, subKey, valueName, BitConverter.GetBytes(value), RegDword);

    /// <summary>Writes a QWORD value.</summary>
    public static bool WriteLong(RegistryHive hive, string subKey, string valueName, long value)
        => WriteBytes(hive, subKey, valueName, BitConverter.GetBytes(value), RegQword);

    /// <summary>Writes a binary value.</summary>
    public static bool WriteBytes(RegistryHive hive, string subKey, string valueName, byte[] value, int type = RegBinary)
    {
        if (!IsSupported) return false;
        IntPtr key = IntPtr.Zero;
        try
        {
            if (RegCreateKeyExW(HiveHandle(hive), subKey, 0, null, 0, KeyWrite | KeyWow6464, IntPtr.Zero, out key, out _) != 0)
                return false;
            return RegSetValueExW(key, valueName, 0, type, value, value.Length) == 0;
        }
        catch { return false; }
        finally { if (key != IntPtr.Zero) RegCloseKey(key); }
    }

    /// <summary>Deletes one value.</summary>
    public static bool DeleteValue(RegistryHive hive, string subKey, string valueName)
    {
        if (!IsSupported) return false;
        IntPtr key = IntPtr.Zero;
        try
        {
            if (RegOpenKeyExW(HiveHandle(hive), subKey, 0, KeyWrite | KeyWow6464, out key) != 0) return false;
            return RegDeleteValueW(key, valueName) == 0;
        }
        catch { return false; }
        finally { if (key != IntPtr.Zero) RegCloseKey(key); }
    }

    /// <summary>Deletes a key. When <paramref name="recursive"/> is false Windows deletes it only when empty.</summary>
    public static bool DeleteKey(RegistryHive hive, string subKey, bool recursive = false)
    {
        if (!IsSupported) return false;
        try
        {
            if (!recursive) return RegDeleteKeyExW(HiveHandle(hive), subKey, KeyWow6464, 0) == 0;

            foreach (string child in GetSubKeyNames(hive, subKey))
                DeleteKey(hive, subKey + "\\" + child, true);
            return RegDeleteKeyExW(HiveHandle(hive), subKey, KeyWow6464, 0) == 0;
        }
        catch { return false; }
    }

    /// <summary>Lists child key names.</summary>
    public static IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey)
    {
        if (!IsSupported) return [];
        var names = new List<string>();
        IntPtr key = IntPtr.Zero;
        try
        {
            if (RegOpenKeyExW(HiveHandle(hive), subKey, 0, KeyRead | KeyWow6464, out key) != 0) return names;
            var buffer = new StringBuilder(512);
            for (int index = 0; ; index++)
            {
                int length = buffer.Capacity;
                int result = RegEnumKeyExW(key, index, buffer, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (result != 0) break;
                names.Add(buffer.ToString(0, length));
            }
        }
        catch { }
        finally { if (key != IntPtr.Zero) RegCloseKey(key); }
        return names;
    }

    /// <summary>
    /// Reads the usual Run and RunOnce keys for the current user and the machine. It never writes or
    /// deletes anything, so a game can show a believable startup list without touching the real system.
    /// </summary>
    public static IReadOnlyList<RegistryStartupEntry> GetStartupEntries()
    {
        if (!IsSupported) return [];

        string[] keys =
        [
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
            @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
        ];

        var entries = new List<RegistryStartupEntry>();
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (string key in keys)
            {
                foreach (RegistryValue value in GetValues(hive, key))
                {
                    string text = value.Data switch
                    {
                        string s => s,
                        string[] parts => string.Join(" ", parts),
                        null => "",
                        _ => value.Data.ToString() ?? "",
                    };
                    entries.Add(new RegistryStartupEntry(hive, key, value.Name, text));
                }
            }
        }
        return entries;
    }

    /// <summary>Lists every value in a key.</summary>
    public static IReadOnlyList<RegistryValue> GetValues(RegistryHive hive, string subKey)
    {
        if (!IsSupported) return [];
        var values = new List<RegistryValue>();
        IntPtr key = IntPtr.Zero;
        try
        {
            if (RegOpenKeyExW(HiveHandle(hive), subKey, 0, KeyRead | KeyWow6464, out key) != 0) return values;
            var name = new StringBuilder(512);
            for (int index = 0; ; index++)
            {
                int nameLength = name.Capacity;
                int dataLength = 0;
                int type = 0;
                int result = RegEnumValueW(key, index, name, ref nameLength, IntPtr.Zero, ref type, null, ref dataLength);
                if (result != 0) break;

                byte[] data = new byte[Math.Max(0, dataLength)];
                result = RegEnumValueW(key, index, name, ref nameLength, IntPtr.Zero, ref type, data, ref dataLength);
                if (result != 0) break;

                string valueName = name.ToString(0, nameLength);
                values.Add(new RegistryValue(valueName, KindOf(type), Decode(type, data, dataLength)));
            }
        }
        catch { }
        finally { if (key != IntPtr.Zero) RegCloseKey(key); }
        return values;
    }

    private static object? ReadValueCore(IntPtr key, string valueName)
    {
        int type = 0;
        int length = 0;
        int result = RegQueryValueExW(key, valueName, IntPtr.Zero, out type, null, ref length);
        if (result != 0 || length <= 0) return null;

        byte[] data = new byte[length];
        result = RegQueryValueExW(key, valueName, IntPtr.Zero, out type, data, ref length);
        return result == 0 ? Decode(type, data, length) : null;
    }

    private static object? Decode(int type, byte[] data, int length)
    {
        if (length <= 0) return null;
        switch (type)
        {
            case RegSz:
            case RegExpandSz:
                return Encoding.Unicode.GetString(data, 0, length).TrimEnd('\0');
            case RegDword:
                return length >= 4 ? BitConverter.ToInt32(data, 0) : null;
            case RegQword:
                return length >= 8 ? BitConverter.ToInt64(data, 0) : null;
            case RegMultiSz:
                return Encoding.Unicode.GetString(data, 0, length).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            case RegBinary:
                return data[..Math.Min(length, data.Length)];
            default:
                return data[..Math.Min(length, data.Length)];
        }
    }

    private static RegistryValueType KindOf(int type) => type switch
    {
        RegSz => RegistryValueType.String,
        RegExpandSz => RegistryValueType.ExpandString,
        RegDword => RegistryValueType.DWord,
        RegQword => RegistryValueType.QWord,
        RegBinary => RegistryValueType.Binary,
        RegMultiSz => RegistryValueType.MultiString,
        _ => RegistryValueType.Unknown,
    };

    private static IntPtr HiveHandle(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => HkeyClassesRoot,
        RegistryHive.CurrentUser => HkeyCurrentUser,
        RegistryHive.LocalMachine => HkeyLocalMachine,
        RegistryHive.Users => HkeyUsers,
        RegistryHive.CurrentConfig => HkeyCurrentConfig,
        _ => HkeyCurrentUser,
    };

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegCreateKeyExW(IntPtr hKey, string lpSubKey, int reserved, string? lpClass,
        int dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryValueExW(IntPtr hKey, string? lpValueName, IntPtr lpReserved,
        out int lpType, byte[]? lpData, ref int lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegSetValueExW(IntPtr hKey, string lpValueName, int reserved,
        int dwType, byte[] lpData, int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteValueW(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteKeyExW(IntPtr hKey, string lpSubKey, int samDesired, int reserved);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegEnumKeyExW(IntPtr hKey, int dwIndex, StringBuilder lpName, ref int lpcchName,
        IntPtr lpReserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegEnumValueW(IntPtr hKey, int dwIndex, StringBuilder lpValueName, ref int lpcchName,
        IntPtr lpReserved, ref int lpType, byte[]? lpData, ref int lpcbData);
}
