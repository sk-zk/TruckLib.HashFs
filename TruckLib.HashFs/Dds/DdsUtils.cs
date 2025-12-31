using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs.HashFsV2;
using static TruckLib.HashFs.Util;
using static TruckLib.HashFs.HashFsV2.Consts;

namespace TruckLib.HashFs.Dds
{
    internal static class DdsUtils
    {
        /// <summary>
        /// Realigns the surface data bytes of a DDS file to the format 
        /// with which it is stored in a HashFS v2 archive.
        /// </summary>
        /// <param name="dds">The DDS file to convert.</param>
        /// <returns>The realigned surface data.</returns>
        public static byte[] ConvertSurfaceData(DdsFile dds)
        {
            var faceCount = 1;
            var subData = GenerateSubResourceData(dds, dds.Data.Length);
            var bufferSize = CalculateDdsHashFsLength((uint)faceCount, dds.Header.MipMapCount, subData);
            var buffer = new byte[bufferSize];

            var dstOffset = 0;
            var srcOffset = 0;
            for (int currentFaceIdx = 0; currentFaceIdx < faceCount; currentFaceIdx++)
            {
                for (int mipmapIdx = 0; mipmapIdx < dds.Header.MipMapCount; mipmapIdx++)
                {
                    dstOffset = NearestMultiple(dstOffset, ImageAlignment);
                    var s = subData[mipmapIdx];
                    for (int doneBytes = 0; doneBytes < s.SlicePitch; doneBytes += s.RowPitch)
                    {
                        dstOffset = NearestMultiple(dstOffset, PitchAlignment);
                        Array.Copy(dds.Data, srcOffset, buffer, dstOffset, s.RowPitch);
                        dstOffset += s.RowPitch;
                        srcOffset += s.RowPitch;
                    }
                }
            }

            return buffer;
        }

        /// <summary>
        /// Realigns the surface data bytes from the HashFS format
        /// back into the format used in a DDS file.
        /// </summary>
        /// <param name="metadata">The tobj/dds metadata from the metadata table entry of the texture.</param>
        /// <param name="dds">The DDS file.</param>
        /// <param name="surfaceData">The surface data.</param>
        /// <returns>The realigned surface data.</returns>
        public static byte[] UnconvertDdsSurfaceData(PackedTobjDdsMetadata metadata, DdsFile dds, byte[] surfaceData)
        {
            var subData = GenerateSubResourceData(dds, surfaceData.Length);

            var ddsDataLength = CalculateDdsDataLength(metadata.FaceCount, subData);
            var dst = new byte[ddsDataLength];

            var srcOffset = 0;
            var dstOffset = 0;
            for (int currentFaceIdx = 0; currentFaceIdx < metadata.FaceCount; currentFaceIdx++)
            {
                for (int mipmapIdx = 0; mipmapIdx < dds.Header.MipMapCount; mipmapIdx++)
                {
                    srcOffset = NearestMultiple(srcOffset, metadata.ImageAlignment);
                    var s = subData[mipmapIdx];
                    for (int doneBytes = 0; doneBytes < s.SlicePitch; doneBytes += s.RowPitch)
                    {
                        srcOffset = NearestMultiple(srcOffset, metadata.PitchAlignment);
                        Array.Copy(surfaceData, srcOffset, dst, dstOffset, s.RowPitch);
                        srcOffset += s.RowPitch;
                        dstOffset += s.RowPitch;
                    }
                }
            }

            return dst;
        }

        public static int CalculateDdsDataLength(uint faceCount, List<SubresourceData> subData)
        {
            int length = 0;
            for (int i = 0; i < subData.Count; i++)
            {
                var rowPitch = subData[i].RowPitch;
                var slicePitch = subData[i].SlicePitch;
                var sectionLength = (int)faceCount * (slicePitch / rowPitch) * rowPitch;
                length += sectionLength;
            }
            return length;
        }

        public static int CalculateDdsHashFsLength(uint faceCount, uint mipmapCount, List<SubresourceData> subData)
        {
            // TODO refactor this
            var dstOffset = 0;
            for (int currentFaceIdx = 0; currentFaceIdx < faceCount; currentFaceIdx++)
            {
                for (int mipmapIdx = 0; mipmapIdx < mipmapCount; mipmapIdx++)
                {
                    dstOffset = NearestMultiple(dstOffset, ImageAlignment);
                    var s = subData[mipmapIdx];
                    for (int doneBytes = 0; doneBytes < s.SlicePitch; doneBytes += s.RowPitch)
                    {
                        dstOffset = NearestMultiple(dstOffset, PitchAlignment);
                        dstOffset += s.RowPitch;
                    }
                }
            }
            return dstOffset;
        }

        public static List<SubresourceData> GenerateSubResourceData(DdsFile dds, int numTotalBytes)
        {
            var subData = new List<SubresourceData>(16);
            var skippedMipmaps = 0;

            const int MaxSize = 0;

            var srcBitsIdx = 0;
            var endBitsIdx = numTotalBytes;

            for (int j = 0; j < dds.HeaderDxt10.ArraySize; j++)
            {
                var width = (int)dds.Header.Width;
                var height = (int)dds.Header.Height;
                var depth = (int)dds.Header.Depth;
                for (int i = 0; i < dds.Header.MipMapCount; i++)
                {
                    var surface = GetSurfaceInfo(width, height, dds.HeaderDxt10.Format);
                    if ((dds.Header.MipMapCount <= 1) 
                        || MaxSize == 0 
                        || (width <= MaxSize && height <= MaxSize && depth <= MaxSize))
                    {
                        subData.Add(new SubresourceData()
                        {
                            DataIdx = srcBitsIdx,
                            RowPitch = surface.RowBytes,
                            SlicePitch = surface.NumBytes,
                        });
                    }
                    else if (j != 0)
                    {
                        // Count number of skipped mipmaps (first item only)
                        skippedMipmaps++;
                    }

                    if (srcBitsIdx + (surface.NumBytes * depth) > endBitsIdx)
                    {
                        throw new EndOfStreamException();
                    }

                    srcBitsIdx += surface.NumBytes * depth;

                    width = Math.Max(width / 2, 1);
                    height = Math.Max(height / 2, 1);
                    depth = Math.Max(depth / 2, 1);
                }
            }

            return subData;
        }

        public static SurfaceInfo GetSurfaceInfo(int width, int height, DxgiFormat format)
        {
            var info = new SurfaceInfo();

            var bc = false;
            var packed = false;
            var planar = false;
            var bpe = 0;
            switch(format)
            {
                case DxgiFormat.BC1_TYPELESS:
                case DxgiFormat.BC1_UNORM:
                case DxgiFormat.BC1_UNORM_SRGB:
                case DxgiFormat.BC4_TYPELESS:
                case DxgiFormat.BC4_UNORM:
                case DxgiFormat.BC4_SNORM:
                    bc = true;
                    bpe = 8;
                    break;

                case DxgiFormat.BC2_TYPELESS:
                case DxgiFormat.BC2_UNORM:
                case DxgiFormat.BC2_UNORM_SRGB:
                case DxgiFormat.BC3_TYPELESS:
                case DxgiFormat.BC3_UNORM:
                case DxgiFormat.BC3_UNORM_SRGB:
                case DxgiFormat.BC5_TYPELESS:
                case DxgiFormat.BC5_UNORM:
                case DxgiFormat.BC5_SNORM:
                case DxgiFormat.BC6H_TYPELESS:
                case DxgiFormat.BC6H_UF16:
                case DxgiFormat.BC6H_SF16:
                case DxgiFormat.BC7_TYPELESS:
                case DxgiFormat.BC7_UNORM:
                case DxgiFormat.BC7_UNORM_SRGB:
                    bc = true;
                    bpe = 16;
                    break;

                case DxgiFormat.R8G8_B8G8_UNORM:
                case DxgiFormat.G8R8_G8B8_UNORM:
                case DxgiFormat.YUY2:
                    packed = true;
                    bpe = 4;
                    break;

                case DxgiFormat.Y210:
                case DxgiFormat.Y216:
                    packed = true;
                    bpe = 8;
                    break;

                case DxgiFormat.NV12:
                case DxgiFormat._420_OPAQUE:
                    planar = true;
                    bpe = 2;
                    break;

                case DxgiFormat.P010:
                case  DxgiFormat.P016:
                    planar = true;
                    bpe = 4;
                    break;
            }

            if (bc)
            {
                var numBlocksWide = 0;
                if (width > 0)
                {
                    numBlocksWide = Math.Max(1, (width + 3) / 4);
                }
                var numBlocksHigh = 0;
                if (height > 0)
                {
                    numBlocksHigh = Math.Max(1, (height + 3) / 4);
                }
                info.RowBytes = numBlocksWide * bpe;
                info.NumRows = numBlocksHigh;
                info.NumBytes = info.RowBytes * numBlocksHigh;
            }
            else if (packed)
            {
                info.RowBytes = ((width + 1) >> 1) * bpe;
                info.NumRows = height;
                info.NumBytes = info.RowBytes * height;
            }
            else if (format == DxgiFormat.NV11)
            {
                info.RowBytes = ((width + 3) >> 2) * 4;
                // Direct3D makes this simplifying assumption, although it is larger than the 4:1:1 data
                info.NumRows = height * 2; 
                info.NumBytes = info.RowBytes * info.NumRows;
            }
            else if (planar)
            {
                info.RowBytes = ((width + 1) >> 1) * bpe;
                info.NumBytes = (info.RowBytes * height) + ((info.RowBytes * height + 1) >> 1);
                info.NumRows = height + ((height + 1) >> 1);
            }
            else
            {
                int bpp = BitsPerPixel(format);
                info.RowBytes = (width * bpp + 7) / 8; // round up to nearest byte
                info.NumRows = height;
                info.NumBytes = info.RowBytes * height;
            }

            return info;
        }

        public static int BitsPerPixel(DxgiFormat format)
        {
            switch(format)
            {
                case DxgiFormat.R32G32B32A32_TYPELESS:
                case DxgiFormat.R32G32B32A32_FLOAT:
                case DxgiFormat.R32G32B32A32_UINT:
                case DxgiFormat.R32G32B32A32_SINT:
                    return 128;

                case DxgiFormat.R32G32B32_TYPELESS:
                case DxgiFormat.R32G32B32_FLOAT:
                case DxgiFormat.R32G32B32_UINT:
                case DxgiFormat.R32G32B32_SINT:
                    return 96;

                case DxgiFormat.R16G16B16A16_TYPELESS:
                case DxgiFormat.R16G16B16A16_FLOAT:
                case DxgiFormat.R16G16B16A16_UNORM:
                case DxgiFormat.R16G16B16A16_UINT:
                case DxgiFormat.R16G16B16A16_SNORM:
                case DxgiFormat.R16G16B16A16_SINT:
                case DxgiFormat.R32G32_TYPELESS:
                case DxgiFormat.R32G32_FLOAT:
                case DxgiFormat.R32G32_UINT:
                case DxgiFormat.R32G32_SINT:
                case DxgiFormat.R32G8X24_TYPELESS:
                case DxgiFormat.D32_FLOAT_S8X24_UINT:
                case DxgiFormat.R32_FLOAT_X8X24_TYPELESS:
                case DxgiFormat.X32_TYPELESS_G8X24_UINT:
                case DxgiFormat.Y416:
                case DxgiFormat.Y210:
                case DxgiFormat.Y216:
                    return 64;

                case DxgiFormat.R10G10B10A2_TYPELESS:
                case DxgiFormat.R10G10B10A2_UNORM:
                case DxgiFormat.R10G10B10A2_UINT:
                case DxgiFormat.R11G11B10_FLOAT:
                case DxgiFormat.R8G8B8A8_TYPELESS:
                case DxgiFormat.R8G8B8A8_UNORM:
                case DxgiFormat.R8G8B8A8_UNORM_SRGB:
                case DxgiFormat.R8G8B8A8_UINT:
                case DxgiFormat.R8G8B8A8_SNORM:
                case DxgiFormat.R8G8B8A8_SINT:
                case DxgiFormat.R16G16_TYPELESS:
                case DxgiFormat.R16G16_FLOAT:
                case DxgiFormat.R16G16_UNORM:
                case DxgiFormat.R16G16_UINT:
                case DxgiFormat.R16G16_SNORM:
                case DxgiFormat.R16G16_SINT:
                case DxgiFormat.R32_TYPELESS:
                case DxgiFormat.D32_FLOAT:
                case DxgiFormat.R32_FLOAT:
                case DxgiFormat.R32_UINT:
                case DxgiFormat.R32_SINT:
                case DxgiFormat.R24G8_TYPELESS:
                case DxgiFormat.D24_UNORM_S8_UINT:
                case DxgiFormat.R24_UNORM_X8_TYPELESS:
                case DxgiFormat.X24_TYPELESS_G8_UINT:
                case DxgiFormat.R9G9B9E5_SHAREDEXP:
                case DxgiFormat.R8G8_B8G8_UNORM:
                case DxgiFormat.G8R8_G8B8_UNORM:
                case DxgiFormat.B8G8R8A8_UNORM:
                case DxgiFormat.B8G8R8X8_UNORM:
                case DxgiFormat.R10G10B10_XR_BIAS_A2_UNORM:
                case DxgiFormat.B8G8R8A8_TYPELESS:
                case DxgiFormat.B8G8R8A8_UNORM_SRGB:
                case DxgiFormat.B8G8R8X8_TYPELESS:
                case DxgiFormat.B8G8R8X8_UNORM_SRGB:
                case DxgiFormat.AYUV:
                case DxgiFormat.Y410:
                case DxgiFormat.YUY2:
                    return 32;

                case DxgiFormat.P010:
                case DxgiFormat.P016:
                    return 24;

                case DxgiFormat.R8G8_TYPELESS:
                case DxgiFormat.R8G8_UNORM:
                case DxgiFormat.R8G8_UINT:
                case DxgiFormat.R8G8_SNORM:
                case DxgiFormat.R8G8_SINT:
                case DxgiFormat.R16_TYPELESS:
                case DxgiFormat.R16_FLOAT:
                case DxgiFormat.D16_UNORM:
                case DxgiFormat.R16_UNORM:
                case DxgiFormat.R16_UINT:
                case DxgiFormat.R16_SNORM:
                case DxgiFormat.R16_SINT:
                case DxgiFormat.B5G6R5_UNORM:
                case DxgiFormat.B5G5R5A1_UNORM:
                case DxgiFormat.A8P8:
                case DxgiFormat.B4G4R4A4_UNORM:
                    return 16;

                case DxgiFormat.NV12:
                case DxgiFormat._420_OPAQUE:
                case DxgiFormat.NV11:
                    return 12;

                case DxgiFormat.R8_TYPELESS:
                case DxgiFormat.R8_UNORM:
                case DxgiFormat.R8_UINT:
                case DxgiFormat.R8_SNORM:
                case DxgiFormat.R8_SINT:
                case DxgiFormat.A8_UNORM:
                case DxgiFormat.AI44:
                case DxgiFormat.IA44:
                case DxgiFormat.P8:
                    return 8;

                case DxgiFormat.R1_UNORM:
                    return 1;

                case DxgiFormat.BC1_TYPELESS:
                case DxgiFormat.BC1_UNORM:
                case DxgiFormat.BC1_UNORM_SRGB:
                case DxgiFormat.BC4_TYPELESS:
                case DxgiFormat.BC4_UNORM:
                case DxgiFormat.BC4_SNORM:
                    return 4;

                case DxgiFormat.BC2_TYPELESS:
                case DxgiFormat.BC2_UNORM:
                case DxgiFormat.BC2_UNORM_SRGB:
                case DxgiFormat.BC3_TYPELESS:
                case DxgiFormat.BC3_UNORM:
                case DxgiFormat.BC3_UNORM_SRGB:
                case DxgiFormat.BC5_TYPELESS:
                case DxgiFormat.BC5_UNORM:
                case DxgiFormat.BC5_SNORM:
                case DxgiFormat.BC6H_TYPELESS:
                case DxgiFormat.BC6H_UF16:
                case DxgiFormat.BC6H_SF16:
                case DxgiFormat.BC7_TYPELESS:
                case DxgiFormat.BC7_UNORM:
                case DxgiFormat.BC7_UNORM_SRGB:
                    return 8;

                default:
                    return 0;
            }
        }
    }

    internal struct SurfaceInfo
    {
        public int NumBytes;
        public int RowBytes;
        public int NumRows;
    }
}
