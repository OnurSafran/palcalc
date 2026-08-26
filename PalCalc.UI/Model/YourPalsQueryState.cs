namespace PalCalc.UI.Model
{
    // Query state is intentionally not part of YourPalsDocument. It belongs to the
    // active save session and is safe to discard without changing saved membership.
    internal sealed class YourPalsQueryState
    {
        public string SearchText { get; set; } = "";
        public YourPalsStatusFilter StatusFilter { get; set; } = YourPalsStatusFilter.All;
        public string SelectedGroupId { get; set; }
        public YourPalsSortField SortField { get; set; } = YourPalsSortField.PalName;
        public bool IsSortAscending { get; set; } = true;
    }

    internal enum YourPalsStatusFilter
    {
        All,
        Resolved,
        Unresolved,
        Stale,
        Conflict,
        Invalid,
    }

    internal enum YourPalsSortField
    {
        Group,
        PalName,
        Status,
        Source,
        Instance,
        Location,
        Key,
    }
}
