using System.IO;

namespace GameManager.Tools.TrackFormats;

/// <summary>One course SZS archive. All fields are usable without a UI or graphics context.</summary>
public sealed record CourseData(
    KmpDocument Kmp,
    KmpCheckpointGraph Checkpoints,
    BrresModelReader.DecodeResult? Model,
    LexDocument? Lex,
    IReadOnlyCollection<string> ArchiveFiles)
{
    /// <summary>Optional collision data; null when the archive has no course.kcl.</summary>
    public KclDocument? Kcl { get; init; }
}

public static class CourseDataLoader
{
    /// <summary>Open a track SZS/Yaz0 or U8 archive and decode its course files.</summary>
    public static CourseData Open(byte[] szsBytes, BrresImportMode brresMode = BrresImportMode.GeometryOnly)
    {
        ArgumentNullException.ThrowIfNull(szsBytes);
        SzsArchiveReader archive = SzsArchiveReader.Open(szsBytes);
        KmpDocument kmp = KmpFileReader.Read(archive.ReadFile("course.kmp"));
        KmpCheckpointGraph checkpoints = KmpCheckpointDecoder.Decode(kmp);
        LexDocument? lex = archive.ContainsFile("course.lex")
            ? LexFileReader.Read(archive.ReadFile("course.lex")) : null;
        BrresModelReader.DecodeResult? model = archive.ContainsFile("course_model.brres")
            ? BrresModelReader.Decode(archive.ReadFile("course_model.brres"), brresMode) : null;
        KclDocument? kcl = archive.ContainsFile("course.kcl")
            ? KclFileReader.Read(archive.ReadFile("course.kcl")) : null;
        return new CourseData(kmp, checkpoints, model, lex, archive.FileNames) { Kcl = kcl };
    }

    public static CourseData OpenFile(string path, BrresImportMode brresMode = BrresImportMode.GeometryOnly)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        return Open(File.ReadAllBytes(path), brresMode);
    }
}
