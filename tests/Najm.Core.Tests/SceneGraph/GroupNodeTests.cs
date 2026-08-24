using System.Numerics;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Core.Tests.SceneGraph;

/// <summary>
/// <see cref="GroupNode"/> exists to make ARCHITECTURE Appendix B.3's
/// <c>layer.Root.Add(new GroupNode())</c> compile and to let a grouping node read as one. These
/// tests say what it is by saying what it is not: it adds nothing to <see cref="Node2D"/>, and every
/// claim below would hold word for word with <c>Node2D</c> substituted.
/// </summary>
[TestClass]
public sealed class GroupNodeTests
{
    [TestMethod]
    public void TheDocumentedIdiomCompilesAndYieldsAWorkingGroupingNode()
    {
        // Appendix B.3, as written, minus the M2 members of the example it appears in.
        var layer = new ScreenLayer();

        var banner = layer.Root.Add(new GroupNode());
        banner.Position = new Vector2(160f, 620f);
        var child = banner.Add(new GroupNode { Position = new Vector2(40f, 0f) });

        Assert.AreSame(banner, child.Parent);
        Assert.AreSame(layer.Root, banner.Parent);
        Assert.AreEqual(new Vector2(200f, 620f), child.WorldPosition);
    }

    [TestMethod]
    public void ItAddsNothingWhatsoeverToNode2D()
    {
        // Not "adds nothing important" — adds nothing. A declared member here would be a behavior
        // the type's own documentation promises it does not have.
        var declared = typeof(GroupNode).GetMembers(
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.HasCount(
            1,
            declared,
            "Only the implicit parameterless constructor may be declared on GroupNode.");
        Assert.IsInstanceOfType<System.Reflection.ConstructorInfo>(declared[0]);
        Assert.AreEqual(typeof(Node2D), typeof(GroupNode).BaseType);
    }

    [TestMethod]
    public void ItPaintsNothingOfItsOwnWhileItsChildrenPaintNormally()
    {
        // A group node's Render is Node's do-nothing default, and its bounds stay empty, so it
        // contributes no geometry and no visual extent to cull against — while the subtree under it
        // paints exactly as it would under any other parent.
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new GroupNode { Position = new Vector2(10f, 20f) });
        var child = group.Add(new PaintCountingDrawable());
        scene.Load(TestEnvironment.Stub());

        Assert.AreEqual(default, group.GeometryBounds);
        Assert.AreEqual(default, group.VisualBounds);
        Assert.AreEqual(default, group.HitBounds);

        var context = new CountingContext();
        scene.RenderDirect(context);

        Assert.AreEqual(1, child.Paints, "The child paints, once.");
        Assert.AreEqual(1, context.DrawPathCount, "And the group itself contributes no primitive.");
    }

    [TestMethod]
    public void ItCarriesTheCompositionStateThatMakesGroupingMeanSomething()
    {
        // The point of a grouping node is the state it holds for its subtree. It holds all of it,
        // because it is a Node2D — this test would fail only if GroupNode had somehow narrowed it.
        var group = new GroupNode
        {
            Opacity = 0.5f,
            Blend = BlendMode.Screen,
            Clip = new Rect(0f, 0f, 4f, 4f),
            ZIndex = 3,
            Isolate = true,
        };

        Assert.AreEqual(0.5f, group.Opacity);
        Assert.AreEqual(BlendMode.Screen, group.Blend);
        Assert.AreEqual(new Rect(0f, 0f, 4f, 4f), group.Clip);
        Assert.AreEqual(3, group.ZIndex);
        Assert.IsTrue(group.Isolate);
    }

    private sealed class PaintCountingDrawable : Drawable
    {
        private readonly PathBuilder path = new PathBuilder().MoveTo(0f, 0f).LineTo(1f, 1f);

        internal int Paints { get; private set; }

        public override void Render(IDrawContext2D context)
        {
            Paints++;
            context.DrawPath(path, Paint.Stroke(Najm.Utils.Color.White, 1f));
        }
    }

    /// <summary>Counts Tier-1 calls and does nothing else.</summary>
    private sealed class CountingContext : DrawContext2DBase
    {
        internal int DrawPathCount { get; private set; }

        internal int DrawTextCount { get; private set; }

        public override SurfaceSpec SurfaceSpec { get; } = new(16, 16);

        public override RenderCaps Caps => RenderCaps.None;

        public override float RenderScale => 1f;

        public override float Scale => 1f;

        public override void Clear(Najm.Utils.Color color)
        {
        }

        public override void DrawPath(PathBuilder path, in Paint paint) => DrawPathCount++;

        // Tier 1 and unreachable from a GroupNode, which draws nothing; overridden because the
        // base class declares it abstract, and counted so a stray call would be visible.
        public override void DrawText(ITextLayout layout, Color? colorOverride = null) => DrawTextCount++;

        public override void DrawImage(
            IImage image,
            in Matrix3x2 imageToLocal,
            ImageSampling sampling = ImageSampling.Linear)
        {
        }

        public override void SetEngineTransform(in Matrix3x2 engineToDevice)
        {
        }

        public override void BeginLayerBracket(in LayerBracket bracket)
        {
        }

        public override void EndLayerBracket()
        {
        }

        public override void BeginUnitBracket(in UnitBracket bracket)
        {
        }

        public override void EndUnitBracket()
        {
        }

        public override void BeginClipBracket(in ClipBracket bracket)
        {
        }

        public override void EndClipBracket()
        {
        }

        public override void PushTransform(in Matrix3x2 localTransform)
        {
        }

        public override void PopTransform()
        {
        }

        public override void PushClip(in Rect bounds)
        {
        }

        public override void PushClip(PathBuilder path)
        {
        }

        public override void PopClip()
        {
        }

        public override void PushOpacity(float opacity)
        {
        }

        public override void PopOpacity()
        {
        }
    }
}
