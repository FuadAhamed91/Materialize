using System;
using UnityEngine;

/// <summary>
/// The player's hotbar for the current chamber: what's left of each item and which one is selected.
/// Matter that is recycled, reset or dissolved in a hazard is refunded, so a room can't soft-lock.
/// </summary>
public class Inventory : MonoBehaviour
{
    public ChamberLoadout loadout;

    int[] counts = new int[0];

    public event Action Changed;

    public int Selected { get; private set; }
    public int SlotCount => loadout ? loadout.items.Length : 0;
    public InventoryItem Item(int slot) => loadout.items[slot];
    public int Count(int slot) => slot >= 0 && slot < counts.Length ? counts[slot] : 0;
    public InventoryItem SelectedItem => SlotCount > 0 ? loadout.items[Selected] : null;

    void Awake() => Load(loadout);

    /// <summary>Swaps in the next chamber's items, full counts, first slot selected.</summary>
    public void Load(ChamberLoadout next)
    {
        loadout = next;
        counts = new int[SlotCount];
        for (int i = 0; i < counts.Length; i++) counts[i] = loadout.items[i].count;
        Selected = 0;
        Changed?.Invoke();
    }

    public void Select(int slot)
    {
        if (SlotCount == 0) return;
        Selected = (slot % SlotCount + SlotCount) % SlotCount;
        Changed?.Invoke();
    }

    public bool TryTake(out InventoryItem item, out int slot)
    {
        slot = Selected;
        item = null;
        if (SlotCount == 0 || counts[slot] <= 0) return false;
        counts[slot]--;
        item = loadout.items[slot];
        Changed?.Invoke();
        return true;
    }

    /// <summary>Returns one item to the hotbar, if it came from the current chamber.</summary>
    public void Refund(ChamberLoadout from, int slot)
    {
        if (from == null || from != loadout || slot < 0 || slot >= SlotCount) return;
        counts[slot] = Mathf.Min(counts[slot] + 1, loadout.items[slot].count);
        Changed?.Invoke();
    }
}
