using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TruckLib.HashFs
{
    internal abstract class HashFsReaderBase : IHashFsReader
    {
        /// <inheritdoc/>
        public required string Path { get; init; }

        /// <inheritdoc/>
        public Dictionary<ulong, IEntry> Entries { get; } = [];

        /// <inheritdoc/>
        public abstract ushort Salt { get; set; }

        /// <inheritdoc/>
        public abstract ushort Version { get; }

        /// <inheritdoc/>
        public BinaryReader BaseReader => Reader;

        internal required BinaryReader Reader { get; init; }

        protected const char Separator = '/';
        protected const string Root = "/";

        /// <inheritdoc/>
        char IFileSystem.DirectorySeparator => Separator;

        /// <inheritdoc/>
        public EntryType EntryExists(string path) 
            => TryGetEntry(path, out var _);

        /// <inheritdoc/>
        public EntryType TryGetEntry(string path, out IEntry entry)
        {
            path = RemoveTrailingSlash(path);
            var hash = HashPath(path);
            if (Entries.TryGetValue(hash, out entry))
            {
                return entry.IsDirectory
                    ? EntryType.Directory
                    : EntryType.File;
            }
            return EntryType.NotFound;
        }

        /// <inheritdoc/>
        public byte[][] Extract(string path)
        {
            if (EntryExists(path) == EntryType.NotFound)
                throw new FileNotFoundException();

            var entry = GetEntry(path);
            return Extract(entry, path);
        }

        /// <inheritdoc/>
        public virtual byte[][] Extract(IEntry entry, string path)
        {
            if (!Entries.ContainsValue(entry))
                throw new FileNotFoundException();

            return [GetEntryContent(entry)];
        }

        /// <inheritdoc/>
        public void ExtractToFile(string entryPath, string outputPath)
        {
            if (EntryExists(entryPath) == EntryType.NotFound)
                throw new FileNotFoundException();

            var entry = GetEntry(entryPath);
            ExtractToFile(entry, entryPath, outputPath);
        }

        /// <inheritdoc/>
        public virtual void ExtractToFile(IEntry entry, string entryPath, string outputPath)
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

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));
            Reader.BaseStream.Position = (long)entry.Offset;
            using var fileStream = new FileStream(outputPath, FileMode.Create);
            if (entry.IsCompressed)
            {
                using var zlibStream = new ZLibStream(Reader.BaseStream, CompressionMode.Decompress, true);
                CopyStream(zlibStream, fileStream, entry.Size);
            }
            else
            {
                CopyStream(Reader.BaseStream, fileStream, entry.Size);
            }
        }

        protected static void CopyStream(Stream input, Stream output, long bytes)
        {
            var buffer = new byte[32768];
            int read;
            while (bytes > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, (int)bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }

        /// <inheritdoc/>
        public DirectoryListing GetDirectoryListing(
            string path, bool filesOnly = false, bool returnAbsolute = true)
        {
            path = RemoveTrailingSlash(path);

            var entryType = EntryExists(path);
            if (entryType == EntryType.NotFound)
                throw new FileNotFoundException();
            else if (entryType != EntryType.Directory)
                throw new ArgumentException($"\"{path}\" is not a directory.");

            var entry = GetEntry(path);

            var dir = GetDirectoryListing(entry, filesOnly);

            if (returnAbsolute)
            {
                MakePathsAbsolute(path, dir.Subdirectories);
                MakePathsAbsolute(path, dir.Files);
            }

            return new DirectoryListing(dir.Subdirectories, dir.Files);
        }

        /// <inheritdoc/>
        public abstract DirectoryListing GetDirectoryListing(IEntry entry,
            bool filesOnly = false);

        /// <inheritdoc/>
        public IEntry GetEntry(string path)
        {
            path = RemoveTrailingSlash(path);
            ulong hash = HashPath(path);
            var entry = Entries[hash];
            return entry;
        }

        /// <inheritdoc/>
        public ulong HashPath(string path, uint? salt = null)
        {
            if (path != "" && path.StartsWith(Separator))
                path = path[1..];

            // TODO do salts work the same way in v2?
            salt ??= Salt;
            if (salt != 0)
                path = salt + path;

            var bytes = Encoding.UTF8.GetBytes(path);
            var hash = CityHash.CityHash64(bytes, (ulong)bytes.Length);
            return hash;
        }

        /// <summary>
        /// Closes the file stream.
        /// </summary>
        public void Dispose()
        {
            Reader.BaseStream.Dispose();
            Reader.Dispose();
        }

        protected static void MakePathsAbsolute(string parent, List<string> paths)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (parent == Root)
                    paths[i] = Root + paths[i];
                else
                    paths[i] = $"{parent}{Separator}{paths[i]}";
            }
        }

        protected static string RemoveTrailingSlash(string path)
        {
            if (path.EndsWith(Separator) && path != Root)
            {
                path = path[0..^1];
            }
            return path;
        }

        protected virtual byte[] GetEntryContent(IEntry entry)
        {
            Reader.BaseStream.Position = (long)entry.Offset;

            byte[] file;
            if (entry.IsCompressed)
            {
                if (entry is EntryV2 v2 && v2.TobjMetadata != null)
                {
                    file = Reader.ReadBytes((int)entry.CompressedSize);
                }
                else
                {
                    file = Reader.ReadBytes((int)entry.CompressedSize);
                    file = DecompressZLib(file);
                }
            }
            else
            {
                file = Reader.ReadBytes((int)entry.Size);
            }
            return file;
        }

        protected static byte[] DecompressZLib(Stream input)
        {
            using var zlibStream = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlibStream.CopyTo(output);
            var decompressed = output.ToArray();
            return decompressed;
        }

        protected static byte[] DecompressZLib(byte[] buffer)
        {
            using var ms = new MemoryStream(buffer);
            return DecompressZLib(ms);
        }

        /// <inheritdoc/>
        bool IFileSystem.FileExists(string path)
        {
            return EntryExists(path) == EntryType.File;
        }

        /// <inheritdoc/>
        IList<string> IFileSystem.GetFiles(string path)
        {
            var dirlist = GetDirectoryListing(path, true, true);
            return dirlist.Files;
        }

        /// <inheritdoc/>
        byte[] IFileSystem.ReadAllBytes(string path)
        {
            return Extract(path)[0];
        }

        /// <inheritdoc/>
        string IFileSystem.ReadAllText(string path)
        {
            var bytes = Extract(path)[0];
            return Encoding.UTF8.GetString(bytes);
        }

        /// <inheritdoc/>
        string IFileSystem.ReadAllText(string path, Encoding encoding)
        {
            var bytes = Extract(path)[0];
            return encoding.GetString(bytes);
        }

        /// <inheritdoc/>
        Stream IFileSystem.Open(string path)
        {
            var bytes = Extract(path)[0];
            return new MemoryStream(bytes);
        }

        /// <inheritdoc/>
        string IFileSystem.GetParent(string path)
        {
            if (path == Root)
            {
                return null;
            }
            return path[0..path.LastIndexOf(Separator)];
        }
    }
}
