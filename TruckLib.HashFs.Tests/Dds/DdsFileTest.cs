using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs.Dds;

namespace TruckLib.HashFs.Tests.Dds
{
    public class DdsFileTest
    {
        DdsFile ddsFile;

        public DdsFileTest() 
        {
            ddsFile = DdsFile.Open("Data/Dds/sample.dds");
        }

        [Fact]
        public void DeserializedHeaderCorrectly()
        {
            Assert.Equal(128u, ddsFile.Header.Width);
            Assert.Equal(128u, ddsFile.Header.Height);
            Assert.Equal(DdsPixelFormat.FourCC_DX10, ddsFile.Header.PixelFormat.FourCC);
            Assert.True(ddsFile.Header.PixelFormat.HasCompressedRgbData);
            Assert.False(ddsFile.Header.PixelFormat.HasUncompressedRgbData);
        }

        [Fact]
        public void DeserializedHeaderDxt10Correctly() 
        {
            Assert.Equal(DxgiFormat.BC3_UNORM_SRGB, ddsFile.HeaderDxt10.Format);
            Assert.Equal(D3d10ResourceDimension.Texture2d, ddsFile.HeaderDxt10.ResourceDimension);
        }
    }
}
