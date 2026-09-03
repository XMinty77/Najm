using Najm.Core;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Najm.Host.Desktop.Tests;

/// <summary>
/// Pins the platform translation §9.1 makes the host's job, against the failure it is most likely
/// to have: a name that matched by coincidence.
/// </summary>
/// <remarks>
/// The two enumerations agree on most spellings and on nothing else. The dangerous entries are the
/// ones where the spellings differ — <c>ShiftLeft</c>/<c>LeftShift</c>, <c>Keypad7</c>/<c>Numpad7</c>,
/// <c>BackSlash</c>/<c>Backslash</c> — because a transposition there compiles, runs, and only shows
/// up as a key that does the wrong thing.
/// </remarks>
[TestClass]
public sealed class PlatformInputMapTests
{
    [TestMethod]
    public void EveryNajmKeyIsReachableFromSomePlatformKey()
    {
        var reached = new HashSet<Key>();
        foreach (var platformKey in Enum.GetValues<SilkKey>())
        {
            var mapped = PlatformInputMap.ToKey(platformKey);
            if (mapped != Key.Unknown)
            {
                reached.Add(mapped);
            }
        }

        var missing = Enum.GetValues<Key>()
            .Where(key => key != Key.Unknown && !reached.Contains(key))
            .ToArray();

        Assert.IsEmpty(
            missing,
            $"No platform key maps to: {string.Join(", ", missing)}. A Najm key nothing produces is "
            + "a binding an author can write and never trigger.");
    }

    [TestMethod]
    public void NoTwoPlatformKeysCollideOnOneNajmKey()
    {
        // Distinct because Enum.GetValues yields one entry per field and Silk.NET spells its
        // number row twice — Number0 and D0 are the same value under two names.
        var owners = new Dictionary<Key, SilkKey>();
        foreach (var platformKey in Enum.GetValues<SilkKey>().Distinct())
        {
            var mapped = PlatformInputMap.ToKey(platformKey);
            if (mapped == Key.Unknown)
            {
                continue;
            }

            Assert.IsFalse(
                owners.TryGetValue(mapped, out var existing),
                $"{platformKey} and {existing} both map to {mapped}; one of them is a typo.");
            owners[mapped] = platformKey;
        }
    }

    [TestMethod]
    public void TheDifferentlySpelledKeysMapWhereTheyLook()
    {
        Assert.AreEqual(Key.LeftShift, PlatformInputMap.ToKey(SilkKey.ShiftLeft));
        Assert.AreEqual(Key.RightShift, PlatformInputMap.ToKey(SilkKey.ShiftRight));
        Assert.AreEqual(Key.LeftControl, PlatformInputMap.ToKey(SilkKey.ControlLeft));
        Assert.AreEqual(Key.RightAlt, PlatformInputMap.ToKey(SilkKey.AltRight));
        Assert.AreEqual(Key.LeftSuper, PlatformInputMap.ToKey(SilkKey.SuperLeft));
        Assert.AreEqual(Key.Numpad7, PlatformInputMap.ToKey(SilkKey.Keypad7));
        Assert.AreEqual(Key.NumpadDivide, PlatformInputMap.ToKey(SilkKey.KeypadDivide));
        Assert.AreEqual(Key.Backslash, PlatformInputMap.ToKey(SilkKey.BackSlash));
        Assert.AreEqual(Key.D7, PlatformInputMap.ToKey(SilkKey.Number7));
    }

    [TestMethod]
    public void KeysNajmHasNoMemberForBecomeUnknown()
    {
        // Key's own remarks: "Anything a host cannot map lands on Unknown, which is a legal value
        // that no binding should match."
        Assert.AreEqual(Key.Unknown, PlatformInputMap.ToKey(SilkKey.F13));
        Assert.AreEqual(Key.Unknown, PlatformInputMap.ToKey(SilkKey.F25));
        Assert.AreEqual(Key.Unknown, PlatformInputMap.ToKey(SilkKey.World1));
        Assert.AreEqual(Key.Unknown, PlatformInputMap.ToKey(SilkKey.Unknown));
    }

    [TestMethod]
    public void TheFiveOrdinaryMouseButtonsMapAndTheRestDoNot()
    {
        Assert.AreEqual(PointerButton.Left, PlatformInputMap.ToButton(SilkMouseButton.Left));
        Assert.AreEqual(PointerButton.Middle, PlatformInputMap.ToButton(SilkMouseButton.Middle));
        Assert.AreEqual(PointerButton.Right, PlatformInputMap.ToButton(SilkMouseButton.Right));
        Assert.AreEqual(PointerButton.X1, PlatformInputMap.ToButton(SilkMouseButton.Button4));
        Assert.AreEqual(PointerButton.X2, PlatformInputMap.ToButton(SilkMouseButton.Button5));

        // None means "do not deliver": InputBuffer requires exactly one defined button, and a
        // twelve-button mouse has nothing for Najm to call buttons six and up.
        Assert.AreEqual(PointerButton.None, PlatformInputMap.ToButton(SilkMouseButton.Button6));
        Assert.AreEqual(PointerButton.None, PlatformInputMap.ToButton(SilkMouseButton.Unknown));
    }
}
