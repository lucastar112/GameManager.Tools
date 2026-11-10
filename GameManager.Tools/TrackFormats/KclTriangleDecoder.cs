// Uses the shared GameManager.Tools.TrackFormats.Vector3 value type.

namespace GameManager.Tools.TrackFormats;

/// <summary>Reconstructed triangle geometry for future KCL display; no graphics API dependency.</summary>
public sealed record KclTriangle(int PrismId, Vector3 A, Vector3 B, Vector3 C,
    Vector3 FaceNormal, KclCollisionFlag Collision);
public sealed record KclTriangleDecodeResult(IReadOnlyList<KclTriangle> Triangles,
    IReadOnlyList<int> DegeneratePrismIds);

public static class KclTriangleDecoder
{
    /// <summary>Converts triangular prisms to 3 vertices, retaining the original 1-based prism ID.</summary>
    public static KclTriangleDecodeResult Decode(KclDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var triangles = new List<KclTriangle>(document.Prisms.Count);
        var invalid = new List<int>();
        foreach (KclPrism prism in document.Prisms)
        {
            Vector3 a = document.Positions[prism.PositionIndex];
            Vector3 face = document.Normals[prism.FaceNormalIndex];
            Vector3 n1 = document.Normals[prism.EdgeNormal1Index];
            Vector3 n2 = document.Normals[prism.EdgeNormal2Index];
            Vector3 n3 = document.Normals[prism.EdgeNormal3Index];
            // Mario Kart Wii's height-and-edge-normal prism reconstruction.
            Vector3 crossA = Cross(n1, face);
            Vector3 crossB = Cross(n2, face);
            double divisorA = Dot(crossA, n3);
            double divisorB = Dot(crossB, n3);
            if (Math.Abs(divisorA) < 1e-10 || Math.Abs(divisorB) < 1e-10)
            {
                invalid.Add(prism.Id);
                continue;
            }
            Vector3 b = AddScaled(a, crossB, (float)(prism.Height / divisorB));
            Vector3 c = AddScaled(a, crossA, (float)(prism.Height / divisorA));
            if (!Finite(b) || !Finite(c)) { invalid.Add(prism.Id); continue; }
            triangles.Add(new KclTriangle(prism.Id, a, b, c, face, prism.Collision));
        }
        return new KclTriangleDecodeResult(triangles, invalid);
    }
    private static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    private static double Dot(Vector3 a, Vector3 b) =>
        (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;

    private static Vector3 AddScaled(Vector3 a, Vector3 b, float scale) => new(
        a.X + b.X * scale,
        a.Y + b.Y * scale,
        a.Z + b.Z * scale);

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
