using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// This file is generated from a definition file.
// When working on this repository, modify the definition file instead.

namespace JanSharp
{
    public static class ItemData
    {
        ///<summary>uint</summary>
        public const int Id = 0;
        ///<summary>int</summary>
        public const int PrefabIndex = 1;
        ///<summary>Vector3</summary>
        public const int Position = 2;
        ///<summary>Quaternion</summary>
        public const int Rotation = 3;
        ///<summary>bool</summary>
        public const int IsAttached = 4;
        ///<summary>int</summary>
        public const int HoldingPlayerId = 5;
        ///<summary>VRCPlayerApi.TrackingDataType</summary>
        public const int AttachedTracking = 6;
        ///<summary>ItemSync</summary>
        public const int Inst = 7;
        public const int ObjectSize = 8;

        public static object[] New(
            uint id = default,
            int prefabIndex = default,
            Vector3 position = default,
            Quaternion rotation = default,
            bool isAttached = default,
            int holdingPlayerId = -1,
            VRCPlayerApi.TrackingDataType attachedTracking = default,
            ItemSync inst = default)
        {
            object[] itemData = new object[ObjectSize];
            itemData[Id] = id;
            itemData[PrefabIndex] = prefabIndex;
            itemData[Position] = position;
            itemData[Rotation] = rotation;
            itemData[IsAttached] = isAttached;
            itemData[HoldingPlayerId] = holdingPlayerId;
            itemData[AttachedTracking] = attachedTracking;
            itemData[Inst] = inst;
            return itemData;
        }

        public static uint GetId(object[] itemData)
            => (uint)itemData[Id];
        public static void SetId(object[] itemData, uint id)
            => itemData[Id] = id;
        public static int GetPrefabIndex(object[] itemData)
            => (int)itemData[PrefabIndex];
        public static void SetPrefabIndex(object[] itemData, int prefabIndex)
            => itemData[PrefabIndex] = prefabIndex;
        public static Vector3 GetPosition(object[] itemData)
            => (Vector3)itemData[Position];
        public static void SetPosition(object[] itemData, Vector3 position)
            => itemData[Position] = position;
        public static Quaternion GetRotation(object[] itemData)
            => (Quaternion)itemData[Rotation];
        public static void SetRotation(object[] itemData, Quaternion rotation)
            => itemData[Rotation] = rotation;
        public static bool GetIsAttached(object[] itemData)
            => (bool)itemData[IsAttached];
        public static void SetIsAttached(object[] itemData, bool isAttached)
            => itemData[IsAttached] = isAttached;
        public static int GetHoldingPlayerId(object[] itemData)
            => (int)itemData[HoldingPlayerId];
        public static void SetHoldingPlayerId(object[] itemData, int holdingPlayerId)
            => itemData[HoldingPlayerId] = holdingPlayerId;
        public static VRCPlayerApi.TrackingDataType GetAttachedTracking(object[] itemData)
            => (VRCPlayerApi.TrackingDataType)itemData[AttachedTracking];
        public static void SetAttachedTracking(object[] itemData, VRCPlayerApi.TrackingDataType attachedTracking)
            => itemData[AttachedTracking] = attachedTracking;
        public static ItemSync GetInst(object[] itemData)
            => (ItemSync)itemData[Inst];
        public static void SetInst(object[] itemData, ItemSync inst)
            => itemData[Inst] = inst;
    }
}
