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
        [HideInInspector][SingletonReference] public ItemTransformController transformController;
        [HideInInspector][SingletonReference] public InterpolationManager interpolation;
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

        private bool shouldHaveControlOfTransformSync = false;
        /// <summary>Used by the <see cref="UpdateManager"/>.</summary>
        [System.NonSerialized] public int customUpdateInternalIndex;
        private float nextMovementIntervalTime = 0f;
        private bool movementLoopIsRunning = false;
        public const float MovementLoopInterval = 0.1f;

        private Vector3 positionLastFrame;
        private Quaternion rotationLastFrame;
        [System.NonSerialized] public Vector3 trackedVelocity;
        [System.NonSerialized] public Vector3 trackedAngularVelocityAxis;
        [System.NonSerialized] public float trackedAngularVelocityAngle;
        /// <summary>An angle axis rotation, magnitude is radians per second. Matches the format of
        /// <see cref="Rigidbody.angularVelocity"/>.</summary>
        public Vector3 TrackedAngularVelocity => trackedAngularVelocityAxis * (trackedAngularVelocityAngle * Mathf.Deg2Rad);
        private const float MaxVelocityWeight = 0.75f;
        private const float VelocityRollingAverageSeconds = 0.1f;

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
            SetAttachedBoneExists(false, Vector3.zero, Quaternion.identity);
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
                AttachToPlayerUsingItemData();
            else
                DetachFromPlayer(interpolateToGameState: true);
        }

        public void AttachToPlayerUsingItemData()
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
                data.attachedOffsetRotation);
        }

        public void AttachToPlayer(
            bool isHeldSpecifically,
            uint playerId,
            HumanBodyBones bone,
            bool boneExists,
            Vector3 offsetVector,
            Quaternion offsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToPlayer");
#endif
            if (attachedToPlayerId != 0u && attachedToPlayerId != localPlayerId)
                itemSystem.DetachFromRemotePlayer(this);

            this.isHeldSpecifically = isHeldSpecifically;
            attachedToPlayerId = playerId;
            attachedToBone = bone;
            attachedBoneExists = boneExists;
            attachedOffsetVector = offsetVector;
            attachedOffsetRotation = offsetRotation;

            StartStopMovementLoop();
            UpdatePickupInteractionPrevention();

            if (playerId == localPlayerId)
            {
                if (isHeldSpecifically)
                    itemSystem.PickUpByLocalPlayer(this);
                else if (boneExists)
                    itemSystem.AttachToLocalPlayer(this);
                else
                    itemSystem.SendDropIA(data, forceNoVelocity: true);
            }
            else if (boneExists)
                itemSystem.AttachToRemotePlayer(this);
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
            attachedToBone = HumanBodyBones.Head;
            attachedBoneExists = false;
            attachedOffsetVector = Vector3.zero;
            attachedOffsetRotation = Quaternion.identity;

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
            bool newPickupIsHeld = pickup.isHeld;
            bool newPickupIsAttached = pickup.isAttached;
            if (!newPickupIsHeld && !newPickupIsAttached)
            {
                if (pickupIsHeld)
                {
                    pickupIsHeld = false;
                    itemSystem.SendDropIA(data, forceNoVelocity: false);
                    return;
                }
                if (pickupIsAttached)
                {
                    pickupIsAttached = false;
                    itemSystem.SendDropIA(data, forceNoVelocity: true);
                    return;
                }
                return;
            }
            // newPickupIsHeld xor newPickupIsAttached is true here.
            if (newPickupIsHeld)
            {
                if (pickupIsHeld) // TODO: Hands or offsets changed. Could probably just send a pickup IA here and make it deal with it.
                    return;
                pickupIsHeld = true;
                pickupIsAttached = false;
                itemSystem.SendPickupIA(data, pickup.usedHermiteCurveWhenLastPickedUp);
            }
            else // newPickupIsAttached is true.
            {
                if (pickupIsAttached) // TODO: Attached bone changed. Could probably just send an attach IA here and make it deal with it.
                    return;
                pickupIsHeld = false;
                pickupIsAttached = true;
                itemSystem.SendAttachIA(data);
            }
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
                itemSystem.PickUpByLocalPlayer(this);
            else if (attachedBoneExists)
                itemSystem.AttachToLocalPlayer(this);
            // Do not detach. Changing offsets should not make the local player drop the item.
        }

        public void SetAttachedBoneExists(bool boneExists, Vector3 offsetVector, Quaternion offsetRotation)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  SetAttachedBoneExists");
#endif
            attachedOffsetVector = offsetVector;
            attachedOffsetRotation = offsetRotation;

            if (attachedBoneExists == boneExists)
            {
                if (!attachedBoneExists || attachedToPlayerId == 0u)
                    return; // Realistically nothing changed.

                if (attachedToPlayerId == localPlayerId)
                {
                    ApplyChangedOffsetsLocallyToPickup();
                    return;
                }
                // Attached to remote player, bone did exist, still exists, offsets have changed, interpolate.
                Transform entityTransform = entity.transform;
                interpolation.LerpLocalPosition(entityTransform, attachedOffsetVector, Entity.TransformChangeInterpolationDuration);
                interpolation.LerpLocalRotation(entityTransform, attachedOffsetRotation, Entity.TransformChangeInterpolationDuration);
                return;
            }

            attachedBoneExists = boneExists;
            StartStopMovementLoop();
            if (attachedToPlayerId == 0)
                return;

            if (attachedToPlayerId == localPlayerId)
                ApplyChangedOffsetsLocallyToPickup();
            else
            {
                if (attachedBoneExists)
                    itemSystem.AttachToRemotePlayer(this);
                else
                    itemSystem.DetachFromRemotePlayer(this);
            }
        }

        private void TakeOrGiveBackControlOfTransformSync(bool interpolateToGameState)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  TakeOrGiveBackControlOfTransformSync");
#endif
            bool prev = shouldHaveControlOfTransformSync;
            shouldHaveControlOfTransformSync = attachedToPlayerId != 0u && attachedBoneExists;
            if (shouldHaveControlOfTransformSync == prev)
                return;

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
            bool movementLoopShouldBeRunning = attachedToPlayerId == localPlayerId
                && isHeldSpecifically // Not when it is attached.
                && (!shouldHaveControlOfTransformSync || physicsExt != null);
            if (movementLoopShouldBeRunning == movementLoopIsRunning)
                return;
            movementLoopIsRunning = movementLoopShouldBeRunning;

            if (!movementLoopShouldBeRunning)
            {
                updateManager.Deregister(this);
                return;
            }
            updateManager.Register(this);
            if (physicsExt == null)
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
            if (!shouldHaveControlOfTransformSync)
            {
                float time = Time.time;
                if (time >= nextMovementIntervalTime)
                {
                    // TODO: flag position and rotation separately and only if it actually changed.
                    entity.FlagForPositionAndRotationChange();
                    nextMovementIntervalTime = time + MovementLoopInterval;
                }
            }
            if (physicsExt == null)
                return;

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
