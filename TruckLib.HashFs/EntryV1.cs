using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TruckLib.HashFs
{
    /// <summary>
    /// Represents the metadata of an entry in a HashFS v1 archive.
    /// </summary>
    public struct EntryV1 : IEntry, IBinarySerializable
    {
        /// <inheritdoc/>
        public ulong Hash { get; internal set; }

        /// <inheritdoc/>
        public ulong Offset { get; internal set; }

        /// <summary>
        /// CRC32 checksum of the file.
        /// </summary>
        public uint Crc { get; internal set; }

        /// <inheritdoc/>
        public uint Size { get; internal set; }

        /// <inheritdoc/>
        public uint CompressedSize { get; internal set; }

        /// <inheritdoc/>
        public bool IsDirectory 
        {
            get => Flags[0];
            set => Flags[0] = value;
        }

        /// <inheritdoc/>
        public bool IsCompressed
        {
            get => Flags[1];
            set => Flags[1] = value;
        }

        public bool Verify => Flags[2]; // TODO: What is this?

        public bool IsEncrypted => Flags[3];

        internal FlagField Flags;

        public void Deserialize(BinaryReader r, uint? version = null)
        {
            throw new NotImplementedException();
        }

        public void Serialize(BinaryWriter w)
        {
            w.Write(Hash);
            w.Write(Offset);
            w.Write(Flags.Bits);
            w.Write(Crc);
            w.Write(Size);
            w.Write(CompressedSize);
        }
    }
}
