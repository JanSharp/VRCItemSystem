using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("8ed95ab9b64568256959aa53ac3bbfe0")] // Runtime/Prefabs/ItemSystem.prefab
    public class ItemSystemAttachingLogic : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private LockstepAPI lockstep;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystem itemSystem;
        [HideInInspector][SerializeField][SingletonReference] private ItemExtensionLogic itemLogic;
        [HideInInspector][SerializeField][SingletonReference] private ItemSystemDetachingLogic detachingLogic;
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        [HideInInspector][SerializeField][SingletonReference] private PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SerializeField][SingletonReference] private ItemTransformController transformController;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        private bool LocalPlayerHasBone(HumanBodyBones bone) => localPlayer.GetBonePosition(bone) != Vector3.zero;

        private bool CanSendAttachIA(ItemExtensionData itemData)
        {
            ItemExtension item = itemData.ext;
            if (item == null || !lockstep.IsInitialized)
                return false; // TODO: Either detach the item, or send an attach IA once lockstep is initialized.
            if (!item.pickup.isAttached)
            {
                Debug.LogError("[ItemSystem] Attempt to SendAttachIA for an item which is not attached to the local player.");
                return false;
            }
            return true;
        }

        public void SendAttachIA(ItemExtensionData itemData)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachingLogic  SendAttachIA");
#endif
            if (!CanSendAttachIA(itemData))
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
            entityData.WritePotentiallyUnknownTransformValues();
            lockstep.WriteSmallInt((int)bone);
            lockstep.WriteVector3(offsetVector);
            lockstep.WriteQuaternion(offsetRotation);
            entityData.RegisterLatencyHiddenUniqueId(lockstep.SendInputAction(attachIAId));

            // Latency hiding.
            AttachIALatency(item, localPlayerId, bone, offsetVector, offsetRotation);
        }

        private bool DoApplyAttachIA(ItemExtensionData itemData)
        {
            if (itemData == null)
                return false;
            EntityData entityData = itemData.entityData;
            if (itemData.IsHeldOrAttached && lockstep.SendingPlayerId != itemData.attachedToPlayerId)
            {
                // Handles two players attaching the same item simultaneously.
                entityData.ResetLatencyStateBecauseIAGotAppliedDifferently();
                return false;
            }
            return true;
        }

        [HideInInspector][SerializeField] private uint attachIAId;
        [LockstepInputAction(nameof(attachIAId))]
        public void OnAttachIA()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachingLogic  OnAttachIA");
#endif
            ItemExtensionData itemData = entitySystem.ReadEntityExtensionDataRef<ItemExtensionData>();
            if (!DoApplyAttachIA(itemData))
                return;
            EntityData entityData = itemData.entityData;

            if (itemData.isHeldByPrimaryHand)
                itemData.ClearPrimary();
            if (itemData.isHeldBySecondaryHand)
                itemData.ClearSecondary();

            // Read IA data.
            entityData.ReadPotentiallyUnknownTransformValues();
            itemData.isAttached = true;
            itemData.attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            itemData.attachedOffsetVector = lockstep.ReadVector3();
            itemData.attachedOffsetRotation = lockstep.ReadQuaternion();

            // Generic attaching stuff.
            itemSystem.SetAttachedToPlayerId(itemData, lockstep.SendingPlayerId);
            entityData.LastUserPlayerData = entitySystem.GetPlayerData(playerDataManager.SendingPlayerData);
            entityData.TakeControlOfTransformSync(transformController);

            // Putting the physics entity to sleep after the item has taken control theoretically
            // reduces the total amount of work that needs to be done by the entity system.
            PhysicsEntityExtensionData physicsData = itemData.physicsData;
            if (physicsData != null)
                physicsData.GoToSleep();

            // Latency State.
            ItemExtension item = itemData.ext;
            if (entityData.ShouldApplyReceivedIAToLatencyState() && item != null)
                AttachIALatency(item, itemData.attachedToPlayerId, itemData.attachedToBone, itemData.attachedOffsetVector, itemData.attachedOffsetRotation);
        }

        private void AttachIALatency(ItemExtension item, uint playerId, HumanBodyBones bone, Vector3 anchorVector, Quaternion anchorRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachingLogic  AttachIALatency - playerId: {playerId}");
#endif
            if (item.isHeldByPrimaryHand)
                item.ClearPrimaryValues();
            if (item.isHeldBySecondaryHand)
                item.ClearSecondaryValues();

            item.isAttached = true;
            item.attachedToBone = bone;
            item.attachedOffsetVector = anchorVector;
            item.attachedOffsetRotation = anchorRotation;

            itemLogic.SetAttachedToPlayerId(item, playerId);
            itemLogic.OnAttachedChanged(item, moveToGameStateOnDetach: true, doInterpolate: true);
            itemSystem.PutPhysicsEntityExtensionToSleep(item);
        }

        public void AttachToLocalPlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachingLogic  AttachToLocalPlayer - doInterpolate: {doInterpolate}");
#endif
            // NOTE: Unfortunately this will only result in proper offsets if the player is in the same avatar,
            // or one with the same bone rotations, which let's be honest is unlikely.

            CustomPickup pickup = item.pickup;
            itemSystem.CancelManualInterpolation(item); // Would interfere with the pickup system.
            itemSystem.DropAndDetachPickupRemotely(pickup);

            HumanBodyBones attachedToBone = item.attachedToBone;
            if (!LocalPlayerHasBone(attachedToBone)) // Handles attachment due to imports.
            {
                var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                item.entity.transform.SetPositionAndRotation(head.position + head.rotation * Vector3.forward, head.rotation);
                detachingLogic.SendDetachIA(item.data); // Will run DetachFromLocalPlayer.
                return;
            }

            item.pickupIsHeldByPrimaryHand = false;
            item.pickupIsHeldBySecondaryHand = false;
            item.pickupIsAttached = true;
            // In theory the isInOnPickupStateChanged check is redundant, however there is no reason to risk it.
            if (!item.isInOnPickupStateChanged && (!pickup.isAttached || pickup.attachedToBone != attachedToBone))
                pickup.ForceBeingAttached(attachedToBone); // TODO: This needs a ForceBeingAttachedExplicit().

            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }

        public void AttachToRemotePlayer(ItemExtension item, bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemSystemAttachingLogic  AttachToRemotePlayer - doInterpolate: {doInterpolate}");
#endif
            VRCPlayerApi attachedToPlayer = VRCPlayerApi.GetPlayerById((int)item.attachedToPlayerId);
            if (!Utilities.IsValid(attachedToPlayer))
                return;

            CustomPickup pickup = item.pickup;
            itemSystem.DropAndDetachPickupLocallyAndRemotely(pickup);

            pickup.SetControllingPlayer(attachedToPlayer);
            pickup.SetControlState(CustomPickupControlState.Attached);
            pickup.attachedToBone = item.attachedToBone;
            pickup.attachedOffsetVector = item.attachedOffsetVector;
            pickup.attachedOffsetRotation = item.attachedOffsetRotation;

            itemSystem.CancelManualInterpolation(item);
            if (doInterpolate)
                pickup.StartInterpolation();
            else
                pickup.StopInterpolation();
        }
    }
}
