using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum AtomType { H, O, C, N }

/// <summary>
/// VR atom controller.
///
/// GRAB  : Lets XRI's Kinematic movement type handle world-space positioning.
///         We only manage Rigidbody state (kinematic, velocity, damping) and
///         a world-space offset baked into the atom's own attachTransform child.
///
/// REST  : Gravity off, rotation frozen, high damping → atoms stay put.
///
/// BOND  : BFS cluster search after each release; upgrades existing molecules.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class AtomController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Atom identity")]
    public AtomType atomType;

    [Header("Grab — hold position")]
    [Tooltip("Offset (in atom's LOCAL space) from the atom centre to the point\n" +
             "that will align with the controller when held.\n" +
             "Z > 0 pushes the atom FORWARD out of the hand.\n" +
             "Z < 0 pulls it into the palm.\n" +
             "Start at 0 and tweak if the atom clips into the controller mesh.")]
    [SerializeField] private Vector3 attachLocalOffset = new Vector3(0f, 0f, 0f);

    [Header("Bond detection")]
    [Tooltip("Radius (metres) of the cluster search.")]
    [SerializeField] private float    bondSearchRadius   = 0.30f;
    [SerializeField] private float    releaseToBondDelay = 0.10f;
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
    [Tooltip("Freeze rotation so atoms don't roll.")]
    [SerializeField] private bool  freezeRotationAtRest  = true;

    // ── Private state ─────────────────────────────────────────────────────────

    private XRGrabInteractable _grab;
    private Rigidbody          _rb;
    private GameObject         _attachChild;   // child that acts as our attach point

    private readonly List<AtomController> _nearby = new();

    public bool IsGrabbed    { get; private set; }
    public bool IsInMolecule { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _rb   = GetComponent<Rigidbody>();

        // ── Create a child attach-point so XRI positions the atom correctly ───
        // XRI aligns interactable.attachTransform ↔ interactor.attachTransform.
        // By making our own child (offset by attachLocalOffset), we control
        // exactly where on the atom the hand "holds" it — no coordinate-system
        // confusion because XRI handles the maths in world space internally.
        _attachChild = new GameObject("[AtomAttach]");
        _attachChild.transform.SetParent(transform, worldPositionStays: false);
        _attachChild.transform.localPosition = attachLocalOffset;
        _attachChild.transform.localRotation = Quaternion.identity;
        _grab.attachTransform = _attachChild.transform;

        // ── XRI movement: Instantaneous ───────────────────────────────────────
        // Instantaneous sets transform.position directly in Update (render rate).
        // Kinematic uses Rigidbody.MovePosition in FixedUpdate (physics rate ~50Hz).
        // In VR at 72-120 Hz, Kinematic causes visible jitter between physics steps.
        // Instantaneous runs at render framerate → perfectly smooth.
        _grab.movementType     = XRBaseInteractable.MovementType.Instantaneous;
        _grab.trackPosition    = true;
        _grab.trackRotation    = true;
        _grab.throwOnDetach    = false;
        _grab.attachEaseInTime = 0f;
        _grab.smoothPosition   = false;
        _grab.smoothRotation   = false;

        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);

        if (atomRenderer == null)
            atomRenderer = GetComponentInChildren<Renderer>();

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
        BondManager.Instance?.CancelPendingForAtom(this);

        // XRI will make the Rigidbody kinematic via Kinematic movement type.
        // We also do it here immediately so there is no one-frame gap.
        if (_rb != null)
        {
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic     = true;
            _rb.useGravity      = false;
            _rb.interpolation   = RigidbodyInterpolation.None;
            _rb.constraints     = RigidbodyConstraints.None;
        }

        // Update the attach child's local offset in case it was tweaked at runtime.
        if (_attachChild != null)
            _attachChild.transform.localPosition = attachLocalOffset;

        SetColor(grabbedColor);
        AudioManager.Instance?.PlayGrab();
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        IsGrabbed = false;

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

    private void CheckBond()
    {
        if (IsInMolecule) return;

        int layerMask = atomLayer.value == 0 ? ~0 : atomLayer.value;

        // BFS: free atoms ─────────────────────────────────────────────────────
        var cluster  = new List<AtomController> { this };
        var frontier = new Queue<AtomController>();
        frontier.Enqueue(this);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            var hits    = Physics.OverlapSphere(current.transform.position,
                              bondSearchRadius, layerMask, QueryTriggerInteraction.Collide);
            foreach (var col in hits)
            {
                if (col.gameObject == current.gameObject) continue;
                var other = col.GetComponentInParent<AtomController>();
                if (other == null || other.IsInMolecule || other.IsGrabbed) continue;
                if (cluster.Contains(other)) continue;
                cluster.Add(other);
                frontier.Enqueue(other);
            }
        }

        // Single pass: nearby formed molecules ────────────────────────────────
        var nearbyMolecules = new List<MoleculeInstance>();
        var molHits = Physics.OverlapSphere(transform.position,
                          bondSearchRadius * 1.5f, layerMask, QueryTriggerInteraction.Collide);
        foreach (var col in molHits)
        {
            var mol = col.GetComponentInParent<MoleculeInstance>();
            if (mol != null && !nearbyMolecules.Contains(mol))
                nearbyMolecules.Add(mol);
        }

        if (cluster.Count < 2 && nearbyMolecules.Count == 0)
        {
            Debug.Log($"[AtomController] {name} — nothing nearby.");
            return;
        }

        Debug.Log($"[AtomController] {name} — cluster:{cluster.Count} mols:{nearbyMolecules.Count}");
        foreach (var a in cluster) a.SetColor(proximityColor);

        _nearby.Clear();
        foreach (var a in cluster)
            if (a != this) _nearby.Add(a);

        BondManager.Instance?.TryBond(this, _nearby, nearbyMolecules);
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
        IsInMolecule  = false;
        _grab.enabled = true;

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
        // Show the attach point offset
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(attachLocalOffset), 0.01f);
    }
}
