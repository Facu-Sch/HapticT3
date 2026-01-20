using Haply.Inverse.DeviceControllers;
using Haply.Inverse.DeviceData;
using UnityEngine;

/// <summary>
/// Haptic driver for Inverse3 + VerseGrip using Unity physics as contact proxy.
/// - Pose: aplica deltas del cursor (Transform) al tool, con escala, sin realinear offsets.
/// - Rotación: copia 1:1 del VerseGrip.
/// - Fuerzas: calcula contacto contra el hígado y envía CursorSetForce cada frame.
/// </summary>
public class HapticToolDriver : MonoBehaviour
{
    [Header("Devices")]
    [Tooltip("Inverse3 controller en escena.")]
    [SerializeField] Inverse3Controller inverseController;
    [Tooltip("VerseGrip controller en escena.")]
    [SerializeField] VerseGripController verseGripController;

    [Header("Pose sources")]
    [Tooltip("Transform del cursor Inverse3 (hijo de Haptic Controller).")]
    [SerializeField] Transform cursorTransform;
    [Tooltip("Transform objetivo a mover (p.ej. Tool_cut). Si está vacío usa este mismo.")]
    [SerializeField] Transform target;
    [Tooltip("Escala aplicada a los deltas de movimiento del cursor (1 = 1:1).")]
    [SerializeField] float positionScale = 1f;
    [Tooltip("Referencia para calcular/applicar deltas (p.ej. centro del hígado). Si está vacío usa mundo.")]
    [SerializeField] Transform liverReference;

    [Header("Tool behaviour")]
    [SerializeField] SofaUnity.SofaLaserModel laser;
    [SerializeField] Light toolLight;

    [Header("Activation")]
    [SerializeField] VerseGripButton activationButton = VerseGripButton.Button0;
    [SerializeField] Color inactiveColor = Color.green;
    [SerializeField] Color activeColor = Color.red;

    [Header("Haptics (stiff)")]
    [Tooltip("Rigidez cuando la herramienta está desactivada (N/m aprox).")]
    [SerializeField] float stiffness = 300f;
    [Tooltip("Amortiguamiento cuando está desactivada (N·s/m aprox).")]
    [SerializeField] float damping = 2f;

    [Header("Haptics (cut / soft)")]
    [Tooltip("Rigidez reducida cuando la herramienta está activa/cortando.")]
    [SerializeField] float stiffnessCut = 80f;
    [Tooltip("Amortiguamiento reducido cuando está activa/cortando.")]
    [SerializeField] float dampingCut = 1f;

    [Header("Force limits")]
    [Tooltip("Fuerza máxima enviada al dispositivo (N).")]
    [SerializeField] float maxForce = 10f;
    [Tooltip("Radio de la sonda para pruebas de contacto (m).")]
    [SerializeField] float probeRadius = 0.01f;

    [Header("Colliders")]
    [Tooltip("Collider del hígado (MeshCollider preferido).")]
    [SerializeField] Collider liverCollider;
    [Tooltip("Collider en la punta de la herramienta (SphereCollider recomendado).")]
    [SerializeField] Collider toolCollider;
    [Tooltip("Transform de la punta del láser para las pruebas de contacto (usa su posición).")]
    [SerializeField] Transform tipTransform;
    [Header("Calibration")]
    [Tooltip("Marca esta casilla para recalibrar (se apaga sola).")]
    [SerializeField] bool calibrateNow = false;

    bool _isActive;
    Vector3 _lastToolPos;
    Vector3 _lastCursorPos;
    bool _hasLastPos;
    bool _hasLastCursor;
    Vector3 _lastCursorLocal;
    Vector3 _lastTargetLocal;
    Vector3 _lastCursorWorldAtCalib;
    Vector3 _lastTargetWorldAtCalib;
    Vector3 _initialTargetPos;
    bool _initialCaptured;

    void Reset()
    {
        target ??= transform;
        if (laser == null) laser = GetComponentInChildren<SofaUnity.SofaLaserModel>();
        if (toolLight == null) toolLight = GetComponentInChildren<Light>();
        if (cursorTransform == null && inverseController != null && inverseController.GenericCursor != null)
        {
            cursorTransform = inverseController.GenericCursor.transform;
        }
        if (target != null)
        {
            _initialTargetPos = target.position;
            _initialCaptured = true;
        }
    }

    void OnEnable()
    {
        if (verseGripController != null)
        {
            verseGripController.ButtonDown.AddListener(OnButtonDown);
            verseGripController.ButtonUp.AddListener(OnButtonUp);
        }
    }

    void OnDisable()
    {
        if (verseGripController != null)
        {
            verseGripController.ButtonDown.RemoveListener(OnButtonDown);
            verseGripController.ButtonUp.RemoveListener(OnButtonUp);
        }
        SetActive(false);
        _hasLastPos = false;
        _hasLastCursor = false;
        _lastCursorLocal = Vector3.zero;
        _lastTargetLocal = Vector3.zero;
        _lastCursorWorldAtCalib = Vector3.zero;
        _lastTargetWorldAtCalib = Vector3.zero;
        // Enviar fuerza cero para liberar control de forma segura
        if (inverseController != null && inverseController.IsReady)
        {
            inverseController.SetCursorLocalForce(Vector3.zero);
        }
    }

    void Update()
    {
        if (calibrateNow)
        {
            calibrateNow = false;
            Calibrate();
        }
        UpdatePose();
        UpdateForces();
    }

    void UpdatePose()
    {
        if (target == null || cursorTransform == null) return;

        var refTf = liverReference;
        if (refTf == null)
        {
            var cursorPos = cursorTransform.position;
            if (_hasLastCursor)
            {
                var deltaWorld = cursorPos - _lastCursorPos;
                target.position += deltaWorld * positionScale;
            }
            _lastCursorPos = cursorPos;
            _hasLastCursor = true;
        }
        else
        {
            var cursorLocal = refTf.InverseTransformPoint(cursorTransform.position);
            if (_hasLastCursor)
            {
                var deltaLocal = cursorLocal - _lastCursorLocal;
                var newTargetLocal = _lastTargetLocal + deltaLocal * positionScale;
                target.position = refTf.TransformPoint(newTargetLocal);
                _lastTargetLocal = newTargetLocal;
            }
            else
            {
                _lastTargetLocal = refTf.InverseTransformPoint(target.position);
            }
            _lastCursorLocal = cursorLocal;
            _hasLastCursor = true;
            _lastCursorWorldAtCalib = cursorTransform.position;
            _lastTargetWorldAtCalib = target.position;
        }

        if (verseGripController != null && verseGripController.IsReady)
        {
            target.rotation = verseGripController.Orientation;
        }
    }

    /// <summary>
    /// Calibra: fija la posición actual del cursor/tool como referencia para los siguientes deltas.
    /// No mueve nada, solo reinicia los acumuladores.
    /// </summary>
    [ContextMenu("Calibrate")]
    public void Calibrate()
    {
        if (cursorTransform == null || target == null) return;
        if (!_initialCaptured)
        {
            _initialTargetPos = target.position;
            _initialCaptured = true;
        }
        // Volver el tool a la posición inicial almacenada
        target.position = _initialTargetPos;

        var refTf = liverReference;
        if (refTf == null)
        {
            _lastCursorPos = cursorTransform.position;
            _lastCursorLocal = cursorTransform.position;
            _lastTargetLocal = target.position;
        }
        else
        {
            _lastCursorLocal = refTf.InverseTransformPoint(cursorTransform.position);
            _lastTargetLocal = refTf.InverseTransformPoint(target.position);
        }
        _lastCursorWorldAtCalib = cursorTransform.position;
        _lastTargetWorldAtCalib = target.position;
        _hasLastCursor = true;
        _hasLastPos = false;
        _lastToolPos = target.position;
    }

    void UpdateForces()
    {
        if (inverseController == null || !inverseController.IsReady || target == null)
        {
            return;
        }

        // Calcular velocidad del tool (para damping)
        Vector3 velocity = Vector3.zero;
        if (_hasLastPos)
        {
            velocity = (target.position - _lastToolPos) / Mathf.Max(Time.deltaTime, 1e-4f);
        }
        _lastToolPos = target.position;
        _hasLastPos = true;

        Vector3 force = Vector3.zero;
        bool hasContact = false;
        Vector3 contactNormal = Vector3.zero;
        float penetration = 0f;

        Vector3 tipPos = tipTransform != null ? tipTransform.position : target.position;

        if (liverCollider != null)
        {
            // Si hay un collider dedicado en la herramienta, usar ComputePenetration con su propio transform
            if (toolCollider != null)
            {
                if (Physics.ComputePenetration(
                        toolCollider, toolCollider.transform.position, toolCollider.transform.rotation,
                        liverCollider, liverCollider.transform.position, liverCollider.transform.rotation,
                        out Vector3 dir, out float dist))
                {
                    hasContact = true;
                    contactNormal = dir.normalized;
                    penetration = dist;
                    // Corregir penetración visual: empuja el tool fuera
                    target.position += contactNormal * penetration;
                }
            }
            // Fallback: overlap sphere en la punta
            if (!hasContact)
            {
                Collider[] overlaps = Physics.OverlapSphere(tipPos, probeRadius);
                foreach (var col in overlaps)
                {
                    if (col == liverCollider)
                    {
                        hasContact = true;
                        Vector3 closest = liverCollider.ClosestPoint(tipPos);
                        contactNormal = (tipPos - closest).normalized;
                        penetration = Mathf.Max(0f, probeRadius - Vector3.Distance(tipPos, closest));
                        break;
                    }
                }
            }
            // Raycast corto para normal/penetración aproximada
            if (!hasContact)
            {
                if (Physics.Raycast(tipPos - target.forward * probeRadius, target.forward, out RaycastHit hitInfo, probeRadius * 2f))
                {
                    if (hitInfo.collider == liverCollider)
                    {
                        hasContact = true;
                        contactNormal = hitInfo.normal;
                        penetration = Mathf.Max(0f, probeRadius - hitInfo.distance);
                    }
                }
            }
        }

        if (hasContact)
        {
            bool active = _isActive || (laser != null && laser.ActivateTool);
            float k = active ? stiffnessCut : stiffness;
            float d = active ? dampingCut : damping;

            // Proyectar velocidad sobre la normal para damping
            float velAlongNormal = Vector3.Dot(velocity, contactNormal);
            Vector3 dampingForce = -d * velAlongNormal * contactNormal;
            force = k * penetration * contactNormal + dampingForce;

            // Clamp
            if (force.magnitude > maxForce)
            {
                force = force.normalized * maxForce;
            }
        }

        // Enviar fuerza (convertida a espacio local del dispositivo)
        Vector3 localForce = force;
        if (force != Vector3.zero)
        {
            localForce = inverseController.InverseTransformVector(force);
        }
        inverseController.SetCursorLocalForce(localForce);
    }

    void OnButtonDown(VerseGripController ctrl, VerseGripEventArgs args)
    {
        if (args.Button == activationButton) SetActive(true);
    }

    void OnButtonUp(VerseGripController ctrl, VerseGripEventArgs args)
    {
        if (args.Button == activationButton) SetActive(false);
    }

    void SetActive(bool active)
    {
        _isActive = active;
        if (laser != null) laser.ActivateTool = active;
        if (toolLight != null)
        {
            toolLight.enabled = true;
            toolLight.color = active ? activeColor : inactiveColor;
        }
    }
}
