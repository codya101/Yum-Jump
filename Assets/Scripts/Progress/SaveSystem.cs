using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent progress store for the whole game. Backed by a single PlayerPrefs
/// string holding JSON (via <see cref="JsonUtility"/>), so it survives quitting
/// and relaunching. There is no scene/prefab setup: every API lazily loads on
/// first use.
///
/// What it tracks, per level scene:
///   - beaten      : the level was finished with enough score (unlocks the next)
///   - bestTime    : fastest completion time in seconds (-1 = never completed)
///   - bestFruits  : most fruit collected on ANY attempt (complete or not)
///   - totalFruits : how many fruit the level contains (learned once played)
///
/// Unlocking is chain-based: Level 1 is always unlocked; Level N is unlocked
/// once Level N-1 is beaten. Mid-level checkpoint progress is intentionally NOT
/// stored here — quitting always restarts a level from its StartPoint.
/// </summary>
public static class SaveSystem
{
    private const string Key = "YumJumpSave_v1";

    [Serializable]
    public class LevelProgress
    {
        public string scene;
        public int levelNumber;
        public bool beaten;
        public float bestTime = -1f; // -1 = no completion yet
        public int bestFruits;
        public int totalFruits;      // 0 = level never played, so total unknown
    }

    [Serializable]
    private class SaveData
    {
        public List<LevelProgress> levels = new List<LevelProgress>();
    }

    private static SaveData data;

    private static SaveData Data
    {
        get
        {
            if (data == null) Load();
            return data;
        }
    }

    private static void Load()
    {
        string json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json))
        {
            data = new SaveData();
            return;
        }

        try
        {
            data = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] Failed to parse save, starting fresh. {e.Message}");
            data = new SaveData();
        }
    }

    private static void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
        PlayerPrefs.Save();
    }

    /// <summary>Returns the record for a scene, creating an empty one if needed.</summary>
    public static LevelProgress GetLevel(string scene)
    {
        foreach (LevelProgress lp in Data.levels)
        {
            if (lp.scene == scene) return lp;
        }

        LevelProgress created = new LevelProgress
        {
            scene = scene,
            levelNumber = ParseLevelNumber(scene),
        };
        Data.levels.Add(created);
        return created;
    }

    /// <summary>
    /// Level 1 is always playable; any later level unlocks only once the level
    /// directly before it has been beaten.
    /// </summary>
    public static bool IsUnlocked(int levelNumber)
    {
        if (levelNumber <= 1) return true;

        foreach (LevelProgress lp in Data.levels)
        {
            if (lp.levelNumber == levelNumber - 1)
                return lp.beaten;
        }
        return false;
    }

    /// <summary>
    /// Records a finished run: marks the level beaten (unlocking the next),
    /// keeps the fastest time, and the highest fruit count / level total.
    /// </summary>
    public static void RecordCompletion(string scene, int levelNumber, float time, int fruits, int totalFruits)
    {
        LevelProgress lp = GetLevel(scene);
        lp.levelNumber = levelNumber;
        lp.beaten = true;

        if (lp.bestTime < 0f || time < lp.bestTime)
            lp.bestTime = time;

        if (fruits > lp.bestFruits) lp.bestFruits = fruits;
        if (totalFruits > 0) lp.totalFruits = totalFruits;

        Save();
    }

    /// <summary>
    /// Records an unfinished attempt: only the best fruit count and the level
    /// total are kept (no completion, no time), so fruit grabbed on a run you
    /// quit out of still counts toward "most fruit on any attempt".
    /// </summary>
    public static void RecordAttempt(string scene, int levelNumber, int fruits, int totalFruits)
    {
        LevelProgress lp = GetLevel(scene);
        lp.levelNumber = levelNumber;

        if (fruits > lp.bestFruits) lp.bestFruits = fruits;
        if (totalFruits > 0) lp.totalFruits = totalFruits;

        Save();
    }

    /// <summary>Erases all saved progress (used by New Game).</summary>
    public static void Wipe()
    {
        data = new SaveData();
        Save();
    }

    /// <summary>"Level1" -> 1. Returns 0 when there is no trailing number.</summary>
    public static int ParseLevelNumber(string scene)
    {
        if (string.IsNullOrEmpty(scene)) return 0;

        int end = scene.Length;
        int start = end;
        while (start > 0 && char.IsDigit(scene[start - 1])) start--;

        if (start == end) return 0;
        return int.TryParse(scene.Substring(start, end - start), out int n) ? n : 0;
    }

    /// <summary>Formats seconds as m:ss (e.g. 67f -> "1:07").</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) return "--:--";
        int total = Mathf.FloorToInt(seconds);
        return (total / 60) + ":" + (total % 60).ToString("00");
    }
}
