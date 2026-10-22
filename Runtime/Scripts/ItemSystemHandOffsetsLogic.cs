using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemHandOffsetsLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
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

        private bool CanSendHandOffsetsChangedIA(ItemExtensionData itemData, bool isPrimary)
        {
            ItemExtension item = itemData.ext;
            if (item == null || !lockstep.IsInitialized)
                return false;
            CustomPickup pickup = item.pickup;
            if ((isPrimary ? !pickup.isHeldByPrimaryHand : !pickup.isHeldBySecondaryHand) || !pickup.controllingPlayerIsLocal)
            {
                Debug.LogError($"[ItemSystem] Attempt to {nameof(SendHandOffsetsChangedIA)} "
                    + $"for an item which is not held by the local player.");
                return false;
            }
            return true;
        }

        public void SendHandOffsetsChangedIA(ItemExtensionData itemData, bool isPrimary)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemOffsetsLogic  SendHandOffsetsChangedIA - isPrimary: {isPrimary}");
#endif
            if (!CanSendHandOffsetsChangedIA(itemData, isPrimary))
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
            // This comes either before or after sending the offsets changed IA,
            // depending on if tracking data sync may start or stop tracking.
            if (!boneExists)
                if (isPrimary)
                    PrimaryOffsetsChangedIALatency(item, bone, boneExists);
                else
                    SecondaryOffsetsChangedIALatency(item, bone, boneExists);

            // Send IA.
            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteFlags(isPrimary, boneExists);
            lockstep.WriteSmallInt((int)bone); // Also having the bone be part of this IA, it's so cheap to add.
            WriteOffsets();
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(handOffsetsChangedIAId));

            // Latency hiding.
            // This comes either before or after sending the offsets changed IA,
            // depending on if tracking data sync may start or stop tracking.
            if (boneExists)
                if (isPrimary)
                    PrimaryOffsetsChangedIALatency(item, bone, boneExists);
                else
                    SecondaryOffsetsChangedIALatency(item, bone, boneExists);
        }

        private bool DoApplyHandOffsetsChangedIA(ItemExtensionData itemData, bool isPrimary)
        {
            if (itemData == null)
                return false;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId
                || (isPrimary ? !itemData.isHeldByPrimaryHand : !itemData.isHeldBySecondaryHand))
            {
                itemData.entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return false;
            }
            return true;
        }

        [HideInInspector][SerializeField] private uint handOffsetsChangedIAId;
        [LockstepInputAction(nameof(handOffsetsChangedIAId))]
        public void OnHandOffsetsChangedIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemOffsetsLogic  OnHandOffsetsChangedIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            lockstep.ReadFlags(out bool isPrimary, out bool boneExists);
            if (!DoApplyHandOffsetsChangedIA(itemData, isPrimary))
                return;
            EntityData entityData = itemData.entityData;

            // Read the rest of IA data.
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

            // Generic interaction stuff.
            entityData.LastUserPlayerData = entitySystem.GetPlayerData(playerDataManager.SendingPlayerData);

            // Latency State.
            ItemExtension item = itemData.ext;
            if (entityData.ShouldApplyReceivedIAToLatencyState() && item != null)
                if (isPrimary)
                    PrimaryOffsetsChangedIALatency(item, itemData.primaryBone, itemData.primaryBoneExists);
                else
                    SecondaryOffsetsChangedIALatency(item, itemData.secondaryBone, itemData.secondaryBoneExists);
        }

        private void PrimaryOffsetsChangedIALatency(ItemExtension item, HumanBodyBones bone, bool boneExists)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemOffsetsLogic  PrimaryOffsetsChangedIALatency - boneExists: {boneExists}");
#endif
            itemLogic.SetPrimaryBone(item, bone);
            item.primaryBoneExists = boneExists;
            item.primaryAnchorVector = anchorVector;
            item.primaryAnchorRotation = anchorRotation;
            item.primaryOffsetVector = offsetVector;
            item.primaryOffsetRotation = offsetRotation;

            itemLogic.OnHandOffsetsChanged(item, isPrimary: true, doInterpolate: true);
        }

        private void SecondaryOffsetsChangedIALatency(ItemExtension item, HumanBodyBones bone, bool boneExists)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemOffsetsLogic  SecondaryOffsetsChangedIALatency - boneExists: {boneExists}");
#endif
            itemLogic.SetSecondaryBone(item, bone);
            item.secondaryBoneExists = boneExists;
            item.secondaryAnchorVector = anchorVector;
            item.secondaryAnchorRotation = anchorRotation;
            item.secondaryOffsetVector = offsetVector;
            item.secondaryOffsetRotation = offsetRotation;

            itemLogic.OnHandOffsetsChanged(item, isPrimary: false, doInterpolate: true);
        }

        public void ChangeLocalPlayerHandOffsets(ItemExtension item, bool isPrimary, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemOffsetsLogic  ChangeLocalPlayerHandOffsets - isPrimary: {isPrimary}, doInterpolate: {doInterpolate}");
#endif
            HumanBodyBones bone = isPrimary ? item.primaryBone : item.secondaryBone;
            VRCPlayerApi.TrackingDataType trackingType = ItemSystemUtil.BoneToTrackingType(bone);
            // May cause it to begin or stop tracking, bones can begin and stop existing because of avatar changes.
            if (isPrimary)
                itemLogic.SetIsPrimaryTrackingDataSyncActive(item, trackingType, !item.primaryBoneExists);
            else
                itemLogic.SetIsSecondaryTrackingDataSyncActive(item, trackingType, !item.secondaryBoneExists);

            CustomPickup pickup = item.pickup;

            if ((isPrimary ? !pickup.isHeldByPrimaryHand : !pickup.isHeldBySecondaryHand)
                || pickup.controllingPlayerId != localPlayerId)
            {
                Debug.LogError($"[ItemSystem] Impossible, attempt to {nameof(ChangeLocalPlayerHandOffsets)} "
                    + $"while the local player is not holding the item.", item);
                return;
            }

            if (isPrimary)
            {
                pickup.primaryHeldTrackingType = trackingType;
                pickup.primaryOffsetVector = item.primaryOffsetVector;
                pickup.primaryOffsetRotation = item.primaryOffsetRotation;
            }
            else
            {
                pickup.secondaryHeldTrackingType = trackingType;
                pickup.secondaryOffsetVector = item.secondaryOffsetVector;
                pickup.secondaryOffsetRotation = item.secondaryOffsetRotation;
            }

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }

        public void ChangeRemotePlayerHandOffsets(ItemExtension item, bool isPrimary, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemOffsetsLogic  ChangeRemotePlayerHandOffsets - isPrimary: {isPrimary}, doInterpolate: {doInterpolate}");
#endif
            VRCPlayerApi attachedToPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            if (!Utilities.IsValid(attachedToPlayer))
                return;

            CustomPickup pickup = item.pickup;

            if (isPrimary ? !pickup.isHeldByPrimaryHand : !pickup.isHeldBySecondaryHand
                || pickup.controllingPlayerId != item.attachedToPlayerId)
            {
                Debug.LogError($"[ItemSystem] Impossible, attempt to {nameof(ChangeRemotePlayerHandOffsets)} "
                    + $"while the item is not held by the player the item thinks it should be.", item);
                return;
            }

            if (isPrimary)
            {
                pickup.primaryHeldTrackingType = ItemSystemUtil.BoneToTrackingType(item.primaryBone);
                pickup.primaryOffsetVector = item.primaryOffsetVector;
                pickup.primaryOffsetRotation = item.primaryOffsetRotation;
            }
            else
            {
                pickup.secondaryHeldTrackingType = ItemSystemUtil.BoneToTrackingType(item.secondaryBone);
                pickup.secondaryOffsetVector = item.secondaryOffsetVector;
                pickup.secondaryOffsetRotation = item.secondaryOffsetRotation;
            }

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }
    }
}
