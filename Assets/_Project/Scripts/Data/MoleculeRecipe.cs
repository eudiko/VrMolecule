using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MoleculeRecipe
{
    public string moleculeName;          // "Water"
    public string formula;               // "H₂O"
    public string bondType;              // "Single Covalent"
    public GameObject moleculePrefab;    // drag in Inspector
    public AudioClip successSound;       // per-molecule sound (optional)

    // Ingredient counts — e.g. H=2, O=1
    public int hydrogenCount;
    public int oxygenCount;
    public int carbonCount;
    public int nitrogenCount;

    /// <summary>Total number of atoms this recipe requires — used for greedy sorting.</summary>
    public int TotalAtoms => hydrogenCount + oxygenCount + carbonCount + nitrogenCount;

    // Returns a sorted key like "C1H4" for fast lookup
    public string GetKey()
    {
        var parts = new List<string>();
        if (carbonCount > 0) parts.Add($"C{carbonCount}");
        if (hydrogenCount > 0) parts.Add($"H{hydrogenCount}");
        if (nitrogenCount > 0) parts.Add($"N{nitrogenCount}");
        if (oxygenCount > 0) parts.Add($"O{oxygenCount}");
        return string.Join("", parts);
    }
}
