﻿
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

// TODO: fixed positions
// TODO: disallowed item theft support

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class JanItemSync : UdonSharpBehaviour
{
    private const bool IsDebug = true;

    // set on Start
    private UpdateManager updateManager;
    private VRC_Pickup pickup;
    // NOTE: VRCPlayerApi.GetBoneTransform is not exposed so we have to use a dummy transform and teleport it around
    // because InverseTransformDirection and TransformDirection require an instance of a Transform
    private Transform dummyTransform;

    private const byte IdleState = 0; // the only state with CustomUpdate deregistered
    private const byte VRWaitingForSmallDiffState = 1;
    private const byte VRSendingState = 2; // attached to hand
    private const byte DesktopWaitingForHandToMoveState = 3;
    private const byte DesktopWaitingForHandToStopMovingState = 4;
    private const byte DesktopSendingState = 5; // attached to hand
    private const byte ReceivingFloatingState = 6;
    private const byte ReceivingAttachedState = 7; // attached to hand
    private byte state = IdleState;
    private byte State
    {
        get => state;
        set
        {
            if (state != value)
            {
                if (IsDebug)
                    Debug.Log($"Switching from {StateToString(state)} to {StateToString(value)}.");
                if (value == IdleState)
                    updateManager.Deregister(this);
                else if (state == IdleState)
                    updateManager.Register(this);
                state = value;
            }
        }
    }
    private string StateToString(byte state)
    {
        switch (state)
        {
            case IdleState:
                return "IdleState";
            case VRWaitingForSmallDiffState:
                return "VRWaitingForSmallDiffState";
            case VRSendingState:
                return "VRSendingState";
            case DesktopWaitingForHandToMoveState:
                return "DesktopWaitingForHandToMoveState";
            case DesktopWaitingForHandToStopMovingState:
                return "DesktopWaitingForHandToStopMovingState";
            case DesktopSendingState:
                return "DesktopSendingState";
            case ReceivingFloatingState:
                return "ReceivingFloatingState";
            case ReceivingAttachedState:
                return "ReceivingAttachedState";
            default:
                return "InvalidState";
        }
    }

    ///<summary>
    ///First bit being 1 indicates the item is attached.
    ///Second bit is used when attached, 0 means attached to right hand, 1 means left hand.
    ///</summary>
    [UdonSynced] private byte syncedFlags;
    [UdonSynced] private Vector3 syncedPosition;
    [UdonSynced] private Quaternion syncedRotation;
    // 29 bytes (1 + 12 + 16) worth of data, and we get 84 bytes as the byte count in OnPostSerialization. I'll leave it at that

    // attachment data for both sending and receiving
    private VRCPlayerApi attachedPlayer;
    private HumanBodyBones attachedBone;
    private Vector3 attachedLocalOffset;
    private Quaternion attachedRotationOffset;

    // VRWaitingForSmallDiffState
    private const float SmallMagnitudeDiff = 0.075f;
    private const float SmallAngleDiff = 10f;
    private const float SmallDiffDuration = 0.1f;
    private float prevPositionOffsetMagnitude;
    private Quaternion prevRotationOffset;
    private float smallDiffStartTime;

    // DesktopWaitingForHandToMoveState
    private const float HandMovementAngleDiff = 20f;
    private Quaternion initialBoneRotation;

    // VRWaitingForSmallDiffState
    private const float DesktopSmallMagnitudeDiff = 0.075f;
    private const float DesktopSmallAngleDiff = 10f;
    private const float DesktopSmallDiffDuration = 0.1f;
    // reused from VRWaitingForSmallDiffState
    // private float prevPositionOffsetMagnitude;
    // private Quaternion prevRotationOffset;
    // private float smallDiffStartTime;

    // ReceivingFloatingState
    private const float InterpolationDuration = 0.2f;
    private Vector3 posInterpolationDiff;
    private Quaternion interpolationStartRotation;
    private float interpolationStartTime;

    // // state tracking to determine when the player and item held still for long enough to really determine the attached offset
    // private const float ExpectedStillFrameCount = 5;
    // private const float ExpectedLongerStillFrameCount = 20;
    // private const float MagnitudeTolerance = 0.075f;
    // private const float IntolerableMagnitudeDiff = 0.15f;
    // private const float IntolerableAngleDiff = 30f;
    // private const float FadeInTime = 2f; // seconds of manual position syncing before attaching
    // private float stillFrameCount;
    // private Vector3 prevBonePos;
    // private Quaternion prevBoneRotation;
    // private Vector3 prevItemPos;
    // private Quaternion prevItemRotation;

    // for the update manager
    private int customUpdateInternalIndex;

    // properties for my laziness
    private Vector3 ItemPosition => this.transform.position;
    private Quaternion ItemRotation => this.transform.rotation;
    private Vector3 AttachedBonePosition => attachedPlayer.GetBonePosition(attachedBone);
    private Quaternion AttachedBoneRotation => attachedPlayer.GetBoneRotation(attachedBone);

    private void Start()
    {
        pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        Debug.Assert(pickup != null, "JanItemSync must be on a GameObject with a VRC_Pickup component.");
        var updateManagerObj = GameObject.Find("/UpdateManager");
        updateManager = updateManagerObj == null ? null : (UpdateManager)updateManagerObj.GetComponent(typeof(UdonBehaviour));
        Debug.Assert(updateManager != null, "JanItemSync requires a GameObject that must be at the root of the scene with the exact name 'UpdateManager' which has the 'UpdateManager' UdonBehaviour.");
        dummyTransform = updateManagerObj.transform;
        if (IsDebug)
            ((MeshRenderer)dummyTransform.GetComponent(typeof(MeshRenderer))).enabled = true;
    }

    private void MoveDummyToBone() => dummyTransform.SetPositionAndRotation(AttachedBonePosition, AttachedBoneRotation);
    private Vector3 GetLocalPositionToBone(Vector3 worldPosition) => dummyTransform.InverseTransformDirection(worldPosition - dummyTransform.position);
    private Quaternion GetLocalRotationToBone(Quaternion worldRotation) => Quaternion.Inverse(dummyTransform.rotation) * worldRotation;

    public override void OnPickup()
    {
        Debug.Assert(pickup.IsHeld, "Picked up but not held?!");
        Debug.Assert(pickup.currentHand != VRC_Pickup.PickupHand.None, "Held but not in either hand?!");

        attachedPlayer = pickup.currentPlayer;
        attachedBone = pickup.currentHand == VRC_Pickup.PickupHand.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;

        // technically redundant because the VRCPickup script already does this, but I do not trust it nor do I trust order of operation
        Networking.SetOwner(attachedPlayer, this.gameObject);

        if (attachedPlayer.IsUserInVR())
        {
            prevPositionOffsetMagnitude = GetLocalPositionToBone(ItemPosition).magnitude;
            prevRotationOffset = GetLocalRotationToBone(ItemRotation);
            State = VRWaitingForSmallDiffState;
            smallDiffStartTime = Time.time;
        }
        else
        {
            initialBoneRotation = AttachedBoneRotation;
            State = DesktopWaitingForHandToMoveState;
        }
    }

    public override void OnDrop()
    {
        // if we already switched to receiving state before this player dropped this item don't do anything
        if (State == ReceivingAttachedState || State == ReceivingFloatingState)
            return;
        State = IdleState;
        SendChanges();
    }

    public void CustomUpdate()
    {
        if (State == IdleState)
        {
            Debug.LogError($"It should truly be impossible for CustomUpdate to run when an item is in IdleState. Item name: ${this.name}.");
            return;
        }
        if (State == ReceivingAttachedState || State == ReceivingFloatingState)
            UpdateReceiver();
        else
        {
            UpdateSender();
            if (IsDebug)
            {
                if (State == VRSendingState || State == DesktopSendingState)
                {
                    MoveDummyToBone();
                    dummyTransform.SetPositionAndRotation(
                        AttachedBonePosition + dummyTransform.TransformDirection(attachedLocalOffset),
                        AttachedBoneRotation * attachedRotationOffset
                    );
                }
                else
                {
                    dummyTransform.SetPositionAndRotation(ItemPosition, ItemRotation);
                }
            }
        }
    }

    ///cSpell:ignore jank
    // alright, so I've decided. For now I'm going to ignore theft and simply declare it undefined
    // and even if/once I handle item theft I'm not going to use VRCPickup for it, I'm going to check if it's allowed
    // and the prevent theft myself, because I have no interest in quite literally telling every client that that player picked up an item
    // because I can already see just how jank and hard it would be to synchronize local position and rotation. It would be pure pain
    // that means, however, we need to know if it is possible to disable a pickup script temporarily
    // I'll have to figure that out once this script has been fully refactored and tested

    private void UpdateSender()
    {
        if (State == VRSendingState || State == DesktopSendingState)
        {
            // I think this part still has to make sure the offset is about right, but we'll see
            // it'll definitely have to sync rotation in desktop mode, not sure if that's possible in VR
            // if (IsDebug)
            // {
            //     // fetch values
            //     var bonePos2 = attachedPlayer.GetBonePosition(attachedBone);
            //     var boneRotation2 = attachedPlayer.GetBoneRotation(attachedBone);

            //     // move some transform to match the bone, because the TransformDirection methods require an instance of a Transform
            //     dummyTransform.SetPositionAndRotation(bonePos2, boneRotation2);
            //     dummyTransform.SetPositionAndRotation(
            //         bonePos2 + dummyTransform.TransformDirection(attachedLocalOffset),
            //         boneRotation2 * attachedRotationOffset
            //     );
            // }
            return;
        }

        MoveDummyToBone();

        if (State == VRWaitingForSmallDiffState)
        {
            var posOffset = GetLocalPositionToBone(ItemPosition);
            var posOffsetMagnitude = posOffset.magnitude;
            var rotOffset = GetLocalRotationToBone(ItemRotation);
            if (Mathf.Abs(posOffsetMagnitude - prevPositionOffsetMagnitude) <= SmallMagnitudeDiff
                && Quaternion.Angle(rotOffset, prevRotationOffset) <= SmallAngleDiff)
            {
                if (smallDiffStartTime <= SmallDiffDuration)
                {
                    attachedLocalOffset = posOffset;
                    attachedRotationOffset = rotOffset;
                    State = VRSendingState;
                }
            }
            else
                smallDiffStartTime = Time.time;

            prevPositionOffsetMagnitude = posOffsetMagnitude;
            prevRotationOffset = rotOffset;
        }
        else
        {
            if (State == DesktopWaitingForHandToMoveState)
            {
                if (Quaternion.Angle(AttachedBoneRotation, initialBoneRotation) > HandMovementAngleDiff)
                {
                    prevPositionOffsetMagnitude = GetLocalPositionToBone(ItemPosition).magnitude;
                    prevRotationOffset = GetLocalRotationToBone(ItemRotation);
                    State = DesktopWaitingForHandToStopMovingState;
                    smallDiffStartTime = Time.time;
                }
            }
            else
            {
                var posOffset = GetLocalPositionToBone(ItemPosition);
                var posOffsetMagnitude = posOffset.magnitude;
                var rotOffset = GetLocalRotationToBone(ItemRotation);
                if (Mathf.Abs(posOffsetMagnitude - prevPositionOffsetMagnitude) <= DesktopSmallMagnitudeDiff
                    && Quaternion.Angle(rotOffset, prevRotationOffset) <= DesktopSmallAngleDiff)
                {
                    if (smallDiffStartTime <= DesktopSmallDiffDuration)
                    {
                        attachedLocalOffset = posOffset;
                        attachedRotationOffset = rotOffset;
                        State = DesktopSendingState;
                    }
                }
                else
                    smallDiffStartTime = Time.time;

                prevPositionOffsetMagnitude = posOffsetMagnitude;
                prevRotationOffset = rotOffset;
            }
        }
        SendChanges(); // regardless of what happened, it has to sync
    }

    private void UpdateReceiver()
    {
        // prevent this object from being moved by this logic if the local player is holding it
        // we might not have gotten the OnPickup event before the (this) Update event yet
        // not sure if that's even possible, but just in case
        if (pickup.IsHeld)
            return;

        if (State == ReceivingAttachedState)
        {
            // fetch values
            var bonePos = AttachedBonePosition;
            var boneRotation = AttachedBoneRotation;

            // move some transform to match the bone, because the TransformDirection methods
            // require an instance of a Transform and we can't get the bone's Transform directly
            this.transform.SetPositionAndRotation(bonePos, boneRotation);
            this.transform.SetPositionAndRotation(
                bonePos + this.transform.TransformDirection(attachedLocalOffset),
                boneRotation * attachedRotationOffset
            );
        }
        else
        {
            var percent = (Time.time - interpolationStartTime) / InterpolationDuration;
            if (percent >= 1f)
            {
                this.transform.SetPositionAndRotation(syncedPosition, syncedRotation);
                State = IdleState;
            }
            else
            {
                this.transform.SetPositionAndRotation(
                    syncedPosition - posInterpolationDiff * (1f - percent),
                    Quaternion.Lerp(interpolationStartRotation, syncedRotation, percent)
                );
            }
        }

        if (IsDebug)
        {
            dummyTransform.SetPositionAndRotation(this.transform.position, this.transform.rotation);
        }
    }

    private void SendChanges()
    {
        RequestSerialization();
    }

    public override void OnPreSerialization()
    {
        if (State == ReceivingAttachedState || State == ReceivingFloatingState)
        {
            Debug.LogWarning("// TODO: uh idk what to do, shouldn't this be impossible?");
        }
        syncedFlags = 0;
        if (State == VRSendingState || State == DesktopSendingState)
        {
            syncedFlags += 1; // set attached flag
            syncedPosition = attachedLocalOffset;
            syncedRotation = attachedRotationOffset;
            if (attachedBone == HumanBodyBones.LeftHand)
                syncedFlags += 2; // set left hand flag, otherwise it's right hand
        }
        else
        {
            // not attached, don't set the attached flag and just sync current position and rotation
            syncedPosition = this.transform.position;
            syncedRotation = this.transform.rotation;
        }
    }

    public override void OnPostSerialization(SerializationResult result)
    {
        if (!result.success)
        {
            Debug.LogWarning($"Syncing request was dropped for {this.name}, trying again.");
            SendChanges(); // TODO: somehow test if this kind of retry even works or if the serialization request got reset right afterwards
        }
        // else
        //     Debug.Log($"Sending {result.byteCount} bytes");
    }

    public override void OnDeserialization()
    {
        if ((syncedFlags & 1) != 0) // is attached?
        {
            attachedBone = (syncedFlags & 2) != 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            attachedLocalOffset = syncedPosition;
            attachedRotationOffset = syncedRotation;
            attachedPlayer = Networking.GetOwner(this.gameObject);
            State = ReceivingAttachedState;
        }
        else // not attached
        {
            posInterpolationDiff = syncedPosition - this.transform.position;
            interpolationStartRotation = this.transform.rotation;
            interpolationStartTime = Time.time;
            State = ReceivingFloatingState;
        }
    }
}
