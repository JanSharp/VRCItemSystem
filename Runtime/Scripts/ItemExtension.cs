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
        [System.NonSerialized] public ItemExtensionData data;
        [System.NonSerialized] public ItemSystem itemSystem;

        [System.NonSerialized] public CustomPickup pickup;
        private bool preventPickupInteraction;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        [System.NonSerialized] public bool ignoreNextPickupEvent = false;
        [System.NonSerialized] public bool ignoreNextDropEvent = false;
        private bool comingFromOnPickup = false;

        private bool shouldHaveControlOfTransformSync = false;
        private bool movementLoopShouldBeRunning = false;
        private bool movementLoopIsRunning = false;
        public const float MovementLoopInterval = 0.1f;

        [System.NonSerialized] public uint attachedToPlayerId;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        [System.NonSerialized] public bool attachedBoneExists;
        [System.NonSerialized] public Vector3 attachedOffsetVector;
        [System.NonSerialized] public Quaternion attachedOffsetRotation;

        private void SetPreventPickupInteraction(bool value)
        {
#if ItemSystemDebug
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
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  UpdatePickupInteractionPrevention");
#endif
            SetPreventPickupInteraction(data == null
                || (attachedToPlayerId != 0u && attachedToPlayerId != localPlayerId));
        }

        public override void OnInstantiate()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnInstantiate");
#endif
            pickup = GetComponent<CustomPickup>();
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
            UpdatePickupInteractionPrevention();
        }

        public override void DisassociateFromExtensionDataAndReset(EntityExtension defaultExtension)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  DisassociateFromExtensionDataAndReset");
#endif
            DetachFromPlayer();
            SetAttachedBoneExists(false, Vector3.zero, Quaternion.identity);
            itemSystem = null;
            data.ext = null;
            data = null;
            UpdatePickupInteractionPrevention();
        }

        public override void AssociateWithExtensionData()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  AssociateWithExtensionData");
#endif
            data = (ItemExtensionData)extensionData;
            data.ext = this;
            itemSystem = data.itemSystem;
            ApplyExtensionData();
            UpdatePickupInteractionPrevention();
        }

        public override void ApplyExtensionData()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  ApplyExtensionData");
#endif
            if (data.attachedToPlayerId != 0)
                AttachToPlayerUsingItemData();
            else
                DetachFromPlayer(interpolateToGameState: true);
        }

        public void AttachToPlayerUsingItemData()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToPlayerUsingItemData");
#endif
            AttachToPlayer(
                data.attachedToPlayerId,
                data.attachedToBone,
                data.attachedBoneExists,
                data.attachedOffsetVector,
                data.attachedOffsetRotation);
        }

        public void AttachToPlayer(uint playerId, HumanBodyBones bone, bool boneExists, Vector3 offsetVector, Quaternion offsetRotation)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  AttachToPlayer");
#endif
            if (attachedToPlayerId != 0u) // Cannot omit interpolateToGameState, U# generates improper code when doing so.
                DetachFromPlayer(interpolateToGameState: false, comingFromAttach: true);

            attachedToPlayerId = playerId;
            attachedToBone = bone;
            attachedBoneExists = boneExists;
            attachedOffsetVector = offsetVector;
            attachedOffsetRotation = offsetRotation;

            StartStopMovementLoop();
            UpdatePickupInteractionPrevention();

            if (comingFromOnPickup)
                return;

            if (playerId == localPlayerId)
                itemSystem.AttachToLocalPlayer(this);
            else if (boneExists)
                itemSystem.AttachToRemotePlayer(this);
        }

        public void DetachFromPlayer(bool interpolateToGameState = false, bool comingFromAttach = false)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  DetachFromPlayer");
#endif
            if (attachedToPlayerId == 0u)
                return;

            if (attachedToPlayerId == localPlayerId)
                itemSystem.DetachFromLocalPlayer(this);
            else
                itemSystem.DetachFromRemotePlayer(this);

            if (comingFromAttach)
                return;

            attachedToPlayerId = 0u;
            attachedToBone = HumanBodyBones.Head;
            attachedBoneExists = false;
            attachedOffsetVector = Vector3.zero;
            attachedOffsetRotation = Quaternion.identity;

            StartStopMovementLoop(interpolateToGameState);
            UpdatePickupInteractionPrevention();
        }

        public override void OnPickup()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickup - ignoreNextPickupEvent: {ignoreNextPickupEvent}");
#endif
            if (ignoreNextPickupEvent)
            {
                ignoreNextPickupEvent = false;
                return;
            }
            if (!lockstep.IsInitialized)
            {
                ignoreNextDropEvent = true;
                pickup.Drop();
                return;
            }
            comingFromOnPickup = true;
            itemSystem.SendPickupIA(data);
            comingFromOnPickup = false;
        }

        public override void OnDrop()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnDrop - ignoreNextDropEvent: {ignoreNextDropEvent}");
#endif
            if (ignoreNextDropEvent)
            {
                ignoreNextDropEvent = false;
                return;
            }
            itemSystem.SendDropIA(data);
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

        public void SetAttachedBoneExists(bool boneExists, Vector3 offsetVector, Quaternion offsetRotation)
        {
#if ItemSystemDebug
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
                    itemSystem.AttachToLocalPlayer(this); // Applies the changed offsets to the pickup.
                    return;
                }
                // Attached to remote player, bone did exist, still exists, offsets have changed, interpolate.
                Transform entityTransform = entity.transform;
                itemSystem.interpolation.InterpolateLocalPosition(entityTransform, attachedOffsetVector, Entity.TransformChangeInterpolationDuration);
                itemSystem.interpolation.InterpolateLocalRotation(entityTransform, attachedOffsetRotation, Entity.TransformChangeInterpolationDuration);
                return;
            }

            attachedBoneExists = boneExists;
            StartStopMovementLoop();
            if (attachedToPlayerId == 0)
                return;

            if (attachedToPlayerId == localPlayerId)
            {
                if (attachedBoneExists)
                    itemSystem.AttachToLocalPlayer(this);
                // Do not detach. Changing offsets should not make the local player drop the item.
            }
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
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  TakeOrGiveBackControlOfTransformSync");
#endif
            bool prev = shouldHaveControlOfTransformSync;
            shouldHaveControlOfTransformSync = attachedToPlayerId != 0u && attachedBoneExists;
            if (shouldHaveControlOfTransformSync == prev)
                return;

            if (shouldHaveControlOfTransformSync)
            {
                entity.TakeControlOfTransformSync(itemSystem.transformController);
                return;
            }
            if (interpolateToGameState)
            {
                entity.GiveBackControlOfTransformSync(
                    itemSystem.transformController,
                    entityData.position,
                    entityData.rotation,
                    entityData.scale);
                return;
            }
            Transform t = entity.transform;
            entity.GiveBackControlOfTransformSync(
                itemSystem.transformController,
                t.position,
                t.rotation,
                t.localScale);
        }

        public void StartStopMovementLoop(bool interpolateToGameState = false)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  StartStopMovementLoop");
#endif
            TakeOrGiveBackControlOfTransformSync(interpolateToGameState);
            bool prev = movementLoopShouldBeRunning;
            movementLoopShouldBeRunning = !shouldHaveControlOfTransformSync && attachedToPlayerId == localPlayerId;
            if (movementLoopShouldBeRunning == prev)
                return;

            if (movementLoopShouldBeRunning)
                StartMovementLoop();
        }

        private void StartMovementLoop()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  StartMovementLoop");
#endif
            if (movementLoopIsRunning)
                return;
            movementLoopIsRunning = true;
            SendCustomEventDelayedFrames(nameof(MovementLoop), 1);
        }

        public void MovementLoop()
        {
            // TODO: flag position and rotation separately and only if it actually changed.
            entity.FlagForPositionAndRotationChange();
            if (!movementLoopShouldBeRunning)
            {
                movementLoopIsRunning = false;
                return;
            }
            SendCustomEventDelayedSeconds(nameof(MovementLoop), MovementLoopInterval);
        }
    }
}
