using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace GameManager.Tools.TrackFormats;

/// <summary>Mario Kart Wii KCL collision attribute. The complete 16-bit value is preserved.</summary>
public readonly record struct KclCollisionFlag(ushort Raw)
{
    public int Type => Raw & 0x1f;
    public int Variant => (Raw >> 5) & 7;
    public int BlightIndex => (Raw >> 8) & 7;
    public int WheelDepth => (Raw >> 11) & 3;
    public int Effects => (Raw >> 13) & 7;
    public bool TrickableBit => (Effects & 1) != 0;
    public bool RejectRoadBit => (Effects & 2) != 0;
    public bool SoftWallBit => (Effects & 4) != 0;
    public override string ToString() => $"0x{Raw:X4} (type=0x{Type:X2}, variant={Variant})";
}

public sealed record KclHeader(
    int PositionOffset, int NormalOffset, int PrismOffset, int OctreeOffset,
    float PrismThickness, Vector3 AreaMinimum,
    uint XMask, uint YMask, uint ZMask,
    uint BlockWidthShift, uint YBlocksShift, uint XYBlocksShift,
    float? SphereRadius);

/// <summary>One 0x10-byte prism; Id is 1-based, matching KCL octree leaf references.</summary>
public sealed record KclPrism(
    int Id, float Height, ushort PositionIndex, ushort FaceNormalIndex,
    ushort EdgeNormal1Index, ushort EdgeNormal2Index, ushort EdgeNormal3Index,
    KclCollisionFlag Collision);

/// <summary>Preserved octree bytes plus a validated reference inventory. Spatial queries come later.</summary>
public sealed record KclOctreeIndex(
    byte[] RawData, int RootEntryCount, int BranchTables, int LeafLists,
    IReadOnlyList<int> ReferencedPrismIds, IReadOnlyList<int> InvalidReferencedPrismIds);

/// <summary>Portable, graphics-independent KCL data. Normals and prism indices are never discarded.</summary>
public sealed record KclDocument(
    KclHeader Header,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<KclPrism> Prisms,
    KclOctreeIndex Octree,
    IReadOnlyList<string> Warnings)
{
    public int PrismCount => Prisms.Count;
    public IReadOnlyDictionary<ushort, int> CollisionFlagCounts => Prisms
        .GroupBy(p => p.Collision.Raw).ToDictionary(g => g.Key, g => g.Count());
    public IReadOnlyDictionary<int, int> CollisionTypeCounts => Prisms
        .GroupBy(p => p.Collision.Type).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Read-only big-endian Mario Kart Wii KCL decoder. No WPF, OpenGL, or mesh generation.
/// Both the 0x38 and 0x3C header variants are accepted.
/// </summary>
public static class KclFileReader
{
    public static KclDocument ReadFile(string path) => Read(File.ReadAllBytes(path));

    public static KclDocument Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Read(bytes.AsSpan());
    }

    public static KclDocument Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 0x38)
            throw new InvalidDataException("KCL header is smaller than 0x38 bytes.");

        int pos = CheckedOffset(U32(bytes, 0), bytes.Length, "positions");
        int norm = CheckedOffset(U32(bytes, 4), bytes.Length, "normals");
        uint prismBase = U32(bytes, 8);
        if (prismBase > int.MaxValue - 0x10)
            throw new InvalidDataException("KCL prism offset overflows.");
        int prism = CheckedOffset(prismBase + 0x10, bytes.Length, "prisms (1-based offset)");
        int octree = CheckedOffset(U32(bytes, 12), bytes.Length, "octree");
        int[] sections = [pos, norm, prism, octree];
        if (sections.Distinct().Count() != 4 || sections.Any(s => s < 0x38))
            throw new InvalidDataException("KCL section offsets overlap or point into the header.");

        float thickness = F32(bytes, 0x10);
        Vector3 areaMinimum = Vec3(bytes, 0x14);
        uint xMask = U32(bytes, 0x20), yMask = U32(bytes, 0x24), zMask = U32(bytes, 0x28);
        uint shift = U32(bytes, 0x2c), yShift = U32(bytes, 0x30), xyShift = U32(bytes, 0x34);
        if (!float.IsFinite(thickness) || !Finite(areaMinimum) || shift >= 32 || yShift >= 32 || xyShift >= 32)
            throw new InvalidDataException("KCL spatial header has invalid values.");

        float? radius = sections.Min() >= 0x3c ? F32(bytes, 0x38) : null;
        if (radius is not null && (!float.IsFinite(radius.Value) || radius.Value < 0))
            throw new InvalidDataException("KCL sphere radius is invalid.");

        var header = new KclHeader(pos, norm, prism, octree, thickness, areaMinimum,
            xMask, yMask, zMask, shift, yShift, xyShift, radius);
        var warnings = new List<string>();
        int positionEnd = NextSectionEnd(pos, sections, bytes.Length);
        int normalEnd = NextSectionEnd(norm, sections, bytes.Length);
        int prismEnd = NextSectionEnd(prism, sections, bytes.Length);
        int octreeEnd = NextSectionEnd(octree, sections, bytes.Length);

        Vector3[] positions = ReadVectors(bytes.Slice(pos, positionEnd - pos), "positions", warnings);
        Vector3[] normals = ReadVectors(bytes.Slice(norm, normalEnd - norm), "normals", warnings);
        int prismLength = prismEnd - prism;
        if (prismLength % 0x10 != 0)
            warnings.Add($"KCL prism section has {prismLength % 0x10} trailing alignment bytes.");
        var prisms = new KclPrism[prismLength / 0x10];
        for (int i = 0; i < prisms.Length; i++)
        {
            int p = prism + i * 0x10;
            float height = F32(bytes, p);
            if (!float.IsFinite(height))
                throw new InvalidDataException($"KCL prism {i + 1} has a non-finite height.");
            ushort point = U16(bytes, p + 4), face = U16(bytes, p + 6);
            ushort edge1 = U16(bytes, p + 8), edge2 = U16(bytes, p + 10), edge3 = U16(bytes, p + 12);
            // Index bounds are checked against the original section arrays, not against a mesh.
            if (point >= positions.Length || face >= normals.Length || edge1 >= normals.Length ||
                edge2 >= normals.Length || edge3 >= normals.Length)
                throw new InvalidDataException($"KCL prism {i + 1} references an out-of-range vector.");
            prisms[i] = new KclPrism(i + 1, height, point, face, edge1, edge2, edge3,
                new KclCollisionFlag(U16(bytes, p + 14)));
        }

        KclOctreeIndex tree = ParseOctree(bytes.Slice(octree, octreeEnd - octree), header, prisms.Length, warnings);
        return new KclDocument(header, positions, normals, prisms, tree, warnings);
    }

    private static KclOctreeIndex ParseOctree(ReadOnlySpan<byte> section, KclHeader h, int prismCount, List<string> warnings)
    {
        byte[] raw = section.ToArray();
        // Root table has ceil(width / (1 << blockWidthShift)) entries for each axis.
        ulong roots = 1;
        foreach (uint mask in new[] { h.XMask, h.YMask, h.ZMask })
        {
            ulong count = ((ulong)~mask >> (int)h.BlockWidthShift) + 1;
            ulong capacity = (ulong)raw.Length / 4;
            if (count == 0 || count > capacity || roots > capacity / count)
                throw new InvalidDataException("KCL root octree dimensions exceed its byte range.");
            roots *= count;
        }
        if (roots == 0 || roots > int.MaxValue || roots * 4 > (ulong)raw.Length)
            throw new InvalidDataException("KCL root octree table is truncated.");

        var visited = new HashSet<int>();
        var leaves = new HashSet<int>();
        var references = new HashSet<int>();
        var invalid = new HashSet<int>();
        var pending = new Stack<(int Base, int Count, int Depth)>();
        pending.Push((0, (int)roots, 0));
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!visited.Add(node.Base)) continue;
            if (node.Depth > 32 || node.Base < 0 || (long)node.Base + node.Count * 4 > raw.Length)
            {
                warnings.Add($"KCL octree branch at 0x{node.Base:X} is out of bounds or excessively deep.");
                continue;
            }
            for (int slot = 0; slot < node.Count; slot++)
            {
                uint value = U32(raw, node.Base + slot * 4);
                int relative = (int)(value & 0x7fffffff);
                long target = (long)node.Base + relative;
                if ((value & 0x80000000) != 0)
                {
                    if (target < 0 || target >= raw.Length || (target & 1) != 0)
                    {
                        warnings.Add("KCL octree leaf pointer falls outside the index data.");
                        continue;
                    }
                    int at = (int)target;
                    if (!leaves.Add(at)) continue;
                    // Wii KCL leaf pointers address the 16-bit prefix word immediately
                    // BEFORE their prism-ID list. Skip that word, then read 1-based
                    // prism IDs until the next zero terminator. The prefix can be a
                    // reused word from a neighboring list, not necessarily zero.
                    bool terminated = false;
                    for (int p = at + 2; p + 2 <= raw.Length; p += 2)
                    {
                        int id = U16(raw, p);
                        if (id == 0) { terminated = true; break; }
                        if (id <= prismCount) references.Add(id);
                        else invalid.Add(id);
                    }
                    if (!terminated) warnings.Add("KCL octree has an unterminated leaf list.");
                }
                else
                {
                    if ((target <= node.Base && relative == 0) || target < 0 || target + 0x20 > raw.Length)
                    {
                        warnings.Add("KCL octree branch pointer is invalid.");
                        continue;
                    }
                    pending.Push(((int)target, 8, node.Depth + 1));
                }
            }
        }
        if (invalid.Count > 0)
            warnings.Add($"KCL octree contains {invalid.Count} out-of-range prism IDs (preserved for diagnostics).");
        return new KclOctreeIndex(raw, (int)roots, visited.Count, leaves.Count,
            references.Order().ToArray(), invalid.Order().ToArray());
    }

    private static Vector3[] ReadVectors(ReadOnlySpan<byte> section, string description, List<string> warnings)
    {
        if (section.Length % 12 != 0)
            warnings.Add($"KCL {description} section has {section.Length % 12} trailing alignment bytes.");
        var vectors = new Vector3[section.Length / 12];
        for (int i = 0; i < vectors.Length; i++)
        {
            Vector3 v = Vec3(section, i * 12);
            if (!Finite(v)) throw new InvalidDataException($"KCL {description}[{i}] has non-finite coordinates.");
            vectors[i] = v;
        }
        return vectors;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static Vector3 Vec3(ReadOnlySpan<byte> b, int pos) => new(F32(b, pos), F32(b, pos + 4), F32(b, pos + 8));
    private static uint U32(ReadOnlySpan<byte> b, int pos) => BinaryPrimitives.ReadUInt32BigEndian(b.Slice(pos, 4));
    private static ushort U16(ReadOnlySpan<byte> b, int pos) => BinaryPrimitives.ReadUInt16BigEndian(b.Slice(pos, 2));
    private static float F32(ReadOnlySpan<byte> b, int pos) => BitConverter.Int32BitsToSingle(unchecked((int)U32(b, pos)));
    private static int CheckedOffset(uint offset, int fileLength, string name)
    {
        if (offset >= fileLength || offset > int.MaxValue)
            throw new InvalidDataException($"KCL {name} section offset is out of bounds.");
        return (int)offset;
    }
    private static int NextSectionEnd(int start, int[] sections, int fileLength) =>
        sections.Where(offset => offset > start).DefaultIfEmpty(fileLength).Min();
}
