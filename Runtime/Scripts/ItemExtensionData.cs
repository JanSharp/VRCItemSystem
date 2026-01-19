using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
        [HideInInspector][SingletonReference] public ItemTransformController transformController;
        [HideInInspector][SingletonReference] public PlayerDataManagerAPI playerDataManager;

        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        [System.NonSerialized] public ItemExtension ext;

        [System.NonSerialized] public PhysicsEntityExtensionData physicsData;

        /// <summary>
        /// <para>When <see cref="IsAttached"/> is <see langword="true"/> this indicates whether this item is
        /// attached due to being held by player or due to being attached to a bone of that player.</para>
        /// <para>In both cases it is attached to a bone on remote remote players, except that when it is
        /// held it might not actually be attached at all and position and rotation gets synced
        /// periodically, as some avatars do not have hand (held in VR) or head (held in desktop)
        /// bones.</para>
        /// </summary>
        [System.NonSerialized] public bool isHeldSpecifically;
        public bool IsAttached => attachedToPlayerId != 0u;
        [System.NonSerialized] public uint attachedToPlayerId;
        /// <summary>
        /// <para>Part of game state, but synced through <see cref="ItemSystem"/>.</para>
        /// </summary>
        [System.NonSerialized] public int heldItemIndex;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// <para>When <see cref="isHeldSpecifically"/> is <see langword="true"/> this is guaranteed to have
        /// one of the following values: <see cref="HumanBodyBones.Head"/> (held in desktop),
        /// <see cref="HumanBodyBones.LeftHand"/> (held in VR) or <see cref="HumanBodyBones.RightHand"/>
        /// (held in VR).</para>
        /// <para>When <see cref="isHeldSpecifically"/> is <see langword="false"/> this may have any value,
        /// including head and hands.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        [System.NonSerialized] public bool attachedBoneExists;
        [System.NonSerialized] public Vector3 attachedOffsetVector;
        [System.NonSerialized] public Quaternion attachedOffsetRotation;

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

        private void ClearAttachedOffsets()
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  ClearAttachedOffsets");
#endif
            attachedOffsetVector = Vector3.zero;
            attachedOffsetRotation = Quaternion.identity;
        }

        // TODO: These write and read functions can be moved out of this file to make it instantiate faster.

        private void WriteAttachedPlayer(bool isExport)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  WriteAttachedPlayer");
#endif
            if (!isExport)
            {
                lockstep.WriteSmallUInt(attachedToPlayerId);
                return;
            }
            playerDataManager.WriteCorePlayerDataRef(playerDataManager.GetCorePlayerDataForPlayerId(attachedToPlayerId));
        }

        private void ReadAttachedPlayer(bool isImport)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  ReadAttachedPlayer");
#endif
            if (!isImport)
            {
                attachedToPlayerId = lockstep.ReadSmallUInt();
                return;
            }
            CorePlayerData playerData = playerDataManager.ReadCorePlayerDataRef(isImport: true);
            attachedToPlayerId = playerData == null || playerData.isOffline ? 0u : playerData.playerId;
        }

        public override void Serialize(bool isExport)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Serialize");
#endif
            bool isAttached = IsAttached;
            lockstep.WriteFlags(isAttached, isHeldSpecifically, attachedBoneExists);
            if (!isAttached)
                return;
            WriteAttachedPlayer(isExport);
            lockstep.WriteSmallInt((int)attachedToBone);
            if (!attachedBoneExists)
                return;
            lockstep.WriteVector3(attachedOffsetVector);
            lockstep.WriteQuaternion(attachedOffsetRotation);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
#if ITEM_SYSTEM_DEBUG
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Deserialize");
#endif
            lockstep.ReadFlags(out bool isAttached, out isHeldSpecifically, out attachedBoneExists);
            if (isAttached)
                ReadAttachedPlayer(isImport);
            attachedToBone = isAttached ? (HumanBodyBones)lockstep.ReadSmallInt() : HumanBodyBones.Head;
            if (!isAttached || !attachedBoneExists)
            {
                ClearAttachedOffsets();
                return;
            }
            attachedOffsetVector = lockstep.ReadVector3();
            attachedOffsetRotation = lockstep.ReadQuaternion();
            entityData.SetTransformSyncControllerDueToDeserialization(transformController);
            if (attachedToPlayerId != 0u)
                return;
            // Only possible for imports, where the player the item was attached to is currently not in the instance.
            entityData.GiveBackControlOfTransformSync(
                transformController,
                entityData.position,
                entityData.rotation,
                entityData.scale);
            attachedToBone = HumanBodyBones.Head; // Reset.
            ClearAttachedOffsets();
        }
    }
}
