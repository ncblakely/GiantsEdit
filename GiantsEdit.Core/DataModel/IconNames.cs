namespace GiantsEdit.Core.DataModel;

/// <summary>
/// Lookup table mapping minishop icon IDs to human-readable names.
/// Sourced from Weapon_Ops.bld icon definitions.
/// </summary>
public static class IconNames
{
    private static readonly Dictionary<int, string> IdToName = new()
    {
        [-1] = "Null",
        [0] = "Syringe",
        [1] = "Gun",
        [2] = "Gun R1",
        [3] = "Shotgun",
        [4] = "Laser",
        [5] = "Machinegun",
        [6] = "Sniper Gun",
        [7] = "Mortar",
        [8] = "Grenade",
        [9] = "Mine",
        [10] = "Popup Bomb",
        [11] = "Turret (Field)",
        [12] = "Turret (Stone)",
        [13] = "Motion Bomb",
        [14] = "Missile",
        [15] = "Homing Missile",
        [16] = "Proximity Missile",
        [17] = "Personal Shield",
        [18] = "Jetpack Upgrade",
        [19] = "Repair Wand",
        [20] = "Bush",
        [21] = "Smartie Grab",
        [22] = "Cluster",
        [23] = "Cloak",
        [24] = "Teleport",
        [25] = "Firewall",
        [26] = "Shadow",
        [27] = "Hail",
        [28] = "Time",
        [29] = "Shrinker",
        [30] = "Follow",
        [31] = "Fire Circle",
        [32] = "Sea Monster",
        [33] = "Tornado",
        [34] = "Empty",
        [35] = "Selected",
        [36] = "Instant Health",
        [37] = "Shotgun Shells",
        [38] = "Bullet Clip",
        [39] = "Sniper Clip",
        [40] = "Missile Ammo",
        [41] = "Homing Ammo",
        [42] = "Proximity Ammo",
        [43] = "Mortar Ammo",
        [44] = "Bow",
        [45] = "Bow RPG",
        [46] = "Bow Sniper",
        [47] = "Bow Homing",
        [48] = "Bow Powerup",
        [49] = "Mecc Tower",
        [50] = "Mecc Shop",
        [51] = "Mecc Vehicle Launch",
        [52] = "Mecc Teleport",
        [53] = "Mecc Turret",
        [54] = "Mecc SAM",
        [55] = "Bow RPG Ammo",
        [56] = "Bow Homing Ammo",
        [57] = "Bow Sniper Ammo",
        [58] = "Bow Powerup Ammo",
        [59] = "Reaper Syringe",
        [60] = "Boat Homing Ammo",
        [61] = "Boat Missile Ammo",
        [62] = "Boat Turbo Ammo",
        [63] = "Reaper Mine",
        [64] = "Flare",
        [65] = "Reaper Tower",
        [66] = "Reaper Shop",
        [67] = "Reaper Vehicle Launch",
        [68] = "Reaper Teleport",
        [69] = "Reaper Turret",
        [70] = "Reaper SAM",
        [71] = "Reaper Special",
        [72] = "Reaper Flare",
        [73] = "Reaper Repair",
        [74] = "Sword",
        [75] = "Incendiary Grenade",
        [76] = "EMP Grenade",
        [77] = "Ammo Pack",
    };

    private static readonly Dictionary<string, int> NameToId;

    static IconNames()
    {
        NameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in IdToName)
            NameToId[kvp.Value] = kvp.Key;
    }

    /// <summary>Returns the friendly name for an icon ID, or the ID as a string if unknown.</summary>
    public static string GetName(int id) =>
        IdToName.TryGetValue(id, out var name) ? name : id.ToString();

    /// <summary>Returns "Name (ID)" display format.</summary>
    public static string GetDisplayName(int id) =>
        IdToName.TryGetValue(id, out var name) ? $"{name} ({id})" : id.ToString();

    /// <summary>
    /// Parses an icon input string. Accepts: ID number, friendly name, or "Name (ID)" format.
    /// Returns the icon ID or null if not recognized.
    /// </summary>
    public static int? ParseInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        // Try "Name (ID)" format
        int paren = input.LastIndexOf('(');
        if (paren > 0 && input.EndsWith(')'))
        {
            var idStr = input[(paren + 1)..^1].Trim();
            if (int.TryParse(idStr, out int id)) return id;
        }

        // Try plain number
        if (int.TryParse(input, out int plainId)) return plainId;

        // Try name lookup
        if (NameToId.TryGetValue(input, out int namedId)) return namedId;

        return null;
    }

    /// <summary>All known icon entries as (ID, Name) pairs, sorted by ID.</summary>
    public static IReadOnlyList<(int Id, string Name)> All { get; } =
        IdToName
            .Where(kvp => kvp.Key >= 0) // exclude Null (-1)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

    /// <summary>Maximum known icon ID.</summary>
    public const int MaxIconId = 77;

    // Icon IDs valid for Mecc minishops
    private static readonly HashSet<int> MeccIconIds =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
        36, 37, 38, 39, 40, 41, 42, 43,
        49, 50, 51, 52, 53, 54,
        64, 75, 76, 77,
    ];

    // Icon IDs valid for Reaper minishops
    private static readonly HashSet<int> ReaperIconIds =
    [
        21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33,
        36,
        44, 45, 46, 47, 48,
        55, 56, 57, 58, 59, 60, 61, 62, 63,
        65, 66, 67, 68, 69, 70, 71, 72, 73, 74,
    ];

    /// <summary>Icon entries valid for Mecc minishops.</summary>
    public static IReadOnlyList<(int Id, string Name)> MeccIcons { get; } =
        All.Where(e => MeccIconIds.Contains(e.Id)).ToList();

    /// <summary>Icon entries valid for Reaper minishops.</summary>
    public static IReadOnlyList<(int Id, string Name)> ReaperIcons { get; } =
        All.Where(e => ReaperIconIds.Contains(e.Id)).ToList();
}
