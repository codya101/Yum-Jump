using UnityEngine;
using YumJump.Agent;

public class StartPoint : SimBehaviour
{
    private Animator anim => GetComponent<Animator>();

    public override SimKind Kind => SimKind.None;

    protected override void SimTick() { }

    private void OnTriggerExit2D(Collider2D collision)
    {
        Player player = collision.GetComponent<Player>();

        if (player != null)
        {
            anim.SetTrigger("activate");
            AudioManager.Instance.PlayStartFlag();
        }
    }
}
