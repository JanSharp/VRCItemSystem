using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemAttachedOffsetsLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemDetachingLogic detachingLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;

        private uint localPlayerId;

        private void Start()
        {
            localPlayerId = (uint)Networking.LocalPlayer.playerId;
        }

        private bool CanSendAttachedOffsetsChangedIA(ItemExtensionData itemData)
        {
            ItemExtension item = itemData.ext;
            if (item == null || !lockstep.IsInitialized)
                return false;
            CustomPickup pickup = item.pickup;
            if (!pickup.isAttached || !pickup.controllingPlayerIsLocal)
            {
                Debug.LogError($"[ItemSystem] Attempt to {nameof(SendAttachedOffsetsChangedIA)} "
                    + $"for an item which is not attached to the local player.");
                return false;
            }
            return true;
        }

        public void SendAttachedOffsetsChangedIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachedOffsetsLogic  SendAttachedOffsetsChangedIA");
#endif
            if (!CanSendAttachedOffsetsChangedIA(itemData))
                return;
            // Do not check if the attached bone exists. It always exists inside of the OnAttach event for
            // the pickup, so it can only not exist if SendAttachIA gets sent outside of an OnAttach event.
            // The item system itself does not do that and it's arguably even invalid.
            ItemExtension item = itemData.ext;
            CustomPickup pickup = item.pickup;
            EntityData entityData = itemData.entityData;

            // Setup.
            HumanBodyBones bone = pickup.attachedToBone;
            Vector3 offsetVector = pickup.attachedOffsetVector;
            Quaternion offsetRotation = pickup.attachedOffsetRotation;

            // Send IA.
            entitySystem.WriteEntityExtensionDataRef(itemData);
            lockstep.WriteSmallInt((int)bone);
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(attachedOffsetsChangedIAId));

            // Latency hiding.
            AttachedOffsetsChangedIALatency(item, bone, offsetVector, offsetRotation);
        }

        private bool DoApplyAttachedOffsetsChangedIA(ItemExtensionData itemData)
        {
            if (itemData == null)
                return false;
            EntityData entityData = itemData.entityData;
            if (!itemData.isAttached || lockstep.SendingPlayerId != itemData.attachedToPlayerId)
            {
                // Handles two players attaching the same item simultaneously.
                entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return false;
            }
            return true;
        }

        [HideInInspector][SerializeField] private uint attachedOffsetsChangedIAId;
        [LockstepInputAction(nameof(attachedOffsetsChangedIAId))]
        public void OnAttachedOffsetsChangedIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachedOffsetsLogic  OnAttachedOffsetsChangedIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (!DoApplyAttachedOffsetsChangedIA(itemData))
                return;
            EntityData entityData = itemData.entityData;

            // Read IA data.
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            itemData.attachedOffsetVector = lockstep.ReadVector3();
            itemData.attachedOffsetRotation = lockstep.ReadQuaternion();

            // Generic interaction stuff.
            entityData.LastUserPlayerData = entitySystem.GetPlayerData(playerDataManager.SendingPlayerData);

            // Latency State.
            ItemExtension item = itemData.ext;
            if (entityData.ShouldApplyReceivedIAToLatencyState() && item != null)
                AttachedOffsetsChangedIALatency(item, itemData.attachedToBone, itemData.attachedOffsetVector, itemData.attachedOffsetRotation);
        }

        private void AttachedOffsetsChangedIALatency(ItemExtension item, HumanBodyBones bone, Vector3 anchorVector, Quaternion anchorRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachedOffsetsLogic  AttachedOffsetsChangedIALatency");
#endif
            item.attachedToBone = bone;
            item.attachedOffsetVector = anchorVector;
            item.attachedOffsetRotation = anchorRotation;

            itemLogic.OnAttachedOffsetsChanged(item, doInterpolate: true);
        }

        public void ChangeLocalPlayerAttachedOffsets(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachedOffsetsLogic  ChangeLocalPlayerAttachedOffsets - doInterpolate: {doInterpolate}");
#endif
            CustomPickup pickup = item.pickup;

            if (!pickup.isAttached || pickup.controllingPlayerId != localPlayerId)
            {
                Debug.LogError($"[ItemSystem] Impossible, attempt to {nameof(ChangeLocalPlayerAttachedOffsets)} "
                    + $"while the local player is not holding the item.", item);
                return;
            }

            pickup.attachedToBone = item.attachedToBone;
            pickup.attachedOffsetVector = item.attachedOffsetVector;
            pickup.attachedOffsetRotation = item.attachedOffsetRotation;

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }

        public void ChangeRemotePlayerAttachedOffsets(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachedOffsetsLogic  ChangeRemotePlayerAttachedOffsets - doInterpolate: {doInterpolate}");
#endif
            VRCPlayerApi attachedToPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            if (!Utilities.IsValid(attachedToPlayer))
                return;

            CustomPickup pickup = item.pickup;

            if (!pickup.isAttached || pickup.controllingPlayerId != item.attachedToPlayerId)
            {
                Debug.LogError($"[ItemSystem] Impossible, attempt to {nameof(ChangeRemotePlayerAttachedOffsets)} "
                    + $"while the item is not held by the player the item thinks it should be.", item);
                return;
            }

            pickup.attachedToBone = item.attachedToBone;
            pickup.attachedOffsetVector = item.attachedOffsetVector;
            pickup.attachedOffsetRotation = item.attachedOffsetRotation;

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }
    }
}
