using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestItemSpawner : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] EntitySystem entitySystem;
        public EntityPrototype prototype;
        public Transform spawnLocation;

        public override void Interact()
        {
            entitySystem.SendCreateEntityIA(prototype.Id, spawnLocation.position, spawnLocation.rotation);
        }
    }
}
