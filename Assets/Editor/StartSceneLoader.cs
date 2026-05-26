using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public class StartSceneLoader
{
    private const string StartScenePath = "Assets/Scenes/MainMenu.unity";

    static StartSceneLoader()
    {
        // Se ejecuta automáticamente al compilar o abrir el proyecto
        SetPlayModeStartScene();
    }

    [MenuItem("Herramientas TFG/Forzar MainMenu al dar Play")]
    public static void SetPlayModeStartScene()
    {
        SceneAsset startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScenePath);
        if (startScene != null)
        {
            EditorSceneManager.playModeStartScene = startScene;
            Debug.Log($"[StartSceneLoader] Escena de inicio configurada a: {StartScenePath}. Siempre se cargará esta escena al dar a Play.");
        }
        else
        {
            Debug.LogWarning($"[StartSceneLoader] No se pudo encontrar la escena en {StartScenePath}");
        }
    }

    [MenuItem("Herramientas TFG/Quitar forzado de MainMenu (Jugar escena actual)")]
    public static void ClearPlayModeStartScene()
    {
        EditorSceneManager.playModeStartScene = null;
        Debug.Log("[StartSceneLoader] Forzado de escena quitado. Ahora al dar a Play se cargará la escena que tengas abierta.");
    }
}
