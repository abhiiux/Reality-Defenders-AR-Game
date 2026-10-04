using System;
using UnityEngine;

namespace RD.Core
{
    [RequireComponent(typeof(Rigidbody))]
    public class ProjectileScript : MonoBehaviour
    {
        [SerializeField] private float lifetime = 10f;
        [SerializeField] private GameObject explosionPrefab; 

        private ParticleSystem explosionEffectParticle;
        private float timer;
        private bool isReturning = true; // inactive until fired
        private Rigidbody rb;

        public event Action<ProjectileScript> ReturnToPool;
        public Rigidbody Rigidbody => rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            explosionEffectParticle = explosionPrefab.GetComponent<ParticleSystem>();
        }

        void Update()
        {
            if (isReturning) return;

            timer -= Time.deltaTime;
            if (timer <= 0f) ReturnProjectile();
        }

        void OnCollisionEnter(Collision _other)
        {
            if(_other.gameObject.tag == "Player")
            {
                _other.gameObject.SetActive(false);
                PlayParticle( _other.transform.position );
                ReturnProjectile();

                GameEvent.TriggerAddScore();
            }
        }

        public void Launch(Vector3 position, Quaternion rotation, Vector3 impulse)
        {
            transform.SetPositionAndRotation(position, rotation);

            // Unity 6: linearVelocity. On older versions use rb.velocity.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(impulse, ForceMode.Impulse);

            timer = lifetime;
            isReturning = false;
        }
        private void PlayParticle(Vector3 position)
        {
            ParticleSystem effectInstance = Instantiate(explosionEffectParticle, position, Quaternion.identity);
        
            // Starts playback
            effectInstance.Play(true);
        }
        private void ReturnProjectile()
        {
            if (isReturning) return;
            isReturning = true;
            ReturnToPool?.Invoke(this);
        }
    }
}