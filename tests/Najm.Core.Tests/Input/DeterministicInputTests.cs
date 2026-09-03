using System.Numerics;

namespace Najm.Core.Tests.Input;

/// <summary>
/// The rule ARCHITECTURE states four times — §2.1, §2.5, §9.1, Appendix A.1 — that a deterministic
/// run takes no input, made mechanical rather than aspirational.
/// </summary>
[TestClass]
public sealed class DeterministicInputTests
{
    [TestMethod]
    public void AFixedStepTickRefusesABlockThatCarriesAnything()
    {
        var scene = new Scene();
        scene.Load(TestEnvironment.Stub());

        try
        {
            var buffer = new InputBuffer();
            buffer.PressKey(Key.Space);

            var failure = Assert.ThrowsExactly<InvalidOperationException>(
                () => scene.Tick(new TickContext(
                    new TimeInfo(1d, 1d, 0L, isFixedStep: true),
                    buffer.Block)));

            StringAssert.Contains(failure.Message, "deterministic runs take no input");
            StringAssert.Contains(failure.Message, "ClockPolicy.Live");

            // Level state alone is enough to be refused: a fixed-step render that could see where
            // the pointer rested would not be reproducible either.
            buffer.BeginFrame();
            Assert.AreEqual(0, buffer.Block.EventCount);
            Assert.ThrowsExactly<InvalidOperationException>(
                () => scene.Tick(new TickContext(
                    new TimeInfo(1d, 1d, 0L, isFixedStep: true),
                    buffer.Block)));

            buffer.ResetState();
            Assert.IsTrue(buffer.Block.IsEmpty);
            scene.Tick(new TickContext(new TimeInfo(1d, 1d, 0L, isFixedStep: true), buffer.Block));
        }
        finally
        {
            scene.Unload();
        }
    }

    [TestMethod]
    public void TheEmptyBlockIsAlwaysAcceptedAndRoutesNothing()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        harness.Add("node", log, new Rect(-1000f, -1000f, 4000f, 4000f));
        harness.Load();
        harness.Router.Focus((Node2D)harness.Layer.Root.Children[0]);
        log.Clear();

        for (var frame = 0; frame < 8; frame++)
        {
            harness.Scene.Tick(new TickContext(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true)));
        }

        Assert.IsEmpty(log, "The router idles on the empty block, focus or no focus.");
    }

    [TestMethod]
    public void ASceneThatPollsInputSeesTheSameDefaultsInEveryDeterministicReplay()
    {
        // Appendix A.1 item 6 asks scene authors not to consult input in a deterministic run. This
        // is what the engine guarantees them if they do it anyway: the answers are constants.
        static List<string> Run()
        {
            var readings = new List<string>();
            var scene = new Scene();
            scene.Layers.Add(new PollingLayer(readings));
            scene.Load(TestEnvironment.Stub());
            try
            {
                for (var frame = 0; frame < 4; frame++)
                {
                    scene.Tick(new TickContext(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true)));
                }
            }
            finally
            {
                scene.Unload();
            }

            return readings;
        }

        var first = Run();
        var second = Run();

        Assert.HasCount(4, first);
        CollectionAssert.AreEqual(first, second);
        Assert.AreEqual("empty:True events:0 pointer:<0, 0> buttons:None space:False", first[0]);
    }

    [TestMethod]
    public void ALiveTickIsWhereInputIsAllowed()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        harness.Add("node", log, new Rect(-50f, -50f, 100f, 100f), position: new Vector2(100f, 100f));
        harness.Load();

        harness.Buffer.PressPointer(0, new Vector2(100f, 100f), PointerButton.Left);
        harness.Tick();

        CollectionAssert.Contains(log, "node:down");
    }

    private sealed class PollingLayer(List<string> readings) : ScreenLayer
    {
        protected override void Update(in TickContext tick)
        {
            var input = tick.Input;
            readings.Add(
                $"empty:{input.IsEmpty} events:{input.EventCount} pointer:{input.PointerPosition} " +
                $"buttons:{input.Buttons} space:{input.IsDown(Key.Space)}");
        }
    }
}
