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

    [TestMethod]
    public void UnparentedFramingIsTheCamerasOwnLocalPositionAndRotation()
    {
        // The world-transform rule must be a no-op for an unparented camera: its world values are
        // its local ones. Derived independently of the implementation, from the documented
        // composition Translate(-p) * Rotate(-θ) * Scale(z, -z) * Translate(size / 2).
        var camera = new Camera2D
        {
            Position = new Vector2(7f, -3f),
            Rotation = Angle.Deg(33d),
            Zoom = 2.5f,
        };
        var expected =
            Matrix3x2.CreateTranslation(new Vector2(-7f, 3f)) *
            Matrix3x2.CreateRotation((float)-Angle.Deg(33d).Radians) *
            Matrix3x2.CreateScale(2.5f, -2.5f) *
            Matrix3x2.CreateTranslation(HdCenter);

        AssertMatrixClose(expected, camera.WorldToVirtual(Hd));
    }

    [TestMethod]
    public void ParentedCameraFramesFromItsRigsWorldPositionAndNotItsLocalOne()
    {
        // 200×100 virtual space, centre (100, 50), zoom 1. A rig at world (3, 4) carries a camera
        // whose own Position is the origin, so the camera sits at world (3, 4) and that point is
        // what lands on the centre. The world origin is 3 left of and 4 below the camera, and the Y
        // flip turns "below" into a larger virtual Y: (100 − 3, 50 + 4) = (97, 54).
        // Framing from the camera's local position instead would have put the world origin on the
        // centre — a follow-cam framing the wrong region entirely.
        var virtualSize = new Vector2(200f, 100f);
        var center = new Vector2(100f, 50f);
        var rig = new Node2D { Position = new Vector2(3f, 4f) };
        var camera = rig.Add(new Camera2D());

        var matrix = camera.WorldToVirtual(virtualSize);

        AssertVectorClose(center, Vector2.Transform(camera.WorldPosition, matrix));
        AssertVectorClose(new Vector2(97f, 54f), Vector2.Transform(Vector2.Zero, matrix));

        // The camera's own offset composes on top of the rig's: world (3+1, 4+2) = (4, 6), which
        // moves the framed centre one right and two up, so the world origin lands at
        // (100 − 4, 50 + 6) = (96, 56).
        camera.Position = new Vector2(1f, 2f);
        var moved = camera.WorldToVirtual(virtualSize);

        Assert.AreEqual(new Vector2(4f, 6f), camera.WorldPosition);
        AssertVectorClose(center, Vector2.Transform(new Vector2(4f, 6f), moved));
        AssertVectorClose(new Vector2(96f, 56f), Vector2.Transform(Vector2.Zero, moved));
    }

    [TestMethod]
    public void CameraUnderARotatedAncestorPicksUpThatRotation()
    {
        // The rig turns a quarter turn about the world origin, so a camera at rig-local (2, 0) sits
        // at world (0, 2) and views the world turned a quarter turn. Under that view, one world
        // unit of +X from the camera maps one virtual unit of +Y (the same relation the unrotated
        // case gives for +X→+X, turned by 90° and then Y-flipped): (100, 51).
        // Reading the camera's local rotation instead would leave the view unturned and land (101, 50).
        var virtualSize = new Vector2(200f, 100f);
        var center = new Vector2(100f, 50f);
        var rig = new Node2D { Rotation = Angle.Deg(90d) };
        var camera = rig.Add(new Camera2D { Position = new Vector2(2f, 0f) });

        var matrix = camera.WorldToVirtual(virtualSize);

        AssertVectorClose(new Vector2(0f, 2f), camera.WorldPosition);
        AssertVectorClose(center, Vector2.Transform(new Vector2(0f, 2f), matrix));
        AssertVectorClose(new Vector2(100f, 51f), Vector2.Transform(new Vector2(1f, 2f), matrix));

        // Rotation accumulates up the chain exactly as the world matrix does: the rig's 90° plus
        // the camera's own -30° is a 60° view, indistinguishable from an unparented camera holding
        // the whole 60° at the same world position.
        camera.Rotation = Angle.Deg(-30d);
        var unparented = new Camera2D { Position = camera.WorldPosition, Rotation = Angle.Deg(60d) };

        AssertMatrixClose(
            unparented.WorldToVirtual(virtualSize),
            camera.WorldToVirtual(virtualSize),
            1e-5f);
    }

    [TestMethod]
    public void AncestorScaleNeverReachesTheFraming()
    {
        // Scale is Zoom's job alone, ancestor scale included. A rig scaled 3× at world (5, -2)
        // carries a camera at rig-local origin, so the camera still sits at world (5, -2) and one
        // world unit must still be one virtual unit: world (6, -2) lands one virtual unit right of
        // the centre. A rig scale that leaked in would have made it three.
        var virtualSize = new Vector2(200f, 100f);
        var center = new Vector2(100f, 50f);
        var rig = new Node2D { Position = new Vector2(5f, -2f), Scale = new Vector2(3f, 3f) };
        var camera = rig.Add(new Camera2D());

        var matrix = camera.WorldToVirtual(virtualSize);

        AssertVectorClose(center, Vector2.Transform(new Vector2(5f, -2f), matrix));
        AssertVectorClose(center + new Vector2(1f, 0f), Vector2.Transform(new Vector2(6f, -2f), matrix));
        Assert.AreEqual(1f, camera.Zoom, "The rig's scale must not have moved the camera's zoom.");

        // A non-uniform, mirroring, rotated rig is the hard case: the rotation must survive and the
        // scale must not, which is exactly an unparented camera holding that rotation at the same
        // world position. A decomposition of the world matrix would have skewed the framing here.
        rig.Scale = new Vector2(-2f, 0.5f);
        rig.Rotation = Angle.Deg(90d);
        var unparented = new Camera2D { Position = camera.WorldPosition, Rotation = Angle.Deg(90d) };

        AssertMatrixClose(unparented.WorldToVirtual(virtualSize), camera.WorldToVirtual(virtualSize), 1e-5f);

        // The camera's own scale is just as inert, which is what the class already promises.
        camera.Scale = new Vector2(7f, 7f);

        AssertMatrixClose(unparented.WorldToVirtual(virtualSize), camera.WorldToVirtual(virtualSize), 1e-5f);
    }

    [TestMethod]
    public void CenterOnAndFitRectPlaceAParentedCameraByItsWorldPosition()
    {
        // Both helpers are documented in world terms, and framing now reads the world position, so
        // both must land the camera's world position on the world point. Under a rig at (10, 0),
        // centring on world (4, 9) is the rig-local position (-6, 9).
        var rig = new Node2D { Position = new Vector2(10f, 0f) };
        var camera = rig.Add(new Camera2D());

        camera.CenterOn(new Vector2(4f, 9f));

        Assert.AreEqual(new Vector2(-6f, 9f), camera.Position);
        AssertVectorClose(new Vector2(4f, 9f), camera.WorldPosition);
        AssertVectorClose(HdCenter, Vector2.Transform(new Vector2(4f, 9f), camera.WorldToVirtual(Hd)));

        // The 40×10 rect fits width-limited at 1920/40 = 48, centred on the world origin, so the
        // camera's world position is the origin and its rig-local position is (-10, 0).
        camera.FitRect(new Rect(-20f, -5f, 40f, 10f), Hd);

        Assert.AreEqual(48f, camera.Zoom, 1e-4f);
        Assert.AreEqual(new Vector2(-10f, 0f), camera.Position);
        AssertVectorClose(Vector2.Zero, camera.WorldPosition);

        var matrix = camera.WorldToVirtual(Hd);
        Assert.AreEqual(0f, Vector2.Transform(new Vector2(-20f, 5f), matrix).X, 1e-3f);
        Assert.AreEqual(Hd.X, Vector2.Transform(new Vector2(20f, -5f), matrix).X, 1e-3f);
    }

    private static void AssertVectorClose(Vector2 expected, Vector2 actual, float tolerance = 1e-4f)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.Y, actual.Y, tolerance, $"Expected {expected} but found {actual}.");
    }

    private static void AssertMatrixClose(Matrix3x2 expected, Matrix3x2 actual, float tolerance = 1e-4f)
    {
        Assert.AreEqual(expected.M11, actual.M11, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.M12, actual.M12, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.M21, actual.M21, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.M22, actual.M22, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.M31, actual.M31, tolerance, $"Expected {expected} but found {actual}.");
        Assert.AreEqual(expected.M32, actual.M32, tolerance, $"Expected {expected} but found {actual}.");
    }
}
