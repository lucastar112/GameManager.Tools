namespace GameManager.Tools.TrackFormats;

/// <summary>Controls resource decoding, not the graphics API used by the caller.</summary>
public enum BrresImportMode
{
    /// <summary>Only MDL0 geometry and polygon references. No textures, images, or TEV state.</summary>
    GeometryOnly = 0,
    /// <summary>Geometry, UV0/UV1, vertex colors, first-layer materials and decoded RGBA8 textures.</summary>
    BaselineTextured = 1,
    /// <summary>Additionally retain GX texgens, TEV stages, layers, and indirect state.
    /// This is decoder metadata; cycle-exact TEV rendering is not implemented in the library.</summary>
    TevMetadata = 2
}
