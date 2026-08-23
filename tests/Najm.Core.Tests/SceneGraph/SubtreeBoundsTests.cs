using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.SceneGraph;

/// <summary>
/// ARCHITECTURE §6.6's subtree aggregates: what a node and its descendants cover, composed through
/// the descendants' own transforms into the node's local space, and the invalidation that keeps the
/// answer current.
/// </summary>
/// <remarks>
/// Every expectation here is derived from the declared rectangles and the row-vector transform rule
/// (<c>Scale × Rotation × Translation</c>), never read back from a run.
/// </remarks>
[TestClass]
public sealed class SubtreeBoundsTests
{
    [TestMethod]
    public void AnEmptySubtreeAggregatesToTheEmptyRectangle()
    {
        var lonely = new Node2D();

        Assert.AreEqual(default, lonely.SubtreeGeometryBounds);
        Assert.AreEqual(default, lonely.SubtreeHitBounds);
        Assert.AreEqual(default, lonely.SubtreeVisualBounds);

        // Children that declare nothing are still nothing: an aggregate of empties is empty, not a
        // point at the origin.
        var parent = new Node2D();
        parent.Add(new Node2D { Position = new Vector2(100f, 100f) });
        parent.Add(new Node2D { Position = new Vector2(-50f, -50f) });

        Assert.AreEqual(default, parent.SubtreeVisualBounds);
    }

    [TestMethod]
    public void ALeafAggregatesToItsOwnDeclarations()
    {
        var leaf = new BoundedNode(new Rect(-2f, -3f, 4f, 6f))
        {
            Visual = new Rect(-3f, -4f, 6f, 8f),
        };

        Assert.AreEqual(new Rect(-2f, -3f, 4f, 6f), leaf.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(-2f, -3f, 4f, 6f), leaf.SubtreeHitBounds);
        Assert.AreEqual(new Rect(-3f, -4f, 6f, 8f), leaf.SubtreeVisualBounds);
    }

    [TestMethod]
    public void ADescendantIsComposedThroughItsOwnTransformNotItsAncestors()
    {
        // The child declares (1,1,2,4) and carries Scale(2,3) then Translate(5,-5), so a point maps
        // as (x, y) -> (2x + 5, 3y - 5): corners (1,1) and (3,5) become (7,-2) and (11,10).
        var root = new Node2D { Position = new Vector2(1000f, 1000f) };
        root.Add(new BoundedNode(new Rect(1f, 1f, 2f, 4f))
        {
            Scale = new Vector2(2f, 3f),
            Position = new Vector2(5f, -5f),
        });

        // The root's own transform is deliberately absent from its own aggregate: the value is
        // stated in the root's local space, which is what makes it stable while the root moves.
        Assert.AreEqual(new Rect(7f, -2f, 4f, 12f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void ANegativelyScaledDescendantContributesItsMirroredExtent()
    {
        // Scale(-1, 1) sends x to -x, so (1,0,2,1) spans x in [-3,-1] rather than [1,3]. Taking two
        // corners instead of four would report a negative width here.
        var root = new Node2D();
        root.Add(new BoundedNode(new Rect(1f, 0f, 2f, 1f)) { Scale = new Vector2(-1f, 1f) });

        Assert.AreEqual(new Rect(-3f, 0f, 2f, 1f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void ARotatedDescendantContributesTheAxisAlignedHullOfItsRotatedRectangle()
    {
        // A quarter turn maps (x, y) -> (-y, x), so the unit-cornered (0,0,2,2) sweeps x in [-2,0]
        // and y in [0,2]. The hull is conservative under rotation by construction.
        var root = new Node2D();
        root.Add(new BoundedNode(new Rect(0f, 0f, 2f, 2f)) { Rotation = Angle.Deg(90d) });

        var aggregate = root.SubtreeGeometryBounds;

        Assert.AreEqual(-2f, aggregate.X, 1e-5f);
        Assert.AreEqual(0f, aggregate.Y, 1e-5f);
        Assert.AreEqual(2f, aggregate.Width, 1e-5f);
        Assert.AreEqual(2f, aggregate.Height, 1e-5f);
    }

    [TestMethod]
    public void DepthComposesOneTransformPerLevel()
    {
        // grandchild (0,0,1,1) at Translate(1,0) is (1,0,1,1) in the child's space; the child's
        // Scale(2,2) then Translate(10,0) maps that to corners (12,0) and (14,2).
        var root = new Node2D();
        var child = root.Add(new Node2D
        {
            Position = new Vector2(10f, 0f),
            Scale = new Vector2(2f, 2f),
        });
        child.Add(new BoundedNode(new Rect(0f, 0f, 1f, 1f)) { Position = new Vector2(1f, 0f) });

        Assert.AreEqual(new Rect(1f, 0f, 1f, 1f), child.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(12f, 0f, 2f, 2f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void SiblingsUnionAndAnEmptySiblingContributesNothing()
    {
        // The empty node sits at the origin. If an empty rectangle were unioned as a point, the
        // aggregate would stretch from (0,0) to (12,12) instead of staying on the real content.
        var root = new BoundedNode(new Rect(10f, 10f, 2f, 2f));
        root.Add(new Node2D());
        root.Add(new BoundedNode(new Rect(0f, 0f, 1f, 1f)) { Position = new Vector2(20f, 4f) });

        Assert.AreEqual(new Rect(10f, 4f, 11f, 8f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void AChildWithEmptyBoundsStillCarriesItsOwnDescendants()
    {
        // An absent child and a present-but-empty child both add nothing themselves. They are not
        // the same thing, and this is the difference: the present one is still walked, so what
        // hangs beneath it still counts.
        var withoutChild = new Node2D();

        Assert.AreEqual(default, withoutChild.SubtreeGeometryBounds);

        var withChild = new Node2D();
        var empty = withChild.Add(new Node2D { Position = new Vector2(5f, 5f) });

        Assert.AreEqual(default, withChild.SubtreeGeometryBounds);

        empty.Add(new BoundedNode(new Rect(0f, 0f, 1f, 1f)));

        Assert.AreEqual(default, empty.GeometryBounds, "The intermediate node declares nothing itself.");
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), empty.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(5f, 5f, 1f, 1f), withChild.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void TheThreeAggregatesTrackTheirOwnDeclarationsIndependently()
    {
        // Visual expansion belongs to visual bounds alone: a stroke that reaches a unit outside the
        // geometry must not widen the interaction gate.
        var root = new Node2D();
        root.Add(new BoundedNode(new Rect(0f, 0f, 10f, 10f))
        {
            Visual = new Rect(-1f, -1f, 12f, 12f),
            Position = new Vector2(20f, 0f),
        });

        Assert.AreEqual(new Rect(20f, 0f, 10f, 10f), root.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(20f, 0f, 10f, 10f), root.SubtreeHitBounds);
        Assert.AreEqual(new Rect(19f, -1f, 12f, 12f), root.SubtreeVisualBounds);
    }

    [TestMethod]
    public void AnInvisibleDescendantStillCounts()
    {
        // Bounds are the subtree's static extent. Visibility flips per frame and the render walk
        // drops an invisible subtree before it ever consults a rectangle, so folding it in here
        // would buy a per-frame invalidation storm for nothing.
        var root = new Node2D();
        var hidden = root.Add(new BoundedNode(new Rect(0f, 0f, 4f, 4f)) { Position = new Vector2(6f, 0f) });

        Assert.AreEqual(new Rect(6f, 0f, 4f, 4f), root.SubtreeVisualBounds);

        hidden.Visible = false;

        Assert.AreEqual(new Rect(6f, 0f, 4f, 4f), root.SubtreeVisualBounds);
    }

    [TestMethod]
    public void ANodesOwnClipIsNotFoldedIntoItsAggregate()
    {
        // §6.7 intersects resolved visual bounds with the *active* clip when it sizes a bracket.
        // Pre-applying the node's own clip here would count it twice.
        var root = new Node2D { Clip = new Rect(0f, 0f, 1f, 1f) };
        root.Add(new BoundedNode(new Rect(0f, 0f, 50f, 50f)));

        Assert.AreEqual(new Rect(0f, 0f, 50f, 50f), root.SubtreeVisualBounds);
    }

    [TestMethod]
    public void AddingAndRemovingAChildInvalidatesEveryAncestor()
    {
        var root = new Node2D();
        var middle = root.Add(new Node2D { Position = new Vector2(3f, 0f) });

        Assert.AreEqual(default, root.SubtreeGeometryBounds, "Reading warms every cache in the tree.");

        var leaf = middle.Add(new BoundedNode(new Rect(0f, 0f, 2f, 2f)));

        Assert.AreEqual(new Rect(0f, 0f, 2f, 2f), middle.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(3f, 0f, 2f, 2f), root.SubtreeGeometryBounds);

        Assert.IsTrue(middle.Remove(leaf));

        Assert.AreEqual(default, middle.SubtreeGeometryBounds);
        Assert.AreEqual(default, root.SubtreeGeometryBounds);

        // The detached subtree keeps answering for itself; it simply answers for nobody else.
        Assert.AreEqual(new Rect(0f, 0f, 2f, 2f), leaf.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void MovingADescendantInvalidatesTheAncestorsItMovedWithin()
    {
        var root = new Node2D();
        var middle = root.Add(new Node2D());
        var leaf = middle.Add(new BoundedNode(new Rect(0f, 0f, 2f, 2f)));

        Assert.AreEqual(new Rect(0f, 0f, 2f, 2f), root.SubtreeGeometryBounds);

        leaf.Position = new Vector2(7f, -7f);

        Assert.AreEqual(new Rect(7f, -7f, 2f, 2f), middle.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(7f, -7f, 2f, 2f), root.SubtreeGeometryBounds);

        leaf.Scale = new Vector2(3f, 1f);

        // Scale applies before translation, so (0,0,2,2) becomes x in [7,13], y unchanged.
        Assert.AreEqual(new Rect(7f, -7f, 6f, 2f), root.SubtreeGeometryBounds);

        // A node's own transform never enters its own aggregate, only its parent's.
        middle.Position = new Vector2(100f, 100f);

        Assert.AreEqual(new Rect(7f, -7f, 6f, 2f), middle.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(107f, 93f, 6f, 2f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void TheUpwardWalkStaysCorrectWhenOnlyPartOfTheTreeIsReadBetweenMutations()
    {
        // The early exit at the first already-dirty ancestor is only sound under the invariant that
        // a dirty node's ancestors are all dirty. This is the sequence that would break a naive
        // version: read an inner node so it goes clean while the root stays dirty, then mutate
        // beneath it again so the second invalidation stops at the still-dirty root.
        var root = new Node2D();
        var middle = root.Add(new Node2D());
        var leaf = middle.Add(new BoundedNode(new Rect(0f, 0f, 1f, 1f)));

        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), root.SubtreeGeometryBounds);

        leaf.Position = new Vector2(2f, 0f);
        Assert.AreEqual(new Rect(2f, 0f, 1f, 1f), middle.SubtreeGeometryBounds);

        leaf.Position = new Vector2(4f, 0f);

        Assert.AreEqual(new Rect(4f, 0f, 1f, 1f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void ADeclaredBoundsChangeReachesTheAggregatesOnlyWhenItIsAnnounced()
    {
        // The engine cannot see inside a virtual property, so §6.6's "geometry, style, effect"
        // invalidation is the node's to send. The per-node declaration is never cached and so is
        // always current; the aggregate is, and holds until it is told.
        var root = new Node2D();
        var leaf = root.Add(new BoundedNode(new Rect(0f, 0f, 1f, 1f)));

        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), root.SubtreeGeometryBounds);

        leaf.Redeclare(new Rect(0f, 0f, 9f, 9f), announce: false);

        Assert.AreEqual(new Rect(0f, 0f, 9f, 9f), leaf.GeometryBounds, "The declaration itself is never cached.");
        Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), root.SubtreeGeometryBounds);

        leaf.Announce();

        Assert.AreEqual(new Rect(0f, 0f, 9f, 9f), root.SubtreeGeometryBounds);
    }

    [TestMethod]
    public void WarmAggregateReadsAllocateNoManagedMemory()
    {
        var root = new Node2D();
        var middle = root.Add(new Node2D { Position = new Vector2(2f, 2f), Scale = new Vector2(1.5f, 0.5f) });
        for (var index = 0; index < 8; index++)
        {
            middle.Add(new BoundedNode(new Rect(index, index, 3f, 3f))
            {
                Position = new Vector2(index, -index),
                Rotation = Angle.Deg(index * 5d),
            });
        }

        var accumulator = root.SubtreeVisualBounds.Width;
        var reads = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            50_000,
            () =>
            {
                accumulator += root.SubtreeGeometryBounds.Width;
                accumulator += root.SubtreeHitBounds.Height;
                accumulator += root.SubtreeVisualBounds.X;
                reads++;
            },
            "Warm subtree bounds reads");

        Assert.AreEqual(reading.Invocations, reads);
        Assert.IsTrue(float.IsFinite(accumulator));
    }

    [TestMethod]
    public void AColdRecomputeAfterAMutationAllocatesNoManagedMemory()
    {
        // The walk itself must not allocate either, or a moving subtree would pay per frame.
        var root = new Node2D();
        var middle = root.Add(new Node2D());
        var leaf = middle.Add(new BoundedNode(new Rect(0f, 0f, 1f, 1f)));
        var offset = 0f;
        var accumulator = root.SubtreeVisualBounds.Width;

        var reading = AllocationProbe.AssertNoneAllocated(
            50_000,
            () =>
            {
                offset = offset == 1f ? 2f : 1f;
                leaf.Position = new Vector2(offset, 0f);
                accumulator += root.SubtreeVisualBounds.X;
            },
            "Invalidate-then-recompute of subtree bounds");

        Assert.IsGreaterThan(0, reading.Invocations);
        Assert.IsTrue(float.IsFinite(accumulator));
    }

    /// <summary>A node whose three declarations are settable, with the announcement kept manual.</summary>
    private sealed class BoundedNode(Rect geometry) : Node2D
    {
        private Rect declared = geometry;

        internal Rect? Visual { get; init; }

        internal Rect? Hit { get; init; }

        public override Rect GeometryBounds => declared;

        public override Rect HitBounds => Hit ?? base.HitBounds;

        public override Rect VisualBounds => Visual ?? base.VisualBounds;

        internal void Redeclare(Rect value, bool announce)
        {
            declared = value;
            if (announce)
            {
                InvalidateBounds();
            }
        }

        internal void Announce() => InvalidateBounds();
    }
}
