using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
        [HideInInspector] [SingletonReference] public ItemSystem itemSystem;

        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        public ItemExtension Extension => (ItemExtension)extension;

        [System.NonSerialized] public uint attachedToPlayerId;
        [System.NonSerialized] public bool attachedBoneExists;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        [System.NonSerialized] public Vector3 attachedOffsetVector;
        [System.NonSerialized] public Quaternion attachedOffsetRotation;

        public override void InitFromExtension()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromExtension");
            #endif
            // The only way for the pickup to be held by the player at this point is through another system
            // forcing it into their hand.
            // The item system should handle this case, however for now it does not. It should use the current
            // pickup data and initialize from that, though I'd have to think about what that implies for
            // syncing.
            Extension.ActualStart();
            Extension.pickup.DecrementPreventInteraction();
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
            lockstep.ReadFlags(out bool isAttached, out attachedBoneExists);
            attachedToPlayerId = isAttached ? lockstep.ReadSmallUInt() : 0u;
            attachedToBone = isAttached ? (HumanBodyBones)lockstep.ReadSmallInt() : HumanBodyBones.Head;
            if (isAttached && attachedBoneExists)
            {
                attachedOffsetVector = lockstep.ReadVector3();
                attachedOffsetRotation = lockstep.ReadQuaternion();
            }
            else
            {
                attachedOffsetVector = Vector3.zero;
                attachedOffsetRotation = Quaternion.identity;
            }
        }
    }
}
