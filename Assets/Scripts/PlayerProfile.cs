using System;
using System.Collections.Generic;
using UnityEngine;

// The player's name + fur color + a local leaderboard, saved in PlayerPrefs (no network, no accounts).
// Colors use the Gorilla Tag style 0-9 per channel.
public static class PlayerProfile
{
    [Serializable]
    public class Entry
    {
        public string name;
        public int r, g, b;
        public float score;   // seconds played with this name
        public Color Color { get { return new Color(r / 9f, g / 9f, b / 9f); } }
    }

    [Serializable]
    class SaveData
    {
        public string name;
        public int r, g, b;
        public List<Entry> entries = new List<Entry>();
    }

    public const int MaxName = 12;
    public const string AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    const string Key = "Gtag.Profile.v1";

    public static event Action Changed;

    public static string Name { get; private set; }
    public static int R { get; private set; }
    public static int G { get; private set; }
    public static int B { get; private set; }
    public static Color Color { get { return new Color(R / 9f, G / 9f, B / 9f); } }

    static readonly List<Entry> entries = new List<Entry>();
    static bool loaded;
    static bool dirty;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;

        try
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                SaveData d = JsonUtility.FromJson<SaveData>(json);
                if (d != null)
                {
                    Name = Sanitize(d.name);
                    R = Mathf.Clamp(d.r, 0, 9);
                    G = Mathf.Clamp(d.g, 0, 9);
                    B = Mathf.Clamp(d.b, 0, 9);
                    if (d.entries != null) entries.AddRange(d.entries);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerProfile] Could not read saved profile: " + e.Message);
        }

        if (string.IsNullOrEmpty(Name))
        {
            Name = "GORILLA" + UnityEngine.Random.Range(1000, 9999);
            R = UnityEngine.Random.Range(0, 10);
            G = UnityEngine.Random.Range(0, 10);
            B = UnityEngine.Random.Range(0, 10);
        }
        Current();   // make sure there is a leaderboard row for the current name
        Save();
    }

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char ch in raw.ToUpperInvariant())
        {
            if (AllowedChars.IndexOf(ch) >= 0 && sb.Length < MaxName) sb.Append(ch);
        }
        return sb.ToString();
    }

    public static bool SetName(string raw)
    {
        string n = Sanitize(raw);
        if (n.Length == 0) return false;
        Name = n;
        Current();
        dirty = true;
        Save();
        RaiseChanged();
        return true;
    }

    public static void SetColor(int r, int g, int b)
    {
        R = Mathf.Clamp(r, 0, 9);
        G = Mathf.Clamp(g, 0, 9);
        B = Mathf.Clamp(b, 0, 9);
        Entry e = Current();
        e.r = R; e.g = G; e.b = B;
        dirty = true;
        RaiseChanged();
    }

    public static void AddScore(float seconds)
    {
        Current().score += seconds;
        dirty = true;
    }

    // leaderboard row of the current name (created if missing)
    static Entry Current()
    {
        foreach (Entry e in entries)
        {
            if (e.name == Name)
            {
                e.r = R; e.g = G; e.b = B;
                return e;
            }
        }
        var n = new Entry { name = Name, r = R, g = G, b = B, score = 0f };
        entries.Add(n);
        return n;
    }

    public static List<Entry> Top(int count)
    {
        var list = new List<Entry>(entries);
        list.Sort((a, b) => b.score.CompareTo(a.score));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    public static void Save()
    {
        if (!loaded || !dirty && PlayerPrefs.HasKey(Key)) return;
        // keep the board small
        if (entries.Count > 50)
        {
            entries.Sort((a, b) => b.score.CompareTo(a.score));
            entries.RemoveRange(50, entries.Count - 50);
            Current();
        }
        var d = new SaveData { name = Name, r = R, g = G, b = B, entries = entries };
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
        PlayerPrefs.Save();
        dirty = false;
    }

    static void RaiseChanged()
    {
        if (Changed != null) Changed();
    }
}
