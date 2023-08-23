using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace JanSharp
{
    [RequireComponent(typeof(VRC.SDK3.Components.VRCPickup))]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ItemSync : UdonSharpBehaviour
    {
        #if ItemSyncDebug
        [HideInInspector] public int debugIndex;
        [HideInInspector] public int debugNonIdleIndex;
        [SerializeField] [HideInInspector] private ItemSyncDebugController debugController;
        #endif

        // set OnBuild
        [SerializeField] [HideInInspector] private UpdateManager updateManager;
        [SerializeField] [HideInInspector] private VRC_Pickup pickup;
        // NOTE: VRCPlayerApi.GetBoneTransform is not exposed so we have to use a dummy transform and teleport it around
        // because InverseTransformDirection and TransformDirection require an instance of a Transform
        [SerializeField] [HideInInspector] private Transform dummyTransform;

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
        private byte state = IdleState;
        #if ItemSyncDebug
        public
        #else
        private
        #endif
        byte State
        {
            get => state;
            set
            {
                if (state != value)
                {
                    #if ItemSyncDebug
                    Debug.Log($"Switching from {StateToString(state)} to {StateToString(value)}.");
                    if (debugController != null)
                    {
                        if (state == IdleState)
                            debugController.RegisterNonIdle(this);
                        else if (value == IdleState)
                            debugController.DeregisterNonIdle(this);
                    }
                    #endif
                    if (value == IdleState)
                        updateManager.Deregister(this);
                    else if (state == IdleState)
                        updateManager.Register(this);
                    state = value;
                    #if ItemSyncDebug
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

        ///<summary>
        ///First bit being 1 indicates the item is attached.
        ///Second bit is used when attached, 0 means attached to right hand, 1 means left hand.
        ///</summary>
        [UdonSynced] private byte syncedFlags;
        [UdonSynced] private Vector3 syncedPosition;
        [UdonSynced] private Quaternion syncedRotation;
        // 29 bytes (1 + 12 + 16) worth of data, and we get 48 bytes as the byte count in OnPostSerialization. I'll leave it at that

        // attachment data for both sending and receiving
        private VRCPlayerApi attachedPlayer;
        private HumanBodyBones attachedBone;
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

        // for the update manager
        private int customUpdateInternalIndex;

        // properties for my laziness
        private Vector3 ItemPosition => this.transform.position;
        private Quaternion ItemRotation => this.transform.rotation;
        private Vector3 AttachedBonePosition => attachedPlayer.GetBonePosition(attachedBone);
        private Quaternion AttachedBoneRotation => attachedPlayer.GetBoneRotation(attachedBone);

        #if ItemSyncDebug
        private void Start()
        {
            var renderer = dummyTransform.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = true;
            if (debugController != null)
                debugController.Register(this);
        }
        #endif

        private void MoveDummyToBone()
            => dummyTransform.SetPositionAndRotation(AttachedBonePosition, AttachedBoneRotation);
        private Vector3 GetLocalPositionToTransform(Transform transform, Vector3 worldPosition)
            => transform.InverseTransformDirection(worldPosition - transform.position);
        private Vector3 GetLocalPositionToBone(Vector3 worldPosition)
            => GetLocalPositionToTransform(dummyTransform, worldPosition);
        private Quaternion GetLocalRotationToTransform(Transform transform, Quaternion worldRotation)
            => Quaternion.Inverse(transform.rotation) * worldRotation;
        private Quaternion GetLocalRotationToBone(Quaternion worldRotation)
            => GetLocalRotationToTransform(dummyTransform, worldRotation);
        private bool IsReceivingState() => State >= ReceivingFloatingState;
        private bool IsAttachedSendingState()
            => State == VRAttachedSendingState
            || State == DesktopAttachedSendingState
            || State == DesktopAttachedRotatingState
            || State == ExactAttachedSendingState;

        public override void OnPickup()
        {
            if (!pickup.IsHeld)
            {
                Debug.LogError("Picked up but not held?!", this);
                return;
            }
            if (pickup.currentHand == VRC_Pickup.PickupHand.None)
            {
                Debug.LogError("Held but not in either hand?!", this);
                return;
            }

            attachedPlayer = pickup.currentPlayer;
            attachedBone = pickup.currentHand == VRC_Pickup.PickupHand.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;

            // technically redundant because the VRCPickup script already does this, but I do not trust it nor do I trust order of operation
            Networking.SetOwner(attachedPlayer, this.gameObject);

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

            if (attachedPlayer.IsUserInVR())
            {
                prevPositionOffset = GetLocalPositionToBone(ItemPosition);
                prevRotationOffset = GetLocalRotationToBone(ItemRotation);
                stillFrameCount = 0;
                State = VRWaitingForConsistentOffsetState;
                consistentOffsetStopTime = Time.time + ConsistentOffsetDuration;
            }
            else
            {
                prevPositionOffset = GetLocalPositionToBone(ItemPosition);
                prevRotationOffset = GetLocalRotationToBone(ItemRotation);
                stillFrameCount = 0;
                State = DesktopWaitingForConsistentOffsetState;
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
            SendChanges();
            State = ExactAttachedSendingState;
            return true;
        }

        public override void OnDrop()
        {
            // if we already switched to receiving state before this player dropped this item don't do anything
            if (IsReceivingState())
                return;
            State = IdleState;
            SendChanges();
            #if ItemSyncDebug
            dummyTransform.SetPositionAndRotation(ItemPosition, ItemRotation);
            #endif
        }

        public void CustomUpdate()
        {
            if (State == IdleState)
            {
                Debug.LogError($"It should truly be impossible for CustomUpdate to run when an item is in IdleState. Item name: ${this.name}.", this);
                return;
            }
            if (IsReceivingState())
                UpdateReceiver();
            else
            {
                UpdateSender();
                #if ItemSyncDebug
                if (IsAttachedSendingState())
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
                #endif
            }
        }

        private bool ItemOffsetWasConsistent()
        {
            var posOffset = GetLocalPositionToBone(ItemPosition);
            var rotOffset = GetLocalRotationToBone(ItemRotation);
            #if ItemSyncDebug
            Debug.Log($"*WaitingForConsistentOffsetState: offset diff: {posOffset - prevPositionOffset}, "
                + $"offset diff magnitude {(posOffset - prevPositionOffset).magnitude}, "
                + $"angle diff: {Quaternion.Angle(rotOffset, prevRotationOffset)}.");
            #endif
            if ((posOffset - prevPositionOffset).magnitude <= SmallMagnitudeDiff
                && Quaternion.Angle(rotOffset, prevRotationOffset) <= SmallAngleDiff)
            {
                stillFrameCount++;
                #if ItemSyncDebug
                Debug.Log($"stillFrameCount: {stillFrameCount}, Time.time: {Time.time}, stop time: {consistentOffsetStopTime}.");
                #endif
                if (stillFrameCount >= ConsistentOffsetFrameCount && Time.time >= consistentOffsetStopTime)
                {
                    #if ItemSyncDebug
                    Debug.Log("Setting attached offset.");
                    #endif
                    attachedLocalOffset = posOffset;
                    attachedRotationOffset = rotOffset;
                    return true;
                }
            }
            else
            {
                #if ItemSyncDebug
                Debug.Log("Moved too much, resetting timer.");
                #endif
                stillFrameCount = 0;
                consistentOffsetStopTime = Time.time + ConsistentOffsetDuration;
            }

            prevPositionOffset = posOffset;
            prevRotationOffset = rotOffset;
            return false;
        }

        private void UpdateSender()
        {
            if (State == VRAttachedSendingState)
            {
                if (VRLocalAttachment)
                    MoveItemToBoneWithOffset(attachedLocalOffset, attachedRotationOffset);
                return;
            }
            if (State == DesktopAttachedSendingState || State == DesktopAttachedRotatingState)
            {
                if (DesktopLocalAttachment)
                {
                    // only set position, because you can rotate items on desktop
                    MoveDummyToBone();
                    this.transform.position = AttachedBonePosition + dummyTransform.TransformDirection(attachedLocalOffset);
                }
                // sync item rotation
                float time = Time.time;
                if (time >= nextRotationCheckTime)
                {
                    MoveDummyToBone();
                    var rotOffset = GetLocalRotationToBone(ItemRotation);
                    if (Quaternion.Angle(attachedRotationOffset, rotOffset) > DesktopRotationTolerance)
                    {
                        State = DesktopAttachedRotatingState;
                        slowDownTime = nextRotationCheckTime + DesktopRotationCheckFastInterval * DesktopRotationFastFalloff;
                        attachedRotationOffset = rotOffset;
                        SendChanges();
                    }
                    else if (time >= slowDownTime)
                    {
                        State = DesktopAttachedSendingState;
                    }
                    nextRotationCheckTime += State == DesktopAttachedRotatingState ? DesktopRotationCheckFastInterval : DesktopRotationCheckInterval;
                }
                return;
            }
            if (State == ExactAttachedSendingState)
                return;

            MoveDummyToBone();

            if (State == VRWaitingForConsistentOffsetState)
            {
                if (ItemOffsetWasConsistent())
                    State = VRAttachedSendingState;
            }
            else
            {
                if (ItemOffsetWasConsistent())
                {
                    State = DesktopAttachedSendingState;
                    nextRotationCheckTime = Time.time + DesktopRotationCheckInterval;
                }
            }
            SendChanges(); // regardless of what happened, it has to sync
        }

        private void MoveItemToBoneWithOffset(Vector3 offset, Quaternion rotationOffset)
        {
            var bonePos = AttachedBonePosition;
            var boneRotation = AttachedBoneRotation;
            this.transform.SetPositionAndRotation(bonePos, boneRotation);
            this.transform.SetPositionAndRotation(
                bonePos + this.transform.TransformDirection(offset),
                boneRotation * rotationOffset
            );
        }

        private void UpdateReceiver()
        {
            // prevent this object from being moved by this logic if the local player is holding it
            // we might not have gotten the OnPickup event before the (this) Update event yet
            // not sure if that's even possible, but just in case
            if (pickup.IsHeld)
                return;

            if (State == ReceivingAttachedState)
                MoveItemToBoneWithOffset(attachedLocalOffset, attachedRotationOffset);
            else
            {
                var percent = (Time.time - interpolationStartTime) / InterpolationDuration;
                if (State == ReceivingFloatingState)
                {
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
                else
                {
                    if (percent >= 1f)
                    {
                        MoveItemToBoneWithOffset(attachedLocalOffset, attachedRotationOffset);
                        State = ReceivingAttachedState;
                    }
                    else
                    {
                        MoveItemToBoneWithOffset(
                            attachedLocalOffset - posInterpolationDiff * (1f - percent),
                            Quaternion.Lerp(interpolationStartRotation, attachedRotationOffset, percent)
                        );
                    }
                }
            }
        }

        private void SendChanges()
        {
            RequestSerialization();
        }

        public override void OnPreSerialization()
        {
            if (IsReceivingState())
            {
                Debug.LogWarning("// TODO: uh idk what to do, shouldn't this be impossible?", this);
            }
            syncedFlags = 0;
            if (IsAttachedSendingState())
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
                syncedPosition = ItemPosition;
                syncedRotation = ItemRotation;
            }
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (!result.success)
            {
                Debug.LogWarning($"Syncing request was dropped for {this.name}, trying again.", this);
                SendChanges(); // TODO: somehow test if this kind of retry even works or if the serialization request got reset right afterwards
            }
            else
            {
                #if ItemSyncDebug
                Debug.Log($"Sending {result.byteCount} bytes");
                #endif
            }
        }

        public override void OnDeserialization()
        {
            if (State != IdleState && pickup.IsHeld) // did someone steal the item?
                pickup.Drop(); // drop it

            bool isAttached = (syncedFlags & 1) != 0;
            if (pickup.DisallowTheft)
                pickup.pickupable = !isAttached;

            if (isAttached)
            {
                attachedPlayer = Networking.GetOwner(this.gameObject); // ensure it is up to date
                attachedBone = (syncedFlags & 2) != 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                if (State == ReceivingAttachedState) // interpolate from old to new offset
                {
                    posInterpolationDiff = syncedPosition - attachedLocalOffset;
                    interpolationStartRotation = attachedRotationOffset;
                }
                else // figure out current local offset and interpolate starting from there
                {
                    MoveDummyToBone();
                    posInterpolationDiff = syncedPosition - GetLocalPositionToBone(ItemPosition);
                    interpolationStartRotation = GetLocalRotationToBone(ItemRotation);
                }
                attachedLocalOffset = syncedPosition;
                attachedRotationOffset = syncedRotation;
                interpolationStartTime = Time.time;
                State = ReceivingMovingToBoneState;
            }
            else // not attached
            {
                posInterpolationDiff = syncedPosition - ItemPosition;
                interpolationStartRotation = ItemRotation;
                interpolationStartTime = Time.time;
                State = ReceivingFloatingState;
            }
        }
    }
}
