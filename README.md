# Sentinel: AI-Powered UI Testing Framework for Unity

**Sentinel** is an intelligent, conversational testing agent built for Unity. It allows developers to perform UI testing using natural language inside the Unity Editor, supporting both **UI Toolkit** and **Canvas (uGUI)** systems.

---

## 🎯 Key Features

- **🗣️ Conversational Testing**: Talk to Sentinel in plain language. Ask it to "Click the play button", "Log in with user 'admin'", or "Verify that the main menu is visible".
- **🔄 Universal UI System Support**:
    - **UI Toolkit**: Automatic discovery of VisualElements.
    - **Canvas (uGUI)**: Support for legacy UI and TextMeshPro elements.
- **🤖 Autonomous Agent Loop**: (Roadmap) Sequential execution of complex test plans with automatic verification.
- **📄 Professional Reporting**: Generates Markdown reports for every test run, including:
    - Step-by-step logs with timestamps.
    - Automatic screenshots for key moments and verifications.
    - Success/Failure status and summaries.
- **🎨 Premium Editor Interface**: A modern, dark-themed chat window integrated directly into the Unity Editor.
    - Debug mode to see the agent's "Thinking" process and tool calls.
    - Character counter and intuitive controls (Enter to send, Ctrl+Enter for new line).

---

## 🔧 Core Components

### 1. UI Inspector Service
The brain of the framework. It scans the current scene (Play Mode or Editor Mode) and returns a JSON hierarchy of:
- Buttons, InputFields (legacy & TMP), Toggles, Sliders.
- Static Text elements (for context and verification).
- Hierarchy paths for stable referencing.

### 2. UI Interactor Service
The hands of the framework. It simulates user input:
- **ClickAsync**: Simulates pointer events for UI Toolkit and `onClick`/`IPointerClickHandler` for Canvas.
- **TypeAsync**: Handles text input for `TextField`, `InputField`, and `TMP_InputField`.
- **ScrollAsync**: Controls `ScrollView` and `ScrollRect` components.
- **WaitForElementAsync**: Polling-based wait for dynamic UI transitions.

### 3. Test Report Service
The record-keeper. It manages the report lifecycle:
- Captures screenshots in both **Edit Mode** (Editor views) and **Play Mode** (Game view).
- Generates structured Markdown files in `Assets/TestReports`.
- Refreshes the Asset Database automatically for immediate feedback.

---

## 🚀 Getting Started

### 1. Requirements
- Unity 2022.3+
- OpenAI or QWEN API Key (configured in `AgentConfig`)
- TextMeshPro (included in most projects)

### 2. Setup
1. Create a `SentinelAgentConfig.asset` via the Create menu.
2. Assign the `SentinelSystemPrompt.asset`.
3. Add the required Tool configurations (`Click`, `QueryUI`, `Screenshot`, etc.).
4. Open the chat window via `Window -> LLM -> Agent Chat`.
5. Select your Config and start testing!

---

## 🔧 Available Tools (MCP Compliant)

| Tool | Description |
| :--- | :--- |
| `query_ui` | Scans the UI for visible and interactable elements. |
| `get_editor_state` | Returns Play Mode status, current scene, and compiler state. |
| `click` | Simulates a click on a UI element by path or name. |
| `type_text` | Enters text into an input field. |
| `screenshot` | Captures the current view and saves it to the report. |
| `wait_for_element` | Waits until a specific element appears or becomes visible. |
| `start_test` / `finish_test` | Marks the boundaries of a test case for reporting. |

---

---

## 🤖 Multi-Agent System (MAS) - ¡Implementado!

El sistema MAS divide el testing inteligente en 3 agentes especializados:

### Arquitectura

```
            ┌─────────────────────────────────────────┐
            │         MAS ORCHESTRATOR                │
            └─────────────────┬───────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         │                    │                    │
         ▼                    ▼                    ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│   PLANNER   │      │  EXECUTOR   │      │  VERIFIER   │
│   📋        │      │  ⚡         │      │  ✅         │
└─────────────┘      └─────────────┘      └─────────────┘
 Crea el plan         Ejecuta acciones    Valida resultados
```

### Cómo Usar MAS

1. **Abrir ventana MAS**: `Window → LLM → MAS Testing`
2. **Configurar agentes** (ya precreados en `Assets/Agents/MASAgents/`):
   - `PlannerAgentConfig`
   - `ExecutorAgentConfig`
   - `VerifierAgentConfig`
3. **Escribir objetivo**: Ej. "Prueba que el botón Play inicia el juego"
4. **Click en Run Test**

El sistema automáticamente:
- Analiza la UI actual
- Genera un plan estructurado
- Ejecuta cada paso
- Verifica el resultado
- Genera reporte final

### Ejemplo de Flujo

```
Objetivo: "Verificar que el botón Start funciona"

📋 PLANNER crea plan:
   1. click(StartButton) → "Inicia el juego"
   2. wait_for_element(GameScene) → "Escena cargada"
   3. screenshot(game_started) → "Evidencia"

⚡ EXECUTOR paso 1: click("StartButton")
✅ VERIFIER: {"success": true, "diagnosis": "Juego iniciando"}

⚡ EXECUTOR paso 2: wait_for_element("GameScene", 5)
✅ VERIFIER: {"success": true, "diagnosis": "GameScene visible"}

📊 RESULTADO: Test PASSED (3/3 pasos)
```

### Archivos del Sistema

```
Assets/Scripts/Sentinel/
├── Core/
│   ├── MASOrchestrator.cs     # Orquestador principal
│   └── SentinelAgentLoop.cs   # Loop simple (alternativo)
├── Models/
│   └── TestPlan.cs            # Modelo de datos
├── Services/
│   ├── UIInspectorService.cs  # Inspecciona UI
│   ├── UIInteractorService.cs # Ejecuta acciones
│   └── TestReportService.cs   # Genera reportes
└── Tools/
    └── SentinelToolSet.cs     # Herramientas MCP

Assets/Agents/MASAgents/
├── PlannerAgentConfig.asset   # Config Planner
├── ExecutorAgentConfig.asset  # Config Executor
├── VerifierAgentConfig.asset  # Config Verifier
└── README_MAS_SETUP.md        # Guía detallada
```

---

## 🎛️ Dos Modos de Testing

### 1. Modo Simple (Agent Chat)
- `Window → LLM → Agent Chat`
- Conversación libre con un solo agente
- Toggle "Auto" para loop automático
- Ideal para exploración y tests rápidos

### 2. Modo MAS (Multi-Agent)
- `Window → LLM → MAS Testing`
- 3 agentes especializados
- Plan estructurado con verificación
- Ideal para tests complejos y reproducibles

---

*Developed with ❤️ as part of the Unity LLM Agentic Coding project.*

