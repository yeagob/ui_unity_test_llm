using UnityEngine;
using InventorySystem.Enums;
using InventorySystem.Models;
using MapSystem.Elements;

namespace InventorySystem.Components
{
    public class InventoryComponent : MonoBehaviour
    {
        [Header("Current Items")]
        [SerializeField] 
        private InventoryItem _keySlot;

        [SerializeField] 
        private InventoryItem _moneySlot;
        
        [SerializeField] 
        private InventoryItem _appleSlot;
        
        [Header("UI References")]
        [SerializeField]
        private GameObject _appleUI;
        
        [SerializeField]
        private GameObject _moneyUI;
        
        [SerializeField]
        private GameObject _keyUI;

        private void Awake()
        {
            _keySlot = InventoryItem.Empty();
            _moneySlot = InventoryItem.Empty();
            _appleSlot = InventoryItem.Empty();
            
            UpdateAllUIStates();
        }

        public bool HasItem(ItemType itemType)
        {
            return GetSlot(itemType).IsValid();
        }

        public bool AddItem(string itemId, ItemType itemType, ItemElement item)
        {
            if (HasItem(itemType))
            {
                return false;
            }

            SetSlot(itemType, new InventoryItem(itemId, itemType, item));
            UpdateUIState(itemType, true);
            return true;
        }

        public bool RemoveItem(ItemType itemType)
        {
            if (!HasItem(itemType))
            {
                return false;
            }

            SetSlot(itemType, InventoryItem.Empty());
            UpdateUIState(itemType, false);
            return true;
        }

        public InventoryItem GetItem(ItemType itemType)
        {
            return GetSlot(itemType);
        }

        public void ClearInventory()
        {
            _keySlot = InventoryItem.Empty();
            _moneySlot = InventoryItem.Empty();
            _appleSlot = InventoryItem.Empty();
            
            UpdateAllUIStates();
        }

        public string GetInventoryDescription()
        {
            System.Collections.Generic.List<string> items = new System.Collections.Generic.List<string>();

            if (_keySlot.IsValid()) items.Add("Key");
            if (_moneySlot.IsValid()) items.Add("Money");
            if (_appleSlot.IsValid()) items.Add("Apple");

            return items.Count == 0 ? "Empty inventory" : string.Join(", ", items);
        }

        private InventoryItem GetSlot(ItemType itemType)
        {
            switch (itemType)
            {
                case ItemType.Key:
                    return _keySlot;
                case ItemType.Money:
                    return _moneySlot;
                case ItemType.Apple:
                    return _appleSlot;
                default:
                    return InventoryItem.Empty();
            }
        }

        private void SetSlot(ItemType itemType, InventoryItem item)
        {
            switch (itemType)
            {
                case ItemType.Key:
                    _keySlot = item;
                    break;
                case ItemType.Money:
                    _moneySlot = item;
                    break;
                case ItemType.Apple:
                    _appleSlot = item;
                    break;
            }
        }

        private void UpdateUIState(ItemType itemType, bool isActive)
        {
            GameObject uiObject = GetUIObject(itemType);
            
            if (uiObject != null)
            {
                uiObject.SetActive(isActive);
            }
        }

        private void UpdateAllUIStates()
        {
            UpdateUIState(ItemType.Key, _keySlot.IsValid());
            UpdateUIState(ItemType.Money, _moneySlot.IsValid());
            UpdateUIState(ItemType.Apple, _appleSlot.IsValid());
        }

        private GameObject GetUIObject(ItemType itemType)
        {
            switch (itemType)
            {
                case ItemType.Key:
                    return _keyUI;
                case ItemType.Money:
                    return _moneyUI;
                case ItemType.Apple:
                    return _appleUI;
                default:
                    return null;
            }
        }
    }
}