using UnityEngine;
using SofaUnity;
using SofaUnityAPI;

/// <summary>
/// Cambia el color de un objeto (verde/rojo) y habilita/deshabilita el corte del SofaLaserModel.
/// Además controla si se dibuja el láser/luz del SofaLaserModel cuando está cortando.
/// </summary>
public class SofaLiverColorCutter : MonoBehaviour
{
    [Header("SOFA")]
    [SerializeField] SofaLaserModel laserModel;

    [Header("Visual")]
    [SerializeField] Renderer targetRenderer;
    [SerializeField] Color inactiveColor = Color.green;
    [SerializeField] Color activeColor = Color.red;

    [Header("Laser Rendering")]
    [SerializeField] bool controlLaserRendering = true;
    [SerializeField] bool drawLaserWhenActive = true;
    [SerializeField] bool drawLaserWhenInactive = false;
    [SerializeField] bool drawLightWhenActive = true;
    [SerializeField] bool drawLightWhenInactive = false;

    bool _isCutting;

    void Reset()
    {
        if (laserModel == null)
            laserModel = GetComponentInChildren<SofaLaserModel>();

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();
    }

    void OnEnable()
    {
        EnsureRaySetup();
        SetCutting(false);
    }

    /// <summary>
    /// Cambia estado: rojo = cortando, verde = inactivo.
    /// </summary>
    public void SetCutting(bool active)
    {
        _isCutting = active;

        EnsureRaySetup();

        if (laserModel != null)
            laserModel.ActivateTool = active;

        UpdateColor(active);
        UpdateLaserRendering(active);
    }

    public void ToggleCutting() => SetCutting(!_isCutting);

    void UpdateColor(bool active)
    {
        if (targetRenderer == null)
            return;

        // Clonar material instanciado para no modificar material compartido global.
        if (Application.isPlaying)
            targetRenderer.material.color = active ? activeColor : inactiveColor;
        else
            targetRenderer.sharedMaterial.color = active ? activeColor : inactiveColor;
    }

    void EnsureRaySetup()
    {
        if (laserModel == null)
            return;

        // Mantener el raycaster activo para seguir casteando (colisión / detección) aunque no se corte.
        laserModel.ActivateRay = true;
        // Forzar modo corte.
        laserModel.RayInteractionType = SofaDefines.SRayInteraction.CuttingTool;
    }

    void UpdateLaserRendering(bool active)
    {
        if (!controlLaserRendering || laserModel == null)
            return;

        laserModel.DrawLaser = active ? drawLaserWhenActive : drawLaserWhenInactive;
        laserModel.DrawLight = active ? drawLightWhenActive : drawLightWhenInactive;
    }
}
