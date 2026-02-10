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
        public ulong Offset { get; set; }

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
            get => flags[0];
            set => flags[0] = value;
        }

        /// <inheritdoc/>
        public bool IsCompressed
        {
            get => flags[1];
            set => flags[1] = value;
        }

        public bool Verify => flags[2]; // TODO: What is this?

        public bool IsEncrypted => flags[3];

        private FlagField flags;

        public FlagField Flags
        {
            get => flags;
            internal set 
            { 
                flags = value; 
            }
        }

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
