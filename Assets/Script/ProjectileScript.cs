using System;
using UnityEngine;

namespace RD.Core
{
    [RequireComponent(typeof(Rigidbody))]
    public class ProjectileScript : MonoBehaviour
    {
        public enum ProjectileState
        {
            Inactive,   // in pool, waiting to be fired
            Flying,     // launched, in the air
            Hit,        // hit the target
            Miss        // flew past max distance
        }

        [SerializeField] private float lifetime = 10f;
        [SerializeField] private float maxDistance = 5f;
        [SerializeField] private GameObject explosionPrefab;

        private ParticleSystem explosionEffectParticle;
        private Rigidbody rb;
        private Vector3 launchPosition;
        private float timer;

        public ProjectileState State { get; private set; } = ProjectileState.Inactive;

        public event Action<ProjectileScript> ReturnToPool;
        public Rigidbody Rigidbody => rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            explosionEffectParticle = explosionPrefab.GetComponent<ParticleSystem>();
        }

        void Update()
        {
            if (State != ProjectileState.Flying) return;

            // Miss: travelled more than maxDistance from the launch point
            if ((transform.position - launchPosition).sqrMagnitude > maxDistance * maxDistance)
            {
                SetState(ProjectileState.Miss);
                return;
            }

            // Safety net
            timer -= Time.deltaTime;
            if (timer <= 0f) SetState(ProjectileState.Miss);
        }

        void OnCollisionEnter(Collision _other)
        {
            if (State != ProjectileState.Flying) return;

            if (_other.gameObject.tag == "Player")
            {
                SetState(ProjectileState.Hit, _other.gameObject);
            }
        }

        public void Launch(Vector3 position, Quaternion rotation, Vector3 impulse)
        {
            transform.SetPositionAndRotation(position, rotation);
            launchPosition = position;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(impulse, ForceMode.Impulse);

            timer = lifetime;
            SetState(ProjectileState.Flying);
        }

        private void SetState(ProjectileState newState, GameObject target = null)
        {
            if (State == newState) return;
            State = newState;

            switch (State)
            {
                case ProjectileState.Hit:
                    target.SetActive(false);
                    PlayParticle(target.transform.position);
                    GameEvent.TriggerAddScore();
                    ReturnProjectile();
                    break;

                case ProjectileState.Miss:
                    ReturnProjectile();
                    break;
            }
        }

        private void PlayParticle(Vector3 position)
        {
            ParticleSystem effectInstance = Instantiate(explosionEffectParticle, position, Quaternion.identity);

            // Starts playback
            effectInstance.Play(true);
        }

        private void ReturnProjectile()
        {
            ReturnToPool?.Invoke(this);
            State = ProjectileState.Inactive; // ready for reuse from the pool
        }
    }
}