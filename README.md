# VR AI Interrogation Simulator (TFG)

Un simulador inmersivo en Realidad Virtual (VR) desarrollado en Unity, donde el jugador asume el rol de un detective encargado de interrogar a un sospechoso. El comportamiento, las respuestas y las emociones del sospechoso están generados en tiempo real mediante Inteligencia Artificial (LLMs locales o en la nube), ofreciendo una experiencia dinámica y única en cada partida.

Este proyecto ha sido desarrollado como Trabajo de Fin de Grado (TFG) para explorar las capacidades de los Modelos de Lenguaje Grande (LLMs) integrados en mecánicas de videojuegos conversacionales.

## Características Principales

*   **Generación Procesal de Casos (`CaseGenerator.cs`)**: Cada partida es distinta. El sistema utiliza un LLM para inventar un delito, una coartada, un secreto oscuro y definir si el sospechoso es realmente Culpable o Inocente.
*   **Interacción por Voz Real (`InterrogationController.cs`)**: El jugador se comunica con el sospechoso usando el micrófono de sus gafas VR. Utiliza **Whisper (Speech-To-Text)** para transcribir el audio a texto.
*   **Detección de Gritos / Presión**: El sistema analiza el volumen (RMS) de la voz del jugador. Si el jugador le "grita" al sospechoso, la IA recibe una instrucción especial que altera su estado emocional, forzándolo a ponerse nervioso o cometer errores.
*   **Respuesta Dinámica de la IA (`DialogueSystem.cs`)**: El sospechoso está controlado por un LLM al que se le inyecta una "personalidad" y el contexto del caso. El modelo reacciona de forma natural a las preguntas del jugador, defendiendo su inocencia o cediendo a la presión.
*   **Sistema de Emociones y Animaciones (`CharacterAnimator.cs`, `NPCBehavior.cs`)**: La IA responde incluyendo etiquetas de emoción (ej. `[ANIMACION: NERVIOSO]`, `[ANIMACION: NEGACION]`). El sistema de Unity parsea estas etiquetas en tiempo real y activa las animaciones correspondientes del NPC.
*   **Text-to-Speech (TTS) Modular (`AudioManager.cs`)**: El texto generado por el LLM es convertido a voz para que el NPC hable. Soporta:
    *   **Edge-TTS** (gratuito, ejecutado localmente).
    *   **OpenAI TTS** (vía API).
    *   **ElevenLabs TTS** (vía API, para máxima expresividad).
*   **Mecánica de Pistas ("Folio VR") (`InterrogationManager.cs`)**: Si el jugador logra acorralar lógicamente al sospechoso, este puede cometer un error verbal etiquetado como `[PISTA: COARTADA]` o `[PISTA: SECRETO]`. Estas pistas se añaden dinámicamente al dossier físico que el jugador sostiene en VR.
*   **Métricas de Latencia (`LatencyMetrics.cs`)**: Sistema exhaustivo para medir el rendimiento de los tiempos de respuesta del STT, LLM y TTS, esencial para la investigación del TFG.

## Arquitectura del Sistema

El flujo de comunicación del sistema sigue este patrón de "Pipeline Conversacional":

1.  **Input del Jugador**: El jugador mantiene presionado un botón en su controlador VR y habla.
2.  **Transcripción (STT)**: Al soltar el botón, el audio se normaliza y se envía a Whisper para transcribirlo a texto.
3.  **Procesamiento LLM**: El texto (junto con una bandera de si el jugador gritó) se envía al LLM. El sistema soporta **Streaming (SSE)** para reducir la latencia, procesando la respuesta frase por frase.
4.  **Parseo y Ejecución**:
    *   Se extraen las emociones (`[ANIMACION: ...]`) para cambiar el estado del Animator.
    *   Se extraen las pistas (`[PISTA: ...]`) para actualizar la UI del jugador.
    *   El texto limpio se envía a la cola del TTS.
5.  **Respuesta (TTS)**: El audio generado se reproduce sincronizado con la animación de los labios del NPC.

## Estructura del Proyecto

El código fuente principal se encuentra en `Assets/Scripts/`:

*   **`Managers/`**: Controladores de alto nivel (`InterrogationManager.cs`, `MainMenuManager.cs`). Gestionan el ciclo de vida de la partida, inicio, reinicios y la UI final del Veredicto.
*   **`IA/`**: Núcleo de la inteligencia artificial. Incluye `DialogueSystem.cs` (conexión con OpenAI/LM Studio), `CaseGenerator.cs` (creación de crímenes procedurales) y configuración.
*   **`Input/`**: `InterrogationController.cs` maneja el micrófono VR y la integración con Whisper.
*   **`Audio/`**: `AudioManager.cs` controla el pipeline de Text-to-Speech (Edge, OpenAI, ElevenLabs).
*   **`NPC/` y `Animation/`**: Scripts como `NPCBehavior.cs` y `CharacterAnimator.cs` que traducen los estados lógicos y tags de la IA en movimientos y expresiones del modelo 3D.
*   **`Core/`**: Eventos globales (`EventSystem.cs`), estado de la partida (`GameContext.cs`) y sistema de métricas.

## Requisitos y Configuración

1.  **Motor**: Unity (Optimizado para VR mediante XR Interaction Toolkit).
2.  **Servidor LLM Local**: El sistema está preparado para funcionar contra una API compatible con OpenAI (ej. **LM Studio**, **Ollama**, o **vLLM**) ejecutándose en local o en red local.
    *   *Nota*: Se recomienda configurar en LM Studio un modelo de parámetros bajos/medios (ej. Llama-3 8B, Phi-3, Qwen) optimizado para roleplay y baja latencia.
3.  **Configuración de IA (`IAConfig`)**: Se debe configurar un objeto *ScriptableObject* en Unity con:
    *   URLs de la API local (para generar casos y para el diálogo).
    *   Claves de API si se usan servicios de pago para TTS (OpenAI / ElevenLabs).

## Cómo Jugar

1.  Al iniciar, el sistema generará un caso aleatorio (ej. "Un atraco en el museo").
2.  Lee el Folio en tu mano izquierda (VR) para entender los hechos y el nombre del sospechoso.
3.  Presiona el botón asignado en el mando VR para empezar a grabar tu voz. Hazle preguntas al sospechoso.
4.  Escucha sus respuestas, fíjate en su lenguaje corporal. Si detectas que miente, presiónale (incluso subiendo la voz).
5.  Busca incongruencias en su historia hasta que cometa un error y suelte una Pista oficial.
6.  Antes de que se acabe el tiempo, emite tu veredicto en el panel final: ¿Culpable o Inocente?

## Notas de Desarrollo (TFG)

Este proyecto demuestra que los modelos de lenguaje pequeños pueden mantener personajes consistentes en tiempo real si se utilizan técnicas de **Prompt Engineering** específicas, **One-Shot Prompting** y un manejo eficiente de la **VRAM** (descarga/carga de modelos bajo demanda). El manejo asíncrono y en hilos de fondo del STT y TTS garantiza que la experiencia VR no sufra caídas de frames (stutters) manteniendo la inmersión del usuario.

---
Desarrollado por Jesus Santacruz - Proyecto de Trabajo de Fin de Grado (TFG).
