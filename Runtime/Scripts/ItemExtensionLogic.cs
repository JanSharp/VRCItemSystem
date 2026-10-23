using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemExtensionLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemPickingUpLogic pickingUpLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemAttachingLogic attachingLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemDroppingLogic droppingLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemDetachingLogic detachingLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemForceDroppingLogic forceDroppingLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemHandOffsetsLogic handOffsetsLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemAttachedOffsetsLogic attachedOffsetsLogic;
        [HideInInspector][SerializeField][SingletonReference] private PlayerTrackingDataSyncManagerAPI trackingDataSync;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;
        [HideInInspector][SerializeField][SingletonReference] private UpdateManager updateManager;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        private void SetPreventPickupInteraction(ItemExtension item, bool value)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  SetPreventPickupInteraction");
#endif
            if (item.preventPickupInteraction == value)
                return;
            item.preventPickupInteraction = value;
            if (item.preventPickupInteraction)
                item.pickup.IncrementPreventInteraction();
            else
                item.pickup.DecrementPreventInteraction();
        }

        public void UpdatePickupInteractionPrevention(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  UpdatePickupInteractionPrevention");
#endif
            if (item.data == null)
            {
                SetPreventPickupInteraction(item, true);
                return;
            }
            uint attachedToPlayerId = item.attachedToPlayerId;
            if (attachedToPlayerId == 0u || attachedToPlayerId == localPlayerId)
                SetPreventPickupInteraction(item, false); // Not held by anybody, or held by local player.
            else // Is held by or attached to another player.
                SetPreventPickupInteraction(
                    item,
                    item.DisallowStealingHeld && item.IsHeld
                        || item.DisallowStealingAttached && item.isAttached);
        }

        public void OnInstantiate(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnInstantiate");
#endif
            item.stateForPickupController = itemSystem.stateForPickupController;
            item.stateForAttachedPickupController = itemSystem.stateForAttachedPickupController;
            item.pickup = item.GetComponent<CustomPickup>();
            item.pickupTransform = item.pickup.transform;
            item.physicsExt = item.entity.GetExtension<PhysicsEntityExtension>(nameof(PhysicsEntityExtensionData));
            UpdatePickupInteractionPrevention(item);
        }

        public void DisassociateFromExtensionDataAndReset(ItemExtension item, EntityExtension defaultExtension)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  DisassociateFromExtensionDataAndReset");
#endif
            DetachFromPlayer(item);
            item.data.ext = null;
            item.data = null;
            UpdatePickupInteractionPrevention(item);
        }

        public void AssociateWithExtensionData(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  AssociateWithExtensionData");
#endif
            item.data = (ItemExtensionData)item.extensionData;
            item.data.ext = item;
            ApplyExtensionData(item);
            UpdatePickupInteractionPrevention(item);
        }

        public void ApplyExtensionData(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  ApplyExtensionData");
#endif
            if (item.data.IsHeldOrAttached)
                AttachToPlayerUsingItemData(item, doInterpolate: false);
            else
                DetachFromPlayer(item, moveToGameState: true, doInterpolate: false);
        }

        public void AttachToPlayerUsingItemData(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  AttachToPlayerUsingItemData");
#endif
            ItemExtensionData itemData = item.data;
            CustomPickup pickup = item.pickup;

            pickup.BeginStateModification();

            itemSystem.DropAndDetachPickupLocallyAndRemotely(pickup);

            SetAttachedToPlayerId(item, itemData.attachedToPlayerId);

            if (!itemData.isHeldByPrimaryHand)
                item.ClearPrimaryValues();
            else
            {
                item.isHeldByPrimaryHand = true;
                SetPrimaryBone(item, itemData.primaryBone);
                item.primaryBoneExists = itemData.primaryBoneExists;
                item.primaryAnchorVector = itemData.primaryAnchorVector;
                item.primaryAnchorRotation = itemData.primaryAnchorRotation;
                item.primaryOffsetVector = itemData.primaryOffsetVector;
                item.primaryOffsetRotation = itemData.primaryOffsetRotation;
                OnPrimaryChanged(item, moveToGameStateOnDetach: true, doInterpolate: false);
            }

            if (!itemData.isHeldBySecondaryHand)
                item.ClearSecondaryValues();
            else
            {
                item.isHeldBySecondaryHand = true;
                SetSecondaryBone(item, itemData.secondaryBone);
                item.secondaryBoneExists = itemData.secondaryBoneExists;
                item.secondaryAnchorVector = itemData.secondaryAnchorVector;
                item.secondaryAnchorRotation = itemData.secondaryAnchorRotation;
                item.secondaryOffsetVector = itemData.secondaryOffsetVector;
                item.secondaryOffsetRotation = itemData.secondaryOffsetRotation;
                OnSecondaryChanged(item, moveToGameStateOnDetach: true, doInterpolate: false);
            }

            if (!itemData.isAttached)
                item.ClearAttachedValues();
            else
            {
                item.isAttached = true;
                item.attachedToBone = itemData.attachedToBone;
                item.attachedOffsetVector = itemData.attachedOffsetVector;
                item.attachedOffsetRotation = itemData.attachedOffsetRotation;
                OnAttachedChanged(item, moveToGameStateOnDetach: true, doInterpolate: false);
            }

            if (item.IsHeldOrAttached)
                itemSystem.PutPhysicsEntityExtensionToSleep(item);

            StartStopMovementLoop(item, moveToGameStateOnDetach: true, doInterpolate: false);
            UpdatePickupInteractionPrevention(item);
            itemSystem.CancelManualInterpolation(item);
            pickup.StopInterpolation();

            pickup.FinishStateModification();
        }

        public void DetachFromPlayer(ItemExtension item, bool moveToGameState = false, bool doInterpolate = false)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  DetachFromPlayer - moveToGameState: {moveToGameState}, doInterpolate: {doInterpolate}");
#endif
            if (item.attachedToPlayerId == 0u)
                return;

            item.ClearPrimaryValues();
            item.ClearSecondaryValues();
            item.ClearAttachedValues();
            SetAttachedToPlayerId(item, 0u);
            OnForceDropped(item, moveToGameState, doInterpolate);
        }

        public void OnPickupStateChanged(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnPickupStateChanged");
#endif
            CustomPickup pickup = item.pickup;
            if (pickup == null || !lockstep.IsInitialized)
                return;

            if (!pickup.controllingPlayerIsLocal && pickup.state != CustomPickupControlState.None)
            {
                // A different player is responsible for syncing this pickup.
                item.pickupIsHeldByPrimaryHand = false;
                item.pickupIsHeldBySecondaryHand = false;
                item.pickupIsAttached = false;
                return;
            }

            bool newIsHeldByPrimaryHand = pickup.isHeldByPrimaryHand;
            bool newIsHeldBySecondaryHand = pickup.isHeldBySecondaryHand;
            bool newIsAttached = pickup.isAttached;

            if (newIsAttached)
            {
                if (item.pickupIsAttached)
                {
                    if (pickup.attachedToBone != item.attachedToBone
                        || pickup.attachedOffsetVector != item.attachedOffsetVector
                        || pickup.attachedOffsetRotation != item.attachedOffsetRotation)
                    {
                        FlagAttachedOffsetsChanged(item);
                    }
                    return;
                }
                item.pickupIsHeldByPrimaryHand = false;
                item.pickupIsHeldBySecondaryHand = false;
                item.pickupIsAttached = true;
                attachingLogic.SendAttachIA(item.data);
                return;
            }
            item.pickupIsAttached = false;

            if (item.pickupIsHeldByPrimaryHand == newIsHeldByPrimaryHand)
            {
                if (newIsHeldByPrimaryHand
                    && (pickup.primaryHeldTrackingType != ItemSystemUtil.BoneToTrackingType(item.primaryBone)
                        || pickup.primaryOffsetVector != item.primaryOffsetVector
                        || pickup.primaryOffsetRotation != item.primaryOffsetRotation))
                {
                    FlagPrimaryOffsetsChanged(item);
                }
            }
            else
            {
                item.pickupIsHeldByPrimaryHand = newIsHeldByPrimaryHand;
                if (newIsHeldByPrimaryHand)
                    pickingUpLogic.SendPickupIA(item.data, isPrimary: true);
                else
                    droppingLogic.SendDropIA(item.data, isPrimary: true, forceNoVelocity: false);
            }

            if (item.pickupIsHeldBySecondaryHand == newIsHeldBySecondaryHand)
            {
                if (newIsHeldBySecondaryHand
                    && (pickup.secondaryHeldTrackingType != ItemSystemUtil.BoneToTrackingType(item.secondaryBone)
                        || pickup.secondaryOffsetVector != item.secondaryOffsetVector
                        || pickup.secondaryOffsetRotation != item.secondaryOffsetRotation))
                {
                    FlagSecondaryOffsetsChanged(item);
                }
            }
            else
            {
                item.pickupIsHeldBySecondaryHand = newIsHeldBySecondaryHand;
                if (newIsHeldBySecondaryHand)
                    pickingUpLogic.SendPickupIA(item.data, isPrimary: false);
                else
                    droppingLogic.SendDropIA(item.data, isPrimary: false, forceNoVelocity: false);
            }
        }

        private void FlagPrimaryOffsetsChanged(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  FlagPrimaryOffsetsChanged");
#endif
            if (item.flaggedForPrimaryOffsetsChanged)
                return;
            item.flaggedForPrimaryOffsetsChanged = true;
            item.SendCustomEventDelayedSeconds(nameof(item.OnPrimaryOffsetsChangedDelayed), ItemExtension.OffsetsChangeSyncInterval);
        }

        private void FlagSecondaryOffsetsChanged(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  FlagSecondaryOffsetsChanged");
#endif
            if (item.flaggedForSecondaryOffsetsChanged)
                return;
            item.flaggedForSecondaryOffsetsChanged = true;
            item.SendCustomEventDelayedSeconds(nameof(item.OnSecondaryOffsetsChangedDelayed), ItemExtension.OffsetsChangeSyncInterval);
        }

        private void FlagAttachedOffsetsChanged(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  FlagAttachedOffsetsChanged");
#endif
            if (item.flaggedForAttachedOffsetsChanged)
                return;
            item.flaggedForAttachedOffsetsChanged = true;
            item.SendCustomEventDelayedSeconds(nameof(item.OnAttachedOffsetsChangedDelayed), ItemExtension.OffsetsChangeSyncInterval);
        }

        public void OnPrimaryOffsetsChangedDelayed(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnPrimaryOffsetsChangedDelayed");
#endif
            item.flaggedForPrimaryOffsetsChanged = false;
            if (item.data != null) // Entity could have been destroyed in the meantime.
                handOffsetsLogic.SendHandOffsetsChangedIA(item.data, isPrimary: true);
        }

        public void OnSecondaryOffsetsChangedDelayed(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnSecondaryOffsetsChangedDelayed");
#endif
            item.flaggedForSecondaryOffsetsChanged = false;
            if (item.data != null) // Entity could have been destroyed in the meantime.
                handOffsetsLogic.SendHandOffsetsChangedIA(item.data, isPrimary: false);
        }

        public void OnAttachedOffsetsChangedDelayed(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnAttachedOffsetsChangedDelayed");
#endif
            item.flaggedForAttachedOffsetsChanged = false;
            if (item.data != null) // Entity could have been destroyed in the meantime.
                attachedOffsetsLogic.SendAttachedOffsetsChangedIA(item.data);
        }

        private void TakeOrGiveBackControlOfTransformSync(ItemExtension item, bool moveToGameStateOnDetach, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  TakeOrGiveBackControlOfTransformSync");
#endif
            bool newValue = item.attachedToPlayerId != 0u;
            if (item.shouldHaveControlOfTransformSync == newValue)
                return;
            item.shouldHaveControlOfTransformSync = newValue;

            Entity entity = item.entity;

            if (newValue)
            {
                entity.TakeControlOfTransformSync(transformController);
                return;
            }

            if (moveToGameStateOnDetach)
            {
                EntityData entityData = item.entityData;
                entity.GiveBackControlOfTransformSync(
                    transformController,
                    entityData.position,
                    entityData.rotation,
                    entityData.scale,
                    doInterpolate ? Entity.TransformChangeInterpolationDuration : 0f);
                return;
            }

            Transform entityTransform = entity.transform;
            entity.GiveBackControlOfTransformSync(
                transformController,
                entityTransform.position,
                entityTransform.rotation,
                entityTransform.localScale);
        }

        public void StartStopMovementLoop(ItemExtension item, bool moveToGameStateOnDetach, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  StartStopMovementLoop");
#endif
            TakeOrGiveBackControlOfTransformSync(item, moveToGameStateOnDetach, doInterpolate);

            bool isAttachedToLocalPlayer = item.attachedToPlayerId == localPlayerId;
            item.isUpdatingPickupController = !isAttachedToLocalPlayer && item.IsHeldOrAttached;
            item.isTrackingVelocity = isAttachedToLocalPlayer && item.IsHeld && item.physicsExt != null;

            if (item.isUpdatingPickupController || item.isTrackingVelocity)
                updateManager.Register(item);
            else
                updateManager.Deregister(item);

            if (!item.isTrackingVelocity)
                return;
            item.trackedVelocity = Vector3.zero;
            item.trackedAngularVelocityAxis = Vector3.zero;
            item.trackedAngularVelocityAngle = 0f;
            Transform entityTransform = item.entity.transform;
            item.positionLastFrame = entityTransform.position;
            item.rotationLastFrame = entityTransform.rotation;
        }

        public void SetAttachedToPlayerId(ItemExtension item, uint playerId)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  SetAttachedToPlayerId");
#endif
            if (item.attachedToPlayerId == playerId)
                return;
            item.attachedToPlayerId = playerId;
            item.attachedTrackingDataSync = playerId == 0u
                ? null
                : playerDataManager.GetPlayerDataForPlayerId<PlayerTrackingDataSync>(nameof(PlayerTrackingDataSync), playerId);
        }

        public void SetPrimaryBone(ItemExtension item, HumanBodyBones bone)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  SetPrimaryBone");
#endif
            if (!item.isPrimaryTrackingDataSyncActive)
            {
                item.primaryBone = bone;
                return;
            }
            HumanBodyBones prevBone = item.primaryBone;
            if (prevBone == bone)
                return;
            trackingDataSync.StopTracking(ItemSystemUtil.BoneToTrackingType(prevBone));
            item.primaryBone = bone;
            trackingDataSync.StopTracking(ItemSystemUtil.BoneToTrackingType(bone));
        }

        public void SetSecondaryBone(ItemExtension item, HumanBodyBones bone)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  SetSecondaryBone");
#endif
            if (!item.isSecondaryTrackingDataSyncActive)
            {
                item.secondaryBone = bone;
                return;
            }
            HumanBodyBones prevBone = item.secondaryBone;
            if (prevBone == bone)
                return;
            trackingDataSync.StopTracking(ItemSystemUtil.BoneToTrackingType(prevBone));
            item.secondaryBone = bone;
            trackingDataSync.StopTracking(ItemSystemUtil.BoneToTrackingType(bone));
        }

        public void SetIsPrimaryTrackingDataSyncActive(
            ItemExtension item,
            VRCPlayerApi.TrackingDataType trackingType,
            bool isPrimaryTrackingDataSyncActive)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  SetIsPrimaryTrackingDataSyncActive");
#endif
            if (item.isPrimaryTrackingDataSyncActive == isPrimaryTrackingDataSyncActive)
                return;
            item.isPrimaryTrackingDataSyncActive = isPrimaryTrackingDataSyncActive;

            if (isPrimaryTrackingDataSyncActive)
                trackingDataSync.SendBeginTrackingIA(trackingType);
            else
                trackingDataSync.SendStopTrackingIA(trackingType);
        }

        public void SetIsSecondaryTrackingDataSyncActive(
            ItemExtension item,
            VRCPlayerApi.TrackingDataType trackingType,
            bool isSecondaryTrackingDataSyncActive)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  SetIsSecondaryTrackingDataSyncActive");
#endif
            if (item.isSecondaryTrackingDataSyncActive == isSecondaryTrackingDataSyncActive)
                return;
            item.isSecondaryTrackingDataSyncActive = isSecondaryTrackingDataSyncActive;

            if (isSecondaryTrackingDataSyncActive)
                trackingDataSync.SendBeginTrackingIA(trackingType);
            else
                trackingDataSync.SendStopTrackingIA(trackingType);
        }

        public void OnPrimaryChanged(ItemExtension item, bool moveToGameStateOnDetach, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnPrimaryChanged - moveToGameStateOnDetach: {moveToGameStateOnDetach}, doInterpolate: {doInterpolate}");
#endif
            StartStopMovementLoop(item, moveToGameStateOnDetach, doInterpolate);
            UpdatePickupInteractionPrevention(item);

            if (item.isHeldByPrimaryHand)
            {
                if (item.attachedToPlayerId == localPlayerId)
                    pickingUpLogic.PickUpByLocalPlayer(item, isPrimary: true, doInterpolate);
                else
                    pickingUpLogic.PickUpByRemotePlayer(item, isPrimary: true, doInterpolate);
            }
            else
                droppingLogic.DropFromLocalOrRemotePlayer(item, isPrimary: true, doInterpolate);
        }

        public void OnSecondaryChanged(ItemExtension item, bool moveToGameStateOnDetach, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnSecondaryChanged - moveToGameStateOnDetach: {moveToGameStateOnDetach}, doInterpolate: {doInterpolate}");
#endif
            StartStopMovementLoop(item, moveToGameStateOnDetach, doInterpolate);
            UpdatePickupInteractionPrevention(item);

            if (item.isHeldBySecondaryHand)
            {
                if (item.attachedToPlayerId == localPlayerId)
                    pickingUpLogic.PickUpByLocalPlayer(item, isPrimary: false, doInterpolate);
                else
                    pickingUpLogic.PickUpByRemotePlayer(item, isPrimary: false, doInterpolate);
            }
            else
                droppingLogic.DropFromLocalOrRemotePlayer(item, isPrimary: false, doInterpolate);
        }

        public void OnAttachedChanged(ItemExtension item, bool moveToGameStateOnDetach, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnAttachedChanged - moveToGameStateOnDetach: {moveToGameStateOnDetach}, doInterpolate: {doInterpolate}");
#endif
            StartStopMovementLoop(item, moveToGameStateOnDetach, doInterpolate);
            UpdatePickupInteractionPrevention(item);

            if (item.isAttached)
            {
                if (item.attachedToPlayerId == localPlayerId)
                    attachingLogic.AttachToLocalPlayer(item, doInterpolate);
                else
                    attachingLogic.AttachToRemotePlayer(item, doInterpolate);
            }
            else
                detachingLogic.DropAndDetachFromLocalOrRemotePlayer(item, doInterpolate);
        }

        public void OnForceDropped(ItemExtension item, bool moveToGameState, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnForceDropped - moveToGameState: {moveToGameState}, doInterpolate: {doInterpolate}");
#endif
            StartStopMovementLoop(item, moveToGameState, doInterpolate);
            UpdatePickupInteractionPrevention(item);
            forceDroppingLogic.ForceDropFromLocalOrRemotePlayer(item, doInterpolate);
        }

        public void OnHandOffsetsChanged(ItemExtension item, bool isPrimary, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnHandOffsetsChanged - isPrimary: {isPrimary}, doInterpolate: {doInterpolate}");
#endif
            if (item.attachedToPlayerId == localPlayerId)
                handOffsetsLogic.ChangeLocalPlayerHandOffsets(item, isPrimary, doInterpolate);
            else
                handOffsetsLogic.ChangeRemotePlayerHandOffsets(item, isPrimary, doInterpolate);
        }

        public void OnAttachedOffsetsChanged(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionLogic  OnAttachedOffsetsChanged - doInterpolate: {doInterpolate}");
#endif
            if (item.attachedToPlayerId == localPlayerId)
                attachedOffsetsLogic.ChangeLocalPlayerAttachedOffsets(item, doInterpolate);
            else
                attachedOffsetsLogic.ChangeRemotePlayerAttachedOffsets(item, doInterpolate);
        }
    }
}
