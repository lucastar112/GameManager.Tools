namespace GameManager.Tools.TrackFormats;

/// <summary>Wii model-space position (Y is vertical), in source units.</summary>
public readonly record struct TrackPoint3(double X, double Y, double Z);

/// <summary>Texture coordinate; retains values outside [0,1] for GX wrap modes.</summary>
public readonly record struct TrackUv(double X, double Y);

/// <summary>RGBA color decoded from MDL0 vertex color arrays.</summary>
public readonly record struct TrackColor(byte A, byte R, byte G, byte B)
{
    public static TrackColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
}

/// <summary>Decoded triangles in one shared position buffer; no GPU or UI resources.</summary>
public sealed class TrackMesh
{
    public List<TrackPoint3> Positions { get; } = [];
    public List<int> TriangleIndices { get; } = [];
    public int TriangleCount => TriangleIndices.Count / 3;
}
