using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Sun, ambient light, fog and sky of a map (Halloween is at night, the others by day).</summary>
public struct MapLighting
{
    public Color Sun;
    public float SunIntensity;
    public Vector3 SunAngles;
    public Color Sky, Equator, Ground;   // trilight ambient
    public Color Fog;
    public float FogStart, FogEnd;
    public bool Skybox;                  // false: a plain background colour instead of the daylight sky
    public Color Background;
}

/// <summary>The maps the game knows: de_dune (built from blocks) and the maps made in Blender (Resources/Maps/&lt;id&gt;).</summary>
public static class MapCatalog
{
    public class Entry
    {
        public string Id, Name, Description;
        public MapLighting Lighting;
        public bool AccountOnly;   // only a logged-in player can choose it (see Accounts)
    }

    public const string DefaultId = "dune";

    static readonly MapLighting Day = new MapLighting
    {
        Sun = new Color(1f, 0.95f, 0.85f), SunIntensity = 1f, SunAngles = new Vector3(50f, -30f, 0f),
        Sky = new Color(0.5f, 0.56f, 0.66f), Equator = new Color(0.42f, 0.45f, 0.38f), Ground = new Color(0.24f, 0.24f, 0.2f),
        Fog = new Color(0.72f, 0.8f, 0.88f), FogStart = 70f, FogEnd = 280f, Skybox = true, Background = new Color(0.62f, 0.75f, 0.9f),
    };

    public static readonly Entry[] All =
    {
        new Entry
        {
            Id = "dune", Name = "Dune", Description = "de_dune, a Dust-style desert map",
            Lighting = new MapLighting
            {
                Sun = new Color(1f, 0.93f, 0.8f), SunIntensity = 0.95f, SunAngles = new Vector3(52f, -35f, 0f),
                Sky = new Color(0.46f, 0.5f, 0.58f), Equator = new Color(0.44f, 0.4f, 0.34f), Ground = new Color(0.26f, 0.22f, 0.18f),
                Fog = new Color(0.84f, 0.77f, 0.64f), FogStart = 60f, FogEnd = 260f, Skybox = true, Background = new Color(0.62f, 0.75f, 0.9f),
            },
        },
        new Entry { Id = "village", Name = "Village", Description = "a village with a church, a market and a barn", Lighting = Day },
        new Entry
        {
            Id = "halloween", Name = "Halloween", Description = "a haunted village at night, with pumpkins and graves", AccountOnly = true,
            Lighting = new MapLighting
            {
                Sun = new Color(0.62f, 0.68f, 0.95f), SunIntensity = 0.55f, SunAngles = new Vector3(55f, 140f, 0f),
                Sky = new Color(0.24f, 0.22f, 0.38f), Equator = new Color(0.22f, 0.19f, 0.26f), Ground = new Color(0.11f, 0.1f, 0.11f),
                Fog = new Color(0.13f, 0.1f, 0.18f), FogStart = 25f, FogEnd = 170f, Skybox = false, Background = new Color(0.04f, 0.035f, 0.08f),
            },
        },
        new Entry
        {
            Id = "industrial", Name = "Industrial", Description = "an industrial yard with a crane, containers and a warehouse",
            Lighting = new MapLighting
            {
                Sun = new Color(1f, 0.92f, 0.78f), SunIntensity = 0.95f, SunAngles = new Vector3(48f, 30f, 0f),
                Sky = new Color(0.5f, 0.52f, 0.58f), Equator = new Color(0.45f, 0.42f, 0.36f), Ground = new Color(0.25f, 0.22f, 0.18f),
                Fog = new Color(0.8f, 0.76f, 0.68f), FogStart = 70f, FogEnd = 300f, Skybox = true, Background = new Color(0.62f, 0.75f, 0.9f),
            },
        },
    };

    public static Entry Find(string id)
    {
        foreach (var entry in All)
            if (entry.Id == id) return entry;
        return null;
    }

    public static GameMap Create(string id)
    {
        var entry = Find(id) ?? Find(DefaultId);
        GameMap map = entry.Id == "dune" ? (GameMap)new MapBuilder() : new ModelMap(entry.Id);
        map.Id = entry.Id;
        map.Name = entry.Name;
        map.Description = entry.Description;
        map.Lighting = entry.Lighting;
        return map;
    }
}

/// <summary>
/// A playable map: its objects (under <see cref="Root"/>), where each team spawns and buys, the two bombsites, the
/// bots' NavMesh and round plans, and the radar picture. <see cref="MapBuilder"/> builds de_dune from blocks;
/// <see cref="ModelMap"/> loads a map made in Blender.
/// </summary>
public abstract class GameMap
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public MapLighting Lighting { get; set; }

    public Transform Root { get; protected set; }
    public Texture2D Radar { get; protected set; }
    public Bounds SiteA { get; protected set; }
    public Bounds SiteB { get; protected set; }
    public Bounds TerroristBuyZone { get; protected set; }
    public Bounds SwatBuyZone { get; protected set; }
    public readonly List<Vector3> TerroristSpawns = new List<Vector3>();
    public readonly List<Vector3> SwatSpawns = new List<Vector3>();
    /// <summary>Places worth checking when a bot hunts for enemies.</summary>
    public readonly List<Vector3> KeyPoints = new List<Vector3>();
    /// <summary>Which way each team faces when it spawns (degrees around the vertical).</summary>
    public float TerroristSpawnYaw { get; protected set; }
    public float SwatSpawnYaw { get; protected set; } = 180f;

    protected Vector3 radarCenter;
    protected float radarSize = 128f;
    NavMeshDataInstance navMesh;

    public abstract void Build(Transform parent);

    /// <summary>"A" or "B" when the position is on a bombsite, otherwise null.</summary>
    public virtual string SiteAt(Vector3 position) => SiteA.Contains(position) ? "A" : SiteB.Contains(position) ? "B" : null;

    /// <summary>World position to 0..1 radar coordinates (y up = north, +Z).</summary>
    public Vector2 ToRadar(Vector3 world) =>
        new Vector2((world.x - radarCenter.x) / radarSize + 0.5f, (world.z - radarCenter.z) / radarSize + 0.5f);

    // Round plans for the bots.
    public abstract Vector3 SitePoint(bool siteA);
    /// <summary>Two ways from the Terrorist spawn to the site.</summary>
    public abstract List<List<Vector3>> TerroristRoutes(bool siteA);
    /// <summary>Where Terrorists watch from once they are on the site (the way SWAT come back).</summary>
    public abstract Vector3 TerroristWatch(bool siteA);
    /// <summary>Where SWAT bots go at the start of a round, and where they look from there.</summary>
    public abstract List<(List<Vector3> Route, Vector3 LookAt)> SwatPosts();

    /// <summary>Removes the map, its NavMesh and its radar picture.</summary>
    public void Unload()
    {
        if (navMesh.valid) NavMesh.RemoveNavMeshData(navMesh);
        if (Root != null) Object.Destroy(Root.gameObject);
        if (Radar != null) Object.Destroy(Radar);
    }

    /// <summary>Bakes the bots' NavMesh from every collider of the map.</summary>
    protected void BakeNavMesh(Bounds bounds)
    {
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(Root, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var settings = NavMesh.GetSettingsByID(0);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        navMesh = NavMesh.AddNavMeshData(data);
    }

    /// <summary>A box for a zone on the ground: from a metre below the floor to 8 m above, grown sideways.</summary>
    protected static Bounds Zone(Bounds area, float grow)
    {
        var zone = new Bounds();
        zone.SetMinMax(new Vector3(area.min.x - grow, area.center.y - 1f, area.min.z - grow),
                       new Vector3(area.max.x + grow, area.center.y + 8f, area.max.z + grow));
        return zone;
    }
}
