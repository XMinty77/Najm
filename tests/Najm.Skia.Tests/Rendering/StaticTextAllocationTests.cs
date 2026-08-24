using System.Numerics;
using Najm.Core;
using Najm.Core.Text;
using Najm.Lib;
using Najm.Utils;
using CoreTypesetter = Najm.Text.Typesetter;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// ARCHITECTURE §3.6 and NAJM-TEXT VI.2: <strong>static text performs no typesetter work during
/// steady-state rendering, and allocates nothing.</strong>
/// </summary>
/// <remarks>
/// <para>
/// This is the number that matters in the whole text performance story. Transition costs — the
/// first shape, the first layout, the first blob — are bounded and permitted; what must be exactly
/// zero is the steady state, because a per-frame allocation in the commonest drawable in the engine
/// is a GC hitch in every scene that has a label in it.
/// </para>
/// <para>
/// The test asserts both halves at once, which is the point: an implementation could be
/// allocation-free by rebuilding nothing and still be re-typesetting into a cache, or could hold a
/// layout and rebuild its text blob every frame. The typeset counter catches the first and the
/// allocation probe catches the second.
/// </para>
/// </remarks>
[TestClass]
public sealed class StaticTextAllocationTests
{
    [TestMethod]
    public void AWarmStaticLabelAllocatesNothingAndTypesetsOnce()
    {
        using var typesetter = new CountingTypesetter(new CoreTypesetter());
        using var provider = new RasterSkiaSurfaceProvider();
        var scene = new Scene { VirtualResolution = new Vector2(320f, 200f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = Color.White });
        var node = layer.Root.Add(new CountingTextNode
        {
            Text = "Najm 2.0",
            Size = 32f,
            Position = new Vector2(20f, 120f),
        });

        scene.Load(new SceneEnvironment(provider, typesetter: typesetter));
        try
        {
            using var target = provider.CreateTarget(new SurfaceSpec(320, 200));

            // One render before the probe, so the transition costs the design explicitly permits —
            // shaping, line layout, the native typeface, the positioned blob — all land outside the
            // measured window. Everything after this point is steady state by definition.
            scene.Render(target);
            var typesetsAfterWarmUp = typesetter.TypesetCount;
            Assert.AreEqual(1, typesetsAfterWarmUp, "The first render is the only typeset.");

            var reading = AllocationProbe.AssertNoneAllocated(
                iterations: 32,
                () => scene.Render(target),
                "Rendering a static text node");

            // The probe re-runs its body to escape a disturbed window, so the honest counter
            // assertion is against what it actually ran rather than against a hard-coded total.
            Assert.AreEqual(
                reading.Invocations + 1,
                node.RenderCount,
                "Every measured render must have gone through the node.");
            Assert.AreEqual(
                typesetsAfterWarmUp,
                typesetter.TypesetCount,
                $"Static text re-typeset during {reading.Invocations} steady-state renders.");
        }
        finally
        {
            scene.Unload();
        }
    }

    /// <summary>A text node that counts how often the traverser reached it.</summary>
    private sealed class CountingTextNode : TextNode
    {
        internal int RenderCount { get; private set; }

        public override void Render(IDrawContext2D context)
        {
            RenderCount++;
            base.Render(context);
        }
    }

    /// <summary>Counts typesets and forwards everything to a real typesetter.</summary>
    private sealed class CountingTypesetter(ITypesetter inner) : ITypesetter, IDisposable
    {
        internal int TypesetCount { get; private set; }

        public void RegisterFamily(FontFamily family) => inner.RegisterFamily(family);

        public void SetDefaultFamilies(string textFamily, string mathFamily) =>
            inner.SetDefaultFamilies(textFamily, mathFamily);

        public FontMetrics Metrics(FontFace face, float size) => inner.Metrics(face, size);

        public ITextLayout Typeset(in TypesetRequest request)
        {
            TypesetCount++;
            return inner.Typeset(request);
        }

        public void Dispose() => (inner as IDisposable)?.Dispose();
    }
}
