using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")]
    public class ItemSystem : UdonSharpBehaviour
    {
        [HideInInspector] [SerializeField] [SingletonReference] private LockstepAPI lockstep;
        [HideInInspector] [SerializeField] [SingletonReference] private EntitySystem entitySystem;
        [HideInInspector] [SerializeField] [SingletonReference] private CustomInteractablesManagerAPI interactables;
        [HideInInspector] [SerializeField] [SingletonReference] private BoneAttachmentManager boneAttachment;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private bool isInVR;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            isInVR = localPlayer.IsUserInVR();
        }

        public override void OnAvatarChanged(VRCPlayerApi player)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnAvatarChanged");
            #endif
            if (!player.isLocal)
                return;
            // The OnAvatarChanged appears to get raised once the avatar has finished loading. However doing
            // the below instantly results in garbage. 0.25 seconds seems reliable enough that the player
            // hopefully has not pressed calibrate after loading the avatar yet, because that'd move the bone
            // away from the tracking data such that it would once again result in garbage.
            SendCustomEventDelayedSeconds(nameof(OnLocalPlayerAvatarChangedDelayed), 0.1f);
        }

        public void OnLocalPlayerAvatarChangedDelayed()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnLocalPlayerAvatarChangedDelayed");
            #endif
            if (!isInVR)
                UpdateHeldItemDueToAvatarChange(interactables.HeldOnDesktop);
            else
            {
                UpdateHeldItemDueToAvatarChange(interactables.HeldInLeftHand);
                UpdateHeldItemDueToAvatarChange(interactables.HeldInRightHand);
            }
        }

        private void UpdateHeldItemDueToAvatarChange(CustomPickup pickup)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  UpdateHeldItemDueToAvatarChange");
            #endif
            if (pickup == null)
                return;
            ItemExtension item = pickup.GetComponent<ItemExtension>();
            if (item == null)
                return;
            SendChangeOffsetIA(item.Data);
        }

        private bool LocalPlayerHasBone(HumanBodyBones bone) => localPlayer.GetBonePosition(bone) != Vector3.zero;

        private void TrackingDataOffsetsToBoneOffsets(
            VRCPlayerApi.TrackingDataType trackingType,
            HumanBodyBones bone,
            Vector3 offsetVector,
            Quaternion offsetRotation,
            out Vector3 resultVector,
            out Quaternion resultRotation)
        {
            VRCPlayerApi.TrackingData trackingData = localPlayer.GetTrackingData(trackingType);
            Vector3 worldPosition = trackingData.position + trackingData.rotation * offsetVector;
            Quaternion worldRotation = trackingData.rotation * offsetRotation;
            Vector3 bonePosition = localPlayer.GetBonePosition(bone);
            Quaternion inverseBoneRotation = Quaternion.Inverse(localPlayer.GetBoneRotation(bone));
            resultVector = inverseBoneRotation * (worldPosition - bonePosition);
            resultRotation = inverseBoneRotation * worldRotation;
        }

        private void BoneOffsetsToTrackingDataOffsets(
            VRCPlayerApi.TrackingDataType trackingType,
            HumanBodyBones bone,
            Vector3 offsetVector,
            Quaternion offsetRotation,
            out Vector3 resultVector,
            out Quaternion resultRotation)
        {
            Vector3 bonePosition = localPlayer.GetBonePosition(bone);
            Quaternion boneRotation = localPlayer.GetBoneRotation(bone);
            Vector3 worldPosition = bonePosition + boneRotation * offsetVector;
            Quaternion worldRotation = boneRotation * offsetRotation;
            VRCPlayerApi.TrackingData trackingData = localPlayer.GetTrackingData(trackingType);
            Quaternion inverseTrackingDataRotation = Quaternion.Inverse(trackingData.rotation);
            resultVector = inverseTrackingDataRotation * (worldPosition - trackingData.position);
            resultRotation = inverseTrackingDataRotation * worldRotation;
        }

        private HumanBodyBones TrackingTypeToBone(VRCPlayerApi.TrackingDataType trackingType)
        {
            return trackingType == VRCPlayerApi.TrackingDataType.LeftHand ? HumanBodyBones.LeftHand
                : trackingType == VRCPlayerApi.TrackingDataType.RightHand ? HumanBodyBones.RightHand
                : HumanBodyBones.Head;
        }

        private VRCPlayerApi.TrackingDataType BoneToTrackingType(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftHand ? VRCPlayerApi.TrackingDataType.LeftHand
                : bone == HumanBodyBones.RightHand ? VRCPlayerApi.TrackingDataType.RightHand
                : VRCPlayerApi.TrackingDataType.Head;
        }

        public void AttachToLocalPlayer(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToLocalPlayer");
            #endif
            // NOTE: Unfortunately this will only result in proper offsets if the player is in the same avatar,
            // or one with the same bone rotations, which let's be honest is unlikely.
            VRCPlayerApi.TrackingDataType trackingType = BoneToTrackingType(itemData.attachedToBone);
            BoneOffsetsToTrackingDataOffsets(
                trackingType, itemData.attachedToBone,
                itemData.attachedOffsetVector, itemData.attachedOffsetRotation,
                out Vector3 offsetVector, out Quaternion offsetRotation);
            itemData.Extension.pickup.ForceBeingPickedUp(trackingType, offsetVector, offsetRotation);
        }

        public void AttachToRemotePlayer(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToRemotePlayer");
            #endif
            VRCPlayerApi holdingPlayer = VRCPlayerApi.GetPlayerById((int)itemData.attachedToPlayerId);
            if (holdingPlayer == null)
                return;
            Transform entityTransform = itemData.entityData.entity.transform;
            boneAttachment.AttachToBone(holdingPlayer, itemData.attachedToBone, entityTransform);
            entityTransform.localPosition = itemData.attachedOffsetVector;
            entityTransform.localRotation = itemData.attachedOffsetRotation;
        }

        private void DetachFromRemotePlayer(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromRemotePlayer");
            #endif
            boneAttachment.DetachFromBone(
                (int)itemData.attachedToPlayerId,
                itemData.attachedToBone,
                itemData.entityData.entity.transform);
        }

        private void WriteOffsets(CustomPickup pickup)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  WriteOffsetsRelativeToBone");
            #endif
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            TrackingDataOffsetsToBoneOffsets(
                pickup.heldTrackingType, bone,
                pickup.heldOffsetVector, pickup.heldOffsetRotation,
                out Vector3 offsetVector, out Quaternion offsetRotation);
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
        }

        private void ReadOffsets(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  ReadOffsets");
            #endif
            itemData.attachedOffsetVector = lockstep.ReadVector3();
            itemData.attachedOffsetRotation = lockstep.ReadQuaternion();
        }

        public void OnLocalPlayerPickup(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnLocalPlayerPickup");
            #endif
            CustomPickup pickup = itemData.Extension.pickup;
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);
            SendPickupIA(itemData, pickup, bone, boneExists);
            if (!boneExists)
                itemData.Extension.ContinuouslyFlagForMovement = true;
        }

        private void SendPickupIA(ItemExtensionData itemData, CustomPickup pickup, HumanBodyBones bone, bool boneExists)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendPickupIA");
            #endif
            entitySystem.WriteEntityExtensionReference(itemData.Extension);
            lockstep.WriteFlags(boneExists);
            lockstep.WriteSmallInt((int)bone);
            if (boneExists)
                WriteOffsets(pickup);
            lockstep.SendInputAction(onPickupIAId);
        }

        [HideInInspector] [SerializeField] private uint onPickupIAId;
        [LockstepInputAction(nameof(onPickupIAId))]
        public void OnPickupIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnPickupIA");
            #endif
            // TODO: add a way to get an extension of a specific type from the list of extension on an entity.
            // Using that here would remove the need to sync the extension index, we'd just need the entity id.
            ItemExtension item = entitySystem.ReadEntityExtensionReference<ItemExtension>();
            if (item == null)
                return;
            ItemExtensionData itemData = item.Data;
            if (itemData.attachedToPlayerId != 0u)
            {
                if (lockstep.SendingPlayerId == itemData.attachedToPlayerId)
                    Debug.LogError($"[ItemSystem] Impossible, got 2 PickupIAs from the same player on the same "
                        + $"entity without a DropIA in between.");
                // If the above is false, then 2 different players attempted to pick up the same item at the
                // same time, ignore the second one - so this current IA.
                return;
            }
            itemData.attachedToPlayerId = lockstep.SendingPlayerId;
            if (itemData.attachedToPlayerId != localPlayerId)
            {
                CustomPickup pickup = itemData.Extension.pickup;
                pickup.IncrementPreventInteraction();
                pickup.Drop(); // TODO: when dropped through this it shouldn't even bother sending a drop IA
            }
            lockstep.ReadFlags(out itemData.attachedBoneExists);
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            if (!itemData.attachedBoneExists)
                return;
            ReadOffsets(itemData);
            itemData.entityData.NoPositionSync = true;
            itemData.entityData.NoRotationSync = true;
            if (itemData.attachedToPlayerId != localPlayerId)
                AttachToRemotePlayer(itemData);
        }

        private void SendChangeOffsetIA(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendChangeOffsetIA");
            #endif
            entitySystem.WriteEntityExtensionReference(itemData.Extension);
            CustomPickup pickup = itemData.Extension.pickup;
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);
            lockstep.WriteFlags(boneExists);
            if (boneExists)
                WriteOffsets(pickup);
            lockstep.SendInputAction(changeOffsetIAId);
            if (!boneExists)
                itemData.Extension.ContinuouslyFlagForMovement = true;
        }

        [HideInInspector] [SerializeField] private uint changeOffsetIAId;
        [LockstepInputAction(nameof(changeOffsetIAId))]
        public void OnChangeOffsetIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnChangeOffsetIA");
            #endif
            ItemExtension item = entitySystem.ReadEntityExtensionReference<ItemExtension>();
            if (item == null)
                return;
            ItemExtensionData itemData = item.Data;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId)
                return; // If attached id is 0u this'll also return, which works out nicely.

            lockstep.ReadFlags(out bool boneExists);
            itemData.entityData.NoPositionSync = boneExists;
            itemData.entityData.NoRotationSync = boneExists;

            if (!boneExists) // Bone does not exist.
            {
                if (itemData.attachedBoneExists && itemData.attachedToPlayerId != localPlayerId)
                    DetachFromRemotePlayer(itemData); // Bone did exist, but does no longer.
                itemData.attachedBoneExists = false;
                itemData.attachedToBone = HumanBodyBones.Head;
                itemData.attachedOffsetVector = Vector3.zero;
                itemData.attachedOffsetRotation = Quaternion.identity;
                return;
            }

            ReadOffsets(itemData);

            // Bone didn't exist, but now it does.
            if (!itemData.attachedBoneExists)
            {
                itemData.attachedBoneExists = true;
                if (itemData.attachedToPlayerId != localPlayerId)
                    AttachToRemotePlayer(itemData);
                return;
            }

            // Bone did exist, still exists, update offsets.
            Transform entityTransform = item.entity.transform;
            entityTransform.localPosition = itemData.attachedOffsetVector;
            entityTransform.localRotation = itemData.attachedOffsetRotation;
        }

        public void SendDropIA(ItemExtensionData itemData)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendDropIA");
            #endif
            Transform entityTransform = itemData.entityData.entity.transform;
            entitySystem.WriteEntityExtensionReference(itemData.Extension);
            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);
            lockstep.SendInputAction(onDropIAId);
        }

        [HideInInspector] [SerializeField] private uint onDropIAId;
        [LockstepInputAction(nameof(onDropIAId))]
        public void OnDropIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnDropIA");
            #endif
            ItemExtension item = entitySystem.ReadEntityExtensionReference<ItemExtension>();
            if (item == null)
                return;
            ItemExtensionData itemData = item.Data;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId)
                return; // If attached id is 0u this'll also return, which works out nicely.
            Vector3 position = lockstep.ReadVector3();
            Quaternion rotation = lockstep.ReadQuaternion();
            EntityData entityData = itemData.entityData;
            entityData.position = position;
            entityData.rotation = rotation;
            entityData.entity.transform.SetPositionAndRotation(position, rotation);
            entityData.NoPositionSync = false;
            entityData.NoRotationSync = false;
            if (itemData.attachedToPlayerId != localPlayerId)
            {
                itemData.Extension.pickup.DecrementPreventInteraction();
                if (itemData.attachedBoneExists)
                    DetachFromRemotePlayer(itemData);
            }
            itemData.attachedToPlayerId = 0u;
            itemData.attachedBoneExists = false;
            itemData.attachedToBone = HumanBodyBones.Head;
            itemData.attachedOffsetVector = Vector3.zero;
            itemData.attachedOffsetRotation = Quaternion.identity;
        }
    }
}
