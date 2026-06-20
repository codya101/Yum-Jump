using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;

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

    private void Update()
    {
        if (levelTimerRunning)
            levelTime += Time.deltaTime;
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
        if (isRespawning) return;
        StartCoroutine(RespawnCoroutine());
    }

    private IEnumerator RespawnCoroutine()
    {
        isRespawning = true;
        yield return new WaitForSeconds(respawnDelay);

        GameObject newPlayer = Instantiate(playerPrefab, respawnPoint.position, Quaternion.identity);
        player = newPlayer.GetComponent<Player>();
        isRespawning = false;
    }
    #endregion

}
