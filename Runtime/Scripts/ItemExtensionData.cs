using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
        [HideInInspector][SingletonReference] public ItemTransformController transformController;
        [HideInInspector][SingletonReference] public PlayerDataManagerAPI playerDataManager;
        [HideInInspector][SingletonReference] public ItemSystem itemSystem;

        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        /// <summary>
        /// <para>Not game state safe.</para>
        /// </summary>
        [System.NonSerialized] public ItemExtension ext;

        #region Game State

        [System.NonSerialized] public PhysicsEntityExtensionData physicsData;

        /// <summary>
        /// <para><c>0u</c> when <see cref="IsHeldOrAttached"/> is <see langword="false"/>.</para>
        /// </summary>
        [System.NonSerialized] public uint attachedToPlayerId;
        /// <summary>
        /// <para>Part of game state, but synced through <see cref="ItemSystem"/>.</para>
        /// </summary>
        [System.NonSerialized] public int attachedItemIndex = DetachedItemIndex;
        public const int DetachedItemIndex = -1;

        public bool IsHeldOrAttached => attachedToPlayerId != 0u;
        public bool IsHeld => isHeldByPrimaryHand || isHeldBySecondaryHand;

        #region Primary
        [System.NonSerialized] public bool isHeldByPrimaryHand = false;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// <para>When <see cref="isHeldByPrimaryHand"/> is <see langword="true"/> this is guaranteed to have
        /// one of the following values: <see cref="HumanBodyBones.Head"/> (held in desktop),
        /// <see cref="HumanBodyBones.LeftHand"/> (held in VR) or <see cref="HumanBodyBones.RightHand"/>
        /// (held in VR).</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones primaryBone = HumanBodyBones.Head;
        /// <summary>
        /// <para>When <see langword="false"/>, <see cref="PlayerTrackingDataSyncManagerAPI"/> is used to sync
        /// the "bone" position (at that point tracking data position).</para>
        /// </summary>
        [System.NonSerialized] public bool primaryBoneExists = false;
        /// <summary>
        /// <para>In bone local space.</para>
        /// <para>When bone exists is <see langword="false"/>, this is relative to the tracking data synced
        /// through <see cref="PlayerTrackingDataSyncManagerAPI"/>.</para>
        /// </summary>
        [System.NonSerialized] public Vector3 primaryAnchorVector = Vector3.zero;
        /// <inheritdoc cref="primaryAnchorVector"/>
        [System.NonSerialized] public Quaternion primaryAnchorRotation = Quaternion.identity;
        /// <summary>
        /// <para>In hand local space, in other words relative to hand position, rotated by hand
        /// rotation.</para>
        /// </summary>
        [System.NonSerialized] public Vector3 primaryOffsetVector = Vector3.zero;
        /// <summary>
        /// <para>In hand local space, in other words relative to hand rotation.</para>
        /// </summary>
        [System.NonSerialized] public Quaternion primaryOffsetRotation = Quaternion.identity;
        #endregion

        #region Secondary
        /// <inheritdoc cref="isHeldByPrimaryHand"/>
        [System.NonSerialized] public bool isHeldBySecondaryHand = false;
        /// <inheritdoc cref="primaryBone"/>
        [System.NonSerialized] public HumanBodyBones secondaryBone = HumanBodyBones.Head;
        /// <inheritdoc cref="primaryBoneExists"/>
        [System.NonSerialized] public bool secondaryBoneExists = false;
        /// <inheritdoc cref="primaryAnchorVector"/>
        [System.NonSerialized] public Vector3 secondaryAnchorVector = Vector3.zero;
        /// <inheritdoc cref="primaryAnchorRotation"/>
        [System.NonSerialized] public Quaternion secondaryAnchorRotation = Quaternion.identity;
        /// <inheritdoc cref="primaryOffsetVector"/>
        [System.NonSerialized] public Vector3 secondaryOffsetVector = Vector3.zero;
        /// <inheritdoc cref="primaryOffsetRotation"/>
        [System.NonSerialized] public Quaternion secondaryOffsetRotation = Quaternion.identity;
        #endregion

        #region Attached
        [System.NonSerialized] public bool isAttached = false;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// <para>When <see cref="isAttached"/> is <see langword="false"/> this may have any value, including
        /// head and hands.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        /// <summary>
        /// <para>In bone local space, in other words relative to bone position, rotated by bone
        /// rotation.</para>
        /// </summary>
        [System.NonSerialized] public Vector3 attachedOffsetVector = Vector3.zero;
        /// <summary>
        /// <para>In bone local space, in other words relative to bone rotation.</para>
        /// </summary>
        [System.NonSerialized] public Quaternion attachedOffsetRotation = Quaternion.identity;
        #endregion

        #endregion

        public override bool WannaBeClassSupportsPooling => true;
        public override void ResetWannaBeClassToDefault()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  ResetWannaBeClassToDefault");
#endif
            base.ResetWannaBeClassToDefault();

            ext = default;
            physicsData = default;
            attachedToPlayerId = default;
            attachedItemIndex = DetachedItemIndex;

            ClearPrimary();
            ClearSecondary();
            ClearAttached();
        }

        private void Init()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Init");
#endif
            physicsData = entityData.GetExtensionData<PhysicsEntityExtensionData>(nameof(PhysicsEntityExtensionData));
        }

        public override void InitFromDefault(EntityExtension entityExtension)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromDefault");
#endif
            Init();
        }

        public override void InitFromPreInstantiated(EntityExtension entityExtension)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromPreInstantiated");
#endif
            Init();
        }

        public override void InitBeforeDeserialization()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitBeforeDeserialization");
#endif
            Init();
        }

        public override void OnAssociatedWithExtension()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  OnAssociatedWithExtension");
#endif
            // The only way for the pickup to be held by the player at this point is through another system
            // forcing it into their hand.
            // The item system should handle this case, however for now it does not. It should use the current
            // pickup data and initialize from that, though I'd have to think about what that implies for
            // syncing.
        }

        public void ClearPrimary()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  ClearPrimary");
#endif
            isHeldByPrimaryHand = false;
            primaryBone = HumanBodyBones.Head;
            primaryBoneExists = false;
            primaryAnchorVector = Vector3.zero;
            primaryAnchorRotation = Quaternion.identity;
            primaryOffsetVector = Vector3.zero;
            primaryOffsetRotation = Quaternion.identity;
        }

        public void ClearSecondary()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  ClearSecondary");
#endif
            isHeldBySecondaryHand = false;
            secondaryBone = HumanBodyBones.Head;
            secondaryBoneExists = false;
            secondaryAnchorVector = Vector3.zero;
            secondaryAnchorRotation = Quaternion.identity;
            secondaryOffsetVector = Vector3.zero;
            secondaryOffsetRotation = Quaternion.identity;
        }

        public void ClearAttached()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  ClearAttached");
#endif
            isAttached = false;
            attachedToBone = HumanBodyBones.Head;
            attachedOffsetVector = Vector3.zero;
            attachedOffsetRotation = Quaternion.identity;
        }

        public override void Serialize(bool isExport)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Serialize");
#endif
            bool isHeldOrAttached = IsHeldOrAttached;
            lockstep.WriteFlags(
                isHeldOrAttached,
                isHeldByPrimaryHand,
                primaryBoneExists,
                isHeldBySecondaryHand,
                secondaryBoneExists,
                isAttached);
            if (!isHeldOrAttached)
                return;

            itemSystem.WriteAttachedPlayerRef(this, isExport);

            if (isHeldByPrimaryHand)
            {
                lockstep.WriteSmallInt((int)primaryBone);
                lockstep.WriteVector3(primaryAnchorVector);
                lockstep.WriteQuaternion(primaryAnchorRotation);
                lockstep.WriteVector3(primaryOffsetVector);
                lockstep.WriteQuaternion(primaryOffsetRotation);
            }

            if (isHeldBySecondaryHand)
            {
                lockstep.WriteSmallInt((int)secondaryBone);
                lockstep.WriteVector3(secondaryAnchorVector);
                lockstep.WriteQuaternion(secondaryAnchorRotation);
                lockstep.WriteVector3(secondaryOffsetVector);
                lockstep.WriteQuaternion(secondaryOffsetRotation);
            }

            if (isAttached)
            {
                lockstep.WriteSmallInt((int)attachedToBone);
                lockstep.WriteVector3(attachedOffsetVector);
                lockstep.WriteQuaternion(attachedOffsetRotation);
            }
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Deserialize");
#endif
            lockstep.ReadFlags(
                out bool isHeldOrAttached,
                out isHeldByPrimaryHand,
                out primaryBoneExists,
                out isHeldBySecondaryHand,
                out secondaryBoneExists,
                out isAttached);
            if (!isHeldOrAttached)
            {
                itemSystem.SetAttachedToPlayerId(this, 0u);
                ClearPrimary();
                ClearSecondary();
                ClearAttached();
                return;
            }

            itemSystem.ReadAttachedPlayerRef(this, isImport);

            if (isHeldByPrimaryHand)
            {
                primaryBone = (HumanBodyBones)lockstep.ReadSmallInt();
                primaryAnchorVector = lockstep.ReadVector3();
                primaryAnchorRotation = lockstep.ReadQuaternion();
                primaryOffsetVector = lockstep.ReadVector3();
                primaryOffsetRotation = lockstep.ReadQuaternion();
            }
            else if (isImport) // For non imports the values are already the defaults, clearing would do nothing.
                ClearPrimary();

            if (isHeldBySecondaryHand)
            {
                secondaryBone = (HumanBodyBones)lockstep.ReadSmallInt();
                secondaryAnchorVector = lockstep.ReadVector3();
                secondaryAnchorRotation = lockstep.ReadQuaternion();
                secondaryOffsetVector = lockstep.ReadVector3();
                secondaryOffsetRotation = lockstep.ReadQuaternion();
            }
            else if (isImport) // For non imports the values are already the defaults, clearing would do nothing.
                ClearSecondary();

            if (isAttached)
            {
                attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
                attachedOffsetVector = lockstep.ReadVector3();
                attachedOffsetRotation = lockstep.ReadQuaternion();
            }
            else if (isImport) // For non imports the values are already the defaults, clearing would do nothing.
                ClearAttached();

            entityData.SetTransformSyncControllerDueToDeserialization(transformController);
            if (attachedToPlayerId != 0u)
                return;
            // Only possible for imports, where the player the item was attached to is currently not in the instance.
            entityData.GiveBackControlOfTransformSync(
                transformController,
                entityData.position,
                entityData.rotation,
                entityData.scale);
            ClearPrimary();
            ClearSecondary();
            ClearAttached();
        }
    }
}
