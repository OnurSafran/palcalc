using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PalCalc.Model.Tests
{
    [TestClass]
    public sealed class PalBreedingCatalogCalculatorTests : PalTestBase
    {
        [TestMethod]
        public void CalculateCatalog_AllPalsRepresented()
        {
            var results = PalBreedingCatalogCalculator.CalculateCatalog(new List<PalInstance>(), paldb, breedingdb);
            Assert.AreEqual(paldb.Pals.Count(), results.Count);
        }

        [TestMethod]
        public void CalculationSession_SummariesAreLightweightAndDetailsAreCachedAndOrdered()
        {
            var session = PalBreedingCatalogCalculationSession.Create(
                new List<PalInstance>(),
                paldb,
                breedingdb);

            Assert.IsTrue(session.Summaries.All(summary => summary.Recipes.Count == 0));

            var child = session.Summaries.First(summary =>
                breedingdb.Breeding.Any(recipe => recipe.Child == summary.ChildPal)).ChildPal;
            var first = session.GetDetails(child);
            var second = session.GetDetails(child);

            Assert.AreSame(first, second);
            Assert.IsNotEmpty(first.Recipes);
            var statusRanks = first.Recipes.Select(recipe => recipe.Status switch
            {
                RecipeAvailabilityStatus.BothParentsOwned => 3,
                RecipeAvailabilityStatus.IncompatibleParentsOwned => 2,
                RecipeAvailabilityStatus.OneParentOwned => 1,
                _ => 0
            }).ToList();
            CollectionAssert.AreEqual(statusRanks.OrderByDescending(rank => rank).ToList(), statusRanks);
        }

        [TestMethod]
        public void CalculationSession_CancellationThrowsOperationCanceledException()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsExactly<OperationCanceledException>(() =>
                PalBreedingCatalogCalculationSession.Create(
                    new List<PalInstance>(),
                    paldb,
                    breedingdb,
                    cancellationToken: cancellation.Token));
        }

        [TestMethod]
        public void CalculateCatalog_ValidPair_MarksReady()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");

            // Find child bred by Cattiva + Chikipi
            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
            var child = recipe.Child;

            var cattivaMale = new PalInstance
            {
                InstanceId = "cat_m_1",
                Pal = cattiva,
                Gender = PalGender.MALE
            };

            var chikipiFemale = new PalInstance
            {
                InstanceId = "chik_f_1",
                Pal = chikipi,
                Gender = PalGender.FEMALE
            };

            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[] { cattivaMale, chikipiFemale },
                paldb,
                breedingdb
            );

            var childResult = results.First(r => r.ChildPal == child);
            Assert.AreEqual(PalBreedingStatus.Ready, childResult.Status);
            Assert.IsTrue(childResult.TotalMatchingPairsCount > 0);
        }

        [TestMethod]
        public void CalculateCatalog_SameGenderParents_MarksSameGenderParentsOwned()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");

            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
            var child = recipe.Child;

            // Both parents are MALE!
            var cattivaMale = new PalInstance
            {
                InstanceId = "cat_m_1",
                Pal = cattiva,
                Gender = PalGender.MALE
            };

            var chikipiMale = new PalInstance
            {
                InstanceId = "chik_m_1",
                Pal = chikipi,
                Gender = PalGender.MALE
            };

            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[] { cattivaMale, chikipiMale },
                paldb,
                breedingdb
            );

            var childResult = results.First(r => r.ChildPal == child);
            Assert.AreEqual(PalBreedingStatus.MissingPair, childResult.Status);

            var recipeResult = childResult.Recipes.FirstOrDefault(r => r.Recipe == recipe);
            Assert.IsNotNull(recipeResult);
            Assert.AreEqual(RecipeAvailabilityStatus.IncompatibleParentsOwned, recipeResult.Status);
            Assert.AreEqual(0, recipeResult.MatchingPairs.Count);
        }

        [TestMethod]
        public void CalculateCatalog_DeduplicatesSameInstanceId()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");

            var cattiva1 = new PalInstance { InstanceId = "cat_1", Pal = cattiva, Gender = PalGender.MALE };
            var cattiva1Duplicate = new PalInstance { InstanceId = "cat_1", Pal = cattiva, Gender = PalGender.FEMALE };

            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[] { cattiva1, cattiva1Duplicate },
                paldb,
                breedingdb
            );

            var cattivaResult = results.First(r => r.ChildPal == cattiva);
            Assert.AreEqual(1, cattivaResult.OwnedCounts.Total);
        }

        [TestMethod]
        [DataRow(PalGender.NONE)]
        [DataRow(PalGender.WILDCARD)]
        [DataRow(PalGender.OPPOSITE_WILDCARD)]
        public void CalculateCatalog_RejectsNonConcreteGender(PalGender invalidGender)
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");

            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
            var child = recipe.Child;

            var cattivaNone = new PalInstance { InstanceId = "cat_invalid", Pal = cattiva, Gender = invalidGender };
            var chikipiFemale = new PalInstance { InstanceId = "chik_f", Pal = chikipi, Gender = PalGender.FEMALE };

            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[] { cattivaNone, chikipiFemale },
                paldb,
                breedingdb
            );

            var childResult = results.First(r => r.ChildPal == child);
            Assert.AreEqual(PalBreedingStatus.MissingPair, childResult.Status);
        }

        [TestMethod]
        public void CalculateCatalog_ReversedConcreteParentOrder_MarksReady()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");
            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));

            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[]
                {
                    new PalInstance { InstanceId = "chik_f", Pal = chikipi, Gender = PalGender.FEMALE },
                    new PalInstance { InstanceId = "cat_m", Pal = cattiva, Gender = PalGender.MALE }
                },
                paldb,
                breedingdb
            );

            Assert.AreEqual(PalBreedingStatus.Ready, results.Single(r => r.ChildPal == recipe.Child).Status);
        }

        [TestMethod]
        public void CalculateCatalog_ExplicitGenderRecipeRequiresCorrectAssignment()
        {
            var recipe = breedingdb.Breeding.First(b =>
                (b.Parent1.Gender == PalGender.MALE || b.Parent1.Gender == PalGender.FEMALE) &&
                (b.Parent2.Gender == PalGender.MALE || b.Parent2.Gender == PalGender.FEMALE)
            );
            var correctParents = new[]
            {
                new PalInstance { InstanceId = "explicit_1", Pal = recipe.Parent1.Pal, Gender = recipe.Parent1.Gender },
                new PalInstance { InstanceId = "explicit_2", Pal = recipe.Parent2.Pal, Gender = recipe.Parent2.Gender }
            };

            var correctResult = PalBreedingCatalogCalculator.CalculateCatalog(correctParents.Reverse(), paldb, breedingdb)
                .Single(r => r.ChildPal == recipe.Child);
            Assert.AreEqual(PalBreedingStatus.Ready, correctResult.Status);

            var wrongParents = new[]
            {
                new PalInstance { InstanceId = "wrong_1", Pal = recipe.Parent1.Pal, Gender = recipe.Parent2.Gender },
                new PalInstance { InstanceId = "wrong_2", Pal = recipe.Parent2.Pal, Gender = recipe.Parent1.Gender }
            };
            var wrongRecipeResult = PalBreedingCatalogCalculator.CalculateCatalog(wrongParents, paldb, breedingdb)
                .Single(r => r.ChildPal == recipe.Child)
                .Recipes.Single(r => r.Recipe == recipe);

            Assert.AreEqual(0, wrongRecipeResult.MatchingPairCount);
        }

        [TestMethod]
        public void CalculateCatalog_SameInstanceCannotFillBothParents()
        {
            var selfRecipe = breedingdb.Breeding.First(b => b.Parent1.Pal == b.Parent2.Pal);
            var onlyParent = new PalInstance
            {
                InstanceId = "only_parent",
                Pal = selfRecipe.Parent1.Pal,
                Gender = PalGender.MALE
            };

            var result = PalBreedingCatalogCalculator.CalculateCatalog(new[] { onlyParent }, paldb, breedingdb)
                .Single(r => r.ChildPal == selfRecipe.Child);

            Assert.AreEqual(PalBreedingStatus.MissingPair, result.Status);
            Assert.AreEqual(0, result.TotalMatchingPairsCount);
        }

        [TestMethod]
        public void CalculateCatalog_IdenticalDuplicateDoesNotInflatePairs()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");
            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
            var parent = new PalInstance { InstanceId = "cat_m", Pal = cattiva, Gender = PalGender.MALE };

            var result = PalBreedingCatalogCalculator.CalculateCatalog(
                new[]
                {
                    parent,
                    parent,
                    new PalInstance { InstanceId = "chik_f", Pal = chikipi, Gender = PalGender.FEMALE }
                },
                paldb,
                breedingdb
            ).Single(r => r.ChildPal == recipe.Child);

            Assert.AreEqual(1, result.TotalMatchingPairsCount);
        }

        [TestMethod]
        public void CalculateCatalog_ConflictingDuplicateMarksAvailabilityUnknown()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[]
                {
                    new PalInstance { InstanceId = "conflict", Pal = cattiva, Gender = PalGender.MALE },
                    new PalInstance { InstanceId = "conflict", Pal = cattiva, Gender = PalGender.FEMALE }
                },
                paldb,
                breedingdb
            );

            Assert.IsTrue(results.Where(r => r.Recipes.Count > 0).All(r => r.Status == PalBreedingStatus.Unknown));
            Assert.IsTrue(results.Where(r => r.Recipes.Count == 0).All(r => r.Status == PalBreedingStatus.Unavailable));
        }

        [TestMethod]
        public void CalculateCatalog_MissingInstanceIdMarksAvailabilityUnknown()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[] { new PalInstance { InstanceId = "", Pal = cattiva, Gender = PalGender.MALE } },
                paldb,
                breedingdb
            );

            Assert.IsTrue(results.Where(r => r.Recipes.Count > 0).All(r => r.Status == PalBreedingStatus.Unknown));
        }

        [TestMethod]
        public void CalculateCatalog_UnknownPalMarksAvailabilityUnknown()
        {
            var unknownPal = new Pal
            {
                Id = new PalId { PalDexNo = int.MaxValue },
                Name = "Unknown",
                InternalName = "Unknown"
            };
            var results = PalBreedingCatalogCalculator.CalculateCatalog(
                new[] { new PalInstance { InstanceId = "unknown", Pal = unknownPal, Gender = PalGender.MALE } },
                paldb,
                breedingdb
            );

            Assert.IsTrue(results.Where(r => r.Recipes.Count > 0).All(r => r.Status == PalBreedingStatus.Unknown));
        }

        [TestMethod]
        public void CalculateCatalog_CapsDisplayedPairsButKeepsExactCount()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");
            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
            var owned = Enumerable.Range(0, 101)
                .Select(i => new PalInstance { InstanceId = $"cat_{i}", Pal = cattiva, Gender = PalGender.MALE })
                .Append(new PalInstance { InstanceId = "chik_f", Pal = chikipi, Gender = PalGender.FEMALE })
                .ToList();

            var recipeResult = PalBreedingCatalogCalculator.CalculateCatalog(owned, paldb, breedingdb)
                .Single(r => r.ChildPal == recipe.Child)
                .Recipes.Single(r => r.Recipe == recipe);

            Assert.AreEqual(101, recipeResult.MatchingPairCount);
            Assert.AreEqual(PalBreedingCatalogCalculator.MaxDisplayedPairsPerRecipe, recipeResult.MatchingPairs.Count);
            Assert.IsTrue(recipeResult.HasMoreMatchingPairs);
        }

        [TestMethod]
        public void CalculateCatalog_DoesNotCallRecipeExpeditionOnlyWhenLaterPairIsAvailable()
        {
            var cattiva = paldb.Pals.First(p => p.Name == "Cattiva");
            var chikipi = paldb.Pals.First(p => p.Name == "Chikipi");
            var recipe = breedingdb.Breeding.First(b => b.Parents.Any(p => p.Pal == cattiva) && b.Parents.Any(p => p.Pal == chikipi));
            var owned = Enumerable.Range(0, 100)
                .Select(i => new PalInstance
                {
                    InstanceId = $"cat_expedition_{i}",
                    Pal = cattiva,
                    Gender = PalGender.MALE,
                    IsOnExpedition = true
                })
                .Append(new PalInstance
                {
                    InstanceId = "cat_available",
                    Pal = cattiva,
                    Gender = PalGender.MALE
                })
                .Append(new PalInstance
                {
                    InstanceId = "chik_available",
                    Pal = chikipi,
                    Gender = PalGender.FEMALE
                });

            var recipeResult = PalBreedingCatalogCalculator.CalculateCatalog(owned, paldb, breedingdb)
                .Single(r => r.ChildPal == recipe.Child)
                .Recipes.Single(r => r.Recipe == recipe);

            Assert.IsTrue(recipeResult.HasNonExpeditionMatchingPair);
            Assert.AreNotEqual(RecipeMissingReason.OnlyExpeditionParentsAvailable, recipeResult.MissingReason);
        }
    }
}
