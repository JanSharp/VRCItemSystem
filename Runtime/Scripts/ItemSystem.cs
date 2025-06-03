using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    [LockstepGameStateDependency(typeof(EntitySystem))]
    public class ItemSystem : LockstepGameState
    {
        public override string GameStateInternalName => "jansharp.item-system";
        public override string GameStateDisplayName => "Item System";
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;
        public override LockstepGameStateOptionsUI ExportUI => null;
        public override LockstepGameStateOptionsUI ImportUI => null;

        [HideInInspector][SerializeField][SingletonReference] private EntitySystem entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private CustomInteractablesManagerAPI interactables;
        [HideInInspector][SerializeField][SingletonReference] private BoneAttachmentManager boneAttachment;

        private ItemExtensionData[] heldItems = new ItemExtensionData[ArrList.MinCapacity];
        private int heldItemsCount = 0;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private bool isInVR;

        private void Start()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  Start");
#endif
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            isInVR = localPlayer.IsUserInVR();
        }

        [LockstepEvent(LockstepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnClientLeft");
#endif
            uint leftPlayerId = lockstep.LeftPlayerId;
            for (int i = heldItemsCount - 1; i >= 0; i--)
            {
                ItemExtensionData itemData = heldItems[i];
                if (itemData.attachedToPlayerId != leftPlayerId)
                    continue;
                SendForceDropSingletonIA(itemData);
            }
        }

        public override void OnAvatarChanged(VRCPlayerApi player)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnAvatarChanged");
#endif
            if (!player.isLocal)
                return;
            // The OnAvatarChanged appears to get raised once the avatar has finished loading. However doing
            // the below instantly results in garbage. 0.1 seconds seems reliable enough that the player
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
            if (holdingPlayer == null || itemData.extension == null)
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
            if (itemData.extension == null)
                return;
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
            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteFlags(boneExists);
            lockstep.WriteSmallInt((int)bone);
            if (boneExists)
                WriteOffsets(pickup);
            lockstep.SendInputAction(onPickupIAId);
        }

        [HideInInspector][SerializeField] private uint onPickupIAId;
        [LockstepInputAction(nameof(onPickupIAId))]
        public void OnPickupIA()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnPickupIA");
#endif
            // TODO: add a way to get an extension of a specific type from the list of extensions on an entity.
            // Using that here would remove the need to sync the extension index, we'd just need the entity id.
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null)
                return;
            if (itemData.attachedToPlayerId != 0u)
            {
                if (lockstep.SendingPlayerId == itemData.attachedToPlayerId)
                    Debug.LogError($"[ItemSystem] Impossible, got 2 PickupIAs from the same player on the same "
                        + $"entity without a DropIA in between.");
                // If the above is false, then 2 different players attempted to pick up the same item at the
                // same time, ignore the second one - so this current IA.
                return;
            }
            itemData.heldItemIndex = heldItemsCount;
            ArrList.Add(ref heldItems, ref heldItemsCount, itemData);
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
            ItemExtension item = itemData.Extension;
            if (item == null)
            {
                Debug.LogError($"[ItemSystem] Attempt to use SendChangeOffsetIA on an ItemExtensionData "
                    + $"which currently does not have an ItemExtension associated with it. "
                    + $"It cannot determine without that.");
                return;
            }
            entitySystem.WriteEntityExtensionDataRef(itemData);
            CustomPickup pickup = item.pickup;
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);
            lockstep.WriteFlags(boneExists);
            if (boneExists)
                WriteOffsets(pickup);
            lockstep.SendInputAction(changeOffsetIAId);
            item.ContinuouslyFlagForMovement = !boneExists;
        }

        [HideInInspector][SerializeField] private uint changeOffsetIAId;
        [LockstepInputAction(nameof(changeOffsetIAId))]
        public void OnChangeOffsetIA()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnChangeOffsetIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null)
                return;
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

            if (itemData.extension == null)
                return;
            // Bone did exist, still exists, update offsets.
            Transform entityTransform = itemData.entityData.entity.transform;
            entityTransform.localPosition = itemData.attachedOffsetVector;
            entityTransform.localRotation = itemData.attachedOffsetRotation;
        }

        public void SendDropIA(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendDropIA");
#endif
            Transform entityTransform = itemData.entityData.entity.transform;
            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);
            lockstep.SendInputAction(onDropIAId);
        }

        [HideInInspector][SerializeField] private uint onDropIAId;
        [LockstepInputAction(nameof(onDropIAId))]
        public void OnDropIA()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnDropIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null || lockstep.SendingPlayerId != itemData.attachedToPlayerId)
                return; // If attached id is 0u this'll also return, which works out nicely.
            Drop(itemData);
        }

        private void SendForceDropSingletonIA(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendForceDropSingletonIA");
#endif
            entitySystem.WriteEntityExtensionDataRef(itemData);
            Entity entity = itemData.entityData.entity;
            if (entity != null)
            {
                Transform entityTransform = entity.transform;
                lockstep.WriteVector3(entityTransform.position);
                lockstep.WriteQuaternion(entityTransform.rotation);
            }
            else
            {
                // TODO: implement some kind of system in order to have better fallback values here.
                // The most likely solution is for held items to periodically sync their current position and
                // rotation. But with like a minute in between syncs, or something.
                lockstep.WriteVector3(Vector3.zero);
                lockstep.WriteQuaternion(Quaternion.identity);
            }
            lockstep.SendSingletonInputAction(onForceDropIAId);
        }

        [HideInInspector][SerializeField] private uint onForceDropIAId;
        [LockstepInputAction(nameof(onForceDropIAId))]
        public void OnForceDrop()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnForceDrop");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null || itemData.attachedToPlayerId == 0u) // Already detached.
                return;
            Drop(itemData);
        }

        private void Drop(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  Drop");
#endif
            Vector3 position = lockstep.ReadVector3();
            Quaternion rotation = lockstep.ReadQuaternion();
            EntityData entityData = itemData.entityData;
            ItemExtension item = itemData.Extension;
            entityData.position = position;
            entityData.rotation = rotation;
            if (item != null)
                entityData.entity.transform.SetPositionAndRotation(position, rotation);
            entityData.NoPositionSync = false;
            entityData.NoRotationSync = false;
            RemoveFromHeldItems(itemData);
            if (itemData.attachedToPlayerId != localPlayerId && item != null)
            {
                item.pickup.DecrementPreventInteraction();
                if (itemData.attachedBoneExists)
                    DetachFromRemotePlayer(itemData);
            }
            itemData.attachedToPlayerId = 0u;
            itemData.attachedBoneExists = false;
            itemData.attachedToBone = HumanBodyBones.Head;
            itemData.attachedOffsetVector = Vector3.zero;
            itemData.attachedOffsetRotation = Quaternion.identity;
        }

        private void RemoveFromHeldItems(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  RemoveFromHeldItems");
#endif
            heldItemsCount--;
            int index = itemData.heldItemIndex;
            itemData.heldItemIndex = 0;
            if (index != heldItemsCount)
            {
                ItemExtensionData other = heldItems[heldItemsCount];
                heldItems[index] = other;
                other.heldItemIndex = index;
            }
            heldItems[heldItemsCount] = null; // Make GC happy.
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SerializeGameState");
#endif
            lockstep.WriteSmallUInt((uint)heldItemsCount);
            for (int i = 0; i < heldItemsCount; i++)
                lockstep.WriteSmallUInt(heldItems[i].entityData.id);
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  DeserializeGameState");
#endif
            heldItemsCount = (int)lockstep.ReadSmallUInt();
            ArrList.EnsureCapacity(ref heldItems, heldItemsCount);
            for (int i = 0; i < heldItemsCount; i++)
            {
                uint id = lockstep.ReadSmallUInt();
                // TODO: have a better way to get specific extension data from entities and or entity data.
                EntityData entityData = entitySystem.GetEntityData(id);
                int extensionIndex = System.Array.IndexOf(entityData.entityPrototype.ExtensionDataClassNames, nameof(ItemExtensionData));
                ItemExtensionData itemData = (ItemExtensionData)entityData.allExtensionData[extensionIndex];
                heldItems[i] = itemData;
                itemData.heldItemIndex = i;
            }
            return null;
        }
    }
}
