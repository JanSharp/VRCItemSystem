using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class BulkSpawner : UdonSharpBehaviour
    {
        public ItemSystem itemSystem;
        public int prefabIndex;
        public Transform bottomLeft;

        public override void Interact()
        {
            float bottom = bottomLeft.position.y;
            float left = bottomLeft.position.x;
            float z = bottomLeft.position.z;
            Quaternion rotation = bottomLeft.rotation;
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 100; x++)
                    itemSystem.SendSpawnItemIA(
                        prefabIndex,
                        new Vector3(left + x, bottom + y, z),
                        rotation
                    );
        }
    }
}
