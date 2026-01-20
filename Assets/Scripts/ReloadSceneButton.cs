using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Enlaza este método al OnClick de un botón de Canvas para recargar la escena actual.
/// </summary>
public class ReloadSceneButton : MonoBehaviour
{
    public void ReloadActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.name);
    }
}
