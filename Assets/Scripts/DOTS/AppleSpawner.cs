using OpenAi;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DOTS
{
    public class AppleSpawner : MonoBehaviour
    {
        public GameObject applePrefab;
        public bool useOpenAI;

        private OpenAiApi _openAI;

        private void Awake()
        {
            if (useOpenAI)
            {
                Configuration.GlobalConfig = OpenAiApi.ReadConfigFromUserDirectory();
                _openAI = new OpenAiApi();
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
                var completion = await _openAI.CreateCompletion("1 line greeting", OpenAiApi.Model.CHAT_GPT);
                completionText = completion.Text == "" ? "New Apple" : completion.Text;
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
