using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

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
        [HideInInspector][SerializeField][SingletonReference] public ItemTransformController transformController;
        [HideInInspector][SerializeField][SingletonReference] private CustomInteractablesManagerAPI interactables;
        [HideInInspector][SerializeField][SingletonReference] private BoneAttachmentManager boneAttachment;
        [HideInInspector][SerializeField][SingletonReference] public InterpolationManager interpolation;

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
            SendChangeOffsetIA(item.data);
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

        public void AttachToLocalPlayer(ItemExtension item)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToLocalPlayer");
#endif
            // NOTE: Unfortunately this will only result in proper offsets if the player is in the same avatar,
            // or one with the same bone rotations, which let's be honest is unlikely.
            VRCPlayerApi.TrackingDataType trackingType = BoneToTrackingType(item.attachedToBone);
            BoneOffsetsToTrackingDataOffsets(
                trackingType, item.attachedToBone,
                item.attachedOffsetVector, item.attachedOffsetRotation,
                out Vector3 offsetVector, out Quaternion offsetRotation);
            CustomPickup pickup = item.pickup;
            if (!pickup.isHeld) // Only raises the OnPickup event if it is not already held.
                item.ignoreNextPickupEvent = true;
            pickup.ForceBeingPickedUp(trackingType, offsetVector, offsetRotation);
        }

        public void DetachFromLocalPlayer(ItemExtension item)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromLocalPlayer");
#endif
            if (!item.pickup.isHeld)
                return;
            item.ignoreNextDropEvent = true;
            item.pickup.Drop();
        }

        public void AttachToRemotePlayer(ItemExtension item)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToRemotePlayer");
#endif
            VRCPlayerApi holdingPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            Transform entityTransform = item.entity.transform;
            boneAttachment.AttachToBone(holdingPlayer, item.attachedToBone, entityTransform);
            interpolation.InterpolateLocalPosition(entityTransform, item.attachedOffsetVector, Entity.TransformChangeInterpolationDuration);
            interpolation.InterpolateLocalRotation(entityTransform, item.attachedOffsetRotation, Entity.TransformChangeInterpolationDuration);
        }

        public void DetachFromRemotePlayer(ItemExtension item)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromRemotePlayer");
#endif
            boneAttachment.DetachFromBone(
                (int)item.attachedToPlayerId,
                item.attachedToBone,
                item.entity.transform);
        }

        private void WriteOffsets(CustomPickup pickup, out Vector3 offsetVector, out Quaternion offsetRotation)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  WriteOffsetsRelativeToBone");
#endif
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            TrackingDataOffsetsToBoneOffsets(
                pickup.heldTrackingType, bone,
                pickup.heldOffsetVector, pickup.heldOffsetRotation,
                out offsetVector, out offsetRotation);
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

        public void SendPickupIA(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendPickupIA");
#endif
            CustomPickup pickup = itemData.ext.pickup;
            if (!pickup.isHeld)
            {
                Debug.LogError("[ItemSystem] Attempt to SendPickupIA for an item which is not held by the local player.");
                return;
            }
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);

            entitySystem.WriteEntityExtensionDataRef(itemData);
            itemData.entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteFlags(boneExists);
            lockstep.WriteSmallInt((int)bone);
            Vector3 offsetVector = Vector3.zero;
            Quaternion offsetRotation = Quaternion.identity;
            if (boneExists)
                WriteOffsets(pickup, out offsetVector, out offsetRotation);
            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(onPickupIAId)))
                return; // TODO: Either drop the item, or send a pickup IA once lockstep is initialized.

            // Latency hiding.
            itemData.ext.AttachToPlayer(localPlayerId, bone, boneExists, offsetVector, offsetRotation);
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
                itemData.entityData.MarkLatencyHiddenUniqueIdAsProcessed();
                return;
            }

            itemData.entityData.ReadPotentiallyUnknownTransformValues();
            itemData.heldItemIndex = heldItemsCount;
            ArrList.Add(ref heldItems, ref heldItemsCount, itemData);
            itemData.attachedToPlayerId = lockstep.SendingPlayerId;
            lockstep.ReadFlags(out itemData.attachedBoneExists);
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            if (itemData.attachedBoneExists)
            {
                ReadOffsets(itemData);
                itemData.entityData.TakeControlOfTransformSync(transformController);
            }

            if (!itemData.entityData.ShouldApplyReceivedIAToLatencyState() || itemData.ext == null)
                return;

            itemData.ext.AttachToPlayerUsingItemData();
        }

        private void SendChangeOffsetIA(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendChangeOffsetIA");
#endif
            ItemExtension item = itemData.ext;
            if (item == null)
            {
                Debug.LogError($"[ItemSystem] Attempt to use SendChangeOffsetIA on an ItemExtensionData "
                    + $"which currently does not have an ItemExtension associated with it. "
                    + $"It cannot determine without that.");
                return;
            }
            entitySystem.WriteEntityExtensionDataRef(itemData);
            Entity entity = item.entity;
            Transform entityTransform = entity.transform;
            CustomPickup pickup = item.pickup;
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);
            lockstep.WriteFlags(boneExists);
            Vector3 offsetVector = Vector3.zero;
            Quaternion offsetRotation = Quaternion.identity;
            if (boneExists)
                WriteOffsets(pickup, out offsetVector, out offsetRotation);
            else
            {
                lockstep.WriteVector3(entityTransform.position);
                lockstep.WriteQuaternion(entityTransform.rotation);
            }

            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(changeOffsetIAId)))
                return;

            // Latency hiding.
            item.SetAttachedBoneExists(boneExists, offsetVector, offsetRotation);
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
            EntityData entityData = itemData.entityData;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId)
            {
                entityData.MarkLatencyHiddenUniqueIdAsProcessed();
                return; // If attached id is 0u this'll also return, which works out nicely.
            }

            lockstep.ReadFlags(out bool boneExists);
            if (boneExists)
            {
                if (!itemData.attachedBoneExists) // Bone didn't exist, but it does now.
                    entityData.TakeControlOfTransformSync(transformController);
                itemData.attachedBoneExists = true;
                ReadOffsets(itemData);
            }
            else // Bone does not exist.
            {
                entityData.position = lockstep.ReadVector3();
                entityData.rotation = lockstep.ReadQuaternion();
                if (itemData.attachedBoneExists) // Bone did exist.
                    entityData.GiveBackControlOfTransformSync(
                        transformController,
                        entityData.position,
                        entityData.rotation,
                        entityData.scale);
                itemData.attachedBoneExists = false;
                itemData.attachedOffsetVector = Vector3.zero;
                itemData.attachedOffsetRotation = Quaternion.identity;
            }

            if (entityData.ShouldApplyReceivedIAToLatencyState() && itemData.ext != null)
                itemData.ext.SetAttachedBoneExists(
                    boneExists,
                    itemData.attachedOffsetVector,
                    itemData.attachedOffsetRotation);
        }

        public void SendDropIA(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendDropIA");
#endif
            Entity entity = itemData.entity;
            Transform entityTransform = entity.transform;
            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);
            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(onDropIAId)))
                return;

            // Latency hiding.
            itemData.ext.DetachFromPlayer();
        }

        [HideInInspector][SerializeField] private uint onDropIAId;
        [LockstepInputAction(nameof(onDropIAId))]
        public void OnDropIA()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnDropIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null)
                return;
            if (lockstep.SendingPlayerId != itemData.attachedToPlayerId)
            {
                itemData.entityData.MarkLatencyHiddenUniqueIdAsProcessed();
                return; // If attached id is 0u this'll also return, which works out nicely.
            }
            Drop(itemData, readPositionAndRotation: true, wasLatencyHidden: true);
        }

        private void SendForceDropSingletonIA(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendForceDropSingletonIA");
#endif
            entitySystem.WriteEntityExtensionDataRef(itemData);
            Entity entity = itemData.entity;
            bool didWritePositionAndRotation = entity != null;
            lockstep.WriteFlags(didWritePositionAndRotation);
            if (didWritePositionAndRotation)
            {
                Transform entityTransform = entity.transform;
                lockstep.WriteVector3(entityTransform.position);
                lockstep.WriteQuaternion(entityTransform.rotation);
            }
            lockstep.SendSingletonInputAction(onForceDropIAId); // Not latency hidden.
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
            lockstep.ReadFlags(out bool didWritePositionAndRotation);
            Drop(itemData, didWritePositionAndRotation, wasLatencyHidden: false);
        }

        private void Drop(ItemExtensionData itemData, bool readPositionAndRotation, bool wasLatencyHidden)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  Drop");
#endif
            EntityData entityData = itemData.entityData;
            if (readPositionAndRotation)
            {
                entityData.position = lockstep.ReadVector3();
                entityData.rotation = lockstep.ReadQuaternion();
            }
            UpdateDroppedItem(itemData);

            if ((wasLatencyHidden && !entityData.ShouldApplyReceivedIAToLatencyState()) || itemData.ext == null)
                return;

            itemData.ext.DetachFromPlayer(interpolateToGameState: true);
        }

        public void UpdateDroppedItem(ItemExtensionData itemData)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemSystem  UpdateDroppedItem");
#endif
            EntityData entityData = itemData.entityData;
            entityData.GiveBackControlOfTransformSync(
                transformController,
                entityData.position,
                entityData.rotation,
                entityData.scale);
            RemoveFromHeldItems(itemData);

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
                EntityData entityData = entitySystem.GetEntityData(id);
                ItemExtensionData itemData = entityData.GetExtensionData<ItemExtensionData>(nameof(ItemExtensionData));
                heldItems[i] = itemData;
                itemData.heldItemIndex = i;
            }
            return null;
        }
    }
}
