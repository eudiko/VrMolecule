using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BondManager : MonoBehaviour
{
    public static BondManager Instance { get; private set; }

    [SerializeField] private MoleculeDatabase database;
    [SerializeField] private Transform moleculeSpawnParent;

    [Header("Bonding behaviour")]
    [Tooltip("How long (seconds) to wait after detecting a small match before " +
             "actually forming it — gives the player time to add more atoms. " +
             "Set to 0 to bond immediately.")]
    [SerializeField] private float pendingBondDelay = 1.2f;

    public event Action<MoleculeRecipe> OnMoleculeFormed;
    public event Action OnMoleculeBroken;

    private readonly HashSet<string>              _discovered  = new();
    private readonly List<GameObject>             _activeMols  = new();
    private readonly List<List<AtomController>>   _atomGroups  = new();

    // Pending coroutines keyed by the trigger atom so we can cancel/upgrade them.
    private readonly Dictionary<AtomController, Coroutine> _pending = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (database == null)
        {
            Debug.LogError("[BondManager] Database is NULL — drag MoleculeDB into Inspector!");
            return;
        }
        database.Initialize();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by AtomController after release.  Selects the BEST (most-atoms)
    /// recipe that the current cluster satisfies, then either forms it immediately
    /// (if it uses ALL atoms in the cluster) or starts a short pending window so
    /// the player can add more atoms to beat a smaller sub-recipe.
    /// </summary>
    public void TryBond(AtomController trigger, List<AtomController> nearby)
    {
        if (database == null) return;

        // Build full candidate group (trigger + neighbours).
        var group = new List<AtomController> { trigger };
        foreach (var a in nearby)
            if (!group.Contains(a)) group.Add(a);

        // Count atom types in the cluster.
        int h = 0, o = 0, c = 0, n = 0;
        foreach (var a in group)
            switch (a.atomType)
            {
                case AtomType.H: h++; break;
                case AtomType.O: o++; break;
                case AtomType.C: c++; break;
                case AtomType.N: n++; break;
            }

        Debug.Log($"[BondManager] TryBond cluster → H{h} O{o} C{c} N{n}");

        // ── Greedy: look for the LARGEST recipe the cluster satisfies ──────────
        // This means if you have H2 + O in range, H2O beats H2.
        var recipe = database.LookupBest(h, o, c, n);
        if (recipe == null)
        {
            Debug.Log("[BondManager] No matching recipe for this cluster.");
            return;
        }

        Debug.Log($"[BondManager] Best recipe found: {recipe.moleculeName} " +
                  $"(needs H{recipe.hydrogenCount} O{recipe.oxygenCount} " +
                  $"C{recipe.carbonCount} N{recipe.nitrogenCount})");

        // Trim the group to exactly what the recipe needs.
        var recipeGroup = TrimToRecipe(group, recipe);

        // ── Does the recipe use ALL atoms in the full cluster? ─────────────────
        // If yes: bond immediately — we have the perfect set.
        // If no:  the cluster has MORE atoms than needed by the best recipe
        //         (e.g. 2 H only, but H2 is the best match and O might come soon).
        //         Start a pending countdown so the player can add more atoms
        //         before we lock in the smaller molecule.
        bool exactFit = recipeGroup.Count == group.Count;

        if (exactFit || pendingBondDelay <= 0f)
        {
            CancelPending(trigger);
            FormMolecule(recipe, recipeGroup);
        }
        else
        {
            // Cancel any existing pending bond for this trigger.
            CancelPending(trigger);
            var co = StartCoroutine(PendingBond(trigger, recipe, recipeGroup, group));
            _pending[trigger] = co;
            Debug.Log($"[BondManager] Pending '{recipe.moleculeName}' " +
                      $"— waiting {pendingBondDelay:F1}s for more atoms.");
        }
    }

    /// <summary>Called by AtomController when an atom is grabbed again —
    /// cancels any pending bond it was part of.</summary>
    public void CancelPendingForAtom(AtomController atom)
    {
        CancelPending(atom);
    }

    // ── Pending bond coroutine ────────────────────────────────────────────────

    private IEnumerator PendingBond(AtomController trigger,
                                    MoleculeRecipe  smallRecipe,
                                    List<AtomController> smallGroup,
                                    List<AtomController> originalCluster)
    {
        yield return new WaitForSeconds(pendingBondDelay);
        _pending.Remove(trigger);

        // Re-check: any of the atoms grabbed or in a molecule now?
        foreach (var a in smallGroup)
            if (a == null || a.IsGrabbed || a.IsInMolecule) yield break;

        // Re-run the cluster search from trigger to see if new atoms arrived.
        // Build fresh nearby list by peeking at what's around right now.
        var refreshedNearby = new List<AtomController>();
        foreach (var a in smallGroup) if (a != trigger) refreshedNearby.Add(a);
        TryBond(trigger, refreshedNearby);   // will find best recipe again
    }

    // ── Molecule formation ────────────────────────────────────────────────────

    private void FormMolecule(MoleculeRecipe recipe, List<AtomController> atoms)
    {
        if (recipe.moleculePrefab == null)
        {
            Debug.LogError($"[BondManager] Recipe '{recipe.moleculeName}' has no prefab!");
            return;
        }

        var center = Vector3.zero;
        foreach (var a in atoms) center += a.transform.position;
        center /= atoms.Count;
        center += Vector3.up * 0.05f;

        var parent = moleculeSpawnParent != null ? moleculeSpawnParent : transform;
        var mol    = Instantiate(recipe.moleculePrefab, center, Quaternion.identity, parent);

        foreach (var a in atoms)
        {
            a.MarkInMolecule(true);
            a.gameObject.SetActive(false);
        }

        _activeMols.Add(mol);
        _atomGroups.Add(new List<AtomController>(atoms));
        bool isNew = _discovered.Add(recipe.formula);

        OnMoleculeFormed?.Invoke(recipe);
        AudioManager.Instance?.PlayBondSuccess(recipe.successSound);

        Debug.Log($"[BondManager] Formed {recipe.moleculeName}" +
                  (isNew ? " — FIRST DISCOVERY!" : ""));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// From the full group, pick exactly the atoms the recipe needs
    /// (greedy: first-found per type).
    /// </summary>
    private static List<AtomController> TrimToRecipe(List<AtomController> group,
                                                      MoleculeRecipe recipe)
    {
        int needH = recipe.hydrogenCount;
        int needO = recipe.oxygenCount;
        int needC = recipe.carbonCount;
        int needN = recipe.nitrogenCount;

        var result = new List<AtomController>();
        foreach (var a in group)
        {
            switch (a.atomType)
            {
                case AtomType.H when needH > 0: result.Add(a); needH--; break;
                case AtomType.O when needO > 0: result.Add(a); needO--; break;
                case AtomType.C when needC > 0: result.Add(a); needC--; break;
                case AtomType.N when needN > 0: result.Add(a); needN--; break;
            }
        }
        return result;
    }

    private void CancelPending(AtomController trigger)
    {
        if (_pending.TryGetValue(trigger, out var co))
        {
            if (co != null) StopCoroutine(co);
            _pending.Remove(trigger);
        }
    }

    // ── Break molecules ───────────────────────────────────────────────────────

    public void BreakLastMolecule()
    {
        if (_activeMols.Count == 0) return;
        int i = _activeMols.Count - 1;
        Destroy(_activeMols[i]); _activeMols.RemoveAt(i);
        foreach (var a in _atomGroups[i]) a.ResetAtom();
        _atomGroups.RemoveAt(i);
        OnMoleculeBroken?.Invoke();
        AudioManager.Instance?.PlayReset();
    }

    public void BreakAllMolecules()
    {
        while (_activeMols.Count > 0) BreakLastMolecule();
    }

    public IReadOnlyCollection<string> DiscoveredMolecules => _discovered;
}
