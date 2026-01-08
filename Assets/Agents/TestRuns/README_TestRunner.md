# 🏃 Test Runner - Guía de Uso

El **Test Runner** permite ejecutar múltiples tests secuencialmente y generar reportes detallados.

## Conceptos Clave

### TestConfiguration
Un test individual con:
- Objetivo
- Criterio de éxito
- Agentes configurados (Planner, Executor, Verifier)

### TestRunConfiguration
Una colección de tests que se ejecutan juntos:
- Lista ordenada de TestConfigurations
- Configuración de comportamiento
- Generación automática de reportes

---

## Cómo Crear Tests

### 1. Crear TestConfiguration

1. Click derecho en Project → **Create → Sentinel → Test Configuration**
2. Nombra el asset (ej: `Test_GraphicsHighest`)
3. Configura:
   - `testName`: Nombre descriptivo
   - `objective`: Qué debe hacer el test
   - `successCriteria`: Cómo verificar éxito
   - Asigna los 3 agentes MAS

### Ejemplos de Tests (templates disponibles en `Assets/Agents/TestConfigs/`):

| Test | Objetivo |
|------|----------|
| `Test_GraphicsHighest` | Poner todos los gráficos al máximo |
| `Test_GraphicsLowest` | Poner todos los gráficos al mínimo |
| `Test_AudioMute` | Silenciar todo el audio |
| `Test_StartNewGame` | Iniciar nueva partida |

---

## Cómo Crear Test Runs

### 1. Crear TestRunConfiguration

1. Click derecho en Project → **Create → Sentinel → Test Run**
2. Nombra el asset (ej: `Run_GraphicsSettings`)
3. Configura:
   - `runName`: Nombre de la suite
   - `description`: Descripción de qué prueba
   - `category`: Categoría para organizar
   - `tests`: Arrastra aquí los TestConfiguration assets
   - `continueOnFailure`: Si continuar cuando un test falla
   - `delayBetweenTests`: Segundos entre tests

### Ejemplos de Runs (templates disponibles en `Assets/Agents/TestRuns/`):

| Run | Descripción |
|-----|------------|
| `Run_GraphicsSettings` | Tests de configuración gráfica |
| `Run_FullSettingsSuite` | Suite completa de configuración |

---

## Usar el Test Runner

1. Abre **Window → LLM → Test Runner**
2. Arrastra una `TestRunConfiguration` al campo "📁 Test Run"
3. Revisa la lista de tests que se ejecutarán
4. Click en **▶️ Run All Tests**

### Durante la Ejecución

- **Progreso**: Barra de progreso muestra avance
- **Log**: Muestra resultado de cada test en tiempo real
- **Stop**: Botón para detener la ejecución

### Después de la Ejecución

- Se genera un reporte `.md` en `Assets/TestReports/`
- Click en **📄 Last Report** para abrirlo

---

## Formato del Reporte Generado

El reporte incluye:

```markdown
# 🏃 Test Run Report: Graphics Settings Suite

**Generated**: 2026-01-08 14:00:00
**Duration**: 45.2 seconds

## 📊 Summary

**Result**: ✅ **ALL PASSED**

| Metric | Value |
|--------|-------|
| Tests Passed | 3 |
| Tests Failed | 0 |
| Tests Skipped | 0 |
| Total Duration | 45.2s |

## 📋 Test Results

| # | Test Name | Objective | Steps | Duration | Status |
|---|-----------|-----------|-------|----------|--------|
| 1 | Graphics Highest | Poner graficos al max... | 5/5 | 15.2s | ✅ Pass |
| 2 | Graphics Lowest | Poner graficos al min... | 5/5 | 14.8s | ✅ Pass |
| 3 | Audio Mute | Silenciar audio... | 3/3 | 15.2s | ✅ Pass |

## 📜 Detailed Results

### ✅ Test 1: Graphics Highest

- **Objective**: Poner todos los gráficos al máximo
- **Status**: PASSED
- **Steps**: 5 passed, 0 failed
- **Duration**: 15.2s

<details>
<summary>Execution Log</summary>

[14:00:05] 🚀 Starting MAS Test...
[14:00:06] 📋 Phase 1: PLANNING
...

</details>
```

---

## Flujo Completo de Trabajo

```
1. Crear Tests Individuales
   └── TestConfiguration assets

2. Crear Test Run
   └── TestRunConfiguration con lista de tests

3. Ejecutar desde Test Runner
   └── Window → LLM → Test Runner

4. Revisar Reporte
   └── Assets/TestReports/*.md
```

---

## Mejores Prácticas

1. **Nombres descriptivos**: Usa nombres claros para tests y runs
2. **Objetivos específicos**: Escribe objetivos precisos y verificables
3. **Orden lógico**: Ordena tests de simple a complejo
4. **continueOnFailure**: Activa para ver todos los fallos en una run
5. **Screenshots**: Activa `screenshotPerTest` para evidencia visual

---

## Integración con CI/CD

Los reportes generados son archivos Markdown estándar que pueden:
- Subirse como artefactos de build
- Parsearse para métricas
- Incluirse en pull requests

Ubicación por defecto: `Assets/TestReports/`

---

*Parte del sistema Sentinel MAS Testing*
