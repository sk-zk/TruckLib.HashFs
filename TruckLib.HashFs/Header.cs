using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs
{
    internal abstract class Header
    {
        public const uint Magic = 0x23534353; // "SCS#"
        public ushort Version { get; set; }
        public ushort Salt { get; set; }
        public string HashMethod { get; set; }
        public uint NumEntries { get; set; }

        public static Header Deserialize(BinaryReader r)
        {
            var magic = r.ReadUInt32();
            if ((magic & 0xFFFF) == 0x4B50) // "PK"
                throw new InvalidDataException("This is a zip file.");
            if (magic != Magic)
                throw new InvalidDataException("Probably not a HashFS file.");

            var version = r.ReadUInt16();

            switch (version)
            {
                case 1:
                    var h1 = new HeaderV1();
                    h1.Version = version;
                    h1.Salt = r.ReadUInt16();
                    h1.HashMethod = new string(r.ReadChars(4));
                    h1.NumEntries = r.ReadUInt32();
                    h1.StartOffset = r.ReadUInt32();
                    return h1;
                case 2:
                    var h2 = new HeaderV2();
                    h2.Version = version;
                    h2.Salt = r.ReadUInt16();
                    h2.HashMethod = new string(r.ReadChars(4));
                    h2.NumEntries = r.ReadUInt32();
                    h2.EntryTableLength = r.ReadUInt32();
                    h2.NumMetadataEntries = r.ReadUInt32();
                    h2.MetadataTableLength = r.ReadUInt32();
                    h2.EntryTableStart = r.ReadUInt64();
                    h2.MetadataTableStart = r.ReadUInt64();
                    h2.SecurityDescriptorOffset = r.ReadUInt32();
                    h2.Platform = (Platform)r.ReadByte();
                    return h2;
                default:
                    throw new NotSupportedException($"HashFS version {version} is not supported");
            }
        }
    }

    internal class HeaderV1 : Header
    {
        public uint StartOffset { get; set; }
    }

    internal class HeaderV2 : Header
    {
        public uint EntryTableLength { get; set; }
        public uint NumMetadataEntries { get; set; }
        public uint MetadataTableLength { get; set; }
        public ulong EntryTableStart { get; set; }
        public ulong MetadataTableStart { get; set; }
        public uint SecurityDescriptorOffset { get; set; }
        public Platform Platform { get; set; }
    }
}
