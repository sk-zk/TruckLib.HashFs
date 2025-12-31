using System;
using System.Collections.Generic;
using System.Text;

namespace TruckLib.HashFs.HashFsV2
{
    internal static class Consts
    {
        /// <summary>
        /// The character used in directory listing files to indicate that
        /// an element is a directory rather than a file.
        /// </summary>
        internal const char DirMarker = '/';

        internal const ulong BlockSize = 16UL;

        internal const int MetadataTableBlockSize = 4;
    }
}
