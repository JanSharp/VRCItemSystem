using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
        [HideInInspector] [SingletonReference] public BoneAttachmentManager boneAttachment;

        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        public ItemExtension Extension => (ItemExtension)extension;

        [System.NonSerialized] public uint holdingPlayerId;
        [System.NonSerialized] public bool heldInRightHand;
        [System.NonSerialized] public Vector3 heldOffsetVector;
        [System.NonSerialized] public Quaternion heldOffsetRotation;

        public override void InitFromExtension()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromExtension");
            #endif
            // Cannot be held at point, at least as of right now, so just do nothing.
            // Otherwise it would have to read data from the CustomPickup and initialize using that.
        }

        public override void Serialize(bool isExport)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Serialize");
            #endif
            lockstep.WriteSmallUInt(holdingPlayerId);
            if (holdingPlayerId == 0u)
                return;
            lockstep.WriteFlags(heldInRightHand);
            lockstep.WriteVector3(heldOffsetVector);
            lockstep.WriteQuaternion(heldOffsetRotation);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Deserialize");
            #endif
            holdingPlayerId = lockstep.ReadSmallUInt();
            if (holdingPlayerId == 0u)
                return;
            lockstep.ReadFlags(out heldInRightHand);
            heldOffsetVector = lockstep.ReadVector3();
            heldOffsetRotation = lockstep.ReadQuaternion();
        }
    }
}
