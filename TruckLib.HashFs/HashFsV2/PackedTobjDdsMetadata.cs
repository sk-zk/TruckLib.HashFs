using System;
using System.Collections.Generic;
using System.IO;
using TruckLib.HashFs.Dds;
using TruckLib.Models;

namespace TruckLib.HashFs.HashFsV2
{
    /// <summary>
    /// Represents the metadata table values of a packed .tobj/.dds entry.
    /// </summary>
    public struct PackedTobjDdsMetadata : IBinarySerializable
    {
        public uint TextureWidth;
        public uint TextureHeight;

        internal FlagField ImgFlags;
        internal FlagField SampleFlags;

        public uint MipmapCount
        {
            get => ImgFlags.GetBitString(0, 4) + 1;
            set => ImgFlags.SetBitString(0, 4, value - 1);
        }

        public DxgiFormat Format
        {
            get => (DxgiFormat)ImgFlags.GetBitString(4, 8);
            set => ImgFlags.SetBitString(4, 8, (uint)value);
        }

        public bool IsCube 
        {
            get => ImgFlags.GetBitString(12, 2) != 0;
            set => ImgFlags.SetBitString(12, 2, value ? 1u : 0u);
        }

        public uint FaceCount 
        {
            get => ImgFlags.GetBitString(14, 6) + 1;
            set => ImgFlags.SetBitString(14, 6, value - 1);
        }

        public int PitchAlignment
        {
            get => 1 << (int)ImgFlags.GetBitString(20, 4);
            set => ImgFlags.SetBitString(20, 4, (uint)Math.Log2(value));
        }

        public int ImageAlignment
        {
            get => 1 << (int)ImgFlags.GetBitString(24, 4);
            set => ImgFlags.SetBitString(24, 4, (uint)Math.Log2(value));
        }

        public TobjFilter MagFilter 
        {
            get => SampleFlags[0] ? TobjFilter.Linear : TobjFilter.Nearest;
            set 
            {
                if (value == TobjFilter.Nearest)
                    SampleFlags[0] = false;
                else if (value == TobjFilter.Linear || value == TobjFilter.Default)
                    SampleFlags[0] = true;
                else
                    throw new ArgumentException();
            }
        }

        public TobjFilter MinFilter
        {
            get => SampleFlags[1] ? TobjFilter.Linear : TobjFilter.Nearest;
            set
            {
                if (value == TobjFilter.Nearest)
                    SampleFlags[1] = false;
                else if (value == TobjFilter.Linear || value == TobjFilter.Default)
                    SampleFlags[1] = true;
                else
                    throw new ArgumentException();
            }
        }

        public TobjMipFilter MipFilter 
        {
            get => (TobjMipFilter)SampleFlags.GetBitString(2, 2);
            set => SampleFlags.SetBitString(2, 2, (uint)value);
        } 

        public TobjAddr AddrU 
        {
            get => (TobjAddr)SampleFlags.GetBitString(4, 3);
            set => SampleFlags.SetBitString(4, 3, (uint)value);
        }

        public TobjAddr AddrV
        {
            get => (TobjAddr)SampleFlags.GetBitString(7, 3);
            set => SampleFlags.SetBitString(7, 3, (uint)value);
        }

        public TobjAddr AddrW
        {
            get => (TobjAddr)SampleFlags.GetBitString(10, 3);
            set => SampleFlags.SetBitString(10, 3, (uint)value);
        }

        /// <summary>
        /// Creates a <see cref="Tobj"/> object from the metadata.
        /// </summary>
        /// <param name="tobjPath">The absolute path of the .tobj file, 
        /// e.g. <c>/model/wall/anti_noise.tobj</c>.</param>
        /// <returns>A <see cref="Tobj"/> object.</returns>
        public Tobj AsTobj(string tobjPath)
        {
            var ddsPath = Path.ChangeExtension(tobjPath, "dds");
            var tobj = new Tobj
            {
                Type = IsCube ? TobjType.CubeMap : TobjType.Map2D,
                MagFilter = MagFilter,
                MinFilter = MinFilter,
                MipFilter = MipFilter,
                AddrU = AddrU,
                AddrV = AddrV,
                AddrW = AddrW,
                Anisotropic = true,
                Compress = true,
                TexturePath = ddsPath,
            };
            return tobj;
        }

        internal static PackedTobjDdsMetadata FromTobj(Tobj tobj, DdsFile dds)
        {
            var tobjMeta = new PackedTobjDdsMetadata();
            tobjMeta.TextureWidth = dds.Header.Width;
            tobjMeta.TextureHeight = dds.Header.Height;
            tobjMeta.MipmapCount = dds.Header.MipMapCount;
            tobjMeta.Format = dds.HeaderDxt10.Format;
            tobjMeta.IsCube = tobj.Type == TobjType.CubeMap;
            tobjMeta.FaceCount = 1;
            tobjMeta.PitchAlignment = 256;
            tobjMeta.ImageAlignment = 512;
            tobjMeta.MagFilter = tobj.MagFilter;
            tobjMeta.MinFilter = tobj.MinFilter;
            tobjMeta.MipFilter = tobj.MipFilter;
            tobjMeta.AddrU = tobj.AddrU;
            tobjMeta.AddrV = tobj.AddrV;
            tobjMeta.AddrW = tobj.AddrW;
            return tobjMeta;
        }

        public void Deserialize(BinaryReader r, uint? version = null)
        {
            TextureWidth = r.ReadUInt16() + 1u;
            TextureHeight = r.ReadUInt16() + 1u;
            ImgFlags = new FlagField(r.ReadUInt32());
            SampleFlags = new FlagField(r.ReadUInt32());
        }

        public void Serialize(BinaryWriter w)
        {
            w.Write((ushort)(TextureWidth - 1));
            w.Write((ushort)(TextureHeight - 1));
            w.Write(ImgFlags.Bits);
            w.Write(SampleFlags.Bits);
        }
    }
}