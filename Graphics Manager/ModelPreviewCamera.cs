using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace CuldceptSagaExplorer.GraphicsManager
{
    public sealed class ModelPreviewCamera
    {
        private const float FieldOfView = MathHelper.PiOver4;
        private const float RotationSpeed = 0.01f;
        private const float PitchLimit = MathHelper.PiOver2 - 0.01f;
        private readonly Vector3 Center;
        private readonly float Radius;
        private float Yaw;
        private float Pitch;
        private float DistanceScale;
        private Vector3 PanOffset;

        public ModelPreviewCamera(Vector3 Center, float Radius)
        {
            if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y) || !float.IsFinite(Center.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(Center));
            }
            if (!float.IsFinite(Radius) || Radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Radius));
            }
            this.Center = Center;
            this.Radius = Radius;
            Reset();
        }

        public void Reset()
        {
            Yaw = MathHelper.PiOver4;
            Pitch = (float)Math.Atan(0.65 / Math.Sqrt(2));
            DistanceScale = 1;
            PanOffset = Vector3.Zero;
        }

        public void Rotate(float DeltaX, float DeltaY)
        {
            ValidateFinite(DeltaX, nameof(DeltaX));
            ValidateFinite(DeltaY, nameof(DeltaY));
            Yaw = MathHelper.WrapAngle(Yaw - DeltaX * RotationSpeed);
            Pitch = MathHelper.Clamp(Pitch + DeltaY * RotationSpeed, -PitchLimit, PitchLimit);
        }

        public void Pan(float DeltaX, float DeltaY, Viewport Viewport)
        {
            ValidateFinite(DeltaX, nameof(DeltaX));
            ValidateFinite(DeltaY, nameof(DeltaY));
            if (Viewport.Width <= 0 || Viewport.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Viewport));
            }
            Vector3 Direction = GetDirection();
            Vector3 Right = Vector3.Normalize(Vector3.Cross(Vector3.Up, Direction));
            Vector3 Up = Vector3.Cross(Direction, Right);
            float UnitsPerPixel = 2 * GetDistance(Viewport.AspectRatio)
                * (float)Math.Tan(FieldOfView * 0.5f) / Viewport.Height;
            PanOffset += (-Right * DeltaX + Up * DeltaY) * UnitsPerPixel;
        }

        public void Zoom(float WheelSteps)
        {
            ValidateFinite(WheelSteps, nameof(WheelSteps));
            DistanceScale = (float)Math.Clamp(DistanceScale * Math.Exp(-WheelSteps * 0.15), 0.02, 100);
        }

        public Matrix CreateView(float AspectRatio)
        {
            Vector3 Target = Center + PanOffset;
            return Matrix.CreateLookAt(Target + GetDirection() * GetDistance(AspectRatio), Target, Vector3.Up);
        }

        public Matrix CreateProjection(float AspectRatio)
        {
            float Distance = GetDistance(AspectRatio);
            float Far = Distance + PanOffset.Length() + Radius * 4;
            // Layered cloth sits a fraction of a unit apart. A near plane at Radius*0.001
            // leaves too few depth steps between those layers, so they flicker. While the
            // camera is outside the model, keep half the clearance in front of it. Dolly
            // inside and the near plane falls back to the small floor.
            float Clearance = Distance - PanOffset.Length() - Radius;
            float Near = Math.Max(Radius * 0.001f, Clearance * 0.5f);
            if (Near >= Far)
            {
                Near = Math.Max(Radius * 0.001f, Far * 0.001f);
            }
            return Matrix.CreatePerspectiveFieldOfView(FieldOfView, AspectRatio, Near, Far);
        }

        private float GetDistance(float AspectRatio)
        {
            ValidateFinite(AspectRatio, nameof(AspectRatio));
            if (AspectRatio <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(AspectRatio));
            }
            float HalfAngle = Math.Min(FieldOfView * 0.5f,
                (float)Math.Atan(Math.Tan(FieldOfView * 0.5f) * AspectRatio));
            return Radius / (float)Math.Sin(HalfAngle) * 1.15f * DistanceScale;
        }

        private Vector3 GetDirection()
        {
            float Horizontal = (float)Math.Cos(Pitch);
            return new Vector3(Horizontal * (float)Math.Sin(Yaw),
                (float)Math.Sin(Pitch), Horizontal * (float)Math.Cos(Yaw));
        }

        private static void ValidateFinite(float Value, string Parameter)
        {
            if (!float.IsFinite(Value))
            {
                throw new ArgumentOutOfRangeException(Parameter);
            }
        }
    }
}
