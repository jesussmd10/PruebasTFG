using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Diagnostics;
using System.IO;

public class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    
    [SerializeField] private string nombreVoz = "es-ES-AlvaroNeural";

    private string outputMp3Path;

    private void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        outputMp3Path = Path.Combine(Application.persistentDataPath, "acusado_azure.mp3");
    }

    public void ReproducirTexto(string texto)
    {
        StartCoroutine(GenerarYReproducirEdge(texto));
    }

    private IEnumerator GenerarYReproducirEdge(string texto)
    {
        if (string.IsNullOrEmpty(texto)) yield break;

        // Limpiamos las comillas del texto para que no rompan la consola de comandos
        string textoLimpio = texto.Replace("\"", "'");

        // Preparamos el comando para edge-tts
        ProcessStartInfo startInfo = new ProcessStartInfo();
        
        startInfo.FileName = @"C:\Users\Jesus Santacruz\anaconda3\Scripts\edge-tts.exe"; 
        
        // Le pasamos los argumentos directamente
        startInfo.Arguments = $"--voice {nombreVoz} --text \"{textoLimpio}\" --write-media \"{outputMp3Path}\"";
        
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        Process edgeProcess = new Process();
        edgeProcess.StartInfo = startInfo;
        edgeProcess.Start();

        // Esperamos a que se descargue el audio de Microsoft
        while (!edgeProcess.HasExited)
        {
            yield return null; 
        }

        // Cargamos el MP3 en Unity
        if (File.Exists(outputMp3Path))
        {
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + outputMp3Path, AudioType.MPEG))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    
                    if (audioSource.clip != null) Destroy(audioSource.clip);
                    
                    audioSource.clip = clip;
                    audioSource.Play();
                    UnityEngine.Debug.Log("Azure hablando mediante edge-tts");
                }
                else
                {
                    UnityEngine.Debug.LogError("Error cargando el MP3: " + www.error);
                }
            }
        }
    }
}