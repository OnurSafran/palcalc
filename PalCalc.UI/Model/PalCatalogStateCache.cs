using PalCalc.Model;
using PalCalc.UI.ViewModel.Inspector;
using System.Collections.Generic;

namespace PalCalc.UI.Model
{
    public class PalCatalogState
    {
        public string SearchText { get; set; } = "";
        public PalCatalogFilterOption SelectedFilter { get; set; } = PalCatalogFilterOption.All;
        public PalCatalogSortOption SelectedSort { get; set; } = PalCatalogSortOption.PalDex;
        public PalId SelectedPalId { get; set; }
        public List<string> PinnedPairKeys { get; set; } = new();
    }

    public static class PalCatalogStateCache
    {
        private static readonly Dictionary<string, PalCatalogState> states = new();
        private static readonly object statesLock = new();

        public static PalCatalogState GetState(string saveId)
        {
            if (string.IsNullOrEmpty(saveId)) return new PalCatalogState();

            lock (statesLock)
            {
                if (!states.TryGetValue(saveId, out var state))
                {
                    state = new PalCatalogState();
                    states[saveId] = state;
                }

                return state;
            }
        }
    }
}
