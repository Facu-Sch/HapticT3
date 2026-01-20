using Haply.Inverse.DeviceControllers;
using Haply.Inverse.DeviceData;
using UnityEngine;
using SofaUnity;

/// <summary>
/// Modelo de material háptico para interacción con el hígado:
/// parámetros separados normal/tangencial + límites de fuerza.
/// </summary>
[System.Serializable]
public class HapticMaterial
{
    [Header("Normal")]
    [Tooltip("Rigidez normal (N/m): qué tan duro se siente.")]
    public float stiffness = 200f;

    [Tooltip("Amortiguamiento normal (N·s/m): cuánto frena el rebote.")]
    public float normalDamping = 10f;

    [Header("Tangencial")]
    [Tooltip("Viscosidad tangencial (N·s/m): arrastre al deslizar.")]
    public float tangentialDamping = 1f;

    [Tooltip("Fricción cinética máxima (N).")]
    public float kineticFriction = 0.5f;

    [Header("Límites")]
    [Tooltip("Fuerza máxima para este material (N).")]
    public float maxForce = 5f;
}

/// <summary>
/// Controlador háptico para Tool_cut (demo hígado SOFA)
/// con múltiples puntos de contacto distribuidos a lo largo del láser.
/// Usa probes + ComputePenetration + fallback por Raycast (hover buffer).
/// </summary>
public class SofaLiverToolDriver : MonoBehaviour
{
    [Header("Devices")]
    [SerializeField] Inverse3Controller inverse3;
    [SerializeField] VerseGripController verseGrip;

    [Header("Pose mapping")]
    [SerializeField] Transform cursorTransform;
    [SerializeField] Transform toolTransform;
    [SerializeField] Transform mappingReference;
    [SerializeField] float positionScale = 1f;
    [SerializeField] bool calibrateNow = false;

    [Header("Contact")]
    [SerializeField] Collider liverCollider;
    [SerializeField] Transform laserStart;  // Punto inicial del láser (base/mango)
    [SerializeField] Transform laserEnd;    // Punto final del láser (punta)
    [SerializeField] int numberOfProbes = 4;  // Cantidad de probes a lo largo del láser
    [SerializeField] float probeRadius = 0.005f;  // Radio de cada probe (5mm default)
    [SerializeField] bool visualizeProbes = true;  // Para debug

    [Header("Tool behaviour")]
    [SerializeField] SofaLaserModel laser;
    [SerializeField] Light toolLight;
    [SerializeField] Color inactiveColor = Color.green;
    [SerializeField] Color activeColor = Color.red;

    [Header("Liver mesh sync")]
    [SerializeField] SofaVisualModel liverVisualModel;
    [SerializeField] MeshFilter liverMeshFilter;
    [SerializeField] bool refreshLiverCollider = false;
    [SerializeField] float colliderRefreshInterval = 0.02f;
    [SerializeField] bool cloneMeshForCollider = true;

    [Header("Force calculation")]
    [Tooltip("Suavizado de fuerza (0 = sin suavizado).")]
    [SerializeField] float forceSmoothing = 0.2f;

    [Header("Haptics (inactive / green)")]
    [SerializeField] HapticMaterial inactiveMaterial = new HapticMaterial
    {
        stiffness         = 250f,
        normalDamping     = 15f,
        tangentialDamping = 2f,
        kineticFriction   = 0.8f,
        maxForce          = 5f
    };

    [Header("Haptics (active / red)")]
    [SerializeField] HapticMaterial activeMaterial = new HapticMaterial
    {
        stiffness         = 80f,
        normalDamping     = 8f,
        tangentialDamping = 1.5f,
        kineticFriction   = 0.4f,
        maxForce          = 3f
    };

    [Header("Activation")]
    [SerializeField] VerseGripButton activationButton = VerseGripButton.Button1;
    [SerializeField] VerseGripButton calibrateButton = VerseGripButton.Button0;

    [Header("Stability")]
    [Tooltip("Umbral mínimo de penetración para activar fuerza (m).")]
    [SerializeField] float penetrationThreshold = 0.001f;

    [Header("Contact robustness")]
    [Tooltip("Distancia de hover para el fallback por Raycast (m).")]
    [SerializeField] float hoverDistance = 0.002f; // 2 mm

    [Tooltip("Profundidad máxima que se usa para el cálculo de fuerza (m).")]
    [SerializeField] float maxPenetrationDepth = 0.003f; // 3 mm

    // --- Probes ---
    private GameObject _probesContainer;
    private SphereCollider[] _probes;
    private Transform[] _probeTransforms;

    // --- Estado interno ---
    bool _isActive;
    Vector3 _lastToolPos;
    bool _hasLastPos;
    Vector3 _lastCursorWorld;
    Vector3 _lastCursorLocal;
    Vector3 _lastToolLocal;
    bool _hasAnchor;
    Vector3 _initialToolPos;
    bool _initialCaptured;
    float _lastColliderRefreshTime;
    Mesh _workingColliderMesh;
    Vector3 _lastLocalForce;

    const float EPS = 1e-5f;

    void Reset()
    {
        if (inverse3 == null)
            inverse3 = GetComponentInChildren<Inverse3Controller>();

        if (verseGrip == null)
            verseGrip = GetComponentInChildren<VerseGripController>();

        if (inverse3 != null && inverse3.GenericCursor != null && cursorTransform == null)
            cursorTransform = inverse3.GenericCursor.transform;

        if (toolTransform == null)
            toolTransform = transform;

        if (!_initialCaptured)
        {
            _initialToolPos = toolTransform.position;
            _initialCaptured = true;
        }
    }

    void Awake()
    {
        GenerateProbes();
    }

    void OnEnable()
    {
        if (toolLight == null)
            toolLight = GetComponentInChildren<Light>();
        if (laser == null)
            laser = GetComponentInChildren<SofaLaserModel>();

        if (verseGrip != null)
        {
            verseGrip.ButtonDown.AddListener(OnButtonDown);
            verseGrip.ButtonUp.AddListener(OnButtonUp);
        }

        SetActive(false);
    }

    void OnDisable()
    {
        if (verseGrip != null)
        {
            verseGrip.ButtonDown.RemoveListener(OnButtonDown);
            verseGrip.ButtonUp.RemoveListener(OnButtonUp);
        }

        _hasLastPos = false;
        _hasAnchor = false;

        if (inverse3 != null && inverse3.IsReady)
            inverse3.SetCursorLocalForce(Vector3.zero);
    }

    void Update()
    {
        if (inverse3 == null || !inverse3.IsReady || cursorTransform == null)
        {
            if (inverse3 != null && inverse3.IsReady)
                inverse3.SetCursorLocalForce(Vector3.zero);
            return;
        }

        if (calibrateNow)
        {
            calibrateNow = false;
            CalibrateMapping();
        }

        RefreshLiverCollider();
        UpdatePoseMapping();
        ApplyRotation();
        UpdateForces();
    }

    // ----------------- GENERACIÓN DE PROBES -------------------
    void GenerateProbes()
    {
        // Limpiar probes anteriores si existen
        if (_probesContainer != null)
            DestroyImmediate(_probesContainer);

        if (laserStart == null || laserEnd == null)
        {
            Debug.LogWarning("SofaLiverToolDriver: laserStart o laserEnd no asignados. No se pueden generar probes.");
            return;
        }

        if (numberOfProbes < 1)
        {
            Debug.LogWarning("SofaLiverToolDriver: numberOfProbes debe ser al menos 1.");
            numberOfProbes = 1;
        }

        // Crear contenedor
        _probesContainer = new GameObject("LaserProbes");
        _probesContainer.transform.SetParent(toolTransform);
        _probesContainer.transform.localPosition = Vector3.zero;
        _probesContainer.transform.localRotation = Quaternion.identity;
        _probesContainer.transform.localScale = Vector3.one;

        _probes = new SphereCollider[numberOfProbes];
        _probeTransforms = new Transform[numberOfProbes];

        // Generar probes distribuidos uniformemente
        for (int i = 0; i < numberOfProbes; i++)
        {
            float t = numberOfProbes == 1 ? 1f : (float)i / (numberOfProbes - 1);

            GameObject probeObj = new GameObject($"Probe_{i}");
            probeObj.transform.SetParent(_probesContainer.transform);

            // Posición interpolada entre start y end
            Vector3 worldPos = Vector3.Lerp(laserStart.position, laserEnd.position, t);
            Vector3 localPos = toolTransform.InverseTransformPoint(worldPos);
            probeObj.transform.localPosition = localPos;
            probeObj.transform.localRotation = Quaternion.identity;

            SphereCollider sphere = probeObj.AddComponent<SphereCollider>();
            sphere.radius = probeRadius;
            sphere.isTrigger = false;

            // Rigidbody kinematic (necesario para ComputePenetration)
            Rigidbody rb = probeObj.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            _probes[i] = sphere;
            _probeTransforms[i] = probeObj.transform;

            // Visualización opcional
            if (visualizeProbes)
            {
                GameObject sphere3D = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere3D.transform.SetParent(probeObj.transform);
                sphere3D.transform.localPosition = Vector3.zero;
                sphere3D.transform.localScale = Vector3.one * (probeRadius * 2f);

                // Remover el collider del visual (no queremos que interfiera)
                Destroy(sphere3D.GetComponent<Collider>());

                // Color según posición
                Renderer rend = sphere3D.GetComponent<Renderer>();
                if (rend != null)
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = Color.Lerp(Color.cyan, Color.yellow, t);
                    rend.material = mat;
                }
            }
        }

        Debug.Log($"SofaLiverToolDriver: Generados {numberOfProbes} probes a lo largo del láser.");
    }

    // Para regenerar probes en runtime si cambian los parámetros
    [ContextMenu("Regenerate Probes")]
    public void RegenerateProbes()
    {
        GenerateProbes();
    }

    // ----------------- Mapeo de Pose -------------------
    void UpdatePoseMapping()
    {
        if (toolTransform == null || cursorTransform == null)
            return;

        Transform refTf = mappingReference;

        if (refTf == null)
        {
            Vector3 cursorPos = cursorTransform.position;

            if (!_hasAnchor)
            {
                if (!_initialCaptured)
                {
                    _initialToolPos = toolTransform.position;
                    _initialCaptured = true;
                }
                toolTransform.position = _initialToolPos;
                _lastCursorWorld = cursorPos;
                _hasAnchor = true;
                return;
            }

            Vector3 deltaWorld = cursorPos - _lastCursorWorld;
            toolTransform.position += deltaWorld * positionScale;
            _lastCursorWorld = cursorPos;
        }
        else
        {
            Vector3 cursorLocal = refTf.InverseTransformPoint(cursorTransform.position);

            if (!_hasAnchor)
            {
                if (!_initialCaptured)
                {
                    _initialToolPos = toolTransform.position;
                    _initialCaptured = true;
                }

                _lastToolLocal = refTf.InverseTransformPoint(_initialToolPos);
                _lastCursorLocal = cursorLocal;
                toolTransform.position = _initialToolPos;
                _hasAnchor = true;
                return;
            }

            Vector3 deltaLocal = cursorLocal - _lastCursorLocal;
            Vector3 newToolLocal = _lastToolLocal + deltaLocal * positionScale;
            toolTransform.position = refTf.TransformPoint(newToolLocal);

            _lastToolLocal = newToolLocal;
            _lastCursorLocal = cursorLocal;
        }

        if (!_hasLastPos)
        {
            _lastToolPos = toolTransform.position;
            _hasLastPos = true;
        }
    }

    void ApplyRotation()
    {
        if (verseGrip != null && verseGrip.IsReady)
            toolTransform.rotation = verseGrip.Orientation;
    }

    [ContextMenu("Calibrate Mapping")]
    public void CalibrateMapping()
    {
        if (!_initialCaptured)
        {
            _initialToolPos = toolTransform.position;
            _initialCaptured = true;
        }

        toolTransform.position = _initialToolPos;

        if (mappingReference == null)
            _lastCursorWorld = cursorTransform.position;
        else
        {
            _lastCursorLocal = mappingReference.InverseTransformPoint(cursorTransform.position);
            _lastToolLocal = mappingReference.InverseTransformPoint(toolTransform.position);
        }

        _hasAnchor = true;
        _hasLastPos = false;
        _lastToolPos = toolTransform.position;
    }

    // ----------------- Fuerzas Hapticas -------------------
    void UpdateForces()
{
    if (!inverse3.IsReady || toolTransform == null || _probes == null || _probes.Length == 0 || liverCollider == null)
        return;

    // 1. Velocidad del tool (mundo)
    Vector3 toolPos = toolTransform.position;
    Vector3 velocity = Vector3.zero;
    if (_hasLastPos)
        velocity = (toolPos - _lastToolPos) / Mathf.Max(Time.deltaTime, 1e-4f);
    _lastToolPos = toolPos;
    _hasLastPos = true;

    // 2. Encontrar el probe con mayor "penetración" usando ComputePenetration + Raycast fallback
    bool hasContact = false;
    Vector3 bestNormal = Vector3.zero;
    float bestPenetration = 0f;

    for (int i = 0; i < _probes.Length; i++)
    {
        if (_probes[i] == null || _probeTransforms[i] == null)
            continue;

        // 👇 OJO: acá cambiamos el nombre de la variable del out
        if (TryGetProbeContact(_probes[i], _probeTransforms[i], velocity,
                               out Vector3 contactNormal,
                               out float contactPenetration))
        {
            if (!hasContact || contactPenetration > bestPenetration)
            {
                hasContact = true;
                bestPenetration = contactPenetration;
                bestNormal = contactNormal;
            }
        }
    }

    // Sin contacto suficiente → sin fuerza
    if (!hasContact || bestPenetration < penetrationThreshold)
    {
        inverse3.SetCursorLocalForce(Vector3.zero);
        _lastLocalForce = Vector3.zero;
        return;
    }

    // 3. Elegir material según modo (verde/rojo)
    HapticMaterial mat = _isActive ? activeMaterial : inactiveMaterial;

    Vector3 n = bestNormal;

    // 👇 Ahora esta variable se llama penetration, pero NO hay otro 'penetration' en el scope
    float penetration = Mathf.Clamp(bestPenetration, 0f, maxPenetrationDepth);

    // 4. Descomponer velocidad: normal + tangencial
    float vNormalMag = Vector3.Dot(velocity, n);
    Vector3 vNormal = vNormalMag * n;
    Vector3 vTangential = velocity - vNormal;

    // 5. Fuerza normal: resorte asimétrico + damping fuerte
    float kIn  = mat.stiffness;
    float kOut = 0.3f * mat.stiffness;  // al salir, 30% de rigidez para evitar pogo

    float kEff = (vNormalMag < 0f) ? kIn : kOut;

    Vector3 F_elastic = kEff * penetration * n;         // siempre empuja hacia afuera
    Vector3 F_dampN   = -mat.normalDamping * vNormal;   // frena el movimiento normal

    Vector3 F_normal = F_elastic + F_dampN;
    // proyectamos estrictamente en la normal
    float normalMag = Vector3.Dot(F_normal, n);
    F_normal = normalMag * n;

    // 6. Fuerza tangencial: viscosidad + fricción limitada
    Vector3 F_tangent = Vector3.zero;
    float vTanMag = vTangential.magnitude;

    if (vTanMag > 1e-5f && mat.tangentialDamping > 0f)
    {
        // Viscosidad tangencial (sensación de arrastre)
        Vector3 F_viscT = -mat.tangentialDamping * vTangential;
        float viscMag = F_viscT.magnitude;

        // Límite de fricción tipo Coulomb simple
        float maxFric = mat.kineticFriction;
        if (viscMag > maxFric && viscMag > 1e-5f)
            F_viscT *= maxFric / viscMag;

        F_tangent = F_viscT;
    }

    // 7. Fuerza total de contacto, con límite propio del material
    Vector3 force = F_normal + F_tangent;

    if (force.magnitude > mat.maxForce)
        force = force.normalized * mat.maxForce;

    // 8. Suavizado temporal
    if (forceSmoothing > 0f)
    {
        float smoothLerp = 1f - Mathf.Exp(-10f * forceSmoothing * Time.deltaTime);
        force = Vector3.Lerp(_lastLocalForce, force, smoothLerp);
    }

    _lastLocalForce = force;
    inverse3.SetCursorLocalForce(force);
}


    /// <summary>
    /// Calcula contacto para un probe:
    /// 1) intenta ComputePenetration;
    /// 2) si no hay penetración, usa Raycast con hoverDistance
    ///    para simular una pequeña penetración virtual.
    /// </summary>
    bool TryGetProbeContact(
        SphereCollider probe,
        Transform probeTransform,
        Vector3 toolVelocity,
        out Vector3 normal,
        out float penetration)
    {
        normal = Vector3.zero;
        penetration = 0f;

        if (liverCollider == null || probe == null || probeTransform == null)
            return false;

        Vector3 probePos = probeTransform.position;

        // 1) Intentar penetración real
        if (Physics.ComputePenetration(
            probe,
            probePos, probeTransform.rotation,
            liverCollider,
            liverCollider.transform.position, liverCollider.transform.rotation,
            out Vector3 dir,
            out float dist))
        {
            if (dist > EPS)
            {
                normal = dir.normalized;
                penetration = dist;
                return true;
            }
        }

        // 2) Fallback: Raycast desde el probe con hover buffer
        // Dirección estimada hacia el hígado
        Vector3 dirGuess = -toolVelocity;
        if (dirGuess.sqrMagnitude < 1e-6f)
            dirGuess = (liverCollider.bounds.center - probePos);

        if (dirGuess.sqrMagnitude < 1e-6f)
            dirGuess = liverCollider.transform.up;

        dirGuess.Normalize();

        Ray ray = new Ray(probePos, dirGuess);
        if (liverCollider.Raycast(ray, out RaycastHit hit, hoverDistance))
        {
            normal = hit.normal;
            float d = hit.distance;
            penetration = Mathf.Max(hoverDistance - d, 0f);
            return penetration > 0f;
        }

        return false;
    }

    // ------------ Sincronización SOFA ---------------
    void RefreshLiverCollider()
    {
        if (!refreshLiverCollider)
            return;

        if (liverCollider == null)
            return;

        if (liverCollider is not MeshCollider mc)
            return;

        // Si el MeshCollider se destruyó al recargar SOFA, salimos silenciosamente.
        if (mc.Equals(null))
            return;

        if (colliderRefreshInterval > 0f &&
            Time.time - _lastColliderRefreshTime < colliderRefreshInterval)
            return;

        Mesh srcMesh = null;

        if (liverVisualModel != null)
            srcMesh = liverVisualModel.GetMesh();
        else if (liverMeshFilter != null)
            srcMesh = liverMeshFilter.sharedMesh;
        else if (mc.GetComponent<MeshFilter>() != null)
            srcMesh = mc.GetComponent<MeshFilter>().sharedMesh;

        if (srcMesh == null)
            return;

        _lastColliderRefreshTime = Time.time;

        if (cloneMeshForCollider)
        {
            if (_workingColliderMesh == null)
                _workingColliderMesh = new Mesh();

            _workingColliderMesh.Clear();
            _workingColliderMesh.vertices = srcMesh.vertices;
            _workingColliderMesh.triangles = srcMesh.triangles;
            _workingColliderMesh.normals = srcMesh.normals;
            _workingColliderMesh.bounds = srcMesh.bounds;

            mc.sharedMesh = _workingColliderMesh;
        }
        else
        {
            mc.sharedMesh = srcMesh;
        }

        if (!mc.enabled)
            mc.enabled = true;
    }

    // ---------------- Activación ------------------
    void OnButtonDown(VerseGripController ctrl, VerseGripEventArgs args)
    {
        if (args.Button == activationButton)
            SetActive(true);
        else if (args.Button == calibrateButton)
            CalibrateMapping();
    }

    void OnButtonUp(VerseGripController ctrl, VerseGripEventArgs args)
    {
        if (args.Button == activationButton)
            SetActive(false);
    }

    void SetActive(bool active)
    {
        _isActive = active;

        if (laser != null)
            laser.ActivateTool = active;

        if (toolLight != null)
        {
            toolLight.enabled = true;
            toolLight.color = active ? activeColor : inactiveColor;
        }

        if (!active && inverse3 != null && inverse3.IsReady)
            inverse3.SetCursorLocalForce(Vector3.zero);
    }

    // Debug visual
    void OnDrawGizmos()
    {
        if (!visualizeProbes || _probes == null)
            return;

        Gizmos.color = Color.cyan;
        foreach (var probe in _probes)
        {
            if (probe != null)
            {
                Gizmos.DrawWireSphere(probe.transform.position, probeRadius);
            }
        }
    }
}
