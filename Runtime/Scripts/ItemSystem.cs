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
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;
        [HideInInspector][SerializeField][SingletonReference] private CustomInteractablesManagerAPI interactables;
        [HideInInspector][SerializeField][SingletonReference] private InterpolationManager interpolation;
        [HideInInspector][SerializeField][SingletonReference] private PlayerTrackingDataSyncManagerAPI trackingDataSync;

        public CustomPickupState stateForPickupController;
        public CustomPickupAttachedState stateForAttachedPickupController;

        private ItemExtensionData[] attachedItems = new ItemExtensionData[ArrList.MinCapacity];
        private int attachedItemsCount = 0;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private bool isInVR;

        public const float VelocityThreshold = 0.3f;
        public const float AngularVelocityDegreesThreshold = 30f;

        private void Start()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  Start");
#endif
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            isInVR = localPlayer.IsUserInVR();
        }

        [LockstepEvent(LockstepEventType.OnClientLeft)]
        public void OnClientLeft()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnClientLeft");
#endif
            uint leftPlayerId = lockstep.LeftPlayerId;
            for (int i = attachedItemsCount - 1; i >= 0; i--)
            {
                ItemExtensionData itemData = attachedItems[i];
                if (itemData.attachedToPlayerId != leftPlayerId)
                    continue;
                SendForceDropSingletonIA(itemData);
            }
        }

        public override void OnAvatarChanged(VRCPlayerApi player)
        {
#if ITEM_SYSTEM_DEBUG
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
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnLocalPlayerAvatarChangedDelayed");
#endif
            if (!isInVR)
                UpdateHeldItemDueToAvatarChange(interactables.HeldOnDesktop);
            else
            {
                UpdateHeldItemDueToAvatarChange(interactables.HeldInLeftHand);
                UpdateHeldItemDueToAvatarChange(interactables.HeldInRightHand);
            }
            // Cannot do anything for the attached items, they might move around but so be it. We don't have a
            // tracking data reference point for those, while for held items we do.
        }

        private void UpdateHeldItemDueToAvatarChange(CustomPickup pickup)
        {
#if ITEM_SYSTEM_DEBUG
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
            // When this is false - even if the bone exists now - the offsets
            // were calculated relative to tracking data and should remain as such.
            bool boneExists,
            Vector3 offsetVector,
            Quaternion offsetRotation,
            out Vector3 resultVector,
            out Quaternion resultRotation)
        {
            Vector3 bonePosition = localPlayer.GetBonePosition(bone);
            if (!boneExists || bonePosition == Vector3.zero)
            {
                resultVector = offsetVector;
                resultRotation = offsetRotation;
                return;
            }
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

        private void CancelManualInterpolation(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  CancelManualInterpolation");
#endif
            Transform entityTransform = item.entity.transform;
            interpolation.CancelPositionInterpolation(entityTransform);
            interpolation.CancelRotationInterpolation(entityTransform);
        }

        private void MoveItemToEntityDataLocationManually(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  InterpolateToEntityDataLocationManually");
#endif
            EntityData entityData = item.entityData;
            Transform entityTransform = item.entity.transform;
            if (doInterpolate)
            {
                interpolation.LerpWorldPosition(entityTransform, entityData.position, Entity.TransformChangeInterpolationDuration);
                interpolation.LerpWorldRotation(entityTransform, entityData.rotation, Entity.TransformChangeInterpolationDuration);
            }
            else
                entityTransform.SetPositionAndRotation(entityData.position, entityData.rotation);
        }

        public void SetAttachedToBone(ItemExtension item, HumanBodyBones bone)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SetAttachedToBone");
#endif
            HumanBodyBones attachedToBone = item.attachedToBone;
            if (attachedToBone == bone)
                return;
            item.attachedToBone = bone;
            if (!item.isTrackingDataSyncActive)
                return;
            trackingDataSync.SendStopTrackingIA(BoneToTrackingType(attachedToBone));
            trackingDataSync.SendBeginTrackingIA(BoneToTrackingType(bone));
        }

        public void PickUpByLocalPlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  PickUpByLocalPlayer");
#endif
            // NOTE: Unfortunately this will only result in proper offsets if the player is in the same avatar,
            // or one with the same bone rotations, which let's be honest is unlikely.
            VRCPlayerApi.TrackingDataType trackingType = BoneToTrackingType(item.attachedToBone);
            if (!item.attachedBoneExists && !item.isTrackingDataSyncActive)
            {
                item.isTrackingDataSyncActive = true;
                trackingDataSync.SendBeginTrackingIA(trackingType);
            }

            CancelManualInterpolation(item); // Would interfere with the pickup system.

            BoneOffsetsToTrackingDataOffsets(
                trackingType, item.attachedToBone, item.attachedBoneExists,
                item.attachedOffsetVector, item.attachedOffsetRotation,
                out Vector3 offsetVector, out Quaternion offsetRotation);
            CustomPickup pickup = item.pickup;
            item.pickupIsHeld = true;
            item.pickupIsAttached = false;
            // In theory the isInOnPickupStateChanged check is redundant, however there is no reason to risk it.
            if (!item.isInOnPickupStateChanged
                && (!pickup.isHeldByPrimaryHand
                || pickup.primaryHeldTrackingType != trackingType
                || Vector3.Distance(pickup.primaryOffsetVector, offsetVector) > 0.01f
                || Quaternion.Angle(pickup.primaryOffsetRotation, offsetRotation) > 0.1f))
            {
                pickup.ForceBeingPickedUpExplicit(trackingType, shouldBecomeSecondaryHand: false, offsetVector, offsetRotation);
            }

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }

        public void AttachToLocalPlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToLocalPlayer");
#endif
            if (!item.attachedBoneExists)
            {
                Debug.LogError($"[ItemSystem] Attempt to AttachToLocalPlayer where attachedBoneExists is "
                    + $"false, this must be caught and handled at some point sooner.");
                return;
            }

            CancelManualInterpolation(item); // Would interfere with the pickup system.

            HumanBodyBones attachedToBone = item.attachedToBone;
            if (!LocalPlayerHasBone(attachedToBone)) // Handles attachment due to imports.
            {
                var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                item.entity.transform.SetPositionAndRotation(head.position + head.rotation * Vector3.forward, head.rotation);
                SendDropIA(item.data, forceNoVelocity: true); // Will run DetachFromLocalPlayer.
                return;
            }
            CustomPickup pickup = item.pickup;
            item.pickupIsHeld = false;
            item.pickupIsAttached = true;
            // In theory the isInOnPickupStateChanged check is redundant, however there is no reason to risk it.
            if (!item.isInOnPickupStateChanged && (!pickup.isAttached || pickup.attachedToBone != attachedToBone))
                pickup.ForceBeingAttached(attachedToBone);

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }

        public void DetachFromLocalPlayer(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromLocalPlayer");
#endif
            if (item.isTrackingDataSyncActive)
            {
                item.isTrackingDataSyncActive = false;
                trackingDataSync.SendStopTrackingIA(BoneToTrackingType(item.attachedToBone));
            }

            item.pickupIsHeld = false;
            item.pickupIsAttached = false;
            DropAndDetachPickupLocally(item.pickup);
        }

        private void DropAndDetachPickupLocally(CustomPickup pickup)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DropAndDetachPickupLocally");
#endif
            pickup.Drop(preventAttachment: true); // Only does anything if it is held by the local player.
            pickup.Detach(); // Only does anything if it is attached to the local player.
        }

        public void AttachToRemotePlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToRemotePlayer");
#endif
            VRCPlayerApi attachedToPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            if (!Utilities.IsValid(attachedToPlayer))
                return;

            CustomPickup pickup = item.pickup;
            DropAndDetachPickupLocally(pickup);
            pickup.SetControllingPlayer(attachedToPlayer);

            if (item.isHeldSpecifically)
            {
                pickup.SetControlState(CustomPickupControlState.Held);
                pickup.isHeldByPrimaryHand = true;
                pickup.primaryHeldTrackingType = BoneToTrackingType(item.attachedToBone);
                pickup.primaryOffsetVector = item.attachedOffsetVector;
                pickup.primaryOffsetRotation = item.attachedOffsetRotation;
                CancelManualInterpolation(item);
            }
            else
            {
                pickup.SetControlState(CustomPickupControlState.Attached);
                pickup.isHeldByPrimaryHand = false;
                pickup.attachedToBone = item.attachedToBone;
                pickup.attachedOffsetVector = item.attachedOffsetVector;
                pickup.attachedOffsetRotation = item.attachedOffsetRotation;
                if (item.attachedBoneExists)
                    CancelManualInterpolation(item);
                else
                    MoveItemToEntityDataLocationManually(item, doInterpolate);
            }

            // Thanks to this only doing anything when the pickup controller Move functions get called,
            // it is fine to run this even when manual interpolation is running. Because in that case the
            // pickup controller will not do anything because the bone does not exist.
            if (doInterpolate)
                pickup.StartInterpolation();
        }

        public void DetachFromRemotePlayer(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromRemotePlayer");
#endif
            CustomPickup pickup = item.pickup;
            pickup.SetControlState(CustomPickupControlState.None);
            pickup.isHeldByPrimaryHand = false;
        }

        private void CalculateOffsets(CustomPickup pickup, out Vector3 offsetVector, out Quaternion offsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  CalculateOffsets");
#endif
            HumanBodyBones bone = TrackingTypeToBone(pickup.primaryHeldTrackingType);
            TrackingDataOffsetsToBoneOffsets(
                pickup.primaryHeldTrackingType, bone,
                pickup.primaryOffsetVector, pickup.primaryOffsetRotation,
                out offsetVector, out offsetRotation);
        }

        private void WriteOffsets(CustomPickup pickup, out Vector3 offsetVector, out Quaternion offsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  WriteOffsets");
#endif
            CalculateOffsets(pickup, out offsetVector, out offsetRotation);
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
        }

        private void WriteOffsets(CustomPickup pickup, Vector3 offsetVector, Quaternion offsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  WriteOffsets");
#endif
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
        }

        private void ReadOffsets(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  ReadOffsets");
#endif
            itemData.attachedOffsetVector = lockstep.ReadVector3();
            itemData.attachedOffsetRotation = lockstep.ReadQuaternion();
        }

        private void PutPhysicsEntityExtensionToSleep(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  PutPhysicsEntityExtensionToSleep");
#endif
            PhysicsEntityExtension physicsEntity = item.physicsExt;
            if (physicsEntity != null && !physicsEntity.isSleeping)
            {
                Transform t = item.entity.transform;
                physicsEntity.GoToSleep(t.position, t.rotation);
            }
        }

        public void SendPickupIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendPickupIA");
#endif
            ItemExtension item = itemData.ext;
            if (item == null || !lockstep.IsInitialized)
                return; // TODO: Either drop the item, or send a pickup IA once lockstep is initialized.
            CustomPickup pickup = item.pickup;
            if (!pickup.isHeldByPrimaryHand)
            {
                Debug.LogError("[ItemSystem] Attempt to SendPickupIA for an item which is not held by the local player.");
                return;
            }

            // Setup.
            VRCPlayerApi.TrackingDataType trackingDataType = pickup.primaryHeldTrackingType;
            HumanBodyBones bone = TrackingTypeToBone(trackingDataType);
            bool boneExists = LocalPlayerHasBone(bone);
            Vector3 offsetVector;
            Quaternion offsetRotation;
            if (boneExists)
                CalculateOffsets(pickup, out offsetVector, out offsetRotation);
            else
            {
                offsetVector = pickup.primaryOffsetVector;
                offsetRotation = pickup.primaryOffsetRotation;
            }

            // Latency hiding.
            // Must happen before sending the pickup IA in order for tracking data sync IA to run first, if it's used.
            item.AttachToPlayer(isHeldSpecifically: true, localPlayerId, bone, boneExists, offsetVector, offsetRotation, doInterpolate: true);
            PutPhysicsEntityExtensionToSleep(item);

            // Sending IA.
            EntityData entityData = itemData.entityData;
            entitySystem.WriteEntityExtensionDataRef(itemData);
            entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteFlags(boneExists);
            lockstep.WriteSmallInt((int)bone);
            WriteOffsets(pickup, offsetVector, offsetRotation);
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(pickupIAId));
        }

        [HideInInspector][SerializeField] private uint pickupIAId;
        [LockstepInputAction(nameof(pickupIAId))]
        public void OnPickupIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnPickupIA");
#endif
            // TODO: add a way to get an extension of a specific type from the list of extensions on an entity.
            // Using that here would remove the need to sync the extension index, we'd just need the entity id.
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null)
                return;
            EntityData entityData = itemData.entityData;
            if (itemData.IsAttached && lockstep.SendingPlayerId != itemData.attachedToPlayerId)
            {
                // Handles two players picking up the same item simultaneously.
                entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return;
            }

            entityData.ReadPotentiallyUnknownTransformValues();
            AddToAttachedItems(itemData);
            itemData.isHeldSpecifically = true;
            itemData.attachedToPlayerId = lockstep.SendingPlayerId;
            lockstep.ReadFlags(out itemData.attachedBoneExists);
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            entityData.TakeControlOfTransformSync(transformController);
            ReadOffsets(itemData);

            // Putting the physics entity to sleep after the item has taken control theoretically
            // reduces the total amount of work that needs to be done by the entity system.
            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            if (physicsData != null)
                physicsData.GoToSleep();

            ItemExtension item = itemData.ext;
            if (!entityData.ShouldApplyReceivedIAToLatencyState() || item == null)
                return;

            item.AttachToPlayerUsingItemData(doInterpolate: true);
            PutPhysicsEntityExtensionToSleep(item);
        }

        public void SendAttachIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendAttachIA");
#endif
            ItemExtension item = itemData.ext;
            if (item == null || !lockstep.IsInitialized)
                return; // TODO: Either detach the item, or send an attach IA once lockstep is initialized.
            CustomPickup pickup = item.pickup;
            if (!pickup.isAttached)
            {
                Debug.LogError("[ItemSystem] Attempt to SendAttachIA for an item which is not attached to the local player.");
                return;
            }
            // Do not check if the attached bone exists. It always exists inside of the OnAttach event for
            // the pickup, so it can only not exist if SendAttachIA gets sent outside of an OnAttach event.
            // The item system itself does not do that and it's arguably even invalid.
            HumanBodyBones bone = pickup.attachedToBone;
            Vector3 attachedOffsetVector = pickup.attachedOffsetVector;
            Quaternion attachedOffsetRotation = pickup.attachedOffsetRotation;
            EntityData entityData = itemData.entityData;

            entitySystem.WriteEntityExtensionDataRef(itemData);
            entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteSmallInt((int)bone);
            lockstep.WriteVector3(attachedOffsetVector);
            lockstep.WriteQuaternion(attachedOffsetRotation);
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(attachIAId));

            // Latency hiding.
            item.AttachToPlayer(
                isHeldSpecifically: false,
                localPlayerId,
                bone,
                boneExists: true,
                attachedOffsetVector,
                attachedOffsetRotation,
                doInterpolate: true);
            PutPhysicsEntityExtensionToSleep(item);
        }

        [HideInInspector][SerializeField] private uint attachIAId;
        [LockstepInputAction(nameof(attachIAId))]
        public void OnAttachIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnAttachIA");
#endif
            // TODO: add a way to get an extension of a specific type from the list of extensions on an entity.
            // Using that here would remove the need to sync the extension index, we'd just need the entity id.
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null)
                return;
            EntityData entityData = itemData.entityData;
            if (itemData.IsAttached && lockstep.SendingPlayerId != itemData.attachedToPlayerId)
            {
                // Handles two players picking up the same item simultaneously.
                entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return;
            }

            entityData.ReadPotentiallyUnknownTransformValues();
            AddToAttachedItems(itemData);
            itemData.isHeldSpecifically = false;
            itemData.attachedToPlayerId = lockstep.SendingPlayerId;
            itemData.attachedBoneExists = true;
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            ReadOffsets(itemData);
            entityData.TakeControlOfTransformSync(transformController);

            // Putting the physics entity to sleep after the item has taken control theoretically
            // reduces the total amount of work that needs to be done by the entity system.
            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            if (physicsData != null)
                physicsData.GoToSleep();

            ItemExtension item = itemData.ext;
            if (!entityData.ShouldApplyReceivedIAToLatencyState() || item == null)
                return;

            item.AttachToPlayerUsingItemData(doInterpolate: true);
            PutPhysicsEntityExtensionToSleep(item);
        }

        private void SendChangeOffsetIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
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
            CustomPickup pickup = item.pickup;
            HumanBodyBones bone = TrackingTypeToBone(pickup.primaryHeldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);
            lockstep.WriteFlags(boneExists, item.isHeldSpecifically);

            Vector3 offsetVector = Vector3.zero;
            Quaternion offsetRotation = Quaternion.identity;
            if (item.isHeldSpecifically || boneExists)
                WriteOffsets(pickup, out offsetVector, out offsetRotation);
            else
            {
                Transform entityTransform = item.entity.transform;
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
#if ITEM_SYSTEM_DEBUG
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

            lockstep.ReadFlags(out bool boneExists, out bool isHeldSpecifically);
            itemData.attachedBoneExists = boneExists;

            if (isHeldSpecifically || boneExists)
                ReadOffsets(itemData);
            else // Is attached to bone and bone does not exist.
            {
                entityData.position = lockstep.ReadVector3();
                entityData.rotation = lockstep.ReadQuaternion();
                itemData.attachedOffsetVector = Vector3.zero;
                itemData.attachedOffsetRotation = Quaternion.identity;
            }

            if (entityData.ShouldApplyReceivedIAToLatencyState() && itemData.ext != null)
                itemData.ext.SetAttachedBoneExists(
                    boneExists,
                    itemData.attachedOffsetVector,
                    itemData.attachedOffsetRotation);
        }

        public void SendDropIA(ItemExtensionData itemData, bool forceNoVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendDropIA");
#endif
            Entity entity = itemData.entity;
            Transform entityTransform = entity.transform;
            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);

            ItemExtension item = itemData.ext;
            PhysicsEntityExtension physicsExt = item.physicsExt;
            bool doApplyVelocity = false;
            if (physicsExt != null)
            {
                doApplyVelocity = !forceNoVelocity
                    && (item.trackedVelocity.magnitude > VelocityThreshold
                        || item.trackedAngularVelocityAngle > AngularVelocityDegreesThreshold);
#if ITEM_SYSTEM_DEBUG
                Debug.Log($"[ItemSystemDebug] ItemSystem  SendDropIA (inner) - forceNoVelocity: {forceNoVelocity}, item.trackedVelocity: {item.trackedVelocity}, item.trackedVelocity.magnitude: {item.trackedVelocity.magnitude}, item.trackedAngularVelocityAxis: {item.trackedAngularVelocityAxis}, item.trackedAngularVelocityAngle: {item.trackedAngularVelocityAngle}, doApplyVelocity: {doApplyVelocity}");
#endif
                lockstep.WriteFlags(doApplyVelocity);
                if (doApplyVelocity)
                {
                    lockstep.WriteVector3(item.trackedVelocity);
                    lockstep.WriteVector3(item.TrackedAngularVelocity);
                }
            }

            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(dropIAId)))
                return;

            // Latency hiding.
            item.DetachFromPlayer();

            if (!doApplyVelocity)
                return;
            physicsExt.SetResponsiblePlayerId(localPlayerId);
            physicsExt.WakeUp();
            physicsExt.rb.velocity = item.trackedVelocity;
            physicsExt.rb.angularVelocity = item.TrackedAngularVelocity;
        }

        [HideInInspector][SerializeField] private uint dropIAId;
        [LockstepInputAction(nameof(dropIAId))]
        public void OnDropIA()
        {
#if ITEM_SYSTEM_DEBUG
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
            Drop(itemData, readPositionAndRotation: true, mightHaveBeenLatencyHidden: true, mightHaveVelocity: true);
        }

        private void SendForceDropSingletonIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
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
            lockstep.SendSingletonInputAction(forceDropIAId); // Not latency hidden.
        }

        [HideInInspector][SerializeField] private uint forceDropIAId;
        [LockstepInputAction(nameof(forceDropIAId))]
        public void OnForceDrop()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  OnForceDrop");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (itemData == null || !itemData.IsAttached)
                return;
            lockstep.ReadFlags(out bool didWritePositionAndRotation);
            Drop(itemData, didWritePositionAndRotation, mightHaveBeenLatencyHidden: false, mightHaveVelocity: false);
        }

        private void Drop(ItemExtensionData itemData, bool readPositionAndRotation, bool mightHaveBeenLatencyHidden, bool mightHaveVelocity)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  Drop");
#endif
            EntityData entityData = itemData.entityData;
            if (readPositionAndRotation)
            {
                entityData.position = lockstep.ReadVector3();
                entityData.rotation = lockstep.ReadQuaternion();
            }
            UpdateDroppedItem(itemData);

            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            bool doApplyVelocity = false;
            if (mightHaveVelocity && physicsData != null)
            {
                lockstep.ReadFlags(out doApplyVelocity);
                if (doApplyVelocity)
                {
                    physicsData.velocity = lockstep.ReadVector3();
                    physicsData.angularVelocity = lockstep.ReadVector3();
                    physicsData.SetResponsiblePlayerId(lockstep.SendingPlayerId);
                    physicsData.WakeUp();
                }
            }

            if ((mightHaveBeenLatencyHidden && !entityData.ShouldApplyReceivedIAToLatencyState()) || itemData.ext == null)
                return;

            itemData.ext.DetachFromPlayer(interpolateToGameState: !doApplyVelocity);

            if (!doApplyVelocity)
                return;
            PhysicsEntityExtension physicsExt = physicsData.ext;
            physicsExt.SetResponsiblePlayerId(lockstep.SendingPlayerId);
            physicsExt.WakeUp();
            physicsExt.RigidbodyUpdate(); // Applies velocity and angularVelocity after interpolation.
        }

        public void UpdateDroppedItem(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  UpdateDroppedItem");
#endif
            EntityData entityData = itemData.entityData;
            entityData.GiveBackControlOfTransformSync(
                transformController,
                entityData.position,
                entityData.rotation,
                entityData.scale);
            RemoveFromAttachedItems(itemData);

            itemData.isHeldSpecifically = false;
            itemData.attachedToPlayerId = 0u;
            itemData.attachedBoneExists = false;
            itemData.attachedToBone = HumanBodyBones.Head;
            itemData.attachedOffsetVector = Vector3.zero;
            itemData.attachedOffsetRotation = Quaternion.identity;
        }

        private void AddToAttachedItems(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  AddToAttachedItems");
#endif
            if (itemData.attachedItemIndex != ItemExtensionData.DetachedItemIndex)
                return;
            itemData.attachedItemIndex = attachedItemsCount;
            ArrList.Add(ref attachedItems, ref attachedItemsCount, itemData);
        }

        private void RemoveFromAttachedItems(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  RemoveFromAttachedItems");
            if (itemData.attachedItemIndex == ItemExtensionData.DetachedItemIndex)
            {
                Debug.LogError($"[ItemSystemDebug] Impossible, heldItemIndex is zero inside of RemoveFromAttachedItems.");
                return;
            }
#endif
            attachedItemsCount--;
            int index = itemData.attachedItemIndex;
            itemData.attachedItemIndex = ItemExtensionData.DetachedItemIndex;
            if (index != attachedItemsCount)
            {
                ItemExtensionData other = attachedItems[attachedItemsCount];
                attachedItems[index] = other;
                other.attachedItemIndex = index;
            }
            attachedItems[attachedItemsCount] = null; // Make GC happy.
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SerializeGameState");
#endif
            if (isExport && !entitySystem.ExportOptions.includeEntities)
                return;
            lockstep.WriteSmallUInt((uint)attachedItemsCount);
            for (int i = 0; i < attachedItemsCount; i++)
                entitySystem.WriteEntityDataRef(attachedItems[i].entityData);
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DeserializeGameState");
#endif
            if (isImport && (!entitySystem.OptionsFromExport.includeEntities || !entitySystem.ImportOptions.includeEntities))
                return null;

            if (isImport)
                for (int i = 0; i < attachedItemsCount; i++)
                {
                    ItemExtensionData itemData = attachedItems[i];
                    if (itemData != null)
                        itemData.attachedItemIndex = ItemExtensionData.DetachedItemIndex;
                }

            attachedItemsCount = (int)lockstep.ReadSmallUInt();
            ArrList.EnsureCapacity(ref attachedItems, attachedItemsCount);
            for (int i = 0; i < attachedItemsCount; i++)
            {
                entitySystem.TryReadEntityDataRef(out EntityData entityData, isImport);
                ItemExtensionData itemData = entityData.GetExtensionData<ItemExtensionData>(nameof(ItemExtensionData));
                attachedItems[i] = itemData;
                itemData.attachedItemIndex = i;
            }
            return null;
        }
    }
}
