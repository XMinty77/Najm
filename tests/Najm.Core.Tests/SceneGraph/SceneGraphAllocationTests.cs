using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.SceneGraph;

[TestClass]
public sealed class SceneGraphAllocationTests
{
    [TestMethod]
    public void WarmStableCapacityDetachedMutationAllocatesNoManagedMemory()
    {
        var parent = new Node2D();
        var child = parent.Add(new Node2D());
        _ = parent.Children.Count;
        Assert.IsTrue(parent.Remove(child));
        parent.Add(child);
        var accumulator = parent.Children.Count;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 100_000; iteration++)
        {
            if (!parent.Remove(child))
            {
                throw new InvalidOperationException("Expected the stable child to be present.");
            }

            parent.Add(child);
            accumulator += parent.Children.Count;
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.AreEqual(100_001, accumulator);
    }

    [TestMethod]
    public void WarmCleanTransformReadsAllocateNoManagedMemory()
    {
        var parent = new Node2D
        {
            Position = new Vector2(10f, 20f),
            Rotation = Angle.Deg(15d),
        };
        var child = parent.Add(new Node2D
        {
            Position = new Vector2(2f, 3f),
            Rotation = Angle.Deg(-30d),
            Scale = new Vector2(1.5f, 0.75f),
        });
        var accumulator = child.LocalMatrix.M11 + child.WorldMatrix.M31 + child.InverseWorld.M32;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 100_000; iteration++)
        {
            accumulator += child.LocalMatrix.M11;
            accumulator += child.WorldMatrix.M31;
            accumulator += child.InverseWorld.M32;
            accumulator += child.WorldPosition.X;
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.AreNotEqual(0f, accumulator);
    }

    [TestMethod]
    public void WarmEqualZIndexPaintOrderAllocatesNoManagedMemory()
    {
        var parent = new Node2D();
        parent.Add(new Node2D());
        parent.Add(new Node2D());
        parent.Add(new Node2D());
        var accumulator = 0;
        for (var index = 0; index < parent.Children.Count; index++)
        {
            accumulator += parent.GetChildInPaintOrder(index) is Node2D ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            for (var index = 0; index < parent.Children.Count; index++)
            {
                accumulator += parent.GetChildInPaintOrder(index) is Node2D ? 1 : 0;
            }
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.AreEqual(3_003, accumulator);
    }

    [TestMethod]
    public void WarmMixedZIndexPaintOrderAllocatesNoManagedMemory()
    {
        var parent = new Node2D();
        parent.Add(new Node2D { ZIndex = 2 });
        parent.Add(new Node2D());
        parent.Add(new Node2D { ZIndex = -1 });
        var accumulator = 0;
        for (var index = 0; index < parent.Children.Count; index++)
        {
            accumulator += parent.GetChildInPaintOrder(index) is Node2D ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            for (var index = 0; index < parent.Children.Count; index++)
            {
                accumulator += parent.GetChildInPaintOrder(index) is Node2D ? 1 : 0;
            }
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.AreEqual(3_003, accumulator);
    }

    [TestMethod]
    public void WarmConcreteChildrenForeachAllocatesNoManagedMemory()
    {
        var parent = new Node2D();
        parent.Add(new Node2D());
        parent.Add(new Node2D());
        var accumulator = 0;
        foreach (var child in parent.Children)
        {
            accumulator += child is Node2D ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            foreach (var child in parent.Children)
            {
                accumulator += child is Node2D ? 1 : 0;
            }
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.AreEqual(20_002, accumulator);
    }
}
