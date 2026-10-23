using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    [LockstepGameStateDependency(typeof(EntitySystemAPI))]
    public class ItemSystem : LockstepGameState
    {
        public override string GameStateInternalName => "jansharp.item-system";
        public override string GameStateDisplayName => "Item System";
        public override bool GameStateSupportsImportExport => true;
        public override uint GameStateDataVersion => 0u;
        public override uint GameStateLowestSupportedDataVersion => 0u;
        public override LockstepGameStateOptionsUI ExportUI => null;
        public override LockstepGameStateOptionsUI ImportUI => null;

        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemForceDroppingLogic forceDroppingLogic;
        [HideInInspector][SerializeField][SingletonReference] private InterpolationManager interpolation;
        [HideInInspector][SerializeField][SingletonReference] private CustomInteractablesManagerAPI interactables;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemHandOffsetsLogic handOffsetsLogic;

        public CustomPickupState stateForPickupController;
        public CustomPickupAttachedState stateForAttachedPickupController;

        private ItemExtensionData[] attachedItems = new ItemExtensionData[ArrList.MinCapacity];
        private int attachedItemsCount = 0;

        private VRCPlayerApi localPlayer;
        private bool isInVR;

        public const float VelocityThreshold = 0.3f;
        public const float AngularVelocityDegreesThreshold = 30f;

        private void Start()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  Start");
#endif
            localPlayer = Networking.LocalPlayer;
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
                forceDroppingLogic.SendForceDropIA(itemData, forceNoVelocity: true, sendSingletonIA: true);
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
                CustomPickup heldInLeftHand = interactables.HeldInLeftHand;
                CustomPickup heldInRightHand = interactables.HeldInRightHand;
                UpdateHeldItemDueToAvatarChange(heldInLeftHand);
                if (heldInRightHand != heldInLeftHand)
                    UpdateHeldItemDueToAvatarChange(heldInRightHand);
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

            if (pickup.isHeldByPrimaryHand)
                handOffsetsLogic.SendHandOffsetsChangedIA(item.data, isPrimary: true);
            if (pickup.isHeldBySecondaryHand)
                handOffsetsLogic.SendHandOffsetsChangedIA(item.data, isPrimary: false);
        }

        public void DropAndDetachPickupLocallyAndRemotely(CustomPickup pickup)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DropAndDetachPickupLocallyAndRemotely");
#endif
            DropAndDetachPickupLocally(pickup);
            DropAndDetachPickupRemotely(pickup);
        }

        public void DropAndDetachPickupLocally(CustomPickup pickup)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DropAndDetachPickupLocally");
#endif
            if (pickup.state == CustomPickupControlState.None || !pickup.controllingPlayerIsLocal) // Not required, but it short circuits.
                return;
            pickup.Drop(preventAttachment: true); // Only does anything if it is held by the local player.
            pickup.Detach(); // Only does anything if it is attached to the local player.
        }

        public void DropAndDetachPickupRemotely(CustomPickup pickup)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DropAndDetachPickupRemotely");
#endif
            if (pickup.state == CustomPickupControlState.None || pickup.controllingPlayerIsLocal)
                return;
            pickup.isHeldByPrimaryHand = false;
            pickup.isHeldBySecondaryHand = false;
            pickup.SetControlState(CustomPickupControlState.None);
        }

        public void CancelManualInterpolation(ItemExtension item)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  CancelManualInterpolation");
#endif
            Transform entityTransform = item.entity.transform;
            interpolation.CancelPositionInterpolation(entityTransform);
            interpolation.CancelRotationInterpolation(entityTransform);
        }

        public void MoveItemToEntityDataLocationManually(ItemExtension item, bool doInterpolate)
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

        public void PutPhysicsEntityExtensionToSleep(ItemExtension item)
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

        public void SetAttachedToPlayerId(ItemExtensionData itemData, uint attachedToPlayerId)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SetAttachedToPlayerId - attachedToPlayerId: {attachedToPlayerId}");
#endif
            if (itemData.attachedToPlayerId == attachedToPlayerId)
                return;
            bool wasAttached = itemData.attachedToPlayerId != 0u;
            bool isAttached = attachedToPlayerId != 0u;
            itemData.attachedToPlayerId = attachedToPlayerId;
            if (wasAttached == isAttached)
                return;
            if (isAttached)
                AddToAttachedItems(itemData);
            else
                RemoveFromAttachedItems(itemData);
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

        public void WriteAttachedPlayerRef(ItemExtensionData itemData, bool isExport)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  WriteAttachedPlayer");
#endif
            if (!isExport)
            {
                lockstep.WriteSmallUInt(itemData.attachedToPlayerId);
                return;
            }
            playerDataManager.WriteCorePlayerDataRef(playerDataManager.GetCorePlayerDataForPlayerId(itemData.attachedToPlayerId));
        }

        public void ReadAttachedPlayerRef(ItemExtensionData itemData, bool isImport)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  ReadAttachedPlayer");
#endif
            if (!isImport)
            {
                SetAttachedToPlayerId(itemData, lockstep.ReadSmallUInt());
                return;
            }
            CorePlayerData playerData = playerDataManager.ReadCorePlayerDataRef(isImport: true);
            SetAttachedToPlayerId(itemData, playerData == null || playerData.isOffline ? 0u : playerData.playerId);
        }

        public override void SerializeGameState(bool isExport, LockstepGameStateOptionsData exportOptions)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  SerializeGameState");
#endif
            if (isExport)
                return;

            // For late joiners the order of attachedItems must be synced in order to make it deterministic
            // and game state safe. For imports this does not require syncing, as the modification of
            // attachedItems during the import is deterministic.
            lockstep.WriteSmallUInt((uint)attachedItemsCount);
            for (int i = 0; i < attachedItemsCount; i++)
                entitySystem.WriteEntityDataRef(attachedItems[i].entityData);
        }

        public override string DeserializeGameState(bool isImport, uint importedDataVersion, LockstepGameStateOptionsData importOptions)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystem  DeserializeGameState");
#endif
            if (isImport)
                return null;

            int expectedAttachedItemsCount = (int)lockstep.ReadSmallUInt();
            if (attachedItemsCount != expectedAttachedItemsCount)
                Debug.LogError($"[ItemSystem] Impossible, expected {expectedAttachedItemsCount} attached items, "
                    + $"got {attachedItemsCount}. The amount of attached items get synced through entity extension "
                    + $"game state serialization and deserialization, something must not be using the appropriate "
                    + $"setter when attaching or detaching an item.");

            // This here is merely reordering items to make said order deterministic.
            for (int i = 0; i < attachedItemsCount; i++)
            {
                entitySystem.TryReadEntityDataRef(out EntityData entityData, isImport: false);
                ItemExtensionData itemData = entityData.GetExtensionData<ItemExtensionData>(nameof(ItemExtensionData));
                attachedItems[i] = itemData;
                itemData.attachedItemIndex = i;
            }
            return null;
        }
    }
}
