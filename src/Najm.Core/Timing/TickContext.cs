namespace Najm.Core;

/// <summary>Contains allocation-free immutable data supplied to one scene tick.</summary>
/// <remarks>
/// The zero-initialized value is invalid because its <see cref="TimeInfo"/> is
/// invalid. Constructed contexts use <see cref="InputBlock.Empty"/> unless an
/// input block is supplied explicitly.
/// </remarks>
public readonly struct TickContext
{
    private readonly TimeInfo time;
    private readonly InputBlock input;

    /// <summary>Creates a tick with the canonical empty input block.</summary>
    public TickContext(in TimeInfo time)
        : this(time, InputBlock.Empty)
    {
    }

    /// <summary>Creates a tick from validated time and input data.</summary>
    public TickContext(in TimeInfo time, in InputBlock input)
    {
        if (!time.IsValid)
        {
            throw new ArgumentException(
                "A tick context requires constructed TimeInfo.",
                nameof(time));
        }

        this.time = time;
        this.input = input;
    }

    /// <summary>Gets whether this context contains constructed time data.</summary>
    public bool IsValid => time.IsValid;

    /// <summary>Gets immutable simulation time for this tick.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public TimeInfo Time
    {
        get
        {
            EnsureValid();
            return time;
        }
    }

    /// <summary>Gets input for this tick.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public InputBlock Input
    {
        get
        {
            EnsureValid();
            return input;
        }
    }

    private void EnsureValid()
    {
        if (!time.IsValid)
        {
            throw new InvalidOperationException(
                "The zero-initialized TickContext is invalid and does not describe a tick.");
        }
    }
}

