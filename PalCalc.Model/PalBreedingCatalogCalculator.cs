using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

    public enum RecipeMissingReason
    {
        None,
        MissingParent1,
        MissingParent2,
        MissingBothParents,
        MissingGenderPair,
        OnlyExpeditionParentsAvailable
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
        public RecipeMissingReason MissingReason { get; set; }
        public List<PalBreedingPairResult> MatchingPairs { get; set; } = new List<PalBreedingPairResult>();
        public int MatchingPairCount { get; set; }
        public bool HasNonExpeditionMatchingPair { get; set; }
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
            var session = PalBreedingCatalogCalculationSession.Create(rawOwnedPals, palDb, breedingDb, ownedDataIsKnown);
            return session.Summaries.Select(summary => session.GetDetails(summary.ChildPal)).ToList();
        }

        internal static bool HasBothSuitableParents(
            BreedingResult recipe,
            List<PalInstance> p1Instances,
            List<PalInstance> p2Instances
        )
        {
            var p1Candidates = SuitableParents(recipe.Parent1, p1Instances).ToList();
            var p2Candidates = SuitableParents(recipe.Parent2, p2Instances).ToList();
            return p1Candidates.Any(p1 => p2Candidates.Any(p2 => p1.InstanceId != p2.InstanceId));
        }

        internal static bool HasSuitableParent(GenderedPal requiredParent, List<PalInstance> instances) =>
            SuitableParents(requiredParent, instances).Any();

        internal static IEnumerable<PalInstance> SuitableParents(GenderedPal requiredParent, IEnumerable<PalInstance> instances) =>
            instances.Where(instance =>
                (instance.Gender == PalGender.MALE || instance.Gender == PalGender.FEMALE) &&
                (requiredParent.Gender == PalGender.WILDCARD ||
                 requiredParent.Gender == PalGender.OPPOSITE_WILDCARD ||
                 requiredParent.Gender == instance.Gender)
            );

        internal static (List<PalBreedingPairResult> Pairs, int Count, bool HasNonExpeditionPair) FindMatchingPairs(
            BreedingResult recipe,
            List<PalInstance> p1Instances,
            List<PalInstance> p2Instances,
            CancellationToken cancellationToken = default
        )
        {
            var pairList = new List<PalBreedingPairResult>();
            var seenPairKeys = new HashSet<string>();
            var pairCount = 0;
            var hasNonExpeditionPair = false;

            foreach (var inst1 in p1Instances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (inst1.Gender != PalGender.MALE && inst1.Gender != PalGender.FEMALE)
                    continue;

                foreach (var inst2 in p2Instances)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                        if (!inst1.IsOnExpedition && !inst2.IsOnExpedition)
                            hasNonExpeditionPair = true;

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

            return (pairList, pairCount, hasNonExpeditionPair);
        }
    }

    public sealed class PalBreedingCatalogCalculationSession
    {
        private const int DetailCacheCapacity = 20;
        private static readonly List<PalInstance> NoOwnedPals = new();

        private readonly bool ownedDataIsKnown;
        private readonly Dictionary<Pal, List<PalInstance>> ownedByPal;
        private readonly Dictionary<Pal, OwnedPalGenderCounts> countsByPal;
        private readonly Dictionary<Pal, List<BreedingResult>> recipesByChild;
        private readonly Dictionary<Pal, PalCatalogEntryResult> detailCache = new();
        private readonly LinkedList<Pal> detailCacheOrder = new();
        private readonly object detailCacheLock = new();

        private PalBreedingCatalogCalculationSession(
            bool ownedDataIsKnown,
            Dictionary<Pal, List<PalInstance>> ownedByPal,
            Dictionary<Pal, OwnedPalGenderCounts> countsByPal,
            Dictionary<Pal, List<BreedingResult>> recipesByChild,
            IReadOnlyList<PalCatalogEntryResult> summaries)
        {
            this.ownedDataIsKnown = ownedDataIsKnown;
            this.ownedByPal = ownedByPal;
            this.countsByPal = countsByPal;
            this.recipesByChild = recipesByChild;
            Summaries = summaries;
        }

        public IReadOnlyList<PalCatalogEntryResult> Summaries { get; }

        public static PalBreedingCatalogCalculationSession Create(
            IEnumerable<PalInstance> rawOwnedPals,
            PalDB palDb,
            PalBreedingDB breedingDb,
            bool ownedDataIsKnown = true,
            CancellationToken cancellationToken = default)
        {
            if (palDb == null) throw new ArgumentNullException(nameof(palDb));
            if (breedingDb == null) throw new ArgumentNullException(nameof(breedingDb));

            var catalogPalsList = palDb.Pals.ToList();
            var catalogPals = catalogPalsList.ToHashSet();
            var sourcePals = (rawOwnedPals ?? Enumerable.Empty<PalInstance>()).ToList();
            var validPals = sourcePals
                .Where(p => p != null && p.Pal != null && !string.IsNullOrWhiteSpace(p.InstanceId))
                .ToList();
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
            var ownedByPal = ownedPals
                .GroupBy(p => p.Pal)
                .ToDictionary(g => g.Key, g => g.ToList());
            var countsByPal = catalogPalsList.ToDictionary(
                pal => pal,
                pal => CountOwnedPals(ownedByPal.GetValueOrDefault(pal)));
            var recipesByChild = breedingDb.Breeding
                .GroupBy(b => b.Child)
                .ToDictionary(g => g.Key, g => g.ToList());

            var summaries = new List<PalCatalogEntryResult>(catalogPalsList.Count);
            foreach (var pal in catalogPalsList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hasRecipes = recipesByChild.TryGetValue(pal, out var recipes) && recipes.Count > 0;
                var status = !hasRecipes
                    ? PalBreedingStatus.Unavailable
                    : !ownedDataIsKnown
                        ? PalBreedingStatus.Unknown
                        : recipes.Any(recipe => HasAnyMatchingPair(recipe, ownedByPal, cancellationToken))
                            ? PalBreedingStatus.Ready
                            : PalBreedingStatus.MissingPair;

                summaries.Add(new PalCatalogEntryResult
                {
                    ChildPal = pal,
                    Status = status,
                    OwnedCounts = countsByPal[pal]
                });
            }

            return new PalBreedingCatalogCalculationSession(
                ownedDataIsKnown,
                ownedByPal,
                countsByPal,
                recipesByChild,
                summaries);
        }

        public PalCatalogEntryResult GetDetails(Pal childPal, CancellationToken cancellationToken = default)
        {
            if (childPal == null) throw new ArgumentNullException(nameof(childPal));

            lock (detailCacheLock)
            {
                if (detailCache.TryGetValue(childPal, out var cached))
                {
                    detailCacheOrder.Remove(childPal);
                    detailCacheOrder.AddLast(childPal);
                    return cached;
                }
            }

            var calculated = CalculateDetails(childPal, cancellationToken);

            lock (detailCacheLock)
            {
                if (detailCache.TryGetValue(childPal, out var cached))
                    return cached;

                detailCache[childPal] = calculated;
                detailCacheOrder.AddLast(childPal);
                if (detailCache.Count > DetailCacheCapacity)
                {
                    var oldest = detailCacheOrder.First.Value;
                    detailCacheOrder.RemoveFirst();
                    detailCache.Remove(oldest);
                }
            }

            return calculated;
        }

        private PalCatalogEntryResult CalculateDetails(Pal childPal, CancellationToken cancellationToken)
        {
            if (!recipesByChild.TryGetValue(childPal, out var recipes) || recipes.Count == 0)
            {
                return new PalCatalogEntryResult
                {
                    ChildPal = childPal,
                    Status = PalBreedingStatus.Unavailable,
                    OwnedCounts = countsByPal.GetValueOrDefault(childPal, new OwnedPalGenderCounts())
                };
            }

            var recipeResults = new List<RecipeMatchResult>(recipes.Count);
            foreach (var recipe in recipes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var p1Counts = countsByPal.GetValueOrDefault(recipe.Parent1.Pal, new OwnedPalGenderCounts());
                var p2Counts = countsByPal.GetValueOrDefault(recipe.Parent2.Pal, new OwnedPalGenderCounts());
                var p1Instances = ownedByPal.GetValueOrDefault(recipe.Parent1.Pal, NoOwnedPals);
                var p2Instances = ownedByPal.GetValueOrDefault(recipe.Parent2.Pal, NoOwnedPals);
                var pairSearch = PalBreedingCatalogCalculator.FindMatchingPairs(
                    recipe,
                    p1Instances,
                    p2Instances,
                    cancellationToken);
                var hasBothSuitableParents = PalBreedingCatalogCalculator.HasBothSuitableParents(recipe, p1Instances, p2Instances);
                var hasOneSuitableParent = PalBreedingCatalogCalculator.HasSuitableParent(recipe.Parent1, p1Instances) ||
                                           PalBreedingCatalogCalculator.HasSuitableParent(recipe.Parent2, p2Instances);
                var status = GetRecipeStatus(pairSearch.Count, hasBothSuitableParents, hasOneSuitableParent);
                var missingReason = GetMissingReason(
                    status,
                    pairSearch.HasNonExpeditionPair,
                    p1Counts,
                    p2Counts);

                recipeResults.Add(new RecipeMatchResult
                {
                    Recipe = recipe,
                    Status = status,
                    MissingReason = missingReason,
                    MatchingPairs = pairSearch.Pairs,
                    MatchingPairCount = pairSearch.Count,
                    HasNonExpeditionMatchingPair = pairSearch.HasNonExpeditionPair,
                    Parent1Counts = p1Counts,
                    Parent2Counts = p2Counts
                });
            }

            // Cache this ordering with the detail result; repeated selections do not sort again.
            recipeResults = recipeResults
                .OrderByDescending(r => r.Status == RecipeAvailabilityStatus.BothParentsOwned)
                .ThenByDescending(r => r.Status == RecipeAvailabilityStatus.IncompatibleParentsOwned)
                .ThenByDescending(r => r.Status == RecipeAvailabilityStatus.OneParentOwned)
                .ToList();

            return new PalCatalogEntryResult
            {
                ChildPal = childPal,
                Status = !ownedDataIsKnown
                    ? PalBreedingStatus.Unknown
                    : recipeResults.Any(r => r.MatchingPairCount > 0)
                        ? PalBreedingStatus.Ready
                        : PalBreedingStatus.MissingPair,
                OwnedCounts = countsByPal.GetValueOrDefault(childPal, new OwnedPalGenderCounts()),
                Recipes = recipeResults
            };
        }

        private RecipeAvailabilityStatus GetRecipeStatus(int pairCount, bool hasBothSuitableParents, bool hasOneSuitableParent)
        {
            if (!ownedDataIsKnown) return RecipeAvailabilityStatus.Unknown;
            if (pairCount > 0) return RecipeAvailabilityStatus.BothParentsOwned;
            if (hasBothSuitableParents) return RecipeAvailabilityStatus.IncompatibleParentsOwned;
            if (hasOneSuitableParent) return RecipeAvailabilityStatus.OneParentOwned;
            return RecipeAvailabilityStatus.NeitherParentOwned;
        }

        private static RecipeMissingReason GetMissingReason(
            RecipeAvailabilityStatus status,
            bool hasNonExpeditionPair,
            OwnedPalGenderCounts p1Counts,
            OwnedPalGenderCounts p2Counts)
        {
            if (status == RecipeAvailabilityStatus.Unknown)
                return RecipeMissingReason.None;
            if (status == RecipeAvailabilityStatus.BothParentsOwned)
                return hasNonExpeditionPair ? RecipeMissingReason.None : RecipeMissingReason.OnlyExpeditionParentsAvailable;
            if (status == RecipeAvailabilityStatus.IncompatibleParentsOwned)
                return RecipeMissingReason.MissingGenderPair;
            if (p1Counts.Total == 0 && p2Counts.Total == 0)
                return RecipeMissingReason.MissingBothParents;
            if (p1Counts.Total == 0)
                return RecipeMissingReason.MissingParent1;
            if (p2Counts.Total == 0)
                return RecipeMissingReason.MissingParent2;
            return RecipeMissingReason.MissingGenderPair;
        }

        private static OwnedPalGenderCounts CountOwnedPals(List<PalInstance> instances)
        {
            if (instances == null) return new OwnedPalGenderCounts();
            var male = instances.Count(i => i.Gender == PalGender.MALE);
            var female = instances.Count(i => i.Gender == PalGender.FEMALE);
            return new OwnedPalGenderCounts
            {
                Total = instances.Count,
                MaleCount = male,
                FemaleCount = female,
                OtherCount = instances.Count - male - female
            };
        }

        private static bool HasAnyMatchingPair(
            BreedingResult recipe,
            Dictionary<Pal, List<PalInstance>> ownedByPal,
            CancellationToken cancellationToken)
        {
            var p1Instances = ownedByPal.GetValueOrDefault(recipe.Parent1.Pal, NoOwnedPals);
            var p2Instances = ownedByPal.GetValueOrDefault(recipe.Parent2.Pal, NoOwnedPals);
            foreach (var p1 in p1Instances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (p1.Gender != PalGender.MALE && p1.Gender != PalGender.FEMALE)
                    continue;

                foreach (var p2 in p2Instances)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (p2.Gender != PalGender.MALE && p2.Gender != PalGender.FEMALE)
                    continue;
                    if (p1.InstanceId == p2.InstanceId || p1.Gender == p2.Gender)
                    continue;
                    if (recipe.Matches(p1.Pal, p1.Gender, p2.Pal, p2.Gender))
                        return true;
                }
            }

            return false;
        }
    }
}
