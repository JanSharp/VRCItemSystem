using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemPickingUpLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemForceDroppingLogic forceDroppingLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;
        [HideInInspector][SerializeField][SingletonReference] private CustomInteractablesManagerAPI interactables;

        #region State Machine
        private Vector3 anchorVector;
        private Quaternion anchorRotation;
        private Vector3 offsetVector;
        private Quaternion offsetRotation;
        #endregion

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        private bool LocalPlayerHasBone(HumanBodyBones bone) => localPlayer.GetBonePosition(bone) != Vector3.zero;

        private void WriteOffsets()
        {
            lockstep.WriteVector3(anchorVector);
            lockstep.WriteQuaternion(anchorRotation);
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
        }

        private void ReadOffsets()
        {
            anchorVector = lockstep.ReadVector3();
            anchorRotation = lockstep.ReadQuaternion();
            offsetVector = lockstep.ReadVector3();
            offsetRotation = lockstep.ReadQuaternion();
        }

        private void ReadPrimaryOffsets(ItemExtensionData itemData)
        {
            ReadOffsets();
            itemData.primaryAnchorVector = anchorVector;
            itemData.primaryAnchorRotation = anchorRotation;
            itemData.primaryOffsetVector = offsetVector;
            itemData.primaryOffsetRotation = offsetRotation;
        }

        private void ReadSecondaryOffsets(ItemExtensionData itemData)
        {
            ReadOffsets();
            itemData.secondaryAnchorVector = anchorVector;
            itemData.secondaryAnchorRotation = anchorRotation;
            itemData.secondaryOffsetVector = offsetVector;
            itemData.secondaryOffsetRotation = offsetRotation;
        }

        private bool CanSendPickupIA(ItemExtensionData itemData, bool isPrimary)
        {
            ItemExtension item = itemData.ext;
            if (item == null || !lockstep.IsInitialized)
                return false; // TODO: Either drop the item, or send a pickup IA once lockstep is initialized.
            CustomPickup pickup = item.pickup;
            if ((isPrimary ? !pickup.isHeldByPrimaryHand : !pickup.isHeldBySecondaryHand) || !pickup.controllingPlayerIsLocal)
            {
                Debug.LogError($"[ItemSystem] Attempt to {nameof(SendPickupIA)} "
                    + $"for an item which is not held by the local player.");
                return false;
            }
            return true;
        }

        public void SendPickupIA(ItemExtensionData itemData, bool isPrimary)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemPickingUpLogic  SendPickupIA - isPrimary: {isPrimary}");
#endif
            if (!CanSendPickupIA(itemData, isPrimary))
                return;
            ItemExtension item = itemData.ext;
            CustomPickup pickup = item.pickup;
            EntityData entityData = itemData.entityData;

            // Setup.
            VRCPlayerApi.TrackingDataType trackingDataType = isPrimary ? pickup.primaryHeldTrackingType : pickup.secondaryHeldTrackingType;
            HumanBodyBones bone = ItemSystemUtil.TrackingTypeToBone(trackingDataType);
            bool boneExists = LocalPlayerHasBone(bone);

            anchorVector = interactables.GetAnchorOffsetVector(trackingDataType);
            anchorRotation = interactables.GetRotationNormalization(trackingDataType);
            offsetVector = isPrimary ? pickup.primaryOffsetVector : pickup.secondaryOffsetVector;
            offsetRotation = isPrimary ? pickup.primaryOffsetRotation : pickup.secondaryOffsetRotation;

            if (boneExists)
                ItemSystemUtil.TrackingDataOffsetsToBoneOffsets(
                    localPlayer,
                    trackingDataType, bone,
                    anchorVector, anchorRotation,
                    out anchorVector, out anchorRotation);

            // Latency hiding.
            // Must happen before sending the pickup IA in order for the tracking data sync IA to run first, if it's used.
            if (isPrimary)
                PrimaryPickupIALatency(item, localPlayerId, bone, boneExists);
            else
                SecondaryPickupIALatency(item, localPlayerId, bone, boneExists);

            // Send IA.
            entitySystem.WriteEntityExtensionDataRef(itemData);
            entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteFlags(isPrimary, boneExists);
            lockstep.WriteSmallInt((int)bone);
            WriteOffsets();
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(pickupIAId));
        }

        private bool DoApplyPickupIA(ItemExtensionData itemData)
        {
            if (itemData == null)
                return false;
            // If theft were to be disallowed in the GS, not just client side, this would be the place to do it.
            // If that were to be the case, call itemData.entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
            return true;
        }

        [HideInInspector][SerializeField] private uint pickupIAId;
        [LockstepInputAction(nameof(pickupIAId))]
        public void OnPickupIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemPickingUpLogic  OnPickupIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (!DoApplyPickupIA(itemData))
                return;
            EntityData entityData = itemData.entityData;
            uint sendingPlayerId = lockstep.SendingPlayerId;

            if (itemData.attachedToPlayerId != 0u && itemData.attachedToPlayerId != sendingPlayerId)
                forceDroppingLogic.UpdateForceDroppedItemInGS(itemData);
            else if (itemData.isAttached)
                itemData.ClearAttached();

            // Read IA data.
            entityData.ReadPotentiallyUnknownTransformValues();
            lockstep.ReadFlags(out bool isPrimary, out bool boneExists);
            if (isPrimary)
            {
                itemData.isHeldByPrimaryHand = true;
                itemData.primaryBone = (HumanBodyBones)lockstep.ReadSmallInt();
                itemData.primaryBoneExists = boneExists;
                ReadPrimaryOffsets(itemData);
            }
            else
            {
                itemData.isHeldBySecondaryHand = true;
                itemData.secondaryBone = (HumanBodyBones)lockstep.ReadSmallInt();
                itemData.secondaryBoneExists = boneExists;
                ReadSecondaryOffsets(itemData);
            }

            // Generic picking up stuff.
            itemSystem.SetAttachedToPlayerId(itemData, sendingPlayerId);
            entityData.LastUserPlayerData = entitySystem.GetPlayerData(playerDataManager.SendingPlayerData);
            entityData.TakeControlOfTransformSync(transformController);
            entityData.SetParentEntityInGS(null);

            // Putting the physics entity to sleep after the item has taken control theoretically
            // reduces the total amount of work that needs to be done by the entity system.
            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            if (physicsData != null)
                physicsData.GoToSleep();

            // Latency State.
            ItemExtension item = itemData.ext;
            if (entityData.ShouldApplyReceivedIAToLatencyState() && item != null)
                if (isPrimary)
                    PrimaryPickupIALatency(item, itemData.attachedToPlayerId, itemData.primaryBone, itemData.primaryBoneExists);
                else
                    SecondaryPickupIALatency(item, itemData.attachedToPlayerId, itemData.secondaryBone, itemData.secondaryBoneExists);
        }

        private void PrimaryPickupIALatency(ItemExtension item, uint playerId, HumanBodyBones bone, bool boneExists)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemPickingUpLogic  PrimaryPickupIALatency - playerId: {playerId}, boneExists: {boneExists}");
#endif
            if (item.attachedToPlayerId != 0u && item.attachedToPlayerId != playerId)
                forceDroppingLogic.UpdateForceDroppedItemInLS(item);
            else if (item.isAttached)
                item.ClearAttachedValues();

            item.isHeldByPrimaryHand = true;
            itemLogic.SetPrimaryBone(item, bone);
            item.primaryBoneExists = boneExists;
            item.primaryAnchorVector = anchorVector;
            item.primaryAnchorRotation = anchorRotation;
            item.primaryOffsetVector = offsetVector;
            item.primaryOffsetRotation = offsetRotation;

            itemLogic.SetAttachedToPlayerId(item, playerId);
            itemLogic.OnPrimaryChanged(item, moveToGameStateOnDetach: true, doInterpolate: true);
            itemSystem.PutPhysicsEntityExtensionToSleep(item);
        }

        private void SecondaryPickupIALatency(ItemExtension item, uint playerId, HumanBodyBones bone, bool boneExists)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemPickingUpLogic  SecondaryPickupIALatency - playerId: {playerId}, boneExists: {boneExists}");
#endif
            if (item.attachedToPlayerId != 0u && item.attachedToPlayerId != playerId)
                forceDroppingLogic.UpdateForceDroppedItemInLS(item);
            else if (item.isAttached)
                item.ClearAttachedValues();

            item.isHeldBySecondaryHand = true;
            itemLogic.SetSecondaryBone(item, bone);
            item.secondaryBoneExists = boneExists;
            item.secondaryAnchorVector = anchorVector;
            item.secondaryAnchorRotation = anchorRotation;
            item.secondaryOffsetVector = offsetVector;
            item.secondaryOffsetRotation = offsetRotation;

            itemLogic.SetAttachedToPlayerId(item, playerId);
            itemLogic.OnSecondaryChanged(item, moveToGameStateOnDetach: true, doInterpolate: true);
            itemSystem.PutPhysicsEntityExtensionToSleep(item);
        }

        public void PickUpByLocalPlayer(ItemExtension item, bool isPrimary, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemPickingUpLogic  PickUpByLocalPlayer - isPrimary: {isPrimary}, doInterpolate: {doInterpolate}");
#endif
            // NOTE: Unfortunately this will only result in proper offsets if the player is in the same avatar,
            // or one with the same bone rotations, which let's be honest is unlikely.

            HumanBodyBones bone = isPrimary ? item.primaryBone : item.secondaryBone;
            VRCPlayerApi.TrackingDataType trackingType = ItemSystemUtil.BoneToTrackingType(bone);
            // May cause it to begin or stop tracking, bones can begin and stop existing because of avatar changes.
            if (isPrimary)
                itemLogic.SetIsPrimaryTrackingDataSyncActive(item, trackingType, !item.primaryBoneExists);
            else
                itemLogic.SetIsSecondaryTrackingDataSyncActive(item, trackingType, !item.secondaryBoneExists);

            CustomPickup pickup = item.pickup;

            itemSystem.CancelManualInterpolation(item); // Would interfere with the pickup system.
            itemSystem.DropAndDetachPickupRemotely(pickup);

            item.pickupIsAttached = false;

            if (isPrimary)
            {
                item.pickupIsHeldByPrimaryHand = true;
                Vector3 offsetVector = item.primaryOffsetVector;
                Quaternion offsetRotation = item.primaryOffsetRotation;
                // In theory the isInOnPickupStateChanged check is redundant, however there is no reason to risk it.
                if (!item.isInOnPickupStateChanged
                    && (!pickup.isHeldByPrimaryHand
                        || !pickup.controllingPlayerIsLocal
                        || pickup.primaryHeldTrackingType != trackingType
                        || pickup.primaryOffsetVector != offsetVector
                        || pickup.primaryOffsetRotation != offsetRotation))
                {
                    pickup.ForceBeingPickedUpExplicit(trackingType, shouldBecomeSecondaryHand: false, offsetVector, offsetRotation);
                }
            }
            else
            {
                item.pickupIsHeldBySecondaryHand = true;
                Vector3 offsetVector = item.secondaryOffsetVector;
                Quaternion offsetRotation = item.secondaryOffsetRotation;
                // In theory the isInOnPickupStateChanged check is redundant, however there is no reason to risk it.
                if (!item.isInOnPickupStateChanged
                    && (!pickup.isHeldBySecondaryHand
                        || !pickup.controllingPlayerIsLocal
                        || pickup.secondaryHeldTrackingType != trackingType
                        || pickup.secondaryOffsetVector != offsetVector
                        || pickup.secondaryOffsetRotation != offsetRotation))
                {
                    pickup.ForceBeingPickedUpExplicit(trackingType, shouldBecomeSecondaryHand: true, offsetVector, offsetRotation);
                }
            }

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }

        public void PickUpByRemotePlayer(ItemExtension item, bool isPrimary, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemPickingUpLogic  PickUpByRemotePlayer - isPrimary: {isPrimary}, doInterpolate: {doInterpolate}");
#endif
            VRCPlayerApi attachedToPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            if (!Utilities.IsValid(attachedToPlayer))
                return;

            CustomPickup pickup = item.pickup;
            bool wasHeldOrAttachedLocally = pickup.state != CustomPickupControlState.None && pickup.controllingPlayerIsLocal;
            if (wasHeldOrAttachedLocally)
            {
                pickup.BeginStateModification();
                itemSystem.DropAndDetachPickupLocally(pickup);
            }

            if (pickup.state == CustomPickupControlState.Attached)
                pickup.SetControlState(CustomPickupControlState.None);
            else if (pickup.state == CustomPickupControlState.Held && pickup.controllingPlayer != attachedToPlayer)
            {
                pickup.SetControlState(CustomPickupControlState.None);
                pickup.isHeldBySecondaryHand = false;
                pickup.isHeldByPrimaryHand = false;
            }

            if (pickup.state == CustomPickupControlState.None)
            {
                pickup.SetControllingPlayer(attachedToPlayer);
                pickup.SetControlState(CustomPickupControlState.Held);
            }

            if (isPrimary)
            {
                pickup.isHeldByPrimaryHand = true;
                pickup.primaryHeldTrackingType = ItemSystemUtil.BoneToTrackingType(item.primaryBone);
                pickup.primaryOffsetVector = item.primaryOffsetVector;
                pickup.primaryOffsetRotation = item.primaryOffsetRotation;
            }
            else
            {
                pickup.isHeldBySecondaryHand = true;
                pickup.secondaryHeldTrackingType = ItemSystemUtil.BoneToTrackingType(item.secondaryBone);
                pickup.secondaryOffsetVector = item.secondaryOffsetVector;
                pickup.secondaryOffsetRotation = item.secondaryOffsetRotation;
            }

            itemSystem.CancelManualInterpolation(item);
            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();

            if (wasHeldOrAttachedLocally)
                pickup.FinishStateModification();
        }
    }
}
