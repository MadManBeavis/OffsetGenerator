using OffsetGenerator.App.Definitions;
using OffsetGenerator.App.Models;
using OffsetGenerator.App.Templates;
using OffsetGenerator.Common;
using OffsetGenerator.Common.Helpers;
using Unispect;

namespace OffsetGenerator.App;

public static partial class Program
{
    private const string ProcessName = "EscapeFromTarkov";
    private const string OutputDirectory = @"..\..\..\Output";
    private const string InstallPathName = ProcessName;

    private static readonly Logger Logger = new Logger();
    private static readonly Inspector Inspector = new Inspector();

    /// <summary>
    /// Entry point
    /// </summary>
    public static async Task Main(string[] args)
    {
        try
        {
            Logger.Log(LogType.Info, $"Written by MadManBeavis");

            EnsureOutputDirectoryExists();

            var consistencyInfo = GetConsistencyInfo();

            // Get running game processes
            var runningProcesses = Process.GetProcessesByName(ProcessName);

            switch (runningProcesses.Length)
            {
                // If the game is not running
                case 0:
                    Logger.Log(LogType.Info, $"Game is not running, starting it...");

                    // This will not work properly. Consider starting the game before running the generator or fix this part.
                    throw new NotImplementedException("This app cannot run the game, please run it before starting this app...");
                case 1:
                {
                    var runningProcess = runningProcesses.First();

                    Logger.Log(LogType.Info, $"Game is already running (pid: {runningProcess.Id})");
                    break;
                }
                default:
                    throw new Exception($"Unexpected running process count {runningProcesses.Length}");
            }

            // Dump types
            Inspector.DumpTypes(Path.Combine(OutputDirectory, "dump.txt"), typeof(BasicMemory), processHandle: ProcessName);

            // And finally generate offsets
            GenerateOffsets();

            // Generate output file
            GenerateOutputFile(consistencyInfo);

            // OK
            Logger.Log(LogType.Success, $"Done");
        }
        catch (Exception ex)
        {
            Logger.Log(LogType.Error, $"An exception occured: {ex.Message}");
        }
        finally
        {
            Console.ReadKey();
        }   
    }

    private static ConsistencyInfo GetConsistencyInfo(string? gameDirectory = null)
    {
        gameDirectory ??= Tools.GetInstallationPath(InstallPathName);

        if (!Directory.Exists(gameDirectory))
            throw new Exception($"Game directory doesn't exist '{gameDirectory}'");
        
        var consistencyInfoFilePath = Path.Combine(Tools.GetInstallationPath(InstallPathName), "ConsistencyInfo");
        if (!File.Exists(consistencyInfoFilePath))
            throw new Exception($"Unable to find consistency info file does not exist '{consistencyInfoFilePath}'");

        var fileContent = File.ReadAllText(consistencyInfoFilePath);
        if (string.IsNullOrEmpty(fileContent))
            throw new Exception($"Invalid consistency info content");

        var consistencyInfo = JsonConvert.DeserializeObject<ConsistencyInfo?>(fileContent);
        return consistencyInfo ?? throw new Exception("Consistency Info couldn't be deserialized");
    }

    /// <summary>
    /// Start generating offsets
    /// </summary>
    private static void GenerateOffsets()
    {
        foreach (var namespaceDefinition in Definitions)
            IterateNamespaceDefinition(namespaceDefinition);
    }

    private static List<NamespaceDefinition> GetAllNamespaceDefinitionsRecursive(NamespaceDefinition parentNamespace)
    {
        var result = new List<NamespaceDefinition>();
        result.Add(parentNamespace);

        foreach (var childNamespace in parentNamespace.NamespaceDefinitions)
        {
            result.AddRange(GetAllNamespaceDefinitionsRecursive(childNamespace));
        }

        return result;
    }

    private static List<NamespaceDefinition> GetAllNamespaceDefinitions()
    {
        var result = new List<NamespaceDefinition>();

        foreach (var namespaceDefinition in Definitions)
            result.AddRange(GetAllNamespaceDefinitionsRecursive(namespaceDefinition));

        return result;
    }

    private static TypeDefWrapper? LookupTypeFromPreviousFoundType(NamespaceDefinition parentNamespace)
    {
        Logger.Log(LogType.Info, $"Finding type for {parentNamespace.DumpNamespaceName} from a previous found type {parentNamespace.PreviousFoundField}");

        var parts = parentNamespace.PreviousFoundField.Split("::");
        var @namespace = parts[0];
        var field = parts[1];


        var allNamespaces = GetAllNamespaceDefinitions();

        //var x = _definitions.Where(def => def.DumpNamespaceName.Equals(_namespace));

        var x = allNamespaces.Where(def => def.DumpNamespaceName.Equals(@namespace));

        var b = x.First().OffsetDefinitions.ToList();

        var y = b.Where(off => off.FoundField != null && off.OffsetName.Equals(field)).First();

        var _field = y.FoundField;

        var dick = Inspector.TypeDefinitions.Where(c => c.FullName == _field.FieldType).First();

        // In case the type is an interface with 0 fields, find a class which implements it
        if (dick.ClassType == "Interface" && dick.Fields.Count == 0)
        {
            var test = Inspector.TypeDefinitions.Where(c => c.ClassType == "Class" && c.Interfaces.Any(b => b.Name == dick.Name)).First();

            return test;
        }

        return dick;
    }

    private static TypeDefWrapper? FindTypeFromParentNamespaceDefinition(NamespaceDefinition parentNamespace)
    {
        // If the type uses a previous found one
        if (parentNamespace.PreviousFoundField != null)
            return LookupTypeFromPreviousFoundType(parentNamespace);

        // Get all classes
        var types = Inspector.TypeDefinitions.Where(c => c.ClassType.Equals("Class")).ToList();

        // Filter by namespace name
        if (parentNamespace.NamespaceName != null)
            types = types.Where(c => c.Namespace.Equals(parentNamespace.NamespaceName)).ToList();

        // Filter by class name
        if (parentNamespace.ClassName != null)
            types = types.Where(c => c.Name.Equals(parentNamespace.ClassName)).ToList();

        // Get the found class
        var foundClass = types.FirstOrDefault();

        return foundClass;
    }

    private static FieldDefWrapper? FindFieldFromOffsetDefinition(TypeDefWrapper type, OffsetDefinition offsetDefinition)
    {
        // Now find the field
        var fields = type.Fields.ToList();

        // Type name
        if (offsetDefinition.IsUnknownType)
            fields = fields.Where(c => c.FieldType.StartsWith("-.GClass")).ToList();
        else if (offsetDefinition.TypeName != null)
            fields = fields.Where(c => c.FieldType.Equals(offsetDefinition.TypeName)).ToList();

        // Field name
        if (offsetDefinition.IsUnknownFieldName)
            fields = fields.Where(c => c.Name.StartsWith("gClass")).ToList();
        else if (offsetDefinition.FieldName != null)
            fields = fields.Where(c => c.Name.Equals(offsetDefinition.FieldName)).ToList();

        FieldDefWrapper? foundField = null;

        if (offsetDefinition.FieldFilterExpression != null)
            foundField = offsetDefinition.FieldFilterExpression(fields);
        else
            foundField = fields.First();

        return foundField;
    }

    /// <summary>
    /// Handles an offset definition
    /// </summary>
    /// <param name="parentNamespace">The parent namespace</param>
    /// <param name="offsetDefinition">The child offset definition of the given parent namespace</param>
    private static void HandleOffsetDefinition(NamespaceDefinition parentNamespace, OffsetDefinition offsetDefinition)
    {
        // Get the found class
        var type = FindTypeFromParentNamespaceDefinition(parentNamespace);

        // Now find the field
        var field = FindFieldFromOffsetDefinition(type, offsetDefinition);

        // Make sure field was found
        if (field == null)
        {
            Logger.Log(LogType.Error, $"{parentNamespace.DumpNamespaceName}::{offsetDefinition.OffsetName} was not found");
            return;
        }

        // Set found field
        //offsetDefinition.FoundField = new FieldInfo(type, field);
        parentNamespace.FoundType = type;
        offsetDefinition.FoundField = field;

        // Log
        Logger.Log(LogType.Success, $"{parentNamespace.DumpNamespaceName}::{offsetDefinition.OffsetName} = 0x{field.Offset:X};");
    }

    /// <summary>
    /// Handles a namespace definition
    /// </summary>
    /// <param name="namespaceDefinition">The namespace definition to handle</param>
    private static void IterateNamespaceDefinition(NamespaceDefinition namespaceDefinition)
    {
        foreach (var offsetDefinition in namespaceDefinition.OffsetDefinitions)
            HandleOffsetDefinition(namespaceDefinition, offsetDefinition);
        
        foreach (var ns in namespaceDefinition.NamespaceDefinitions)
            IterateNamespaceDefinition(ns);
    }

    /// <summary>
    /// Generates the output file
    /// </summary>
    private static void GenerateOutputFile(ConsistencyInfo consistencyInfo)
    {
        var template = new CppTemplate();

        template.AddInstruction("#pragma once", 0);
        template.AddInstruction("#include \"stdafx.h\"", 0);
        template.SkipLine();

        template.AddHeader(consistencyInfo.Version);
        template.SkipLine();

        template.AddInstruction("namespace offsets", 0);
        template.AddInstruction("{", 0);

        foreach (var namespaceDefinition in Definitions)
        {
            template.HandleNamespace(namespaceDefinition, 1, namespaceDefinition == Definitions.Last());
        }

        template.AddInstruction("}", 0);
        template.Build(Path.Combine(OutputDirectory, "offsets.h"));
    }

    private static void EnsureOutputDirectoryExists()
    {
        if (!Directory.Exists(OutputDirectory))
            Directory.CreateDirectory(OutputDirectory);
    }
}