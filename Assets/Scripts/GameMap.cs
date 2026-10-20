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

/// <summary>
/// The maps the game knows, made in Blender (Resources/Maps/&lt;id&gt;; de_dune is built from blocks when it has no
/// model there). A map may also have its own versions for small matches, with one site's half shut by hand:
/// Resources/Maps/&lt;id&gt;/closedA and closedB (Tools/export_maps.ps1); without one the game walls that half off itself.
/// </summary>
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
        new Entry
        {
            Id = "bone", Name = "Bone", Description = "a bone yard: two fenced bases, giant pumpkins and a ribcage in the middle",
            Lighting = new MapLighting
            {
                Sun = new Color(1f, 0.82f, 0.62f), SunIntensity = 0.85f, SunAngles = new Vector3(38f, 60f, 0f),
                Sky = new Color(0.46f, 0.4f, 0.54f), Equator = new Color(0.42f, 0.34f, 0.36f), Ground = new Color(0.2f, 0.16f, 0.16f),
                Fog = new Color(0.36f, 0.27f, 0.36f), FogStart = 70f, FogEnd = 300f, Skybox = false, Background = new Color(0.27f, 0.2f, 0.32f),
            },
        },
    };

    public static Entry Find(string id)
    {
        foreach (var entry in All)
            if (entry.Id == id) return entry;
        return null;
    }

    static readonly Dictionary<string, bool> present = new Dictionary<string, bool>();

    // Whether Resources/<folder>/parts.json exists (every exported map has one).
    static bool Exported(string folder)
    {
        if (!present.TryGetValue(folder, out bool found))
        {
            var parts = Resources.Load<TextAsset>(folder + "/parts");
            present[folder] = found = parts != null;
            if (parts != null) Resources.UnloadAsset(parts);
        }
        return found;
    }

    /// <summary>Whether the map has its own version with that site's half shut ("A" or "B").</summary>
    public static bool HasClosedVersion(string id, string site) =>
        (site == "A" || site == "B") && Exported($"Maps/{id}/closed{site}");

    /// <param name="closed">"A" or "B": the map's own closed version (see <see cref="HasClosedVersion"/>).</param>
    public static GameMap Create(string id, string closed = null)
    {
        var entry = Find(id) ?? Find(DefaultId);
        bool blocks = entry.Id == "dune" && !Exported("Maps/dune");
        GameMap map = blocks ? (GameMap)new MapBuilder() : new ModelMap(entry.Id, HasClosedVersion(entry.Id, closed) ? closed : null);
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

    /// <summary>Leaves the map's parts unmerged (static batching merges them for drawing): -ds-export-closed reads them.</summary>
    public static bool KeepMeshesApart;

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

    /// <summary>The bombsite walled off for small matches (1v1 to 3v3), or null when both are open.</summary>
    public string ClosedSite { get; private set; }
    Transform barrier;

    /// <summary>"A" or "B" when this is the map's own closed version for small matches (made in Blender, with its
    /// own walls); null for the normal map.</summary>
    public virtual string ClosedVersion => null;

    /// <summary>True when the map itself shuts that site's half (its closed version): no walls are added.</summary>
    protected virtual bool ClosedByModel(string site) => false;

    /// <summary>A place on the map, for messages: in the coordinates the map was made in.</summary>
    protected virtual string Where(Vector3 p) => $"({p.x:0}, {p.z:0})";

    public abstract void Build(Transform parent);

    /// <summary>"A" or "B" when the position is on a bombsite that is open, otherwise null.</summary>
    public string SiteAt(Vector3 position)
    {
        string site = SiteUnder(position);
        return site == ClosedSite ? null : site;
    }

    /// <summary>The bombsite under a position, open or not.</summary>
    protected virtual string SiteUnder(Vector3 position) => SiteA.Contains(position) ? "A" : SiteB.Contains(position) ? "B" : null;

    public Bounds SiteZone(string site) => site == "A" ? SiteA : SiteB;

    // The closed half (small matches): the map in 1 m cells, true on the closed site's side.
    const float GridCell = 1f;
    bool[] closedCells;
    int gridW, gridH;
    Vector3 gridMin;

    /// <summary>On the closed side of the map (small matches), seen from above.</summary>
    public bool InClosedSite(Vector3 p)
    {
        if (ClosedSite == null) return false;
        if (closedCells == null)
        {
            Bounds zone = SiteZone(ClosedSite);
            zone.Expand(new Vector3(4f, 0f, 4f));
            return zone.Contains(new Vector3(p.x, zone.center.y, p.z));
        }
        int x = Mathf.FloorToInt((p.x - gridMin.x) / GridCell), z = Mathf.FloorToInt((p.z - gridMin.z) / GridCell);
        return x >= 0 && z >= 0 && x < gridW && z < gridH && closedCells[x + z * gridW];
    }

    /// <summary>
    /// Small matches play on one site: the other site's half of the map is walled off (or both open again: null).
    /// The map's walkable ground is cut where the way to the closed site gets shorter than the way to the open one,
    /// keeping both spawns, the open site and the ways between them open; every passage across that line gets a
    /// wall. A map where no such cut leaves the rest connected gets a wall round the closed site instead.
    /// </summary>
    public void SetClosedSite(string site)
    {
        if (site == ClosedSite) return;
        ClosedSite = site;
        RemoveWalls();
        closedCells = null;
        if (Root == null) return;
        if (bakedWithWalls) Rebake();   // back to the map's own NavMesh
        if (site == null) return;

        float started = Time.realtimeSinceStartup;
        if (ClosedByModel(site))
        {
            if (MarkClosedSide(site, out int shut))
            {
                Debug.Log($"[Map] {Name}: its own version with the {site} side closed ({shut}% of the map out of reach)");
                return;
            }
            Debug.LogWarning($"[Map] {Name}: its version with the {site} side closed still lets the spawns reach site {site}; walls added");
        }
        if (WallOffHalf(site, out int share, out int panels, out int plugs))
        {
            Debug.Log($"[Map] The {site} side is closed for a small match: {share}% of the map, {panels} wall panels" +
                      $"{(plugs > 0 ? $" and {plugs} across ways the first walls missed" : "")} ({(Time.realtimeSinceStartup - started) * 1000f:0} ms)");
            return;
        }
        RemoveWalls();
        closedCells = null;
        NewBarrier(site);
        WallRoundSite(site);
        Rebake();
        Debug.Log($"[Map] Site {site} closed (walled off for a small match; its whole side could not be closed here)");
    }

    // The walls go at once (not at the end of the frame): the NavMesh is baked again right after.
    void RemoveWalls()
    {
        if (barrier != null) Object.DestroyImmediate(barrier.gameObject);
        barrier = null;
    }

    void NewBarrier(string site)
    {
        barrier = new GameObject("Closed half " + site).transform;
        barrier.SetParent(Root, false);
    }

    // The bots' NavMesh again, with the walls in it (they are colliders under the map's root).
    void Rebake()
    {
        if (navMesh.valid) NavMesh.RemoveNavMeshData(navMesh);
        BakeNavMesh(bakedBounds);
        bakedWithWalls = barrier != null;
    }

    // Concrete with two warning bands at eye height: where it closes a passage, it is plain that the way is shut.
    void Panel(Vector3 center, Vector3 size, float floor)
    {
        var concrete = new Color(0.55f, 0.55f, 0.53f);
        var hazard = new Color(0.95f, 0.75f, 0.1f);
        var wall = Effects.Shape(PrimitiveType.Cube, barrier, center, size, concrete, collider: true);
        wall.name = "Closed side wall";
        foreach (float above in new[] { 1.1f, 2.3f })
            Effects.Shape(PrimitiveType.Cube, barrier, new Vector3(center.x, floor + above, center.z),
                          new Vector3(size.x + 0.04f, 0.3f, size.z + 0.04f), hazard);
    }

    void WallRoundSite(string site)
    {
        Bounds zone = SiteZone(site);
        const float grow = 1.5f, thick = 0.5f;
        float bottom = zone.min.y - 1f, height = 8f, floor = zone.min.y + 1f;
        float x0 = zone.min.x - grow, x1 = zone.max.x + grow, z0 = zone.min.z - grow, z1 = zone.max.z + grow;
        Panel(new Vector3((x0 + x1) / 2f, bottom + height / 2f, z0), new Vector3(x1 - x0 + thick, height, thick), floor);
        Panel(new Vector3((x0 + x1) / 2f, bottom + height / 2f, z1), new Vector3(x1 - x0 + thick, height, thick), floor);
        Panel(new Vector3(x0, bottom + height / 2f, (z0 + z1) / 2f), new Vector3(thick, height, z1 - z0 + thick), floor);
        Panel(new Vector3(x1, bottom + height / 2f, (z0 + z1) / 2f), new Vector3(thick, height, z1 - z0 + thick), floor);
    }

    /// <summary>
    /// Walls off the closed site's side. The ground (1 m cells) is cut where the walk to the closed site gets shorter
    /// than the walk to the open one; the cut moves towards the closed site until the spawns and the open site stay
    /// together. Then the NavMesh is baked with the walls and checked for real: every spawn must still reach the open
    /// site and the other spawn, and none the closed site; a way through that the cells missed (a ledge, a gap) gets a
    /// wall across it. False when no cut works on this map.
    /// </summary>
    /// <summary>
    /// The walkable ground in 1 m cells (from the NavMesh), with each cell's floor height and which neighbouring
    /// cells can be walked between (to the right, +x, and up, +z). False when there is no NavMesh, or it is too big.
    /// </summary>
    bool WalkGrid(out bool[] walk, out float[] floorY, out bool[] right, out bool[] up)
    {
        walk = right = up = null;
        floorY = null;
        var triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices.Length == 0) return false;
        Vector3 lo = triangulation.vertices[0], hi = lo;
        foreach (var v in triangulation.vertices)
        {
            lo = Vector3.Min(lo, v);
            hi = Vector3.Max(hi, v);
        }
        gridMin = new Vector3(lo.x - 1f, lo.y, lo.z - 1f);
        gridW = Mathf.CeilToInt((hi.x - gridMin.x) / GridCell) + 2;
        gridH = Mathf.CeilToInt((hi.z - gridMin.z) / GridCell) + 2;
        int n = gridW * gridH;
        if (n > 250000) return false;
        walk = new bool[n];
        floorY = new float[n];
        cellPoints = new Vector3[n];
        float probe = lo.y - 0.5f, reach = Mathf.Min(hi.y - lo.y + 1.5f, 4f);
        for (int z = 0; z < gridH; z++)
            for (int x = 0; x < gridW; x++)
            {
                Vector3 c = CellCentre(x, z, probe);
                if (!NavMesh.SamplePosition(c, out var hit, reach, NavMesh.AllAreas)) continue;
                if (new Vector2(hit.position.x - c.x, hit.position.z - c.z).magnitude > GridCell * 0.71f) continue;
                walk[x + z * gridW] = true;
                floorY[x + z * gridW] = hit.position.y;
                cellPoints[x + z * gridW] = hit.position;
            }
        right = new bool[n];
        up = new bool[n];
        for (int z = 0; z < gridH; z++)
            for (int x = 0; x < gridW; x++)
            {
                int i = x + z * gridW;
                if (!walk[i]) continue;
                if (x + 1 < gridW && walk[i + 1]) right[i] = Joined(i, i + 1, x, z, x + 1, z, floorY);
                if (z + 1 < gridH && walk[i + gridW]) up[i] = Joined(i, i + gridW, x, z, x, z + 1, floorY);
            }
        return true;
    }

    /// <summary>
    /// The map's own closed version already shuts the site's half: the closed side is all ground the spawns cannot
    /// walk to. False when a spawn can still reach the closed site, or the spawns cannot reach the open site or each
    /// other (then walls are added as on any other map).
    /// </summary>
    bool MarkClosedSide(string site, out int share)
    {
        share = 0;
        if (!WalkGrid(out var walk, out var floorY, out var right, out var up)) return false;
        var spawnCells = new List<int>();
        foreach (var spawn in TerroristSpawns) { int c = NearestCell(spawn, walk); if (c >= 0) spawnCells.Add(c); }
        foreach (var spawn in SwatSpawns) { int c = NearestCell(spawn, walk); if (c >= 0) spawnCells.Add(c); }
        int openSite = NearestCell(SitePoint(site != "A"), walk);
        int tSpawn = NearestCell(TerroristSpawns[0], walk), swatSpawn = NearestCell(SwatSpawns[0], walk);
        if (spawnCells.Count == 0 || openSite < 0 || tSpawn < 0 || swatSpawn < 0) return false;
        int[] fromT = Distances(new[] { tSpawn }, walk, right, up, null);
        if (fromT[swatSpawn] < 0 || fromT[openSite] < 0) return false;
        int[] fromSpawns = Distances(spawnCells.ToArray(), walk, right, up, null);

        // No spawn may walk onto the closed site: paths to points all over its floor (every 3 m).
        Bounds zone = SiteZone(site);
        var path = new NavMeshPath();
        for (float x = zone.min.x + 1f; x < zone.max.x; x += 3f)
            for (float z = zone.min.z + 1f; z < zone.max.z; z += 3f)
            {
                if (!NavMesh.SamplePosition(new Vector3(x, zone.min.y + 1f, z), out var hit, 1.5f, NavMesh.AllAreas)
                    || SiteUnder(hit.position) != site) continue;
                if (!Reaches(TerroristSpawns[0], hit.position, path) && !Reaches(SwatSpawns[0], hit.position, path)) continue;
                // Where the way in goes, so whoever made the map can find the gap.
                var way = new List<string>();
                foreach (var corner in path.corners) way.Add(Where(corner));
                Debug.LogWarning($"[Map] {Name}: the way onto closed site {site}: {string.Join(" -> ", way)}");
                return false;
            }

        var closed = new bool[walk.Length];
        int closedCount = 0, walkCount = 0;
        for (int i = 0; i < walk.Length; i++)
        {
            if (!walk[i]) continue;
            walkCount++;
            closed[i] = fromSpawns[i] < 0;
            if (closed[i]) closedCount++;
        }
        closedCells = closed;
        share = walkCount > 0 ? Mathf.RoundToInt(100f * closedCount / walkCount) : 0;
        return true;
    }

    bool WallOffHalf(string site, out int share, out int panels, out int plugs)
    {
        share = panels = plugs = 0;
        // 1. The walkable ground, in 1 m cells, and which neighbouring cells can be walked between.
        if (!WalkGrid(out var walk, out var floorY, out var right, out var up)) return false;
        int n = walk.Length;

        // 2. How far each cell is, walking, from the sites and from the nearest spawn point.
        Vector3 shutPoint = SitePoint(site == "A"), openPoint = SitePoint(site != "A");
        int closedSite = NearestCell(shutPoint, walk), openSite = NearestCell(openPoint, walk);
        int tSpawn = NearestCell(TerroristSpawns[0], walk), swatSpawn = NearestCell(SwatSpawns[0], walk);
        if (closedSite < 0 || openSite < 0 || tSpawn < 0 || swatSpawn < 0) return false;
        int[] fromClosed = Distances(new[] { closedSite }, walk, right, up, null);
        int[] fromOpen = Distances(new[] { openSite }, walk, right, up, null);
        var spawnCells = new List<int>();
        foreach (var spawn in TerroristSpawns) { int c = NearestCell(spawn, walk); if (c >= 0) spawnCells.Add(c); }
        foreach (var spawn in SwatSpawns) { int c = NearestCell(spawn, walk); if (c >= 0) spawnCells.Add(c); }
        int[] fromSpawns = Distances(spawnCells.ToArray(), walk, right, up, null);

        // 3. Cut, wall, bake and check; a cut that does not work moves towards the closed site.
        foreach (int margin in new[] { 0, 4, 8, 14, 20, 30, 45 })
        {
            var closed = new bool[n];
            for (int i = 0; i < n; i++)
                closed[i] = walk[i] && fromClosed[i] >= 0 && (fromOpen[i] < 0 || fromClosed[i] + margin < fromOpen[i])
                            && (fromSpawns[i] < 0 || fromSpawns[i] > 8);
            if (!closed[closedSite]) return false;
            int[] open = Distances(new[] { tSpawn }, walk, right, up, closed);
            if (open[swatSpawn] < 0 || open[openSite] < 0) continue;

            NewBarrier(site);
            panels = BuildCut(closed, walk, right, up, floorY);
            closedCells = closed;
            Rebake();
            plugs = 0;
            bool works = OpenWaysKept(openPoint);
            while (works)
            {
                Vector3? leak = Leak(shutPoint);
                if (leak == null) break;   // sealed
                if (plugs >= 12)
                {
                    works = false;
                    break;
                }
                Plug(leak.Value, LeakDirection);
                plugs++;
                Rebake();
                works = OpenWaysKept(openPoint);
            }
            if (works)
            {
                int closedCount = 0, walkCount = 0;
                for (int i = 0; i < n; i++)
                {
                    if (walk[i]) walkCount++;
                    if (closed[i]) closedCount++;
                }
                share = walkCount > 0 ? Mathf.RoundToInt(100f * closedCount / walkCount) : 0;
                return true;
            }
            RemoveWalls();
            closedCells = null;
            Rebake();
        }
        return false;
    }

    // A wall on every cell side between the two sides, joined into long panels.
    int BuildCut(bool[] closed, bool[] walk, bool[] right, bool[] up, float[] floorY)
    {
        var linesX = new Dictionary<int, List<(int, float)>>();   // walls along z, on the left side of cell column x
        var linesZ = new Dictionary<int, List<(int, float)>>();   // walls along x, on the lower side of cell row z
        for (int z = 0; z < gridH; z++)
            for (int x = 0; x < gridW; x++)
            {
                int i = x + z * gridW;
                if (right[i] && closed[i] != closed[i + 1])
                    Add(linesX, x + 1, z, Mathf.Min(floorY[i], floorY[i + 1]));
                if (up[i] && closed[i] != closed[i + gridW])
                    Add(linesZ, z + 1, x, Mathf.Min(floorY[i], floorY[i + gridW]));
            }
        int count = 0;
        const float thick = 0.4f, tall = 5f;
        foreach (var line in linesX)
            foreach (var (from, to, floor) in Runs(line.Value))
            {
                float x = gridMin.x + line.Key * GridCell;
                float z0 = gridMin.z + from * GridCell - 0.25f, z1 = gridMin.z + (to + 1) * GridCell + 0.25f;
                Panel(new Vector3(x, floor - 1f + tall / 2f, (z0 + z1) / 2f), new Vector3(thick, tall, z1 - z0), floor);
                count++;
            }
        foreach (var line in linesZ)
            foreach (var (from, to, floor) in Runs(line.Value))
            {
                float z = gridMin.z + line.Key * GridCell;
                float x0 = gridMin.x + from * GridCell - 0.25f, x1 = gridMin.x + (to + 1) * GridCell + 0.25f;
                Panel(new Vector3((x0 + x1) / 2f, floor - 1f + tall / 2f, z), new Vector3(x1 - x0, tall, thick), floor);
                count++;
            }
        return count;
    }

    bool Reaches(Vector3 from, Vector3 to, NavMeshPath path)
    {
        if (!NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas))
            return false;
        return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    // Both spawns still reach the open site and each other.
    bool OpenWaysKept(Vector3 openPoint)
    {
        var path = new NavMeshPath();
        return Reaches(TerroristSpawns[0], openPoint, path) && Reaches(SwatSpawns[0], openPoint, path)
               && Reaches(TerroristSpawns[0], SwatSpawns[0], path);
    }

    Vector3 LeakDirection;

    // Where a spawn's way to the closed site crosses onto the closed side, or null when no spawn gets there.
    Vector3? Leak(Vector3 shutPoint)
    {
        var path = new NavMeshPath();
        foreach (var spawn in new[] { TerroristSpawns[0], SwatSpawns[0] })
        {
            if (!Reaches(spawn, shutPoint, path)) continue;
            var corners = path.corners;
            for (int k = 0; k + 1 < corners.Length; k++)
            {
                Vector3 a = corners[k], b = corners[k + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.4f));
                Vector3 previous = a;
                for (int s = 1; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(a, b, (float)s / steps);
                    if (InClosedSite(p) && !InClosedSite(previous))
                    {
                        LeakDirection = new Vector3(p.x - previous.x, 0f, p.z - previous.z).normalized;
                        return (p + previous) / 2f;
                    }
                    previous = p;
                }
            }
            // On the closed side all the way (should not happen): close it at the site.
            LeakDirection = new Vector3(shutPoint.x - spawn.x, 0f, shutPoint.z - spawn.z).normalized;
            return Vector3.MoveTowards(shutPoint, spawn, 6f);
        }
        return null;
    }

    // A wall across a way the cells missed: from wall to wall of that passage (12 m each side at most).
    void Plug(Vector3 at, Vector3 along)
    {
        if (along.sqrMagnitude < 0.01f) along = Vector3.forward;
        Vector3 across = new Vector3(-along.z, 0f, along.x);
        Vector3 eye = at + Vector3.up * 1.2f;
        float left = Physics.Raycast(eye, -across, out var hitLeft, 12f, Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hitLeft.distance : 12f;
        float right = Physics.Raycast(eye, across, out var hitRight, 12f, Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore) ? hitRight.distance : 12f;
        Vector3 centre = at + across * ((right - left) / 2f);
        float width = left + right + 0.6f;
        const float tall = 5f;
        var size = new Vector3(width, tall, 0.4f);
        var wall = Effects.Shape(PrimitiveType.Cube, barrier, new Vector3(centre.x, at.y - 1f + tall / 2f, centre.z), size,
                                 new Color(0.55f, 0.55f, 0.53f), collider: true, euler: new Vector3(0f, Quaternion.LookRotation(along).eulerAngles.y, 0f));
        wall.name = "Closed side wall";
        foreach (float above in new[] { 1.1f, 2.3f })
            Effects.Shape(PrimitiveType.Cube, barrier, new Vector3(centre.x, at.y + above, centre.z), new Vector3(width + 0.04f, 0.3f, 0.44f),
                          new Color(0.95f, 0.75f, 0.1f), euler: new Vector3(0f, Quaternion.LookRotation(along).eulerAngles.y, 0f));
    }

    Vector3 CellCentre(int x, int z, float y) => new Vector3(gridMin.x + (x + 0.5f) * GridCell, y, gridMin.z + (z + 0.5f) * GridCell);

    // Walkable between two neighbouring cells: from the NavMesh point found for one to the other's (a cell's middle may
    // lie just off the NavMesh, beside a wall, and a ray from there is not stopped by it).
    bool Joined(int a, int b, int ax, int az, int bx, int bz, float[] floorY)
    {
        if (Mathf.Abs(floorY[a] - floorY[b]) > 1.2f) return false;
        return !NavMesh.Raycast(cellPoints[a], cellPoints[b], out _, NavMesh.AllAreas);
    }

    Vector3[] cellPoints;   // each walkable cell's point on the NavMesh (WalkGrid)

    int NearestCell(Vector3 p, bool[] walk)
    {
        int cx = Mathf.FloorToInt((p.x - gridMin.x) / GridCell), cz = Mathf.FloorToInt((p.z - gridMin.z) / GridCell);
        for (int r = 0; r <= 4; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x >= 0 && z >= 0 && x < gridW && z < gridH && walk[x + z * gridW]) return x + z * gridW;
                }
        return -1;
    }

    // Steps from the nearest of some cells to every other (-1: cannot get there), not entering cells marked in "avoid".
    int[] Distances(int[] starts, bool[] walk, bool[] right, bool[] up, bool[] avoid)
    {
        var dist = new int[walk.Length];
        for (int i = 0; i < dist.Length; i++) dist[i] = -1;
        var queue = new Queue<int>();
        foreach (int start in starts)
        {
            if (dist[start] >= 0) continue;
            dist[start] = 0;
            queue.Enqueue(start);
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            void Visit(int j)
            {
                if (dist[j] >= 0 || (avoid != null && avoid[j])) return;
                dist[j] = dist[i] + 1;
                queue.Enqueue(j);
            }
            if (right[i]) Visit(i + 1);
            if (i % gridW > 0 && right[i - 1]) Visit(i - 1);
            if (up[i]) Visit(i + gridW);
            if (i >= gridW && up[i - gridW]) Visit(i - gridW);
        }
        return dist;
    }

    static void Add(Dictionary<int, List<(int, float)>> lines, int line, int at, float floor)
    {
        if (!lines.TryGetValue(line, out var list)) lines[line] = list = new List<(int, float)>();
        list.Add((at, floor));
    }

    // Neighbouring cell sides on one line, at about the same height, become one panel.
    static IEnumerable<(int, int, float)> Runs(List<(int at, float floor)> sides)
    {
        sides.Sort((a, b) => a.at.CompareTo(b.at));
        int from = sides[0].at, to = from;
        float floor = sides[0].floor;
        for (int k = 1; k < sides.Count; k++)
        {
            var (at, y) = sides[k];
            if (at == to + 1 && Mathf.Abs(y - floor) < 0.8f)
            {
                to = at;
                floor = Mathf.Min(floor, y);
                continue;
            }
            yield return (from, to, floor);
            from = to = at;
            floor = y;
        }
        yield return (from, to, floor);
    }

    /// <summary>Tests (-ds-closed-site): with the walls up, do the spawns still reach the open site and each other,
    /// and is the closed site out of reach?</summary>
    public string CheckClosedSite()
    {
        if (ClosedSite == null) return "both sites open";
        Vector3 open = SitePoint(ClosedSite == "B"), shut = SitePoint(ClosedSite == "A");
        string Way(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas))
                return "off the navmesh";
            var path = new NavMeshPath();
            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
            return path.status == NavMeshPathStatus.PathComplete ? "ok" : path.status.ToString();
        }
        int shutIn = 0;
        foreach (var spawn in TerroristSpawns) if (InClosedSite(spawn)) shutIn++;
        foreach (var spawn in SwatSpawns) if (InClosedSite(spawn)) shutIn++;
        return $"{Name}, the {ClosedSite} side closed: T spawn to the open site {Way(TerroristSpawns[0], open)}, " +
               $"SWAT spawn to the open site {Way(SwatSpawns[0], open)}, T spawn to SWAT spawn {Way(TerroristSpawns[0], SwatSpawns[0])}, " +
               $"T spawn to the closed site {Way(TerroristSpawns[0], shut)}, SWAT spawn to the closed site {Way(SwatSpawns[0], shut)}, " +
               $"spawns on the closed side {shutIn}";
    }

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

    Bounds bakedBounds;
    bool bakedWithWalls;   // the NavMesh has the closed side's walls in it

    /// <summary>Bakes the bots' NavMesh from every collider of the map.</summary>
    protected void BakeNavMesh(Bounds bounds)
    {
        bakedBounds = bounds;
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
