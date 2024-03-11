using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemSystem : LockStepGameState
    {
        public override string GameStateDisplayName => "Item System";
        [HideInInspector] public LockStep lockStep;
        private DataList iaData;
        private int lockStepPlayerId;

        [SerializeField] private GameObject[] itemPrefabs;

        ///<summary>uint id => object[] itemData</summary>
        private DataDictionary allItems = new DataDictionary(); // Part of game state.
        ///<summary>0 is an invalid id.</summary>
        private uint nextItemId = 1u; // Part of game state.

        private ItemSync[] activeItems = new ItemSync[ArrList.MinCapacity];
        private int activeItemsCount = 0;

        ///<summary>ItemData[]</summary>
        private object[][] itemsWaitingForSpawn = new object[ArrList.MinCapacity][];
        private int itemsWaitingForSpawnCount = 0;

        private void Update()
        {
            if (itemsWaitingForSpawnCount != 0)
                SpawnItem(itemsWaitingForSpawn[--itemsWaitingForSpawnCount]);

            for (int i = 0; i < activeItemsCount; i++)
                activeItems[i].UpdateActiveItem();
        }

        public void SendSpawnItemIA(int prefabIndex, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendSpawnItemIA  prefabIndex: {prefabIndex}, position: {position}, rotation: {rotation}");
            iaData = new DataList();
            iaData.Add((double)prefabIndex);
            WriteVector3(iaData, position);
            WriteQuaternion(iaData, rotation);
            lockStep.SendInputAction(spawnItemIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint spawnItemIAId;
        [LockStepInputAction(nameof(spawnItemIAId))]
        public void OnSpawnItemIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnSpawnItemIA");
            int i = 0;
            uint id = nextItemId++;
            object[] itemData = ItemData.New(
                id: id,
                prefabIndex: (int)iaData[i++].Double,
                position: ReadVector3(iaData, ref i),
                rotation: ReadQuaternion(iaData, ref i)
            );
            allItems.Add(id, new DataToken(itemData));
            ArrList.Add(ref itemsWaitingForSpawn, ref itemsWaitingForSpawnCount, itemData);
        }

        private void SpawnItem(object[] itemData)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SpawnItem  itemId: {ItemData.GetId(itemData)}");
            GameObject obj = Instantiate(
                itemPrefabs[ItemData.GetPrefabIndex(itemData)],
                ItemData.GetPosition(itemData),
                ItemData.GetRotation(itemData),
                transform);
            ItemSync itemSync = obj.GetComponent<ItemSync>();
            ItemData.SetInst(itemData, itemSync);
            itemSync.itemSystem = this;
            itemSync.localPlayer = Networking.LocalPlayer;
            itemSync.localPlayerId = Networking.LocalPlayer.playerId;
            itemSync.pickup = itemSync.GetComponent<VRC_Pickup>();
            itemSync.rb = itemSync.GetComponent<Rigidbody>();
            itemSync.id = ItemData.GetId(itemData);
            itemSync.data = itemData;
            itemSync.indexInPool = 0; // TODO: index in pool
            int holdingPlayerId = ItemData.GetHoldingPlayerId(itemData);
            if (holdingPlayerId != -1)
                itemSync.SetHoldingPlayer(holdingPlayerId, ItemData.GetIsLeftHand(itemData));
            if (ItemData.GetIsAttached(itemData))
                itemSync.SetAttached(ItemData.GetPosition(itemData), ItemData.GetRotation(itemData));
        }

        public void SendDespawnItemIA(uint itemId)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendDespawnItemIA  itemId: {itemId}");
            iaData = new DataList();
            iaData.Add((double)itemId);
            lockStep.SendInputAction(despawnItemIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint despawnItemIAId;
        [LockStepInputAction(nameof(despawnItemIAId))]
        public void OnDespawnItemIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnDespawnItemIA");
            // int i = 0;
            // if (!allItemsOld.Remove((uint)iaData[i++].Double, out DataToken itemSyncToken))
            //     return;
            // ItemSync item = (ItemSync)itemSyncToken.Reference;
            // MarkAsInactive(item);
            // // Debug.Log($"<dlt> itemSyncToken: {itemSyncToken}, type: {itemSyncToken.TokenType}, ref: {itemSyncToken.Reference}, item: {item}");
            // GameObject.Destroy(item.gameObject); // TODO: pooling.
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
            iaData = new DataList();
            iaData.Add((double)itemId);
            WriteVector3(iaData, position);
            WriteQuaternion(iaData, rotation);
            lockStep.SendInputAction(floatingPositionIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint floatingPositionIAId;
        [LockStepInputAction(nameof(floatingPositionIAId))]
        public void OnFloatingPositionIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnFloatingPositionIA");
            int i = 0;
            uint itemId = (uint)iaData[i++].Double;
            Vector3 position = ReadVector3(iaData, ref i);
            Quaternion rotation = ReadQuaternion(iaData, ref i);

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
            iaData = new DataList();
            iaData.Add((double)itemId);
            iaData.Add((double)holdingPlayerId);
            iaData.Add(isLeftHand ? 1.0 : 0.0);
            lockStep.SendInputAction(pickupIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint pickupIAId;
        [LockStepInputAction(nameof(pickupIAId))]
        public void OnPickupIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnPickupIA");
            int i = 0;
            uint itemId = (uint)iaData[i++].Double;
            int holdingPlayerId = (int)iaData[i++].Double;
            bool isLeftHand = iaData[i++].Double == 1.0;

            if (!TryGetItemData(itemId, out object[] itemData))
                return;
            ItemData.SetHoldingPlayerId(itemData, holdingPlayerId);
            ItemData.SetIsLeftHand(itemData, isLeftHand);
            ItemSync itemSync = ItemData.GetInst(itemData);
            if (itemSync != null)
                itemSync.SetHoldingPlayer(holdingPlayerId, isLeftHand);
        }

        public void SendDropIA(uint itemId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendDropIA  itemId: {itemId}, position: {position}, rotation: {rotation}");
            iaData = new DataList();
            iaData.Add((double)itemId);
            WriteVector3(iaData, position);
            WriteQuaternion(iaData, rotation);
            lockStep.SendInputAction(dropIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint dropIAId;
        [LockStepInputAction(nameof(dropIAId))]
        public void OnDropIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnDropIA");
            int i = 0;
            uint itemId = (uint)iaData[i++].Double;
            Vector3 position = ReadVector3(iaData, ref i);
            Quaternion rotation = ReadQuaternion(iaData, ref i);

            if (!TryGetItemData(itemId, out object[] itemData))
                return;
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
            iaData = new DataList();
            iaData.Add((double)itemId);
            WriteVector3(iaData, position);
            WriteQuaternion(iaData, rotation);
            lockStep.SendInputAction(attachIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint attachIAId;
        [LockStepInputAction(nameof(attachIAId))]
        public void OnAttachIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnAttachIA");
            int i = 0;
            uint itemId = (uint)iaData[i++].Double;
            Vector3 position = ReadVector3(iaData, ref i);
            Quaternion rotation = ReadQuaternion(iaData, ref i);

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
                if (item.HoldingPlayerId == lockStepPlayerId)
                    SendDropIA(item.id, item.transform.position, item.transform.rotation);
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
                    SendDropIA(item.id, item.transform.position, item.transform.rotation);
            }
        }

        public override DataList SerializeGameState()
        {
            Debug.Log($"[ItemSystem] ItemSystem  SerializeGameState");
            DataList stream = new DataList();

            stream.Add((double)nextItemId);
            DataList items = allItems.GetValues();
            int count = items.Count;
            stream.Add((double)count);
            for (int i = 0; i < count; i++)
            {
                object[] itemData = (object[])items[i].Reference;
                stream.Add((double)ItemData.GetId(itemData));
                stream.Add((double)ItemData.GetPrefabIndex(itemData));
                WriteVector3(stream, ItemData.GetPosition(itemData));
                WriteQuaternion(stream, ItemData.GetRotation(itemData));
                int holdingPlayerId = ItemData.GetHoldingPlayerId(itemData);
                stream.Add((double)holdingPlayerId);
                if (holdingPlayerId != -1)
                {
                    stream.Add((ItemData.GetIsLeftHand(itemData) ? 1.0 : 0.0)
                        + (ItemData.GetIsAttached(itemData) ? 2.0 : 0.0));
                }
            }

            return stream;
        }

        public override string DeserializeGameState(DataList stream)
        {
            Debug.Log($"[ItemSystem] ItemSystem  DeserializeGameState");
            int i = 0;

            nextItemId = (uint)stream[i++].Double;
            int count = (int)stream[i++].Double;
            for (int j = 0; j < count; j++)
            {
                uint id = (uint)stream[i++].Double;
                object[] itemData = ItemData.New(
                    id: id,
                    prefabIndex: (int)stream[i++].Double,
                    position: ReadVector3(stream, ref i),
                    rotation: ReadQuaternion(stream, ref i)
                );
                int holdingPlayerId = (int)stream[i++].Double;
                ItemData.SetHoldingPlayerId(itemData, holdingPlayerId);
                if (holdingPlayerId != -1)
                {
                    int flags = (int)stream[i++].Double;
                    ItemData.SetIsLeftHand(itemData, (flags & 1) != 0);
                    ItemData.SetIsAttached(itemData, (flags & 2) != 0);
                }
                allItems.Add(id, new DataToken(itemData));
                ArrList.Add(ref itemsWaitingForSpawn, ref itemsWaitingForSpawnCount, itemData);
            }

            return null;
        }

        private static void WriteVector3(DataList stream, Vector3 vec)
        {
            stream.Add((double)vec.x);
            stream.Add((double)vec.y);
            stream.Add((double)vec.z);
        }

        private static void WriteQuaternion(DataList stream, Quaternion quat)
        {
            stream.Add((double)quat.x);
            stream.Add((double)quat.y);
            stream.Add((double)quat.z);
            stream.Add((double)quat.w);
        }

        private static Vector3 ReadVector3(DataList stream, ref int i)
        {
            return new Vector3(
                (float)stream[i++].Double,
                (float)stream[i++].Double,
                (float)stream[i++].Double
            );
        }

        private static Quaternion ReadQuaternion(DataList stream, ref int i)
        {
            return new Quaternion(
                (float)stream[i++].Double,
                (float)stream[i++].Double,
                (float)stream[i++].Double,
                (float)stream[i++].Double
            );
        }
    }
}
