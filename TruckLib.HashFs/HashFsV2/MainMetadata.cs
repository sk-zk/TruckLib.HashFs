using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static TruckLib.HashFs.HashFsV2.Consts;

namespace TruckLib.HashFs.HashFsV2
{
    internal struct MainMetadata : IBinarySerializable
    {
        public uint CompressedSize;

        public uint Size;

        public uint Unknown;

        public uint OffsetBlock;

        public readonly ulong Offset => OffsetBlock * BlockSize;

        private FlagField flags1;

        public FlagField Flags2;

        public bool IsCompressed
        {
            get => flags1[4];
            set => flags1[4] = value;
        }

        public void Deserialize(BinaryReader r, uint? version = null)
        {
            var compressedSizeBytes = r.ReadBytes(3);
            var compressedSizeMsbAndFlags = r.ReadByte();
            CompressedSize = (uint)(
                compressedSizeBytes[0]
                + (compressedSizeBytes[1] << 8)
                + (compressedSizeBytes[2] << 16)
                + ((compressedSizeMsbAndFlags & 0x0F) << 24)
                );
            flags1 = new FlagField(compressedSizeMsbAndFlags & 0xF0u);

            var sizeBytes = r.ReadBytes(3);
            var sizeMsbAndFlags = r.ReadByte();
            Size = (uint)(
                sizeBytes[0]
                + (sizeBytes[1] << 8)
                + (sizeBytes[2] << 16)
                + ((sizeMsbAndFlags & 0x0F) << 24)
                );
            Flags2 = new FlagField(sizeMsbAndFlags & 0xF0u);

            Unknown = r.ReadUInt32();
            OffsetBlock = r.ReadUInt32();
        }

        public void Serialize(BinaryWriter w)
        {
            w.Write((byte)CompressedSize);
            w.Write((byte)(CompressedSize >> 8));
            w.Write((byte)(CompressedSize >> 16));
            w.Write((byte)((byte)(CompressedSize >> 24 & 0x0F) | (flags1.Bits & 0xF0)));

            w.Write((byte)Size);
            w.Write((byte)(Size >> 8));
            w.Write((byte)(Size >> 16));
            w.Write((byte)((byte)(Size >> 24 & 0x0F) | (Flags2.Bits & 0xF0)));

            w.Write(Unknown);
            w.Write(OffsetBlock);
        }
    }
}
