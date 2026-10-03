using UnityEngine;

/// <summary>Trigger in each exit vestibule: moves the player to the next chamber, or ends the run.</summary>
[RequireComponent(typeof(BoxCollider))]
public class ChamberExit : MonoBehaviour
{
    public Transform nextSpawn;
    public string nextTitle;
    [TextArea] public string nextObjective;
    public string completionMessage = "ALL CHAMBERS COMPLETE · MATTER SYNTHESIS CERTIFIED";

    bool used;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponent<FirstPersonController>();
        if (player == null || used) return;
        used = true;

        MatterHUD hud = MatterHUD.Instance;
        if (nextSpawn)
        {
            player.SetCheckpoint(nextSpawn);
            player.Teleport(nextSpawn.position, nextSpawn.eulerAngles.y);
            if (hud)
            {
                hud.SetObjective(nextTitle, nextObjective);
                hud.Flash(nextTitle, 4f);
            }
        }
        else if (hud)
        {
            hud.SetObjective("COMPLETE", completionMessage);
            hud.Flash(completionMessage, new Color(0.5f, 1f, 0.6f), 8f);
        }
    }
}
