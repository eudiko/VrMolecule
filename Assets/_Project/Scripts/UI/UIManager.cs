using System.Collections.Generic;
using TMPro;
using UnityEngine;
using DG.Tweening;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Library panel (world-space Canvas)")]
    [SerializeField] private GameObject libraryPanel;
    [SerializeField] private Transform libraryEntryParent;   // Vertical Layout Group
    [SerializeField] private GameObject libraryEntryPrefab;   // TMP label prefab

    [Header("Inspector panel")]
    [SerializeField] private GameObject inspectorPanel;
    [SerializeField] private TextMeshProUGUI inspectorName;
    [SerializeField] private TextMeshProUGUI inspectorFormula;
    [SerializeField] private TextMeshProUGUI inspectorBond;

    [Header("Notification banner")]
    [SerializeField] private GameObject notificationBanner;
    [SerializeField] private TextMeshProUGUI notificationText;

    [Header("Panel positioning")]
    [SerializeField] private Transform playerHead;       // Main Camera
    [SerializeField] private float panelDistance = 1.5f;

    private readonly HashSet<string> _shownEntries = new();

    // ─── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        BondManager.Instance.OnMoleculeFormed += ShowMoleculeFormed;
        libraryPanel.SetActive(true);
        inspectorPanel.SetActive(false);
        notificationBanner.SetActive(false);
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
        if (!_shownEntries.Add(recipe.formula)) return; // already listed

        var entry = Instantiate(libraryEntryPrefab, libraryEntryParent);
        var label = entry.GetComponent<TextMeshProUGUI>();
        label.text = $"{recipe.moleculeName}  {recipe.formula}";

        // Animate in
        entry.transform.localScale = Vector3.zero;
        entry.transform.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack);
    }

    // ─── Inspector panel ───────────────────────────────────────────

    public void ShowInspector(MoleculeRecipe recipe)
    {
        inspectorName.text = recipe.moleculeName;
        inspectorFormula.text = recipe.formula;
        inspectorBond.text = $"Bond: {recipe.bondType}";

        inspectorPanel.SetActive(true);
        inspectorPanel.transform.DOScale(Vector3.one, 0.2f)
                                .From(Vector3.zero)
                                .SetEase(Ease.OutBack);

        // Auto-hide after 4 seconds
        CancelInvoke(nameof(HideInspector));
        Invoke(nameof(HideInspector), 4f);
    }

    private void HideInspector() =>
        inspectorPanel.transform.DOScale(Vector3.zero, 0.15f)
                                .OnComplete(() => inspectorPanel.SetActive(false));

    // ─── Notification banner ───────────────────────────────────────

    private void ShowNotification(string msg)
    {
        notificationText.text = msg;
        notificationBanner.SetActive(true);
        notificationBanner.transform.DOScale(Vector3.one, 0.2f).From(Vector3.zero);

        CancelInvoke(nameof(HideNotification));
        Invoke(nameof(HideNotification), 2.5f);
    }

    private void HideNotification() =>
        notificationBanner.transform.DOScale(Vector3.zero, 0.15f)
                                    .OnComplete(() => notificationBanner.SetActive(false));

    // ─── Panel placement (call once on Start) ──────────────────────

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
