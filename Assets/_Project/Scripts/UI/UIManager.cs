using System.Collections.Generic;
using TMPro;
using UnityEngine;
using DG.Tweening;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Library panel (world-space Canvas)")]
    [SerializeField] private GameObject libraryPanel;
    [SerializeField] private Transform  libraryEntryParent;
    [SerializeField] private GameObject libraryEntryPrefab;

    [Header("Inspector panel")]
    [SerializeField] private GameObject      inspectorPanel;
    [SerializeField] private TextMeshProUGUI inspectorName;
    [SerializeField] private TextMeshProUGUI inspectorFormula;
    [SerializeField] private TextMeshProUGUI inspectorBond;

    [Header("Notification banner")]
    [SerializeField] private GameObject      notificationBanner;
    [SerializeField] private TextMeshProUGUI notificationText;

    [Header("Panel positioning")]
    [SerializeField] private Transform playerHead;
    [SerializeField] private float     panelDistance = 1.5f;

    private readonly HashSet<string> _shownEntries = new();

    // ─── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (BondManager.Instance == null)
        {
            Debug.LogError("[UIManager] BondManager not found!");
            return;
        }

        BondManager.Instance.OnMoleculeFormed += ShowMoleculeFormed;

        if (libraryPanel)       libraryPanel.SetActive(true);
        if (inspectorPanel)     inspectorPanel.SetActive(false);
        if (notificationBanner) notificationBanner.SetActive(false);
    }

    private void OnDestroy()
    {
        if (BondManager.Instance != null)
            BondManager.Instance.OnMoleculeFormed -= ShowMoleculeFormed;
    }

    // ─── Bond event handler ────────────────────────────────────────

    private void ShowMoleculeFormed(MoleculeRecipe recipe)
    {
        AddToLibrary(recipe);
        ShowInspector(recipe);
        ShowNotification($"Formed {recipe.moleculeName}!");
    }

    // ─── Library panel ─────────────────────────────────────────────

    private void AddToLibrary(MoleculeRecipe recipe)
    {
        if (!_shownEntries.Add(recipe.formula)) return;

        if (libraryEntryPrefab == null)
        {
            Debug.LogError("[UIManager] libraryEntryPrefab is not assigned!");
            return;
        }

        var entry = Instantiate(libraryEntryPrefab, libraryEntryParent);

        // Try root TMP first
        var rootTMP = entry.GetComponent<TextMeshProUGUI>();
        if (rootTMP != null)
        {
            rootTMP.text = $"{recipe.moleculeName}   {recipe.formula}";
        }
        else
        {
            // Try named children
            var allTMP = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (allTMP.Length == 0)
            {
                Debug.LogError("[UIManager] LibraryEntry prefab has no TextMeshProUGUI!");
                return;
            }

            foreach (var t in allTMP)
            {
                switch (t.name)
                {
                    case "MoleculeName": t.text = recipe.moleculeName; break;
                    case "FormulaLabel": t.text = recipe.formula;      break;
                    case "BondLabel":    t.text = recipe.bondType;     break;
                    default:
                        if (t == allTMP[0])
                            t.text = $"{recipe.moleculeName}   {recipe.formula}";
                        break;
                }
            }
        }

        entry.transform.localScale = Vector3.zero;
        entry.transform.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack);
    }

    // ─── Inspector panel ───────────────────────────────────────────

    public void ShowInspector(MoleculeRecipe recipe)
    {
        if (inspectorName    != null) inspectorName.text    = recipe.moleculeName;
        if (inspectorFormula != null) inspectorFormula.text  = recipe.formula;
        if (inspectorBond    != null) inspectorBond.text     = $"Bond: {recipe.bondType}";

        if (inspectorPanel == null)
        {
            Debug.LogWarning("[UIManager] Inspector panel not assigned!");
            return;
        }

        inspectorPanel.SetActive(true);
        inspectorPanel.transform.DOScale(Vector3.one, 0.2f)
                                .From(Vector3.zero)
                                .SetEase(Ease.OutBack);

        CancelInvoke(nameof(HideInspector));
        Invoke(nameof(HideInspector), 4f);
    }

    private void HideInspector() =>
        inspectorPanel.transform.DOScale(Vector3.zero, 0.15f)
                                .OnComplete(() => inspectorPanel.SetActive(false));

    // ─── Notification banner ───────────────────────────────────────

    private void ShowNotification(string msg)
    {
        if (notificationBanner == null)
        {
            Debug.LogWarning("[UIManager] Notification banner not assigned!");
            return;
        }

        notificationText.text = msg;
        notificationBanner.SetActive(true);
        notificationBanner.transform.DOScale(Vector3.one, 0.2f).From(Vector3.zero);

        CancelInvoke(nameof(HideNotification));
        Invoke(nameof(HideNotification), 2.5f);
    }

    private void HideNotification() =>
        notificationBanner.transform.DOScale(Vector3.zero, 0.15f)
                                    .OnComplete(() => notificationBanner.SetActive(false));

    // ─── Panel placement ───────────────────────────────────────────

    public void PlacePanelInFrontOfPlayer(GameObject panel)
    {
        if (playerHead == null) return;
        var forward = playerHead.forward;
        forward.y = 0;
        forward.Normalize();
        panel.transform.position = playerHead.position + forward * panelDistance;
        panel.transform.LookAt(playerHead.position);
        panel.transform.Rotate(0, 180, 0);
    }
}
