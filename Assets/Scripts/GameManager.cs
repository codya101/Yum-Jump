using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YumJump.Agent;

public class GameManager : SimBehaviour
{
    public static GameManager instance;

    /// <summary>The prefab the agent server re-instantiates on reset.</summary>
    public GameObject PlayerPrefabRef => playerPrefab;

    /// <summary>Where the player currently respawns (start flag, or the last checkpoint).</summary>
    public Transform RespawnPointRef => respawnPoint;

    public override SimKind Kind => SimKind.None;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private float respawnDelay;
    public Player player;

    [Header("Fruits Management")]
    public bool fruitsAreRandom;
    public int fruitsCollected;
    public int totalFruits;
    public int score;
    public Dictionary<FruitType, int> fruitsCollectedByType = new Dictionary<FruitType, int>();
    public Dictionary<FruitType, int> totalFruitsByType = new Dictionary<FruitType, int>();

    private static readonly Dictionary<FruitType, int> fruitScores = new Dictionary<FruitType, int>
    {
        { FruitType.Apple,      10 },
        { FruitType.Banana,     25 },
        { FruitType.Cherry,     50 },
        { FruitType.Orange,     100 },
        { FruitType.Strawberry, 250 },
        { FruitType.Melon,      500 },
        { FruitType.Kiwi,       750 },
        { FruitType.Pineapple,  1000 },
    };

    [Header("Checkpoints")]
    public bool canReactivate;

    [Header("Level Timer")]
    // Elapsed play time for the current level, in seconds. Ticks with
    // Time.deltaTime, so a pause (Time.timeScale = 0) freezes it automatically
    // while respawn waits still count as play time.
    public float levelTime;
    private bool levelTimerRunning = true;

    private void Awake()
    {
        if (instance == null)
            instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        CollectFruitsInfo();
    }

    protected override void SimTick()
    {
        if (levelTimerRunning)
            levelTime += SimClock.DeltaTime;
    }

    /// <summary>Puts run progress back to what it was in the snapshot being restored.</summary>
    public void AgentRestoreProgress(int restoredScore, int restoredFruits,
                                     Dictionary<FruitType, int> restoredFruitsByType)
    {
        score = restoredScore;
        fruitsCollected = restoredFruits;

        // Rolled back with the other tallies: the fruits themselves come back on a reset, so a
        // per-type count that kept accumulating would drift out of step with fruitsCollected and
        // report more of a type than the level contains. Refilled in place rather than replaced,
        // so anything holding the dictionary keeps seeing the live counts.
        fruitsCollectedByType.Clear();
        if (restoredFruitsByType != null)
        {
            foreach (KeyValuePair<FruitType, int> entry in restoredFruitsByType)
                fruitsCollectedByType[entry.Key] = entry.Value;
        }

        levelTime = 0f;
        levelTimerRunning = true;
        isRespawning = false;
    }

    /// <summary>Freezes the level timer (called when the finish is reached).</summary>
    public void StopLevelTimer() => levelTimerRunning = false;

    private void CollectFruitsInfo()
    {
        Fruit[] allFruits = FindObjectsByType<Fruit>(FindObjectsSortMode.None);
        totalFruits = allFruits.Length;

        foreach (Fruit fruit in allFruits)
        {
            FruitType type = fruit.FruitType;
            if (totalFruitsByType.ContainsKey(type))
                totalFruitsByType[type]++;
            else
                totalFruitsByType[type] = 1;
        }
    }

    public void AddFruit(FruitType fruitType)
    {
        fruitsCollected++;
        score += fruitScores[fruitType];

        if (fruitsCollectedByType.ContainsKey(fruitType))
            fruitsCollectedByType[fruitType]++;
        else
            fruitsCollectedByType[fruitType] = 1;
    }
    public bool FruitsHaveRandomLook() => fruitsAreRandom;

    #region  Respawn Management
    public void UpdateRespawnPosition(Transform newRespawnPoint) => respawnPoint = newRespawnPoint;

    private bool isRespawning;

    public void RespawnPlayer()
    {
        // In agent mode the server owns respawning: it happens on reset, at a known tick,
        // with the rest of the world restored to match.
        if (SimClock.ManualMode) return;

        if (isRespawning) return;
        StartCoroutine(RespawnCoroutine());
    }

    private IEnumerator RespawnCoroutine()
    {
        isRespawning = true;
        yield return SimClock.Wait(respawnDelay);

        GameObject newPlayer = Instantiate(playerPrefab, respawnPoint.position, Quaternion.identity);
        player = newPlayer.GetComponent<Player>();
        isRespawning = false;
    }
    #endregion

}
