using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.SceneGraph;

[TestClass]
public sealed class Camera2DTests
{
    private static readonly Vector2 Hd = new(1920f, 1080f);
    private static readonly Vector2 HdCenter = new(960f, 540f);

    [TestMethod]
    public void PositionLandsExactlyOnTheVirtualCenter()
    {
        var camera = new Camera2D
        {
            Position = new Vector2(7f, -3f),
            Rotation = Angle.Deg(33d),
            Zoom = 2.5f,
        };

        var mapped = Vector2.Transform(camera.Position, camera.WorldToVirtual(Hd));

        AssertVectorClose(HdCenter, mapped);
    }

    [TestMethod]
    public void UnitZoomMapsOneWorldUnitToOneVirtualUnit()
    {
        var camera = new Camera2D();

        var matrix = camera.WorldToVirtual(Hd);
        var origin = Vector2.Transform(Vector2.Zero, matrix);
        var alongX = Vector2.Transform(new Vector2(1f, 0f), matrix);
        var alongY = Vector2.Transform(new Vector2(0f, 1f), matrix);

        Assert.AreEqual(1f, camera.Zoom);
        AssertVectorClose(HdCenter, origin);
        Assert.AreEqual(1f, alongX.X - origin.X, 1e-4f);
        Assert.AreEqual(1f, MathF.Abs(alongY.Y - origin.Y), 1e-4f);
    }

    [TestMethod]
    public void ZoomOfFourQuadruplesOnScreenSize()
    {
        var camera = new Camera2D();
        var first = new Vector2(-3f, 2f);
        var second = new Vector2(5f, 9f);

        var unitZoom = Vector2.Distance(
            Vector2.Transform(first, camera.WorldToVirtual(Hd)),
            Vector2.Transform(second, camera.WorldToVirtual(Hd)));

        camera.Zoom = 4f;
        var quadrupleZoom = Vector2.Distance(
            Vector2.Transform(first, camera.WorldToVirtual(Hd)),
            Vector2.Transform(second, camera.WorldToVirtual(Hd)));

        Assert.AreEqual(4f * unitZoom, quadrupleZoom, 1e-3f);
    }

    [TestMethod]
    public void IncreasingWorldYMapsToDecreasingVirtualY()
    {
        var camera = new Camera2D { Zoom = 3f };
        var matrix = camera.WorldToVirtual(Hd);

        var up = Vector2.Transform(new Vector2(0f, 10f), matrix);
        var down = Vector2.Transform(new Vector2(0f, -10f), matrix);
        var right = Vector2.Transform(new Vector2(10f, 0f), matrix);

        // The single flip in the engine: world +Y is visually up, virtual +Y is visually down.
        Assert.IsLessThan(
            HdCenter.Y,
            up.Y,
            $"World +Y must map to a smaller virtual Y than the center, but mapped to {up.Y}.");
        Assert.IsGreaterThan(
            HdCenter.Y,
            down.Y,
            $"World -Y must map to a larger virtual Y than the center, but mapped to {down.Y}.");
        Assert.IsGreaterThan(
            HdCenter.X,
            right.X,
            $"World +X must keep its sign, but mapped to {right.X}.");
        Assert.AreEqual(HdCenter.Y - 30f, up.Y, 1e-4f);
        Assert.AreEqual(HdCenter.Y + 30f, down.Y, 1e-4f);
        Assert.AreEqual(3f, matrix.M11, 1e-6f);
        Assert.AreEqual(-3f, matrix.M22, 1e-6f);
    }

    [TestMethod]
    public void RotationTurnsAboutPositionAndNotAboutTheWorldOrigin()
    {
        var virtualSize = new Vector2(200f, 200f);
        var center = new Vector2(100f, 100f);
        var camera = new Camera2D
        {
            Position = new Vector2(100f, 0f),
            Rotation = Angle.Deg(90d),
        };

        var matrix = camera.WorldToVirtual(virtualSize);
        var pivot = Vector2.Transform(camera.Position, matrix);
        var oneRightOfPivot = Vector2.Transform(camera.Position + new Vector2(1f, 0f), matrix);
        var worldOrigin = Vector2.Transform(Vector2.Zero, matrix);

        AssertVectorClose(center, pivot);
        AssertVectorClose(new Vector2(100f, 101f), oneRightOfPivot);

        // Rotating about the world origin would have left the origin at the virtual center.
        AssertVectorClose(new Vector2(100f, 0f), worldOrigin);
        Assert.IsGreaterThan(1f, Vector2.Distance(worldOrigin, center));
    }

    [TestMethod]
    public void WorldToVirtualAndVirtualToWorldRoundTrip()
    {
        var camera = new Camera2D
        {
            Position = new Vector2(-12.5f, 41.25f),
            Rotation = Angle.Deg(37d),
            Zoom = 6.75f,
        };
        var point = new Vector2(3.5f, -8.25f);

        var virtualPoint = Vector2.Transform(point, camera.WorldToVirtual(Hd));
        var worldPoint = Vector2.Transform(virtualPoint, camera.VirtualToWorld(Hd));

        AssertVectorClose(point, worldPoint, 1e-3f);
        AssertVectorClose(
            camera.Position,
            Vector2.Transform(HdCenter, camera.VirtualToWorld(Hd)),
            1e-3f);
    }

    [TestMethod]
    public void CenterOnMovesPositionOnly()
    {
        var camera = new Camera2D { Zoom = 2f, Rotation = Angle.Deg(15d) };

        camera.CenterOn(new Vector2(4f, 9f));

        Assert.AreEqual(new Vector2(4f, 9f), camera.Position);
        Assert.AreEqual(2f, camera.Zoom);
        Assert.AreEqual(Angle.Deg(15d), camera.Rotation);
        AssertVectorClose(HdCenter, Vector2.Transform(new Vector2(4f, 9f), camera.WorldToVirtual(Hd)));
    }

    [TestMethod]
    public void FitRectFitsAWideRectWithWidthAsTheLimitingAxis()
    {
        var camera = new Camera2D();
        var rect = new Rect(-20f, -5f, 40f, 10f);

        camera.FitRect(rect, Hd);
        var matrix = camera.WorldToVirtual(Hd);
        var leftTop = Vector2.Transform(new Vector2(rect.Left, rect.Bottom), matrix);
        var rightBottom = Vector2.Transform(new Vector2(rect.Right, rect.Top), matrix);

        Assert.AreEqual(48f, camera.Zoom, 1e-4f);
        Assert.AreEqual(Vector2.Zero, camera.Position);

        // Width is limiting: it touches both virtual edges exactly.
        Assert.AreEqual(0f, leftTop.X, 1e-3f);
        Assert.AreEqual(Hd.X, rightBottom.X, 1e-3f);

        // Height has slack and stays strictly inside.
        Assert.AreEqual(300f, leftTop.Y, 1e-3f);
        Assert.AreEqual(780f, rightBottom.Y, 1e-3f);
        Assert.IsTrue(leftTop.Y > 0f && rightBottom.Y < Hd.Y);
    }

    [TestMethod]
    public void FitRectFitsATallRectWithHeightAsTheLimitingAxis()
    {
        var camera = new Camera2D();
        var rect = new Rect(6f, 30f, 10f, 40f);

        camera.FitRect(rect, Hd);
        var matrix = camera.WorldToVirtual(Hd);
        var leftTop = Vector2.Transform(new Vector2(rect.Left, rect.Bottom), matrix);
        var rightBottom = Vector2.Transform(new Vector2(rect.Right, rect.Top), matrix);

        Assert.AreEqual(27f, camera.Zoom, 1e-4f);
        Assert.AreEqual(new Vector2(11f, 50f), camera.Position);

        // Height is limiting: the world top edge lands on virtual Y zero because of the flip.
        Assert.AreEqual(0f, leftTop.Y, 1e-3f);
        Assert.AreEqual(Hd.Y, rightBottom.Y, 1e-3f);

        // Width has slack and stays strictly inside.
        Assert.AreEqual(825f, leftTop.X, 1e-3f);
        Assert.AreEqual(1095f, rightBottom.X, 1e-3f);
        Assert.IsTrue(leftTop.X > 0f && rightBottom.X < Hd.X);
    }

    [TestMethod]
    public void FitRectKeepsARotatedRectFullyVisible()
    {
        var camera = new Camera2D { Rotation = Angle.Deg(90d) };
        var rect = new Rect(-20f, -5f, 40f, 10f);

        camera.FitRect(rect, Hd);
        var matrix = camera.WorldToVirtual(Hd);

        // A quarter turn swaps the spans, so height now limits: min(1920/10, 1080/40) = 27.
        Assert.AreEqual(27f, camera.Zoom, 1e-4f);
        foreach (var corner in new[]
                 {
                     new Vector2(rect.Left, rect.Top),
                     new Vector2(rect.Right, rect.Top),
                     new Vector2(rect.Left, rect.Bottom),
                     new Vector2(rect.Right, rect.Bottom),
                 })
        {
            var mapped = Vector2.Transform(corner, matrix);
            Assert.IsTrue(
                mapped.X >= -1e-3f && mapped.X <= Hd.X + 1e-3f &&
                mapped.Y >= -1e-3f && mapped.Y <= Hd.Y + 1e-3f,
                $"Corner {corner} mapped outside the viewport at {mapped}.");
        }
    }

    [TestMethod]
    public void InvalidZoomAndViewportAndRectAreRejected()
    {
        var camera = new Camera2D();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Zoom = 0f);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Zoom = -1f);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Zoom = float.NaN);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.Zoom = float.PositiveInfinity);
        Assert.AreEqual(1f, camera.Zoom);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.WorldToVirtual(new Vector2(0f, 1080f)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.WorldToVirtual(new Vector2(1920f, -1f)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.VirtualToWorld(new Vector2(float.NaN, 1f)));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.FitRect(new Rect(0f, 0f, 0f, 10f), Hd));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.FitRect(new Rect(0f, 0f, 10f, 0f), Hd));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.FitRect(default, Hd));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => camera.FitRect(new Rect(0f, 0f, 10f, 10f), Vector2.Zero));
    }

    [TestMethod]
    public void UnrepresentableFramingFailsLoudlyInsteadOfReturningGarbage()
    {
        var camera = new Camera2D
        {
            Position = new Vector2(1e30f, 1e30f),
            Zoom = 1e30f,
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => camera.WorldToVirtual(Hd));
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.VirtualToWorld(Hd));
    }

    [TestMethod]
    public void CameraIsAnOrdinaryNodeInTheTree()
    {
        var parent = new Node2D { Position = new Vector2(10f, 10f) };
        var camera = parent.Add(new Camera2D { Position = new Vector2(1f, 2f) });

        Assert.AreSame(parent, camera.Parent);
        Assert.AreEqual(new Vector2(11f, 12f), camera.WorldPosition);
    }

    private static void AssertVectorClose(Vector2 expected, Vector2 actual, float tolerance = 1e-4f)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.Y, actual.Y, tolerance, $"Expected {expected} but found {actual}.");
    }
}
