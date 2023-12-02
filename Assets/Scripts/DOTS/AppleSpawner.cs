using System;
using System.Collections.Generic;
using OpenAi;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DOTS
{
    public class AppleSpawner : MonoBehaviour
    {
        public GameObject applePrefab;
        public bool useOpenAI;
        public bool realTime = true;

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
        }

        private void OnEnable()
        {
            var appleSpawnSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<AppleSpawnerSystem>();
            appleSpawnSystem.SpawnApple += SpawnApple;
        }

        private void OnDisable()
        {
            if (World.DefaultGameObjectInjectionWorld == null) return;

            var appleSpawnSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<AppleSpawnerSystem>();
            appleSpawnSystem.SpawnApple -= SpawnApple;
        }

        private async void SpawnApple(float3 position)
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
            apple.transform.position = new Vector3(position.x, position.y, position.z);

            if (useOpenAI)
            {
                var text = apple.GetComponentInChildren<TextMeshProUGUI>();
                text.text = completionText == "" ? "New Apple" : completionText;
            }
        }
    }
}
