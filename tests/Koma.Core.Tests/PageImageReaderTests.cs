using Koma.Core.Model;

namespace Koma.Core.Tests;

/// <summary>
/// What a JPEG frame header says about its colour model.
/// </summary>
public sealed class PageImageReaderTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void TellsCmykFromTheComponentCount(byte components, bool cmyk)
    {
        // Start of image, then a baseline frame header of 20 by 10 pixels:
        // precision, height, width, component count, and three bytes a
        // component. Nothing past the frame header is read.
        byte[] frame = [8, 0, 10, 0, 20, components, .. Enumerable.Repeat((byte)0, components * 3)];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, (byte)(frame.Length + 2), .. frame];

        PageImageFacts? facts = PageImageReader.TryRead(jpeg);

        Assert.NotNull(facts);
        Assert.Equal((20, 10), (facts.Width, facts.Height));
        Assert.Equal(cmyk, facts.IsCmyk);
    }
}
