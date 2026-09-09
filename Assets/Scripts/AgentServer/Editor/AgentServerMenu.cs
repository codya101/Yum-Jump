using UnityEditor;
using UnityEngine;
using YumJump.Agent;

namespace YumJump.AgentEditor
{
    /// <summary>
    /// Editor switches for the agent bridge. Turning it on makes the next Play-mode session start
    /// the TCP server and hand control of the world to the agent; turning it off restores normal
    /// keyboard play. Nothing else in the project changes.
    /// </summary>
    public static class AgentServerMenu
    {
        private const string EnabledMenu = "Tools/Agent Server/Enabled in Play Mode";
        private const string PortMenu = "Tools/Agent Server/Set Port...";
        private const string AudioMenu = "Tools/Agent Server/Play Audio While Agent Plays";
        private const string HelpMenu = "Tools/Agent Server/How to run the agent";

        [MenuItem(EnabledMenu, priority = 0)]
        private static void ToggleEnabled()
        {
            bool enabled = PlayerPrefs.GetInt(AgentBootstrap.PrefsKey, 0) == 1;
            PlayerPrefs.SetInt(AgentBootstrap.PrefsKey, enabled ? 0 : 1);
            PlayerPrefs.Save();
            Debug.Log($"[AgentServer] {(enabled ? "disabled" : "enabled")} for Play mode " +
                      $"(port {PlayerPrefs.GetInt(AgentBootstrap.PrefsPortKey, AgentServer.DefaultPort)}).");
        }

        [MenuItem(EnabledMenu, validate = true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(EnabledMenu, PlayerPrefs.GetInt(AgentBootstrap.PrefsKey, 0) == 1);
            return true;
        }

        [MenuItem(AudioMenu, priority = 1)]
        private static void ToggleAudio()
        {
            bool audible = PlayerPrefs.GetInt(AgentBootstrap.PrefsAudioKey, 0) == 1;
            PlayerPrefs.SetInt(AgentBootstrap.PrefsAudioKey, audible ? 0 : 1);
            PlayerPrefs.Save();
        }

        [MenuItem(AudioMenu, validate = true)]
        private static bool ToggleAudioValidate()
        {
            Menu.SetChecked(AudioMenu, PlayerPrefs.GetInt(AgentBootstrap.PrefsAudioKey, 0) == 1);
            return true;
        }

        [MenuItem(PortMenu, priority = 2)]
        private static void SetPort()
        {
            AgentPortWindow.Open();
        }

        [MenuItem(HelpMenu, priority = 20)]
        private static void Help()
        {
            EditorUtility.DisplayDialog(
                "Running the agent",
                "1. Tools > Agent Server > Enabled in Play Mode\n" +
                "2. Open the level scene (e.g. Level1) and press Play.\n" +
                "   The world freezes: it only advances while the agent runs a plan.\n" +
                "3. In a terminal, from Yum-Jump-LLM:\n" +
                "     python -m yumjump_agent play --speed 4\n\n" +
                "Useful checks before a full run:\n" +
                "     python -m yumjump_agent determinism\n" +
                "     python -m yumjump_agent map --save level.txt",
                "OK");
        }
    }

    /// <summary>Tiny prompt for the listen port, stored in PlayerPrefs.</summary>
    public sealed class AgentPortWindow : EditorWindow
    {
        private int port;

        public static void Open()
        {
            var window = GetWindow<AgentPortWindow>(true, "Agent Server Port");
            window.port = PlayerPrefs.GetInt(AgentBootstrap.PrefsPortKey, AgentServer.DefaultPort);
            window.minSize = new Vector2(260, 90);
            window.maxSize = new Vector2(260, 90);
        }

        private void OnGUI()
        {
            port = EditorGUILayout.IntField("Port", port);
            EditorGUILayout.Space();
            if (GUILayout.Button("Save"))
            {
                PlayerPrefs.SetInt(AgentBootstrap.PrefsPortKey, Mathf.Clamp(port, 1024, 65535));
                PlayerPrefs.Save();
                Close();
            }
        }
    }
}
