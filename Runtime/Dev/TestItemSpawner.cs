using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestItemSpawner : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private EntitySystemAPI entitySystem;
        public EntityPrototype prototype;
        public Transform spawnLocation;

        public override void Interact()
        {
            entitySystem.SendCreateEntityIA(prototype.Id, spawnLocation.position, spawnLocation.rotation);
        }
    }
}
