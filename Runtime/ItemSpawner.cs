using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemSpawner : UdonSharpBehaviour
    {
        public ItemSystem itemSystem;
        public int prefabIndex;
        public Transform spawnAreaCornerOne;
        public Transform spawnAreaCornerTwo;

        public override void Interact()
        {
            Vector3 one = spawnAreaCornerOne.position;
            Vector3 two = spawnAreaCornerTwo.position;
            itemSystem.SendSpawnItemIA(
                prefabIndex,
                new Vector3(
                    Random.Range(Mathf.Min(one.x, two.x), Mathf.Max(one.x, two.x)),
                    Random.Range(Mathf.Min(one.y, two.y), Mathf.Max(one.y, two.y)),
                    Random.Range(Mathf.Min(one.z, two.z), Mathf.Max(one.z, two.z))
                ),
                Quaternion.Lerp(spawnAreaCornerOne.rotation, spawnAreaCornerTwo.rotation, Random.Range(0f, 1f))
            );
        }
    }
}
