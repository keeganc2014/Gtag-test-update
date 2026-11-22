using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GorillaLocomotion;

// Offline test menu for the map. X button (left controller) opens it on your left hand;
// poke a button with your right hand and pull the right trigger. Desktop: Tab to open, aim + left click.
public class TestMenu : MonoBehaviour
{
    class Btn
    {
        public Image bg;
        public Text label;
        public Func<string> text;
        public Func<bool> isOn;
        public Action click;
    }

    GtagBootstrap boot;
    XRRigInput rig;
    Player player;
    Rigidbody body;

    GameObject panel;
    readonly List<Btn> buttons = new List<Btn>();
    readonly Dictionary<Collider, Btn> byCollider = new Dictionary<Collider, Btn>();
    Btn hover;
    bool open;

    // state
    int speedIdx, gravIdx;
    bool fly, noclip, platforms, flashlight, darkness;
    bool hasSaved;
    Vector3 savedRig;
    readonly GameObject[] plat = new GameObject[2];
    Light torch;

    static readonly string[] SpeedNames = { "Normal", "Fast", "Super" };
    static readonly float[] JumpMult = { 1.1f, 1.3f, 1.5f };
    static readonly float[] MaxJump = { 6.5f, 9f, 13f };
    static readonly float[] GravScale = { 1f, 0.5f, 0.25f };
    const float FlySpeed = 6f;

    Color cNormal = new Color(0.17f, 0.17f, 0.2f, 0.95f);
    Color cOn = new Color(0.15f, 0.45f, 0.2f, 0.95f);
    Color cHover = new Color(0.75f, 0.45f, 0.1f, 0.95f);

    void Start()
    {
        boot = GtagBootstrap.Instance;
        rig = boot.Rig;
        player = boot.Player;
        body = boot.Body;

        BuildPanel();
        BuildTorch();
        panel.SetActive(false);
    }

    // ---------------- UI construction ----------------

    static Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return f;
    }

    void BuildPanel()
    {
        Font font = GetFont();

        panel = new GameObject("TestMenu");
        panel.layer = GtagBootstrap.MenuLayer;
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(360f, 440f);
        rt.localScale = Vector3.one * 0.001f;   // 1 canvas px = 1 mm

        MakeRect("Background", panel.transform, Vector2.zero, new Vector2(360f, 440f)).gameObject
            .AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.05f, 0.92f);

        Text title = MakeText("Title", panel.transform, new Vector2(0f, 195f), new Vector2(340f, 40f), font, 26);
        title.text = "MAP TEST MENU";
        title.fontStyle = FontStyle.Bold;

        int n = 0;
        Add(ref n, font, () => "Speed: " + SpeedNames[speedIdx], null, () => { speedIdx = (speedIdx + 1) % 3; ApplySpeed(); });
        Add(ref n, font, () => "Gravity: " + GravScale[gravIdx] + "x", null, () => { gravIdx = (gravIdx + 1) % 3; ApplyGravity(); });
        Add(ref n, font, () => "Fly: " + (fly ? "ON" : "OFF"), () => fly, () => { fly = !fly; });
        Add(ref n, font, () => "Noclip: " + (noclip ? "ON" : "OFF"), () => noclip, () => SetNoclip(!noclip));
        Add(ref n, font, () => "Platforms: " + (platforms ? "ON" : "OFF"), () => platforms, () => { platforms = !platforms; if (!platforms) ClearPlatforms(); });
        Add(ref n, font, () => "Flashlight: " + (flashlight ? "ON" : "OFF"), () => flashlight, () => { flashlight = !flashlight; torch.enabled = flashlight; });
        Add(ref n, font, () => "Darkness: " + (darkness ? "ON" : "OFF"), () => darkness, () => SetDarkness(!darkness));
        Add(ref n, font, () => "Go to Spawn", null, () => boot.TeleportToSpawn());
        Add(ref n, font, () => "Random Spot", null, () => boot.TeleportToGround(boot.FindGround(1)));
        Add(ref n, font, () => hasSaved ? "Save Pos (set)" : "Save Pos", null, () => { savedRig = player.transform.position; hasSaved = true; });
        Add(ref n, font, () => "Load Pos", null, () => { if (hasSaved) boot.MoveRig(savedRig); });
        Add(ref n, font, () => "Close", null, () => SetOpen(false));
    }

    void Add(ref int index, Font font, Func<string> text, Func<bool> isOn, Action click)
    {
        int col = index % 2;
        int row = index / 2;
        Vector2 pos = new Vector2(col == 0 ? -85f : 85f, 135f - row * 62f);
        index++;

        RectTransform r = MakeRect("Button", panel.transform, pos, new Vector2(160f, 50f));
        var img = r.gameObject.AddComponent<Image>();
        img.color = cNormal;

        var box = r.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(160f, 50f, 120f);
        box.isTrigger = true;

        Text t = MakeText("Label", r, Vector2.zero, new Vector2(156f, 46f), font, 20);

        var b = new Btn { bg = img, label = t, text = text, isOn = isOn, click = click };
        buttons.Add(b);
        byCollider[box] = b;
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = GtagBootstrap.MenuLayer;
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return r;
    }

    static Text MakeText(string name, Transform parent, Vector2 pos, Vector2 size, Font font, int fontSize)
    {
        RectTransform r = MakeRect(name, parent, pos, size);
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    void BuildTorch()
    {
        var go = new GameObject("Flashlight");
        go.transform.SetParent(boot.Head, false);
        torch = go.AddComponent<Light>();
        torch.type = LightType.Spot;
        torch.range = 35f;
        torch.spotAngle = 60f;
        torch.intensity = 3f;
        torch.shadows = LightShadows.None;
        torch.enabled = false;
    }

    // ---------------- update ----------------

    void Update()
    {
        if (boot == null || !player.enabled) return;

        if (rig.MenuTogglePressed) SetOpen(!open);

        UpdateMenu();
        UpdateFly();
        UpdatePlatforms();
    }

    void SetOpen(bool value)
    {
        open = value;
        panel.SetActive(open);
        hover = null;
    }

    void UpdateMenu()
    {
        if (!open) return;

        // place the panel next to the left hand, turned toward the head
        Vector3 headPos = boot.Head.position;
        Vector3 pos;
        if (rig.XRActive)
        {
            Vector3 lp = boot.LeftHand.position;
            pos = lp + Vector3.up * 0.12f + (headPos - lp).normalized * 0.12f;
        }
        else
        {
            pos = headPos + boot.Head.forward * 0.7f + boot.Head.right * -0.1f;
        }
        panel.transform.position = pos;
        panel.transform.rotation = Quaternion.LookRotation(pos - headPos, Vector3.up);

        // what is being pointed at / poked?
        hover = null;
        Collider hit = null;
        if (rig.XRActive)
        {
            Collider[] hits = Physics.OverlapSphere(boot.RightHand.position, 0.04f, 1 << GtagBootstrap.MenuLayer, QueryTriggerInteraction.Collide);
            if (hits.Length > 0) hit = hits[0];
        }
        else
        {
            RaycastHit rh;
            Ray ray = new Ray(boot.Head.position, boot.Head.forward);
            if (Physics.Raycast(ray, out rh, 3f, 1 << GtagBootstrap.MenuLayer, QueryTriggerInteraction.Collide))
                hit = rh.collider;
        }
        if (hit != null) byCollider.TryGetValue(hit, out hover);

        if (hover != null && rig.ClickPressed)
        {
            hover.click();
            rig.Pulse();
        }

        foreach (Btn b in buttons)
        {
            b.label.text = b.text();
            b.bg.color = b == hover ? cHover : (b.isOn != null && b.isOn() ? cOn : cNormal);
        }
    }

    void UpdateFly()
    {
        bool flying = fly || noclip;
        body.useGravity = !flying;
        if (!flying) return;

        if (rig.FlyHeld && !open)
        {
            Vector3 dir = rig.XRActive ? boot.RightHand.forward : boot.Head.forward;
            body.velocity = dir * FlySpeed;
        }
        else
        {
            body.velocity = Vector3.zero;
        }
    }

    void UpdatePlatforms()
    {
        if (!platforms) return;
        HandPlatform(0, rig.LeftGrip, boot.LeftHand);
        HandPlatform(1, rig.RightGrip, boot.RightHand);
    }

    void HandPlatform(int i, bool grip, Transform hand)
    {
        if (grip && plat[i] == null)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Platform";
            p.transform.localScale = new Vector3(0.5f, 0.04f, 0.5f);
            p.transform.position = hand.position + Vector3.down * 0.05f;
            p.transform.rotation = Quaternion.Euler(0f, hand.eulerAngles.y, 0f);
            p.GetComponent<Renderer>().material.color = new Color(0.2f, 0.75f, 0.3f);
            plat[i] = p;
        }
        else if (!grip && plat[i] != null)
        {
            Destroy(plat[i]);
            plat[i] = null;
        }
    }

    void ClearPlatforms()
    {
        for (int i = 0; i < plat.Length; i++)
        {
            if (plat[i] != null) Destroy(plat[i]);
            plat[i] = null;
        }
    }

    // ---------------- settings ----------------

    void ApplySpeed()
    {
        player.jumpMultiplier = JumpMult[speedIdx];
        player.maxJumpSpeed = MaxJump[speedIdx];
    }

    void ApplyGravity()
    {
        Physics.gravity = Vector3.down * 9.81f * GravScale[gravIdx];
    }

    void SetNoclip(bool on)
    {
        noclip = on;
        player.locomotionEnabledLayers = on ? (LayerMask)0 : (LayerMask)1;
        boot.HeadCollider.isTrigger = on;
        boot.BodyCollider.isTrigger = on;
    }

    void SetDarkness(bool on)
    {
        darkness = on;
        boot.Sun.intensity = on ? 0.04f : 1f;
        RenderSettings.ambientLight = on ? new Color(0.02f, 0.02f, 0.03f) : new Color(0.55f, 0.58f, 0.62f);
        RenderSettings.fog = on;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.fogColor = Color.black;
        boot.Cam.backgroundColor = on ? Color.black : new Color(0.45f, 0.62f, 0.85f);
        if (on && !flashlight) { flashlight = true; torch.enabled = true; }
    }

    void OnDestroy()
    {
        Physics.gravity = Vector3.down * 9.81f;
    }
}
