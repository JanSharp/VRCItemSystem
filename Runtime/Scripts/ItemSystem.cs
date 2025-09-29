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
        [HideInInspector][SerializeField][SingletonReference] private BoneAttachmentManager boneAttachment;
        [HideInInspector][SerializeField][SingletonReference] private InterpolationManager interpolation;

        private ItemExtensionData[] attachedItems = new ItemExtensionData[ArrList.MinCapacity];
        private int attachedItemsCount = 0;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;
        private bool isInVR;

        public const float VelocityThreshold = 0.2f;
        public const float AngularVelocityDegreesThreshold = 20f;

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
            // TODO: Update attached items.
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
            Vector3 offsetVector,
            Quaternion offsetRotation,
            out Vector3 resultVector,
            out Quaternion resultRotation)
        {
            Vector3 bonePosition = localPlayer.GetBonePosition(bone);
            if (bonePosition == Vector3.zero)
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

        public void PickUpByLocalPlayer(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  PickUpByLocalPlayer");
#endif
            // NOTE: Unfortunately this will only result in proper offsets if the player is in the same avatar,
            // or one with the same bone rotations, which let's be honest is unlikely.
            VRCPlayerApi.TrackingDataType trackingType = BoneToTrackingType(item.attachedToBone);
            BoneOffsetsToTrackingDataOffsets(
                trackingType, item.attachedToBone,
                item.attachedOffsetVector, item.attachedOffsetRotation,
                out Vector3 offsetVector, out Quaternion offsetRotation);
            CustomPickup pickup = item.pickup;
            item.pickupIsHeld = true;
            item.pickupIsAttached = false;
            pickup.ForceBeingPickedUp(trackingType, offsetVector, offsetRotation, attachUsingHermiteCurve);
        }

        public void AttachToLocalPlayer(ItemExtension item)
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
            Transform entityTransform = item.entity.transform;
            if (!LocalPlayerHasBone(item.attachedToBone)) // Handles attachment due to imports.
            {
                var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                entityTransform.SetPositionAndRotation(head.position + head.rotation * Vector3.forward, head.rotation);
                SendDropIA(item.data, forceNoVelocity: true); // Will run DetachFromLocalPlayer.
                return;
            }
            CustomPickup pickup = item.pickup;
            item.pickupIsHeld = false;
            item.pickupIsAttached = true;
            pickup.ForceBeingAttached(item.attachedToBone);
            interpolation.LerpLocalPosition(entityTransform, item.attachedOffsetVector, Entity.TransformChangeInterpolationDuration);
            interpolation.LerpLocalRotation(entityTransform, item.attachedOffsetRotation, Entity.TransformChangeInterpolationDuration);
        }

        public void DetachFromLocalPlayer(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromLocalPlayer");
#endif
            item.pickupIsHeld = false;
            item.pickupIsAttached = false;
            CustomPickup pickup = item.pickup;
            if (pickup.isHeld)
                pickup.Drop();
            else if (pickup.isAttached)
                pickup.Detach();
        }

        /// <summary>
        /// <para>Only used by <see cref="OnPickupIA"/>.</para>
        /// </summary>
        private bool attachUsingHermiteCurve = false;
        public void AttachToRemotePlayer(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  AttachToRemotePlayer");
#endif
            VRCPlayerApi attachedToPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            if (!Utilities.IsValid(attachedToPlayer))
                return;
            Transform entityTransform = item.entity.transform;
            boneAttachment.AttachToBone(attachedToPlayer, item.attachedToBone, entityTransform);
            // HACK: This is just copy paste from CustomInteractHandManager PickupActivePickup. Me no like.
            if (attachUsingHermiteCurve)
            {
                Vector3 heldOffsetVector = item.attachedOffsetVector;
                Vector3 directVector = heldOffsetVector - entityTransform.localPosition;
                float distance = directVector.magnitude;
                Vector3 originVelocity = Quaternion.Inverse(entityTransform.parent.rotation) * Vector3.up * distance / 2f;
                float duration = Mathf.Min(CustomInteractablesManagerAPI.MaxPickupInterpolationDuration, CustomInteractablesManagerAPI.PickupInterpolationDuration * distance);
                interpolation.HermiteCurveLocalPosition(entityTransform, originVelocity, heldOffsetVector, directVector, duration);
                interpolation.LerpLocalRotation(entityTransform, item.attachedOffsetRotation, duration);
            }
            else
            {
                interpolation.LerpLocalPosition(entityTransform, item.attachedOffsetVector, CustomInteractablesManagerAPI.PickupInterpolationDuration);
                interpolation.LerpLocalRotation(entityTransform, item.attachedOffsetRotation, CustomInteractablesManagerAPI.PickupInterpolationDuration);
            }
        }

        public void DetachFromRemotePlayer(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DetachFromRemotePlayer");
#endif
            boneAttachment.DetachFromBone(
                (int)item.attachedToPlayerId,
                item.attachedToBone,
                item.entity.transform);
        }

        private void WriteOffsets(CustomPickup pickup, out Vector3 offsetVector, out Quaternion offsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
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

        public void SendPickupIA(ItemExtensionData itemData, bool useHermiteCurve = false)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendPickupIA");
#endif
            ItemExtension item = itemData.ext;
            CustomPickup pickup = item.pickup;
            if (pickup == null || !lockstep.IsInitialized)
                return; // TODO: Either drop the item, or send a pickup IA once lockstep is initialized.
            if (!pickup.isHeld)
            {
                Debug.LogError("[ItemSystem] Attempt to SendPickupIA for an item which is not held by the local player.");
                return;
            }
            HumanBodyBones bone = TrackingTypeToBone(pickup.heldTrackingType);
            bool boneExists = LocalPlayerHasBone(bone);

            EntityData entityData = itemData.entityData;
            entitySystem.WriteEntityExtensionDataRef(itemData);
            entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteFlags(boneExists, useHermiteCurve);
            lockstep.WriteSmallInt((int)bone);
            Vector3 offsetVector = Vector3.zero;
            Quaternion offsetRotation = Quaternion.identity;
            if (boneExists)
                WriteOffsets(pickup, out offsetVector, out offsetRotation);
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(onPickupIAId));

            // Latency hiding.
            item.AttachToPlayer(isHeldSpecifically: true, localPlayerId, bone, boneExists, offsetVector, offsetRotation);
            PutPhysicsEntityExtensionToSleep(item);
        }

        [HideInInspector][SerializeField] private uint onPickupIAId;
        [LockstepInputAction(nameof(onPickupIAId))]
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
                entityData.MarkLatencyHiddenUniqueIdAsProcessed();
                return;
            }

            entityData.ReadPotentiallyUnknownTransformValues();
            itemData.heldItemIndex = attachedItemsCount;
            ArrList.Add(ref attachedItems, ref attachedItemsCount, itemData);
            itemData.attachedToPlayerId = lockstep.SendingPlayerId;
            lockstep.ReadFlags(out itemData.attachedBoneExists, out bool useHermiteCurve);
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            if (itemData.attachedBoneExists)
            {
                ReadOffsets(itemData);
                entityData.TakeControlOfTransformSync(transformController);
            }

            // Putting the physics entity to sleep after the item has taken control theoretically
            // reduces the total amount of work that needs to be done by the entity system.
            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            if (physicsData != null)
                physicsData.GoToSleep();

            ItemExtension item = itemData.ext;
            if (!entityData.ShouldApplyReceivedIAToLatencyState() || item == null)
                return;

            attachUsingHermiteCurve = useHermiteCurve;
            item.AttachToPlayerUsingItemData();
            attachUsingHermiteCurve = false;
            PutPhysicsEntityExtensionToSleep(item);
        }

        public void SendAttachIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SendAttachIA");
#endif
            ItemExtension item = itemData.ext;
            CustomPickup pickup = item.pickup;
            if (pickup == null || !lockstep.IsInitialized)
                return; // TODO: Either detach the item, or send an attach IA once lockstep is initialized.
            if (!pickup.isAttached)
            {
                Debug.LogError("[ItemSystem] Attempt to SendAttachIA for an item which is not attached to the local player.");
                return;
            }
            // Do not check if the attached bone exists. It always exists inside of the OnAttach event for
            // the pickup, so it can only not exist if SendAttachIA gets sent outside of an OnAttach event.
            // The item system itself does not do that and it's arguably even invalid.
            HumanBodyBones bone = pickup.attachedToBone;

            EntityData entityData = itemData.entityData;
            entitySystem.WriteEntityExtensionDataRef(itemData);
            entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteSmallInt((int)bone);
            Transform entityTransform = itemData.entity.transform;
            Vector3 offsetVector = entityTransform.localPosition;
            Quaternion offsetRotation = entityTransform.localRotation;
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(attachIAId));

            // Latency hiding.
            item.AttachToPlayer(isHeldSpecifically: false, localPlayerId, bone, boneExists: true, offsetVector, offsetRotation);
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
                entityData.MarkLatencyHiddenUniqueIdAsProcessed();
                return;
            }

            entityData.ReadPotentiallyUnknownTransformValues();
            itemData.heldItemIndex = attachedItemsCount;
            ArrList.Add(ref attachedItems, ref attachedItemsCount, itemData);
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

            item.AttachToPlayerUsingItemData();
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
                lockstep.WriteFlags(doApplyVelocity);
                if (doApplyVelocity)
                {
                    lockstep.WriteVector3(item.trackedVelocity);
                    lockstep.WriteVector3(item.TrackedAngularVelocity);
                }
            }

            if (!itemData.entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(onDropIAId)))
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

        [HideInInspector][SerializeField] private uint onDropIAId;
        [LockstepInputAction(nameof(onDropIAId))]
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
            lockstep.SendSingletonInputAction(onForceDropIAId); // Not latency hidden.
        }

        [HideInInspector][SerializeField] private uint onForceDropIAId;
        [LockstepInputAction(nameof(onForceDropIAId))]
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
            Vector3 velocity = Vector3.zero;
            Vector3 angularVelocity = Vector3.zero;
            if (mightHaveVelocity && physicsData != null)
            {
                lockstep.ReadFlags(out doApplyVelocity);
                if (doApplyVelocity)
                {
                    velocity = lockstep.ReadVector3();
                    angularVelocity = lockstep.ReadVector3();
                    physicsData.velocity = velocity;
                    physicsData.angularVelocity = angularVelocity;
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
            RemoveFromHeldItems(itemData);

            itemData.attachedToPlayerId = 0u;
            itemData.attachedBoneExists = false;
            itemData.attachedToBone = HumanBodyBones.Head;
            itemData.attachedOffsetVector = Vector3.zero;
            itemData.attachedOffsetRotation = Quaternion.identity;
        }

        private void RemoveFromHeldItems(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  RemoveFromHeldItems");
#endif
            attachedItemsCount--;
            int index = itemData.heldItemIndex;
            itemData.heldItemIndex = 0;
            if (index != attachedItemsCount)
            {
                ItemExtensionData other = attachedItems[attachedItemsCount];
                attachedItems[index] = other;
                other.heldItemIndex = index;
            }
            attachedItems[attachedItemsCount] = null; // Make GC happy.
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SerializeGameState");
#endif
            lockstep.WriteSmallUInt((uint)attachedItemsCount);
            for (int i = 0; i < attachedItemsCount; i++)
                lockstep.WriteSmallUInt(attachedItems[i].entityData.id);
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DeserializeGameState");
#endif
            attachedItemsCount = (int)lockstep.ReadSmallUInt();
            ArrList.EnsureCapacity(ref attachedItems, attachedItemsCount);
            for (int i = 0; i < attachedItemsCount; i++)
            {
                uint id = lockstep.ReadSmallUInt();
                EntityData entityData = entitySystem.GetEntityData(id);
                ItemExtensionData itemData = entityData.GetExtensionData<ItemExtensionData>(nameof(ItemExtensionData));
                attachedItems[i] = itemData;
                itemData.heldItemIndex = i;
            }
            return null;
        }
    }
}
