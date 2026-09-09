using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YumJump.Agent
{
    /// <summary>
    /// Brings the agent server up without any scene or prefab edits (this project builds its
    /// runtime objects in code, so the server follows the same convention).
    ///
    /// It switches on when any of these is true:
    ///   * the player was launched with -agentServer  (optionally -agentPort N, -agentLevel Level1)
    ///   * the environment variable YUMJUMP_AGENT is set to 1
    ///   * PlayerPrefs has "YumJump.AgentServer" = 1  (the editor menu under Tools/Agent Server)
    /// Otherwise the game runs exactly as before.
    /// </summary>
    public static class AgentBootstrap
    {
        public const string PrefsKey = "YumJump.AgentServer";
        public const string PrefsPortKey = "YumJump.AgentServer.Port";
        public const string PrefsAudioKey = "YumJump.AgentServer.Audio";
        private const string EnvKey = "YUMJUMP_AGENT";

        private static string pendingLevel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (!Enabled) return;

            string level = CommandLineValue("-agentLevel");
            if (!string.IsNullOrEmpty(level) && SceneManager.GetActiveScene().name != level)
            {
                // Load the requested level first, then start the server in that scene.
                pendingLevel = level;
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.LoadScene(level);
                return;
            }

            CreateServer();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (pendingLevel == null || scene.name != pendingLevel) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            pendingLevel = null;
            CreateServer();
        }

        private static void CreateServer()
        {
            if (AgentServer.Instance != null) return;

            var go = new GameObject("AgentServer");
            AgentServer server = go.AddComponent<AgentServer>();
            server.Configure(ResolvePort());
        }

        public static bool Enabled
        {
            get
            {
                if (HasCommandLineFlag("-agentServer")) return true;
                try
                {
                    if (Environment.GetEnvironmentVariable(EnvKey) == "1") return true;
                }
                catch (Exception)
                {
                    // Some platforms deny environment access; the other switches still work.
                }
                return PlayerPrefs.GetInt(PrefsKey, 0) == 1;
            }
        }

        /// <summary>
        /// Whether the game should be audible while the agent plays. Off by default: an agent
        /// session is usually a headless build running behind other work, and a level's music
        /// coming out of nowhere is the last thing anyone wants. Pass -agentAudio (or set the
        /// PlayerPrefs key) to hear it.
        /// </summary>
        public static bool AudioEnabled =>
            HasCommandLineFlag("-agentAudio") || PlayerPrefs.GetInt(PrefsAudioKey, 0) == 1;

        private static int ResolvePort()
        {
            string fromArgs = CommandLineValue("-agentPort");
            if (!string.IsNullOrEmpty(fromArgs) && int.TryParse(fromArgs, out int parsed)) return parsed;
            return PlayerPrefs.GetInt(PrefsPortKey, AgentServer.DefaultPort);
        }

        private static bool HasCommandLineFlag(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string CommandLineValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
