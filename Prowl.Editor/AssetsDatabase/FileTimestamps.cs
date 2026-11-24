// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Infinity.Editor;

/// <summary>
/// Creation time as the filesystem knows it. <see cref="File.GetCreationTimeUtc"/> maps to the last write time on
/// Unix, which is identical for a file and a copy of it (copy preserves mtime), so duplicate GUID resolution could
/// not tell an original from its copy. Linux exposes the birth time through statx; Windows already does through the
/// managed API.
/// </summary>
internal static class FileTimestamps
{
    private const int AtFdCwd = -100;
    private const uint StatxBtime = 0x0000_0800;
    private const uint StatxCtime = 0x0000_0040;

    public static DateTime GetCreatedOrChangedUtc(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            if (TryStatx(path, StatxBtime, out DateTime birth)) return birth;
            if (TryStatx(path, StatxCtime, out DateTime changed)) return changed;
        }

        return File.GetCreationTimeUtc(path);
    }

    private static bool TryStatx(string path, uint wantedMask, out DateTime time)
    {
        time = default;
        try
        {
            if (Statx(AtFdCwd, path, 0, wantedMask, out StatxBuffer buffer) != 0) return false;
            if ((buffer.Mask & wantedMask) == 0) return false;

            StatxTimestamp stamp = wantedMask == StatxBtime ? buffer.BirthTime : buffer.ChangeTime;
            time = DateTimeOffset.FromUnixTimeSeconds(stamp.Seconds).UtcDateTime.AddTicks(stamp.Nanoseconds / 100);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "statx")]
    private static extern int Statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags, uint mask,
        out StatxBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct StatxTimestamp
    {
        public long Seconds;
        public uint Nanoseconds;
        private readonly int _reserved;
    }

    // Linux's struct statx, which is a fixed 256 bytes on every architecture.
    [StructLayout(LayoutKind.Sequential)]
    private struct StatxBuffer
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        private readonly ushort _spare0;
        public ulong Inode;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public StatxTimestamp AccessTime;
        public StatxTimestamp BirthTime;
        public StatxTimestamp ChangeTime;
        public StatxTimestamp ModifyTime;
        public uint RDeviceMajor;
        public uint RDeviceMinor;
        public uint DeviceMajor;
        public uint DeviceMinor;
        public ulong MountId;
        public uint DirectIoMemoryAlignment;
        public uint DirectIoOffsetAlignment;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        private ulong[] _spare3;
    }
}
