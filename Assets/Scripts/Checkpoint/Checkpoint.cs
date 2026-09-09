using UnityEngine;
using YumJump.Agent;

public class Checkpoint : SimBehaviour
{
    private Animator anim => GetComponent<Animator>();
    private bool isActive;

    [SerializeField] private bool canBeReactivated;

    public override SimKind Kind => SimKind.None;   // static: it lives in the map, not the dynamics list

    protected override void SimTick() { }

    protected override object CaptureExtra() => isActive;

    protected override void RestoreExtra(object extra)
    {
        isActive = extra is bool b && b;
    }

    private void Start()
    {
        canBeReactivated = GameManager.instance.canReactivate;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isActive && canBeReactivated == false)
            return;

        Player player = collision.GetComponent<Player>();

        if (player != null)
            ActivateCheckpoint();
    }

    private void ActivateCheckpoint()
    {
        isActive = true;
        anim.SetTrigger("activate");
        AudioManager.Instance.PlayCheckpoint();
        GameManager.instance.UpdateRespawnPosition(transform);

        // Tell the agent, and have the server snapshot the whole world at this moment so a
        // later reset to this checkpoint restores hazard phases too, not just the player.
        SimEvents.ReportCheckpoint(SimId);
        ResetController.RequestCheckpointCapture(SimId, transform);
    }
}
