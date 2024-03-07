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
        [SerializeField] [HideInInspector] LockStep lockStep;
        private DataList iaData;

        [SerializeField] private GameObject[] itemPrefabs;
        [SerializeField] private Transform dummyTransform;

        /// <summary>uint id => ItemSync</summary>
        private DataDictionary allItems = new DataDictionary(); // Part of game state.
        private uint nextItemId = 0u; // Part of game state.

        private ItemSync[] activeItems = new ItemSync[ArrList.MinCapacity];
        private int activeItemsCount = 0;

        private void Update()
        {
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
            SpawnItem(
                (int)iaData[i++].Double,
                ReadVector3(iaData, ref i),
                ReadQuaternion(iaData, ref i)
            );
        }

        private void SpawnItem(int prefabIndex, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SpawnItem  prefabIndex: {prefabIndex}, position: {position}, rotation: {rotation}");
            SpawnItem(prefabIndex, position, rotation, nextItemId++);
        }

        private void SpawnItem(int prefabIndex, Vector3 position, Quaternion rotation, uint id)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SpawnItem  prefabIndex: {prefabIndex}, position: {position}, rotation: {rotation}, id: {id}");
            GameObject inst = Instantiate(itemPrefabs[prefabIndex], position, rotation, transform);
            ItemSync item = inst.GetComponent<ItemSync>();
            item.itemSystem = this;
            item.dummyTransform = dummyTransform;
            item.localPlayer = Networking.LocalPlayer;
            item.localPlayerId = Networking.LocalPlayer.playerId;
            item.pickup = item.GetComponent<VRC_Pickup>();
            item.id = id;
            allItems.Add(id, item);
            item.prefabIndex = prefabIndex;
            item.targetPosition = position;
            item.targetRotation = rotation;
        }

        private bool TryGetItem(uint itemId, out ItemSync itemSync)
        {
            Debug.Log($"[ItemSystem] ItemSystem  TryGetItem  itemId: {itemId}");
            if (!allItems.TryGetValue(itemId, out DataToken itemSyncToken))
            {
                // If it's not in the dictionary then it's been deleted already.
                itemSync = null;
                return false;
            }
            itemSync = (ItemSync)itemSyncToken.Reference;
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

            if (!TryGetItem(itemId, out ItemSync itemSync))
                return;
            itemSync.SetFloatingPosition(position, rotation);
        }

        public void SendSetHoldingPlayerIA(uint itemId, int holdingPlayerId, bool isLeftHand)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendSetHoldingPlayerIA  itemId: {itemId}, holdingPlayerId: {holdingPlayerId}, isLeftHand: {isLeftHand}");
            iaData = new DataList();
            iaData.Add((double)itemId);
            iaData.Add((double)holdingPlayerId);
            iaData.Add((double)(isLeftHand ? 1.0 : 0.0));
            lockStep.SendInputAction(setHoldingPlayerIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint setHoldingPlayerIAId;
        [LockStepInputAction(nameof(setHoldingPlayerIAId))]
        public void OnSetHoldingPlayerIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnSetHoldingPlayerIA");
            int i = 0;
            uint itemId = (uint)iaData[i++].Double;
            int holdingPlayerId = (int)iaData[i++].Double;
            bool isLeftHand = iaData[i++].Double == 1.0;

            if (!TryGetItem(itemId, out ItemSync itemSync))
                return;
            itemSync.isLeftHand = isLeftHand;
            itemSync.HoldingPlayerId = holdingPlayerId;
        }

        public void SendSetAttachedIA(uint itemId, Vector3 position, Quaternion rotation)
        {
            Debug.Log($"[ItemSystem] ItemSystem  SendSetAttachedIA  itemId: {itemId}, position: {position}, rotation: {rotation}");
            iaData = new DataList();
            iaData.Add((double)itemId);
            WriteVector3(iaData, position);
            WriteQuaternion(iaData, rotation);
            lockStep.SendInputAction(setAttachedIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint setAttachedIAId;
        [LockStepInputAction(nameof(setAttachedIAId))]
        public void OnSetAttachedIA()
        {
            Debug.Log($"[ItemSystem] ItemSystem  OnSetAttachedIA");
            int i = 0;
            uint itemId = (uint)iaData[i++].Double;
            Vector3 position = ReadVector3(iaData, ref i);
            Quaternion rotation = ReadQuaternion(iaData, ref i);

            if (!TryGetItem(itemId, out ItemSync itemSync))
                return;
            itemSync.SetAttached(position, rotation);
        }

        public void MarkAsActive(ItemSync item)
        {
            Debug.Log($"[ItemSystem] ItemSystem  MarkAsActive  itemId: {item.id}");
            item.activeIndex = activeItemsCount;
            ArrList.Add(ref activeItems, ref activeItemsCount, item);
        }

        public void MarkAsInactive(ItemSync item)
        {
            Debug.Log($"[ItemSystem] ItemSystem  MarkAsInactive  itemId: {item.id}");
            ItemSync lastItem = activeItems[--activeItemsCount];
            int activeIndex = item.activeIndex;
            activeItems[activeIndex] = lastItem;
            lastItem.activeIndex = activeIndex;
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
                ItemSync item = (ItemSync)items[i].Reference;
                stream.Add((double)item.id);
                stream.Add((double)item.prefabIndex);
                WriteVector3(stream, item.targetPosition);
                WriteQuaternion(stream, item.targetRotation);
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
                int prefabIndex = (int)stream[i++].Double;
                Vector3 position = ReadVector3(stream, ref i);
                Quaternion rotation = ReadQuaternion(stream, ref i);
                SpawnItem(prefabIndex, position, rotation, id);
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
