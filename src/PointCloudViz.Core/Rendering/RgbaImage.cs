using PointCloudViz.Core.Data;

namespace PointCloudViz.Core.Rendering;

/// <summary>简单的 RGBA8 位图。</summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>按行存储的 RGBA 字节。</summary>
    public byte[] Pixels { get; }

    public void Fill(Rgb24 color)
    {
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            Pixels[i] = color.R;
            Pixels[i + 1] = color.G;
            Pixels[i + 2] = color.B;
            Pixels[i + 3] = 255;
        }
    }

    public Rgb24 GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new Rgb24(Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    public void SetPixel(int x, int y, Rgb24 c)
    {
        int i = (y * Width + x) * 4;
        Pixels[i] = c.R;
        Pixels[i + 1] = c.G;
        Pixels[i + 2] = c.B;
        Pixels[i + 3] = 255;
    }

    public void SavePng(string path) => File.WriteAllBytes(path, PngEncoder.Encode(this));
}
