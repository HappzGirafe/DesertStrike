using UnityEngine;

/// <summary>
/// Every map's sky (Resources/Shaders/Sky): the low sky rising into the zenith, a haze in the fog colour at and below
/// the horizon (so the empty land round the walled map reads as haze, and far walls fade into the same colour), the
/// sun's or moon's glow and disc in the sun's direction, and stars at night. One material, recoloured for each map.
/// </summary>
public static class MapSky
{
    static readonly int ZenithId = Shader.PropertyToID("_Zenith");
    static readonly int HorizonId = Shader.PropertyToID("_Horizon");
    static readonly int FogId = Shader.PropertyToID("_Fog");
    static readonly int SunGlowId = Shader.PropertyToID("_SunGlow");
    static readonly int SunDiscId = Shader.PropertyToID("_SunDisc");
    static readonly int StarsId = Shader.PropertyToID("_Stars");

    static Material material;
    static bool unsupported;

    /// <summary>The skybox in a map's colours, or null where the graphics chip cannot draw it.</summary>
    public static Material For(MapLighting lighting)
    {
        if (material == null)
        {
            if (unsupported) return null;
            var shader = Resources.Load<Shader>("Shaders/Sky");
            if (shader == null || !shader.isSupported)
            {
                unsupported = true;
                return null;
            }
            material = new Material(shader) { name = "Map Sky", hideFlags = HideFlags.DontSave };
        }
        material.SetColor(ZenithId, lighting.Zenith);
        material.SetColor(HorizonId, lighting.Horizon);
        material.SetColor(FogId, lighting.Fog);
        material.SetColor(SunGlowId, lighting.SunGlow);
        material.SetFloat(SunDiscId, lighting.SunDisc);
        material.SetFloat(StarsId, lighting.Stars);
        return material;
    }
}
