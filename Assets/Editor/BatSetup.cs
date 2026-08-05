using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds everything Unity-side for the ceiling bat enemy from the already-sliced
// sheets in Assets/Graphics/Enemies/Bat:
//   - animation clips (20 fps, matching the AngryPig clips)
//   - Bat_AC animator controller ("state" int + "isHit" trigger, wired to Bat.cs:
//     Perched=Idle, Alert=Ceiling Out, Fly=Flying (dive + return flight),
//     Land=Ceiling In (final approach to the perch), plus Hit)
//   - Assets/Prefabs/Enemies/Bat.prefab (kinematic body + capsule, Animator child
//     borrowing AngryPig's material/sorting layer, HeadTrigger child for stomps,
//     deathVFX + sight-blocking layers copied from the pig)
// Safe to re-run: clips and the prefab are rebuilt in place so scene instances
// keep pointing at them. Run via:
//   Tools > Yum Jump > Build Bat Enemy
public static class BatSetup
{
    private const string SpriteFolder   = "Assets/Graphics/Enemies/Bat";
    private const string AnimFolder     = "Assets/Animations/Enemies/Bat";
    private const string ControllerPath = AnimFolder + "/Bat_AC.controller";
    private const string PrefabPath     = "Assets/Prefabs/Enemies/Bat.prefab";
    private const string PigPrefabPath  = "Assets/Prefabs/Enemies/AngryPig.prefab";
    private const float Fps = 20f; // matches the AngryPig clips

    [MenuItem("Tools/Yum Jump/Build Bat Enemy")]
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder(AnimFolder))
            AssetDatabase.CreateFolder("Assets/Animations/Enemies", "Bat");

        Sprite[] idleFrames = LoadSprites("Idle (46x30).png");

        AnimationClip idle       = CreateClip("batIdle",       idleFrames,                            loop: true);
        AnimationClip ceilingOut = CreateClip("batCeilingOut", LoadSprites("Ceiling Out (46x30).png"), loop: true);
        AnimationClip flying     = CreateClip("batFlying",     LoadSprites("Flying (46x30).png"),      loop: true);
        AnimationClip ceilingIn  = CreateClip("batCeilingIn",  LoadSprites("Ceiling In (46x30).png"),  loop: true);
        AnimationClip hit        = CreateClip("batHit",        LoadSprites("Hit (46x30).png"),         loop: false);

        AnimatorController controller = BuildController(idle, ceilingOut, flying, ceilingIn, hit);
        BuildPrefab(controller, idleFrames[0]);

        AssetDatabase.SaveAssets();
        Debug.Log("BatSetup: built clips + Bat_AC in " + AnimFolder + " and prefab at " + PrefabPath +
                  ". Drop the prefab just under a ceiling; the yellow gizmo is the detection box. " +
                  "Add Assets/Resources/Audio/SFX/SFX_Bat_Screech for the telegraph sound.");
    }

    // ─── Sprites & clips ──────────────────────────────────────────────────────

    private static Sprite[] LoadSprites(string fileName)
    {
        string path = SpriteFolder + "/" + fileName;
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
        if (sprites.Length == 0)
            throw new System.InvalidOperationException("BatSetup: no sliced sprites found at " + path);

        // Slice names end in _0, _1, ... — numeric sort keeps frame order correct past _9.
        return sprites
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1)))
            .ToArray();
    }

    private static AnimationClip CreateClip(string name, Sprite[] frames, bool loop)
    {
        string path = AnimFolder + "/" + name + ".anim";

        var clip = new AnimationClip { name = name, frameRate = Fps };

        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
            propertyName = "m_Sprite",
        };
        var keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / Fps, value = frames[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Overwrite in place on re-runs so the controller's references stay valid.
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clip, existing);
            Object.DestroyImmediate(clip);
            return existing;
        }

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    // ─── Animator controller ──────────────────────────────────────────────────

    private static AnimatorController BuildController(AnimationClip idle, AnimationClip ceilingOut,
                                                      AnimationClip flying, AnimationClip ceilingIn,
                                                      AnimationClip hit)
    {
        // Rebuilt from scratch each run; only the Bat prefab references it and
        // BuildPrefab rewires that same run, so the GUID change is harmless.
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("state", AnimatorControllerParameterType.Int);
        controller.AddParameter("isHit", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState perched  = sm.AddState("Perched");
        AnimatorState alert    = sm.AddState("Alert");
        AnimatorState fly      = sm.AddState("Fly");
        AnimatorState land     = sm.AddState("Land");
        AnimatorState hitState = sm.AddState("Hit");

        perched.motion  = idle;
        alert.motion    = ceilingOut;
        fly.motion      = flying;
        land.motion     = ceilingIn;
        hitState.motion = hit;

        sm.defaultState = perched;

        // Bat.cs walks the cycle in order, so a simple chain is all it needs.
        // Fly covers the dive and most of the return; Land is only the final
        // approach to the perch, so the ceiling-in clip plays once, not looped.
        AddStateTransition(perched, alert,   1);
        AddStateTransition(alert,   fly,     2);
        AddStateTransition(fly,     land,    3);
        AddStateTransition(land,    perched, 0);

        AnimatorStateTransition toHit = sm.AddAnyStateTransition(hitState);
        toHit.AddCondition(AnimatorConditionMode.If, 0f, "isHit");
        toHit.hasExitTime = false;
        toHit.duration = 0f;
        toHit.canTransitionToSelf = false;

        return controller;
    }

    private static void AddStateTransition(AnimatorState from, AnimatorState to, int stateValue)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.AddCondition(AnimatorConditionMode.Equals, stateValue, "state");
        t.hasExitTime = false;
        t.duration = 0f;
    }

    // ─── Prefab ───────────────────────────────────────────────────────────────

    private static void BuildPrefab(AnimatorController controller, Sprite defaultSprite)
    {
        // Borrow look + shared references from the pig so the bat matches project conventions.
        GameObject pigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigPrefabPath);
        SpriteRenderer pigSr = pigPrefab != null ? pigPrefab.GetComponentInChildren<SpriteRenderer>() : null;
        AngryPig pig         = pigPrefab != null ? pigPrefab.GetComponentInChildren<AngryPig>() : null;

        GameObject root = new GameObject("Bat");
        try
        {
            var rb = root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.freezeRotation = true;

            // Body core of the 46x30 @ 16 PPU frames (2.88 x 1.88 world units) —
            // deliberately smaller than the art so wingtips don't kill the player.
            var col = root.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Horizontal;
            col.size = new Vector2(1.4f, 1.1f);

            Bat bat = root.AddComponent<Bat>();

            GameObject animGo = new GameObject("Animator");
            animGo.transform.SetParent(root.transform, false);
            var sr = animGo.AddComponent<SpriteRenderer>();
            sr.sprite = defaultSprite;
            if (pigSr != null)
            {
                sr.sharedMaterial = pigSr.sharedMaterial;
                sr.sortingLayerID = pigSr.sortingLayerID;
                sr.sortingOrder = pigSr.sortingOrder;
            }
            var animator = animGo.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            // Thin trigger poking just above the capsule top, so a falling player
            // registers a stomp before body contact — same geometry as the pig.
            GameObject head = new GameObject("HeadTrigger");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            var headCol = head.AddComponent<BoxCollider2D>();
            headCol.isTrigger = true;
            headCol.size = new Vector2(1.0f, 0.2f);
            head.AddComponent<EnemyHeadTrigger>();

            // deathVFX and sightBlockingLayers are private serialized fields on both
            // scripts, so copy them from the pig via SerializedObject.
            if (pig != null)
            {
                var pigSo = new SerializedObject(pig);
                var batSo = new SerializedObject(bat);
                batSo.FindProperty("deathVFX").objectReferenceValue =
                    pigSo.FindProperty("deathVFX").objectReferenceValue;
                batSo.FindProperty("sightBlockingLayers").intValue =
                    pigSo.FindProperty("sightBlockingLayers").intValue;
                batSo.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
