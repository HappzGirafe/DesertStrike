using UnityEngine;

/// <summary>
/// Stops drawing characters that are hidden behind walls. Right before the main camera draws each frame
/// (<see cref="Camera.onPreCull"/>), every body inside the view is tested with a few line casts from the
/// camera to its head, chest, shoulders, hips, feet and gun. Bodies that no line reaches are switched off
/// for that frame (renderers and shadows); the moment any line gets through they are drawn again — in the
/// same frame, because the test runs before anything is drawn. Bodies outside the view are left to Unity's
/// own frustum culling.
/// </summary>
public class VisibilityCuller : MonoBehaviour
{
    static readonly Vector3[] StandingPoints =
    {
        new Vector3(0f, 0.95f, 0f),       // head (fractions of the body height for y)
        new Vector3(0f, 0.7f, 0f),        // chest
        new Vector3(-0.42f, 0.72f, 0f),   // shoulders and arms
        new Vector3(0.42f, 0.72f, 0f),
        new Vector3(-0.3f, 0.35f, 0f),    // hips
        new Vector3(0.3f, 0.35f, 0f),
        new Vector3(0f, 0.08f, 0f),       // feet
    };

    // A dead body lies on its back, along its local -Z.
    static readonly Vector3[] LyingPoints =
    {
        new Vector3(0f, 0.25f, 0f),
        new Vector3(0f, 0.25f, -0.85f),
        new Vector3(0f, 0.25f, -1.65f),
        new Vector3(-0.35f, 0.25f, -1.1f),
        new Vector3(0.35f, 0.25f, -1.1f),
    };

    /// <summary>Lets everything draw (for comparing; set by the -ds-nocull command-line option).</summary>
    public static bool Disabled;

    /// <summary>Bodies inside the view in the last frame, and how many of them were drawn (for -ds-perf).</summary>
    public static int InView, Drawn;

    readonly Plane[] frustum = new Plane[6];

    void OnEnable() => Camera.onPreCull += Cull;

    void OnDisable()
    {
        Camera.onPreCull -= Cull;
        foreach (var body in CharacterBody.All) body.SetCulled(false);
    }

    void Cull(Camera cam)
    {
        var gm = GameManager.Instance;
        if (gm == null || cam != gm.MainCamera) return;

        GeometryUtility.CalculateFrustumPlanes(cam, frustum);
        Vector3 eye = cam.transform.position;
        InView = Drawn = 0;
        foreach (var body in CharacterBody.All)
        {
            // Outside the view nothing is drawn anyway; skipping the lines here is free.
            if (!GeometryUtility.TestPlanesAABB(frustum, body.ViewBounds)) { body.SetCulled(!Disabled); continue; }
            InView++;
            bool visible = Disabled || CanSee(eye, body);
            body.SetCulled(!visible);
            if (visible) Drawn++;
        }
    }

    static bool CanSee(Vector3 eye, CharacterBody body)
    {
        var t = body.transform;
        if (body.IsLying)
        {
            foreach (var point in LyingPoints)
                if (Ballistics.HasLineOfSight(eye, t.TransformPoint(point))) return true;
            return false;
        }

        float height = body.Self.Height;
        foreach (var point in StandingPoints)
            if (Ballistics.HasLineOfSight(eye, t.TransformPoint(new Vector3(point.x, point.y * height, point.z)))) return true;
        return Ballistics.HasLineOfSight(eye, body.GunTip);
    }
}
