﻿using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace JanSharp
{
    // TODO: maybe rigidbody.MovePosition for interpolation. Also note that setting position my take effect 1
    // frame delayed, or it is just a jittering issue while the item is actually held (which is why
    // UpdateSender is not writing to rb.position)

    [RequireComponent(typeof(VRC.SDK3.Components.VRCPickup))]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemSync : UdonSharpBehaviour
    {
        [System.NonSerialized] public ItemSystem itemSystem;
        [System.NonSerialized] public VRCPlayerApi localPlayer;
        [System.NonSerialized] public int localPlayerId;

        [System.NonSerialized] public uint id;
        private int holdingPlayerId = -1;
        private bool isLeftHand;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private bool isVisible = true;
        // TODO: pickup.IsHeld null ref exception
        public bool LocalPlayerIsInControl => pickup.IsHeld || (holdingPlayerId == -1 && itemSystem.lockstep.IsMaster);

        public int HoldingPlayerId => holdingPlayerId;

        public void SetHoldingPlayer(int playerId, bool isLeftHand)
        {
            if (playerId == holdingPlayerId && isLeftHand == this.isLeftHand)
                return;
            holdingPlayerId = playerId;
            this.isLeftHand = isLeftHand;
            attachedBone = isLeftHand
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand;
            attachedTracking = isLeftHand
                ? VRCPlayerApi.TrackingDataType.LeftHand
                : VRCPlayerApi.TrackingDataType.RightHand;

            if (holdingPlayerId == localPlayerId) // Confirmed, game state now matches latency state.
                return;
            attachedPlayer = VRCPlayerApi.GetPlayerById(holdingPlayerId);
            LocalState = IdleState;
        }

        public void UnsetHoldingPlayer(Vector3 droppedPosition, Quaternion droppedRotation)
        {
            holdingPlayerId = -1;
            SetFloatingPosition(droppedPosition, droppedRotation);
        }

        public void Disable()
        {
            isVisible = false;
            holdingPlayerId = -1;
            LocalState = IdleState;
            pickup.Drop();
            this.gameObject.SetActive(false);
        }

        public void Enable()
        {
            isVisible = true;
            this.gameObject.SetActive(true);
        }

        #if ItemSyncDebug
        [HideInInspector] public int debugIndex;
        [HideInInspector] public int debugNonIdleIndex;
        [SerializeField] [HideInInspector] private ItemSyncDebugController debugController;
        #endif

        [System.NonSerialized] public VRC_Pickup pickup;
        [System.NonSerialized] public Rigidbody rb;


        private Vector3 parentPos;
        private Quaternion parentRot;
        private Vector3 worldPos;
        private Quaternion worldRot;
        private Vector3 localPos;
        private Quaternion localRot;

        private void WorldToLocal()
        {
            Quaternion inverseParentRot = Quaternion.Inverse(parentRot);
            localPos = inverseParentRot * (worldPos - parentPos);
            localRot = inverseParentRot * worldRot;
        }

        private void LocalToWorld()
        {
            worldPos = parentPos + (parentRot * localPos);
            worldRot = parentRot * localRot;
        }

        ///<summary>Does nothing when returning false, otherwise sets parentPos and parentRot.</summary>
        private bool TryLoadHandPos()
        {
            if (!isAttachedPlayerValid)
                return false;
            VRCPlayerApi player = attachedPlayer;
            HumanBodyBones bone = attachedBone;
            parentPos = player.GetBonePosition(bone);
            if (parentPos != Vector3.zero)
            {
                parentRot = player.GetBoneRotation(bone);
                return true;
            }
            var trackingData = player.GetTrackingData(attachedTracking);
            parentPos = trackingData.position;
            parentRot = trackingData.rotation;
            return true;
        }


        private const byte IdleState = 0; // the only state with CustomUpdate deregistered
        private const byte VRWaitingForConsistentOffsetState = 1;
        private const byte VRAttachedSendingState = 2; // attached to hand
        private const byte DesktopWaitingForConsistentOffsetState = 3;
        private const byte DesktopAttachedSendingState = 4; // attached to hand
        private const byte DesktopAttachedRotatingState = 5; // attached to hand
        private const byte ExactAttachedSendingState = 6; // attached to hand
        private const byte ReceivingFloatingState = 7;
        private const byte ReceivingMovingToBoneState = 8; // attached to hand, but interpolating offset towards the actual attached position
        private const byte ReceivingAttachedState = 9; // attached to hand
        private byte localState = IdleState;
        #if ItemSyncDebug
        public
        #else
        private
        #endif
        byte LocalState
        {
            get => localState;
            set
            {
                if (localState != value)
                {
                    #if ItemSyncDebug
                    Debug.Log($"[ItemSystemDebug] Switching from {StateToString(localState)} to {StateToString(value)}.");
                    if (debugController != null)
                    {
                        if (localState == IdleState)
                            debugController.RegisterNonIdle(this);
                        else if (value == IdleState)
                            debugController.DeregisterNonIdle(this);
                    }
                    #endif
                    if (localState == IdleState)
                        itemSystem.MarkAsActive(this);
                    else if (value == IdleState)
                        itemSystem.MarkAsInactive(this);
                    localState = value;
                    #if ItemSyncDebug
                    if (debugController != null)
                        debugController.UpdateItemStatesText();
                    #endif
                }
            }
        }
        #if ItemSyncDebug
        public
        #else
        private
        #endif
        string StateToString(byte state)
        {
            switch (state)
            {
                case IdleState:
                    return "IdleState";
                case VRWaitingForConsistentOffsetState:
                    return "VRWaitingForConsistentOffsetState";
                case VRAttachedSendingState:
                    return "VRAttachedSendingState";
                case DesktopWaitingForConsistentOffsetState:
                    return "DesktopWaitingForConsistentOffsetState";
                case DesktopAttachedSendingState:
                    return "DesktopAttachedSendingState";
                case DesktopAttachedRotatingState:
                    return "DesktopAttachedRotatingState";
                case ExactAttachedSendingState:
                    return "ExactAttachedSendingState";
                case ReceivingFloatingState:
                    return "ReceivingFloatingState";
                case ReceivingMovingToBoneState:
                    return "ReceivingMovingToHandState";
                case ReceivingAttachedState:
                    return "ReceivingAttachedState";
                default:
                    return "InvalidState";
            }
        }

        public void SetFloatingPosition(Vector3 position, Quaternion rotation)
        {
            targetPosition = position;
            targetRotation = rotation;
            if (holdingPlayerId == localPlayerId)
                return; // The local player is in latency state and sending, ignore the game state.
            posInterpolationDiff = targetPosition - ItemPosition;
            interpolationStartRotation = ItemRotation;
            interpolationStartTime = Time.time;
            LocalState = ReceivingFloatingState;
        }

        public void SetAttached(Vector3 position, Quaternion rotation)
        {
            targetPosition = position;
            targetRotation = rotation;
            if (holdingPlayerId == localPlayerId)
                return; // The local player is in latency state and sending, ignore the game state.

            if (LocalState == ReceivingAttachedState) // interpolate from old to new offset
            {
                posInterpolationDiff = position - attachedLocalOffset;
                interpolationStartRotation = attachedRotationOffset;
            }
            else if (TryLoadHandPos()) // figure out current local offset and interpolate starting from there
            {
                worldPos = ItemPosition;
                worldRot = ItemRotation;
                WorldToLocal();
                posInterpolationDiff = position - localPos;
                interpolationStartRotation = localRot;
            }
            attachedLocalOffset = position;
            attachedRotationOffset = rotation;
            interpolationStartTime = Time.time;
            LocalState = ReceivingMovingToBoneState;
        }

        // attachment data for both sending and receiving
        private VRCPlayerApi attachedPlayer;
        private bool isAttachedPlayerValid => attachedPlayer != null && attachedPlayer.IsValid();
        private HumanBodyBones attachedBone;
        private VRCPlayerApi.TrackingDataType attachedTracking;
        private Vector3 attachedLocalOffset;
        private Quaternion attachedRotationOffset;

        // local attachment means that the item will also be attached to the hand for the player holding the item
        // once the local position and rotation offset has been determined.
        // this ultimately solves the issue that the offset determination will never be perfect, but
        // by locally attaching it will still look the same for everyone including the person holding the item
        #if ItemSyncDebug
        [HideInInspector] public bool VRLocalAttachment = true; // asdf
        [HideInInspector] public bool DesktopLocalAttachment = true; // asdf
        #else
        private const bool VRLocalAttachment = true;
        private const bool DesktopLocalAttachment = true;
        #endif

        // VRWaitingForConsistentOffsetState and DesktopWaitingForConsistentOffsetState
        #if ItemSyncDebug
        [HideInInspector] public float SmallMagnitudeDiff = 0.01f; // asdf
        [HideInInspector] public float SmallAngleDiff = 7f; // asdf
        [HideInInspector] public float ConsistentOffsetDuration = 0.2f; // asdf
        [HideInInspector] public int ConsistentOffsetFrameCount = 4; // asdf
        #else
        private const float SmallMagnitudeDiff = 0.01f;
        private const float SmallAngleDiff = 7f;
        private const float ConsistentOffsetDuration = 0.2f;
        private const int ConsistentOffsetFrameCount = 4;
        #endif
        private Vector3 prevPositionOffset;
        private Quaternion prevRotationOffset;
        private float consistentOffsetStopTime;
        private int stillFrameCount; // to prevent super low framerate from causing false positives

        // DesktopAttachedSendingState and DesktopAttachedRotatingState
        #if ItemSyncDebug
        [HideInInspector] public float DesktopRotationCheckInterval = 1f; // asdf
        [HideInInspector] public float DesktopRotationCheckFastInterval = 0.15f; // asdf
        [HideInInspector] public float DesktopRotationTolerance = 3f; // asdf
        /// <summary>
        /// Amount of fast checks where the rotation didn't change before going back to the slower interval
        /// </summary>
        [HideInInspector] public int DesktopRotationFastFalloff = 10; // asdf
        #else
        private const float DesktopRotationCheckInterval = 1f;
        private const float DesktopRotationCheckFastInterval = 0.15f;
        private const float DesktopRotationTolerance = 3f;
        private const int DesktopRotationFastFalloff = 10;
        #endif
        private float nextRotationCheckTime;
        private float slowDownTime;

        // ExactAttachedSendingState
        // NOTE: these really should be static fields, but UdonSharp 0.20.3 does not support them
        private Quaternion gripRotationOffset = Quaternion.Euler(0, 35, 0);
        private Quaternion gunRotationOffset = Quaternion.Euler(0, 305, 0);

        // ReceivingFloatingState and AttachedInterpolationState
        #if ItemSyncDebug
        [HideInInspector] public float InterpolationDuration = 0.2f; // asdf
        #else
        private const float InterpolationDuration = 0.2f;
        #endif
        private Vector3 posInterpolationDiff;
        private Quaternion interpolationStartRotation;
        private float interpolationStartTime;

        [System.NonSerialized] public int activeIndex = -1;

        // properties for my laziness
        private Vector3 ItemPosition => this.transform.position;
        private Quaternion ItemRotation => this.transform.rotation;

        #if ItemSyncDebug
        private void Start()
        {
            if (debugController != null)
                debugController.Register(this);
        }
        #endif

        public void Despawn()
        {
            Debug.Log($"[ItemSystem] ItemSync  Despawn  itemId: {this.id}");
            itemSystem.SendDespawnItemIA(this.id);
        }

        private Vector3 GetLocalPositionToTransform(Transform transform, Vector3 worldPosition)
            => transform.InverseTransformDirection(worldPosition - transform.position);
        private Quaternion GetLocalRotationToTransform(Transform transform, Quaternion worldRotation)
            => Quaternion.Inverse(transform.rotation) * worldRotation;
        private bool IsReceivingState() => LocalState >= ReceivingFloatingState;
        private bool IsAttachedSendingState()
            => LocalState == VRAttachedSendingState
            || LocalState == DesktopAttachedSendingState
            || LocalState == DesktopAttachedRotatingState
            || LocalState == ExactAttachedSendingState;

        public override void OnPickup()
        {
            Debug.Log($"[ItemSystem] ItemSync  OnPickup  itemId: {this.id}");
            if (!pickup.IsHeld)
            {
                Debug.LogError("[ItemSystem] Picked up but not held?!", this);
                return;
            }
            if (pickup.currentHand == VRC_Pickup.PickupHand.None)
            {
                Debug.LogError("[ItemSystem] Held but not in either hand?!", this);
                return;
            }

            attachedPlayer = localPlayer;
            var currentHand = pickup.currentHand;
            attachedBone = currentHand == VRC_Pickup.PickupHand.Left
                ? HumanBodyBones.LeftHand
                : HumanBodyBones.RightHand;
            attachedTracking = currentHand == VRC_Pickup.PickupHand.Left
                ? VRCPlayerApi.TrackingDataType.LeftHand
                : VRCPlayerApi.TrackingDataType.RightHand;

            itemSystem.SendPickupIA(id, localPlayerId, currentHand == VRC_Pickup.PickupHand.Left);

            // if (pickup.orientation == VRC_Pickup.PickupOrientation.Gun)
            // {
            //     if (HandleExactOffset(pickup.ExactGun, gunRotationOffset))
            //         return;
            // }
            // else if (pickup.orientation == VRC_Pickup.PickupOrientation.Grip)
            // {
            //     if (HandleExactOffset(pickup.ExactGrip, gripRotationOffset))
            //         return;
            // }

            if (!TryLoadHandPos())
                return;
            worldPos = ItemPosition;
            worldRot = ItemRotation;
            WorldToLocal();
            if (attachedPlayer.IsUserInVR())
            {
                prevPositionOffset = localPos;
                prevRotationOffset = localRot;
                stillFrameCount = 0;
                LocalState = VRWaitingForConsistentOffsetState;
                consistentOffsetStopTime = Time.time + ConsistentOffsetDuration;
            }
            else
            {
                prevPositionOffset = localPos;
                prevRotationOffset = localRot;
                stillFrameCount = 0;
                LocalState = DesktopWaitingForConsistentOffsetState;
                consistentOffsetStopTime = Time.time + ConsistentOffsetDuration;
            }
        }

        private bool HandleExactOffset(Transform exact, Quaternion rotationOffset)
        {
            if (exact == null)
                return false;
            // figure out offset from exact transform to center of object
            // this is pretty much copied from CyanEmu, except it doesn't work
            // either I'm stupid and too tired to see it or - what I actually believe - I have to do it differently
            // what's great is that there is basically zero documentation about manipulation of quaternions. Yay!
            // TODO: fix exact offsets
            attachedRotationOffset = rotationOffset * Quaternion.Inverse(GetLocalRotationToTransform(exact, ItemRotation));
            attachedLocalOffset = attachedRotationOffset * GetLocalPositionToTransform(exact, ItemPosition);
            SendFloatingData();
            LocalState = ExactAttachedSendingState;
            return true;
        }

        public override void OnDrop()
        {
            Debug.Log($"[ItemSystem] ItemSync  OnDrop  itemId: {this.id}");
            if (!isVisible)
                return;
            if (!IsReceivingState()) // Doing an if check just for better latency state handling.
                LocalState = IdleState;
            itemSystem.SendDropIA(id, holdingPlayerId, transform.position, transform.rotation);
        }

        public void UpdateActiveItem()
        {
            if (LocalState == IdleState)
            {
                Debug.LogError($"[ItemSystem] It should truly be impossible for CustomUpdate to run when an item is in IdleState. Item name: ${this.name}.", this);
                return;
            }
            if (IsReceivingState())
                UpdateReceiver();
            else
                UpdateSender();
        }

        private bool ItemOffsetWasConsistent()
        {
            worldPos = ItemPosition;
            worldRot = ItemRotation;
            WorldToLocal();
            #if ItemSyncDebug
            Debug.Log($"[ItemSystemDebug] WaitingForConsistentOffsetState: offset diff: {localPos - prevPositionOffset}, "
                + $"offset diff magnitude {(localPos - prevPositionOffset).magnitude}, "
                + $"angle diff: {Quaternion.Angle(localRot, prevRotationOffset)}.");
            #endif
            if ((localPos - prevPositionOffset).magnitude <= SmallMagnitudeDiff
                && Quaternion.Angle(localRot, prevRotationOffset) <= SmallAngleDiff)
            {
                stillFrameCount++;
                #if ItemSyncDebug
                Debug.Log($"[ItemSystemDebug] stillFrameCount: {stillFrameCount}, Time.time: {Time.time}, stop time: {consistentOffsetStopTime}.");
                #endif
                if (stillFrameCount >= ConsistentOffsetFrameCount && Time.time >= consistentOffsetStopTime)
                {
                    #if ItemSyncDebug
                    Debug.Log("[ItemSystemDebug] Setting attached offset.");
                    #endif
                    attachedLocalOffset = localPos;
                    attachedRotationOffset = localRot;
                    return true;
                }
            }
            else
            {
                #if ItemSyncDebug
                Debug.Log("[ItemSystemDebug] Moved too much, resetting timer.");
                #endif
                stillFrameCount = 0;
                consistentOffsetStopTime = Time.time + ConsistentOffsetDuration;
            }

            prevPositionOffset = localPos;
            prevRotationOffset = localRot;
            return false;
        }

        private void UpdateSender()
        {
            if (LocalState == VRAttachedSendingState)
            {
                if (VRLocalAttachment)
                    MoveItemToBoneWithOffset(attachedLocalOffset, attachedRotationOffset);
                return;
            }
            if (LocalState == DesktopAttachedSendingState || LocalState == DesktopAttachedRotatingState)
            {
                if (!TryLoadHandPos())
                    return;
                if (DesktopLocalAttachment)
                {
                    // only set position, because you can rotate items on desktop
                    localPos = attachedLocalOffset;
                    LocalToWorld();
                    this.transform.position = worldPos;
                }
                // sync item rotation
                float time = Time.time;
                if (time >= nextRotationCheckTime)
                {
                    worldRot = ItemRotation;
                    WorldToLocal();
                    if (Quaternion.Angle(attachedRotationOffset, localRot) > DesktopRotationTolerance)
                    {
                        LocalState = DesktopAttachedRotatingState;
                        slowDownTime = nextRotationCheckTime + DesktopRotationCheckFastInterval * DesktopRotationFastFalloff;
                        attachedRotationOffset = localRot;
                        SendAttachedData();
                    }
                    else if (time >= slowDownTime)
                    {
                        LocalState = DesktopAttachedSendingState;
                        // SendAttachedData(); // TODO: Should this be here? Or just not at all? I don't think it should.
                    }
                    nextRotationCheckTime += LocalState == DesktopAttachedRotatingState ? DesktopRotationCheckFastInterval : DesktopRotationCheckInterval;
                }
                return;
            }
            if (LocalState == ExactAttachedSendingState)
                return;

            bool success = TryLoadHandPos();

            if (LocalState == VRWaitingForConsistentOffsetState && success)
            {
                if (ItemOffsetWasConsistent())
                {
                    LocalState = VRAttachedSendingState;
                    SendAttachedData();
                    return;
                }
            }
            else if (success)
            {
                if (ItemOffsetWasConsistent())
                {
                    LocalState = DesktopAttachedSendingState;
                    nextRotationCheckTime = Time.time + DesktopRotationCheckInterval;
                    SendAttachedData();
                    return;
                }
            }

            SendFloatingData(rateLimited: true); // otherwise it's still floating
        }

        private void MoveItemToBoneWithOffset(Vector3 offset, Quaternion rotationOffset)
        {
            if (!TryLoadHandPos())
                return;
            localPos = offset;
            localRot = rotationOffset;
            LocalToWorld();
            rb.position = worldPos;
            rb.rotation = worldRot;
        }

        private void UpdateReceiver()
        {
            // prevent this object from being moved by this logic if the local player is holding it
            // we might not have gotten the OnPickup event before the (this) Update event yet
            // not sure if that's even possible, but just in case
            if (pickup.IsHeld)
                return;

            if (LocalState == ReceivingAttachedState)
                MoveItemToBoneWithOffset(targetPosition, targetRotation);
            else
            {
                var percent = (Time.time - interpolationStartTime) / InterpolationDuration;
                if (LocalState == ReceivingFloatingState)
                {
                    if (percent >= 1f)
                    {
                        rb.position = targetPosition;
                        rb.rotation = targetRotation;
                        LocalState = IdleState;
                    }
                    else
                    {
                        rb.position = targetPosition - posInterpolationDiff * (1f - percent);
                        rb.rotation = Quaternion.Lerp(interpolationStartRotation, targetRotation, percent);
                    }
                }
                else // ReceivingMovingToBoneState
                {
                    if (percent >= 1f)
                    {
                        MoveItemToBoneWithOffset(targetPosition, targetRotation);
                        LocalState = ReceivingAttachedState;
                    }
                    else
                    {
                        MoveItemToBoneWithOffset(
                            targetPosition - posInterpolationDiff * (1f - percent),
                            Quaternion.Lerp(interpolationStartRotation, targetRotation, percent)
                        );
                    }
                }
            }
        }

        private float nextFloatingSendTime = -1f;

        private void SendFloatingData(bool rateLimited = false)
        {
            if (rateLimited)
            {
                if (Time.time < nextFloatingSendTime)
                    return;
                nextFloatingSendTime = Time.time + 0.2f;
            }

            itemSystem.SendFloatingPositionIA(id, transform.position, transform.rotation);
        }

        private void SendAttachedData()
        {
            itemSystem.SendAttachIA(id, attachedLocalOffset, attachedRotationOffset);
        }
    }
}
