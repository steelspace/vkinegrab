namespace vkinegrab.Models;

public sealed class BadgeSet : IEquatable<BadgeSet>
{
    private readonly string _key;

    private BadgeSet(IEnumerable<(BadgeKind Kind, string Code)> items)
    {
        var ordered = items.OrderBy(i => i.Kind).ThenBy(i => i.Code).ToList();
        _key = string.Join(",", ordered.Select(i => $"{(int)i.Kind}:{i.Code}"));
    }

    public static BadgeSet From(IEnumerable<CinemaBadge>? badges)
    {
        if (badges == null) return new BadgeSet(Enumerable.Empty<(BadgeKind, string)>());
        var items = badges.Select(b => (b.Kind, b.Code ?? string.Empty));
        return new BadgeSet(items);
    }

    public override string ToString() => _key;

    public override bool Equals(object? obj) => Equals(obj as BadgeSet);
    public bool Equals(BadgeSet? other) => other != null && _key == other._key;
    public override int GetHashCode() => _key.GetHashCode();
}