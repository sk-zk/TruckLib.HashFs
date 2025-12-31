using System;
using System.Collections.Generic;
using System.Text;

namespace TruckLib.HashFs.HashFsV2
{
    internal enum MetadataChunkType
    {
        /// <summary>
        /// Primary chunk type for a packed tobj/dds entry.
        /// </summary>
        Image = 1,

        /// <summary>
        /// Secondary chunk type used for packed tobj/dds entries.
        /// </summary>
        Sample = 2,

        MipProxy = 3,

        InlineDirectory = 4,

        /// <summary>
        /// Secondary chunk type used for pmg files.
        /// </summary>
        Unknown6 = 6,

        /// <summary>
        /// Primary chunk type for a regular file.
        /// </summary>
        Plain = 128,

        /// <summary>
        /// Primary chunk type for directory listings.
        /// </summary>
        Directory = 129,

        Mip0 = 130,

        Mip1 = 131,

        /// <summary>
        /// Secondary chunk type used for packed tobj/dds entries.
        /// </summary>
        MipTail = 132,
    }
}
