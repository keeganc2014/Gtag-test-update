using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GorillaLocomotion;

// Builds the whole test scene at runtime: map, lighting, gorilla rig, input and the test menu.
// Nothing needs to be placed in the scene by hand - it starts itself in any scene.
public class GtagBootstrap : MonoBehaviour
{
    public const int PlayerLayer = 2;   // built-in "Ignore Raycast" - keeps the player out of hand raycasts
    public const int MenuLayer = 5;     // built-in "UI"

    public static GtagBootstrap Instance { get; private set; }

    public Player Player { get; private set; }
    public Rigidbody Body { get; private set; }
    public Camera Cam { get; private set; }
    public Transform Head { get { return Cam.transform; } }
    public Transform LeftHand { get; private set; }
    public Transform RightHand { get; private set; }
    public SphereCollider HeadCollider { get; private set; }
    public CapsuleCollider BodyCollider { get; private set; }
    public XRRigInput Rig { get; private set; }
    public Light Sun { get; private set; }
    public Bounds MapBounds { get; private set; }
    public Vector3 SpawnPoint { get; private set; }

    GameObject mapRoot;

    static readonly Dictionary<string, string> TextureAlias = new Dictionary<string, string>
    {
        { "caverockarrow1", "caverockwitharrows" },
        { "caverockarrow2", "caverockwitharrows2" },
        { "clay", "clay3" },
        { "crackedcement", "crackedconcrete" },
        { "darkbrownpanel", "darkerbrownpanel" },
        { "tableclay", "claytable" },
        { "loglowres_1", "loglowres" },
        { "pitground", "ground" },
        { "riverbed", "ground" },
        { "riverwall", "rockwall" },
        { "pitwall", "rockwall" },
        { "minewall", "rockwall" },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (Object.FindObjectOfType<GtagBootstrap>() != null) return;
        new GameObject("GtagBootstrap").AddComponent<GtagBootstrap>();
    }

    void Awake()
    {
        Instance = this;
    }

    IEnumerator Start()
    {
        BuildWorld();
        yield return new WaitForFixedUpdate();   // let physics register the freshly created map colliders
        Physics.SyncTransforms();

        PlayerProfile.Load();
        SpawnPoint = FindStump();   // you start in the stump (tree room with the computer), like the real game
        BuildPlayer(SpawnPoint);
        gameObject.AddComponent<GorillaRigAvatar>().Init(this);

        // give XR tracking a moment to deliver real poses, then sync the locomotion state to them
        yield return new WaitForSeconds(0.6f);
        ResyncPlayer();
        Player.enabled = true;

        gameObject.AddComponent<TestMenu>();
        BuildComputer();
    }

    void Update()
    {
        if (Player != null && Player.enabled && Player.transform.position.y < MapBounds.min.y - 30f)
            TeleportToSpawn();
    }

    // ---------------- world ----------------

    void BuildWorld()
    {
        var lightGo = new GameObject("Sun");
        Sun = lightGo.AddComponent<Light>();
        Sun.type = LightType.Directional;
        Sun.intensity = 1f;
        Sun.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);

        GameObject prefab = Resources.Load<GameObject>("Map/GorillaTagMap");
        if (prefab != null)
        {
            mapRoot = Instantiate(prefab);
            mapRoot.name = "GorillaTagMap";
            PrepareMap(mapRoot);
        }
        else
        {
            Debug.LogWarning("[GtagTest] Assets/Resources/Map/GorillaTagMap.fbx not found - using a flat floor instead.");
        }

        MapBounds = ComputeBounds(mapRoot);
        if (mapRoot == null)
            MakeFallbackFloor();
        Debug.Log("[GtagTest] Map bounds: center " + MapBounds.center + " size " + MapBounds.size);
    }

    void MakeFallbackFloor()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FallbackFloor";
        floor.transform.localScale = new Vector3(200f, 1f, 200f);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        MapBounds = new Bounds(Vector3.zero, new Vector3(200f, 10f, 200f));
    }

    static Bounds ComputeBounds(GameObject root)
    {
        if (root == null) return new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.transform.position, new Vector3(100f, 10f, 100f));
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    void PrepareMap(GameObject root)
    {
        // textures shipped next to the model (Resources/Map/Textures)
        var tex = new Dictionary<string, Texture2D>();
        foreach (Texture2D t in Resources.LoadAll<Texture2D>("Map/Textures"))
            tex[t.name.ToLowerInvariant()] = t;

        // everything on the Default layer = grabbable by the locomotion
        bool hasColliders = root.GetComponentInChildren<Collider>() != null;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
        {
            mf.gameObject.layer = 0;
            if (!hasColliders && mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        // re-link textures by material name if the importer didn't do it
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.mainTexture != null) continue;
                string key = BaseName(m.name);
                string alias;
                if (TextureAlias.TryGetValue(key, out alias)) key = alias;
                Texture2D t;
                if (tex.TryGetValue(key, out t)) m.mainTexture = t;
            }
            r.materials = mats;
        }
    }

    // "bark.001 (Instance)" -> "bark"
    static string BaseName(string n)
    {
        n = n.Replace(" (Instance)", "").ToLowerInvariant();
        int dot = n.LastIndexOf('.');
        if (dot > 0)
        {
            int num;
            if (int.TryParse(n.Substring(dot + 1), out num)) n = n.Substring(0, dot);
        }
        return n;
    }

    // ---------------- the stump (spawn + computer) ----------------

    Bounds computerScreen;
    bool hasComputerScreen;

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        string want = name.ToLowerInvariant();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLowerInvariant() == want) return t;
        return null;
    }

    static bool BoundsOf(Transform t, out Bounds b)
    {
        b = new Bounds();
        if (t == null) return false;
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return true;
    }

    // The stump = the tree room: stool + table + monitor ("GorillaComputer_fixed") in the map.
    // Spawn = floor next to the stool; the computer panel goes in front of the monitor.
    Vector3 FindStump()
    {
        Transform computer = FindDeep(mapRoot != null ? mapRoot.transform : null, "GorillaComputer_fixed");
        Transform stool = FindDeep(computer, "stool");
        Transform monitor = FindDeep(computer, "monitor");

        Bounds sb;
        if (BoundsOf(stool, out sb) || BoundsOf(computer, out sb))
        {
            if (BoundsOf(monitor, out computerScreen)) hasComputerScreen = true;

            RaycastHit hit;
            Vector3 origin = new Vector3(sb.center.x, sb.min.y + 0.4f, sb.center.z);
            if (Physics.Raycast(origin, Vector3.down, out hit, 3f, 1 << 0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(sb.center.x, sb.min.y, sb.center.z);
        }

        Debug.LogWarning("[GtagTest] Stump (GorillaComputer_fixed / stool) not found in the map - spawning at the middle of the map instead.");
        return FindGround(0);
    }

    void BuildComputer()
    {
        Vector3 center, away;
        if (hasComputerScreen)
        {
            Vector3 toPlayer = SpawnPoint - computerScreen.center;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Vector3.back;
            toPlayer.Normalize();
            // push the panel out to the player's side of the monitor
            Vector3 e = computerScreen.extents;
            float reach = Mathf.Abs(toPlayer.x) * e.x + Mathf.Abs(toPlayer.z) * e.z + 0.04f;
            center = computerScreen.center + toPlayer * reach;
            away = -toPlayer;
        }
        else
        {
            center = SpawnPoint + Vector3.up * 1.3f + Vector3.forward * 1f;
            away = Vector3.forward;
        }
        gameObject.AddComponent<GorillaComputer>().Build(center, away);
    }

    // ---------------- ground sampling / teleport ----------------

    // attempt 0 = middle of the map, higher attempts = random spots
    public Vector3 FindGround(int attempt)
    {
        Bounds b = MapBounds;
        for (int i = 0; i < 300; i++)
        {
            float spread = attempt == 0 ? Mathf.Min(1f, i / 100f) : 1f;
            Vector3 p = b.center;
            p.x += Random.Range(-b.extents.x, b.extents.x) * spread;
            p.z += Random.Range(-b.extents.z, b.extents.z) * spread;
            if (attempt == 0 && i == 0) { p.x = b.center.x; p.z = b.center.z; }

            RaycastHit hit;
            Vector3 origin = new Vector3(p.x, b.max.y + 50f, p.z);
            if (Physics.Raycast(origin, Vector3.down, out hit, b.size.y + 120f, 1 << 0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.7f)
                return hit.point;
        }
        return new Vector3(b.center.x, b.min.y + 1f, b.center.z);
    }

    public void TeleportToGround(Vector3 ground)
    {
        Vector3 rig = Player.transform.position;
        Vector3 delta = new Vector3(ground.x - Head.position.x, (ground.y + 0.2f) - rig.y, ground.z - Head.position.z);
        MoveRig(rig + delta);
    }

    public void TeleportToSpawn() { TeleportToGround(SpawnPoint); }

    public void MoveRig(Vector3 newRigPosition)
    {
        Player.transform.position = newRigPosition;
        Body.position = newRigPosition;
        Body.velocity = Vector3.zero;
        Physics.SyncTransforms();
        ResyncPlayer();
    }

    public void ResyncPlayer()
    {
        Player.leftHandFollower.position = Player.leftHandTransform.position;
        Player.rightHandFollower.position = Player.rightHandTransform.position;
        Player.InitializeValues();
    }

    // ---------------- player rig ----------------

    void BuildPlayer(Vector3 spawn)
    {
        // built inactive so the Player component's Awake runs only after every field is assigned
        var playerGo = new GameObject("GorillaPlayer");
        playerGo.SetActive(false);
        playerGo.layer = PlayerLayer;
        playerGo.transform.position = spawn + Vector3.up * 0.2f;

        Body = playerGo.AddComponent<Rigidbody>();
        Body.mass = 1f;
        Body.useGravity = true;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.constraints = RigidbodyConstraints.FreezeRotation;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // head / camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.layer = PlayerLayer;
        camGo.transform.SetParent(playerGo.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Cam = camGo.AddComponent<Camera>();
        Cam.nearClipPlane = 0.05f;
        Cam.farClipPlane = 1500f;
        Cam.clearFlags = CameraClearFlags.SolidColor;
        Cam.backgroundColor = new Color(0.45f, 0.62f, 0.85f);
        camGo.AddComponent<AudioListener>();
        HeadCollider = camGo.AddComponent<SphereCollider>();
        HeadCollider.radius = 0.15f;

        // body capsule hangs under the head (Player.cs keeps it upright)
        var bodyGo = new GameObject("Body Collider");
        bodyGo.layer = PlayerLayer;
        bodyGo.transform.SetParent(camGo.transform, false);
        bodyGo.transform.localPosition = new Vector3(0f, -0.24f, -0.06f);
        BodyCollider = bodyGo.AddComponent<CapsuleCollider>();
        BodyCollider.radius = 0.15f;
        BodyCollider.height = 0.5f;

        // tracked controller transforms
        LeftHand = new GameObject("LeftHand Controller").transform;
        LeftHand.SetParent(playerGo.transform, false);
        RightHand = new GameObject("RightHand Controller").transform;
        RightHand.SetParent(playerGo.transform, false);
        LeftHand.localPosition = new Vector3(-0.25f, 1.2f, 0.3f);
        RightHand.localPosition = new Vector3(0.25f, 1.2f, 0.3f);

        // visible hand spheres (these are what actually collide, via Player.cs spherecasts)
        Transform leftFollower = MakeFollower("LeftHand Follower", playerGo.transform);
        Transform rightFollower = MakeFollower("RightHand Follower", playerGo.transform);
        leftFollower.position = LeftHand.position;
        rightFollower.position = RightHand.position;

        // Player.cs with the same tuning values as the original Gorilla Rig prefab
        Player = playerGo.AddComponent<Player>();
        Player.enabled = false;
        Player.headCollider = HeadCollider;
        Player.bodyCollider = BodyCollider;
        Player.leftHandFollower = leftFollower;
        Player.rightHandFollower = rightFollower;
        Player.leftHandTransform = LeftHand;
        Player.rightHandTransform = RightHand;
        Player.velocityHistorySize = 8;
        Player.maxArmLength = 1.5f;
        Player.unStickDistance = 1f;
        Player.velocityLimit = 0.4f;
        Player.maxJumpSpeed = 6.5f;
        Player.jumpMultiplier = 1.1f;
        Player.minimumRaycastDistance = 0.05f;
        Player.defaultSlideFactor = 0.03f;
        Player.defaultPrecision = 0.995f;
        Player.locomotionEnabledLayers = 1 << 0;

        Rig = gameObject.AddComponent<XRRigInput>();
        Rig.head = camGo.transform;
        Rig.leftHand = LeftHand;
        Rig.rightHand = RightHand;
        Rig.player = Player;
        Rig.body = Body;

        playerGo.SetActive(true);   // Awake runs now with everything assigned
    }

    static Transform MakeFollower(string name, Transform parent)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        DestroyImmediate(s.GetComponent<Collider>());
        s.layer = PlayerLayer;
        s.transform.SetParent(parent, true);
        s.transform.localScale = Vector3.one * 0.1f;
        return s.transform;
    }
}
