using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
        [HideInInspector][SingletonReference] public ItemSystem itemSystem;
        [HideInInspector][SingletonReference] public UpdateManager updateManager;

        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        [System.NonSerialized] public ItemExtension ext;

        [System.NonSerialized] public PhysicsEntityExtensionData physicsData;

        [System.NonSerialized] public uint attachedToPlayerId;
        /// <summary>
        /// <para>Part of game state, but synced through <see cref="ItemSystem"/>.</para>
        /// </summary>
        [System.NonSerialized] public int heldItemIndex;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        [System.NonSerialized] public bool attachedBoneExists;
        [System.NonSerialized] public Vector3 attachedOffsetVector;
        [System.NonSerialized] public Quaternion attachedOffsetRotation;

        private void Init()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Init");
#endif
            physicsData = entityData.GetExtensionData<PhysicsEntityExtensionData>(nameof(PhysicsEntityExtensionData));
        }

        public override void InitFromDefault(EntityExtension entityExtension)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromDefault");
#endif
            Init();
        }

        public override void InitFromPreInstantiated(EntityExtension entityExtension)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromPreInstantiated");
#endif
            Init();
        }

        public override void InitBeforeDeserialization()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitBeforeDeserialization");
#endif
            Init();
        }

        public override void OnAssociatedWithExtension()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  OnAssociatedWithExtension");
#endif
            // The only way for the pickup to be held by the player at this point is through another system
            // forcing it into their hand.
            // The item system should handle this case, however for now it does not. It should use the current
            // pickup data and initialize from that, though I'd have to think about what that implies for
            // syncing.
        }

        public override void Serialize(bool isExport)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Serialize");
#endif
            bool isAttached = attachedToPlayerId != 0u;
            lockstep.WriteFlags(isAttached, attachedBoneExists);
            if (!isAttached)
                return;
            lockstep.WriteSmallUInt(attachedToPlayerId);
            lockstep.WriteSmallInt((int)attachedToBone);
            if (!attachedBoneExists)
                return;
            lockstep.WriteVector3(attachedOffsetVector);
            lockstep.WriteQuaternion(attachedOffsetRotation);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Deserialize");
#endif
            if (!isImport)
                Init();
            lockstep.ReadFlags(out bool isAttached, out attachedBoneExists);
            attachedToPlayerId = isAttached ? lockstep.ReadSmallUInt() : 0u;
            attachedToBone = isAttached ? (HumanBodyBones)lockstep.ReadSmallInt() : HumanBodyBones.Head;
            if (isAttached && attachedBoneExists)
            {
                attachedOffsetVector = lockstep.ReadVector3();
                attachedOffsetRotation = lockstep.ReadQuaternion();
                entityData.SetTransformSyncControllerDueToDeserialization(itemSystem.transformController);
            }
            else
            {
                attachedOffsetVector = Vector3.zero;
                attachedOffsetRotation = Quaternion.identity;
            }
        }
    }
}
