using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChatSystem.Configuration.ScriptableObjects;
using ChatSystem.Models.LLM;
using UnityEngine;
using ChatSystem.Services.Orchestrators.Interfaces;
using ChatSystem.Services.Context.Interfaces;
using ChatSystem.Services.Agents.Interfaces;
using ChatSystem.Services.Orchestrators;
using ChatSystem.Services.Context;
using ChatSystem.Services.Agents;
using ChatSystem.Services.Logging;
using ChatSystem.Services.Tools;
using ChatSystem.Services.Persistence;
using ChatSystem.Services.Persistence.Interfaces;
using ChatSystem.Services.Tools.Interfaces;
using MapSystem;
using MapSystem.Elements;
using MapSystem.Enums;
using MapSystem.Models.Map;
using InventorySystem.Services.Tools;
using InventorySystem.Components;
using TMPro;

namespace ChatSystem.Characters
{
    
    public class CharacterAgent : TurnCharacter
    {
        [Header("Misión en la vida")]
        [SerializeField]
        private string _initialMessage;
        
        [SerializeField]
        private int _actionPoints;
        
        [Header("Agente")]
        [SerializeField]
        private AgentConfig[] agentConfigurations;

        [Header("Game Refs")]
        [SerializeField]
        private CharacterElement _characterElement;
        
        [SerializeField]
        private MapSystem.MapSystem _mapSystem;
        
        [field:SerializeField]
        public Sprite AvatarImage { get; set; }

        [Header("Dialog System")]
        [SerializeField] 
        private TextMeshProUGUI _dialogText ;
        
        [SerializeField] 
        private GameObject _dialogObject;

        
        private IChatOrchestrator chatOrchestrator;
        private ILLMOrchestrator llmOrchestrator;
        private IContextManager contextManager;
        private IAgentExecutor agentExecutor;
        private IPersistenceService persistenceService;
        private IToolSet characterToolSet;
        private IToolSet inventoryToolSet;
        private InventoryComponent inventoryComponent;
        
        private Dictionary<ConextType, PromptConfig> _contextPrompt = new Dictionary<ConextType, PromptConfig>(); 

        public override void Initialize()
        {
            HideDialog();
            inventoryComponent = GetComponent<InventoryComponent>();
            CreateCoreServices();
            CreateToolSets();
            CreateServices();
            ConfigureServices();
            LoggingService.Initialize(LogLevel.Debug);
        }

        public override async Task ExecuteTurn()
        {
            int missingPoints = _actionPoints;
            
            HideDialog();
            
            AgentConfig firstAgent = agentConfigurations[0];
            
            MapCell[] map = _mapSystem.GetAllCellsWithElements();

            do
            {
                UniversalLogUI.Instance.Log($"\\nAction Points: {missingPoints}");
                
                //Vision prompt
                PromptConfig visionPromptConfig = CreateVisionPromptMap(map);
                firstAgent.contextPrompts.Add(visionPromptConfig);
                
                //Inventory prompt
                if (inventoryComponent != null)
                {
                    PromptConfig inventoryPromptConfig = CreateInventoryPromptConfig();
                    firstAgent.contextPrompts.Add(inventoryPromptConfig);
                }
                
                LLMResponse response = await chatOrchestrator.ProcessUserMessageAsync(_characterElement.Id.ToString(), _initialMessage);

                missingPoints --;
                
                firstAgent.contextPrompts.Remove(visionPromptConfig);
                if (inventoryComponent != null)
                {
                    firstAgent.contextPrompts.RemoveAll(p => p.promptId == "inventory-status");
                }
                HideDialog();
                
            }while (missingPoints > 0);
            

            int count = firstAgent.contextPrompts.Count;
            for (int i = count-1; i > 0; i--)
            {
                firstAgent.contextPrompts.RemoveAt(i);    
            }
        }
        
        private void CreateCoreServices()
        {
            contextManager = new ContextManager();
            agentExecutor = new AgentExecutor();
            persistenceService = new PersistenceService();
        }

        private void CreateToolSets()
        {
            characterToolSet = new CharacterToolSet(this);
            agentExecutor.RegisterToolSet(characterToolSet);
            
            if (_mapSystem != null)
            {
                inventoryToolSet = new InventoryToolSet(this, _mapSystem);
                agentExecutor.RegisterToolSet(inventoryToolSet);
            }
        }

        private void CreateServices()
        {
            llmOrchestrator = new LLMOrchestrator(agentExecutor);
            chatOrchestrator = new ChatOrchestrator();

            RegisterAgentConfigurations();
        }

        private void RegisterAgentConfigurations()
        {
            if (agentConfigurations != null && agentConfigurations.Length > 0)
            {
                foreach (AgentConfig config in agentConfigurations)
                {
                    if (config != null)
                    {
                        llmOrchestrator.RegisterAgentConfig(config);
                    }
                }
            }
        }

        private void ConfigureServices()
        {
            if (chatOrchestrator is ChatOrchestrator chatOrchestratorImpl)
            {
                chatOrchestratorImpl.SetLLMOrchestrator(llmOrchestrator);
                chatOrchestratorImpl.SetContextManager(contextManager);
                chatOrchestratorImpl.SetPersistenceService(persistenceService);
            }
        }

        [System.Serializable]
        public struct MapDataJson
        {
            public MapCellJson[] cells;
            public MapMetadata metadata;
            public string currentInventory;
        }

        [System.Serializable]
        public struct MapCellJson
        {
            public int r;
            public int c;
            public MapElementJson[] e;
        }

        [System.Serializable]
        public struct MapElementJson
        {
            public int id;
            public string type;
            public string name;
        }

        [System.Serializable]
        public struct MapMetadata
        {
            public int totalCells;
            public int traversableCells;
            public int elementsCount;
            public DateTime generatedAt;
        }

        private PromptConfig CreateVisionPromptMap(MapCell[] mapElementsCells)
        {
            List<MapCellJson> cellsJson = new List<MapCellJson>();
            int traversableCount = 0;
            int totalElements = 0;

            foreach (MapCell cell in mapElementsCells)
            {
                List<MapElementJson> elementsJson = new List<MapElementJson>();

                foreach (MapElement element in cell.elements)
                {
                    elementsJson.Add(new MapElementJson
                    {
                        id = element.Id,
                        type = element.ElementType.ToString(),
                        name = element.name,
                    });
                }

                if (_characterElement.FacingDirection == ViewDirection.Left && cell.gridCell.column > _characterElement.GetGridPosition().y ||
                    _characterElement.FacingDirection == ViewDirection.Right && cell.gridCell.column < _characterElement.GetGridPosition().y )
                {
                    continue;
                }
                
                cellsJson.Add(new MapCellJson
                {
                    r = cell.gridCell.row,
                    c = cell.gridCell.column,
                    e = elementsJson.ToArray()
                });
                    

                if (cell.isTraversable)
                {
                    traversableCount++;
                }
                totalElements += cell.elements.Count;
            }

            string currentInventoryDesc = inventoryComponent != null ? inventoryComponent.GetInventoryDescription() : "No inventory available";

            MapDataJson mapData = new MapDataJson
            {
                cells = cellsJson.ToArray(),
                metadata = new MapMetadata
                {
                    totalCells = mapElementsCells.Length,
                    traversableCells = traversableCount,
                    elementsCount = totalElements,
                    generatedAt = DateTime.UtcNow
                },
                currentInventory = currentInventoryDesc
            };

            string jsonContent = JsonUtility.ToJson(mapData, true);

            PromptConfig promptConfig = ScriptableObject.CreateInstance<PromptConfig>();
            promptConfig.promptId = "map-system-data";
            promptConfig.promptName = "Estado Actual del Mapa";
            promptConfig.category = "Sistema del Mapa";
            promptConfig.description = "Estado actual del mapa con todas las celdas y elementos";
            promptConfig.enabled = true;
            promptConfig.priority = 10;
            promptConfig.version = "1.0";

            promptConfig.content = $@"DATOS DEL SISTEMA DEL MAPA

            El mapa está representado como una cuadrícula de celdas, donde cada celda puede contener uno o más elementos.

            Se trata de un tablero de: {_mapSystem.GridSystem.GetGridConfiguration().gridWidth}x{_mapSystem.GridSystem.GetGridConfiguration().gridHeight}

            ESTRUCTURA DEL MAPA:
            - Cada celda tiene coordenadas de fila/columna            
            - Los elementos en las celdas tienen tipos: Item, Character, u Obstacle
            - Cada elemento tiene un ID para referencia         

            INVENTARIO ACTUAL:
            {currentInventoryDesc}

            HERRAMIENTAS DE INVENTARIO DISPONIBLES:
            - pickup_item(itemId): Recoger un objeto del mapa por su ID
            - drop_item(itemType): Soltar un objeto del inventario 
            - give_item(targetCharacterId, itemType): Dar un objeto a otro personaje

            TIPOS DE OBJETOS:
            - Key: Llaves (solo 1 por slot)
            - Money: Dinero (solo 1 por slot)  
            - Apple: Manzanas (solo 1 por slot)

            DATOS ACTUALES DEL MAPA:
            {jsonContent}

            Usa estos datos del mapa para entender las relaciones espaciales, planificar movimientos e interactuar con los elementos por sus IDs.";

            return promptConfig;
        }

        private PromptConfig CreateInventoryPromptConfig()
        {
            PromptConfig inventoryPrompt = ScriptableObject.CreateInstance<PromptConfig>();
            inventoryPrompt.promptId = "inventory-status";
            inventoryPrompt.promptName = "Estado del Inventario";
            inventoryPrompt.category = "Inventario";
            inventoryPrompt.description = "Estado actual del inventario del personaje";
            inventoryPrompt.enabled = true;
            inventoryPrompt.priority = 15;
            inventoryPrompt.version = "1.0";

            inventoryPrompt.content = $@"INVENTARIO ACTUAL:

            Tienes 3 slots (uno por cada tipo de objeto):
            - Key slot: {(inventoryComponent.HasItem(InventorySystem.Enums.ItemType.Key) ? "Ocupado" : "Libre")}
            - Money slot: {(inventoryComponent.HasItem(InventorySystem.Enums.ItemType.Money) ? "Ocupado" : "Libre")}
            - Apple slot: {(inventoryComponent.HasItem(InventorySystem.Enums.ItemType.Apple) ? "Ocupado" : "Libre")}
            ";

            return inventoryPrompt;
        }

        public void Talk(string message)
        {
            _dialogObject.SetActive(true);
            _dialogText.text = message;
            List<CharacterElement> nearCharacterElements = _mapSystem.GetAllCharactesAtDistance(2, _characterElement.CurrentGridCell);
            foreach (CharacterElement nearCharacterElement in nearCharacterElements)
            {
                if (nearCharacterElement.GetComponent<CharacterAgent>() != null)
                {
                    nearCharacterElement.GetComponent<CharacterAgent>().Listen(message, agentConfigurations[0].agentName);
                }
            }
        }

        private void Listen(string message, string remit)
        {
            PromptConfig promptConversations = ScriptableObject.CreateInstance<PromptConfig>();
            promptConversations.promptId = "map-system-data";
            promptConversations.promptName = "Conversaciones";
            promptConversations.category = "";
            promptConversations.description = "Conversaciones acumuladas en el turno actual";
            promptConversations.enabled = true;
            promptConversations.priority = 10;
            promptConversations.version = "1.0";

            promptConversations.content += $@" {remit} Ha dicho: {message}";

            agentConfigurations[0].contextPrompts.Add(promptConversations);
        }

        public void AddContextPrompt(PromptConfig promptConfig)
        {
            if (agentConfigurations != null && agentConfigurations.Length > 0)
            {
                agentConfigurations[0].contextPrompts.Add(promptConfig);
            }
        }

        private void HideDialog()
        {
            _dialogObject.SetActive(false);
        }

        public bool Teleport(int row, int col)
        {
            bool moved = _characterElement.TryMoveTo(row, col);
            if (moved && inventoryComponent != null)
            {
                _characterElement.UpdateInventoryContext();
            }
            return moved;
        }

        public ViewDirection Flip()
        {
            return _characterElement.Flip();
        }

        public CharacterElement GetCharacterElement()
        {
            return _characterElement;
        }
    }
}