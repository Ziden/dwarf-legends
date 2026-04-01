using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DwarfFortress.GameLogic.Data;

/// <summary>
/// Immutable set of string tags. The universal connector between content definitions.
/// Items, tiles, creatures, recipes, and jobs all carry TagSets.
/// A recipe input says "I need 1 item with tags [metal][refined]" — not "1 iron bar".
/// </summary>
public sealed class TagSet
{
    private readonly ImmutableHashSet<string> _tags;

    public static readonly TagSet Empty = new(ImmutableHashSet<string>.Empty);

    private TagSet(ImmutableHashSet<string> tags) => _tags = tags;

    public static TagSet From(params string[] tags)
        => new(tags.Select(t => t.ToLowerInvariant()).ToImmutableHashSet());

    public static TagSet From(IEnumerable<string> tags)
        => new(tags.Select(t => t.ToLowerInvariant()).ToImmutableHashSet());

    /// <summary>Returns true if ALL of the provided tags are present.</summary>
    public bool HasAll(params string[] required)
        => required.All(t => _tags.Contains(t.ToLowerInvariant()));

    /// <summary>Returns true if ANY of the provided tags are present.</summary>
    public bool HasAny(params string[] tags)
        => tags.Any(t => _tags.Contains(t.ToLowerInvariant()));

    /// <summary>Returns true if the given tag is present.</summary>
    public bool Contains(string tag) => _tags.Contains(tag.ToLowerInvariant());

    /// <summary>Returns a new TagSet with the added tag.</summary>
    public TagSet With(string tag)
        => new(_tags.Add(tag.ToLowerInvariant()));

    /// <summary>Returns a new TagSet without the specified tag.</summary>
    public TagSet Without(string tag)
        => new(_tags.Remove(tag.ToLowerInvariant()));

    /// <summary>Returns a new TagSet that is the union of this and another TagSet.</summary>
    public TagSet Union(TagSet other)
        => new(_tags.Union(other._tags));

    public int Count => _tags.Count;
    public IReadOnlyCollection<string> All => _tags;

    public override string ToString()
        => $"[{string.Join(", ", _tags.OrderBy(t => t))}]";

    public override bool Equals(object? obj)
        => obj is TagSet other && _tags.SetEquals(other._tags);

    public override int GetHashCode()
    {
        // Order-independent hash
        int hash = 0;
        foreach (var tag in _tags)
            hash ^= tag.GetHashCode(StringComparison.OrdinalIgnoreCase);
        return hash;
    }
}
