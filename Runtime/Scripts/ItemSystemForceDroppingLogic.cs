using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemForceDroppingLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemDroppingLogic droppingLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemDetachingLogic detachingLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        public void SendForceDropIA(ItemExtensionData itemData, bool forceNoVelocity, bool sendSingletonIA)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemForceDroppingLogic  SendForceDropIA - forceNoVelocity: {forceNoVelocity}, sendSingletonIA: {sendSingletonIA}");
#endif
            ItemExtension item = itemData.ext;
            Transform entityTransform = itemData.entity.transform;

            entitySystem.WriteEntityExtensionDataRef(itemData);

            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);
            droppingLogic.WriteTrackedVelocity(item, forceNoVelocity, out bool doApplyVelocity);

            if (sendSingletonIA)
            {
                lockstep.SendSingletonInputAction(forceDropIAId);
                // Cannot latency hide singleton input actions.
                return;
            }

            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(forceDropIAId)))
                return;

            // Latency hiding.
            ForceDropIALatencyHiding(item, doApplyVelocity);
        }

        private bool DoApplyForceDropIA(ItemExtensionData itemData)
        {
            if (itemData == null)
                return false;
            if (itemData.attachedToPlayerId == 0u)
            {
                itemData.entityData.MarkLatencyHiddenUniqueIdAsProcessed();
                return false;
            }
            return true;
        }

        [HideInInspector][SerializeField] private uint forceDropIAId;
        [LockstepInputAction(nameof(forceDropIAId))]
        public void OnForceDropIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemForceDroppingLogic  OnForceDropIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (!DoApplyForceDropIA(itemData))
                return;

            // Force dropping does not count as using the item, not setting last user.

            EntityData entityData = itemData.entityData;
            entityData.position = lockstep.ReadVector3();
            entityData.rotation = lockstep.ReadQuaternion();

            UpdateForceDroppedItemInGS(itemData);

            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            droppingLogic.ReadTrackedVelocity(physicsData, out bool doApplyVelocity);

            if (!entityData.ShouldApplyReceivedIAToLatencyState() || itemData.ext == null)
                return;

            ForceDropIAGameStateToLatency(itemData.ext, doApplyVelocity);
        }

        public void UpdateForceDroppedItemInGS(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemForceDroppingLogic  UpdateForceDroppedItemInGS");
#endif
            itemData.ClearPrimary();
            itemData.ClearSecondary();
            itemData.ClearAttached();
            itemSystem.SetAttachedToPlayerId(itemData, 0u);
        }

        private void ForceDropIALatencyHiding(ItemExtension item, bool doApplyVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemForceDroppingLogic  ForceDropIALatencyHiding - doApplyVelocity: {doApplyVelocity}");
#endif
            item.ClearPrimaryValues();
            item.ClearSecondaryValues();
            item.ClearAttachedValues();
            itemLogic.SetAttachedToPlayerId(item, 0u);
            itemLogic.OnForceDropped(item, moveToGameState: false, doInterpolate: false);

            if (!doApplyVelocity)
                return;
            PhysicsEntityExtension physicsExt = item.physicsExt;
            physicsExt.SetResponsiblePlayerId(localPlayerId);
            physicsExt.WakeUp();
            physicsExt.rb.velocity = item.trackedVelocity;
            physicsExt.rb.angularVelocity = item.TrackedAngularVelocity;
        }

        private void ForceDropIAGameStateToLatency(ItemExtension item, bool doApplyVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemForceDroppingLogic  ForceDropIAGameStateToLatency - doApplyVelocity: {doApplyVelocity}");
#endif
            item.ClearPrimaryValues();
            item.ClearSecondaryValues();
            item.ClearAttachedValues();
            itemLogic.SetAttachedToPlayerId(item, 0u);
            itemLogic.OnForceDropped(item, moveToGameState: true, doInterpolate: !doApplyVelocity);

            if (!doApplyVelocity)
                return;
            PhysicsEntityExtension physicsExt = item.physicsExt;
            physicsExt.SetResponsiblePlayerId(lockstep.SendingPlayerId);
            physicsExt.WakeUp();
            physicsExt.RigidbodyUpdate(); // Applies velocity and angularVelocity after interpolation.
        }

        public void ForceDropFromLocalOrRemotePlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  ForceDropFromLocalOrRemotePlayer - doInterpolate: {doInterpolate}");
#endif
            // Same logic. Because tracking data sync has already been informed to stop tracking
            // hand tracking data by calling the clear primary and secondary functions.
            detachingLogic.DropAndDetachFromLocalOrRemotePlayer(item, doInterpolate);
        }
    }
}
