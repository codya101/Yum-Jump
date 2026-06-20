using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;

/// <summary>
/// Discovers the game's level scenes at runtime by scanning Build Settings for
/// scenes named "LevelN". Works in builds (no Editor-only APIs), so the Level
/// Select screen stays data-driven: add "Level4" to Build Settings and it shows
/// up automatically, in order.
/// </summary>
public static class LevelCatalog
{
    public struct LevelEntry
    {
        public string scene;
        public int number;
    }

    private static readonly Regex LevelNameRegex = new Regex(@"^Level(\d+)$");

    public static List<LevelEntry> GetLevels()
    {
        List<LevelEntry> levels = new List<LevelEntry>();

        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = Path.GetFileNameWithoutExtension(path);

            Match m = LevelNameRegex.Match(name);
            if (!m.Success) continue;

            levels.Add(new LevelEntry
            {
                scene = name,
                number = int.Parse(m.Groups[1].Value),
            });
        }

        levels.Sort((a, b) => a.number.CompareTo(b.number));
        return levels;
    }
}
