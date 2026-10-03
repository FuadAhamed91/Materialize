using UnityEngine;

/// <summary>Acid trench / pit floor: respawns the player at their checkpoint and dissolves matter that falls in.</summary>
[RequireComponent(typeof(BoxCollider))]
public class HazardZone : MonoBehaviour
{
    public bool affectsPlayer = true;
    public string playerMessage = "Respawning";
    public bool dissolvesMatter = true;
    public string matterMessage = "";

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponent<FirstPersonController>();
        if (player)
        {
            if (!affectsPlayer) return;
            player.Respawn();
            if (!string.IsNullOrEmpty(playerMessage) && MatterHUD.Instance)
                MatterHUD.Instance.Flash(playerMessage, new Color(1f, 0.45f, 0.35f));
            return;
        }

        if (!dissolvesMatter || other.attachedRigidbody == null) return;
        var matter = other.attachedRigidbody.GetComponent<SpawnedMatter>();
        if (matter == null || matter.IsDissolving) return;
        matter.Dissolve();
        if (!string.IsNullOrEmpty(matterMessage) && MatterHUD.Instance) MatterHUD.Instance.Flash(matterMessage);
    }
}
