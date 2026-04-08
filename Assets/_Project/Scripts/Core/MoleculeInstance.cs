using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached dynamically by BondManager to every spawned molecule GameObject.
/// Stores which recipe it represents and which atoms were consumed,
/// so those atoms can be reclaimed when the molecule is upgraded.
///
/// A SphereCollider trigger is added at runtime so that AtomController's
/// OverlapSphere can detect nearby molecules for upgrade checks.
/// </summary>
public class MoleculeInstance : MonoBehaviour
{
    public MoleculeRecipe         Recipe;
    public List<AtomController>   SourceAtoms = new();

    [Tooltip("How far (m) an atom must be to trigger an upgrade check.")]
    public float interactionRadius = 0.35f;

    private void Awake()
    {
        // Add a trigger sphere so OverlapSphere can find this molecule.
        var col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius    = interactionRadius;
    }
}
