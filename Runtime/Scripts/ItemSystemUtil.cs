using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    public static class ItemSystemUtil
    {
        public static void TrackingDataOffsetsToBoneOffsets(
            VRCPlayerApi localPlayer,
            VRCPlayerApi.TrackingDataType trackingType,
            HumanBodyBones bone,
            Vector3 offsetVector,
            Quaternion offsetRotation,
            out Vector3 resultVector,
            out Quaternion resultRotation)
        {
            VRCPlayerApi.TrackingData trackingData = localPlayer.GetTrackingData(trackingType);
            Vector3 worldPosition = trackingData.position + trackingData.rotation * offsetVector;
            Quaternion worldRotation = trackingData.rotation * offsetRotation;
            Vector3 bonePosition = localPlayer.GetBonePosition(bone);
            Quaternion inverseBoneRotation = Quaternion.Inverse(localPlayer.GetBoneRotation(bone));
            resultVector = inverseBoneRotation * (worldPosition - bonePosition);
            resultRotation = inverseBoneRotation * worldRotation;
        }

        public static void BoneOffsetsToTrackingDataOffsets(
            VRCPlayerApi localPlayer,
            VRCPlayerApi.TrackingDataType trackingType,
            HumanBodyBones bone,
            // When this is false - even if the bone exists now - the offsets
            // were calculated relative to tracking data and should remain as such.
            bool boneExists,
            Vector3 offsetVector,
            Quaternion offsetRotation,
            out Vector3 resultVector,
            out Quaternion resultRotation)
        {
            Vector3 bonePosition = localPlayer.GetBonePosition(bone);
            if (!boneExists || bonePosition == Vector3.zero)
            {
                resultVector = offsetVector;
                resultRotation = offsetRotation;
                return;
            }
            Quaternion boneRotation = localPlayer.GetBoneRotation(bone);
            Vector3 worldPosition = bonePosition + boneRotation * offsetVector;
            Quaternion worldRotation = boneRotation * offsetRotation;
            VRCPlayerApi.TrackingData trackingData = localPlayer.GetTrackingData(trackingType);
            Quaternion inverseTrackingDataRotation = Quaternion.Inverse(trackingData.rotation);
            resultVector = inverseTrackingDataRotation * (worldPosition - trackingData.position);
            resultRotation = inverseTrackingDataRotation * worldRotation;
        }

        public static HumanBodyBones TrackingTypeToBone(VRCPlayerApi.TrackingDataType trackingType)
        {
            return trackingType == VRCPlayerApi.TrackingDataType.LeftHand ? HumanBodyBones.LeftHand
                : trackingType == VRCPlayerApi.TrackingDataType.RightHand ? HumanBodyBones.RightHand
                : HumanBodyBones.Head;
        }

        public static VRCPlayerApi.TrackingDataType BoneToTrackingType(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftHand ? VRCPlayerApi.TrackingDataType.LeftHand
                : bone == HumanBodyBones.RightHand ? VRCPlayerApi.TrackingDataType.RightHand
                : VRCPlayerApi.TrackingDataType.Head;
        }
    }
}
