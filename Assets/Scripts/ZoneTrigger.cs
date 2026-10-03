using UnityEngine;

/// <summary>Opens doors when the player walks into the trigger box (e.g. the top of the ledge).</summary>
[RequireComponent(typeof(BoxCollider))]
public class ZoneTrigger : MonoBehaviour
{
    public ChamberDoor[] targets;
    public string message = "";
    public bool once = true;

    bool fired;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if ((fired && once) || other.GetComponent<FirstPersonController>() == null) return;
        fired = true;
        if (targets != null)
            foreach (ChamberDoor door in targets)
                if (door) door.Open();
        if (!string.IsNullOrEmpty(message) && MatterHUD.Instance) MatterHUD.Instance.Flash(message);
    }
}
