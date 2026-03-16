using System.Text;

namespace FortniteForge.Core.Services.VerseGeneration;

/// <summary>
/// Fluent builder for Verse source code. Handles indentation-sensitive syntax.
/// Verse uses significant whitespace (4 spaces per level).
/// </summary>
public class VerseCodeBuilder
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private const int IndentSize = 4;

    private string Prefix => new(' ', _indent * IndentSize);

    public VerseCodeBuilder Line(string line = "")
    {
        if (string.IsNullOrEmpty(line))
            _sb.AppendLine();
        else
            _sb.AppendLine(Prefix + line);
        return this;
    }

    public VerseCodeBuilder Indent() { _indent++; return this; }
    public VerseCodeBuilder Dedent() { _indent = Math.Max(0, _indent - 1); return this; }

    public VerseCodeBuilder Comment(string text) => Line($"# {text}");

    public VerseCodeBuilder SectionComment(string text)
    {
        Line();
        Comment(new string('=', 60));
        Comment($" {text}");
        Comment(new string('=', 60));
        return this;
    }

    public VerseCodeBuilder Using(string module) => Line($"using {{ {module} }}");

    /// <summary>Standard UEFN using block for Creative devices.</summary>
    public VerseCodeBuilder StandardUsings()
    {
        Using("/Fortnite.com/Devices");
        Using("/Verse.org/Simulation");
        Using("/UnrealEngine.com/Temporary/SpatialMath");
        return this;
    }

    /// <summary>UI-specific usings on top of standard.</summary>
    public VerseCodeBuilder UIUsings()
    {
        StandardUsings();
        Using("/Fortnite.com/UI");
        Using("/UnrealEngine.com/Temporary/UI");
        Using("/Verse.org/Colors");
        Using("/Verse.org/Random");
        return this;
    }

    /// <summary>Game-logic usings (players, teams, etc.).</summary>
    public VerseCodeBuilder GameplayUsings()
    {
        StandardUsings();
        Using("/Fortnite.com/Characters");
        Using("/Fortnite.com/Game");
        Using("/Fortnite.com/Playspaces");
        Using("/Fortnite.com/Teams");
        return this;
    }

    public VerseCodeBuilder ClassDef(string name, string parent = "creative_device")
    {
        Line($"{name} := class({parent}):");
        return Indent();
    }

    public VerseCodeBuilder Editable(string name, string type, string defaultValue)
    {
        Line("@editable");
        return Line($"{name} : {type} = {defaultValue}");
    }

    public VerseCodeBuilder Var(string name, string type, string defaultValue)
        => Line($"var {name} : {type} = {defaultValue}");

    public VerseCodeBuilder ImmutableVar(string name, string type, string defaultValue)
        => Line($"{name} : {type} = {defaultValue}");

    public VerseCodeBuilder OnBegin()
    {
        Line();
        Line("OnBegin<override>()<suspends>:void=");
        return Indent();
    }

    public VerseCodeBuilder Method(string name, string returnType, bool suspends = false, bool overrides = false)
    {
        var mods = "";
        if (overrides) mods += "<override>";
        if (suspends) mods += "<suspends>";
        Line($"{name}(){mods}:{returnType}=");
        return Indent();
    }

    public VerseCodeBuilder MethodWithParams(string name, string parameters, string returnType, bool suspends = false)
    {
        var susp = suspends ? "<suspends>" : "";
        Line($"{name}({parameters}){susp}:{returnType}=");
        return Indent();
    }

    public VerseCodeBuilder EndBlock() => Dedent();

    public VerseCodeBuilder Raw(string text)
    {
        _sb.Append(text);
        return this;
    }

    /// <summary>
    /// Adds a block with proper indentation. The action receives the builder for writing within the block.
    /// </summary>
    public VerseCodeBuilder Block(string header, Action<VerseCodeBuilder> body)
    {
        Line(header);
        Indent();
        body(this);
        Dedent();
        return this;
    }

    public override string ToString() => _sb.ToString();

    public int Length => _sb.Length;
}
