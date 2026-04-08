using System.Collections.Generic;
using UnityEngine;

// Attach to each atom tray. Respawns atoms when
// the tray area has fewer than minCount atoms.
public class AtomSpawner : MonoBehaviour
{
    [SerializeField] private GameObject atomPrefab;
    [SerializeField] private int maxAtoms = 3;
    [SerializeField] private float checkInterval = 2f;
    [SerializeField] private Transform[] spawnPoints;

    private void Start() =>
        InvokeRepeating(nameof(CheckAndSpawn), 1f, checkInterval);

    private void CheckAndSpawn()
    {
        if (atomPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
            return;

        int active = 0;
        var emptyPoints = new List<Transform>();

        foreach (var sp in spawnPoints)
        {
            if (sp == null)
                continue;

            // Check if an atom already exists near this spawn point
            var cols = Physics.OverlapSphere(sp.position, 0.05f);
            bool occupied = false;
            foreach (var c in cols)
                if (c.GetComponent<AtomController>() != null)
                { occupied = true; break; }

            if (occupied)
                active++;
            else
                emptyPoints.Add(sp);
        }

        if (active >= maxAtoms)
            return;

        var toSpawn = Mathf.Min(maxAtoms - active, emptyPoints.Count);
        for (var i = 0; i < toSpawn; i++)
            Instantiate(atomPrefab, emptyPoints[i].position, Quaternion.identity);
    }
}
