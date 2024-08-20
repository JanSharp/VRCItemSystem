using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace JanSharp
{
    // TODO: maybe rigidbody.MovePosition for interpolation. Also note that setting position my take effect 1
    // frame delayed, or it is just a jittering issue while the item is actually held (which is why
    // UpdateSender is not writing to rb.position)

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemSync : UdonSharpBehaviour
    {
        [System.NonSerialized] public ItemSystem itemSystem;
        [System.NonSerialized] public VRCPlayerApi localPlayer;
        [System.NonSerialized] public int localPlayerId;

        [System.NonSerialized] public uint id;
        private int holdingPlayerId = -1;
        private VRCPlayerApi.TrackingDataType attachedTrackingGS;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private bool isVisible = true;
        // TODO: pickup.IsHeld null ref exception
        public bool LocalPlayerIsInControl => pickup.isHeld || (holdingPlayerId == -1 && itemSystem.lockstep.IsMaster);

        public int HoldingPlayerId => holdingPlayerId;

        public void SetHoldingPlayer(int playerId, VRCPlayerApi.TrackingDataType attachedTrackingGS)
        {
            if (playerId == holdingPlayerId && attachedTrackingGS == this.attachedTrackingGS)
                return;
            holdingPlayerId = playerId;
            this.attachedTrackingGS = attachedTrackingGS;

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

        [System.NonSerialized] public CustomPickup pickup;
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
            if (!IsAttachedPlayerValid)
                return false;
            var trackingData = attachedPlayer.GetTrackingData(attachedTracking);
            parentPos = trackingData.position;
            parentRot = trackingData.rotation;
            return true;
        }


        private const byte IdleState = 0; // the only state with CustomUpdate deregistered
        private const byte AttachedSendingState = 1; // attached to hand
        // private const byte DesktopAttachedRotatingState = 2; // attached to hand
        private const byte ReceivingFloatingState = 3;
        private const byte ReceivingMovingToBoneState = 4; // attached to hand, but interpolating offset towards the actual attached position
        private const byte ReceivingAttachedState = 5; // attached to hand
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
                case AttachedSendingState:
                    return "AttachedSendingState";
                // case DesktopAttachedRotatingState:
                //     return "DesktopAttachedRotatingState";
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
        private bool IsAttachedPlayerValid => attachedPlayer != null && attachedPlayer.IsValid();
        private VRCPlayerApi.TrackingDataType attachedTracking;
        private Vector3 attachedLocalOffset;
        private Quaternion attachedRotationOffset;

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

        private bool IsReceivingState() => LocalState >= ReceivingFloatingState;

        public override void OnPickup()
        {
            Debug.Log($"[ItemSystem] ItemSync  OnPickup  itemId: {this.id}");

            attachedPlayer = localPlayer;
            attachedTracking = pickup.heldTrackingData;

            itemSystem.SendPickupIA(id, localPlayerId, attachedTracking);

            if (!TryLoadHandPos())
                return;
            // worldPos = ItemPosition;
            // worldRot = ItemRotation;
            // WorldToLocal();
            attachedLocalOffset = pickup.heldOffsetVector;
            attachedRotationOffset = pickup.heldOffsetRotation;
            LocalState = AttachedSendingState;
            SendAttachedData();
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
                Debug.LogError($"[ItemSystem] It should truly be impossible for an item to get updated white it is in IdleState. Item name: ${this.name}.", this);
                return;
            }
            if (IsReceivingState())
                UpdateReceiver();
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
            if (pickup.isHeld)
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
