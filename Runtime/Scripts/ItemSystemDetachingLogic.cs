using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemDetachingLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;

        public void SendDetachIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDetachingLogic  SendDetachIA");
#endif
            ItemExtension item = itemData.ext;
            Transform entityTransform = itemData.entity.transform;

            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);

            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(detachIAId)))
                return;

            // Latency hiding.
            DetachIALatencyHiding(item);
        }

        private void DetachIALatencyHiding(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDetachingLogic  DetachIALatencyHiding");
#endif
            item.ClearAttachedValues();
            itemLogic.SetAttachedToPlayerId(item, 0u);
            itemLogic.OnAttachedChanged(item, moveToGameStateOnDetach: false, doInterpolate: false);
        }

        private void DetachIAGameStateToLatency(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDetachingLogic  DetachIAGameStateToLatency");
#endif
            item.ClearAttachedValues();
            itemLogic.SetAttachedToPlayerId(item, 0u);
            itemLogic.OnAttachedChanged(item, moveToGameStateOnDetach: true, doInterpolate: true);
        }

        private bool DoApplyDetachIA(ItemExtensionData itemData)
        {
            if (itemData == null)
                return false;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId
                || !itemData.isAttached) // DetachIA is only valid on items that are specifically attached.
            {
                itemData.entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return false; // If attachedToPlayerId is 0u this'll also return, which works out nicely.
            }
            return true;
        }

        [HideInInspector][SerializeField] private uint detachIAId;
        [LockstepInputAction(nameof(detachIAId))]
        public void OnDetachIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDetachingLogic  OnDetachIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (!DoApplyDetachIA(itemData))
                return;

            EntityData entityData = itemData.entityData;
            entityData.LastUserPlayerData = entitySystem.GetPlayerData(playerDataManager.SendingPlayerData);

            entityData.GiveBackControlOfTransformSync(
                transformController,
                lockstep.ReadVector3(),
                lockstep.ReadQuaternion(),
                entityData.scale);
            itemSystem.SetAttachedToPlayerId(itemData, 0u);
            itemData.ClearAttached();

            if (!entityData.ShouldApplyReceivedIAToLatencyState() || itemData.ext == null)
                return;

            DetachIAGameStateToLatency(itemData.ext);
        }

        public void DropAndDetachFromLocalOrRemotePlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDetachingLogic  DropAndDetachFromLocalOrRemotePlayer - doInterpolate: {doInterpolate}");
#endif
            item.pickupIsHeldByPrimaryHand = false;
            item.pickupIsHeldBySecondaryHand = false;
            item.pickupIsAttached = false;
            CustomPickup pickup = item.pickup;
            itemSystem.DropAndDetachPickupLocallyAndRemotely(pickup);

            if (!doInterpolate)
                itemSystem.CancelManualInterpolation(item);
            // Doing nothing when doInterpolate is true, interpolating is handled through
            // entity transform controllers when pickup state is none.
        }
    }
}
