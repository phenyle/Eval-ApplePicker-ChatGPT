using OpenAi;
using TMPro;
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

        public int numApples;
        public int counter;

        private OpenAiApi _openAI;

        private void Start()
        {
            Configuration.GlobalConfig = OpenAiApi.ReadConfigFromUserDirectory();
            _openAI = new OpenAiApi();

           InvokeRepeating(nameof(DropApple), 2f, appleDropDelay);
        }

        private async void DropApple()
        {
            counter++;
            if (counter % 50 == 0 && numApples < 1000)
            {
                numApples *= 2;
            }

            for (var i = 0; i < numApples; i++)
            {
                var completion = await _openAI.CreateCompletion("1 line greeting", OpenAiApi.Model.CHAT_GPT);

                var apple = Instantiate(applePrefab);
                apple.transform.position = Position;

                var text = apple.GetComponentInChildren<TextMeshProUGUI>();
                text.text = completion.Text == "" ? "New Apple" : completion.Text;
            }
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

            Debug.Log("log: " + numApples + "," + 1f / Time.deltaTime);
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
