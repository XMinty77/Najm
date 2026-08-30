using System.Numerics;
using Najm.Core;
using Najm.Core.Text;
using Najm.Text;

namespace Najm.Lib.Tests;

/// <summary>Stands a text node up inside a loaded scene, which is the only state it works in.</summary>
/// <remarks>
/// A <see cref="TextNode"/> resolves two things at attach and cannot answer anything before it
/// has: its scene's typesetter, and its layer's y-axis orientation. Every test here therefore needs
/// a real loaded scene, a real <see cref="Typesetter"/>, and a layer of a stated kind — but none of
/// them needs pixels, so the surface provider below does nothing at all.
/// </remarks>
internal sealed class TextTestScene : IDisposable
{
    private readonly Scene scene;
    private readonly bool ownsTypesetter;

    private TextTestScene(Layer layer, Node2D root, ITypesetter? typesetter)
    {
        ownsTypesetter = typesetter is null;
        Typesetter = typesetter ?? new Typesetter();
        Layer = layer;
        Root = root;
        scene = new Scene { VirtualResolution = new Vector2(1920f, 1080f) };
        scene.Layers.Add(layer);
        scene.Load(new SceneEnvironment(new SilentSurfaceProvider(), typesetter: Typesetter));
    }

    internal Node2D Root { get; }

    internal Layer Layer { get; }

    internal ITypesetter Typesetter { get; }

    /// <summary>Builds a Y-down screen-space scene.</summary>
    internal static TextTestScene Screen(ITypesetter? typesetter = null)
    {
        var layer = new ScreenLayer();
        return new TextTestScene(layer, layer.Root, typesetter);
    }

    /// <summary>Builds a Y-up world-space scene.</summary>
    internal static TextTestScene World(ITypesetter? typesetter = null)
    {
        var layer = new WorldLayer2D();
        return new TextTestScene(layer, layer.Root, typesetter);
    }

    /// <summary>Adds a node to the layer root, attaching it.</summary>
    internal T Add<T>(T node)
        where T : Node2D
    {
        Root.Add(node);
        return node;
    }

    public void Dispose()
    {
        scene.Unload();
        if (ownsTypesetter)
        {
            (Typesetter as IDisposable)?.Dispose();
        }
    }

    /// <summary>A provider that satisfies scene load and draws nothing, because nothing draws.</summary>
    private sealed class SilentSurfaceProvider : ISurfaceProvider
    {
        public RenderCaps Caps => RenderCaps.None;

        public IRenderTarget CreateTarget(in SurfaceSpec spec) =>
            throw new NotSupportedException("These tests measure layouts; they do not draw.");

        public ICompositor CreateCompositor() => new SilentCompositor();

        public void Dispose()
        {
        }
    }

    private sealed class SilentCompositor : ICompositor
    {
        public CompositorStats Stats => default;

        public CompositorDebugOptions Debug { get; } = new();

        public void Render(LayerStack layers, IRenderTarget output, in Vector2 virtualResolution, float renderScale)
        {
        }

        public void Dispose()
        {
        }
    }
}
