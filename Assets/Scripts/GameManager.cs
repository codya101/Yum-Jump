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
    public Dictionary<FruitType, int> fruitsCollectedByType = new Dictionary<FruitType, int>();

    [Header("Checkpoints")]
    public bool canReactivate;

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

    private void CollectFruitsInfo()
    {
        Fruit[] allFruits = FindObjectsByType<Fruit>(FindObjectsSortMode.None);
        totalFruits = allFruits.Length;
    }

    public void AddFruit(FruitType fruitType)
    {
        fruitsCollected++;

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
