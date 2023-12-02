using UnityEngine;

namespace OOP
{
    public class Apple : MonoBehaviour
    {
       public float bottomY;

        private void Update()
        {
            var pos = Position;
            pos.y += Time.deltaTime * -9.81f;
            Position = pos;
            if (pos.y < bottomY)
            {
                Destroy(gameObject);
            }
        }

        private Vector3 Position
        {
            get => transform.position;
            set => transform.position = value;
        }
    }
}
