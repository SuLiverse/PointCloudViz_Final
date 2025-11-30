using System.Windows.Media;

namespace PointCloudViz_Final.Models
{
    public struct PointRecord
    {
        public float X;
        public float Y;
        public float Z;
        public float Intensity;
        public Color Color;

        public PointRecord(float x, float y, float z, float intensity = 0, Color? color = null)
        {
            X = x; Y = y; Z = z;
            Intensity = intensity;
            Color = color ?? Colors.White;
        }
    }
}
