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

        [SerializeField] GameObject[] itemPrefabs;

        /// <summary>uint id => ItemSync</summary>
        private DataDictionary allItems = new DataDictionary(); // Part of game state.
        private uint nextItemId = 0u; // Part of game state.

        public void SendSpawnItemIA(int prefabIndex, Vector3 position, Quaternion rotation)
        {
            iaData = new DataList();
            iaData.Add((double)prefabIndex);
            iaData.Add((double)position.x);
            iaData.Add((double)position.y);
            iaData.Add((double)position.z);
            iaData.Add((double)rotation.x);
            iaData.Add((double)rotation.y);
            iaData.Add((double)rotation.z);
            iaData.Add((double)rotation.w);
            lockStep.SendInputAction(spawnItemIAId, iaData);
        }

        [SerializeField] [HideInInspector] private uint spawnItemIAId;
        [LockStepInputAction(nameof(spawnItemIAId))]
        public void OnSpawnItemIA()
        {
            int i = 0;
            SpawnItem(
                (int)iaData[i++].Double,
                new Vector3(
                    (float)iaData[i++].Double,
                    (float)iaData[i++].Double,
                    (float)iaData[i++].Double
                ),
                new Quaternion(
                    (float)iaData[i++].Double,
                    (float)iaData[i++].Double,
                    (float)iaData[i++].Double,
                    (float)iaData[i++].Double
                )
            );
        }

        private void SpawnItem(int prefabIndex, Vector3 position, Quaternion rotation)
        {
            SpawnItem(prefabIndex, position, rotation, nextItemId++);
        }

        private void SpawnItem(int prefabIndex, Vector3 position, Quaternion rotation, uint id)
        {
            GameObject inst = Instantiate(itemPrefabs[prefabIndex], position, rotation, transform);
            ItemSync item = inst.GetComponent<ItemSync>();
            item.id = id;
            allItems.Add(id, item);
            item.prefabIndex = prefabIndex;
            item.position = position;
            item.rotation = rotation;
        }

        public override DataList SerializeGameState()
        {
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
                Vector3 position = item.position;
                stream.Add((double)position.x);
                stream.Add((double)position.y);
                stream.Add((double)position.z);
                Quaternion rotation = item.rotation;
                stream.Add((double)rotation.x);
                stream.Add((double)rotation.y);
                stream.Add((double)rotation.z);
                stream.Add((double)rotation.w);
            }

            return stream;
        }

        public override string DeserializeGameState(DataList stream)
        {
            int i = 0;

            nextItemId = (uint)stream[i++].Double;
            int count = (int)stream[i++].Double;
            for (int j = 0; j < count; j++)
            {
                uint id = (uint)stream[i++].Double;
                int prefabIndex = (int)stream[i++].Double;
                Vector3 position = new Vector3(
                    (float)stream[i++].Double,
                    (float)stream[i++].Double,
                    (float)stream[i++].Double
                );
                Quaternion rotation = new Quaternion(
                    (float)stream[i++].Double,
                    (float)stream[i++].Double,
                    (float)stream[i++].Double,
                    (float)stream[i++].Double
                );
                SpawnItem(prefabIndex, position, rotation, id);
            }

            return null;
        }
    }
}
