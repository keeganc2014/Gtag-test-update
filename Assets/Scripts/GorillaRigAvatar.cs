using System.Collections.Generic;
using UnityEngine;

// Hooks the Gorilla IK rig up to the game.
//
// If Assets/Resources/Rig/GorillaRig.fbx (exported from Gorilla_IK_Rig.blend) exists, it is spawned and driven:
//   head bone        <- headset
//   upper_arm/forearm/hand (L/R) <- two-bone IK toward the hand followers (what the locomotion actually uses)
// Bone names are the ones in the .blend: shoulder.L/R, upper_arm.L/R, forearm.L/R, hand.L/R.
// If the FBX is not there yet, a simple built-in gorilla (chest + arms + hands) is used instead,
// so the game works either way. The fur color comes from PlayerProfile (set on the computer).
public class GorillaRigAvatar : MonoBehaviour
{
    public const string ResourcePath = "Rig/GorillaRig";

    // tweak if the exported hands point the wrong way
    public Vector3 handRotationOffsetL = Vector3.zero;
    public Vector3 handRotationOffsetR = Vector3.zero;
    public bool hideOwnHead = true;

    GtagBootstrap boot;
    Transform root;
    readonly List<Renderer> furRenderers = new List<Renderer>();

    // rig mode
    bool rigMode;
    Transform headBone, upL, foL, haL, upR, foR, haR;
    Vector3 headLocal;     // head bone position in the model root's space at bind pose

    // procedural mode
    Transform chest, armL, armR;

    public void Init(GtagBootstrap b)
    {
        boot = b;
        PlayerProfile.Load();

        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab != null && TryBuildRig(prefab)) rigMode = true;
        else BuildProcedural();

        if (rigMode)   // the model has its own hands, the collision spheres do not need to be seen
        {
            boot.Player.leftHandFollower.GetComponent<Renderer>().enabled = false;
            boot.Player.rightHandFollower.GetComponent<Renderer>().enabled = false;
        }

        ApplyColor();
        PlayerProfile.Changed += ApplyColor;
        Debug.Log("[GorillaRigAvatar] " + (rigMode ? "Using Resources/" + ResourcePath : "Gorilla rig FBX not found - using the built-in gorilla body."));
    }

    void OnDestroy() { PlayerProfile.Changed -= ApplyColor; }

    // ---------------- rig from the FBX ----------------

    bool TryBuildRig(GameObject prefab)
    {
        GameObject go = Instantiate(prefab);
        go.name = "GorillaRigModel";
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (Camera c in go.GetComponentsInChildren<Camera>()) Destroy(c.gameObject);
        foreach (Light l in go.GetComponentsInChildren<Light>()) Destroy(l.gameObject);
        foreach (Animator a in go.GetComponentsInChildren<Animator>()) a.enabled = false;
        SetLayerRecursive(go, GtagBootstrap.PlayerLayer);

        headBone = Find(go.transform, "head");
        upL = Find(go.transform, "upper_arm.L"); foL = Find(go.transform, "forearm.L"); haL = Find(go.transform, "hand.L");
        upR = Find(go.transform, "upper_arm.R"); foR = Find(go.transform, "forearm.R"); haR = Find(go.transform, "hand.R");

        if (headBone == null || upL == null || foL == null || haL == null || upR == null || foR == null || haR == null)
        {
            Debug.LogWarning("[GorillaRigAvatar] GorillaRig is missing head / upper_arm / forearm / hand bones (L and R) - falling back to the built-in body.");
            Destroy(go);
            return false;
        }

        root = go.transform;
        root.position = Vector3.zero;
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one;
        headLocal = root.InverseTransformPoint(headBone.position);

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) furRenderers.Add(r);
        return true;
    }

    // "upper_arm.L" == "upper_arm_L" == "UpperArmL" for matching purposes
    static string Norm(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    static Transform Find(Transform parent, string name)
    {
        string want = Norm(name);
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (Norm(t.name) == want) return t;
        return null;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    // ---------------- built-in gorilla ----------------

    void BuildProcedural()
    {
        var rootGo = new GameObject("GorillaBodyBuiltin");
        root = rootGo.transform;

        chest = Prim(PrimitiveType.Sphere, "Chest", root, new Vector3(0.42f, 0.5f, 0.32f));
        armL = Prim(PrimitiveType.Cylinder, "ArmL", root, new Vector3(0.09f, 0.3f, 0.09f));
        armR = Prim(PrimitiveType.Cylinder, "ArmR", root, new Vector3(0.09f, 0.3f, 0.09f));

        // the hand follower spheres (created by GtagBootstrap) are the visible hands
        furRenderers.Add(boot.Player.leftHandFollower.GetComponent<Renderer>());
        furRenderers.Add(boot.Player.rightHandFollower.GetComponent<Renderer>());
    }

    Transform Prim(PrimitiveType type, string name, Transform parent, Vector3 scale)
    {
        GameObject p = GameObject.CreatePrimitive(type);
        p.name = name;
        Destroy(p.GetComponent<Collider>());
        p.layer = GtagBootstrap.PlayerLayer;
        p.transform.SetParent(parent, false);
        p.transform.localScale = scale;
        furRenderers.Add(p.GetComponent<Renderer>());
        return p.transform;
    }

    // ---------------- color ----------------

    void ApplyColor()
    {
        Color c = PlayerProfile.Color;
        foreach (Renderer r in furRenderers)
        {
            if (r == null) continue;
            foreach (Material m in r.materials)   // instanced copies, the shared FBX materials stay untouched
            {
                if (m == null) continue;
                string n = m.name.ToLowerInvariant();
                if (rigMode && (n.Contains("nametag") || n.Contains("text") || n.Contains("headphone") || n.Contains("bow"))) continue;
                if (m.HasProperty("_Color")) m.color = c;
                else if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            }
        }
    }

    // ---------------- per frame ----------------

    void LateUpdate()
    {
        if (boot == null || boot.Player == null || !boot.Player.enabled) return;
        if (rigMode) UpdateRig(); else UpdateProcedural();
    }

    Quaternion BodyYaw()
    {
        Vector3 f = Vector3.ProjectOnPlane(boot.Head.forward, Vector3.up);
        if (f.sqrMagnitude < 0.01f) f = Vector3.ProjectOnPlane(boot.Head.up, Vector3.up);
        return f.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up);
    }

    void UpdateRig()
    {
        root.rotation = BodyYaw();
        root.position = boot.Head.position - root.rotation * headLocal;

        headBone.rotation = boot.Head.rotation * Quaternion.Euler(0f, 0f, 0f);
        if (hideOwnHead) headBone.localScale = Vector3.one * 0.001f;   // you would otherwise see the inside of your own head

        Vector3 right = root.right;
        SolveArm(upL, foL, haL, boot.Player.leftHandFollower.position, boot.LeftHand.rotation * Quaternion.Euler(handRotationOffsetL), -right);
        SolveArm(upR, foR, haR, boot.Player.rightHandFollower.position, boot.RightHand.rotation * Quaternion.Euler(handRotationOffsetR), right);
    }

    // classic two-bone IK: shoulder(upper) -> elbow(fore) -> wrist(hand)
    void SolveArm(Transform upper, Transform fore, Transform hand, Vector3 target, Quaternion handRot, Vector3 sideDir)
    {
        Vector3 a = upper.position;
        Vector3 b = fore.position;
        Vector3 c = hand.position;
        float l1 = (b - a).magnitude;
        float l2 = (c - b).magnitude;
        if (l1 < 0.001f || l2 < 0.001f) return;

        Vector3 toT = target - a;
        float dist = Mathf.Clamp(toT.magnitude, 0.02f, (l1 + l2) * 0.999f);
        Vector3 dir = toT.sqrMagnitude < 0.0001f ? root.forward : toT.normalized;

        // elbow bends back + out + down
        Vector3 pole = -root.forward * 0.4f + sideDir * 0.5f - Vector3.up * 0.6f;
        Vector3 side = Vector3.ProjectOnPlane(pole, dir);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.ProjectOnPlane(Vector3.down, dir);
        side.Normalize();

        float x = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
        Vector3 elbow = a + dir * x + side * h;
        Vector3 wrist = a + dir * dist;

        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        Vector3 b2 = fore.position;
        Vector3 c2 = hand.position;
        fore.rotation = Quaternion.FromToRotation(c2 - b2, wrist - b2) * fore.rotation;
        hand.rotation = handRot;
    }

    void UpdateProcedural()
    {
        Quaternion yaw = BodyYaw();
        Vector3 head = boot.Head.position;
        chest.position = head + Vector3.down * 0.38f - yaw * Vector3.forward * 0.05f;
        chest.rotation = yaw;

        Vector3 shoulderL = head + Vector3.down * 0.25f + yaw * Vector3.left * 0.22f;
        Vector3 shoulderR = head + Vector3.down * 0.25f + yaw * Vector3.right * 0.22f;
        Stretch(armL, shoulderL, boot.Player.leftHandFollower.position);
        Stretch(armR, shoulderR, boot.Player.rightHandFollower.position);
    }

    static void Stretch(Transform cyl, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.001f) return;
        cyl.position = from + d * 0.5f;
        cyl.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
        Vector3 s = cyl.localScale;
        cyl.localScale = new Vector3(s.x, len * 0.5f, s.z);   // Unity cylinder is 2 units tall
    }
}
