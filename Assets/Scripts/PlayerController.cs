using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// First-person controls: movement, mouse look, shooting, recoil, scope, the view model, and the bomb
/// (E to plant or defuse, G to drop it). Input comes from <see cref="GameInput"/> (keyboard and mouse, or the
/// touch controls on a phone). In a LAN game as a client the shots and actions go to the host.
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(Combatant))]
public class PlayerController : MonoBehaviour, ICombatantController
{
    public static float MouseSensitivity = 2f;

    const float Gravity = -20f;
    const float JumpSpeed = 6.6f;
    const float StandHeight = Combatant.StandHeight;
    const float CrouchHeight = Combatant.CrouchHeight;
    const float NormalFov = 75f;
    static readonly Vector3 ViewModelOffset = new Vector3(0.2f, -0.2f, 0.52f);

    public Combatant Self { get; private set; }
    public bool IsScoped { get; private set; }
    public float CurrentSpread { get; private set; }
    public bool UseHeld { get; private set; }   // E: plant or defuse
    public int Jumps { get; private set; }      // how many times the player has jumped (the tutorial asks for one)

    CharacterController controller;
    Transform head;
    Transform viewModel;
    Camera cam;
    Vector3 velocity;
    float yaw, pitch, recoil, sprayHeat, kick;
    float lastShotTime = -1f, stepTimer, rescopeAt = -1f;
    bool crouching;

    public void Setup(Camera camera)
    {
        Self = GetComponent<Combatant>();
        controller = GetComponent<CharacterController>();
        controller.radius = 0.35f;
        controller.stepOffset = 0.45f;
        controller.slopeLimit = 50f;

        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        Self.Eye = head;
        viewModel = new GameObject("ViewModel").transform;
        viewModel.SetParent(head, false);
        SetHeight(StandHeight);

        // Lets bots path around the player instead of walking through them.
        var obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = 0.35f;
        obstacle.height = StandHeight;
        obstacle.center = new Vector3(0f, StandHeight / 2f, 0f);

        cam = camera;
        Self.WeaponChanged += RefreshViewModel;
        Self.Died += OnDied;
        AttachCamera();
    }

    public void Respawn(Vector3 position, float facingYaw)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, facingYaw, 0f));
        controller.enabled = true;
        yaw = facingYaw;
        pitch = recoil = sprayHeat = kick = 0f;
        velocity = Vector3.zero;
        IsScoped = false;
        rescopeAt = -1f;
        crouching = false;
        TouchControls.StandUp();
        SetHeight(StandHeight);
        AttachCamera();
    }

    void AttachCamera()
    {
        cam.transform.SetParent(head, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        cam.fieldOfView = NormalFov;
        viewModel.gameObject.SetActive(true);
    }

    void OnDied(Combatant killer)
    {
        controller.enabled = false;
        IsScoped = false;
        UseHeld = false;
        viewModel.gameObject.SetActive(false);
        cam.transform.SetParent(null, true);
    }

    /// <summary>Joined a LAN game during a round: spectate until the next one.</summary>
    public void ForceDead() => OnDied(null);

    void Update()
    {
        UseHeld = false;
        if (shownSkins != WeaponSkins.Version && Self.Current != null) BuildViewModel();   // a skin came from another game
        if (!Self.IsAlive) return;
        var gm = GameManager.Instance;
        if (gm.IsPaused) return;

        bool inRound = gm.State == MatchState.Freeze || gm.State == MatchState.Live || gm.State == MatchState.RoundEnd;
        bool hasControl = inRound && !gm.BuyMenuOpen && !gm.ConsoleOpen;
        bool canAct = hasControl && gm.State != MatchState.Freeze;

        if (hasControl) Look();
        ApplyView();
        if (canAct) HandleBomb(gm);
        // Planting or defusing keeps you in place (looking around is fine).
        Move(canAct && gm.Bomb.User != Self);
        if (canAct) HandleWeapons();
        CurrentSpread = ComputeSpread(Self.Current);
        Recover();
        UpdateViewModel();
    }

    // -ds-scope: a scoped weapon stays scoped (screenshots of the scope).
    static readonly bool ForceScope = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ds-scope") >= 0;

    void HandleBomb(GameManager gm)
    {
        UseHeld = GameInput.UseHeld;
        if (UseHeld && !Self.IsMirror) gm.Bomb.HoldUse(Self);
        if (GameInput.UseDown && !BombJobHere(gm))
        {
            // E next to a gun on the ground: take it (it swaps for the one of that kind carried).
            var gun = gm.Drops.Nearest(Self, WeaponDrops.SwapRadius);
            if (gun != null)
            {
                if (Self.IsMirror) gm.Net.SendAction(NetAction.PickUp, gun.Id);
                else gm.Drops.PickUp(Self, gun.Id);
            }
        }
        if (GameInput.DropDown)
        {
            // G drops the gun in hand; with the knife in hand, the bomb.
            var current = Self.Current;
            if (current == null || current.Data.Slot == WeaponSlot.Knife)
            {
                if (Self.IsMirror) gm.Net.SendAction(NetAction.DropBomb, 0);
                else gm.Bomb.Drop(Self);
            }
            else if (Self.IsMirror) gm.Net.SendAction(NetAction.DropWeapon, (int)current.Data.Slot);
            else gm.Drops.Drop(Self, current.Data.Slot);
        }
    }

    // E plants or defuses here, so it does not take a gun lying on the site at the same time.
    bool BombJobHere(GameManager gm)
    {
        var bomb = gm.Bomb;
        return (bomb.State == BombState.Carried && bomb.Carrier == Self && gm.Map.SiteAt(transform.position) != null)
               || (bomb.State == BombState.Planted && Self.Team == Team.Swat
                   && Vector3.Distance(transform.position, bomb.Position) < BombManager.DefuseRadius);
    }

    void Look()
    {
        float sensitivity = MouseSensitivity * (IsScoped ? 0.35f : 1f);
        Vector2 look = GameInput.Look(sensitivity);
        yaw += look.x;
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        // Aimbot (Cheats): the view swings onto the head of the nearest enemy in sight.
        if (Cheats.AimbotOn && Cheats.AimbotTarget(GameManager.Instance, Self, cam.transform.position, cam.transform.forward) is Vector3 head)
        {
            Vector3 to = head - cam.transform.position;
            float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            float t = 1f - Mathf.Exp(-25f * Time.deltaTime);
            yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, t);
            pitch = Mathf.Clamp(Mathf.Lerp(pitch, wantPitch + recoil, t), -85f, 85f);   // the view is pitch - recoil
        }
    }

    void ApplyView()
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Euler(pitch - recoil, 0f, 0f);
        float fov = IsScoped ? Self.Current.Data.ZoomFov : NormalFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-25f * Time.deltaTime));
    }

    void Move(bool canMove)
    {
        float dt = Time.deltaTime;
        Vector2 stick = GameInput.Move;
        Vector3 input = canMove ? new Vector3(stick.x, 0f, stick.y) : Vector3.zero;
        input = Vector3.ClampMagnitude(input, 1f);
        if (Cheats.FlyOn)
        {
            // Fly (Cheats): no gravity, twice as fast; jump goes up, crouch goes down.
            Vector3 flying = transform.rotation * input * Self.Current.Data.MoveSpeed * 2f;
            float up = canMove ? (GameInput.JumpHeld ? 1f : 0f) - (GameInput.CrouchHeld ? 1f : 0f) : 0f;
            velocity = Vector3.Lerp(velocity, new Vector3(flying.x, up * 9f, flying.z), 1f - Mathf.Exp(-8f * dt));
            crouching = false;
            controller.Move(velocity * dt);
            SetHeight(Mathf.MoveTowards(controller.height, StandHeight, 5f * dt));
            return;
        }
        crouching = canMove && GameInput.CrouchHeld;
        bool walking = canMove && GameInput.WalkHeld;

        float speed = Self.Current.Data.MoveSpeed * (crouching ? 0.42f : walking ? 0.52f : 1f) * (IsScoped ? 0.55f : 1f);
        Vector3 wish = transform.rotation * input * speed;
        bool grounded = controller.isGrounded;

        var horizontal = new Vector3(velocity.x, 0f, velocity.z);
        horizontal = Vector3.Lerp(horizontal, wish, 1f - Mathf.Exp(-(grounded ? 14f : 2.5f) * dt));
        if (grounded)
        {
            if (velocity.y < 0f) velocity.y = -2f;
            if (canMove && GameInput.JumpDown)
            {
                velocity.y = JumpSpeed;
                Jumps++;
            }
        }
        velocity.y += Gravity * dt;
        velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);

        var flags = controller.Move(velocity * dt);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
        SetHeight(Mathf.MoveTowards(controller.height, crouching ? CrouchHeight : StandHeight, 5f * dt));

        // Running footsteps are loud: they play for you and alert nearby enemies. Walking (Shift) is silent.
        if (grounded && !walking && !crouching && horizontal.magnitude > 3.5f)
        {
            stepTimer -= dt;
            if (stepTimer <= 0f)
            {
                stepTimer = 0.36f;
                SoundFX.Play(SoundFX.Step, transform.position, 0.35f, Random.Range(0.85f, 1.1f), false);
                GameManager.Instance.ReportNoise(transform.position, Self.Team, 15f);
            }
        }
    }

    void SetHeight(float height)
    {
        controller.height = height;
        controller.center = new Vector3(0f, height / 2f, 0f);
        Self.Height = height;
        head.localPosition = new Vector3(0f, height - 0.15f, 0f);
    }

    void HandleWeapons()
    {
        if (GameInput.SlotDown is WeaponSlot slot) SwitchTo(slot);
        if (GameInput.LastWeaponDown && Self.EquipPrevious()) IsScoped = false;
        int cycle = GameInput.WeaponCycle;
        if (cycle != 0) CycleWeapon(cycle);
        if (GameInput.ReloadDown && Self.StartReload()) IsScoped = false;

        var weapon = Self.Current;
        var data = weapon.Data;
        if (GameInput.AltFireDown)
        {
            if (data.ZoomFov > 0f && !Self.IsReloading)
            {
                IsScoped = !IsScoped;
                rescopeAt = -1f;
            }
            else if (data.SelectFire)
            {
                weapon.AutoMode = !weapon.AutoMode;
                SoundFX.Play(SoundFX.DryFire, transform.position, 0.5f, 1.4f, false);
                if (Self.IsMirror)
                {
                    Self.HoldPrediction();
                    GameManager.Instance.Net.SendAction(NetAction.AutoMode, weapon.AutoMode ? 1 : 0);
                }
            }
        }
        if (ForceScope && data.ZoomFov > 0f && !Self.IsReloading) IsScoped = true;
        Self.Scoped = IsScoped;   // the state this frame's shot is fired in (for "noscope")
        if (rescopeAt > 0f && Time.time >= rescopeAt)
        {
            rescopeAt = -1f;
            IsScoped = data.ZoomFov > 0f && !Self.IsReloading;
        }

        bool trigger = weapon.IsAutomatic ? GameInput.FireHeld : GameInput.FireDown;
        if (trigger)
        {
            if (!data.IsMelee && weapon.Mag == 0)
            {
                if (GameInput.FireDown)
                {
                    SoundFX.Play(SoundFX.DryFire, transform.position, 0.5f, 1f, false);
                    Self.StartReload();
                }
            }
            else if (Self.TryFire(cam.transform.forward, ComputeSpread(weapon)))
            {
                lastShotTime = Time.time;
                recoil += data.Recoil;
                sprayHeat = Mathf.Min(sprayHeat + data.Recoil * 0.5f, 4f);
                kick = 1f;
                if (IsScoped)
                {
                    // Bolt-action: drop out of the scope after each shot and come back once it is chambered.
                    IsScoped = false;
                    rescopeAt = Time.time + data.FireInterval * 0.8f;
                }
            }
        }

        if (!data.IsMelee && weapon.Mag == 0 && weapon.Reserve > 0 && !Self.IsReloading && !GameInput.FireHeld)
            Self.StartReload();
    }

    float ComputeSpread(WeaponInstance weapon)
    {
        var data = weapon.Data;
        if (data.IsMelee) return 0f;
        if (weapon.FirstShotReady) return 0f;   // not fired for 3 s: dead on (the crosshair closes up to show it)
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        float spread = weapon.Spread + data.MoveSpread * Mathf.InverseLerp(1.8f, 5.5f, speed) + sprayHeat;
        if (!controller.isGrounded) spread += 6f;
        if (crouching) spread *= 0.7f;
        // No-scope shots are allowed: a little less accurate than scoped, but standing still they hit.
        if (data.ZoomFov > 0f && !IsScoped) spread += 1.5f;
        return spread;
    }

    void Recover()
    {
        float sinceShot = Time.time - lastShotTime;
        recoil = Mathf.MoveTowards(Mathf.Min(recoil, 12f), 0f, (sinceShot > 0.15f ? 10f : 2f) * Time.deltaTime);
        sprayHeat = Mathf.MoveTowards(sprayHeat, 0f, (sinceShot > 0.2f ? 8f : 0.5f) * Time.deltaTime);
        kick = Mathf.MoveTowards(kick, 0f, 10f * Time.deltaTime);
    }

    void SwitchTo(WeaponSlot slot)
    {
        if (!Self.Equip(slot)) return;
        IsScoped = false;
        rescopeAt = -1f;
    }

    void CycleWeapon(int step)
    {
        int index = (int)Self.Current.Data.Slot;
        for (int i = 1; i <= 3; i++)
        {
            var slot = (WeaponSlot)(((index + step * i) % 3 + 3) % 3);
            if (Self.Get(slot) != null)
            {
                SwitchTo(slot);
                return;
            }
        }
    }

    void RefreshViewModel()
    {
        IsScoped = false;
        BuildViewModel();
    }

    int shownSkins;

    // The gun in the hands, with its own skin: a gun picked up from someone keeps theirs (on a network client the host
    // says which skin each gun has).
    void BuildViewModel()
    {
        shownSkins = WeaponSkins.Version;
        for (int i = viewModel.childCount - 1; i >= 0; i--) Destroy(viewModel.GetChild(i).gameObject);
        if (Self.Current == null) return;
        WeaponModels.Build(Self.Current.Data, viewModel, false, out var muzzle, WeaponSkins.SkinOf(Self, Self.Current));
        Self.Muzzle = muzzle;
    }

    void UpdateViewModel()
    {
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        float bobAmount = controller.isGrounded ? Mathf.Clamp01(speed / 6f) : 0f;
        float t = Time.time * 9f;
        var bob = new Vector3(Mathf.Cos(t) * 0.012f, Mathf.Abs(Mathf.Sin(t)) * 0.015f, 0f) * bobAmount;
        viewModel.localPosition = ViewModelOffset + bob + new Vector3(0f, 0f, -0.06f * kick);
        float reloadDip = Self.IsReloading ? Mathf.Sin(Self.ReloadProgress * Mathf.PI) * 35f : 0f;
        viewModel.localRotation = Quaternion.Euler(-4f * kick + reloadDip, 0f, 0f);
        viewModel.gameObject.SetActive(!IsScoped);
    }
}
