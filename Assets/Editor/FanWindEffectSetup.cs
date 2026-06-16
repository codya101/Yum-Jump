using System.IO;
using UnityEditor;
using UnityEngine;

// Adds a "white wind streaks" particle effect to the Fan prefab so the updraft
// column is visible. Editing the prefab propagates the effect to every fan
// instance in every scene (Level3 included). Run via:
//   Tools > Yum Jump > Add Fan Wind Effect
public static class FanWindEffectSetup
{
    private const string PrefabPath   = "Assets/Prefabs/Traps/Fan.prefab";
    private const string ChildName    = "WindStreaks";
    private const string TexturePath  = "Assets/Graphics/Traps/Fan/WindStreak.png";
    private const string MaterialPath = "Assets/Graphics/Traps/Fan/WindStreak.mat";

    // Updraft column, read from the prefab's PolygonCollider2D trigger:
    // wide section spans x[-7, 7], y[~7.4, ~32.9]. We emit a band across the
    // bottom of that column and push streaks up through it.
    private const float ColumnWidth = 12.5f;  // a little inside the ±7 walls
    private const float EmitY       = 6.5f;   // bottom of the float zone
    private const float TravelSpeed = 11f;    // ~matches Fan.updraftSpeed (9)
    private const float ColumnTop   = 32.5f;

    [MenuItem("Tools/Yum Jump/Add Fan Wind Effect")]
    public static void Setup()
    {
        Texture2D tex = EnsureTexture();
        Material mat = EnsureMaterial(tex);

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform existing = root.transform.Find(ChildName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            GameObject go = new GameObject(ChildName);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ConfigureParticles(go, mat, root.GetComponent<SpriteRenderer>());

            // Looping wind SFX (driven by the SFX volume slider). WindAudioLoop spawns
            // its own AudioSource on a child emitter partway up the column at runtime.
            if (root.GetComponent<WindAudioLoop>() == null)
                root.AddComponent<WindAudioLoop>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log("FanWindEffectSetup: added '" + ChildName + "' wind streaks + looping wind SFX to " +
                  PrefabPath + ". All Fan instances are updated. Save any open scenes.");
    }

    private static void ConfigureParticles(GameObject go, Material mat, SpriteRenderer fanSprite)
    {
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float lifetime = (ColumnTop - EmitY) / TravelSpeed; // travel the full column

        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.85f, lifetime * 1.1f);
        main.startSpeed = 0f; // movement comes from velocityOverLifetime
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.4f); // thin: width of the streak
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 30f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(ColumnWidth, 1.5f, 0.1f);
        shape.position = new Vector3(0f, EmitY, 0f);
        shape.rotation = Vector3.zero;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // all three axes must share the same MinMaxCurve mode (two-constants here)
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(TravelSpeed * 0.9f, TravelSpeed * 1.15f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // slow, broad sway so streaks curve into wavy gusts instead of darting like confetti
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        noise.frequency = 0.16f;   // low frequency -> long, smooth S-curves
        noise.scrollSpeed = 0.9f;
        noise.damping = true;
        noise.octaveCount = 2;     // a touch of detail for a more organic flow

        // fade in at the bottom, fade out near the top
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.18f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 3.5f;
        renderer.velocityScale = 0.28f; // much longer streaks the faster they move
        renderer.cameraVelocityScale = 0f;
        renderer.material = mat;
        renderer.alignment = ParticleSystemRenderSpace.View;
        if (fanSprite != null)
        {
            renderer.sortingLayerID = fanSprite.sortingLayerID; // same layer as the fan
            renderer.sortingOrder = fanSprite.sortingOrder - 1; // tuck behind the blades
        }
    }

    private static Texture2D EnsureTexture()
    {
        if (File.Exists(TexturePath))
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (existing != null) return existing;
        }

        const int w = 32, h = 96;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float ny = (y + 0.5f) / h * 2f - 1f;          // -1..1 vertical
            float vert = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(ny), 4f)); // long, soft ends
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) / w * 2f - 1f;        // -1..1 horizontal
                float horiz = Mathf.Clamp01(1f - nx * nx);  // soft edges
                float a = horiz * vert;
                a = a * a;                                   // crisper core
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

        var ti = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    private static Material EnsureMaterial(Texture2D tex)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Sprites/Default"); // unlit, alpha-blended, respects particle color
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.mainTexture = tex;
        mat.color = Color.white;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
