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
        [HideInInspector][SingletonReference] public ItemExtensionLogic itemLogic;
        [HideInInspector][SingletonReference] public ItemSystem itemSystem;
        [HideInInspector][SingletonReference] public PlayerTrackingDataSyncManagerAPI trackingDataSync;
        [HideInInspector][SingletonReference] public PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SingletonReference] public ItemTransformController transformController;
        [HideInInspector][SingletonReference] public UpdateManager updateManager;
        /// <summary>
        /// <para>Used by the <see cref="UpdateManager"/>.</para>
        /// </summary>
        private int customUpdateInternalIndex;

        [SerializeField] private bool disallowStealingHeld = true;
        [SerializeField] private bool disallowStealingAttached = true;
        public bool DisallowStealingHeld
        {
            get => disallowStealingHeld;
            set
            {
                disallowStealingHeld = value;
                itemLogic.UpdatePickupInteractionPrevention(this);
            }
        }
        public bool DisallowStealingAttached
        {
            get => disallowStealingAttached;
            set
            {
                disallowStealingAttached = value;
                itemLogic.UpdatePickupInteractionPrevention(this);
            }
        }

        [System.NonSerialized] public CustomPickupAttachedState stateForAttachedPickupController;
        [System.NonSerialized] public CustomPickupState stateForPickupController;

        [System.NonSerialized] public ItemExtensionData data;
        [System.NonSerialized] public PhysicsEntityExtension physicsExt;
        [System.NonSerialized] public CustomPickup pickup;
        [System.NonSerialized] public Transform pickupTransform;
        [System.NonSerialized] public bool preventPickupInteraction;

        /// <summary>
        /// <para>Only relevant on the client which is actually holding the pickup.</para>
        /// </summary>
        [System.NonSerialized] public bool pickupIsHeldByPrimaryHand;
        /// <inheritdoc cref="pickupIsHeldByPrimaryHand"/>
        [System.NonSerialized] public bool pickupIsHeldBySecondaryHand;
        /// <summary>
        /// <para>Only relevant on the client to which the pickup is actually attached to.</para>
        /// </summary>
        [System.NonSerialized] public bool pickupIsAttached;
        /// <summary>
        /// <para>Used to prevent recursion.</para>
        /// </summary>
        [System.NonSerialized] public bool isInOnPickupStateChanged;

        /// <summary>
        /// <para>Used to not depend on any guarantees of pickup and drop functions running in exact pairs.
        /// This is simply more robust, even if it might be redundant.</para>
        /// </summary>
        [System.NonSerialized] public bool isPrimaryTrackingDataSyncActive;
        /// <inheritdoc cref="isPrimaryTrackingDataSyncActive"/>
        [System.NonSerialized] public bool isSecondaryTrackingDataSyncActive;

        [System.NonSerialized] public bool shouldHaveControlOfTransformSync = false;
        [System.NonSerialized] public bool isUpdatingPickupController = false;

        [System.NonSerialized] public bool isTrackingVelocity = false;
        [System.NonSerialized] public Vector3 positionLastFrame;
        [System.NonSerialized] public Quaternion rotationLastFrame;
        [System.NonSerialized] public Vector3 trackedVelocity;
        [System.NonSerialized] public Vector3 trackedAngularVelocityAxis;
        [System.NonSerialized] public float trackedAngularVelocityAngle;
        /// <summary>An angle axis rotation, magnitude is radians per second. Matches the format of
        /// <see cref="Rigidbody.angularVelocity"/>.</summary>
        public Vector3 TrackedAngularVelocity => trackedAngularVelocityAxis * (trackedAngularVelocityAngle * Mathf.Deg2Rad);
        private const float MaxVelocityWeight = 0.8f;
        private const float VelocityRollingAverageSeconds = 0.05f;

        [System.NonSerialized] public bool flaggedForPrimaryOffsetsChanged = false;
        [System.NonSerialized] public bool flaggedForSecondaryOffsetsChanged = false;
        [System.NonSerialized] public bool flaggedForAttachedOffsetsChanged = false;
        public const float OffsetsChangeSyncInterval = 0.275f;

        // /// <summary>
        // /// <para>Unlike the name might imply, the bone might actually just not exist. This is the latency
        // /// state side of <see cref="ItemExtensionData.primaryAttachedBoneExists"/>. So if the attached player is the
        // /// local player, this variable very very most likely reflects reality, only case where it might not
        // /// is when it was changed in latency state and then reset back to the game state through
        // /// <see cref="ApplyExtensionData"/> for any reason immediately after.</para>
        // /// <para>When the attached player is a remote player this is almost meaningless, the bone for that
        // /// player may or may not exist at this point in time, who knows. However what it does mean is that
        // /// even if the bone currently doesn't exist, it is likely going to exist in the relatively near
        // /// future. And if it legitimately doesn't exist, there is going to be a drop input action coming
        // /// soon.</para>
        // /// <para>Similar story for when this is <see langword="false"/>, the bone might actually exist,
        // /// though it likely doesn't and nothing is going to use the bone regardless.</para>
        // /// </summary>

        [System.NonSerialized] public PlayerTrackingDataSync attachedTrackingDataSync;

        #region Item Data Latency Mirror

        /// <summary>
        /// <para><c>0u</c> when <see cref="IsHeldOrAttached"/> is <see langword="false"/>.</para>
        /// </summary>
        [System.NonSerialized] public uint attachedToPlayerId;

        public bool IsHeldOrAttached => attachedToPlayerId != 0u;
        public bool IsHeld => isHeldByPrimaryHand || isHeldBySecondaryHand;

        #region Primary
        /// <inheritdoc cref="ItemExtensionData.isHeldByPrimaryHand"/>
        [System.NonSerialized] public bool isHeldByPrimaryHand = false;
        /// <inheritdoc cref="ItemExtensionData.primaryBone"/>
        [System.NonSerialized] public HumanBodyBones primaryBone = HumanBodyBones.Head;
        /// <inheritdoc cref="ItemExtensionData.primaryBoneExists"/>
        [System.NonSerialized] public bool primaryBoneExists = false;
        /// <inheritdoc cref="ItemExtensionData.primaryAnchorVector"/>
        [System.NonSerialized] public Vector3 primaryAnchorVector = Vector3.zero;
        /// <inheritdoc cref="ItemExtensionData.primaryAnchorRotation"/>
        [System.NonSerialized] public Quaternion primaryAnchorRotation = Quaternion.identity;
        /// <inheritdoc cref="ItemExtensionData.primaryOffsetVector"/>
        [System.NonSerialized] public Vector3 primaryOffsetVector = Vector3.zero;
        /// <inheritdoc cref="ItemExtensionData.primaryOffsetRotation"/>
        [System.NonSerialized] public Quaternion primaryOffsetRotation = Quaternion.identity;
        #endregion

        #region Secondary
        /// <inheritdoc cref="ItemExtensionData.isHeldBySecondaryHand"/>
        [System.NonSerialized] public bool isHeldBySecondaryHand = false;
        /// <inheritdoc cref="ItemExtensionData.secondaryBone"/>
        [System.NonSerialized] public HumanBodyBones secondaryBone = HumanBodyBones.Head;
        /// <inheritdoc cref="ItemExtensionData.secondaryBoneExists"/>
        [System.NonSerialized] public bool secondaryBoneExists = false;
        /// <inheritdoc cref="ItemExtensionData.secondaryAnchorVector"/>
        [System.NonSerialized] public Vector3 secondaryAnchorVector = Vector3.zero;
        /// <inheritdoc cref="ItemExtensionData.secondaryAnchorRotation"/>
        [System.NonSerialized] public Quaternion secondaryAnchorRotation = Quaternion.identity;
        /// <inheritdoc cref="ItemExtensionData.secondaryOffsetVector"/>
        [System.NonSerialized] public Vector3 secondaryOffsetVector = Vector3.zero;
        /// <inheritdoc cref="ItemExtensionData.secondaryOffsetRotation"/>
        [System.NonSerialized] public Quaternion secondaryOffsetRotation = Quaternion.identity;
        #endregion

        #region Attached
        /// <inheritdoc cref="ItemExtensionData.isAttached"/>
        [System.NonSerialized] public bool isAttached = false;
        /// <inheritdoc cref="ItemExtensionData.attachedToBone"/>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        /// <inheritdoc cref="ItemExtensionData.attachedOffsetVector"/>
        [System.NonSerialized] public Vector3 attachedOffsetVector = Vector3.zero;
        /// <inheritdoc cref="ItemExtensionData.attachedOffsetRotation"/>
        [System.NonSerialized] public Quaternion attachedOffsetRotation = Quaternion.identity;
        #endregion

        #endregion

        public override void OnInstantiate() => itemLogic.OnInstantiate(this);
        public override void DisassociateFromExtensionDataAndReset(EntityExtension defaultExtension) => itemLogic.DisassociateFromExtensionDataAndReset(this, defaultExtension);
        public override void AssociateWithExtensionData() => itemLogic.AssociateWithExtensionData(this);
        public override void ApplyExtensionData() => itemLogic.ApplyExtensionData(this);

        public void ClearPrimaryValues()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  ClearPrimaryValues");
#endif
            if (isPrimaryTrackingDataSyncActive)
            {
                isPrimaryTrackingDataSyncActive = false;
                trackingDataSync.StopTracking(ItemSystemUtil.BoneToTrackingType(primaryBone));
            }
            isHeldByPrimaryHand = false;
            primaryBone = HumanBodyBones.Head;
            primaryBoneExists = false;
            primaryAnchorVector = Vector3.zero;
            primaryAnchorRotation = Quaternion.identity;
            primaryOffsetVector = Vector3.zero;
            primaryOffsetRotation = Quaternion.identity;
        }

        public void ClearSecondaryValues()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  ClearSecondaryValues");
#endif
            if (isSecondaryTrackingDataSyncActive)
            {
                isSecondaryTrackingDataSyncActive = false;
                trackingDataSync.StopTracking(ItemSystemUtil.BoneToTrackingType(secondaryBone));
            }
            isHeldBySecondaryHand = false;
            secondaryBone = HumanBodyBones.Head;
            secondaryBoneExists = false;
            secondaryAnchorVector = Vector3.zero;
            secondaryAnchorRotation = Quaternion.identity;
            secondaryOffsetVector = Vector3.zero;
            secondaryOffsetRotation = Quaternion.identity;
        }

        public void ClearAttachedValues()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtension  ClearAttachedValues");
#endif
            isAttached = false;
            attachedToBone = HumanBodyBones.Head;
            attachedOffsetVector = Vector3.zero;
            attachedOffsetRotation = Quaternion.identity;
        }

        public void OnPickupStateChanged()
        {
            isInOnPickupStateChanged = true;
            itemLogic.OnPickupStateChanged(this);
            isInOnPickupStateChanged = false;
        }

        public void OnPrimaryOffsetsChangedDelayed() => itemLogic.OnPrimaryOffsetsChangedDelayed(this);
        public void OnSecondaryOffsetsChangedDelayed() => itemLogic.OnSecondaryOffsetsChangedDelayed(this);
        public void OnAttachedOffsetsChangedDelayed() => itemLogic.OnAttachedOffsetsChangedDelayed(this);

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
            if (isAttached)
            {
                VRCPlayerApi player = pickup.controllingPlayer;
                if (!Utilities.IsValid(player))
                    return;
                Vector3 bonePosition = player.GetBonePosition(attachedToBone);
                if (bonePosition == Vector3.zero)
                    return;
                stateForAttachedPickupController.bonePosition = bonePosition;
                stateForAttachedPickupController.boneRotation = player.GetBoneRotation(attachedToBone);

                stateForAttachedPickupController.pickup = pickup;
                stateForAttachedPickupController.pickupTransform = pickup.transform;
                pickup.GetPickupController().MoveAttachedPickup(stateForAttachedPickupController);
                return;
            }

            // Held by at least 1 hand.

            // Would have loved to move these 2 blocks into functions, but with the early returns it is more
            // performant for them to be inlined, and this is performance critical code. Thanks Udon.

            if (isHeldByPrimaryHand)
            {
                if (primaryBoneExists)
                {
                    VRCPlayerApi player = pickup.controllingPlayer;
                    if (!Utilities.IsValid(player))
                        return;
                    Vector3 bonePosition = player.GetBonePosition(primaryBone);
                    if (bonePosition == Vector3.zero)
                        return;
                    Quaternion boneRotation = player.GetBoneRotation(primaryBone);
                    stateForPickupController.primaryHandPosition = bonePosition + boneRotation * primaryAnchorVector;
                    stateForPickupController.primaryHandRotation = boneRotation * primaryAnchorRotation;
                }
                else
                {
                    attachedTrackingDataSync.GetCurrentPosition(primaryBone);
                    Quaternion resultRotation = attachedTrackingDataSync.resultRotation;
                    stateForPickupController.primaryHandPosition = attachedTrackingDataSync.resultPosition + resultRotation * primaryAnchorVector;
                    stateForPickupController.primaryHandRotation = resultRotation * primaryAnchorRotation;
                }
            }

            if (isHeldBySecondaryHand)
            {
                if (secondaryBoneExists)
                {
                    VRCPlayerApi player = pickup.controllingPlayer;
                    if (!Utilities.IsValid(player))
                        return;
                    Vector3 bonePosition = player.GetBonePosition(secondaryBone);
                    if (bonePosition == Vector3.zero)
                        return;
                    Quaternion boneRotation = player.GetBoneRotation(secondaryBone);
                    stateForPickupController.secondaryHandPosition = bonePosition + boneRotation * secondaryAnchorVector;
                    stateForPickupController.secondaryHandRotation = boneRotation * secondaryAnchorRotation;
                }
                else
                {
                    attachedTrackingDataSync.GetCurrentPosition(secondaryBone);
                    Quaternion resultRotation = attachedTrackingDataSync.resultRotation;
                    stateForPickupController.secondaryHandPosition = attachedTrackingDataSync.resultPosition + resultRotation * secondaryAnchorVector;
                    stateForPickupController.secondaryHandRotation = resultRotation * secondaryAnchorRotation;
                }
            }

            stateForPickupController.pickup = pickup;
            stateForPickupController.pickupTransform = pickup.transform;
            pickup.GetPickupController().MovePickup(stateForPickupController);
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
