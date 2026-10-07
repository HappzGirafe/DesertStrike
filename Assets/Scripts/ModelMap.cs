using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// A map made in Blender: Resources/Maps/&lt;id&gt;/model.fbx with parts.json (exported by Tools/export_map_fbx.py).
///
/// The model marks its areas by object name: TSpawn_Tiles and CTSpawn_Tiles are the Terrorist and SWAT spawns (and
/// buy zones), Site_Tiles covers both bombsites, and the painted letters Decal_A and Decal_B tell which site is which.
/// From those the map places the spawn points, bakes the bots' NavMesh, works out their routes and posts, and
/// photographs itself from above for the radar. Markings, leaves, webs and glowing bits get no collider (bullets
/// and players pass through them); everything else is solid.
/// </summary>
public class ModelMap : GameMap
{
    const int SpawnsPerTeam = 10;
    const int RadarPixels = 256;

    readonly string folder;
    Bounds bounds;
    readonly List<Vector3> terroristTiles = new List<Vector3>();   // spawn tiles' triangles (3 corners each), world space
    readonly List<Vector3> swatTiles = new List<Vector3>();
    readonly List<Vector3> siteTilesA = new List<Vector3>(), siteTilesB = new List<Vector3>();   // the sites' floors
    Vector3 terroristSpawn, swatSpawn, siteA, siteB, middle, approachA, approachB, watchA, watchB;
    List<List<Vector3>> routesA, routesB;
    List<(List<Vector3> Route, Vector3 LookAt)> swatPosts;

    public ModelMap(string id)
    {
        folder = "Maps/" + id;
    }

    public override Vector3 SitePoint(bool a) => a ? siteA : siteB;
    public override List<List<Vector3>> TerroristRoutes(bool a) => a ? routesA : routesB;
    public override Vector3 TerroristWatch(bool a) => a ? watchA : watchB;
    public override List<(List<Vector3> Route, Vector3 LookAt)> SwatPosts() => swatPosts;

    public override void Build(Transform parent)
    {
        Root = new GameObject("Map " + Id).transform;
        Root.SetParent(parent, false);
        var prefab = Resources.Load<GameObject>(folder + "/model");
        if (prefab == null)
        {
            Debug.LogError($"[Map] {folder}/model is missing; using a plain floor.");
            Effects.Shape(PrimitiveType.Cube, Root, new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f), new Color(0.5f, 0.5f, 0.45f), collider: true);
        }
        else
        {
            var model = Object.Instantiate(prefab, Root, false).transform;
            model.name = "Model";
            // Blender's top view, north up: the FBX export turns the map half round, so turn it back
            // (then the painted letters read the right way up on the radar).
            model.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;
            Dress(model);
            // Centre the map on the origin (the menu camera circles around it), keeping its floor height.
            model.position += new Vector3(-bounds.center.x, 0f, -bounds.center.z);
            bounds.center = new Vector3(0f, bounds.center.y, 0f);
            Physics.SyncTransforms();
            ReadMarkings(model);
        }
        if (bounds.size == Vector3.zero) bounds = new Bounds(new Vector3(0f, 5f, 0f), new Vector3(100f, 10f, 100f));
        if (terroristTiles.Count == 0) MarkFallbackAreas();
        if (SiteA.size == Vector3.zero) MarkFallbackSites();

        BakeNavMesh(new Bounds(bounds.center, bounds.size + new Vector3(10f, 20f, 10f)));
        PlaceSpawns(terroristTiles, TerroristSpawns);
        PlaceSpawns(swatTiles, SwatSpawns);
        PlanRounds();
        BuildRadar();
        StaticBatchingUtility.Combine(Root.gameObject);
        Debug.Log($"[Map] {Name}: {bounds.size.x:0} x {bounds.size.z:0} m, spawns {TerroristSpawns.Count} T / {SwatSpawns.Count} SWAT");
    }

    // ----------------------------------------------------------------- look and collision

    void Dress(Transform model)
    {
        var looks = PartLooks.Load(folder + "/parts");
        bool first = true;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
        {
            string part = filter.gameObject.name;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            Material material = looks.TryGetValue(part, out var look) ? look.CreateMaterial(folder) : null;
            renderer.sharedMaterial = material != null ? material : Effects.Mat(new Color(0.5f, 0.5f, 0.5f));
            bool marking = IsMarking(part);
            renderer.shadowCastingMode = marking ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (!marking && IsSolid(part)) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;

            if (first) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            first = false;
        }
    }

    static bool IsMarking(string part) => part.StartsWith("Decal_") || part.EndsWith("_Tiles");

    // See-through things, and the houses' trim (Tools/remake_houses.py): boards a few centimetres off the walls
    // would only narrow the gaps between houses that people and bots walk through.
    static bool IsSolid(string part)
    {
        foreach (string see in new[] { "Leaves", "Web", "Glow", "Ghost", "Water", "Trim", "Shutter", "Ridge", "Chimney_Cap" })
            if (part.Contains(see)) return false;
        return true;
    }

    // ----------------------------------------------------------------- spawns, buy zones and bombsites

    void ReadMarkings(Transform model)
    {
        AddTriangles(Find(model, "TSpawn_Tiles"), terroristTiles);
        AddTriangles(Find(model, "CTSpawn_Tiles"), swatTiles);
        if (terroristTiles.Count == 0 || swatTiles.Count == 0)
        {
            Debug.LogWarning($"[Map] {Name} has no TSpawn_Tiles / CTSpawn_Tiles; using the map's ends as spawns.");
            terroristTiles.Clear();
            swatTiles.Clear();
        }
        else
        {
            TerroristBuyZone = Zone(BoundsOf(terroristTiles), 1f);
            SwatBuyZone = Zone(BoundsOf(swatTiles), 1f);
        }

        // Site_Tiles holds both sites: each triangle belongs to the site whose painted letter is nearer.
        var tiles = new List<Vector3>();
        AddTriangles(Find(model, "Site_Tiles"), tiles);
        var letterA = Find(model, "Decal_A");
        var letterB = Find(model, "Decal_B");
        if (tiles.Count == 0 || letterA == null || letterB == null)
        {
            Debug.LogWarning($"[Map] {Name} has no Site_Tiles / Decal_A / Decal_B; using the map's sides as bombsites.");
            MarkFallbackSites();
            return;
        }
        Vector3 a = letterA.GetComponent<Renderer>().bounds.center, b = letterB.GetComponent<Renderer>().bounds.center;
        var nearA = new List<Vector3>();
        var nearB = new List<Vector3>();
        for (int i = 0; i + 2 < tiles.Count; i += 3)
        {
            Vector3 middleOf = (tiles[i] + tiles[i + 1] + tiles[i + 2]) / 3f;
            var into = FlatDistance(middleOf, a) <= FlatDistance(middleOf, b) ? nearA : nearB;
            into.Add(tiles[i]);
            into.Add(tiles[i + 1]);
            into.Add(tiles[i + 2]);
        }
        SiteA = Zone(nearA.Count > 0 ? BoundsOf(nearA) : new Bounds(a, Vector3.one * 12f), 0f);
        SiteB = Zone(nearB.Count > 0 ? BoundsOf(nearB) : new Bounds(b, Vector3.one * 12f), 0f);
        siteTilesA.AddRange(nearA);
        siteTilesB.AddRange(nearB);
    }

    // A map without bombsite markings: A on the west side, B on the east side.
    void MarkFallbackSites()
    {
        SiteA = Zone(new Bounds(new Vector3(bounds.min.x + 12f, bounds.min.y, bounds.center.z), new Vector3(16f, 0f, 16f)), 0f);
        SiteB = Zone(new Bounds(new Vector3(bounds.max.x - 12f, bounds.min.y, bounds.center.z), new Vector3(16f, 0f, 16f)), 0f);
    }

    // A map without spawn markings: Terrorists at the south end, SWAT at the north end.
    void MarkFallbackAreas()
    {
        float y = bounds.min.y;
        Vector3 Corner(float x, float z) => new Vector3(x, y, z);
        float south = bounds.min.z + 10f, north = bounds.max.z - 10f;
        foreach (var (list, z) in new[] { (terroristTiles, south), (swatTiles, north) })
        {
            list.Add(Corner(-8f, z - 5f)); list.Add(Corner(8f, z - 5f)); list.Add(Corner(8f, z + 5f));
            list.Add(Corner(-8f, z - 5f)); list.Add(Corner(8f, z + 5f)); list.Add(Corner(-8f, z + 5f));
        }
        TerroristBuyZone = Zone(BoundsOf(terroristTiles), 1f);
        SwatBuyZone = Zone(BoundsOf(swatTiles), 1f);
    }

    /// <summary>Up to ten spread-out places on the spawn tiles with room to stand and a NavMesh underneath.</summary>
    void PlaceSpawns(List<Vector3> tiles, List<Vector3> spawns)
    {
        var area = BoundsOf(tiles);
        var candidates = new List<Vector3>();
        for (float x = area.min.x + 0.9f; x <= area.max.x - 0.9f; x += 1.6f)
            for (float z = area.min.z + 0.9f; z <= area.max.z - 0.9f; z += 1.6f)
            {
                var point = new Vector3(x, area.center.y, z);
                if (!InsideTriangles(tiles, point)) continue;
                if (!NavMesh.SamplePosition(point, out var hit, 0.75f, NavMesh.AllAreas)) continue;
                if (Physics.CheckCapsule(hit.position + Vector3.up * 0.45f, hit.position + Vector3.up * 1.6f, 0.4f,
                                         Ballistics.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;
                candidates.Add(hit.position);
            }
        if (candidates.Count == 0) candidates.Add(Snap(area.center, 10f));

        // Start next to the middle of the area, then keep taking the place farthest from those already taken.
        Vector3 centre = area.center;
        int best = 0;
        for (int i = 1; i < candidates.Count; i++)
            if (FlatDistance(candidates[i], centre) < FlatDistance(candidates[best], centre)) best = i;
        spawns.Add(candidates[best]);
        candidates.RemoveAt(best);
        while (spawns.Count < SpawnsPerTeam && candidates.Count > 0)
        {
            int farthest = 0;
            float farthestDistance = -1f;
            for (int i = 0; i < candidates.Count; i++)
            {
                float nearest = float.MaxValue;
                foreach (var taken in spawns) nearest = Mathf.Min(nearest, FlatDistance(candidates[i], taken));
                if (nearest > farthestDistance) { farthestDistance = nearest; farthest = i; }
            }
            spawns.Add(candidates[farthest]);
            candidates.RemoveAt(farthest);
        }
    }

    // ----------------------------------------------------------------- the bots' round plans

    void PlanRounds()
    {
        terroristSpawn = Snap(BoundsOf(terroristTiles).center, 10f);
        swatSpawn = Snap(BoundsOf(swatTiles).center, 10f);
        // On the site's floor, reachable from the spawn (so a site under a roof is not snapped onto the roof).
        siteA = SnapReachable(SiteMiddle(siteTilesA, SiteA), 10f, terroristSpawn);
        siteB = SnapReachable(SiteMiddle(siteTilesB, SiteB), 10f, terroristSpawn);
        middle = SnapReachable(Vector3.Lerp(terroristSpawn, swatSpawn, 0.5f), 15f, swatSpawn);
        TerroristSpawnYaw = YawTowards(terroristSpawn, bounds.center);
        SwatSpawnYaw = YawTowards(swatSpawn, bounds.center);

        approachA = PointBeforeEnd(terroristSpawn, siteA, 12f);
        approachB = PointBeforeEnd(terroristSpawn, siteB, 12f);
        routesA = new List<List<Vector3>> { Route(terroristSpawn, siteA, 1f), Route(terroristSpawn, siteA, -1f) };
        routesB = new List<List<Vector3>> { Route(terroristSpawn, siteB, 1f), Route(terroristSpawn, siteB, -1f) };
        watchA = SnapReachable(Vector3.Lerp(siteA, swatSpawn, 0.35f), 8f, siteA);
        watchB = SnapReachable(Vector3.Lerp(siteB, swatSpawn, 0.35f), 8f, siteB);

        swatPosts = new List<(List<Vector3>, Vector3)>
        {
            (new List<Vector3> { Hold(siteA, approachA, 0f) }, approachA),
            (new List<Vector3> { Hold(siteB, approachB, 0f) }, approachB),
            (new List<Vector3> { middle }, SnapReachable(Vector3.Lerp(middle, terroristSpawn, 0.5f), 10f, middle)),
            (new List<Vector3> { Hold(siteA, approachA, 1f) }, approachA),
            (new List<Vector3> { Hold(siteB, approachB, -1f) }, approachB),
        };

        Debug.Log($"[Map] {Name} walks: T spawn to A {Walk(terroristSpawn, siteA)}, to B {Walk(terroristSpawn, siteB)}; " +
                  $"SWAT spawn to A {Walk(swatSpawn, siteA)}, to B {Walk(swatSpawn, siteB)}; site A at {siteA}, B at {siteB} " +
                  $"(zones {SiteA.center}, {SiteB.center}), T spawn {terroristSpawn}, SWAT spawn {swatSpawn}");
        KeyPoints.AddRange(new[] { terroristSpawn, swatSpawn, siteA, siteB, middle, approachA, approachB });
        foreach (var route in routesA) KeyPoints.Add(route[0]);
        foreach (var route in routesB) KeyPoints.Add(route[0]);
        var random = new System.Random(Id.GetHashCode());
        for (int tries = 0; tries < 40 && KeyPoints.Count < 22; tries++)
        {
            var point = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)random.NextDouble()), bounds.min.y,
                                    Mathf.Lerp(bounds.min.z, bounds.max.z, (float)random.NextDouble()));
            if (NavMesh.SamplePosition(point, out var hit, 3f, NavMesh.AllAreas) && Reachable(swatSpawn, hit.position))
                KeyPoints.Add(hit.position);
        }
    }

    /// <summary>
    /// The site under a position: only on its painted floor (with a metre to spare round the edges), not anywhere in
    /// the box round it, which on an odd-shaped site reaches far past the tiles.
    /// </summary>
    public override string SiteAt(Vector3 position) =>
        OnTiles(siteTilesA, SiteA, position) ? "A" : OnTiles(siteTilesB, SiteB, position) ? "B" : null;

    static bool OnTiles(List<Vector3> tiles, Bounds zone, Vector3 p)
    {
        if (!zone.Contains(p)) return false;
        if (tiles.Count == 0) return true;   // a site marked without tiles: its box
        for (int i = 0; i + 2 < tiles.Count; i += 3)
            if (NearTriangle(p, tiles[i], tiles[i + 1], tiles[i + 2], 1f)) return true;
        return false;
    }

    // Seen from above: inside the triangle, or within `margin` of one of its edges.
    static bool NearTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, float margin)
    {
        float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
        bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
        if (!(negative && positive)) return true;
        return EdgeDistance(p, a, b) <= margin || EdgeDistance(p, b, c) <= margin || EdgeDistance(p, c, a) <= margin;
    }

    static float Side(Vector3 p, Vector3 a, Vector3 b) => (p.x - b.x) * (a.z - b.z) - (a.x - b.x) * (p.z - b.z);

    static float EdgeDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = new Vector2(b.x - a.x, b.z - a.z);
        var ap = new Vector2(p.x - a.x, p.z - a.z);
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
        return (ap - ab * t).magnitude;
    }

    /// <summary>A point on the site's floor near the middle of its box (the box's own middle can miss an odd-shaped
    /// site): the middle of the tile triangle closest to it.</summary>
    static Vector3 SiteMiddle(List<Vector3> tiles, Bounds zone)
    {
        var middle = new Vector3(zone.center.x, zone.min.y + 1f, zone.center.z);   // zones start 1 m below the floor
        if (tiles.Count < 3) return middle;
        Vector3 best = middle;
        float bestDistance = float.MaxValue;
        for (int i = 0; i + 2 < tiles.Count; i += 3)
        {
            Vector3 centre = (tiles[i] + tiles[i + 1] + tiles[i + 2]) / 3f;
            float distance = FlatDistance(centre, middle);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = centre;
            }
        }
        return best;
    }

    /// <summary>To the site, past a point to one side of the straight line (two sides = two ways in).</summary>
    List<Vector3> Route(Vector3 from, Vector3 to, float side)
    {
        Vector3 along = to - from;
        along.y = 0f;
        Vector3 across = new Vector3(-along.z, 0f, along.x).normalized;
        Vector3 via = SnapReachable(Vector3.Lerp(from, to, 0.5f) + across * side * along.magnitude * 0.3f, 10f, from);
        return Reachable(via, to) && FlatDistance(via, to) > 6f ? new List<Vector3> { via, to } : new List<Vector3> { to };
    }

    /// <summary>A defending spot just behind the site, seen from where the Terrorists come in.</summary>
    Vector3 Hold(Vector3 site, Vector3 approach, float side)
    {
        Vector3 back = site - approach;
        back.y = 0f;
        back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.forward;
        Vector3 across = new Vector3(-back.z, 0f, back.x);
        return SnapReachable(site + back * 3f + across * side * 4f, 4f, swatSpawn);
    }

    /// <summary>The point on the walking path from <paramref name="from"/> that lies this far before the end.</summary>
    static Vector3 PointBeforeEnd(Vector3 from, Vector3 to, float distance)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.corners.Length < 2)
            return Vector3.Lerp(to, from, 0.3f);
        var corners = path.corners;
        for (int i = corners.Length - 1; i > 0; i--)
        {
            float segment = Vector3.Distance(corners[i], corners[i - 1]);
            if (segment >= distance) return Vector3.MoveTowards(corners[i], corners[i - 1], distance);
            distance -= segment;
        }
        return corners[0];
    }

    // ----------------------------------------------------------------- radar

    /// <summary>A picture of the map from straight above (north up), without fog or shadows.</summary>
    void BuildRadar()
    {
        radarCenter = new Vector3(bounds.center.x, 0f, bounds.center.z);
        radarSize = Mathf.Max(bounds.size.x, bounds.size.z) * 1.02f;

        var camera = new GameObject("Radar Camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = radarSize / 2f;
        camera.aspect = 1f;
        camera.transform.SetPositionAndRotation(new Vector3(radarCenter.x, bounds.max.y + 20f, radarCenter.z), Quaternion.Euler(90f, 0f, 0f));
        camera.nearClipPlane = 0.5f;
        camera.farClipPlane = bounds.size.y + 60f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = ~(1 << Ballistics.CharacterLayer);
        camera.allowHDR = false;
        camera.allowMSAA = false;

        var target = RenderTexture.GetTemporary(RadarPixels, RadarPixels, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        bool fog = RenderSettings.fog;
        float shadowDistance = QualitySettings.shadowDistance;
        RenderSettings.fog = false;
        QualitySettings.shadowDistance = 0f;
        // Roofs over rooms people walk in (named "Ceiling", like site B's on Industrial) are left out, so the
        // radar shows the rooms.
        var ceilings = new List<Renderer>();
        foreach (var renderer in Root.GetComponentsInChildren<Renderer>())
            if (renderer.enabled && renderer.name.Contains("Ceiling"))
            {
                renderer.enabled = false;
                ceilings.Add(renderer);
            }
        camera.Render();
        foreach (var renderer in ceilings) renderer.enabled = true;
        RenderSettings.fog = fog;
        QualitySettings.shadowDistance = shadowDistance;

        var previous = RenderTexture.active;
        RenderTexture.active = target;
        Radar = new Texture2D(RadarPixels, RadarPixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Radar.ReadPixels(new Rect(0, 0, RadarPixels, RadarPixels), 0, 0);
        RenderTexture.active = previous;
        camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(target);
        Object.Destroy(camera.gameObject);

        // Night maps come out dark: brighten them so the radar stays readable, and make it a little see-through.
        var pixels = Radar.GetPixels32();
        float light = 0f;
        int count = 0;
        foreach (var p in pixels)
            if (p.a > 0) { light += (p.r + p.g + p.b) / (3f * 255f); count++; }
        float boost = count > 0 ? Mathf.Clamp(0.42f / Mathf.Max(0.01f, light / count), 1f, 3f) : 1f;
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            pixels[i] = p.a == 0
                ? new Color32(0, 0, 0, 0)
                : new Color32((byte)Mathf.Min(255f, p.r * boost), (byte)Mathf.Min(255f, p.g * boost), (byte)Mathf.Min(255f, p.b * boost), 220);
        }
        Radar.SetPixels32(pixels);
        Radar.Apply();
    }

    // ----------------------------------------------------------------- helpers

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void AddTriangles(Transform part, List<Vector3> into)
    {
        var filter = part != null ? part.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return;
        var mesh = filter.sharedMesh;
        var vertices = mesh.vertices;
        var matrix = part.localToWorldMatrix;
        foreach (int index in mesh.triangles) into.Add(matrix.MultiplyPoint3x4(vertices[index]));
    }

    static Bounds BoundsOf(List<Vector3> points)
    {
        var result = new Bounds(points.Count > 0 ? points[0] : Vector3.zero, Vector3.zero);
        foreach (var p in points) result.Encapsulate(p);
        return result;
    }

    static bool InsideTriangles(List<Vector3> triangles, Vector3 p)
    {
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Vector2 a = Flat(triangles[i]), b = Flat(triangles[i + 1]), c = Flat(triangles[i + 2]), q = Flat(p);
            if (Mathf.Abs(Cross(a, b, c)) < 1e-6f) continue;   // a triangle seen edge-on from above has no inside
            float d1 = Cross(q, a, b), d2 = Cross(q, b, c), d3 = Cross(q, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
            if (!(negative && positive)) return true;
        }
        return false;
    }

    static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(Flat(a), Flat(b));
    static float YawTowards(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

    static Vector3 Snap(Vector3 point, float radius) =>
        NavMesh.SamplePosition(point, out var hit, radius, NavMesh.AllAreas) ? hit.position : point;

    /// <summary>The nearest NavMesh point that can be walked to from <paramref name="from"/> (not a roof).</summary>
    static Vector3 SnapReachable(Vector3 point, float radius, Vector3 from)
    {
        for (float r = 2f; r <= radius * 2f; r *= 2f)
            if (NavMesh.SamplePosition(point, out var hit, r, NavMesh.AllAreas) && Reachable(from, hit.position)) return hit.position;
        return Snap(point, radius);
    }

    /// <summary>How far it is to walk from one point to another ("no way" when the NavMesh does not join them).</summary>
    static string Walk(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return "no way";
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return $"{length:0} m";
    }

    static bool Reachable(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }
}
