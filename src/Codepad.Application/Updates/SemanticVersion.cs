using System.Globalization;

namespace Codepad.Application.Updates;

/// <summary>
/// A version by the SemVer 2.0 rules. The updater orders releases by this value and never by an upload date. A leading <c>v</c>
/// is accepted, as in a Git tag. Build metadata after <c>+</c> is read and ignored in the order, as SemVer says.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] preRelease;

    private SemanticVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        this.preRelease = preRelease;
    }

    /// <summary>Gets the major number.</summary>
    public int Major { get; }

    /// <summary>Gets the minor number.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch number.</summary>
    public int Patch { get; }

    /// <summary>Gets a value indicating whether this is a stable version, which has no pre-release part.</summary>
    public bool IsStable => preRelease.Length == 0;

    /// <summary>Reads a version.</summary>
    /// <param name="text">The text, for example <c>1.2.3</c>, <c>v1.2.3</c> or <c>1.2.3-rc.1</c>.</param>
    /// <param name="version">The version when the text is valid.</param>
    /// <returns>True when the text is a valid version.</returns>
    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string rest = text[0] == 'v' ? text[1..] : text;
        int plus = rest.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            string metadata = rest[(plus + 1)..];
            if (metadata.Length == 0 || !metadata.Split('.').All(IsIdentifier))
            {
                return false;
            }

            rest = rest[..plus];
        }

        string[] pre = [];
        int dash = rest.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            string tail = rest[(dash + 1)..];
            pre = tail.Split('.');
            if (tail.Length == 0 || !pre.All(part => IsIdentifier(part) && !(IsNumber(part) && part.Length > 1 && part[0] == '0')))
            {
                return false;
            }

            rest = rest[..dash];
        }

        string[] numbers = rest.Split('.');
        if (numbers.Length != 3 || !numbers.All(IsNumber) || numbers.Any(number => number.Length > 1 && number[0] == '0'))
        {
            return false;
        }

        if (!int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(numbers[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, pre);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        result = result != 0 ? result : Minor.CompareTo(other.Minor);
        result = result != 0 ? result : Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // A version with a pre-release part is lower than the same version without one.
        if (IsStable || other.IsStable)
        {
            return IsStable == other.IsStable ? 0 : IsStable ? 1 : -1;
        }

        for (int index = 0; index < Math.Min(preRelease.Length, other.preRelease.Length); index++)
        {
            int part = ComparePart(preRelease[index], other.preRelease[index]);
            if (part != 0)
            {
                return part;
            }
        }

        return preRelease.Length.CompareTo(other.preRelease.Length);
    }

    /// <inheritdoc />
    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    /// <summary>Tells whether two versions are equal by the SemVer order.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Tells whether two versions differ.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    /// <summary>Tells whether the first version is lower.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>True when lower.</returns>
    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Tells whether the first version is lower or equal.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>True when lower or equal.</returns>
    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => left is null || left.CompareTo(right) <= 0;

    /// <summary>Tells whether the first version is higher.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>True when higher.</returns>
    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => left is not null && left.CompareTo(right) > 0;

    /// <summary>Tells whether the first version is higher or equal.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>True when higher or equal.</returns>
    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', preRelease));

    /// <inheritdoc />
    public override string ToString()
    {
        string core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return preRelease.Length == 0 ? core : core + "-" + string.Join('.', preRelease);
    }

    private static int ComparePart(string left, string right)
    {
        bool leftNumber = IsNumber(left);
        bool rightNumber = IsNumber(right);
        if (leftNumber && rightNumber)
        {
            // Numbers can be longer than a long. Compare by length, then by digits.
            return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
        }

        if (leftNumber != rightNumber)
        {
            return leftNumber ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool IsNumber(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    private static bool IsIdentifier(string text) => text.Length > 0 && text.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
