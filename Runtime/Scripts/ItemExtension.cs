using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AssociatedEntityExtensionData(typeof(ItemExtensionData))]
    [RequireComponent(typeof(CustomPickup))]
    [RequireComponent(typeof(Entity))]
    [SingletonDependency(typeof(ItemSystem))]
    [DisallowMultipleComponent]
    public class ItemExtension : EntityExtension
    {
        [HideInInspector][SingletonReference] public ItemSystem itemSystem;
        [HideInInspector][SingletonReference] public PlayerTrackingDataSyncManagerAPI trackingDataSync;
        [HideInInspector][SingletonReference] public PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SingletonReference] public ItemTransformController transformController;
        [HideInInspector][SingletonReference] public UpdateManager updateManager;
        [System.NonSerialized] public ItemExtensionData data;

        [System.NonSerialized] public PhysicsEntityExtension physicsExt;
        [System.NonSerialized] public CustomPickup pickup;
        private bool preventPickupInteraction;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        /// <summary>
        /// <para>Only relevant on the client which is actually holding the pickup.</para>
        /// </summary>
        [System.NonSerialized] public bool pickupIsHeld;
        /// <summary>
        /// <para>Only relevant on the client to which the pickup is actually attached to.</para>
        /// </summary>
        [System.NonSerialized] public bool pickupIsAttached;
        /// <summary>
        /// <para>Used to prevent recursion.</para>
        /// </summary>
        [System.NonSerialized] public bool isInOnPickupStateChanged;
        /// <summary>
        /// <para>Used to not depend on any guarantees of pickup and drop functions running in exact
        /// pairs. This is simply more robust, even if it might be redundant.</para>
        /// </summary>
        [System.NonSerialized] public bool isTrackingDataSyncActive;

        private bool shouldHaveControlOfTransformSync = false;
        /// <summary>Used by the <see cref="UpdateManager"/>.</summary>
        [System.NonSerialized] public int customUpdateInternalIndex;
        private bool isUpdatingPickupController = false;

        private bool isTrackingVelocity = false;
        private Vector3 positionLastFrame;
        private Quaternion rotationLastFrame;
        [System.NonSerialized] public Vector3 trackedVelocity;
        [System.NonSerialized] public Vector3 trackedAngularVelocityAxis;
        [System.NonSerialized] public float trackedAngularVelocityAngle;
        /// <summary>An angle axis rotation, magnitude is radians per second. Matches the format of
        /// <see cref="Rigidbody.angularVelocity"/>.</summary>
        public Vector3 TrackedAngularVelocity => trackedAngularVelocityAxis * (trackedAngularVelocityAngle * Mathf.Deg2Rad);
        private const float MaxVelocityWeight = 0.8f;
        private const float VelocityRollingAverageSeconds = 0.05f;

        [System.NonSerialized] public bool isHeldSpecifically;
        [System.NonSerialized] public uint attachedToPlayerId;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        /// <summary>
        /// <para>Unlike the name might imply, the bone might actually just not exist. This is the latency
        /// state side of <see cref="ItemExtensionData.attachedBoneExists"/>. So if the attached player is the
        /// local player, this variable very very most likely reflects reality, only case where it might not
        /// is when it was changed in latency state and then reset back to the game state through
        /// <see cref="ApplyExtensionData"/> for any reason immediately after.</para>
        /// <para>When the attached player is a remote player this is almost meaningless, the bone for that
        /// player may or may not exist at this point in time, who knows. However what it does mean is that
        /// even if the bone currently doesn't exist, it is likely going to exist in the relatively near
        /// future. And if it legitimately doesn't exist, there is going to be a drop input action coming
        /// soon.</para>
        /// <para>Similar story for when this is <see langword="false"/>, the bone might actually exist,
        /// though it likely doesn't and nothing is going to use the bone regardless.</para>
        /// </summary>
        [System.NonSerialized] public bool attachedBoneExists;
        [System.NonSerialized] public Vector3 attachedOffsetVector;
        [System.NonSerialized] public Quaternion attachedOffsetRotation;
        [System.NonSerialized] public Vector3 playerToAnchorOffsetVector;
        [System.NonSerialized] public Quaternion playerToAnchorOffsetRotation;

        private void SetPreventPickupInteraction(bool value)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  SetPreventPickupInteraction");
#endif
            if (preventPickupInteraction == value)
                return;
            preventPickupInteraction = value;
            if (preventPickupInteraction)
                pickup.IncrementPreventInteraction();
            else
                pickup.DecrementPreventInteraction();
        }

        private void UpdatePickupInteractionPrevention()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  UpdatePickupInteractionPrevention");
#endif
            SetPreventPickupInteraction(data == null
                || (attachedToPlayerId != 0u && attachedToPlayerId != localPlayerId));
        }

        public override void OnInstantiate()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnInstantiate");
#endif
            pickup = GetComponent<CustomPickup>();
            physicsExt = entity.GetExtension<PhysicsEntityExtension>(nameof(PhysicsEntityExtensionData));
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            UpdatePickupInteractionPrevention();
        }

        public override void DisassociateFromExtensionDataAndReset(EntityExtension defaultExtension)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  DisassociateFromExtensionDataAndReset");
#endif
            DetachFromPlayer();
            SetAttachedBoneExists(false, Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.identity);
            data.ext = null;
            data = null;
            UpdatePickupInteractionPrevention();
        }

        public override void AssociateWithExtensionData()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  AssociateWithExtensionData");
#endif
            data = (ItemExtensionData)extensionData;
            data.ext = this;
            ApplyExtensionData();
            UpdatePickupInteractionPrevention();
        }

        public override void ApplyExtensionData()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  ApplyExtensionData");
#endif
            if (data.IsAttached)
                AttachToPlayerUsingItemData(doInterpolate: false);
            else
                DetachFromPlayer(interpolateToGameState: true);
        }

        public void AttachToPlayerUsingItemData(bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToPlayerUsingItemData");
#endif
            AttachToPlayer(
                data.isHeldSpecifically,
                data.attachedToPlayerId,
                data.attachedToBone,
                data.attachedBoneExists,
                data.attachedOffsetVector,
                data.attachedOffsetRotation,
                data.playerToAnchorOffsetVector,
                data.playerToAnchorOffsetRotation,
                doInterpolate);
        }

        public void AttachToPlayer(
            bool isHeldSpecifically,
            uint playerId,
            HumanBodyBones bone,
            bool boneExists,
            Vector3 attachedOffsetVector,
            Quaternion attachedOffsetRotation,
            Vector3 playerToAnchorOffsetVector,
            Quaternion playerToAnchorOffsetRotation,
            bool doInterpolate)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToPlayer");
#endif
            if (attachedToPlayerId != 0u && attachedToPlayerId != localPlayerId)
                itemSystem.DetachFromRemotePlayer(this);

            this.isHeldSpecifically = isHeldSpecifically;
            attachedToPlayerId = playerId;
            itemSystem.SetAttachedToBone(this, bone);
            attachedBoneExists = boneExists;
            this.attachedOffsetVector = attachedOffsetVector;
            this.attachedOffsetRotation = attachedOffsetRotation;
            this.playerToAnchorOffsetVector = playerToAnchorOffsetVector;
            this.playerToAnchorOffsetRotation = playerToAnchorOffsetRotation;

            StartStopMovementLoop();
            UpdatePickupInteractionPrevention();

            if (playerId == localPlayerId)
            {
                if (isHeldSpecifically)
                    itemSystem.PickUpByLocalPlayer(this, doInterpolate);
                else if (boneExists)
                    itemSystem.AttachToLocalPlayer(this, doInterpolate);
                else // I don't even think this is possible as it stands currently...
                    itemSystem.SendDropIA(data, forceNoVelocity: true);
            }
            else
                itemSystem.AttachToRemotePlayer(this, doInterpolate);
        }

        public void DetachFromPlayer(bool interpolateToGameState = false)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  DetachFromPlayer");
#endif
            if (attachedToPlayerId == 0u)
                return;

            if (attachedToPlayerId == localPlayerId)
                itemSystem.DetachFromLocalPlayer(this);
            else
                itemSystem.DetachFromRemotePlayer(this);

            isHeldSpecifically = false;
            attachedToPlayerId = 0u;
            attachedToBone = HumanBodyBones.Head; // isTrackingDataSyncActive is false here, no need to call SetAttachedToBone.
            attachedBoneExists = false;
            attachedOffsetVector = Vector3.zero;
            attachedOffsetRotation = Quaternion.identity;
            playerToAnchorOffsetVector = Vector3.zero;
            playerToAnchorOffsetRotation = Quaternion.identity;

            StartStopMovementLoop(interpolateToGameState);
            UpdatePickupInteractionPrevention();
        }

        public void OnPickupStateChanged()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupStateChanged");
#endif
            if (pickup == null || !lockstep.IsInitialized)
                return;
            isInOnPickupStateChanged = true;
            bool newPickupIsHeld = pickup.isHeldByPrimaryHand;
            bool newPickupIsAttached = pickup.isAttached;
            if (!newPickupIsHeld && !newPickupIsAttached)
            {
                if (pickupIsHeld)
                {
                    pickupIsHeld = false;
                    itemSystem.SendDropIA(data, forceNoVelocity: false);
                    isInOnPickupStateChanged = false;
                    return;
                }
                if (pickupIsAttached)
                {
                    pickupIsAttached = false;
                    itemSystem.SendDropIA(data, forceNoVelocity: true);
                    isInOnPickupStateChanged = false;
                    return;
                }
                isInOnPickupStateChanged = false;
                return;
            }
            // newPickupIsHeld xor newPickupIsAttached is true here.
            if (newPickupIsHeld)
            {
                if (pickupIsHeld) // Hands or offsets changed.
                {
                    itemSystem.SendPickupIA(data);
                    isInOnPickupStateChanged = false;
                    return;
                }
                pickupIsHeld = true;
                pickupIsAttached = false;
                itemSystem.SendPickupIA(data);
            }
            else // newPickupIsAttached is true.
            {
                if (pickupIsAttached) // Attached bone changed.
                {
                    itemSystem.SendAttachIA(data);
                    isInOnPickupStateChanged = false;
                    return;
                }
                pickupIsHeld = false;
                pickupIsAttached = true;
                itemSystem.SendAttachIA(data);
            }
            isInOnPickupStateChanged = false;
        }

        public override void OnPickupUseDown()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupUseDown");
#endif
        }

        public override void OnPickupUseUp()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupUseUp");
#endif
        }

        private void ApplyChangedOffsetsLocallyToPickup()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  ApplyChangedOffsetsLocallyToPickup");
#endif
            if (isHeldSpecifically)
                itemSystem.PickUpByLocalPlayer(this, doInterpolate: true);
            else if (attachedBoneExists)
                itemSystem.AttachToLocalPlayer(this, doInterpolate: true);
            // Do not detach. Changing offsets should not make the local player drop the item.
        }

        public void SetAttachedBoneExists(
            bool boneExists,
            Vector3 attachedOffsetVector,
            Quaternion attachedOffsetRotation,
            Vector3 playerToAnchorOffsetVector,
            Quaternion playerToAnchorOffsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  SetAttachedBoneExists");
#endif
            this.attachedOffsetVector = attachedOffsetVector;
            this.attachedOffsetRotation = attachedOffsetRotation;
            this.playerToAnchorOffsetVector = playerToAnchorOffsetVector;
            this.playerToAnchorOffsetRotation = playerToAnchorOffsetRotation;

            if (attachedBoneExists == boneExists)
            {
                if (!attachedBoneExists || attachedToPlayerId == 0u)
                    return; // Realistically nothing changed.

                if (attachedToPlayerId == localPlayerId)
                    ApplyChangedOffsetsLocallyToPickup();
                else
                    itemSystem.AttachToRemotePlayer(this, doInterpolate: true);
                return;
            }

            attachedBoneExists = boneExists;
            StartStopMovementLoop();
            if (attachedToPlayerId == 0u)
                return;

            if (attachedToPlayerId == localPlayerId)
                ApplyChangedOffsetsLocallyToPickup();
            else
                itemSystem.AttachToRemotePlayer(this, doInterpolate: true);
        }

        private void TakeOrGiveBackControlOfTransformSync(bool interpolateToGameState)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  TakeOrGiveBackControlOfTransformSync");
#endif
            // Could technically add this to the following condition: && (isHeldSpecifically || attachedBoneExists)
            // However that actually always ends up being true, because when isHeldSpecifically is false, attachedBoneExists is always true.
            bool newValue = attachedToPlayerId != 0u;
            if (shouldHaveControlOfTransformSync == newValue)
                return;
            shouldHaveControlOfTransformSync = newValue;

            if (shouldHaveControlOfTransformSync)
            {
                entity.TakeControlOfTransformSync(transformController);
                return;
            }
            if (interpolateToGameState)
            {
                entity.GiveBackControlOfTransformSync(
                    transformController,
                    entityData.position,
                    entityData.rotation,
                    entityData.scale);
                return;
            }
            Transform t = entity.transform;
            entity.GiveBackControlOfTransformSync(
                transformController,
                t.position,
                t.rotation,
                t.localScale);
        }

        public void StartStopMovementLoop(bool interpolateToGameState = false)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  StartStopMovementLoop");
#endif
            TakeOrGiveBackControlOfTransformSync(interpolateToGameState);

            isUpdatingPickupController = attachedToPlayerId != localPlayerId && (isHeldSpecifically || attachedBoneExists);
            isTrackingVelocity = isHeldSpecifically && physicsExt != null;

            if (isUpdatingPickupController || isTrackingVelocity)
                updateManager.Register(this);
            else
                updateManager.Deregister(this);

            if (!isTrackingVelocity)
                return;
            trackedVelocity = Vector3.zero;
            trackedAngularVelocityAxis = Vector3.zero;
            trackedAngularVelocityAngle = 0f;
            Transform t = entity.transform;
            positionLastFrame = t.position;
            rotationLastFrame = t.rotation;
        }

        /// <summary>Called by the <see cref="UpdateManager"/>.</summary>
        public void CustomUpdate()
        {
            if (isUpdatingPickupController)
                UpdateRemotePickupController();
            if (isTrackingVelocity)
                TrackVelocity();
        }

        private void UpdateRemotePickupController()
        {
            if (isHeldSpecifically)
            {
                CustomPickupState state = itemSystem.stateForPickupController;
                state.pickup = pickup;
                state.pickupTransform = pickup.transform;
                if (attachedBoneExists)
                {
                    VRCPlayerApi player = pickup.controllingPlayer;
                    if (player == null)
                        return;
                    Vector3 bonePosition = player.GetBonePosition(attachedToBone);
                    if (bonePosition == Vector3.zero)
                        return;
                    Quaternion boneRotation = player.GetBoneRotation(attachedToBone);
                    state.primaryHandPosition = bonePosition + boneRotation * playerToAnchorOffsetVector;
                    state.primaryHandRotation = boneRotation * playerToAnchorOffsetRotation;
                }
                else
                {
                    PlayerTrackingDataSync player = playerDataManager.GetPlayerDataForPlayerId<PlayerTrackingDataSync>(nameof(PlayerTrackingDataSync), attachedToPlayerId);
                    player.GetCurrentPosition(attachedToBone);
                    Quaternion resultRotation = player.resultRotation;
                    state.primaryHandPosition = player.resultPosition + resultRotation * playerToAnchorOffsetVector;
                    state.primaryHandRotation = resultRotation * playerToAnchorOffsetRotation;
                }
                pickup.GetPickupController().MovePickup(state);
            }
            else
            {
                CustomPickupAttachedState state = itemSystem.stateForAttachedPickupController;
                state.pickup = pickup;
                state.pickupTransform = pickup.transform;
                VRCPlayerApi player = pickup.controllingPlayer;
                if (player == null)
                    return;
                Vector3 bonePosition = player.GetBonePosition(attachedToBone);
                if (bonePosition == Vector3.zero)
                    return;
                state.bonePosition = bonePosition;
                state.boneRotation = player.GetBoneRotation(attachedToBone);
                pickup.GetPickupController().MoveAttachedPickup(state);
            }
        }

        private void TrackVelocity()
        {
            float deltaTime = Time.deltaTime;
            float weight = Mathf.Min(MaxVelocityWeight, deltaTime / VelocityRollingAverageSeconds);
            float inverseWeight = 1f - weight;
            Transform t = entity.transform;
            Vector3 position = t.position;
            Quaternion rotation = t.rotation;
            (Quaternion.Inverse(rotationLastFrame) * rotation).ToAngleAxis(out float angle, out Vector3 axis);
            trackedVelocity = weight * ((position - positionLastFrame) / deltaTime)
                + inverseWeight * trackedVelocity;
            trackedAngularVelocityAxis = weight * axis
                + inverseWeight * trackedAngularVelocityAxis;
            trackedAngularVelocityAngle = weight * (angle / deltaTime)
                + inverseWeight * trackedAngularVelocityAngle;
            positionLastFrame = position;
            rotationLastFrame = rotation;
        }
    }
}
