namespace RiveTT.Tools.Utilities;

public static class RoomInclusion
{
    public static bool Include(bool placed, double area, bool includeUnplaced, bool includeNotEnclosed) =>
        placed ? area > 0 || includeNotEnclosed : includeUnplaced;
}
