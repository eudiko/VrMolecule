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
    [Tooltip("Seconds to wait after a SMALL recipe matches before locking it in. " +
             "Gives the player time to add more atoms. 0 = instant.")]
    [SerializeField] private float pendingBondDelay = 1.2f;

    public event Action<MoleculeRecipe> OnMoleculeFormed;
    public event Action                 OnMoleculeBroken;

    private readonly HashSet<string>            _discovered = new();
    private readonly List<GameObject>           _activeMols = new();
    private readonly List<List<AtomController>> _atomGroups = new();

    // Pending coroutines keyed by trigger atom so they can be cancelled.
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
    /// Entry point from AtomController.CheckBond.
    ///
    /// Strategy (in priority order):
    ///  1. UPGRADE  — Can free atoms + a nearby formed molecule make a BIGGER molecule?
    ///               If yes, destroy the old molecule and spawn the new one immediately.
    ///  2. NEW BOND  — Can free atoms alone make a molecule?
    ///               If the recipe uses ALL atoms in the cluster → immediate.
    ///               If it only uses a subset → start a pending delay so the player
    ///               can add more atoms before we commit.
    /// </summary>
    public void TryBond(AtomController            trigger,
                        List<AtomController>      freeNearby,
                        List<MoleculeInstance>    nearbyMolecules = null)
    {
        if (database == null) return;

        // Build the free cluster.
        var freeGroup = new List<AtomController> { trigger };
        foreach (var a in freeNearby)
            if (!freeGroup.Contains(a)) freeGroup.Add(a);

        CountAtoms(freeGroup, out int fh, out int fo, out int fc, out int fn);
        Debug.Log($"[BondManager] TryBond — free atoms: H{fh} O{fo} C{fc} N{fn}");

        // ── 1. Try upgrade with each nearby molecule ───────────────────────────
        if (nearbyMolecules != null && nearbyMolecules.Count > 0)
        {
            MoleculeInstance bestMol      = null;
            MoleculeRecipe   bestUpgrade  = null;
            int              bestSize     = 0;

            foreach (var mol in nearbyMolecules)
            {
                if (mol == null || mol.Recipe == null) continue;

                // Combined atom counts: free atoms on hand + atoms locked in molecule.
                int ch = fh + mol.Recipe.hydrogenCount;
                int co = fo + mol.Recipe.oxygenCount;
                int cc = fc + mol.Recipe.carbonCount;
                int cn = fn + mol.Recipe.nitrogenCount;

                var candidate = database.LookupBest(ch, co, cc, cn);

                // Only count it as an upgrade if it is BIGGER than the existing molecule.
                if (candidate != null &&
                    candidate.TotalAtoms > mol.Recipe.TotalAtoms &&
                    candidate.TotalAtoms > bestSize)
                {
                    bestMol     = mol;
                    bestUpgrade = candidate;
                    bestSize    = candidate.TotalAtoms;
                }
            }

            if (bestUpgrade != null && bestMol != null)
            {
                Debug.Log($"[BondManager] UPGRADE: {bestMol.Recipe.moleculeName} " +
                          $"→ {bestUpgrade.moleculeName}");

                // Collect the reclaimed atoms from the old molecule.
                var reclaimedAtoms = new List<AtomController>(bestMol.SourceAtoms);

                // Silently break the old molecule (no audio / event — it's being upgraded).
                BreakMoleculeInstance(bestMol);

                // Build the combined pool: free atoms + reclaimed atoms.
                var upgradePool = new List<AtomController>(freeGroup);
                foreach (var a in reclaimedAtoms)
                    if (!upgradePool.Contains(a)) upgradePool.Add(a);

                // Trim to exactly what the new recipe needs.
                var upgradeGroup = TrimToRecipe(upgradePool, bestUpgrade);

                CancelPending(trigger);
                FormMolecule(bestUpgrade, upgradeGroup);
                return;
            }
        }

        // ── 2. Plain bond from free atoms only ────────────────────────────────
        var recipe = database.LookupBest(fh, fo, fc, fn);
        if (recipe == null)
        {
            Debug.Log("[BondManager] No recipe matches the current cluster.");
            return;
        }

        Debug.Log($"[BondManager] Best free-atom recipe: {recipe.moleculeName}");
        var recipeGroup = TrimToRecipe(freeGroup, recipe);

        // If recipe uses all atoms in the cluster → bond immediately.
        // If it uses fewer → start a pending window (player may add more atoms).
        bool exactFit = recipeGroup.Count == freeGroup.Count;
        if (exactFit || pendingBondDelay <= 0f)
        {
            CancelPending(trigger);
            FormMolecule(recipe, recipeGroup);
        }
        else
        {
            CancelPending(trigger);
            var co = StartCoroutine(PendingBond(trigger, recipe, recipeGroup, freeGroup, nearbyMolecules));
            _pending[trigger] = co;
            Debug.Log($"[BondManager] Pending '{recipe.moleculeName}' — " +
                      $"waiting {pendingBondDelay:F1}s for more atoms.");
        }
    }

    /// <summary>Cancel any pending bond that involves this atom.</summary>
    public void CancelPendingForAtom(AtomController atom) => CancelPending(atom);

    // ── Pending bond ──────────────────────────────────────────────────────────

    private IEnumerator PendingBond(AtomController         trigger,
                                    MoleculeRecipe         recipe,
                                    List<AtomController>   group,
                                    List<AtomController>   originalCluster,
                                    List<MoleculeInstance> nearbyMolecules)
    {
        yield return new WaitForSeconds(pendingBondDelay);
        _pending.Remove(trigger);

        // Bail if any atom was moved or already consumed.
        foreach (var a in group)
            if (a == null || a.IsGrabbed || a.IsInMolecule) yield break;

        // Re-run with the same neighbourhood — a fresh TryBond call will
        // pick up any atoms that arrived during the wait.
        var refreshNearby = new List<AtomController>(originalCluster);
        refreshNearby.Remove(trigger);
        TryBond(trigger, refreshNearby, nearbyMolecules);
    }

    // ── Molecule formation ────────────────────────────────────────────────────

    private void FormMolecule(MoleculeRecipe recipe, List<AtomController> atoms)
    {
        if (recipe.moleculePrefab == null)
        {
            Debug.LogError($"[BondManager] Recipe '{recipe.moleculeName}' has no prefab assigned!");
            return;
        }

        var center = Vector3.zero;
        foreach (var a in atoms) center += a.transform.position;
        center /= atoms.Count;
        center += Vector3.up * 0.05f;

        var parent = moleculeSpawnParent != null ? moleculeSpawnParent : transform;
        var mol    = Instantiate(recipe.moleculePrefab, center, Quaternion.identity, parent);

        // Attach MoleculeInstance so AtomController can detect it later for upgrades.
        var inst        = mol.AddComponent<MoleculeInstance>();
        inst.Recipe     = recipe;
        inst.SourceAtoms = new List<AtomController>(atoms);

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

    // ── Break helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Silently destroys a specific molecule instance and unlocks its atoms
    /// for reuse in an upgrade bond. Does NOT fire events or play audio.
    /// </summary>
    private void BreakMoleculeInstance(MoleculeInstance inst)
    {
        if (inst == null) return;

        int idx = _activeMols.IndexOf(inst.gameObject);
        if (idx >= 0)
        {
            _activeMols.RemoveAt(idx);
            _atomGroups.RemoveAt(idx);
        }

        // Fully unlock atoms so FormMolecule can re-lock them cleanly.
        // We call MarkInMolecule(false) which re-enables the grab interactable
        // and clears IsInMolecule — then FormMolecule re-locks them immediately.
        // We do NOT call ResetAtom() because that would SetActive(true) and
        // trigger physics — FormMolecule will handle SetActive(false) itself.
        foreach (var a in inst.SourceAtoms)
        {
            if (a == null) continue;
            a.MarkInMolecule(false);   // clears flag + re-enables grab
            // Keep inactive (SetActive stays false) — FormMolecule re-hides them.
        }

        Destroy(inst.gameObject);
    }

    public void BreakLastMolecule()
    {
        if (_activeMols.Count == 0) return;
        int i = _activeMols.Count - 1;

        // Re-enable and restore the atoms.
        foreach (var a in _atomGroups[i]) a.ResetAtom();

        Destroy(_activeMols[i]);
        _activeMols.RemoveAt(i);
        _atomGroups.RemoveAt(i);

        OnMoleculeBroken?.Invoke();
        AudioManager.Instance?.PlayReset();
    }

    public void BreakAllMolecules()
    {
        while (_activeMols.Count > 0) BreakLastMolecule();
    }

    // ── Atom helpers ──────────────────────────────────────────────────────────

    private static void CountAtoms(List<AtomController> group,
                                   out int h, out int o, out int c, out int n)
    {
        h = o = c = n = 0;
        foreach (var a in group)
            switch (a.atomType)
            {
                case AtomType.H: h++; break;
                case AtomType.O: o++; break;
                case AtomType.C: c++; break;
                case AtomType.N: n++; break;
            }
    }

    private static List<AtomController> TrimToRecipe(List<AtomController> pool,
                                                      MoleculeRecipe recipe)
    {
        int needH = recipe.hydrogenCount;
        int needO = recipe.oxygenCount;
        int needC = recipe.carbonCount;
        int needN = recipe.nitrogenCount;

        var result = new List<AtomController>();
        foreach (var a in pool)
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

    public IReadOnlyCollection<string> DiscoveredMolecules => _discovered;
}
