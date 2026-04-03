using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GodotClient.UI;


public sealed record RequirementAnalysis(bool CanFulfill, string NeededSummary, string MissingSummary);

public static class SelectionRequirementHelper
{
    public static RequirementAnalysis Analyze(GameSimulation simulation, IReadOnlyList<RecipeInput> inputs)
    {
        var itemSystem = simulation.Context.Get<ItemSystem>();
        var dataManager = simulation.Context.Get<DataManager>();
        var available = itemSystem.GetUsableItems().ToList();

        var requirements = inputs
            .SelectMany(input => Enumerable.Repeat(input.RequiredTags, input.Quantity))
            .OrderByDescending(tags => tags.Count)
            .ToList();

        var neededSummary = string.Join(" | ", Summarize(requirements));
        var bestMatched = new List<TagSet>();
        TryFindBest(requirements, 0, available, dataManager, new HashSet<int>(), new List<TagSet>(), ref bestMatched);

        if (bestMatched.Count == requirements.Count)
            return new RequirementAnalysis(true, neededSummary, string.Empty);

        var matchedCounts = bestMatched.GroupBy(tags => FormatTagSet(tags))
            .ToDictionary(group => group.Key, group => group.Count());
        var missing = new List<string>();
        foreach (var group in requirements.GroupBy(tags => FormatTagSet(tags)))
        {
            int matched = matchedCounts.GetValueOrDefault(group.Key);
            int missingCount = group.Count() - matched;
            if (missingCount > 0)
                missing.Add($"{missingCount}x {group.Key}");
        }

        return new RequirementAnalysis(false, neededSummary, string.Join(" | ", missing));
    }

    private static bool TryFindBest(
        IReadOnlyList<TagSet> requirements,
        int index,
        IReadOnlyList<Item> available,
        DataManager dataManager,
        HashSet<int> matchedIds,
        List<TagSet> matchedRequirements,
        ref List<TagSet> bestMatched)
    {
        if (matchedRequirements.Count > bestMatched.Count)
            bestMatched = new List<TagSet>(matchedRequirements);

        if (index >= requirements.Count)
            return true;

        var tags = requirements[index];
        var candidates = available
            .Where(item => !matchedIds.Contains(item.Id) && MatchesTags(dataManager, item.DefId, tags))
            .ToList();

        bool solved = false;
        foreach (var candidate in candidates)
        {
            matchedIds.Add(candidate.Id);
            matchedRequirements.Add(tags);

            if (TryFindBest(requirements, index + 1, available, dataManager, matchedIds, matchedRequirements, ref bestMatched))
                solved = true;

            matchedRequirements.RemoveAt(matchedRequirements.Count - 1);
            matchedIds.Remove(candidate.Id);

            if (solved)
                return true;
        }

        return false;
    }

    private static bool MatchesTags(DataManager dataManager, string itemDefId, TagSet requiredTags)
    {
        var def = dataManager.Items.GetOrNull(itemDefId);
        return def?.Tags.HasAll(requiredTags.All.ToArray()) ?? false;
    }

    private static IEnumerable<string> Summarize(IReadOnlyList<TagSet> requirements)
        => requirements
            .GroupBy(FormatTagSet)
            .Select(group => $"{group.Count()}x {group.Key}")
            .OrderBy(text => text);

    private static string FormatTagSet(TagSet tags)
        => string.Join("/", tags.All.OrderBy(tag => tag));
}