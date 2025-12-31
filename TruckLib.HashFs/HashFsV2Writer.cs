using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TruckLib.HashFs.Dds;
using TruckLib.Models;
using static TruckLib.HashFs.Util;
using TruckLib.HashFs.HashFsV2;
using static TruckLib.HashFs.HashFsV2.Consts;

namespace TruckLib.HashFs
{    
    /// <summary>
    /// Creates a new HashFS v1 archive.
    /// </summary>
    public class HashFsV2Writer : HashFsWriterBase
    {
        /// <inheritdoc/>
        public override void Save(string path)
        {
            using var fs = File.Create(path);
            Save(fs);
        }

        /// <inheritdoc/>
        public override void Save(Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, true);

            // Generate directory listing files
            var dirLists = GenerateDirectoryListings(stream, tree, "/");

            // Write the files, generate the metadata table stream,
            // and generate the entry table entries
            var (entries, metaStream) = WriteFiles(files.Concat(dirLists).ToDictionary(), stream);

            // Write the entry table
            var entryTableStart = stream.Position;
            using var entryStream = new MemoryStream();
            var sorted = entries.OrderBy(x => x.Hash).ToArray().AsSpan();
            var asBytes = MemoryMarshal.AsBytes(sorted);
            entryStream.Write(asBytes);
            entryStream.Position = 0;
            CompressZlib(entryStream, stream, CompressionLevel.SmallestSize);
            var entryTableLength = stream.Position - entryTableStart;
            AdvanceStreamToNextBlock(stream); // byte-match the official packer

            // Write the metadata table stream
            var metaTableStart = stream.Position;
            metaStream.Position = 0;
            CompressZlib(metaStream, stream, CompressionLevel.SmallestSize);
            var metaTableLength = stream.Position - metaTableStart;

            // Add a version number at the end for future debugging purposes
            WriteWatermark(w);

            // Write the header
            var nonDdsCount = files.Count(x => 
                x.Key.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
            stream.Position = 0;
            var header = new HeaderV2()
            {
                Salt = Salt,
                HashMethod = "CITY",
                NumEntries = (uint)(nonDdsCount + dirLists.Count),
                EntryTableLength = (uint)entryTableLength,
                NumMetadataEntries = (uint)(metaStream.Length / 4),
                MetadataTableLength = (uint)metaTableLength,
                EntryTableStart = (uint)entryTableStart,
                MetadataTableStart = (uint)metaTableStart,
                SecurityDescriptorOffset = 0,
                Platform = Platform.PC,
            };
            header.Serialize(w);
        }

        private (List<EntryTableEntry> Entries, MemoryStream MetaStream) WriteFiles(
            Dictionary<string, IFile> files, Stream outStream)
        {
            const int fileStartOffset = 4096;
            outStream.Position = fileStartOffset;

            var metaStream = new MemoryStream();
            using var metaWriter = new BinaryWriter(metaStream, Encoding.UTF8, true);

            List<EntryTableEntry> entries = [];

            foreach (var (path, file) in files)
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".dds")
                    continue;

                EntryTableEntry entry;
                if (extension == ".tobj")
                {
                    entry = WriteTobjDdsEntry(file, files, outStream, metaWriter, path);
                }
                else
                {
                    entry = WriteRegularEntry(file, outStream, metaWriter, path);
                }
                entries.Add(entry);

                // File offsets must be multiples of 16
                AdvanceStreamToNextBlock(outStream);
            }

            return (entries, metaStream);
        }

        private EntryTableEntry WriteTobjDdsEntry(IFile file, Dictionary<string, IFile> files, Stream outStream,
            BinaryWriter metaWriter, string path)
        {
            var startPos = outStream.Position;

            // Open the tobj and dds files
            var tobj = LoadTobjFromFileStream(file);

            if (!files.TryGetValue(tobj.TexturePath, out var ddsEntry))
            {
                throw new FileNotFoundException();
            }
            var dds = DdsFile.Load(ddsEntry.Open());

            // Realign dds bytes
            var buffer = DdsUtils.ConvertSurfaceData(dds);

            // Write the dds bytes
            // TODO GDeflate compression
            var compress = false;
            outStream.Write(buffer);

            var endPos = outStream.Position;
            var uncompressedSize = (uint)buffer.Length;
            var compressedSize = (uint)(endPos - startPos);

            // Write the metadata table entry
            var metaOffset = metaWriter.BaseStream.Position;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var numChunks = WriteMetadataChunkTypes(metaWriter, metaOffset, file, extension);

            var tobjMeta = PackedTobjDdsMetadata.FromTobj(tobj, dds);
            tobjMeta.Serialize(metaWriter);

            var meta = new MainMetadata()
            {
                CompressedSize = compressedSize,
                Size = uncompressedSize,
                IsCompressed = compress,
                OffsetBlock = (uint)((ulong)startPos / BlockSize),
            };
            meta.IsCompressed = compress;
            meta.Flags2.Bits = 48; // don't know what this does
            meta.Serialize(metaWriter);

            // Create the entry table entry
            var entry = new EntryTableEntry()
            {
                Hash = HashPath(path, Salt),
                MetadataIndex = (uint)(metaOffset / MetadataTableBlockSize),
                MetadataCount = numChunks,
                Flags = (ushort)(file.IsDirectory ? 1 : 0),
            };
            return entry;
        }

        private static Tobj LoadTobjFromFileStream(IFile file)
        {
            var tobjStream = file.Open();
            using var tobjBr = new BinaryReader(tobjStream);
            var tobj = new Tobj();
            tobj.Deserialize(tobjBr);
            return tobj;
        }

        private EntryTableEntry WriteRegularEntry(IFile file, Stream outStream, BinaryWriter metaWriter, string path)
        {
            var startPos = outStream.Position;

            // Write the file
            var fileStream = file.Open();

            var compress = CompressionLevel != CompressionLevel.NoCompression
                && fileStream.Length > CompressionThreshold;
            if (compress)
            {
                CompressZlib(fileStream, outStream, CompressionLevel);
            }
            else
            {
                fileStream.CopyTo(outStream);
            }

            var endPos = outStream.Position;
            var uncompressedSize = (uint)fileStream.Length;
            var compressedSize = (uint)(endPos - startPos);

            fileStream.Dispose();

            // Write the metadata table entry
            var metaOffset = metaWriter.BaseStream.Position;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var numChunks = WriteMetadataChunkTypes(metaWriter, metaOffset, file, extension);

            var meta = new MainMetadata()
            {
                CompressedSize = compressedSize,
                Size = uncompressedSize,
                IsCompressed = compress,
                OffsetBlock = (uint)((ulong)startPos / BlockSize),
            };
            meta.IsCompressed = compress;
            meta.Serialize(metaWriter);

            if (extension == ".pmg")
            {
                // 8 extra bytes with unknown purpose for chunk type 6
                metaWriter.Write(0UL);
            }

            // Create the entry table entry
            var entry = new EntryTableEntry()
            {
                Hash = HashPath(path, Salt),
                MetadataIndex = (uint)(metaOffset / MetadataTableBlockSize),
                MetadataCount = numChunks,
                Flags = (ushort)(file.IsDirectory ? 1 : 0),
            };

            return entry;
        }

        private static ushort WriteMetadataChunkTypes(BinaryWriter w, long metaOffset, IFile file, string extension)
        {
            List<MetadataChunkType> chunks = new(1);

            if (file.IsDirectory)
            {
                chunks.Add(MetadataChunkType.Directory);
            }
            else if (extension == ".tobj")
            {
                chunks.Add(MetadataChunkType.Image);
                chunks.Add(MetadataChunkType.Sample);
                chunks.Add(MetadataChunkType.MipTail);
            }
            else
            {
                chunks.Add(MetadataChunkType.Plain);
                if (extension == ".pmg")
                {
                    // I don't know what this signals to the game,
                    // but the official packer adds it for all pmg files
                    chunks.Add(MetadataChunkType.Unknown6);
                }
            }

            var metaIndex = (metaOffset / MetadataTableBlockSize) + chunks.Count;
            for (int i = 0; i < chunks.Count; i++)
            {
                w.Write((byte)metaIndex);
                w.Write((byte)(metaIndex >> 8));
                w.Write((byte)(metaIndex >> 16));
                w.Write((byte)chunks[i]);

                if (chunks.Count > 1)
                {
                    var chunkLength = chunks[i] switch
                    {
                        MetadataChunkType.Plain => 4,
                        MetadataChunkType.Unknown6 => 2,
                        MetadataChunkType.Directory => 4,
                        MetadataChunkType.Image => 2,
                        MetadataChunkType.Sample => 1,
                        MetadataChunkType.MipTail => 4,
                        _ => throw new NotImplementedException(),
                    };
                    metaIndex += chunkLength;
                }
            }

            return (ushort)chunks.Count;
        }

        private static void AdvanceStreamToNextBlock(Stream stream)
        {
            stream.Position = NearestMultiple(stream.Position, (long)BlockSize);
        }

        private static Dictionary<string, IFile> GenerateDirectoryListings(Stream stream, Directory dir,
            string path, Dictionary<string, IFile> dirListFiles = null)
        {
            dirListFiles ??= [];

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8, true);

            // Encode strings
            List<byte[]> strings = [];
            foreach (var (name, _) in dir.Directories)
            {
                var utf8Bytes = Encoding.UTF8.GetBytes(DirMarker + name);
                strings.Add(utf8Bytes);
            }
            foreach (var file in dir.Files)
            {
                // Don't add any dds files to the listing - dds files referenced by a tobj
                // get packed into the tobj entry, and loose dds files are ignored.
                if (file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var utf8Bytes = Encoding.UTF8.GetBytes(file);
                strings.Add(utf8Bytes);
            }

            // Write string counts and lengths
            w.Write((uint)strings.Count);
            foreach (var str in strings)
            {
                w.Write((byte)str.Length);
            }

            // Write strings
            foreach (var str in strings)
            {
                w.Write(str);
            }

            dirListFiles.Add(path, new BufferFile()
            {
                Buffer = ms.ToArray(),
                IsDirectory = true,
            });

            // Recurse
            foreach (var (name, subdir) in dir.Directories)
            {
                var subpath = Combine(path, name);
                GenerateDirectoryListings(stream, subdir, subpath, dirListFiles);
            }

            return dirListFiles;
        }
    }
}
