using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TutorialSetupLevel1
{
    private const string RootName = "TutorialSigns";

    [MenuItem("Tools/Yum Jump/Setup Level 1 Tutorial Signs")]
    public static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Level1")
        {
            if (!EditorUtility.DisplayDialog("Setup Tutorial Signs",
                "Active scene is '" + scene.name + "', not 'Level1'.\nAdd signs anyway?",
                "Yes", "Cancel"))
                return;
        }

        GameObject root = GameObject.Find(RootName);
        if (root != null)
        {
            int choice = EditorUtility.DisplayDialogComplex("Setup Tutorial Signs",
                "A '" + RootName + "' GameObject already exists.",
                "Replace", "Cancel", "Just Select");
            if (choice == 1) return;
            if (choice == 2) { Selection.activeGameObject = root; EditorGUIUtility.PingObject(root); return; }
            Undo.DestroyObjectImmediate(root);
            root = null;
        }

        root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create TutorialSigns");

        AddSign(root.transform, "1_Welcome",    new Vector3(-58f, -7f, 0.1f),
                "Welcome to Yum Jump!",
                new TutorialSign.KeyKind[0]);

        AddSign(root.transform, "2_Move",       new Vector3(-52f, -7f, 0.1f),
                "Use these keys to move",
                new[] { TutorialSign.KeyKind.A, TutorialSign.KeyKind.D },
                keycapSpacingPx: 24f);

        AddSign(root.transform, "3_Jump",       new Vector3(-47f, -7f, 0.1f),
                "Press to jump",
                new[] { TutorialSign.KeyKind.Space });

        AddSign(root.transform, "4_DoubleJump", new Vector3(-42f, -7f, 0.1f),
                "Tap again in midair\nto double jump",
                new[] { TutorialSign.KeyKind.Space });

        AddSign(root.transform, "5_WallJump",   new Vector3(-37f, -7f, 0.1f),
                "Hold S or D against a wall to wall slide.\nPress Space to wall jump.\nChain wall jumps to scale a wall quickly!",
                new TutorialSign.KeyKind[0]);

        AddSign(root.transform, "6_Fruits",      new Vector3(-32f, -7f, 0.1f),
                "Collect fruits to add to your score.",
                new TutorialSign.KeyKind[0]);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log("TutorialSetupLevel1: created 5 tutorial signs under '" + RootName + "'. Drag them in the Scene view to fine-tune positions, then save the scene.");
    }

    private static void AddSign(Transform parent, string name, Vector3 worldPos, string message,
                                TutorialSign.KeyKind[] keys, float? keycapSpacingPx = null)
    {
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        TutorialSign sign = go.AddComponent<TutorialSign>();
        sign.message = message;
        sign.keys = keys;
        if (keycapSpacingPx.HasValue) sign.keycapSpacingPx = keycapSpacingPx.Value;
        sign.Rebuild();
    }
}
