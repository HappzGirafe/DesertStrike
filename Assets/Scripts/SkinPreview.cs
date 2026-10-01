using UnityEngine;

/// <summary>Renders a slowly turning gun into a texture for the inventory screen.</summary>
public class SkinPreview : MonoBehaviour
{
    // Far above the map so the preview camera sees nothing but the gun.
    static readonly Vector3 StagePosition = new Vector3(0f, 500f, 0f);

    public RenderTexture Texture { get; private set; }

    Camera previewCamera;
    Light fill;
    Transform turntable;
    Transform gun;
    WeaponData shownWeapon;
    WeaponSkin shownSkin;

    void Awake()
    {
        Texture = new RenderTexture(768, 480, 24) { antiAliasing = 4 };

        var stage = new GameObject("Skin Preview").transform;
        stage.position = StagePosition;
        turntable = new GameObject("Turntable").transform;
        turntable.SetParent(stage, false);

        var cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(stage, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.09f, -0.6f);
        cameraObject.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.targetTexture = Texture;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.13f, 0.13f, 0.15f);
        previewCamera.fieldOfView = 30f;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 5f;
        previewCamera.enabled = false;

        fill = new GameObject("Preview Light").AddComponent<Light>();
        fill.transform.SetParent(stage, false);
        fill.transform.localPosition = new Vector3(-0.4f, 0.5f, -0.6f);
        fill.type = LightType.Point;
        fill.range = 3f;
        fill.intensity = 1.5f;
    }

    public void Show(WeaponData weapon, WeaponSkin skin)
    {
        previewCamera.enabled = true;
        if (weapon == shownWeapon && skin == shownSkin) return;
        shownWeapon = weapon;
        shownSkin = skin;
        if (gun != null) Destroy(gun.gameObject);

        gun = WeaponModels.Build(weapon, turntable, false, out _, skin);
        // Center the gun on the turntable so it spins in place, and back the camera off so long guns fit.
        var renderers = gun.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        gun.position -= bounds.center - turntable.position;

        float radius = Mathf.Max(0.12f, bounds.extents.magnitude);
        float distance = radius / Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
        previewCamera.transform.localPosition = new Vector3(0f, distance * 0.15f, -distance);
        previewCamera.transform.localRotation = Quaternion.LookRotation(-previewCamera.transform.localPosition);
        previewCamera.farClipPlane = distance * 3f;
        fill.transform.localPosition = new Vector3(-0.6f, 0.8f, -1f) * distance;
        fill.range = distance * 4f;
    }

    public void Hide()
    {
        previewCamera.enabled = false;
        if (gun != null) Destroy(gun.gameObject);
        gun = null;
        shownWeapon = null;
        shownSkin = null;
    }

    void Update()
    {
        if (previewCamera.enabled) turntable.Rotate(0f, 40f * Time.unscaledDeltaTime, 0f, Space.Self);
    }
}
