using CuldceptSagaExplorer.GraphicsManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using System;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [Category("Camera")]
    public class ModelPreviewCameraTests
    {
        private static readonly Vector3 Center = new Vector3(2, 3, 4);
        private const float Radius = 2;

        [TestCase(800, 600)]
        [TestCase(300, 900)]
        public void DefaultViewPreservesOriginalFraming(int Width, int Height)
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            float Aspect = (float)Width / Height;
            float HalfAngle = Math.Min(MathHelper.PiOver4 * 0.5f,
                (float)Math.Atan(Math.Tan(MathHelper.PiOver4 * 0.5f) * Aspect));
            float Distance = Radius / (float)Math.Sin(HalfAngle) * 1.15f;
            Matrix Expected = Matrix.CreateLookAt(Center + Vector3.Normalize(new Vector3(1, 0.65f, 1))
                * Distance, Center, Vector3.Up);
            Assert.That(Vector3.Distance(Matrix.Invert(Camera.CreateView(Aspect)).Translation,
                Matrix.Invert(Expected).Translation), Is.LessThan(0.00001f));
            AssertCentered(Camera, new Viewport(0, 0, Width, Height));
        }

        [TestCase(0)]
        [TestCase(120)]
        public void PanMovesTheModelByTheDraggedPixels(float Rotation)
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            Viewport CameraViewport = new Viewport(0, 0, 800, 600);
            Camera.Rotate(Rotation, 20);
            Camera.Zoom(3);
            Vector3 Before = Project(Camera, CameraViewport);
            Camera.Pan(40, -25, CameraViewport);
            Vector3 After = Project(Camera, CameraViewport);
            Assert.That(After.X - Before.X, Is.EqualTo(40).Within(0.001));
            Assert.That(After.Y - Before.Y, Is.EqualTo(-25).Within(0.001));
        }

        [Test]
        public void RotationKeepsTheTargetCenteredAndCannotFlipAtThePoles()
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            Viewport CameraViewport = new Viewport(0, 0, 600, 600);
            float Distance = Vector3.Distance(Center, Matrix.Invert(Camera.CreateView(1)).Translation);
            foreach (float Vertical in new[] { 100000f, -200000f, 100f })
            {
                Camera.Rotate(170, Vertical);
                Vector3 Position = Matrix.Invert(Camera.CreateView(1)).Translation;
                Assert.That(Vector3.Distance(Position, Center), Is.EqualTo(Distance).Within(0.001));
                AssertCentered(Camera, CameraViewport);
            }
        }

        [Test]
        public void WheelMovesAlongTheViewDirectionAndHasSafeDistanceLimits()
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            Vector3 Original = Matrix.Invert(Camera.CreateView(1)).Translation - Center;
            Camera.Zoom(1);
            Vector3 Closer = Matrix.Invert(Camera.CreateView(1)).Translation - Center;
            Assert.That(Closer.Length(), Is.EqualTo(Original.Length() * Math.Exp(-0.15)).Within(0.00001));
            Assert.That(Vector3.Dot(Vector3.Normalize(Closer), Vector3.Normalize(Original)),
                Is.EqualTo(1).Within(0.00001));
            Camera.Zoom(-1);
            Assert.That(Vector3.Distance(Matrix.Invert(Camera.CreateView(1)).Translation,
                Center + Original), Is.LessThan(0.00001));
            foreach (float Steps in new[] { 10000f, -20000f })
            {
                Camera.Zoom(Steps);
                Vector3 Position = Matrix.Invert(Camera.CreateView(1)).Translation;
                Assert.That(float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z));
                Assert.That(Vector3.Distance(Position, Center), Is.GreaterThan(Radius * 0.001f));
                AssertCentered(Camera, new Viewport(0, 0, 600, 600));
                Assert.That(float.IsFinite(Camera.CreateProjection(1).M33));
            }
        }

        [Test]
        public void NearPlaneStaysInFrontOfTheModelUntilTheCameraDolliesInside()
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            float Distance = Radius / (float)Math.Sin(MathHelper.PiOver4 * 0.5f) * 1.15f;
            float Front = Distance - Radius;
            float Near = NearPlane(Camera.CreateProjection(1));
            Assert.That(Near, Is.LessThan(Front));
            Assert.That(Near, Is.GreaterThan(Front * 0.4f));

            Camera.Zoom(10000f);
            Assert.That(NearPlane(Camera.CreateProjection(1)), Is.EqualTo(Radius * 0.001f).Within(0.0000001f));
        }

        [Test]
        public void ResetRestoresRotationPanAndZoom()
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            Matrix View = Camera.CreateView(1);
            Matrix Projection = Camera.CreateProjection(1);
            Camera.Rotate(70, -90);
            Camera.Pan(25, 80, new Viewport(0, 0, 600, 600));
            Camera.Zoom(-3);
            Camera.Reset();
            Assert.That(Camera.CreateView(1), Is.EqualTo(View));
            Assert.That(Camera.CreateProjection(1), Is.EqualTo(Projection));
        }

        [Test]
        public void InvalidInputsAreRejected()
        {
            ModelPreviewCamera Camera = new ModelPreviewCamera(Center, Radius);
            Assert.Throws<ArgumentOutOfRangeException>(() => Camera.Zoom(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Camera.Rotate(float.PositiveInfinity, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Camera.Pan(1, 1, new Viewport()));
            Assert.Throws<ArgumentOutOfRangeException>(() => Camera.CreateView(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Camera.CreateProjection(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModelPreviewCamera(Center, 0));
        }

        private static float NearPlane(Matrix Projection)
        {
            return Projection.M43 / Projection.M33;
        }

        private static Vector3 Project(ModelPreviewCamera Camera, Viewport CameraViewport)
        {
            return CameraViewport.Project(Center, Camera.CreateProjection(CameraViewport.AspectRatio),
                Camera.CreateView(CameraViewport.AspectRatio), Matrix.Identity);
        }

        private static void AssertCentered(ModelPreviewCamera Camera, Viewport CameraViewport)
        {
            Vector3 Projected = Project(Camera, CameraViewport);
            Assert.That(Projected.X, Is.EqualTo(CameraViewport.Width / 2f).Within(0.01));
            Assert.That(Projected.Y, Is.EqualTo(CameraViewport.Height / 2f).Within(0.01));
        }
    }
}
