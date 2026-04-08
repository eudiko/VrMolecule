using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MoleculeDB", menuName = "Chemistry Lab/Molecule Database")]
public class MoleculeDatabase : ScriptableObject
{
    [SerializeField] private List<MoleculeRecipe> recipes = new();

    // Exact-match lookup (key → recipe)
    private Dictionary<string, MoleculeRecipe> _lookup;

    // All recipes sorted largest → smallest (by total atom count)
    // so LookupBest always prefers bigger molecules.
    private List<MoleculeRecipe> _sortedRecipes;

    public void Initialize()
    {
        _lookup = new Dictionary<string, MoleculeRecipe>();
        foreach (var r in recipes)
            _lookup[r.GetKey()] = r;

        // Sort descending by total atom count so H2O (3) beats H2 (2).
        _sortedRecipes = new List<MoleculeRecipe>(recipes);
        _sortedRecipes.Sort((a, b) => b.TotalAtoms.CompareTo(a.TotalAtoms));

        Debug.Log($"[MoleculeDB] Loaded {recipes.Count} recipe(s).");
    }

    /// <summary>
    /// Exact match: h/o/c/n must equal the recipe exactly.
    /// </summary>
    public MoleculeRecipe Lookup(int h, int o, int c, int n)
    {
        var dummy = new MoleculeRecipe
        {
            hydrogenCount  = h,
            oxygenCount    = o,
            carbonCount    = c,
            nitrogenCount  = n
        };
        return _lookup.TryGetValue(dummy.GetKey(), out var result) ? result : null;
    }

    /// <summary>
    /// Best-fit match: returns the LARGEST recipe whose atom requirements
    /// are fully satisfied by the available counts (h, o, c, n).
    ///
    /// Example: cluster has H=2, O=1.
    ///   - H2O  needs H=2, O=1 → fits → BEST (3 atoms)
    ///   - H2   needs H=2      → also fits but only 2 atoms → skipped
    ///
    /// Example: cluster has H=2, O=0.
    ///   - H2O  needs O=1      → does NOT fit
    ///   - H2   needs H=2      → fits → returned
    /// </summary>
    public MoleculeRecipe LookupBest(int h, int o, int c, int n)
    {
        if (_sortedRecipes == null) return null;

        // Guard: if no atoms are available at all, never match.
        if (h == 0 && o == 0 && c == 0 && n == 0) return null;

        foreach (var r in _sortedRecipes)
        {
            // Recipe must need at least 1 atom AND all counts must fit.
            if (r.TotalAtoms > 0      &&
                r.hydrogenCount  <= h &&
                r.oxygenCount    <= o &&
                r.carbonCount    <= c &&
                r.nitrogenCount  <= n)
            {
                return r;
            }
        }
        return null;
    }

    public IReadOnlyList<MoleculeRecipe> AllRecipes => recipes;
}
