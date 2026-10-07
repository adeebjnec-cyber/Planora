using System.Xml.Linq;
using Mpxj = MPXJ.Net;
using Planora.Engine.Model;

namespace Planora.Engine.Interop;

/// <summary>
/// Other project formats through the open-source MPXJ library: Microsoft Project .mpp files (all
/// versions MPXJ reads) and many other schedulers' files (Primavera P6, Asta, Phoenix, GanttProject,
/// MPX, ...). Files are read by MPXJ, converted to Microsoft Project XML, and imported by
/// <see cref="MspdiFile"/>, so every format shares one tested importer. Export goes the other way,
/// from Microsoft Project XML to the chosen format. (MPXJ cannot write .mpp; Microsoft Project opens
/// the XML format directly.)
/// </summary>
public static class ForeignFormats
{
    /// <summary>File extensions MPXJ can read, for open-file dialogs.</summary>
    public static readonly IReadOnlyList<(string Name, string Pattern)> ReadableFormats = new[]
    {
        ("Microsoft Project files", "*.mpp;*.mpt"),
        ("Primavera P6 files", "*.xer;*.pmxml"),
        ("MPX files", "*.mpx"),
        ("Other schedulers (Asta, Phoenix, GanttProject, ...)", "*.pp;*.ppx;*.gan;*.sdef;*.fts;*.pod;*.cdpx;*.schedule_grid"),
    };

    public enum ExportFormat
    {
        Mpx,
        PrimaveraXml,
        PrimaveraXer,
        Planner,
        Sdef,
    }

    /// <summary>Reads any file MPXJ understands (e.g. .mpp) into a Planora project.</summary>
    public static Project Load(string path) => Load(path, null);

    /// <summary>Loads a file through MPXJ; import problems are added to <paramref name="warnings"/>.</summary>
    public static Project Load(string path, ICollection<string>? warnings)
    {
        Mpxj.ProjectFile? file;
        try
        {
            // Read from our own stream so the file is always closed, even when MPXJ cannot read it.
            using var stream = File.OpenRead(path);
            file = new Mpxj.UniversalProjectReader().Read(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException($"The file could not be read: {ex.Message}", ex);
        }
        if (file is null)
            throw new InvalidDataException("This file is not a project format Planora can read.");

        using var xml = new MemoryStream();
        new Mpxj.MSPDIWriter { MicrosoftProjectCompatibleOutput = true }.Write(file, xml);
        xml.Position = 0;
        var project = MspdiFile.Read(XDocument.Load(xml), warnings);
        if (string.IsNullOrWhiteSpace(project.Name) || project.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            project.Name = Path.GetFileNameWithoutExtension(path);
        return project;
    }

    /// <summary>Writes the project in another scheduler's format.</summary>
    public static void Save(Project project, string path, ExportFormat format)
    {
        using var xml = new MemoryStream();
        MspdiFile.Write(project).Save(xml);
        xml.Position = 0;
        var file = new Mpxj.UniversalProjectReader().Read(xml)
            ?? throw new InvalidOperationException("The project could not be converted.");
        var target = format switch
        {
            ExportFormat.Mpx => Mpxj.FileFormat.MPX,
            ExportFormat.PrimaveraXml => Mpxj.FileFormat.PMXML,
            ExportFormat.PrimaveraXer => Mpxj.FileFormat.XER,
            ExportFormat.Planner => Mpxj.FileFormat.PLANNER,
            _ => Mpxj.FileFormat.SDEF,
        };
        new Mpxj.UniversalProjectWriter(target).Write(file, path);
    }
}
