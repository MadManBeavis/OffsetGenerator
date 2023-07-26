namespace OffsetGenerator.App.Models;

public class ConsistencyInfo
{
    public string? Version { get; set; }

    public List<ConsistencyInfoEntry>? Entries { get; set; }
}
