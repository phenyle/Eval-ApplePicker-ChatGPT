using System;
using System.Collections.Generic;
using OpenAi;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

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
        public bool useOpenAI;
        public bool realTime = true;

        public int numApples;
        public int counter;

        private OpenAiApi _openAI;
        private List<string> _labels = new();

        private async void Start()
        {
            if (useOpenAI)
            {
                Configuration.GlobalConfig = OpenAiApi.ReadConfigFromUserDirectory();
                _openAI = new OpenAiApi();

                if (!realTime)
                {
                    for (var i = 0; i < 500; i++)
                    {
                        var completion = await _openAI.CreateCompletion("1 line greeting", OpenAiApi.Model.CHAT_GPT);
                        _labels.Add(completion.Text == "" ? "New Apple" : completion.Text);
                        Debug.LogFormat("added {0} completions", i + 1);
                    }
                }
            }

            InvokeRepeating(nameof(DropApple), 2f, appleDropDelay);
        }

        private async void DropApple()
        {
            counter++;
            if (counter % 20 == 0 && numApples < 1000) numApples += 5;

            for (var i = 0; i < numApples; i++)
            {
                var completionText = "";
                if (useOpenAI)
                {
                    if (realTime)
                    {
                        var completion = await _openAI.CreateCompletion("1 line greeting", OpenAiApi.Model.CHAT_GPT);
                        completionText = completion.Text == "" ? "New Apple" : completion.Text;
                    }
                    else
                    {
                        completionText = _labels[(Index)Random.Range(0, _labels.Count - 1)];
                    }
                }

                var apple = Instantiate(applePrefab);
                apple.transform.position = Position;

                if (useOpenAI)
                {
                    var text = apple.GetComponentInChildren<TextMeshProUGUI>();
                    text.text = completionText;
                }
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
