namespace CSJS.Testing;

/// <summary>Marks a method as a CSJS test. Tests must be public, static, and parameterless.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CsjsTestAttribute : Attribute
{
    public CsjsTestAttribute() { }
    public CsjsTestAttribute(string displayName) => DisplayName = displayName;

    /// <summary>Name shown in the runner output. Defaults to the method name.</summary>
    public string? DisplayName { get; }

    /// <summary>Set to skip the test, with the reason reported.</summary>
    public string? Skip { get; set; }
}

/// <summary>Groups tests under a heading in the runner output.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CsjsTestSuiteAttribute : Attribute
{
    public CsjsTestSuiteAttribute() { }
    public CsjsTestSuiteAttribute(string name) => Name = name;
    public string? Name { get; }
}

/// <summary>
/// Assertions available inside a CSJS test.
/// <para>
/// These are ordinary C# that the transpiler carries into JavaScript, so a failing assertion
/// throws in the JS runtime and the runner reports it. That is the point of the framework:
/// the assertion runs where the transpiled code runs, not against the .NET original.
/// </para>
/// </summary>
public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new CsjsAssertionException(message ?? "Expected the condition to be true.");
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition) throw new CsjsAssertionException(message ?? "Expected the condition to be false.");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!ValuesEqual(expected, actual))
            throw new CsjsAssertionException(message ?? $"Expected '{expected}' but got '{actual}'.");
    }

    public static void NotEqual<T>(T expected, T actual, string? message = null)
    {
        if (ValuesEqual(expected, actual))
            throw new CsjsAssertionException(message ?? $"Expected a value other than '{expected}'.");
    }

    public static void Null(object? value, string? message = null)
    {
        if (value is not null) throw new CsjsAssertionException(message ?? $"Expected null but got '{value}'.");
    }

    public static void NotNull(object? value, string? message = null)
    {
        if (value is null) throw new CsjsAssertionException(message ?? "Expected a non-null value.");
    }

    /// <summary>Compares floating point values within a tolerance.</summary>
    public static void Close(double expected, double actual, double tolerance = 1e-9, string? message = null)
    {
        var difference = expected > actual ? expected - actual : actual - expected;
        if (difference > tolerance)
            throw new CsjsAssertionException(message ?? $"Expected {expected} ± {tolerance} but got {actual}.");
    }

    public static void Contains(string haystack, string needle, string? message = null)
    {
        if (!haystack.Contains(needle))
            throw new CsjsAssertionException(message ?? $"Expected '{haystack}' to contain '{needle}'.");
    }

    /// <summary>Asserts that <paramref name="action"/> throws.</summary>
    public static void Throws(Action action, string? message = null)
    {
        var threw = false;
        try
        {
            action();
        }
        catch (Exception)
        {
            threw = true;
        }

        if (!threw) throw new CsjsAssertionException(message ?? "Expected the action to throw.");
    }

    public static void Fail(string message) => throw new CsjsAssertionException(message);

    private static bool ValuesEqual<T>(T left, T right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }
}

/// <summary>Thrown when an assertion fails. The runner reports these as failures, not crashes.</summary>
public sealed class CsjsAssertionException : Exception
{
    public CsjsAssertionException(string message) : base(message) { }
}
