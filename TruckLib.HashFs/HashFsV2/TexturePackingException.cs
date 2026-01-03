using System;
using System.Collections.Generic;
using System.Text;

namespace TruckLib.HashFs.HashFsV2
{
    /// <summary>
    /// Thrown if an error occurred while packing a .tobj/.dds pair into a HashFS v2 archive.
    /// </summary>
    public class TexturePackingException : Exception
    {
        /// <summary>
        /// The path to the .tobj file where the error occurred.
        /// </summary>
        public string TobjPath { get; init; }

        public TexturePackingException(string tobjPath, string message)
            : base(message)
        {
            TobjPath = tobjPath;
        }
    }
}
