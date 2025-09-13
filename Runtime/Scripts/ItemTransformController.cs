using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemTransformController : EntityTransformController
    {
        [HideInInspector][SingletonReference] public ItemSystem itemSystem;

        public override void OnControlLost(EntityData entityData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  OnControlLost");
#endif
            ItemExtensionData itemData = entityData.GetExtensionData<ItemExtensionData>(nameof(ItemExtensionData));
            itemSystem.UpdateDroppedItem(itemData);
        }

        public override void OnControlTakenOver(EntityData entityData, EntityTransformController newController)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  OnControlTakenOver");
#endif
            OnControlLost(entityData);
        }

        public override void OnLatencyControlLost(Entity entity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  OnLatencyControlLost");
#endif
            ItemExtension item = entity.GetExtension<ItemExtension>(nameof(ItemExtensionData));
            item.DetachFromPlayer();
        }

        public override void OnLatencyControlTakenOver(Entity entity, EntityTransformController newController)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  OnLatencyControlTakenOver");
#endif
            OnLatencyControlLost(entity);
        }

        public override bool TryGetGameStatePosition(EntityData entityData, out Vector3 position)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  TryGetGameStatePosition");
#endif
            position = Vector3.zero;
            return false;
        }

        public override bool TryGetGameStateRotation(EntityData entityData, out Quaternion rotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  TryGetGameStateRotation");
#endif
            rotation = Quaternion.identity;
            return false;
        }

        public override bool TryGetGameStateScale(EntityData entityData, out Vector3 scale)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemTransformController  TryGetGameStateScale");
#endif
            scale = entityData.scale;
            return true;
        }
    }
}
