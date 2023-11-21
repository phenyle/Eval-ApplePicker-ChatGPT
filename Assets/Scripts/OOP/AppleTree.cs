using UnityEngine;

namespace OOP
{
    public class AppleTree : MonoBehaviour
    {
        [Header("Apple Tree Properties")]
        public float speed;
        public float leftAndRightEdge;
        public float changeDirectionChance;

        [Header("Apple Drop Settings")]
        public GameObject applePrefab;
        public float appleDropDelay;

        private void Start()
        {
            Invoke(nameof(DropApple), 2f);
        }

        private void DropApple()
        {
            var apple = Instantiate(applePrefab);
            apple.transform.position = Position;
            Invoke(nameof(DropApple), appleDropDelay);
        }

        private void Update()
        {
            var pos = Position;
            pos.x += speed * Time.deltaTime;
            Position = pos;

            if (pos.x < -leftAndRightEdge)
            {
                speed = Mathf.Abs(speed);
            }
            else if (pos.x > leftAndRightEdge)
            {
                speed = -Mathf.Abs(speed);
            }
        }

        private void FixedUpdate()
        {
            if (Random.value < changeDirectionChance)
            {
                speed *= -1;
            }
        }

        private Vector3 Position
        {
            get => transform.position;
            set => transform.position = value;
        }
    }
}
