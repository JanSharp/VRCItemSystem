using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemDroppingLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemPickingUpLogic pickingUpLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        public void SendDropIA(ItemExtensionData itemData, bool isPrimary, bool forceNoVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDroppingLogic  SendDropIA - isPrimary: {isPrimary}");
#endif
            if (!lockstep.IsInitialized)
                return;

            ItemExtension item = itemData.ext;
            Transform entityTransform = itemData.entity.transform;

            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteFlags(isPrimary);

            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);
            WriteTrackedVelocity(item, forceNoVelocity, out bool doApplyVelocity);

            itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(dropIAId));

            bool isHeldByOtherHand = isPrimary
                ? item.isHeldBySecondaryHand
                : item.isHeldByPrimaryHand;

            // Latency hiding.
            DropIALatencyHiding(item, isPrimary, isHeldByOtherHand, doApplyVelocity);

            // Send offsets of the other hand.
            if (isHeldByOtherHand)
                pickingUpLogic.SendPickupIA(itemData, !isPrimary);
        }

        private bool DoApplyDropIA(ItemExtensionData itemData, bool isPrimary)
        {
            if (itemData == null)
                return false;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId
                // DropIA is only valid on items that are specifically held by the dropping hand.
                || !(isPrimary ? itemData.isHeldByPrimaryHand : itemData.isHeldBySecondaryHand))
            {
                itemData.entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return false; // If attachedToPlayerId is 0u this'll also return, which works out nicely.
            }
            return true;
        }

        [HideInInspector][SerializeField] private uint dropIAId;
        [LockstepInputAction(nameof(dropIAId))]
        public void OnDropIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnDropIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            lockstep.ReadFlags(out bool isPrimary);
            if (!DoApplyDropIA(itemData, isPrimary))
                return;

            EntityData entityData = itemData.entityData;
            entityData.LastUserPlayerData = entitySystem.GetPlayerData(playerDataManager.SendingPlayerData);

            bool isHeldByOtherHand = isPrimary
                ? itemData.isHeldBySecondaryHand
                : itemData.isHeldByPrimaryHand;

            entityData.position = lockstep.ReadVector3();
            entityData.rotation = lockstep.ReadQuaternion();

            if (isPrimary)
                itemData.ClearPrimary();
            else
                itemData.ClearSecondary();

            if (!isHeldByOtherHand)
            {
                itemSystem.SetAttachedToPlayerId(itemData, 0u);
                entityData.GiveBackControlOfTransformSync(transformController, entityData.position, entityData.rotation, entityData.scale);
            }

            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            bool doApplyVelocity = false;
            if (!isHeldByOtherHand)
                ReadTrackedVelocity(physicsData, out doApplyVelocity);

            if (!entityData.ShouldApplyReceivedIAToLatencyState() || itemData.ext == null)
                return;

            DropIAGameStateToLatency(itemData.ext, isPrimary, isHeldByOtherHand, doApplyVelocity);
        }

        private void DropIALatencyHiding(ItemExtension item, bool isPrimary, bool isHeldByOtherHand, bool doApplyVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDroppingLogic  DropIALatencyHiding - isPrimary: {isPrimary}, isHeldByOtherHand: {isHeldByOtherHand}, doApplyVelocity: {doApplyVelocity}");
#endif
            if (!isHeldByOtherHand)
                itemLogic.SetAttachedToPlayerId(item, 0u);

            if (isPrimary)
            {
                item.ClearPrimaryValues();
                itemLogic.OnPrimaryChanged(item, moveToGameStateOnDetach: false, doInterpolate: false);
            }
            else
            {
                item.ClearSecondaryValues();
                itemLogic.OnSecondaryChanged(item, moveToGameStateOnDetach: false, doInterpolate: false);
            }

            if (isHeldByOtherHand || !doApplyVelocity)
                return;
            PhysicsEntityExtension physicsExt = item.physicsExt;
            physicsExt.SetResponsiblePlayerId(localPlayerId);
            physicsExt.WakeUp();
            physicsExt.rb.velocity = item.trackedVelocity;
            physicsExt.rb.angularVelocity = item.TrackedAngularVelocity;
        }

        private void DropIAGameStateToLatency(ItemExtension item, bool isPrimary, bool isHeldByOtherHand, bool doApplyVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDroppingLogic  DropIAGameStateToLatency - isPrimary: {isPrimary}, isHeldByOtherHand: {isHeldByOtherHand}, doApplyVelocity: {doApplyVelocity}");
#endif
            if (!isHeldByOtherHand)
                itemLogic.SetAttachedToPlayerId(item, 0u);

            if (isPrimary)
            {
                item.ClearPrimaryValues();
                itemLogic.OnPrimaryChanged(item, moveToGameStateOnDetach: true, doInterpolate: true);
            }
            else
            {
                item.ClearSecondaryValues();
                itemLogic.OnSecondaryChanged(item, moveToGameStateOnDetach: true, doInterpolate: true);
            }

            if (isHeldByOtherHand || !doApplyVelocity)
                return;
            PhysicsEntityExtension physicsExt = item.physicsExt;
            physicsExt.SetResponsiblePlayerId(lockstep.SendingPlayerId);
            physicsExt.WakeUp();
            physicsExt.RigidbodyUpdate(); // Applies velocity and angularVelocity after interpolation.
        }

        private bool ShouldSendAndApplyVelocity(ItemExtension item)
        {
            // IsHeld is always true when coming from this file, but when coming from force drop it may not be.
            return item.IsHeld // Held, not attached. Attached does not track velocity.
                && (item.trackedVelocity.magnitude > ItemSystem.VelocityThreshold
                    || item.trackedAngularVelocityAngle > ItemSystem.AngularVelocityDegreesThreshold);
        }

        public void WriteTrackedVelocity(ItemExtension item, bool forceNoVelocity, out bool doApplyVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDroppingLogic  WriteTrackedVelocity - forceNoVelocity: {forceNoVelocity}");
#endif
            if (item.physicsExt == null)
            {
                doApplyVelocity = false;
                return;
            }
            doApplyVelocity = !forceNoVelocity && ShouldSendAndApplyVelocity(item);
            lockstep.WriteFlags(doApplyVelocity);
            if (!doApplyVelocity)
                return;
            lockstep.WriteVector3(item.trackedVelocity);
            lockstep.WriteVector3(item.TrackedAngularVelocity);
        }

        public void ReadTrackedVelocity(PhysicsEntityExtensionData physicsData, out bool doApplyVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDroppingLogic  ReadTrackedVelocity");
#endif
            doApplyVelocity = false;
            if (physicsData == null)
                return;
            lockstep.ReadFlags(out doApplyVelocity);
            if (!doApplyVelocity)
                return;
            physicsData.velocity = lockstep.ReadVector3();
            physicsData.angularVelocity = lockstep.ReadVector3();
            physicsData.SetResponsiblePlayerId(lockstep.SendingPlayerId);
            physicsData.WakeUp();
        }

        public void DropFromLocalOrRemotePlayer(ItemExtension item, bool isPrimary, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemDroppingLogic  DropFromLocalOrRemotePlayer - isPrimary: {isPrimary}, doInterpolate: {doInterpolate}");
#endif
            // This can use the primaryBone and secondaryBone reliably thanks to the setter for those two variables,
            // as well as how the ClearPrimaryValues and ClearSecondaryValues functions stop tracking too,
            // in other words if the clear functions ran already then this here will simply do nothing.
            if (isPrimary)
                itemLogic.SetIsPrimaryTrackingDataSyncActive(item, ItemSystemUtil.BoneToTrackingType(item.primaryBone), false);
            else
                itemLogic.SetIsSecondaryTrackingDataSyncActive(item, ItemSystemUtil.BoneToTrackingType(item.secondaryBone), false);

            item.pickupIsAttached = false;
            CustomPickup pickup = item.pickup;
            if (pickup.state == CustomPickupControlState.Attached)
                Debug.LogError($"[ItemSystem] Impossible, running DropFromLocalOrRemotePlayer for a pickup which is attached not held.");

            if (isPrimary)
            {
                item.pickupIsHeldByPrimaryHand = false;
                if (pickup.state != CustomPickupControlState.None && pickup.controllingPlayerIsLocal)
                    pickup.ForceDropByExplicitHand(isPrimary: true, preventAttachment: true);
                else
                {
                    pickup.isHeldByPrimaryHand = false;
                    pickup.isHeldBySecondaryHand = item.isHeldBySecondaryHand;
                    if (!item.isHeldBySecondaryHand)
                        pickup.SetControlState(CustomPickupControlState.None);
                }
            }
            else
            {
                item.pickupIsHeldBySecondaryHand = false;
                if (pickup.controllingPlayerIsLocal)
                    pickup.ForceDropByExplicitHand(isPrimary: false, preventAttachment: true);
                else
                {
                    pickup.isHeldBySecondaryHand = false;
                    pickup.isHeldBySecondaryHand = item.isHeldBySecondaryHand;
                    if (!item.isHeldBySecondaryHand)
                        pickup.SetControlState(CustomPickupControlState.None);
                }
            }

            if (!doInterpolate)
            {
                pickup.StopInterpolation();
                itemSystem.CancelManualInterpolation(item);
            }
            else if (pickup.state != CustomPickupControlState.None)
            {
                // Not calling CancelManualInterpolation nor StartInterpolation when state is none,
                // as in that case interpolating is handled through entity transform controllers.
                itemSystem.CancelManualInterpolation(item);
                pickup.StartInterpolation();
            }
        }
    }
}
