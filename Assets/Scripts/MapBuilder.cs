using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds "de_dune": a desert map with a Dust-2-style layout (outside long, long doors, long A,
/// catwalk/short A, mid doors, upper and lower tunnels to B) out of primitives, then bakes a NavMesh
/// for the bots. The layout is a 32x32 grid of 4 m cells; north (+Z) is the SWAT side, south the
/// Terrorist side.
/// </summary>
public class MapBuilder
{
    public const int Size = 32;
    public const float Cell = 4f;

    enum Area : byte { Wall, Floor, TSpawn, SwatSpawn, SiteA, SiteB }

    static readonly Color SandFloor = new Color(0.66f, 0.55f, 0.38f);
    static readonly Color WallColor = new Color(0.8f, 0.68f, 0.5f);
    static readonly Color CrateColor = new Color(0.52f, 0.37f, 0.2f);
    static readonly Color DoorColor = new Color(0.33f, 0.22f, 0.13f);
    static readonly Color PaintColor = new Color(0.55f, 0.12f, 0.08f);

    // Letter strokes as (x0, y0, x1, y1) in a 0.6 x 1 box.
    static readonly float[,] LetterA = { { 0f, 0f, 0.3f, 1f }, { 0.3f, 1f, 0.6f, 0f }, { 0.13f, 0.42f, 0.47f, 0.42f } };
    static readonly float[,] LetterB =
    {
        { 0f, 0f, 0f, 1f }, { 0f, 1f, 0.42f, 1f }, { 0.42f, 1f, 0.55f, 0.86f }, { 0.55f, 0.86f, 0.55f, 0.64f },
        { 0.55f, 0.64f, 0.42f, 0.5f }, { 0f, 0.5f, 0.45f, 0.5f }, { 0.45f, 0.5f, 0.6f, 0.36f },
        { 0.6f, 0.36f, 0.6f, 0.14f }, { 0.6f, 0.14f, 0.45f, 0f }, { 0f, 0f, 0.45f, 0f },
    };

    readonly Area[,] grid = new Area[Size, Size];
    readonly List<(Area area, int x0, int z0, int x1, int z1)> patches = new List<(Area, int, int, int, int)>();
    readonly Dictionary<string, Vector3> points = new Dictionary<string, Vector3>();
    readonly System.Random random = new System.Random(2);

    public Transform Root { get; private set; }
    public Texture2D Radar { get; private set; }
    public Bounds SiteA { get; private set; }
    public Bounds SiteB { get; private set; }
    public Bounds TerroristBuyZone { get; private set; }
    public Bounds SwatBuyZone { get; private set; }

    /// <summary>"A" or "B" when the position is on a bombsite, otherwise null.</summary>
    public string SiteAt(Vector3 position) => SiteA.Contains(position) ? "A" : SiteB.Contains(position) ? "B" : null;
    public readonly List<Vector3> TerroristSpawns = new List<Vector3>();
    public readonly List<Vector3> SwatSpawns = new List<Vector3>();
    public readonly List<Vector3> KeyPoints = new List<Vector3>();

    public static Vector3 CellCenter(float x, float z) =>
        new Vector3((x - Size / 2f + 0.5f) * Cell, 0f, (z - Size / 2f + 0.5f) * Cell);

    public Vector3 Point(string name) => points[name];

    public List<Vector3> PointsFor(IEnumerable<string> names)
    {
        var result = new List<Vector3>();
        foreach (var name in names) result.Add(points[name]);
        return result;
    }

    /// <summary>World position to 0..1 radar coordinates (y up = north).</summary>
    public static Vector2 ToRadar(Vector3 world) =>
        new Vector2(world.x / (Size * Cell) + 0.5f, world.z / (Size * Cell) + 0.5f);

    public void Build(Transform parent)
    {
        Root = new GameObject("Map").transform;
        Root.SetParent(parent, false);
        CarveLayout();
        BuildGround();
        BuildWalls();
        BuildProps();
        BuildSiteLetters();
        DefinePoints();
        BuildRadar();
        BakeNavMesh();
    }

    void CarveLayout()
    {
        Carve(Area.TSpawn, 10, 1, 21, 6);       // T spawn
        Carve(Area.Floor, 22, 2, 29, 5);        // outside long
        Carve(Area.Floor, 27, 6, 28, 9);        // long doors
        Carve(Area.Floor, 26, 10, 29, 23);      // long A
        Carve(Area.SiteA, 21, 24, 29, 29);      // bombsite A
        Carve(Area.Floor, 17, 17, 21, 18);      // catwalk
        Carve(Area.Floor, 20, 19, 21, 23);      // short A
        Carve(Area.Floor, 14, 7, 16, 22);       // mid
        Carve(Area.Floor, 15, 23, 15, 24);      // mid doors
        Carve(Area.SwatSpawn, 11, 25, 19, 30);  // SWAT (CT) spawn
        Carve(Area.Floor, 20, 26, 20, 28);      // spawn to A
        Carve(Area.Floor, 10, 26, 10, 27);      // B doors
        Carve(Area.SiteB, 2, 23, 9, 29);        // bombsite B
        Carve(Area.Floor, 2, 12, 4, 22);        // upper tunnels
        Carve(Area.Floor, 2, 9, 9, 11);         // tunnel entrance hall
        Carve(Area.Floor, 6, 2, 9, 8);          // outside tunnels
        Carve(Area.Floor, 5, 14, 13, 15);       // lower tunnels

        SiteA = Zone(21, 24, 29, 29, 0f);
        SiteB = Zone(2, 23, 9, 29, 0f);
        TerroristBuyZone = Zone(10, 1, 21, 6, 1f);
        SwatBuyZone = Zone(11, 25, 19, 30, 1f);
    }

    /// <summary>World-space box over a block of cells, grown by <paramref name="grow"/> metres sideways.</summary>
    static Bounds Zone(int x0, int z0, int x1, int z1, float grow)
    {
        var bounds = new Bounds();
        bounds.SetMinMax(CellCenter(x0, z0) - new Vector3(Cell / 2f + grow, 1f, Cell / 2f + grow),
                         CellCenter(x1, z1) + new Vector3(Cell / 2f + grow, 8f, Cell / 2f + grow));
        return bounds;
    }

    void Carve(Area area, int x0, int z0, int x1, int z1)
    {
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                grid[x, z] = area;
        if (area != Area.Floor) patches.Add((area, x0, z0, x1, z1));
    }

    void BuildGround()
    {
        float extent = Size * Cell;
        Effects.Shape(PrimitiveType.Cube, Root, new Vector3(0f, -0.5f, 0f), new Vector3(extent, 1f, extent), SandFloor, collider: true);

        // Slightly different sand on the sites and spawns so they read on the ground.
        foreach (var p in patches)
        {
            Color color = p.area == Area.SiteA || p.area == Area.SiteB
                ? new Color(0.58f, 0.47f, 0.32f)
                : new Color(0.7f, 0.6f, 0.43f);
            Vector3 center = (CellCenter(p.x0, p.z0) + CellCenter(p.x1, p.z1)) * 0.5f;
            center.y = 0.01f;
            var size = new Vector3((p.x1 - p.x0 + 1) * Cell - 0.6f, 0.02f, (p.z1 - p.z0 + 1) * Cell - 0.6f);
            Effects.Shape(PrimitiveType.Cube, Root, center, size, color);
        }
    }

    void BuildWalls()
    {
        // Merge each row's wall cells into long blocks to keep the object count low.
        for (int z = 0; z < Size; z++)
        {
            int x = 0;
            while (x < Size)
            {
                if (grid[x, z] != Area.Wall) { x++; continue; }
                int start = x;
                while (x < Size && grid[x, z] == Area.Wall) x++;

                bool outer = z == 0 || z == Size - 1 || start == 0 || x == Size;
                float height = outer ? 10f : 7f;
                float shade = (random.Next(5) - 2) * 0.015f;
                var color = new Color(WallColor.r + shade, WallColor.g + shade, WallColor.b + shade);
                Vector3 center = (CellCenter(start, z) + CellCenter(x - 1, z)) * 0.5f;
                center.y = height / 2f;
                Effects.Shape(PrimitiveType.Cube, Root, center, new Vector3((x - start) * Cell, height, Cell), color, collider: true);
            }
        }
    }

    void BuildProps()
    {
        // Bombsite A
        Crate(23, 26); Crate(24, 26, 0f, 0f, 2); Crate(27, 28, 0.6f, 0.6f, 2); Crate(29, 29, 0.8f, 0.8f); Crate(22, 24, -0.5f, 0f);
        // Bombsite B
        Crate(4, 26, 0f, 0f, 2); Crate(5, 26); Crate(8, 24); Crate(3, 28, -0.6f, 0.6f); Crate(7, 29, 0.6f, 0.6f);
        // Long, mid, spawns, tunnels
        Crate(26, 15, -0.8f, 0f); Crate(29, 20, 0.8f, 0f);
        Crate(16, 11, 0.8f, 0f);
        Crate(12, 2); Crate(19, 5, 0f, 0f, 2);
        Crate(17, 29);
        Crate(8, 10, 0.8f, 0.5f);
        Crate(24, 2, 0f, -0.8f);

        Barrel(20, 2, new Color(0.55f, 0.2f, 0.12f)); Barrel(11, 6, new Color(0.2f, 0.3f, 0.45f));
        Barrel(29, 11, new Color(0.55f, 0.2f, 0.12f)); Barrel(2, 24, new Color(0.2f, 0.3f, 0.45f));
        Barrel(19, 30, new Color(0.55f, 0.2f, 0.12f));

        // Door frames over the choke points.
        Lintel(15f, 23.5f, 4f, 8f);     // mid doors
        Lintel(27.5f, 7.5f, 8f, 8f);    // long doors
        Lintel(10f, 26.5f, 4f, 8f);     // B doors

        // Open wooden doors at mid and long.
        float midZ = CellCenter(0f, 22f).z + 1.2f;
        Effects.Shape(PrimitiveType.Cube, Root, new Vector3(-3.85f, 1.75f, midZ), new Vector3(0.15f, 3.5f, 2.4f), DoorColor, collider: true);
        Effects.Shape(PrimitiveType.Cube, Root, new Vector3(-0.15f, 1.75f, midZ), new Vector3(0.15f, 3.5f, 2.4f), DoorColor, collider: true);
        float longZ = CellCenter(0f, 9.5f).z;
        float longLeft = CellCenter(27f, 0f).x - Cell / 2f, longRight = CellCenter(28f, 0f).x + Cell / 2f;
        Effects.Shape(PrimitiveType.Cube, Root, new Vector3(longLeft + 0.1f, 1.75f, longZ), new Vector3(0.15f, 3.5f, 2.4f), DoorColor, collider: true);
        Effects.Shape(PrimitiveType.Cube, Root, new Vector3(longRight - 0.1f, 1.75f, longZ), new Vector3(0.15f, 3.5f, 2.4f), DoorColor, collider: true);

        // Roofs make the tunnels dark.
        Roof(3f, 17f, 3 * Cell, 11 * Cell);
        Roof(9f, 14.5f, 9 * Cell, 2 * Cell);
    }

    void Crate(int x, int z, float offsetX = 0f, float offsetZ = 0f, int stack = 1)
    {
        Vector3 basePosition = CellCenter(x, z) + new Vector3(offsetX, 0f, offsetZ);
        float bottom = 0f;
        for (int i = 0; i < stack; i++)
        {
            float size = i == 0 ? 2f : 1.6f;
            float shade = (random.Next(5) - 2) * 0.03f;
            var color = new Color(CrateColor.r + shade, CrateColor.g + shade, CrateColor.b + shade);
            var crate = Effects.Shape(PrimitiveType.Cube, Root, basePosition + Vector3.up * (bottom + size / 2f),
                                      Vector3.one * size, color, collider: true, euler: new Vector3(0f, i * 14f, 0f));
            // Darker frame bands so the boxes read as wooden crates.
            var band = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f);
            foreach (float y in new[] { -0.45f, 0f, 0.45f })
                Effects.Shape(PrimitiveType.Cube, crate.transform, new Vector3(0f, y, 0f), new Vector3(1.02f, 0.08f, 1.02f), band);
            bottom += size;
        }
    }

    void Barrel(int x, int z, Color color)
    {
        Vector3 position = CellCenter(x, z) + Vector3.up * 0.65f;
        Effects.Shape(PrimitiveType.Cylinder, Root, position, new Vector3(0.9f, 0.65f, 0.9f), color, collider: true);
    }

    void Lintel(float x, float z, float sizeX, float sizeZ) =>
        Effects.Shape(PrimitiveType.Cube, Root, CellCenter(x, z) + Vector3.up * 5.5f, new Vector3(sizeX, 3f, sizeZ), WallColor, collider: true);

    void Roof(float x, float z, float sizeX, float sizeZ) =>
        Effects.Shape(PrimitiveType.Cube, Root, CellCenter(x, z) + Vector3.up * 4.75f, new Vector3(sizeX, 0.5f, sizeZ),
                      new Color(0.7f, 0.6f, 0.44f), collider: true);

    void BuildSiteLetters()
    {
        // Painted on the south face of the north wall above each site.
        float face = CellCenter(0f, 30f).z - Cell / 2f - 0.015f;
        const float height = 3f;
        Paint(LetterA, new Vector3(CellCenter(25f, 0f).x - 0.3f * height, 2.2f, face), height);
        Paint(LetterB, new Vector3(CellCenter(5.5f, 0f).x - 0.3f * height, 2.2f, face), height);
    }

    void Paint(float[,] strokes, Vector3 origin, float height)
    {
        Vector3 intoWall = Vector3.forward;
        float thickness = 0.12f * height;
        for (int i = 0; i < strokes.GetLength(0); i++)
        {
            Vector3 a = origin + new Vector3(strokes[i, 0], strokes[i, 1], 0f) * height;
            Vector3 b = origin + new Vector3(strokes[i, 2], strokes[i, 3], 0f) * height;
            Vector3 along = b - a;
            var stroke = Effects.Shape(PrimitiveType.Cube, Root, (a + b) * 0.5f,
                                       new Vector3(along.magnitude + thickness, thickness, 0.04f), PaintColor);
            stroke.transform.rotation = Quaternion.LookRotation(intoWall, Vector3.Cross(intoWall, along.normalized));
        }
    }

    void DefinePoints()
    {
        AddPoint("TSpawn", 15f, 3f);
        AddPoint("CTSpawn", 15f, 28f);
        AddPoint("OutsideLong", 24f, 4f);
        AddPoint("LongDoors", 27.5f, 7.5f);
        AddPoint("Long", 27.5f, 16f);
        AddPoint("LongCorner", 27.5f, 22f);
        AddPoint("ASite", 25f, 27f);
        AddPoint("AHoldLong", 24f, 28.5f);
        AddPoint("AHoldShort", 22.5f, 27f);
        AddPoint("Short", 20.5f, 21f);
        AddPoint("Catwalk", 18.5f, 17.5f);
        AddPoint("Mid", 15f, 14f);
        AddPoint("MidDoors", 15f, 24f);
        AddPoint("CTMid", 15f, 27f);
        AddPoint("CTtoA", 20f, 27f);
        AddPoint("BDoors", 10f, 26.5f);
        AddPoint("BSite", 5f, 26f);
        AddPoint("BHold", 7f, 27.5f);
        AddPoint("BHold2", 8.5f, 25.5f);
        AddPoint("Tunnels", 3f, 18f);
        AddPoint("TunnelExit", 3f, 22f);
        AddPoint("TunnelHall", 5f, 10f);
        AddPoint("OutsideTunnels", 7.5f, 5f);
        AddPoint("LowerTunnels", 9f, 14.5f);

        int[,] tSpawns = { { 11, 3 }, { 13, 3 }, { 15, 3 }, { 17, 3 }, { 19, 3 }, { 12, 5 }, { 14, 5 }, { 16, 5 }, { 18, 5 }, { 20, 5 } };
        int[,] swatSpawns = { { 12, 28 }, { 14, 28 }, { 16, 28 }, { 18, 28 }, { 13, 26 }, { 15, 26 }, { 17, 26 }, { 19, 26 }, { 12, 30 }, { 15, 30 } };
        for (int i = 0; i < tSpawns.GetLength(0); i++) TerroristSpawns.Add(CellCenter(tSpawns[i, 0], tSpawns[i, 1]));
        for (int i = 0; i < swatSpawns.GetLength(0); i++) SwatSpawns.Add(CellCenter(swatSpawns[i, 0], swatSpawns[i, 1]));
    }

    void AddPoint(string name, float x, float z)
    {
        points[name] = CellCenter(x, z);
        KeyPoints.Add(points[name]);
    }

    void BuildRadar()
    {
        Radar = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
            {
                Color color;
                switch (grid[x, z])
                {
                    case Area.Wall: color = new Color(0f, 0f, 0f, 0f); break;
                    case Area.SiteA:
                    case Area.SiteB: color = new Color(0.85f, 0.45f, 0.3f, 0.85f); break;
                    case Area.TSpawn: color = new Color(0.9f, 0.75f, 0.45f, 0.85f); break;
                    case Area.SwatSpawn: color = new Color(0.55f, 0.7f, 0.9f, 0.85f); break;
                    default: color = new Color(0.8f, 0.72f, 0.55f, 0.85f); break;
                }
                Radar.SetPixel(x, z, color);
            }
        Radar.Apply();
    }

    void BakeNavMesh()
    {
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(Root, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var settings = NavMesh.GetSettingsByID(0);
        var bounds = new Bounds(Vector3.zero, new Vector3(Size * Cell + 10f, 40f, Size * Cell + 10f));
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        NavMesh.AddNavMeshData(data);
    }
}
