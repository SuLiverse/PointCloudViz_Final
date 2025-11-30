using System;
using System.Numerics;

namespace PointCloudViz_Final.Models
{
    public class Camera
    {
        public float Yaw { get; set; } = 45f;
        public float Pitch { get; set; } = 20f;
        public float Distance { get; set; } = 10f;
        public Vector3 Target { get; set; } = Vector3.Zero;

        public float Fov { get; set; } = 60f * (float)Math.PI / 180f;
        public float Near { get; set; } = 0.01f;
        public float Far  { get; set; } = 10000f;

        public Matrix4x4 ViewMatrix
        {
            get
            {
                var yawRad = Yaw * (float)Math.PI / 180f;
                var pitchRad = Pitch * (float)Math.PI / 180f;
                var dir = new Vector3(
                    (float)(Math.Cos(pitchRad) * Math.Cos(yawRad)),
                    (float)(Math.Sin(pitchRad)),
                    (float)(Math.Cos(pitchRad) * Math.Sin(yawRad))
                );
                // 右手系：把相机放在目标后方，朝向 Target
                var position = Target - dir * Distance;
                return Matrix4x4.CreateLookAt(position, Target, new Vector3(0, 1, 0));
            }
        }

        public Matrix4x4 ProjectionMatrix(float aspect)
            => Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspect, Near, Far);

        public void ResetToBBox(BoundingBox bbox)
        {
            var sizeX = bbox.MaxX - bbox.MinX;
            var sizeY = bbox.MaxY - bbox.MinY;
            var sizeZ = bbox.MaxZ - bbox.MinZ;
            var maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));

            Target = new Vector3(
                (bbox.MinX + bbox.MaxX) * 0.5f,
                (bbox.MinY + bbox.MaxY) * 0.5f,
                (bbox.MinZ + bbox.MaxZ) * 0.5f
            );
            Distance = Math.Max(1f, maxSize * 1.3f + 1f);
            Yaw = 45f; Pitch = 20f;
        }
    }
}
