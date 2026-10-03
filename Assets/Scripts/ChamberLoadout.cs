using System;
using UnityEngine;

/// <summary>One kind of object in a chamber's inventory, and how many of it the player gets.</summary>
[Serializable]
public class InventoryItem
{
    public string name;
    public int count = 1;
    public PhysicalObjectConfig config;
}

/// <summary>The objects provided for one chamber: what solves it, plus a few that don't.</summary>
public class ChamberLoadout : MonoBehaviour
{
    public InventoryItem[] items = new InventoryItem[0];
}
