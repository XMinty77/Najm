using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.SceneGraph;

[TestClass]
public sealed class Transform2DTests
{
    [TestMethod]
    public void TranslatedParentAndRotatedChildFollowRowVectorConvention()
    {
        var parent = new Node2D { Position = new Vector2(10f, 20f) };
        var child = parent.Add(new Node2D
        {
            Position = new Vector2(2f, 0f),
            Rotation = Angle.Deg(90d),
        });

        var transformedPoint = Vector2.Transform(Vector2.UnitX, child.WorldMatrix);

        AssertVectorClose(new Vector2(12f, 20f), child.WorldPosition);
        AssertVectorClose(new Vector2(12f, 21f), transformedPoint);

        var expectedLocal =
            Matrix3x2.CreateScale(Vector2.One) *
            Matrix3x2.CreateRotation(MathF.PI / 2f) *
            Matrix3x2.CreateTranslation(2f, 0f);
        AssertMatrixClose(expectedLocal, child.LocalMatrix);
        AssertMatrixClose(expectedLocal * parent.WorldMatrix, child.WorldMatrix);
    }

    [TestMethod]
    public void RemovalAndReparentInvalidateEntireWorldSubtree()
    {
        var firstParent = new Node2D { Position = new Vector2(10f, 0f) };
        var secondParent = new Node2D { Position = new Vector2(20f, 0f) };
        var child = firstParent.Add(new Node2D { Position = new Vector2(1f, 0f) });
        var grandchild = child.Add(new Node2D { Position = new Vector2(2f, 0f) });

        AssertVectorClose(new Vector2(11f, 0f), child.WorldPosition);
        AssertVectorClose(new Vector2(13f, 0f), grandchild.WorldPosition);
        _ = grandchild.InverseWorld;

        Assert.IsTrue(firstParent.Remove(child));
        AssertVectorClose(new Vector2(1f, 0f), child.WorldPosition);
        AssertVectorClose(new Vector2(3f, 0f), grandchild.WorldPosition);

        secondParent.Add(child);
        AssertVectorClose(new Vector2(21f, 0f), child.WorldPosition);
        AssertVectorClose(new Vector2(23f, 0f), grandchild.WorldPosition);
        AssertVectorClose(Vector2.Zero, Vector2.Transform(grandchild.WorldPosition, grandchild.InverseWorld));
    }

    [TestMethod]
    public void InverseAndCrossNodeConversionsRoundTrip()
    {
        var root = new Node2D
        {
            Position = new Vector2(4f, -2f),
            Rotation = Angle.Deg(12d),
        };
        var first = root.Add(new Node2D
        {
            Position = new Vector2(2f, 1f),
            Rotation = Angle.Deg(30d),
            Scale = new Vector2(1.5f, 0.75f),
        });
        var second = root.Add(new Node2D
        {
            Position = new Vector2(-3f, 4f),
            Rotation = Angle.Deg(-20d),
            Scale = new Vector2(2f, 1.25f),
        });
        var point = new Vector2(0.5f, -0.2f);

        var world = Vector2.Transform(point, first.WorldMatrix);
        var expectedInSecond = Vector2.Transform(world, second.InverseWorld);
        var actualInSecond = first.ToLocalOf(second, point);
        var viaMatrix = Vector2.Transform(point, first.TransformTo(second));
        var roundTrip = second.ToLocalOf(first, actualInSecond);

        AssertVectorClose(expectedInSecond, actualInSecond, 2e-5f);
        AssertVectorClose(expectedInSecond, viaMatrix, 2e-5f);
        AssertVectorClose(point, roundTrip, 2e-5f);
        AssertVectorClose(point, Vector2.Transform(world, first.InverseWorld), 2e-5f);
    }

    [TestMethod]
    public void SingularInverseFailsAndRecoversAfterScaleChange()
    {
        var node = new Node2D
        {
            Position = new Vector2(3f, 4f),
            Scale = new Vector2(0f, 2f),
        };

        _ = node.WorldMatrix;
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = node.InverseWorld);

        node.Scale = new Vector2(2f, 2f);
        var localPoint = new Vector2(1f, -1f);
        var worldPoint = Vector2.Transform(localPoint, node.WorldMatrix);

        AssertVectorClose(localPoint, Vector2.Transform(worldPoint, node.InverseWorld));
    }

    [TestMethod]
    public void ExtremeFiniteScalesProduceARepresentableInverseOrFailAndRecover()
    {
        var veryLarge = new Node2D { Scale = new Vector2(float.MaxValue, float.MaxValue) };

        var identity = veryLarge.WorldMatrix * veryLarge.InverseWorld;
        AssertMatrixClose(Matrix3x2.Identity, identity, 2e-6f);
        Assert.IsTrue(AllFinite(veryLarge.InverseWorld));

        var tooSmall = new Node2D { Scale = new Vector2(float.Epsilon, 1f) };
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = tooSmall.InverseWorld);

        tooSmall.Scale = Vector2.One;
        AssertMatrixClose(Matrix3x2.Identity, tooSmall.InverseWorld);
    }

    [TestMethod]
    public void WorldOverflowFailsWithoutPoisoningTheCacheAndRecovers()
    {
        var parent = new Node2D { Scale = new Vector2(float.MaxValue, 1f) };
        var child = parent.Add(new Node2D { Scale = new Vector2(2f, 1f) });

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = child.WorldMatrix);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = child.InverseWorld);

        parent.Scale = Vector2.One;
        AssertMatrixClose(new Matrix3x2(2f, 0f, 0f, 1f, 0f, 0f), child.WorldMatrix);
        AssertMatrixClose(new Matrix3x2(0.5f, 0f, 0f, 1f, 0f, 0f), child.InverseWorld);
    }

    [TestMethod]
    public void AncestorChangesInvalidateCleanWorldAndInverseCachesThroughTheSubtree()
    {
        var root = new Node2D { Position = new Vector2(3f, 4f) };
        var child = root.Add(new Node2D { Position = new Vector2(2f, 0f) });
        var grandchild = child.Add(new Node2D { Position = new Vector2(1f, 0f) });
        _ = grandchild.WorldMatrix;
        _ = grandchild.InverseWorld;

        root.Position = new Vector2(-5f, 6f);
        AssertVectorClose(new Vector2(-2f, 6f), grandchild.WorldPosition);
        AssertVectorClose(Vector2.Zero, Vector2.Transform(grandchild.WorldPosition, grandchild.InverseWorld));

        root.Rotation = Angle.Deg(90d);
        AssertVectorClose(new Vector2(-5f, 9f), grandchild.WorldPosition);
        AssertVectorClose(Vector2.Zero, Vector2.Transform(grandchild.WorldPosition, grandchild.InverseWorld));

        root.Scale = new Vector2(2f, 3f);
        AssertVectorClose(new Vector2(-5f, 12f), grandchild.WorldPosition);
        AssertVectorClose(Vector2.Zero, Vector2.Transform(grandchild.WorldPosition, grandchild.InverseWorld));
    }

    [TestMethod]
    public void LocalInputsMustBeFiniteWhileZeroAndNegativeScaleRemainValid()
    {
        var node = new Node2D();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => node.Position = new Vector2(float.NaN, 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => node.Position = new Vector2(0f, float.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => node.Scale = new Vector2(float.NegativeInfinity, 1f));
        Assert.ThrowsExactly<ArgumentException>(() => node.ScaleMode = (ScaleMode)99);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = Angle.Rad(double.NaN));

        node.Scale = new Vector2(-2f, 0f);
        node.Rotation = Angle.Rad(double.MaxValue);
        var local = node.LocalMatrix;

        Assert.IsTrue(AllFinite(local));
    }

    [TestMethod]
    public void ScaleModeDoesNotAffectLogicalMatrices()
    {
        var parent = new Node2D
        {
            Position = new Vector2(3f, 5f),
            Rotation = Angle.Deg(25d),
            Scale = new Vector2(2f, 0.5f),
        };
        var child = parent.Add(new Node2D
        {
            Position = new Vector2(7f, -1f),
            Rotation = Angle.Deg(-15d),
        });
        var localBefore = child.LocalMatrix;
        var worldBefore = child.WorldMatrix;
        var inverseBefore = child.InverseWorld;

        child.ScaleMode = ScaleMode.Virtual;

        Assert.AreEqual(ScaleMode.Virtual, child.ScaleMode);
        Assert.AreEqual(localBefore, child.LocalMatrix);
        Assert.AreEqual(worldBefore, child.WorldMatrix);
        Assert.AreEqual(inverseBefore, child.InverseWorld);
    }

    [TestMethod]
    public void RepeatedCleanReadsReturnIdenticalCachedValues()
    {
        var node = new Node2D
        {
            Position = new Vector2(8f, -3f),
            Rotation = Angle.Deg(37d),
            Scale = new Vector2(1.25f, -0.75f),
        };
        var local = node.LocalMatrix;
        var world = node.WorldMatrix;
        var inverse = node.InverseWorld;

        Assert.AreEqual(local, node.LocalMatrix);
        Assert.AreEqual(world, node.WorldMatrix);
        Assert.AreEqual(inverse, node.InverseWorld);
        Assert.AreEqual(node.WorldPosition, node.WorldPosition);
    }

    private static bool AllFinite(Matrix3x2 matrix) =>
        float.IsFinite(matrix.M11) &&
        float.IsFinite(matrix.M12) &&
        float.IsFinite(matrix.M21) &&
        float.IsFinite(matrix.M22) &&
        float.IsFinite(matrix.M31) &&
        float.IsFinite(matrix.M32);

    private static void AssertVectorClose(Vector2 expected, Vector2 actual, float tolerance = 1e-5f)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance, "X differs.");
        Assert.AreEqual(expected.Y, actual.Y, tolerance, "Y differs.");
    }

    private static void AssertMatrixClose(Matrix3x2 expected, Matrix3x2 actual, float tolerance = 1e-5f)
    {
        Assert.AreEqual(expected.M11, actual.M11, tolerance);
        Assert.AreEqual(expected.M12, actual.M12, tolerance);
        Assert.AreEqual(expected.M21, actual.M21, tolerance);
        Assert.AreEqual(expected.M22, actual.M22, tolerance);
        Assert.AreEqual(expected.M31, actual.M31, tolerance);
        Assert.AreEqual(expected.M32, actual.M32, tolerance);
    }
}
