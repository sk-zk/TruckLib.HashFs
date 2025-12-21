using GisDeflate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TruckLib.HashFs.Dds;

namespace TruckLib.HashFs
{
    internal class HashFsV2Reader : HashFsReaderBase
    {
        /// <summary>
        /// The header of the archive.
        /// </summary>
        internal required HeaderV2 Header { get; init; }

        public override ushort Version => 2;

        public override ushort Salt
        {
            get => Header.Salt;
            set => Header.Salt = value;
        }

        public Platform Platform => Header.Platform;

        /// <summary>
        /// The character used in directory listing files to indicate that
        /// an element is a directory rather than a file.
        /// </summary>
        internal const char DirMarker = '/';

        /// <inheritdoc/>
        public override DirectoryListing GetDirectoryListing(
            IEntry entry, bool filesOnly = false)
        {
            var arr = GetEntryContent(entry);
            using var ms = new MemoryStream(arr);
            using var dirReader = new BinaryReader(ms);

            var count = dirReader.ReadUInt32();
            var stringLengths = dirReader.ReadBytes((int)count);

            var subdirs = new List<string>();
            var files = new List<string>();

            for (int i = 0; i < count; i++)
            {
                var str = Encoding.UTF8.GetString(dirReader.ReadBytes(stringLengths[i]));
                // is directory
                if (str.StartsWith(DirMarker))
                {
                    if (!filesOnly)
                        subdirs.Add(str[1..]);
                }
                // is file
                else
                {
                    files.Add(str);
                }
            }

            return new DirectoryListing(subdirs, files);
        }

        /// <inheritdoc/>
        public override byte[][] Extract(IEntry entry, string path)
        {
            if (!Entries.ContainsValue(entry))
                throw new FileNotFoundException();

            if (entry is EntryV2 v2 && v2.TobjMetadata != null)
            {
                using var tobjMs = new MemoryStream();
                RecreateTobj(v2, path, tobjMs);

                using var ddsMs = new MemoryStream();
                RecreateDds(v2, ddsMs);

                return [tobjMs.ToArray(), ddsMs.ToArray()];
            }
            else
            {
                return [GetEntryContent(entry)];
            }
        }

        /// <inheritdoc/>
        public override void ExtractToFile(IEntry entry, string entryPath, string outputPath)
        {
            if (entry.IsDirectory)
            {
                throw new ArgumentException("This is a directory.", nameof(entry));
            }

            if (entry.Size == 0)
            {
                // create an empty file
                File.Create(outputPath).Dispose();
                return;
            }

            Reader.BaseStream.Position = (long)entry.Offset;
            using var fileStream = new FileStream(outputPath, FileMode.Create);
            if (entry is EntryV2 v2 && v2.TobjMetadata != null)
            {
                RecreateTobj(v2, entryPath, fileStream);
                using var ddsFileStream = new FileStream(
                    Path.ChangeExtension(outputPath, "dds"),
                    FileMode.Create);
                RecreateDds(v2, ddsFileStream);
            }
            else if (entry.IsCompressed)
            {
                using var zlibStream = new ZLibStream(Reader.BaseStream, CompressionMode.Decompress, true);
                CopyStream(zlibStream, fileStream, entry.Size);
            }
            else
            {
                CopyStream(Reader.BaseStream, fileStream, entry.Size);
            }
        }

        private static void RecreateTobj(EntryV2 entry, string tobjPath, Stream stream)
        {
            using var w = new BinaryWriter(stream);
            var tobj = entry.TobjMetadata.Value.AsTobj(tobjPath);
            tobj.Serialize(w);
        }

        private void RecreateDds(EntryV2 entry, Stream stream)
        {
            var dds = new DdsFile();
            dds.Header = new DdsHeader()
            {
                IsCapsValid = true,
                IsHeightValid = true,
                IsWidthValid = true,
                IsPixelFormatValid = true,
                CapsTexture = true,
                Width = (uint)entry.TobjMetadata.Value.TextureWidth,
                Height = (uint)entry.TobjMetadata.Value.TextureHeight,
                IsMipMapCountValid = entry.TobjMetadata.Value.MipmapCount > 0,
                MipMapCount = entry.TobjMetadata.Value.MipmapCount,
            };
            dds.Header.PixelFormat = new DdsPixelFormat()
            {
                FourCC = DdsPixelFormat.FourCC_DX10,
                HasCompressedRgbData = true,
            };
            dds.HeaderDxt10 = new DdsHeaderDxt10()
            {
                Format = entry.TobjMetadata.Value.Format,
                ArraySize = 1,
                ResourceDimension = D3d10ResourceDimension.Texture2d,
            };

            if (entry.TobjMetadata.Value.MipmapCount > 1)
            {
                dds.Header.IsMipMapCountValid = true;
                dds.Header.CapsMipMap = true;
                dds.Header.CapsComplex = true;
            }

            if (entry.TobjMetadata.Value.IsCube)
            {
                dds.Header.CapsComplex = true;
                dds.Header.Caps2Cubemap = true;
                dds.Header.Caps2CubemapPositiveX = true;
                dds.Header.Caps2CubemapNegativeX = true;
                dds.Header.Caps2CubemapPositiveY = true;
                dds.Header.Caps2CubemapNegativeY = true;
                dds.Header.Caps2CubemapPositiveZ = true;
                dds.Header.Caps2CubemapNegativeZ = true;
                dds.HeaderDxt10.MiscFlag = D3d10ResourceMiscFlag.TextureCube;
            }

            var data = GetEntryContent(entry);
            if (entry.IsCompressed)
            {
                data = GDeflate.Decompress(data);
            }
            dds.Data = DdsUtils.ConvertDecompBytesToDdsBytes(entry, dds, data);

            using var w = new BinaryWriter(stream);
            dds.Serialize(w);
        }

        internal void ParseEntries()
        {
            var entryTable = ReadEntryTable();
            ReadMetadataTableAndCreateEntries(entryTable);
        }

        internal const ulong BlockSize = 16UL;

        private void ReadMetadataTableAndCreateEntries(Span<EntryTableEntry> entryTable)
        {
            Reader.BaseStream.Position = (long)Header.MetadataTableStart;
            var metadataTableBuffer = DecompressZLib(Reader.ReadBytes((int)Header.MetadataTableLength));
            using var metadataTableStream = new MemoryStream(metadataTableBuffer);
            using var mr = new BinaryReader(metadataTableStream);

            var meta = new MainMetadata();

            foreach (var entry in entryTable)
            {
                mr.BaseStream.Position = entry.MetadataIndex * 4;

                var chunkTypes = new MetadataChunkType[entry.MetadataCount];
                for (int i = 0; i < entry.MetadataCount; i++)
                {
                    var pos = mr.BaseStream.Position;
                    var indexBytes = mr.ReadBytes(3);
                    var index = indexBytes[0] + (indexBytes[1] << 8) + (indexBytes[2] << 16);
                    var chunkType = (MetadataChunkType)mr.ReadByte();
                    chunkTypes[i] = chunkType;
                    // I don't think we need the index for anything?
                }

                if (chunkTypes[0] == MetadataChunkType.Plain)
                {
                    meta.Deserialize(mr);

                    Entries.Add(entry.Hash, new EntryV2()
                    {
                        Hash = entry.Hash,
                        Offset = meta.Offset,
                        CompressedSize = meta.CompressedSize,
                        Size = meta.Size,
                        IsCompressed = meta.IsCompressed,
                        IsDirectory = false,
                    });
                }
                else if (chunkTypes[0] == MetadataChunkType.Directory)
                {
                    meta.Deserialize(mr);

                    Entries.Add(entry.Hash, new EntryV2()
                    {
                        Hash = entry.Hash,
                        Offset = meta.Offset,
                        CompressedSize = meta.CompressedSize,
                        Size = meta.Size,
                        IsCompressed = meta.IsCompressed,
                        IsDirectory = true,
                    });
                }
                else if (chunkTypes[0] == MetadataChunkType.Image)
                {
                    var tobjMeta = new PackedTobjDdsMetadata
                    {
                        TextureWidth = mr.ReadUInt16() + 1,
                        TextureHeight = mr.ReadUInt16() + 1,
                        ImgFlags = new FlagField(mr.ReadUInt32()),
                        SampleFlags = new FlagField(mr.ReadUInt32())
                    };
                    meta.Deserialize(mr);

                    Entries.Add(entry.Hash, new EntryV2()
                    {
                        Hash = entry.Hash,
                        Offset = meta.Offset,
                        CompressedSize = meta.CompressedSize,
                        Size = meta.CompressedSize,
                        IsCompressed = meta.IsCompressed,
                        IsDirectory = false,
                        TobjMetadata = tobjMeta,
                    });
                }
                else
                {
                    throw new NotImplementedException($"Unhandled metadata type {chunkTypes[0]}");
                }
            }
        }

        private Span<EntryTableEntry> ReadEntryTable()
        {
            Reader.BaseStream.Position = (long)Header.EntryTableStart;
            var entryTableBuffer = DecompressZLib(Reader.ReadBytes((int)Header.EntryTableLength));
            var entryTable = MemoryMarshal.Cast<byte, EntryTableEntry>(entryTableBuffer.AsSpan());
            entryTable.Sort((x, y) => (int)(x.MetadataIndex - y.MetadataIndex));
            return entryTable;
        }
    }

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

    internal enum MetadataChunkType
    {
        Image = 1,
        Sample = 2,
        MipProxy = 3,
        InlineDirectory = 4,
        Unknown6 = 6,
        Plain = 128,
        Directory = 129,
        Mip0 = 130,
        Mip1 = 131,
        MipTail = 132,
    }

    internal struct MainMetadata : IBinarySerializable
    {
        public uint CompressedSize;

        public uint Size;

        public uint Unknown;

        public uint OffsetBlock;

        public readonly ulong Offset => OffsetBlock * HashFsV2Reader.BlockSize;

        private FlagField flags;

        public bool IsCompressed 
        {
            get => flags[4];
            set => flags[4] = value;
        } 

        public void Deserialize(BinaryReader r, uint? version = null)
        {
            var compressedSizeBytes = r.ReadBytes(3);
            var compressedSizeMsbAndCompressedFlag = r.ReadByte();
            CompressedSize = (uint)(
                compressedSizeBytes[0]
                + (compressedSizeBytes[1] << 8)
                + (compressedSizeBytes[2] << 16)
                + ((compressedSizeMsbAndCompressedFlag & 0x0F) << 24)
                );
            flags = new FlagField(compressedSizeMsbAndCompressedFlag & 0xF0u);
            Size = r.ReadUInt32();
            Unknown = r.ReadUInt32();
            OffsetBlock = r.ReadUInt32();
        }

        public void Serialize(BinaryWriter w)
        {
            w.Write((byte)CompressedSize);
            w.Write((byte)(CompressedSize >> 8));
            w.Write((byte)(CompressedSize >> 16));
            w.Write((byte)((byte)(CompressedSize >> 24 & 0x0F) | (flags.Bits & 0xF0)));
            w.Write(Size);
            w.Write(Unknown);
            w.Write(OffsetBlock);
        }
    }
}
