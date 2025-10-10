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

        /// <inheritdoc/>
        public override DirectoryListing GetDirectoryListing(
            IEntry entry, bool filesOnly = false)
        {
            var bytes = GetEntryContent(entry);
            using var ms = new MemoryStream(bytes);
            using var dirReader = new BinaryReader(ms);

            var count = dirReader.ReadUInt32();
            var stringLengths = dirReader.ReadBytes((int)count);

            var subdirs = new List<string>();
            var files = new List<string>();

            for (int i = 0; i < count; i++)
            {
                var str = Encoding.UTF8.GetString(dirReader.ReadBytes(stringLengths[i]));
                // is directory
                if (str.StartsWith('/'))
                {
                    if (filesOnly) continue;
                    var subPath = str[1..];
                    subdirs.Add(subPath);
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
                    System.IO.Path.ChangeExtension(outputPath, "dds"),
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
            const ulong blockSize = 16UL;

            var entryTable = ReadEntryTable();

            Reader.BaseStream.Position = (long)Header.MetadataTableStart;
            var metadataTableBuffer = DecompressZLib(Reader.ReadBytes((int)Header.MetadataTableLength));
            using var metadataTableStream = new MemoryStream(metadataTableBuffer);
            using var mr = new BinaryReader(metadataTableStream);

            foreach (var entry in entryTable)
            {
                mr.BaseStream.Position = entry.MetadataIndex * 4;

                var indexBytes = mr.ReadBytes(3);
                var index = indexBytes[0] + (indexBytes[1] << 8) + (indexBytes[2] << 16);

                var chunkType = (MetadataChunkType)mr.ReadByte();
                if (chunkType == MetadataChunkType.Plain)
                {
                    if (entry.MetadataCount == 2)
                    {
                        // Skip 4 bytes ahead to go to directly
                        // to the payload of chunk type 6.
                        // Don't know what the deal with that is
                        mr.ReadUInt32();
                    }

                    var compressedSizeBytes = mr.ReadBytes(3);
                    var compressedSizeMsbAndCompressedFlag = mr.ReadByte();
                    var compressedSize = compressedSizeBytes[0]
                        + (compressedSizeBytes[1] << 8)
                        + (compressedSizeBytes[2] << 16)
                        + ((compressedSizeMsbAndCompressedFlag & 0x0F) << 24);
                    var flags = (byte)(compressedSizeMsbAndCompressedFlag & 0xF0);
                    var size = mr.ReadUInt32();
                    var unknown2 = mr.ReadUInt32();
                    var offsetBlock = mr.ReadUInt32();

                    Entries.Add(entry.Hash, new EntryV2()
                    {
                        Hash = entry.Hash,
                        Offset = offsetBlock * blockSize,
                        CompressedSize = (uint)compressedSize,
                        Size = size,
                        IsCompressed = (flags & 0x10) != 0,
                        IsDirectory = false,
                    });
                }
                else if (chunkType == MetadataChunkType.Directory)
                {
                    var compressedSizeBytes = mr.ReadBytes(3);
                    var compressedSizeMsbAndCompressedFlag = mr.ReadByte();
                    var compressedSize = compressedSizeBytes[0]
                        + (compressedSizeBytes[1] << 8)
                        + (compressedSizeBytes[2] << 16)
                        + ((compressedSizeMsbAndCompressedFlag & 0x0F) << 24);
                    var flags = (byte)(compressedSizeMsbAndCompressedFlag & 0xF0);
                    var size = mr.ReadUInt32();
                    var unknown2 = mr.ReadUInt32();
                    var offsetBlock = mr.ReadUInt32();

                    Entries.Add(entry.Hash, new EntryV2()
                    {
                        Hash = entry.Hash,
                        Offset = offsetBlock * blockSize,
                        CompressedSize = (uint)compressedSize,
                        Size = size,
                        IsCompressed = (flags & 0x10) != 0,
                        IsDirectory = true,
                    });
                }
                else if (chunkType == MetadataChunkType.Image)
                {
                    // Skip 8 bytes ahead (past the sample chunk,
                    // which is also empty) to go directly to the
                    // mip tail payload.
                    mr.ReadUInt64();

                    var meta = new PackedTobjDdsMetadata
                    {
                        TextureWidth = mr.ReadUInt16() + 1,
                        TextureHeight = mr.ReadUInt16() + 1,
                        ImgFlags = new FlagField(mr.ReadUInt32()),
                        SampleFlags = new FlagField(mr.ReadUInt32())
                    };
                    var compressedSizeBytes = mr.ReadBytes(3);
                    var compressedSizeMsbAndCompressedFlag = mr.ReadByte();
                    var compressedSize = compressedSizeBytes[0]
                        + (compressedSizeBytes[1] << 8)
                        + (compressedSizeBytes[2] << 16)
                        + ((compressedSizeMsbAndCompressedFlag & 0x0F) << 24);
                    var flags = (compressedSizeMsbAndCompressedFlag & 0xF0);
                    var unknown3 = mr.ReadBytes(8);
                    var offsetBlock = mr.ReadUInt32();

                    Entries.Add(entry.Hash, new EntryV2()
                    {
                        Hash = entry.Hash,
                        Offset = offsetBlock * blockSize,
                        CompressedSize = (uint)compressedSize,
                        Size = (uint)compressedSize,
                        IsCompressed = (flags & 0x10) != 0,
                        IsDirectory = false,
                        TobjMetadata = meta,
                    });
                }
                else
                {
                    throw new NotImplementedException($"Unhandled metadata type {chunkType}");
                }
            }
        }

        private Span<EntryTableEntry> ReadEntryTable()
        {
            Reader.BaseStream.Position = (long)Header.EntryTableStart;
            var entryTableBuffer = DecompressZLib(Reader.ReadBytes((int)Header.EntryTableLength));
            var entryTable = MemoryMarshal.Cast<byte, EntryTableEntry>(entryTableBuffer);
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
        Unknown = 6,
        Plain = 128,
        Directory = 129,
        Mip0 = 130,
        Mip1 = 131,
        MipTail = 132,
    }
}
