using System.Collections.Generic;
using UnityEngine;
using YumJump.Agent;

public enum FruitType
{
    Apple,
    Banana,
    Cherry,
    Kiwi,
    Melon,
    Orange,
    Pineapple,
    Strawberry
}

public class Fruit : SimBehaviour
{
    [SerializeField] private FruitType fruitType;
    public FruitType FruitType => fruitType;
    [SerializeField] private GameObject pickupVFX;

    private GameManager gameManager;
    private Animator anim;

    public override SimKind Kind => SimKind.Collectible;

    protected override void SimTick() { }

    /// <summary>
    /// Which fruit this is, in the dynamics entry the agent reads. The wire id is built from
    /// the class name (<c>fruit_7</c>), so without this the type never crosses and every
    /// collectible looks alike.
    ///
    /// <para>The enum name and not its point value: what a fruit is worth is a rule about
    /// scoring, and invariant I5 keeps interpretation on the client. The agent already knows
    /// the table; what it could not know is which fruit it is looking at.</para>
    /// </summary>
    public override void DescribeTo(Dictionary<string, object> fields)
    {
        fields["fruitType"] = fruitType.ToString();
    }

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        gameManager = GameManager.instance;
        SetRandomLookIfNeeded();
    }

    private void SetRandomLookIfNeeded()
    {
        if (gameManager.FruitsHaveRandomLook() == false)
        {
            UpdateFruitVisuals();
            return;
        }

        // A random sprite would make two runs of the same plan look different; in agent mode
        // the fruit keeps its own type so replays are identical down to the pixels.
        int randomIndex = SimClock.ManualMode ? (int)fruitType : Random.Range(0, 8);
        anim.SetFloat("fruitIndex", randomIndex);
    }

    private void UpdateFruitVisuals() => anim.SetFloat("fruitIndex", (int)fruitType);

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Player player = collision.GetComponent<Player>();

        if (player != null)
        {
            gameManager.AddFruit(fruitType);
            AudioManager.Instance.PlayPickup();
            SimEvents.ReportFruitCollected(SimId);
            SimObjects.Despawn(gameObject);

            GameObject newVFX = Instantiate(pickupVFX, transform.position, Quaternion.identity);
        }
    }
}
