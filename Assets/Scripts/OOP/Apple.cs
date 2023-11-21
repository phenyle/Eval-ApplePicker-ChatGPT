using UnityEngine;

namespace OOP
{
    public class Apple : MonoBehaviour
    {
       public float bottomY;

        private void Update()
        {
            if (transform.position.y < bottomY)
            {
                Destroy(gameObject);
            }
        }
    }
}
