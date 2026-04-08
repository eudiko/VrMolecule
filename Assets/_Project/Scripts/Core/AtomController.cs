using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum AtomType { H, O, C, N }

/// <summary>
/// Robust VR atom controller.
///
/// GRAB  — Atom is re-parented to the interactor transform so it tracks
///          the hand with zero lag and is always visible.
///          Rigidbody is fully kinematic + interpolation disabled while held
///          so there is no physics-lag on any axis.
///
/// REST  — Gravity off, rotation frozen, high damping = atoms won't roll.
///
/// BOND  — OverlapSphere with a generous radius fires after every release.
///          Uses a per-instance list (not static) so concurrent checks don't
///          trample each other. Prints verbose debug logs to the console.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class AtomController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Atom identity")]
    public AtomType atomType;

    [Header("Grab — hand offset")]
    [Tooltip("Local position relative to the controller attach point. " +
             "Keep (0,0,0) so the atom sits exactly where the controller is.")]
    [SerializeField] private Vector3 handPositionOffset = new Vector3(0f, 0f, 0.03f);
    [SerializeField] private Vector3 handRotationOffset = Vector3.zero;

    [Header("Bond detection")]
    [Tooltip("How far (metres) the OverlapSphere searches for neighbours. " +
             "Increase this if bonding is unreliable in VR.")]
    [SerializeField] private float    bondSearchRadius  = 0.30f;   // 30 cm — generous for VR
    [SerializeField] private float    releaseToBondDelay = 0.10f;  // seconds
    [SerializeField] private LayerMask atomLayer         = ~0;

    [Header("Visual feedback")]
    [SerializeField] private Renderer atomRenderer;
    [SerializeField] private Color    defaultColor   = Color.white;
    [SerializeField] private Color    grabbedColor   = Color.yellow;
    [SerializeField] private Color    proximityColor = Color.cyan;

    [Header("Physics — resting")]
    [Tooltip("Keep false so atoms sit still on the tray.")]
    [SerializeField] private bool  useGravityWhenResting = false;
    [SerializeField] private float restingLinearDamping  = 20f;
    [SerializeField] private float restingAngularDamping = 20f;
    [Tooltip("Prevents atoms from rolling when not held.")]
    [SerializeField] private bool  freezeRotationAtRest  = true;

    // ── Private state ─────────────────────────────────────────────────────────

    private XRGrabInteractable _grab;
    private Rigidbody          _rb;
    private Transform          _originalParent;

    // Per-instance list — NOT static, so concurrent checks never share state.
    private readonly List<AtomController> _nearby = new();

    public bool IsGrabbed    { get; private set; }
    public bool IsInMolecule { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _rb   = GetComponent<Rigidbody>();

        // The toolkit doesn't touch position/rotation — we handle it via parenting.
        _grab.movementType     = XRBaseInteractable.MovementType.Instantaneous;
        _grab.trackPosition    = false;
        _grab.trackRotation    = false;
        _grab.throwOnDetach    = false;
        _grab.attachEaseInTime = 0f;
        _grab.smoothPosition   = false;
        _grab.smoothRotation   = false;

        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);

        if (atomRenderer == null)
            atomRenderer = GetComponentInChildren<Renderer>();

        _originalParent = transform.parent;
        ApplyRestingPhysics();
    }

    private void OnDestroy()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnGrabbed);
        _grab.selectExited.RemoveListener(OnReleased);
    }

    // ── Grab events ───────────────────────────────────────────────────────────

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        IsGrabbed = true;
        CancelInvoke(nameof(CheckBond));
        BondManager.Instance?.CancelPendingForAtom(this);   // cancel any pending bond

        if (_rb != null)
        {
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic     = true;
            _rb.useGravity      = false;
            _rb.constraints     = RigidbodyConstraints.None;
            // Disable interpolation while kinematic — prevents one-frame lag.
            _rb.interpolation   = RigidbodyInterpolation.None;
        }

        // Parent atom to the interactor transform so it tracks with zero lag.
        Transform attachPt = GetInteractorTransform(args);
        if (attachPt != null)
        {
            transform.SetParent(attachPt, worldPositionStays: false);
            transform.localPosition = handPositionOffset;   // (0,0,0) = at the hand
            transform.localRotation = Quaternion.Euler(handRotationOffset);
        }
        else
        {
            Debug.LogWarning("[AtomController] Could not find interactor transform; atom may misplace.");
        }

        SetColor(grabbedColor);
        AudioManager.Instance?.PlayGrab();
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        IsGrabbed = false;

        // Detach from controller — world position is preserved.
        transform.SetParent(_originalParent, worldPositionStays: true);

        if (_rb != null)
        {
            _rb.isKinematic     = false;
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        ApplyRestingPhysics();
        SetColor(defaultColor);

        Invoke(nameof(CheckBond), releaseToBondDelay);
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    private void ApplyRestingPhysics()
    {
        if (_rb == null) return;

        _rb.useGravity     = useGravityWhenResting;
        _rb.linearDamping  = restingLinearDamping;
        _rb.angularDamping = restingAngularDamping;
        _rb.isKinematic    = false;
        _rb.interpolation  = RigidbodyInterpolation.Interpolate;

        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        _rb.constraints = freezeRotationAtRest
            ? RigidbodyConstraints.FreezeRotation
            : RigidbodyConstraints.None;

        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    // ── Bond detection ────────────────────────────────────────────────────────

    /// <summary>
    /// BFS cluster search for free atoms, then a single-pass search for nearby
    /// formed molecules.  Both are forwarded to BondManager so it can decide
    /// whether to form a new molecule or upgrade an existing one.
    /// </summary>
    private void CheckBond()
    {
        if (IsInMolecule) return;

        int layerMask = atomLayer.value == 0 ? ~0 : atomLayer.value;

        // ── 1. BFS: collect all free, connected atoms ─────────────────────────
        var cluster = new List<AtomController> { this };
        var frontier = new Queue<AtomController>();
        frontier.Enqueue(this);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            var hits = Physics.OverlapSphere(
                current.transform.position, bondSearchRadius, layerMask,
                QueryTriggerInteraction.Collide);

            foreach (var col in hits)
            {
                if (col.gameObject == current.gameObject) continue;

                var other = col.GetComponentInParent<AtomController>();
                if (other == null)           continue;
                if (other.IsInMolecule)      continue;
                if (other.IsGrabbed)         continue;
                if (cluster.Contains(other)) continue;

                cluster.Add(other);
                frontier.Enqueue(other);
            }
        }

        // ── 2. Single-pass: find nearby MoleculeInstance objects ──────────────
        // Search from THIS atom with a slightly larger radius so the player just
        // needs to hold the atom near the molecule, not at its exact centre.
        var nearbyMolecules = new List<MoleculeInstance>();
        var molHits = Physics.OverlapSphere(
            transform.position, bondSearchRadius * 1.5f, layerMask,
            QueryTriggerInteraction.Collide);

        foreach (var col in molHits)
        {
            var mol = col.GetComponentInParent<MoleculeInstance>();
            if (mol != null && !nearbyMolecules.Contains(mol))
                nearbyMolecules.Add(mol);
        }

        bool hasNeighbours   = cluster.Count >= 2;
        bool hasMolNeighbour = nearbyMolecules.Count > 0;

        if (!hasNeighbours && !hasMolNeighbour)
        {
            Debug.Log($"[AtomController] {name} — nothing nearby, no bond check.");
            return;
        }

        Debug.Log($"[AtomController] {name} — free cluster: {cluster.Count} atom(s), " +
                  $"nearby molecules: {nearbyMolecules.Count}");

        foreach (var a in cluster) a.SetColor(proximityColor);

        _nearby.Clear();
        foreach (var a in cluster)
            if (a != this) _nearby.Add(a);

        BondManager.Instance?.TryBond(this, _nearby, nearbyMolecules);
    }


    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Transform GetInteractorTransform(SelectEnterEventArgs args)
    {
        if (args?.interactorObject == null) return null;

        if (args.interactorObject is XRBaseInteractor bi)
            return bi.attachTransform != null ? bi.attachTransform : bi.transform;

        if (args.interactorObject is MonoBehaviour mb)
            return mb.transform;

        return null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void MarkInMolecule(bool v)
    {
        IsInMolecule  = v;
        _grab.enabled = !v;

        if (v && _rb != null)
        {
            _rb.isKinematic     = true;
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    public void SetColor(Color c)
    {
        if (atomRenderer != null)
            atomRenderer.material.color = c;
    }

    public void ResetAtom()
    {
        IsInMolecule = false;
        _grab.enabled = true;
        transform.SetParent(_originalParent, worldPositionStays: true);

        if (_rb != null)
            _rb.isKinematic = false;

        ApplyRestingPhysics();
        SetColor(defaultColor);
        gameObject.SetActive(true);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, bondSearchRadius);
    }
}
