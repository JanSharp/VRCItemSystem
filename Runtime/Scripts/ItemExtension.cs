using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AssociatedEntityExtensionData(typeof(ItemExtensionData))]
    [RequireComponent(typeof(CustomPickup))]
    public class ItemExtension : EntityExtension
    {
        public ItemExtensionData Data => (ItemExtensionData)extensionData;

        private CustomPickup pickup;

        private uint localPlayerId;

        private void Start()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  Start");
            #endif
            pickup = GetComponent<CustomPickup>();
            localPlayerId = (uint)Networking.LocalPlayer.playerId;
        }

        public override void ApplyExtensionData()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  ApplyExtensionData");
            #endif
            if (Data.holdingPlayerId == 0u)
                return;
            if (Data.holdingPlayerId == localPlayerId)
                AttachToLocalPlayer();
            else
                AttachToRemotePlayer();
        }

        public override void OnPickup()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickup");
            #endif
            SendPickupIA();
            // StartMovementLoop();
        }

        public override void OnDrop()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnDrop");
            #endif
            SendDropIA();
            // entity.FlagForMovement();
        }

        public override void OnPickupUseDown()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupUseDown");
            #endif
        }

        public override void OnPickupUseUp()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupUseUp");
            #endif
        }

        private void SendPickupIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  SendPickupIA");
            #endif
            lockstep.WriteFlags(pickup.heldTrackingType == VRCPlayerApi.TrackingDataType.RightHand);
            lockstep.WriteVector3(pickup.heldOffsetVector);
            lockstep.WriteQuaternion(pickup.heldOffsetRotation);
            SendExtensionInputAction(nameof(OnPickupIA));
        }

        [EntityExtensionInputAction]
        public void OnPickupIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupIA");
            #endif
            // TODO: handle non existent bones somewhere
            lockstep.ReadFlags(out Data.heldInRightHand);
            Data.heldOffsetVector = lockstep.ReadVector3();
            Data.heldOffsetRotation = lockstep.ReadQuaternion();
            Data.holdingPlayerId = lockstep.SendingPlayerId;
            // TODO: the entity system internally should periodically fetch a snapshot of the world position of these entities
            entity.entityData.transformState = EntityTransformState.Desynced;
            if (Data.holdingPlayerId != localPlayerId)
                AttachToRemotePlayer();
        }

        private void AttachToLocalPlayer()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToLocalPlayer");
            #endif
            pickup.ForceBeingPickedUp(
                Data.heldInRightHand
                    ? VRCPlayerApi.TrackingDataType.RightHand
                    : VRCPlayerApi.TrackingDataType.LeftHand,
                Data.heldOffsetVector,
                Data.heldOffsetRotation);
        }

        private void AttachToRemotePlayer()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToRemotePlayer");
            #endif
            VRCPlayerApi holdingPlayer = VRCPlayerApi.GetPlayerById((int)Data.holdingPlayerId);
            if (holdingPlayer == null)
                return;
            Transform entityTransform = entity.transform;
            Data.boneAttachment.AttachToBone(
                holdingPlayer,
                Data.heldInRightHand
                    ? HumanBodyBones.RightHand
                    : HumanBodyBones.LeftHand,
                entityTransform);
            entityTransform.localPosition = Data.heldOffsetVector;
            entityTransform.localRotation = Data.heldOffsetRotation;
        }

        private void SendDropIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  SendDropIA");
            #endif
            Transform entityTransform = entity.transform;
            lockstep.WriteVector3(entityTransform.position);
            lockstep.WriteQuaternion(entityTransform.rotation);
            SendExtensionInputAction(nameof(OnDropIA));
        }

        [EntityExtensionInputAction]
        public void OnDropIA()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnDropIA");
            #endif
            Vector3 position = lockstep.ReadVector3();
            Quaternion rotation = lockstep.ReadQuaternion();
            entity.entityData.position = position;
            entity.entityData.rotation = rotation;
            entity.transform.SetPositionAndRotation(position, rotation);
            if (Data.holdingPlayerId == 0u) // Already dropped.
                return;
            entity.entityData.transformState = EntityTransformState.Synced;
            Data.boneAttachment.DetachFromBone(
                (int)Data.holdingPlayerId,
                Data.heldInRightHand
                    ? HumanBodyBones.RightHand
                    : HumanBodyBones.LeftHand,
                entity.transform);
            Data.holdingPlayerId = 0u;
            Data.heldInRightHand = false;
            Data.heldOffsetVector = Vector3.zero;
            Data.heldOffsetRotation = Quaternion.identity;
        }

        private bool movementLoopIsRunning = false;
        private void StartMovementLoop()
        {
            if (movementLoopIsRunning)
                return;
            movementLoopIsRunning = true;
            MovementLoop();
        }

        public void MovementLoop()
        {
            if (!pickup.isHeld)
            {
                movementLoopIsRunning = false;
                return;
            }
            entity.FlagForMovement();
            SendCustomEventDelayedSeconds(nameof(MovementLoop), 0.1f);
        }
    }
}
