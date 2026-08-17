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

        var reading = AllocationProbe.AssertNoneAllocated(
            100_000,
            () =>
            {
                if (!parent.Remove(child))
                {
                    throw new InvalidOperationException("Expected the stable child to be present.");
                }

                parent.Add(child);
                accumulator += parent.Children.Count;
            },
            "Detach and reattach at stable capacity");

        // One from the reading before the probe ran, then one per detach/reattach cycle. The probe
        // decides how many cycles that is, so the expected total comes from it.
        Assert.AreEqual(1 + reading.Invocations, accumulator);
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
        var reads = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            100_000,
            () =>
            {
                accumulator += child.LocalMatrix.M11;
                accumulator += child.WorldMatrix.M31;
                accumulator += child.InverseWorld.M32;
                accumulator += child.WorldPosition.X;
                reads++;
            },
            "Clean transform reads");

        Assert.AreEqual(reading.Invocations, reads);
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

        var reading = AllocationProbe.AssertNoneAllocated(
            1_000,
            () =>
            {
                for (var index = 0; index < parent.Children.Count; index++)
                {
                    accumulator += parent.GetChildInPaintOrder(index) is Node2D ? 1 : 0;
                }
            },
            "Equal-ZIndex paint-order reads");

        // Three children walked once before the probe, then three per probe iteration. Anything
        // less means a child went missing from paint order.
        Assert.AreEqual(3 * (1 + reading.Invocations), accumulator);
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

        var reading = AllocationProbe.AssertNoneAllocated(
            1_000,
            () =>
            {
                for (var index = 0; index < parent.Children.Count; index++)
                {
                    accumulator += parent.GetChildInPaintOrder(index) is Node2D ? 1 : 0;
                }
            },
            "Mixed-ZIndex paint-order reads");

        // Three children walked once before the probe, then three per probe iteration. Anything
        // less means a child went missing from the sorted paint order.
        Assert.AreEqual(3 * (1 + reading.Invocations), accumulator);
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

        var reading = AllocationProbe.AssertNoneAllocated(
            10_000,
            () =>
            {
                foreach (var child in parent.Children)
                {
                    accumulator += child is Node2D ? 1 : 0;
                }
            },
            "Concrete children foreach");

        // Two children enumerated once before the probe, then two per probe iteration. A boxed
        // enumerator would show up as allocation; a short enumeration would show up here.
        Assert.AreEqual(2 * (1 + reading.Invocations), accumulator);
    }
}
