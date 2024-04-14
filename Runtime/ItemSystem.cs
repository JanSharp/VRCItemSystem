﻿using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemSystem : LockStepGameState
    {
        // TODO: Items held by players get spawned near 0, 0 and quickly interpolate to their hand. Not good
        // TODO: There's some bug where an item can get stuck being attached to a player. Worst part is since
        // the player ignores attachments to themselves, they don't know about it
        // TODO: ItemSystemDebug controller, listing all items and their state

        public override string GameStateInternalName => "jansharp.item-system";
        public override string GameStateDisplayName => "Item System";
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;
        [HideInInspector] public LockStep lockStep;
        private uint lockStepPlayerId;

        [SerializeField] private GameObject[] itemPrefabs;
        ///<summary>unusedItemInsts[prefabIndex][itemSync.instIndex]</summary>
        private ItemSync[][] unusedItemInsts;
        ///<summary>unusedItemInstsCounts[prefabIndex]</summary>
        private int[] unusedItemInstsCounts;

        ///<summary>uint id => object[] itemData</summary>
        private DataDictionary allItems = new DataDictionary(); // Part of game state.
        ///<summary>0 is an invalid id.</summary>
        private uint nextItemId = 1u; // Part of game state.

        private ItemSync[] activeItems = new ItemSync[ArrList.MinCapacity];
        private int activeItemsCount = 0;

        ///<summary>ItemData[]</summary>
        private object[][] itemsWaitingForSpawn = new object[ArrList.MinCapacity][];
        private int itemsWaitingForSpawnCount = 0;

        private void Start()
        {
            int prefabCount = itemPrefabs.Length;
            unusedItemInsts = new ItemSync[prefabCount][];
            unusedItemInstsCounts = new int[prefabCount];
            for (int i = 0; i < prefabCount; i++)
                unusedItemInsts[i] = new ItemSync[ArrList.MinCapacity];
        }

        private void Update()
        {
            if (itemsWaitingForSpawnCount != 0)
                SpawnItem(itemsWaitingForSpawn[--itemsWaitingForSpawnCount]);

            for (int i = 0; i < activeItemsCount; i++)
                activeItems[i].UpdateActiveItem();
        }

        private void CompleteReset()
        {
            DataList values = allItems.GetValues();
            int count = values.Count;
            for (int i = 0; i < count; i++)
            {
                object[] itemData = (object[])values[i].Reference;
                DespawnItem(itemData);
            }
            allItems.Clear();
            nextItemId = 1u;
        }

        public void SendSpawnItemIA(int prefabIndex, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendSpawnItemIA  prefabIndex: {prefabIndex}, position: {position}, rotation: {rotation}");
            lockStep.WriteSmall((uint)prefabIndex);
            lockStep.Write(position);
            lockStep.Write(rotation);
            lockStep.SendInputAction(spawnItemIAId);
        }

        [SerializeField] [HideInInspector] private uint spawnItemIAId;
        [LockStepInputAction(nameof(spawnItemIAId))]
        public void OnSpawnItemIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnSpawnItemIA");
            uint id = nextItemId++;
            object[] itemData = ItemData.New(
                id: id,
                prefabIndex: (int)lockStep.ReadSmallUInt(),
                position: lockStep.ReadVector3(),
                rotation: lockStep.ReadQuaternion()
            );
            allItems.Add(id, new DataToken(itemData));
            ArrList.Add(ref itemsWaitingForSpawn, ref itemsWaitingForSpawnCount, itemData);
        }

        private ItemSync GetItemInst(object[] itemData)
        {
            ItemSync itemSync;
            int prefabIndex = ItemData.GetPrefabIndex(itemData);
            Debug.Log($"[ItemSystemDebug] GetItemInst  (inner) - {prefabIndex}");
            int unusedCount = unusedItemInstsCounts[prefabIndex];
            if (unusedCount > 0)
            {
                itemSync = unusedItemInsts[prefabIndex][--unusedCount];
                unusedItemInstsCounts[prefabIndex] = unusedCount;
                itemSync.id = ItemData.GetId(itemData);
                // Cannot use Rigidbody position and rotation because that's not instant enough, therefore the
                // reactivated item could still be in a despawner trigger, causing it to instantly disappear.
                itemSync.transform.position = ItemData.GetPosition(itemData);
                itemSync.transform.rotation = ItemData.GetRotation(itemData);
                itemSync.Enable();
                return itemSync;
            }

            GameObject prefab = itemPrefabs[prefabIndex];
            GameObject obj = Instantiate(
                prefab,
                ItemData.GetPosition(itemData),
                ItemData.GetRotation(itemData),
                transform);
            obj.name = prefab.name;
            itemSync = obj.GetComponent<ItemSync>();
            itemSync.itemSystem = this;
            itemSync.localPlayer = Networking.LocalPlayer;
            itemSync.localPlayerId = Networking.LocalPlayer.playerId;
            itemSync.pickup = itemSync.GetComponent<VRC_Pickup>();
            itemSync.rb = itemSync.GetComponent<Rigidbody>();
            return itemSync;
        }

        private void SpawnItem(object[] itemData)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SpawnItem  itemId: {ItemData.GetId(itemData)}");
            ItemSync itemSync = GetItemInst(itemData);
            ItemData.SetInst(itemData, itemSync);
            itemSync.id = ItemData.GetId(itemData);
            int holdingPlayerId = ItemData.GetHoldingPlayerId(itemData);
            if (holdingPlayerId != -1)
                itemSync.SetHoldingPlayer(holdingPlayerId, ItemData.GetIsLeftHand(itemData));
            if (ItemData.GetIsAttached(itemData))
                itemSync.SetAttached(ItemData.GetPosition(itemData), ItemData.GetRotation(itemData));
        }

        public void SendDespawnItemIA(uint itemId)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendDespawnItemIA  itemId: {itemId}");
            lockStep.WriteSmall(itemId);
            lockStep.SendInputAction(despawnItemIAId);
        }

        [SerializeField] [HideInInspector] private uint despawnItemIAId;
        [LockStepInputAction(nameof(despawnItemIAId))]
        public void OnDespawnItemIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnDespawnItemIA");
            if (!allItems.Remove(lockStep.ReadSmallUInt(), out DataToken itemDataToken))
                return;
            object[] itemData = (object[])itemDataToken.Reference;
            DespawnItem(itemData);
        }

        private void DespawnItem(object[] itemData)
        {
            ItemData.SetIsAttached(itemData, false);
            ItemData.SetHoldingPlayerId(itemData, -1);
            ItemSync itemSync = ItemData.GetInst(itemData);
            if (itemSync == null)
                return;
            itemSync.Disable();
            int prefabIndex = ItemData.GetPrefabIndex(itemData);
            ItemSync[] unused = unusedItemInsts[prefabIndex];
            int unusedCount = unusedItemInstsCounts[prefabIndex];
            ArrList.Add(ref unused, ref unusedCount, itemSync);
            unusedItemInsts[prefabIndex] = unused;
            unusedItemInstsCounts[prefabIndex] = unusedCount;
        }

        private bool TryGetItemData(uint itemId, out object[] itemData)
        {
            Debug.Log($"[ItemSystem] ItemSystem  TryGetItem  itemId: {itemId}");
            if (!allItems.TryGetValue(itemId, out DataToken itemDataToken))
            {
                // If it's not in the dictionary then it's been deleted already.
                itemData = null;
                return false;
            }
            itemData = (object[])itemDataToken.Reference;
            return true;
        }

        public void SendFloatingPositionIA(uint itemId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendFloatingPositionIA  itemId: {itemId}, position: {position}, rotation: {rotation}");
            lockStep.WriteSmall(itemId);
            lockStep.Write(position);
            lockStep.Write(rotation);
            lockStep.SendInputAction(floatingPositionIAId);
        }

        [SerializeField] [HideInInspector] private uint floatingPositionIAId;
        [LockStepInputAction(nameof(floatingPositionIAId))]
        public void OnFloatingPositionIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnFloatingPositionIA");
            uint itemId = lockStep.ReadSmallUInt();
            Vector3 position = lockStep.ReadVector3();
            Quaternion rotation = lockStep.ReadQuaternion();

            if (!TryGetItemData(itemId, out object[] itemData))
                return;
            ItemData.SetPosition(itemData, position);
            ItemData.SetRotation(itemData, rotation);
            ItemSync itemSync = ItemData.GetInst(itemData);
            if (itemSync != null)
                itemSync.SetFloatingPosition(position, rotation);
        }

        public void SendPickupIA(uint itemId, int holdingPlayerId, bool isLeftHand)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendPickupIA  itemId: {itemId}, holdingPlayerId: {holdingPlayerId}, isLeftHand: {isLeftHand}");
            lockStep.WriteSmall(itemId);
            lockStep.WriteSmall(holdingPlayerId);
            lockStep.Write((byte)(isLeftHand ? 1 : 0));
            lockStep.SendInputAction(pickupIAId);
        }

        [SerializeField] [HideInInspector] private uint pickupIAId;
        [LockStepInputAction(nameof(pickupIAId))]
        public void OnPickupIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnPickupIA");
            uint itemId = lockStep.ReadSmallUInt();
            int holdingPlayerId = lockStep.ReadSmallInt();
            bool isLeftHand = lockStep.ReadByte() == 1;

            if (!TryGetItemData(itemId, out object[] itemData))
                return;
            ItemData.SetIsAttached(itemData, false); // After an item is picked up, it is not attached yet.
            ItemData.SetHoldingPlayerId(itemData, holdingPlayerId);
            ItemData.SetIsLeftHand(itemData, isLeftHand);
            ItemSync itemSync = ItemData.GetInst(itemData);
            if (itemSync != null)
                itemSync.SetHoldingPlayer(holdingPlayerId, isLeftHand);
        }

        public void SendDropIA(uint itemId, int prevHoldingPlayerId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendDropIA  itemId: {itemId}, prevHoldingPlayerId: {prevHoldingPlayerId}, position: {position}, rotation: {rotation}");
            lockStep.WriteSmall(itemId);
            lockStep.WriteSmall(prevHoldingPlayerId);
            lockStep.Write(position);
            lockStep.Write(rotation);
            lockStep.SendInputAction(dropIAId);
        }

        [SerializeField] [HideInInspector] private uint dropIAId;
        [LockStepInputAction(nameof(dropIAId))]
        public void OnDropIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnDropIA");
            uint itemId = lockStep.ReadSmallUInt();
            int prevHoldingPlayerId = lockStep.ReadSmallInt();
            Vector3 position = lockStep.ReadVector3();
            Quaternion rotation = lockStep.ReadQuaternion();

            if (!TryGetItemData(itemId, out object[] itemData))
                return;
            if (prevHoldingPlayerId != ItemData.GetHoldingPlayerId(itemData))
                return; // The player that dropped the item isn't the one that's holding it anymore, ignore it.
            ItemData.SetHoldingPlayerId(itemData, -1);
            ItemData.SetPosition(itemData, position);
            ItemData.SetRotation(itemData, rotation);
            ItemSync itemSync = ItemData.GetInst(itemData);
            if (itemSync != null)
                itemSync.UnsetHoldingPlayer(position, rotation);
        }

        public void SendAttachIA(uint itemId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendAttachIA  itemId: {itemId}, position: {position}, rotation: {rotation}");
            lockStep.WriteSmall(itemId);
            lockStep.Write(position);
            lockStep.Write(rotation);
            lockStep.SendInputAction(attachIAId);
        }

        [SerializeField] [HideInInspector] private uint attachIAId;
        [LockStepInputAction(nameof(attachIAId))]
        public void OnAttachIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnAttachIA");
            uint itemId = lockStep.ReadSmallUInt();
            Vector3 position = lockStep.ReadVector3();
            Quaternion rotation = lockStep.ReadQuaternion();

            if (!TryGetItemData(itemId, out object[] itemData))
                return;
            ItemData.SetIsAttached(itemData, true);
            ItemData.SetPosition(itemData, position);
            ItemData.SetRotation(itemData, rotation);
            ItemSync itemSync = ItemData.GetInst(itemData);
            if (itemSync != null)
                itemSync.SetAttached(position, rotation);
        }

        public void MarkAsActive(ItemSync item)
        {
            Debug.Log($"[ItemSystem] ItemSystem  MarkAsActive  itemId: {item.id}");
            if (item.activeIndex != -1)
                return;
            item.activeIndex = activeItemsCount;
            ArrList.Add(ref activeItems, ref activeItemsCount, item);
        }

        public void MarkAsInactive(ItemSync item)
        {
            Debug.Log($"[ItemSystem] ItemSystem  MarkAsInactive  itemId: {item.id}");
            if (item.activeIndex == -1)
                return;
            ItemSync lastItem = activeItems[--activeItemsCount];
            int activeIndex = item.activeIndex;
            activeItems[activeIndex] = lastItem;
            lastItem.activeIndex = activeIndex;
            item.activeIndex = -1;
        }

        [LockStepEvent(LockStepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnClientLeft  playerId: {lockStepPlayerId}");
            if (!lockStep.IsMaster)
                return;
            // Drop items held by the left player.
            for (int i = 0; i < activeItemsCount; i++)
            {
                ItemSync item = activeItems[i];
                int playerId = item.HoldingPlayerId;
                if (playerId == (int)lockStepPlayerId)
                    SendDropIA(item.id, playerId, item.transform.position, item.transform.rotation);
            }
        }

        [LockStepEvent(LockStepEventType.OnMasterChanged)]
        public void OnMasterChanged()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnMasterChanged");
            if (!lockStep.IsMaster)
                return;
            // Check if any items are held by players that no longer exist.
            for (int i = 0; i < activeItemsCount; i++)
            {
                ItemSync item = activeItems[i];
                int playerId = item.HoldingPlayerId;
                if (playerId == -1)
                    continue;
                if (VRCPlayerApi.GetPlayerById(playerId) == null)
                    SendDropIA(item.id, playerId, item.transform.position, item.transform.rotation);
            }
        }

        public override void SerializeGameState(bool isExport)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SerializeGameState  isExport: {isExport}");

            if (!isExport)
                lockStep.WriteSmall(nextItemId);
            DataList items = allItems.GetValues();
            int count = items.Count;
            lockStep.WriteSmall((uint)count);
            for (int i = 0; i < count; i++)
            {
                object[] itemData = (object[])items[i].Reference;
                if (!isExport)
                    lockStep.WriteSmall(ItemData.GetId(itemData));
                lockStep.WriteSmall((uint)ItemData.GetPrefabIndex(itemData));
                int holdingPlayerId = ItemData.GetHoldingPlayerId(itemData);
                if (isExport)
                {
                    if (holdingPlayerId == -1)
                    {
                        lockStep.Write(ItemData.GetPosition(itemData));
                        lockStep.Write(ItemData.GetRotation(itemData));
                    }
                    else
                    {
                        Transform itemTransform = ItemData.GetInst(itemData).transform;
                        lockStep.Write(itemTransform.position);
                        lockStep.Write(itemTransform.rotation);
                    }
                }
                else
                {
                    lockStep.Write(ItemData.GetPosition(itemData));
                    lockStep.Write(ItemData.GetRotation(itemData));
                    lockStep.WriteSmall(holdingPlayerId);
                    if (holdingPlayerId != -1)
                    {
                        lockStep.Write((byte)((ItemData.GetIsLeftHand(itemData) ? 1 : 0)
                            | (ItemData.GetIsAttached(itemData) ? 2 : 0)));
                    }
                }
            }
        }

        public override string DeserializeGameState(bool isImport)
        {
            Debug.Log($"[ItemSystem] ItemSystem  DeserializeGameState  isImport: {isImport}");

            if (isImport)
                CompleteReset();

            if (!isImport)
                nextItemId = lockStep.ReadSmallUInt();
            int count = (int)lockStep.ReadSmallUInt();
            for (int j = 0; j < count; j++)
            {
                uint id = isImport ? nextItemId++ : lockStep.ReadSmallUInt();
                object[] itemData = ItemData.New(
                    id: id,
                    prefabIndex: (int)lockStep.ReadSmallUInt(),
                    position: lockStep.ReadVector3(),
                    rotation: lockStep.ReadQuaternion()
                );
                if (!isImport)
                {
                    int holdingPlayerId = lockStep.ReadSmallInt();
                    ItemData.SetHoldingPlayerId(itemData, holdingPlayerId);
                    if (holdingPlayerId != -1)
                    {
                        int flags = lockStep.ReadByte();
                        ItemData.SetIsLeftHand(itemData, (flags & 1) != 0);
                        ItemData.SetIsAttached(itemData, (flags & 2) != 0);
                    }
                }
                allItems.Add(id, new DataToken(itemData));
                ArrList.Add(ref itemsWaitingForSpawn, ref itemsWaitingForSpawnCount, itemData);
            }

            return null;
        }
    }
}
