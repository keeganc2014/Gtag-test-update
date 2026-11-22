using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The computer in the stump: set your NAME, set your fur COLOR (RGB 0-9) and look at the LEADERBOARD.
// Poke a button with your right hand + right trigger (desktop: aim with the head/mouse + left click).
// Everything is local/offline for now - the leaderboard lives on this device.
public class GorillaComputer : MonoBehaviour
{
    class Btn
    {
        public Image bg;
        public Text label;
        public Func<string> text;
        public Func<bool> isOn;
        public Action click;
    }

    const float W = 700f, H = 500f;

    GtagBootstrap boot;
    XRRigInput rig;
    GameObject panel;
    readonly GameObject[] pages = new GameObject[3];
    readonly List<Btn> buttons = new List<Btn>();
    readonly Dictionary<Collider, Btn> byCollider = new Dictionary<Collider, Btn>();
    Btn hover;
    int page;

    // name page
    string nameBuf = "";
    Text nameText, statusText;
    float statusUntil;

    // color page
    Image preview;
    Text previewName;
    readonly Text[] rgbVals = new Text[3];

    // board page
    const int Rows = 8;
    readonly Text[] rowRank = new Text[Rows];
    readonly Image[] rowSwatch = new Image[Rows];
    readonly Text[] rowName = new Text[Rows];
    readonly Text[] rowScore = new Text[Rows];
    float nextBoardRefresh, nextSave;

    Color cNormal = new Color(0.12f, 0.15f, 0.12f, 0.96f);
    Color cOn = new Color(0.12f, 0.45f, 0.2f, 0.96f);
    Color cHover = new Color(0.8f, 0.5f, 0.1f, 0.96f);
    static readonly string[] PageNames = { "NAME", "COLOR", "BOARD" };

    // center = middle of the screen, awayFromPlayer = horizontal direction the panel should face (text reads from the other side)
    public void Build(Vector3 center, Vector3 awayFromPlayer)
    {
        boot = GtagBootstrap.Instance;
        rig = boot.Rig;
        PlayerProfile.Load();
        nameBuf = PlayerProfile.Name;
        PlayerProfile.Changed += OnProfileChanged;

        Font font = GetFont();
        panel = new GameObject("GorillaComputerPanel");
        panel.layer = GtagBootstrap.MenuLayer;
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(W, H);
        rt.localScale = Vector3.one * 0.0011f;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude < 0.0001f) awayFromPlayer = Vector3.forward;
        panel.transform.position = center;
        panel.transform.rotation = Quaternion.LookRotation(awayFromPlayer.normalized, Vector3.up);

        MakeRect("Background", panel.transform, Vector2.zero, new Vector2(W, H)).gameObject
            .AddComponent<Image>().color = new Color(0.02f, 0.05f, 0.03f, 0.95f);

        // tabs
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            AddButton(panel.transform, font, new Vector2(-220f + i * 220f, 212f), new Vector2(200f, 50f), 24,
                () => PageNames[idx], () => page == idx, () => SetPage(idx));
        }

        pages[0] = MakePage("NamePage");
        pages[1] = MakePage("ColorPage");
        pages[2] = MakePage("BoardPage");
        BuildNamePage(font, pages[0].transform);
        BuildColorPage(font, pages[1].transform);
        BuildBoardPage(font, pages[2].transform);
        SetPage(0);
    }

    GameObject MakePage(string name)
    {
        RectTransform r = MakeRect(name, panel.transform, Vector2.zero, new Vector2(W, H));
        return r.gameObject;
    }

    void SetPage(int p)
    {
        page = p;
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == p);
        nextBoardRefresh = 0f;
    }

    // ---------------- name page ----------------

    void BuildNamePage(Font font, Transform parent)
    {
        nameText = MakeText("NameText", parent, new Vector2(0f, 140f), new Vector2(660f, 50f), font, 34, TextAnchor.MiddleCenter);
        nameText.color = new Color(0.6f, 1f, 0.6f);

        string[] rows = { "ABCDEFGHIJ", "KLMNOPQRST", "UVWXYZ0123", "456789" };
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                char ch = rows[r][c];
                AddButton(parent, font, new Vector2((c - 4.5f) * 64f, 78f - r * 54f), new Vector2(58f, 46f), 24,
                    () => ch.ToString(), null, () => TypeChar(ch));
            }
        }
        AddButton(parent, font, new Vector2(-200f, -138f), new Vector2(190f, 50f), 24, () => "DELETE", null, () => { if (nameBuf.Length > 0) nameBuf = nameBuf.Substring(0, nameBuf.Length - 1); });
        AddButton(parent, font, new Vector2(0f, -138f), new Vector2(190f, 50f), 24, () => "CLEAR", null, () => nameBuf = "");
        AddButton(parent, font, new Vector2(200f, -138f), new Vector2(190f, 50f), 24, () => "SAVE NAME", null, SaveName);

        statusText = MakeText("Status", parent, new Vector2(0f, -205f), new Vector2(660f, 36f), font, 22, TextAnchor.MiddleCenter);
        statusText.color = new Color(1f, 0.9f, 0.4f);
    }

    void TypeChar(char ch)
    {
        if (nameBuf.Length < PlayerProfile.MaxName) nameBuf += ch;
    }

    void SaveName()
    {
        if (PlayerProfile.SetName(nameBuf))
        {
            nameBuf = PlayerProfile.Name;
            Status("Name saved: " + PlayerProfile.Name);
        }
        else Status("Type at least one letter or number");
    }

    void Status(string s)
    {
        statusText.text = s;
        statusUntil = Time.unscaledTime + 3f;
    }

    // ---------------- color page ----------------

    void BuildColorPage(Font font, Transform parent)
    {
        Text title = MakeText("Title", parent, new Vector2(-60f, 140f), new Vector2(500f, 40f), font, 28, TextAnchor.MiddleCenter);
        title.text = "PICK YOUR COLOR  (0 - 9)";

        string[] labels = { "RED", "GREEN", "BLUE" };
        for (int i = 0; i < 3; i++)
        {
            int ch = i;
            float y = 70f - i * 75f;
            Text l = MakeText("Label" + i, parent, new Vector2(-260f, y), new Vector2(120f, 46f), font, 26, TextAnchor.MiddleLeft);
            l.text = labels[i];
            AddButton(parent, font, new Vector2(-110f, y), new Vector2(70f, 56f), 32, () => "-", null, () => Bump(ch, -1));
            rgbVals[i] = MakeText("Value" + i, parent, new Vector2(-30f, y), new Vector2(70f, 56f), font, 34, TextAnchor.MiddleCenter);
            AddButton(parent, font, new Vector2(50f, y), new Vector2(70f, 56f), 32, () => "+", null, () => Bump(ch, +1));
        }

        RectTransform sw = MakeRect("Preview", parent, new Vector2(230f, 40f), new Vector2(170f, 170f));
        preview = sw.gameObject.AddComponent<Image>();
        previewName = MakeText("PreviewName", parent, new Vector2(230f, -70f), new Vector2(230f, 40f), font, 24, TextAnchor.MiddleCenter);

        AddButton(parent, font, new Vector2(-60f, -190f), new Vector2(220f, 50f), 24, () => "RANDOM", null,
            () => PlayerProfile.SetColor(UnityEngine.Random.Range(0, 10), UnityEngine.Random.Range(0, 10), UnityEngine.Random.Range(0, 10)));
        AddButton(parent, font, new Vector2(180f, -190f), new Vector2(220f, 50f), 24, () => "SAVE", null,
            () => { PlayerProfile.Save(); Status("Color saved"); });
    }

    void Bump(int channel, int delta)
    {
        int r = PlayerProfile.R, g = PlayerProfile.G, b = PlayerProfile.B;
        if (channel == 0) r += delta;
        else if (channel == 1) g += delta;
        else b += delta;
        PlayerProfile.SetColor(r, g, b);
    }

    // ---------------- board page ----------------

    void BuildBoardPage(Font font, Transform parent)
    {
        Text title = MakeText("Title", parent, new Vector2(0f, 150f), new Vector2(600f, 40f), font, 28, TextAnchor.MiddleCenter);
        title.text = "LEADERBOARD";

        for (int i = 0; i < Rows; i++)
        {
            float y = 100f - i * 36f;
            rowRank[i] = MakeText("Rank" + i, parent, new Vector2(-290f, y), new Vector2(60f, 32f), font, 22, TextAnchor.MiddleLeft);
            rowSwatch[i] = MakeRect("Swatch" + i, parent, new Vector2(-225f, y), new Vector2(28f, 28f)).gameObject.AddComponent<Image>();
            rowName[i] = MakeText("Name" + i, parent, new Vector2(-70f, y), new Vector2(260f, 32f), font, 24, TextAnchor.MiddleLeft);
            rowScore[i] = MakeText("Score" + i, parent, new Vector2(230f, y), new Vector2(180f, 32f), font, 24, TextAnchor.MiddleRight);
        }

        Text foot = MakeText("Footer", parent, new Vector2(0f, -200f), new Vector2(660f, 50f), font, 18, TextAnchor.MiddleCenter);
        foot.text = "Score = time played with that name. Saved on this device only (no online / public lobbies yet).";
        foot.color = new Color(0.7f, 0.8f, 0.7f);
    }

    void RefreshBoard()
    {
        List<PlayerProfile.Entry> top = PlayerProfile.Top(Rows);
        for (int i = 0; i < Rows; i++)
        {
            bool has = i < top.Count;
            rowRank[i].gameObject.SetActive(has);
            rowSwatch[i].gameObject.SetActive(has);
            rowName[i].gameObject.SetActive(has);
            rowScore[i].gameObject.SetActive(has);
            if (!has) continue;
            PlayerProfile.Entry e = top[i];
            bool me = e.name == PlayerProfile.Name;
            Color tc = me ? new Color(1f, 0.92f, 0.4f) : Color.white;
            rowRank[i].text = "#" + (i + 1);
            rowRank[i].color = tc;
            rowSwatch[i].color = e.Color;
            rowName[i].text = e.name + (me ? "  (you)" : "");
            rowName[i].color = tc;
            int secs = Mathf.FloorToInt(e.score);
            rowScore[i].text = (secs / 60) + ":" + (secs % 60).ToString("00");
            rowScore[i].color = tc;
        }
    }

    // ---------------- update ----------------

    void OnProfileChanged() { nextBoardRefresh = 0f; }

    void Update()
    {
        if (panel == null || boot == null || !boot.Player.enabled) return;

        // leaderboard score = time played
        PlayerProfile.AddScore(Time.unscaledDeltaTime);
        if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + 10f; PlayerProfile.Save(); }

        if (statusText != null && statusUntil > 0f && Time.unscaledTime > statusUntil) { statusText.text = ""; statusUntil = 0f; }

        // live labels
        if (page == 0) nameText.text = "NAME:  " + nameBuf + ((int)(Time.unscaledTime * 2f) % 2 == 0 ? "_" : " ");
        if (page == 1)
        {
            rgbVals[0].text = PlayerProfile.R.ToString();
            rgbVals[1].text = PlayerProfile.G.ToString();
            rgbVals[2].text = PlayerProfile.B.ToString();
            preview.color = PlayerProfile.Color;
            previewName.text = PlayerProfile.Name;
        }
        if (page == 2 && Time.unscaledTime >= nextBoardRefresh)
        {
            nextBoardRefresh = Time.unscaledTime + 0.5f;
            RefreshBoard();
        }

        // what is poked / aimed at?
        hover = null;
        Collider hit = null;
        if (rig.XRActive)
        {
            Collider[] hits = Physics.OverlapSphere(boot.RightHand.position, 0.04f, 1 << GtagBootstrap.MenuLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length && hit == null; i++)
                if (byCollider.ContainsKey(hits[i])) hit = hits[i];
        }
        else
        {
            RaycastHit rh;
            Ray ray = new Ray(boot.Head.position, boot.Head.forward);
            if (Physics.Raycast(ray, out rh, 3f, 1 << GtagBootstrap.MenuLayer, QueryTriggerInteraction.Collide)
                && byCollider.ContainsKey(rh.collider))
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
            if (!b.bg.gameObject.activeInHierarchy) continue;
            b.label.text = b.text();
            b.bg.color = b == hover ? cHover : (b.isOn != null && b.isOn() ? cOn : cNormal);
        }
    }

    void OnApplicationPause(bool paused) { if (paused) PlayerProfile.Save(); }
    void OnApplicationQuit() { PlayerProfile.Save(); }
    void OnDestroy()
    {
        PlayerProfile.Changed -= OnProfileChanged;
        PlayerProfile.Save();
    }

    // ---------------- UI helpers ----------------

    void AddButton(Transform parent, Font font, Vector2 pos, Vector2 size, int fontSize, Func<string> text, Func<bool> isOn, Action click)
    {
        RectTransform r = MakeRect("Button", parent, pos, size);
        var img = r.gameObject.AddComponent<Image>();
        img.color = cNormal;

        var box = r.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x, size.y, 120f);
        box.isTrigger = true;

        Text t = MakeText("Label", r, Vector2.zero, size - new Vector2(4f, 4f), font, fontSize, TextAnchor.MiddleCenter);
        var b = new Btn { bg = img, label = t, text = text, isOn = isOn, click = click };
        buttons.Add(b);
        byCollider[box] = b;
    }

    static Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return f;
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

    static Text MakeText(string name, Transform parent, Vector2 pos, Vector2 size, Font font, int fontSize, TextAnchor anchor)
    {
        RectTransform r = MakeRect(name, parent, pos, size);
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = Color.white;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
}
