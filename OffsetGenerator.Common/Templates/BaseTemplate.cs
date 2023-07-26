using System.Text;
using OffsetGenerator.Common.Helpers;

namespace OffsetGenerator.Common.Templates;

public class BaseTemplate
{
    protected const char Space = '	';
    protected readonly StringBuilder Sb = new();
    protected static readonly Logger Logger = new Logger();

    public BaseTemplate() { }

    public void AddInstruction(string instruction, int intend = 2)
        => Sb.AppendLine($"{new string(Space, intend)}{instruction}");

    public void AddHeader(string? gameVersion)
    {
        AddInstruction("/*", 0);
        AddInstruction($"Generated using Beavis' Gen", 1);
        AddInstruction($"At {DateTime.Now}", 1);
        AddInstruction(string.IsNullOrEmpty(gameVersion) ? "Unknown game version" : $"Game version {gameVersion}", 1);
        AddInstruction("*/", 0);
    }

    public void Build(string path)
        => File.WriteAllText(path, Build());

    public string Build()
        => Sb.ToString();

    public void SkipLine()
        => Sb.AppendLine();
}
