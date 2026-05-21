using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class TutorialOverlayCameraSetup
{
    private const string LayerName = "TutorialUI";
    private const string OverlayCameraName = "TutorialOverlayCamera";

    [MenuItem("Tools/Yum Jump/Setup Tutorial Overlay Camera")]
    public static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();

        int layer = EnsureLayer(LayerName);
        if (layer < 0)
        {
            EditorUtility.DisplayDialog("Setup Tutorial Overlay Camera",
                "No free Unity Layer slot to add '" + LayerName + "'. Free up a user layer in Project Settings > Tags and Layers, then retry.",
                "OK");
            return;
        }

        Camera main = Camera.main;
        if (main == null)
        {
            EditorUtility.DisplayDialog("Setup Tutorial Overlay Camera",
                "No camera tagged 'MainCamera' in the active scene.",
                "OK");
            return;
        }

        Undo.RecordObject(main, "Update main camera culling mask");
        main.cullingMask &= ~(1 << layer);
        EditorUtility.SetDirty(main);

        GameObject overlayGO = GameObject.Find(OverlayCameraName);
        Camera overlayCam;
        if (overlayGO == null)
        {
            overlayGO = new GameObject(OverlayCameraName);
            Undo.RegisterCreatedObjectUndo(overlayGO, "Create tutorial overlay camera");
            overlayGO.transform.SetParent(main.transform, false);
            overlayGO.transform.localPosition = Vector3.zero;
            overlayGO.transform.localRotation = Quaternion.identity;

            overlayCam = overlayGO.AddComponent<Camera>();
        }
        else
        {
            overlayCam = overlayGO.GetComponent<Camera>();
            if (overlayCam == null) overlayCam = overlayGO.AddComponent<Camera>();
            Undo.RecordObject(overlayCam, "Update overlay camera");
        }

        overlayCam.orthographic = main.orthographic;
        overlayCam.orthographicSize = main.orthographicSize;
        overlayCam.fieldOfView = main.fieldOfView;
        overlayCam.nearClipPlane = main.nearClipPlane;
        overlayCam.farClipPlane = main.farClipPlane;
        overlayCam.cullingMask = 1 << layer;
        overlayCam.clearFlags = CameraClearFlags.Depth;
        overlayCam.depth = main.depth + 1;
        EditorUtility.SetDirty(overlayCam);

        UniversalAdditionalCameraData overlayData = overlayGO.GetComponent<UniversalAdditionalCameraData>();
        if (overlayData == null) overlayData = overlayGO.AddComponent<UniversalAdditionalCameraData>();
        Undo.RecordObject(overlayData, "Update overlay URP data");
        overlayData.renderType = CameraRenderType.Overlay;
        overlayData.renderPostProcessing = false;
        EditorUtility.SetDirty(overlayData);

        UniversalAdditionalCameraData mainData = main.GetComponent<UniversalAdditionalCameraData>();
        if (mainData == null) mainData = main.gameObject.AddComponent<UniversalAdditionalCameraData>();
        Undo.RecordObject(mainData, "Update main URP data");
        if (!mainData.cameraStack.Contains(overlayCam))
            mainData.cameraStack.Add(overlayCam);
        EditorUtility.SetDirty(mainData);

        GameObject signRoot = GameObject.Find("TutorialSigns");
        if (signRoot != null)
        {
            SetLayerRecursive(signRoot, layer);
            foreach (TutorialSign s in signRoot.GetComponentsInChildren<TutorialSign>(true))
                s.Rebuild();
            EditorUtility.SetDirty(signRoot);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = overlayGO;
        EditorGUIUtility.PingObject(overlayGO);
        Debug.Log("TutorialOverlayCameraSetup: layer '" + LayerName + "' (id " + layer + ") in use; overlay camera '" + OverlayCameraName + "' stacked on main. Run the Level 1 sign menu (or Rebuild signs) and they will render crisp through the overlay.");
    }

    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;

        Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0) return -1;

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || !layers.isArray) return -1;

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
        }
        return -1;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }
}
