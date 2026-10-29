using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestPlateEntity : UdonSharpBehaviour
    {
        [HideInInspector][SingletonReference] public LockstepAPI lockstep;
        [HideInInspector][FindInParent] public Entity entity;

        private Entity[] watchedEntities = new Entity[ArrList.MinCapacity];
        private int watchedEntitiesCount = 0;

        public void OnTriggerEnter(Collider other)
        {
            Debug.Log("[ItemSystemDebug] TestPlateEntity  OnTriggerEnter");
            if (other == null) // Some VRChat internal, if it even raises the event for those, but we have no trust.
                return;
            Entity otherEntity = other.GetComponentInParent<Entity>();
            if (otherEntity == null || otherEntity == entity || otherEntity.entityData == null) // Checking entityData, coming from an untrusted event.
                return;

            if (otherEntity.transformSyncController == null) // Not controlled by anything else, immediately attach.
            {
                SendSetParentIAIfInControl(otherEntity);
                return;
            }

            if (ArrList.Contains(ref watchedEntities, ref watchedEntitiesCount, otherEntity))
                return;
            ArrList.Add(ref watchedEntities, ref watchedEntitiesCount, otherEntity);
            otherEntity.RegisterForRuntimeEvent(this, Entity.OnLatencyTransformSyncControllerChangedLastFrameEventName);
        }

        public void OnTriggerExit(Collider other)
        {
            Debug.Log("[ItemSystemDebug] TestPlateEntity  OnTriggerExit");
            if (other == null) // Some VRChat internal, if it even raises the event for those, but we have no trust.
                return;
            Entity otherEntity = other.GetComponentInParent<Entity>();
            if (otherEntity == null)
                return;
            if (ArrList.Remove(ref watchedEntities, ref watchedEntitiesCount, otherEntity) != -1)
                otherEntity.DeregisterFromRuntimeEvent(this, Entity.OnLatencyTransformSyncControllerChangedLastFrameEventName);
        }

        [EntityRuntimeEventListener(Entity.OnLatencyTransformSyncControllerChangedLastFrameEventName)]
        public void OnLatencyTransformSyncControllerChangedLastFrame()
        {
            Debug.Log("[ItemSystemDebug] TestPlateEntity  OnLatencyTransformSyncControllerChangedLastFrame");
            for (int i = watchedEntitiesCount - 1; i >= 0; i--)
            {
                Entity otherEntity = watchedEntities[i];
                if (otherEntity.entityData == null) // Got destroyed.
                {
                    ArrList.RemoveAt(ref watchedEntities, ref watchedEntitiesCount, i);
                    continue;
                }
                if (otherEntity.transformSyncController != null) // Still controlled by something.
                    continue;
                ArrList.RemoveAt(ref watchedEntities, ref watchedEntitiesCount, i);
                otherEntity.DeregisterFromRuntimeEvent(this, Entity.OnLatencyTransformSyncControllerChangedLastFrameEventName);
                SendSetParentIAIfInControl(otherEntity);
            }
        }

        private void SendSetParentIAIfInControl(Entity otherEntity)
        {
            Debug.Log("[ItemSystemDebug] TestPlateEntity  SendSetParentIAIfInControl");
            EntitySystemPlayerData lastUser = otherEntity.entityData.LastUserPlayerData;
            if (lastUser == null ? lockstep.IsMaster : lastUser.core.isLocal)
                otherEntity.SendSetParentEntityIA(entity, doInterpolateRemotely: true);
        }
    }
}
