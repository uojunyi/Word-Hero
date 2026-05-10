using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Data structure for a complete question
[Serializable]
public class QuestionData
{
    public string questionText;
    public string[] options;          // 4 options
    public int correctOptionIndex;    // 0, 1, 2, or 3
    public string explanation;
    public int towerLevel;
}

// Response format from Hugging Face API
[Serializable]
public class HuggingFaceResponse
{
    public string generated_text;
}

// Request format for Hugging Face API
[Serializable]
public class HuggingFaceParameters
{
    public int max_new_tokens;
    public float temperature;
    public bool do_sample;
}

[Serializable]
public class HuggingFaceRequest
{
    public string inputs;
    public HuggingFaceParameters parameters;
}

public class QuestionGenerator : MonoBehaviour
{
    [Header("Hugging Face Configuration")]
    [Tooltip("Get your free API key from https://huggingface.co/settings/tokens")]
    private string apiKey;

    [Tooltip("Model to use - Mistral works well for Q&A")]
    private string modelEndpoint;

    [Tooltip("Fallback to local questions if API fails")]
    public bool useLocalFallback = true;

    [Header("Local Fallback Questions (Optional)")]
    public QuestionData[] localQuestions;

    // Internal queue for handling multiple requests
    private Queue<Action<QuestionData>> pendingCallbacks = new Queue<Action<QuestionData>>();
    private bool isRequesting = false;
    private int apiCallCount = 0;

    // Topics based on your hackathon themes
    private string[] topics = new string[]
    {
        "climate resilience and disaster preparedness",
        "healthcare accessibility",
        "financial inclusion",
        "sustainable cities and infrastructure",
        "education technology",
        "digital equity and global connectivity"
    };

    private void Awake()
    {
        LoadApiKey();
    }

    private void LoadApiKey()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "huggingface_key.txt");
        string endpointPath = System.IO.Path.Combine(Application.streamingAssetsPath, "huggingface_modelEndpoint.txt");

        if (System.IO.File.Exists(path) && System.IO.File.Exists(endpointPath))
        {
            apiKey = System.IO.File.ReadAllText(path).Trim();
            modelEndpoint = System.IO.File.ReadAllText(endpointPath).Trim();
            Debug.Log("API Key and Model End point Loaded Successfully");
        }
        else
        {
            Debug.LogError("Missing huggingface_key.txt or huggingface_modelEndpoint.txt");
        }
    }

    /// <summary>
    /// Request a generated question for a specific tower level
    /// </summary>
    public void RequestQuestionForLevel(int level, Action<QuestionData> onComplete)
    {
        pendingCallbacks.Enqueue(onComplete);

        if (!isRequesting)
        {
            StartCoroutine(GenerateQuestionCoroutine(level));
        }
    }

    private IEnumerator GenerateQuestionCoroutine(int level)
    {
        isRequesting = true;
        apiCallCount++;

        Debug.Log($"Generating question for level {level} (Request #{apiCallCount})");

        // Build the prompt
        string prompt = BuildEducationalPrompt(level);

        // Create request body
        string jsonBody = $@"
        {{
            ""inputs"": ""{prompt.Replace("\"", "\\\"").Replace("\n", "\\n")}"",
            ""parameters"": {{
                ""max_new_tokens"": 200,
                ""temperature"": 0.7,
                ""do_sample"": true
            }}
        }}";

        using (UnityWebRequest request = new UnityWebRequest(modelEndpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            // Send request with timeout
            request.timeout = 10;
            yield return request.SendWebRequest();

            QuestionData result = null;

            // Check if request was successful
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                Debug.Log("RAW RESPONSE: " + responseText);
                result = ParseHuggingFaceResponse(responseText, level);

                if (result != null)
                {
                    Debug.Log($"Generated question for level {level}: {result.questionText}");
                }
                else
                {
                    Debug.LogWarning("Failed to parse Hugging Face response, using fallback");
                    result = GetFallbackQuestion(level);
                }
            }
            else
            {
                // API failed - use local fallback
                Debug.LogWarning($"Hugging Face API error: {request.error}");
                if (useLocalFallback)
                {
                    result = GetFallbackQuestion(level);
                }
                else
                {
                    result = CreateEmergencyQuestion(level);
                }
            }

            // Invoke all queued callbacks
            while (pendingCallbacks.Count > 0)
            {
                var callback = pendingCallbacks.Dequeue();
                callback?.Invoke(result);
            }

            isRequesting = false;
        }
    }

    /// <summary>
    /// Builds an educational prompt for the AI model
    /// </summary>
    private string BuildEducationalPrompt(int level)
    {
        string difficulty = level < 3 ? "easy" :
                           (level < 7 ? "medium" : "hard");

        return $@"
        You are an educational vocabulary quiz generator.

        Generate ONE multiple-choice vocabulary question.

        The quiz format:
        - Give a definition.
        - Player must choose the correct word.
        - Provide exactly 4 answer choices.
        - Only ONE answer is correct.

        Difficulty: {difficulty}

        IMPORTANT:
        You MUST follow this exact format.

        QUESTION: [definition here]
        A) [word]
        B) [word]
        C) [word]
        D) [word]
        CORRECT: [A or B or C or D]
        EXPLANATION: [brief explanation of the correct word]

        Example:

        QUESTION: What word means 'a strong feeling of happiness or excitement'?
        A) Anger
        B) Joy
        C) Fear
        D) Sleep
        CORRECT: B
        EXPLANATION: Joy means a feeling of great happiness.

        Now generate a new vocabulary question:
        ";
    }

    /// <summary>
    /// Parses the AI response into a QuestionData object
    /// </summary>
    private QuestionData ParseHuggingFaceResponse(string jsonResponse, int level)
    {
        try
        {
            // Hugging Face returns an array of responses
            string generatedText = ExtractGeneratedText(jsonResponse);

            if (string.IsNullOrEmpty(generatedText))
            {
                Debug.LogError("Empty response from Hugging Face");
                return null;
            }

            // Parse the formatted response
            string[] lines = generatedText.Split('\n');

            string question = "";
            string[] options = new string[4];
            string correctLetter = "";
            string explanation = "";

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("QUESTION:"))
                    question = trimmed.Substring(9).Trim();
                else if (trimmed.StartsWith("A)") || trimmed.StartsWith("A."))
                    options[0] = trimmed.Substring(2).Trim();

                else if (trimmed.StartsWith("B)") || trimmed.StartsWith("B."))
                    options[1] = trimmed.Substring(2).Trim();

                else if (trimmed.StartsWith("C)") || trimmed.StartsWith("C."))
                    options[2] = trimmed.Substring(2).Trim();

                else if (trimmed.StartsWith("D)") || trimmed.StartsWith("D."))
                    options[3] = trimmed.Substring(2).Trim();
                else if (trimmed.StartsWith("CORRECT:"))
                    correctLetter = trimmed.Substring(8).Trim().ToUpper();
                else if (trimmed.StartsWith("EXPLANATION:"))
                    explanation = trimmed.Substring(12).Trim();
            }

            // Validate we have all parts
            if (string.IsNullOrEmpty(question) ||
                options[0] == null || options[1] == null ||
                options[2] == null || options[3] == null ||
                string.IsNullOrEmpty(correctLetter))
            {
                Debug.LogWarning("Incomplete response from AI, missing fields");
                return null;
            }

            // Convert letter to index (A=0, B=1, C=2, D=3)
            int correctIndex = correctLetter[0] - 'A';

            QuestionData qd = new QuestionData();
            qd.questionText = question;
            qd.options = options;
            qd.correctOptionIndex = correctIndex;
            qd.explanation = string.IsNullOrEmpty(explanation) ? "No explanation provided." : explanation;
            qd.towerLevel = level;

            return qd;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing Hugging Face response: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts just the generated text from Hugging Face's response format
    /// </summary>
    private string ExtractGeneratedText(string jsonResponse)
    {
        // Hugging Face returns: [{"generated_text": "..."}]
        try
        {
            // Parse as array
            var wrapper = JsonUtility.FromJson<HuggingFaceResponseWrapper>(jsonResponse);
            if (wrapper != null && wrapper.responses != null && wrapper.responses.Length > 0)
            {
                return wrapper.responses[0].generated_text;
            }
        }
        catch
        {
            // Fallback: manual extraction
            int startIndex = jsonResponse.IndexOf("\"generated_text\":\"");
            if (startIndex == -1)
                startIndex = jsonResponse.IndexOf("\"generated_text\": \"");

            if (startIndex != -1)
            {
                startIndex = jsonResponse.IndexOf("\"", startIndex + 16) + 1;
                int endIndex = jsonResponse.IndexOf("\"", startIndex);
                if (endIndex != -1)
                {
                    return jsonResponse.Substring(startIndex, endIndex - startIndex)
                                   .Replace("\\n", "\n")
                                   .Replace("\\\"", "\"");
                }
            }
        }

        return jsonResponse;
    }

    /// <summary>
    /// Returns a fallback question when API fails
    /// </summary>
    private QuestionData GetFallbackQuestion(int level)
    {
        if (localQuestions != null && localQuestions.Length > 0)
        {
            int index = (level - 1) % localQuestions.Length;
            QuestionData qd = localQuestions[index];
            qd.towerLevel = level;
            return qd;
        }

        return CreateEmergencyQuestion(level);
    }

    /// <summary>
    /// Ultra-fallback hardcoded questions (never fails)
    /// </summary>
    private QuestionData CreateEmergencyQuestion(int level)
    {
        QuestionData qd = new QuestionData();

        string[] questions =
        {
        "What word means 'the ability to recover quickly from difficulties'?",
        "What word means 'a person who studies stars and space'?",
        "What word means 'careful use of money or resources'?",
        "What word means 'a place where books are kept for reading'?",
        "What word means 'showing kindness and concern for others'?",
        "What word means 'a large natural stream of water'?"
    };

        string[][] options =
        {
        new string[] { "Resilience", "Confusion", "Silence", "Weakness" },
        new string[] { "Astronaut", "Astronomer", "Teacher", "Pilot" },
        new string[] { "Wasteful", "Generosity", "Economy", "Luxury" },
        new string[] { "Museum", "Library", "Hospital", "Factory" },
        new string[] { "Cruel", "Selfish", "Compassionate", "Lazy" },
        new string[] { "Mountain", "Forest", "River", "Desert" }
    };

        int[] correctAnswers =
        {
        0,
        1,
        2,
        1,
        2,
        2
    };

        string[] explanations =
        {
        "Resilience means recovering quickly from difficult situations.",
        "An astronomer studies stars, planets, and space.",
        "Economy refers to careful management of money or resources.",
        "A library is a place where books are available for reading.",
        "Compassionate means showing kindness and care toward others.",
        "A river is a large flowing stream of water."
    };

        int index = (level - 1) % questions.Length;

        qd.questionText = questions[index];
        qd.options = options[index];
        qd.correctOptionIndex = correctAnswers[index];
        qd.explanation = explanations[index];
        qd.towerLevel = level;

        return qd;
    }

    // Helper wrapper class for JSON parsing
    [Serializable]
    private class HuggingFaceResponseWrapper
    {
        public HuggingFaceResponse[] responses;
    }
}

// Add this at the bottom of the file or in a separate file for JSON helper
[Serializable]
public class HuggingFaceResponseRoot
{
    public HuggingFaceSingleResponse[] responses;
}

[Serializable]
public class HuggingFaceSingleResponse
{
    public string generated_text;
}