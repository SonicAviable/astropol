using UnityEngine;

namespace StellarisClone.Rendering
{
    public class PlanetRotator : MonoBehaviour
    {
        [SerializeField] private float rotationSpeed = 8f;
        private Vector3 _axis = Vector3.up;

        private void Start()
        {
            // Случайный наклон оси вращения планеты (как у Земли 23.5°)
            _axis = Quaternion.Euler(Random.Range(-15f, 15f), 0, Random.Range(-25f, 25f)) * Vector3.up;
            rotationSpeed = Random.Range(4f, 12f);
        }

        private void Update()
        {
            transform.Rotate(_axis, rotationSpeed * Time.deltaTime, Space.World);
        }
    }
}