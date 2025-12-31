using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TruckLib.HashFs.HashFsV2
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct EntryTableEntry
    {
        [FieldOffset(0)]
        public ulong Hash;

        [FieldOffset(8)]
        public uint MetadataIndex;

        [FieldOffset(12)]
        public ushort MetadataCount;

        [FieldOffset(14)]
        public ushort Flags;
    }
}
