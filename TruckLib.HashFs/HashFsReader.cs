using System;
using System.IO;

namespace TruckLib.HashFs
{
    /// <summary>
    /// Static factory class for creating the appropriate <c>HashFsV*Reader</c>
    /// depending on the HashFS version.
    /// </summary>
    public static class HashFsReader
    {
        private const string SupportedHashMethod = "CITY";

        /// <summary>
        /// Opens a HashFS archive.
        /// </summary>
        /// <param name="path">The path to the HashFS archive.</param>
        /// <returns>A IHashFsReader.</returns>
        public static IHashFsReader Open(string path)
        {
            return Open(path, false);
        }

        /// <summary>
        /// Opens a HashFS archive.
        /// </summary>
        /// <param name="path">The path to the HashFS archive.</param>
        /// <param name="forceEntryTableAtEnd">If true, the entry table will be read
        /// from the end of the file, regardless of where the archive header says they are located.
        /// Only supported for v1.</param>
        /// <returns>A IHashFsReader.</returns>
        public static IHashFsReader Open(string path, bool forceEntryTableAtEnd)
        {
            var fs = File.OpenRead(path);
            return Open(fs, forceEntryTableAtEnd);
        }

        /// <summary>
        /// Opens a HashFS archive.
        /// </summary>
        /// <param name="stream">The stream containing the HashFS archive.</param>
        /// <returns>A IHashFsReader.</returns>
        public static IHashFsReader Open(Stream stream)
        {
            return Open(stream, false);
        }

        /// <summary>
        /// Opens a HashFS archive.
        /// </summary>
        /// <param name="stream">The stream containing the HashFS archive.</param>
        /// <param name="forceEntryTableAtEnd">If true, the entry table will be read
        /// from the end of the file, regardless of where the archive header says they are located.
        /// Only supported for v1.</param>
        /// <returns>A IHashFsReader.</returns>
        public static IHashFsReader Open(Stream stream, bool forceEntryTableAtEnd)
        {
            var reader = new BinaryReader(stream);

            var header = Header.Deserialize(reader);

            if (header.HashMethod != SupportedHashMethod)
            {
                throw new NotSupportedException($"Unsupported hash method {header.HashMethod}");
            }

            switch (header.Version)
            {
                case 1:
                    var h1 = new HashFsV1Reader
                    {
                        Reader = reader,
                        Header = (HeaderV1)header,
                    };
                    h1.ParseEntryTable(forceEntryTableAtEnd);
                    return h1;
                case 2:
                    var h2 = new HashFsV2Reader
                    {
                        Reader = reader,
                        Header = (HeaderV2)header,
                    };
                    h2.ParseEntries();
                    return h2;
                default:
                    throw new NotSupportedException($"HashFS version {header.Version} is not supported");
            }
        }
    }
}
