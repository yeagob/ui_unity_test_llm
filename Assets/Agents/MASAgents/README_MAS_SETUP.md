# 🤖 Guía de Configuración del Sistema Multi-Agente (MAS)

## Descripción General

El sistema MAS (Multi-Agent System) utiliza 3 agentes especializados para ejecutar tests de UI de forma más inteligente y estructurada:

1. **Planner Agent** 📋 - Analiza el objetivo y crea un plan estructurado
2. **Executor Agent** ⚡ - Ejecuta las acciones del plan una por una
3. **Verifier Agent** ✅ - Verifica si cada paso se ejecutó correctamente

## Estructura del Sistema

```
MASOrchestrator
    │
    ├── Fase 1: PLANIFICACIÓN
    │   └── Planner Agent → Crea plan JSON con pasos
    │
    └── Fase 2: EJECUCIÓN (loop por cada paso)
        ├── Executor Agent → Ejecuta la acción
        └── Verifier Agent → Valida el resultado
            ├── PASS → Siguiente paso
            ├── RETRY → Reintentar paso (máx 3 veces)
            └── ABORT → Terminar test

    Si TEST FALLA → 🔄 RETRY CON ANÁLISIS (hasta 3 intentos)
        ├── Planner recibe contexto completo del fallo
        ├── Analiza qué salió mal
        └── Crea NUEVO plan con diferente estrategia
```

### 🔄 Sistema de Retry con Análisis

Si un test falla, el sistema automáticamente:

1. **Registra el intento fallido** con detalles de cada paso
2. **Pasa el contexto completo al Planner**:
   - Pasos que funcionaron ✅
   - Pasos que fallaron ❌ con diagnóstico
   - Estado actual de la UI
3. **El Planner analiza el fallo** y explica qué salió mal
4. **Genera un NUEVO plan** con estrategia diferente
5. **Repite hasta 3 intentos máximo**

Esto permite que el agente aprenda de sus errores y pruebe diferentes enfoques.



## Cómo Crear los Agentes

### Paso 1: Crear los PromptConfig

Los prompts ya están creados en:
- `Assets/Agents/Prompts/MAS/PlannerAgentPrompt.asset`
- `Assets/Agents/Prompts/MAS/ExecutorAgentPrompt.asset`
- `Assets/Agents/Prompts/MAS/VerifierAgentPrompt.asset`

### Paso 2: Crear las Configuraciones de Agente

En Unity:

1. Click derecho en `Assets/Agents/MASAgents/`
2. Selecciona **Create → LLM → Agent Configuration**
3. Crea 3 configuraciones:

#### A) PlannerAgentConfig

| Campo | Valor |
|-------|-------|
| Agent Id | `mas_planner` |
| Agent Name | `MAS Planner` |
| Description | `Agent that creates structured test plans` |
| Provider Config | Selecciona tu OpenAI provider |
| Model Config | Selecciona gpt-4 o similar |
| System Prompt | `PlannerAgentPrompt` (el asset creado) |
| Available Tools | Solo agregar: `QueryUIToolConfig`, `GetEditorStateToolConfig` |
| Max Tool Calls | `5` |
| Max Response Tokens | `2048` |

#### B) ExecutorAgentConfig

| Campo | Valor |
|-------|-------|
| Agent Id | `mas_executor` |
| Agent Name | `MAS Executor` |
| Description | `Agent that executes single UI actions` |
| Provider Config | Selecciona tu OpenAI provider |
| Model Config | Selecciona gpt-4 o similar |
| System Prompt | `ExecutorAgentPrompt` (el asset creado) |
| Available Tools | Agregar: `ClickToolConfig`, `TypeTextToolConfig`, `WaitSecondsToolConfig`, `WaitForElementToolConfig` |
| Max Tool Calls | `1` |
| Max Response Tokens | `512` |

#### C) VerifierAgentConfig

| Campo | Valor |
|-------|-------|
| Agent Id | `mas_verifier` |
| Agent Name | `MAS Verifier` |
| Description | `Agent that verifies action results` |
| Provider Config | Selecciona tu OpenAI provider |
| Model Config | Selecciona gpt-4 o similar |
| System Prompt | `VerifierAgentPrompt` (el asset creado) |
| Available Tools | Agregar: `QueryUIToolConfig`, `ScreenshotToolConfig` |
| Max Tool Calls | `3` |
| Max Response Tokens | `1024` |

### Paso 3: Abrir la Ventana MAS

1. En Unity, ve a **Window → LLM → MAS Testing**
2. En la sección "Agent Configurations":
   - Arrastra `PlannerAgentConfig` al campo "Planner Agent"
   - Arrastra `ExecutorAgentConfig` al campo "Executor Agent"
   - Arrastra `VerifierAgentConfig` al campo "Verifier Agent"

### Paso 4: Ejecutar un Test

1. En el campo "Test Objective", escribe el objetivo:
   ```
   Prueba que el botón Play inicia el juego correctamente
   ```

2. Click en **▶️ Run Test**

3. Observa:
   - El **Plan** se mostrará con los pasos identificados
   - El **Log** mostrará el progreso de cada fase
   - La **Barra de Progreso** indicará el avance

4. Si necesitas detener, usa **⏹️ Stop**

## Ejemplo de Flujo

```
Usuario: "Prueba que el botón Start funciona"

📋 PLANNER:
   1. Usa query_ui → Ve botones disponibles
   2. Genera plan:
      - Step 1: click(StartButton) → "Inicia el juego"
      - Step 2: wait_for_element(GameScene) → "Escena cargada"
      - Step 3: screenshot(game_started) → "Captura evidencia"

⚡ EXECUTOR (paso 1):
   - Ejecuta: click("StartButton")
   
✅ VERIFIER (paso 1):
   - Usa query_ui → Ve nuevo estado
   - Responde: {"success": true, "diagnosis": "Botón clickeado, juego iniciando"}

⚡ EXECUTOR (paso 2):
   - Ejecuta: wait_for_element("GameScene", 5)

✅ VERIFIER (paso 2):
   - Usa query_ui → Ve GameScene presente
   - Responde: {"success": true}

... continúa hasta completar el plan
```

## Ubicación de Archivos

```
Assets/
├── Scripts/
│   └── Sentinel/
│       ├── Core/
│       │   ├── MASOrchestrator.cs      # Orquestador principal
│       │   └── SentinelAgentLoop.cs    # Loop agéntico simple
│       └── Models/
│           └── TestPlan.cs             # Modelo de datos del plan
│
├── Agents/
│   ├── Prompts/
│   │   └── MAS/
│   │       ├── PlannerAgentPrompt.asset
│   │       ├── ExecutorAgentPrompt.asset
│   │       └── VerifierAgentPrompt.asset
│   │
│   └── MASAgents/                      # CREAR ESTA CARPETA
│       ├── PlannerAgentConfig.asset    # Crear desde Unity
│       ├── ExecutorAgentConfig.asset   # Crear desde Unity
│       └── VerifierAgentConfig.asset   # Crear desde Unity
```

## Troubleshooting

### "Please configure all 3 agents before running"
- Asegúrate de arrastrar las 3 configuraciones de agente a la ventana MAS

### "Planning failed"
- Verifica que el Provider Config tenga un token válido
- Asegúrate que el Planner tenga acceso a `query_ui`

### "Execution failed"
- Verifica que los elementos UI existan en la escena
- El juego debe estar en Play Mode para interactuar con UI de runtime

### "Verification timeout"
- Aumenta el timeout de `wait_for_element`
- Verifica que los nombres de elementos sean correctos

## Consejos

1. **Objetivos claros**: Escribe objetivos específicos y verificables
2. **Elementos UI con nombre**: Asegúrate que los elementos UI tengan nombres descriptivos
3. **Play Mode**: Para tests de runtime, asegúrate de estar en Play Mode
4. **Logs**: Revisa la consola de Unity para más detalles

---

## 🆕 Nuevas Funcionalidades

### 📋 Copiar Reporte al Portapapeles

Click en el botón **📋 Copy Report** para copiar un reporte completo en formato Markdown que incluye:
- Objetivo del test
- Configuración de agentes
- Plan con estado de cada paso
- Resultado final con métricas
- Log completo de ejecución

Ideal para compartir resultados o documentar issues.

### 💾 Persistencia del Objetivo

El objetivo del test se guarda automáticamente y persiste entre sesiones. No tienes que volver a escribirlo cada vez que abres la ventana.

### 📂 Configuraciones de Test Reutilizables

Puedes crear **TestConfiguration** assets para guardar y reutilizar configuraciones de test:

1. Click derecho en el Project
2. **Create → Sentinel → Test Configuration**
3. Configura:
   - `testName`: Nombre descriptivo
   - `objective`: Objetivo del test
   - `successCriteria`: Criterio de éxito
   - Asigna los 3 agentes
4. En la ventana MAS, arrastra el asset al campo **📂 Load Test Config**

Esto cargará automáticamente todos los valores.

### Ejemplo de Reporte Generado

```markdown
# 🤖 MAS Test Report
**Generated**: 2026-01-08 13:45:00

## 🎯 Test Objective
Prueba que el botón Play inicia el juego correctamente

## ⚙️ Agent Configuration
- **Planner**: MAS Planner Agent
- **Executor**: MAS Executor Agent
- **Verifier**: MAS Verifier Agent

## 📋 Test Plan
| Step | Action | Target | Expected | Status |
|------|--------|--------|----------|--------|
| 1 | click | PlayButton | Inicia juego | ✅ Passed |
| 2 | wait_for_element | GameScene | Escena visible | ✅ Passed |

## 📊 Result
✅ **PASSED**
- Steps Passed: 2/2
- Steps Failed: 0
- Duration: 00:15

## 📜 Execution Log
[13:45:00] 🚀 Starting MAS Test: Prueba que el botón Play...
[13:45:01] 📋 Phase 1: PLANNING
[13:45:05] ✅ Plan created with 2 steps
...
```

---

Para más información, consulta la documentación del sistema en `README.md`.

