namespace CsjsLib;

/// <summary>An amount in a currency. Records compare by value, in JavaScript as in C#.</summary>
public record Money(decimal Amount, string Currency)
{
    public Money Add(Money other)
    {
        if (other.Currency != Currency)
            throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.");
        return this with { Amount = Amount + other.Amount };
    }

    /// <summary>Splits evenly into <paramref name="parts"/>, giving any remainder to the first.</summary>
    public List<Money> Split(int parts)
    {
        if (parts <= 0) throw new ArgumentOutOfRangeException(nameof(parts));

        var cents = (int)Math.Round(Amount * 100);
        var share = cents / parts;
        var remainder = cents % parts;

        return Enumerable.Range(0, parts)
            .Select(i => new Money((share + (i < remainder ? 1 : 0)) / 100m, Currency))
            .ToList();
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}
