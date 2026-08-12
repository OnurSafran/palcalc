using System;
using System.Collections.Generic;
using System.Linq;

namespace PalCalc.Model
{
    public enum PalBreedingStatus
    {
        Ready,
        MissingPair,
        Unavailable,
        Unknown
    }

    public enum RecipeAvailabilityStatus
    {
        BothParentsOwned,
        IncompatibleParentsOwned,
        OneParentOwned,
        NeitherParentOwned,
        Unknown
    }

    public class OwnedPalGenderCounts
    {
        public int Total { get; set; }
        public int MaleCount { get; set; }
        public int FemaleCount { get; set; }
        public int OtherCount { get; set; }

        public bool HasMale => MaleCount > 0;
        public bool HasFemale => FemaleCount > 0;
        public bool HasConcretePair => HasMale && HasFemale;
    }

    public class PalBreedingPairResult
    {
        public PalInstance Parent1 { get; set; }
        public PalInstance Parent2 { get; set; }
        public bool HasExpeditionParent => (Parent1?.IsOnExpedition ?? false) || (Parent2?.IsOnExpedition ?? false);
    }

    public class RecipeMatchResult
    {
        public BreedingResult Recipe { get; set; }
        public RecipeAvailabilityStatus Status { get; set; }
        public List<PalBreedingPairResult> MatchingPairs { get; set; } = new List<PalBreedingPairResult>();
        public int MatchingPairCount { get; set; }
        public bool HasMoreMatchingPairs => MatchingPairCount > MatchingPairs.Count;
        public OwnedPalGenderCounts Parent1Counts { get; set; }
        public OwnedPalGenderCounts Parent2Counts { get; set; }
    }

    public class PalCatalogEntryResult
    {
        public Pal ChildPal { get; set; }
        public PalBreedingStatus Status { get; set; }
        public OwnedPalGenderCounts OwnedCounts { get; set; }
        public List<RecipeMatchResult> Recipes { get; set; } = new List<RecipeMatchResult>();
        public int TotalMatchingPairsCount => Recipes.Sum(r => r.MatchingPairCount);
        public bool HasMatchingPair => Status == PalBreedingStatus.Ready;
    }

    public static class PalBreedingCatalogCalculator
    {
        public const int MaxDisplayedPairsPerRecipe = 100;

        public static List<PalCatalogEntryResult> CalculateCatalog(
            IEnumerable<PalInstance> rawOwnedPals,
            PalDB palDb,
            PalBreedingDB breedingDb,
            bool ownedDataIsKnown = true
        )
        {
            if (palDb == null) throw new ArgumentNullException(nameof(palDb));
            if (breedingDb == null) throw new ArgumentNullException(nameof(breedingDb));

            // Duplicate references are normal, but conflicting records or missing identity
            // fields make save-specific availability unsafe to assert.
            var sourcePals = (rawOwnedPals ?? Enumerable.Empty<PalInstance>()).ToList();
            var validPals = sourcePals
                .Where(p => p != null && p.Pal != null && !string.IsNullOrWhiteSpace(p.InstanceId))
                .ToList();
            var catalogPals = palDb.Pals.ToHashSet();
            var hasMalformedRecord = sourcePals.Any(p => p == null || p.Pal == null || string.IsNullOrWhiteSpace(p.InstanceId));
            var hasUnknownPal = validPals.Any(p => !catalogPals.Contains(p.Pal));
            var hasConflictingRecord = validPals
                .GroupBy(p => p.InstanceId, StringComparer.Ordinal)
                .Any(g => g.Select(p => (p.Pal, p.Gender)).Distinct().Count() > 1);
            ownedDataIsKnown &= !hasMalformedRecord && !hasUnknownPal && !hasConflictingRecord;

            var ownedPals = validPals
                .GroupBy(p => p.InstanceId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            // 2. Index owned instances by Pal
            var ownedByPal = ownedPals
                .GroupBy(p => p.Pal)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 3. Compute gender counts per Pal
            var countsByPal = palDb.Pals.ToDictionary(
                pal => pal,
                pal =>
                {
                    if (!ownedByPal.TryGetValue(pal, out var instances))
                    {
                        return new OwnedPalGenderCounts();
                    }

                    int male = instances.Count(i => i.Gender == PalGender.MALE);
                    int female = instances.Count(i => i.Gender == PalGender.FEMALE);
                    int total = instances.Count;
                    int other = total - male - female;

                    return new OwnedPalGenderCounts
                    {
                        Total = total,
                        MaleCount = male,
                        FemaleCount = female,
                        OtherCount = other
                    };
                }
            );

            // 4. Index breeding recipes by Child Pal
            var recipesByChild = breedingDb.Breeding
                .GroupBy(b => b.Child)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 5. Build Catalog entry results for every Pal in PalDB
            var results = new List<PalCatalogEntryResult>();

            foreach (var pal in palDb.Pals)
            {
                var ownedCounts = countsByPal[pal];

                if (!recipesByChild.TryGetValue(pal, out var recipes) || recipes.Count == 0)
                {
                    results.Add(new PalCatalogEntryResult
                    {
                        ChildPal = pal,
                        Status = PalBreedingStatus.Unavailable,
                        OwnedCounts = ownedCounts,
                        Recipes = new List<RecipeMatchResult>()
                    });
                    continue;
                }

                var recipeResults = new List<RecipeMatchResult>();

                foreach (var recipe in recipes)
                {
                    var parent1Pal = recipe.Parent1.Pal;
                    var parent2Pal = recipe.Parent2.Pal;

                    var p1Counts = countsByPal.GetValueOrDefault(parent1Pal, new OwnedPalGenderCounts());
                    var p2Counts = countsByPal.GetValueOrDefault(parent2Pal, new OwnedPalGenderCounts());

                    var p1Instances = ownedByPal.GetValueOrDefault(parent1Pal, new List<PalInstance>());
                    var p2Instances = ownedByPal.GetValueOrDefault(parent2Pal, new List<PalInstance>());

                    var pairSearch = FindMatchingPairs(recipe, p1Instances, p2Instances);
                    var hasBothSuitableParents = HasBothSuitableParents(recipe, p1Instances, p2Instances);
                    var hasOneSuitableParent = HasSuitableParent(recipe.Parent1, p1Instances) ||
                                               HasSuitableParent(recipe.Parent2, p2Instances);

                    RecipeAvailabilityStatus recipeStatus;
                    if (!ownedDataIsKnown)
                    {
                        recipeStatus = RecipeAvailabilityStatus.Unknown;
                    }
                    else if (pairSearch.Count > 0)
                    {
                        recipeStatus = RecipeAvailabilityStatus.BothParentsOwned;
                    }
                    else if (hasBothSuitableParents)
                    {
                        recipeStatus = RecipeAvailabilityStatus.IncompatibleParentsOwned;
                    }
                    else if (hasOneSuitableParent)
                    {
                        recipeStatus = RecipeAvailabilityStatus.OneParentOwned;
                    }
                    else
                    {
                        recipeStatus = RecipeAvailabilityStatus.NeitherParentOwned;
                    }

                    recipeResults.Add(new RecipeMatchResult
                    {
                        Recipe = recipe,
                        Status = recipeStatus,
                        MatchingPairs = pairSearch.Pairs,
                        MatchingPairCount = pairSearch.Count,
                        Parent1Counts = p1Counts,
                        Parent2Counts = p2Counts
                    });
                }

                bool hasAnyMatchingPair = recipeResults.Any(r => r.MatchingPairCount > 0);

                results.Add(new PalCatalogEntryResult
                {
                    ChildPal = pal,
                    Status = !ownedDataIsKnown
                        ? PalBreedingStatus.Unknown
                        : hasAnyMatchingPair ? PalBreedingStatus.Ready : PalBreedingStatus.MissingPair,
                    OwnedCounts = ownedCounts,
                    Recipes = recipeResults
                });
            }

            return results;
        }

        private static bool HasBothSuitableParents(
            BreedingResult recipe,
            List<PalInstance> p1Instances,
            List<PalInstance> p2Instances
        )
        {
            var p1Candidates = SuitableParents(recipe.Parent1, p1Instances).ToList();
            var p2Candidates = SuitableParents(recipe.Parent2, p2Instances).ToList();
            return p1Candidates.Any(p1 => p2Candidates.Any(p2 => p1.InstanceId != p2.InstanceId));
        }

        private static bool HasSuitableParent(GenderedPal requiredParent, List<PalInstance> instances) =>
            SuitableParents(requiredParent, instances).Any();

        private static IEnumerable<PalInstance> SuitableParents(GenderedPal requiredParent, IEnumerable<PalInstance> instances) =>
            instances.Where(instance =>
                (instance.Gender == PalGender.MALE || instance.Gender == PalGender.FEMALE) &&
                (requiredParent.Gender == PalGender.WILDCARD ||
                 requiredParent.Gender == PalGender.OPPOSITE_WILDCARD ||
                 requiredParent.Gender == instance.Gender)
            );

        private static (List<PalBreedingPairResult> Pairs, int Count) FindMatchingPairs(
            BreedingResult recipe,
            List<PalInstance> p1Instances,
            List<PalInstance> p2Instances
        )
        {
            var pairList = new List<PalBreedingPairResult>();
            var seenPairKeys = new HashSet<string>();
            var pairCount = 0;

            foreach (var inst1 in p1Instances)
            {
                if (inst1.Gender != PalGender.MALE && inst1.Gender != PalGender.FEMALE)
                    continue;

                foreach (var inst2 in p2Instances)
                {
                    if (inst2.Gender != PalGender.MALE && inst2.Gender != PalGender.FEMALE)
                        continue;

                    // Must be distinct instances
                    if (inst1.InstanceId == inst2.InstanceId)
                        continue;

                    // Must be opposite concrete genders
                    if (inst1.Gender == inst2.Gender)
                        continue;

                    // Recipe gender match check
                    if (!recipe.Matches(inst1.Pal, inst1.Gender, inst2.Pal, inst2.Gender))
                        continue;

                    // Canonical pair key to avoid duplicate (A,B) and (B,A) entries
                    var pairKey = string.CompareOrdinal(inst1.InstanceId, inst2.InstanceId) < 0
                        ? $"{inst1.InstanceId}_{inst2.InstanceId}"
                        : $"{inst2.InstanceId}_{inst1.InstanceId}";

                    if (seenPairKeys.Add(pairKey))
                    {
                        pairCount++;
                        if (pairList.Count < MaxDisplayedPairsPerRecipe)
                        {
                            pairList.Add(new PalBreedingPairResult
                            {
                                Parent1 = inst1,
                                Parent2 = inst2
                            });
                        }
                    }
                }
            }

            return (pairList, pairCount);
        }
    }
}
