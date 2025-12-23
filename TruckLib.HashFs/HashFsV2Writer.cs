using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
            var (entries, metaStream) = WriteFiles(files.Concat(dirLists), stream);

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
            stream.Position = 0;
            var header = new HeaderV2()
            {
                Salt = Salt,
                HashMethod = "CITY",
                NumEntries = (uint)(files.Count + dirLists.Count),
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
            IEnumerable<KeyValuePair<string, IFile>> files, Stream outStream)
        {
            const int fileStartOffset = 4096;
            outStream.Position = fileStartOffset;

            var metaStream = new MemoryStream();
            using var metaWriter = new BinaryWriter(metaStream, Encoding.UTF8, true);

            List<EntryTableEntry> entries = [];

            foreach (var (path, file) in files)
            {
                // Write the file
                var startPos = outStream.Position;

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
                var metaOffset = metaStream.Position;

                var extension = Path.GetExtension(path).ToLowerInvariant();
                var numChunks = WriteMetadataChunks(metaWriter, metaOffset, file, extension);

                var meta = new MainMetadata()
                {
                    CompressedSize = compressedSize,
                    Size = uncompressedSize,
                    IsCompressed = compress,
                    OffsetBlock = (uint)((ulong)startPos / HashFsV2Reader.BlockSize),
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
                    Hash = Util.HashPath(path, Salt),
                    MetadataIndex = (uint)(metaOffset / 4),
                    MetadataCount = numChunks,
                    Flags = (ushort)(file.IsDirectory ? 1 : 0),
                };
                entries.Add(entry);

                // File offsets must be multiples of 16
                AdvanceStreamToNextBlock(outStream);
            }

            return (entries, metaStream);
        }

        private static ushort WriteMetadataChunks(BinaryWriter w, long metaOffset, IFile file, string extension)
        {
            List<MetadataChunkType> chunks = new(1);

            if (file.IsDirectory)
            {
                chunks.Add(MetadataChunkType.Directory);
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

            var metaIndex = (metaOffset / 4) + chunks.Count;
            for (int i = 0; i < chunks.Count; i++)
            {
                w.Write((byte)metaIndex);
                w.Write((byte)(metaIndex >> 8));
                w.Write((byte)(metaIndex >> 16));
                w.Write((byte)chunks[i]);
                metaIndex += 4;
            }

            return (ushort)chunks.Count;
        }

        private static void AdvanceStreamToNextBlock(Stream stream)
        {
            var nextPosition = (long)Math.Ceiling(stream.Position / (float)HashFsV2Reader.BlockSize) 
                * (long)HashFsV2Reader.BlockSize;
            stream.Position = nextPosition;
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
                var utf8Bytes = Encoding.UTF8.GetBytes(HashFsV2Reader.DirMarker + name);
                strings.Add(utf8Bytes);
            }
            foreach (var file in dir.Files)
            {
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
