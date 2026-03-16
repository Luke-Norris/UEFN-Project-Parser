namespace FortniteForge.Core.Models;

public enum VerseCategory
{
    GameManagement,
    PlayerEconomy,
    UISystem,
    AbilitySystem,
    TycoonBuilding,
    RankedProgression,
    Environment,
    Utility,
    Combat,
    Movement
}

/// <summary>
/// Parsed metadata for a single .verse file in the reference library.
/// </summary>
public class VerseFileIndex
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int LineCount { get; set; }

    public List<string> UsingStatements { get; set; } = new();
    public List<VerseClassInfo> Classes { get; set; } = new();
    public List<string> DeviceTypesUsed { get; set; } = new();
    public List<string> EditableProperties { get; set; } = new();
    public List<string> FunctionNames { get; set; } = new();
    public List<VerseCategory> Categories { get; set; } = new();

    public string FullText { get; set; } = "";
}

public class VerseClassInfo
{
    public string ClassName { get; set; } = "";
    public string ParentClass { get; set; } = "";
    public List<string> Modifiers { get; set; } = new();
    public List<string> PropertyNames { get; set; } = new();
    public List<string> FunctionNames { get; set; } = new();
}

public class VerseSearchResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public List<string> MatchReasons { get; set; } = new();
    public List<VerseCategory> Categories { get; set; } = new();
    public List<string> DeviceTypesUsed { get; set; } = new();
    public List<string> ClassNames { get; set; } = new();
    public int Relevance { get; set; }
}

public class VerseLibraryStats
{
    public int TotalFiles { get; set; }
    public int TotalLines { get; set; }
    public long TotalSizeBytes { get; set; }
    public Dictionary<string, int> FilesByCategory { get; set; } = new();
    public Dictionary<string, int> DeviceUsageCounts { get; set; } = new();
    public List<string> TopUsingStatements { get; set; } = new();
    public DateTime IndexedAt { get; set; }
}
