using TMPro;
using UnityEngine;
using OpenAi;

namespace OOP
{
    public class Label : MonoBehaviour
    {
        private static TextMeshProUGUI _text;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
        }

        private void Start()
        {
            Generate();
        }

        private static async void Generate()
        {
            var openAI = new OpenAiApi();
            var completion = await openAI.CreateCompletion("1 line greeting", OpenAiApi.Model.CHAT_GPT);
            _text.text = completion.choices[0].text;
        }
    }
}
