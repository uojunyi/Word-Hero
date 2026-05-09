using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class QuestionData
{
    public string questionText;
    public string[] options;
    public int correctOptionIndex; // 0-based
    public string explanation;
    public int towerLevel;
}

[Serializable]
public class WatsonRequest
{
    public string input;
    public string level;
}

[Serializable]
public class WatsonResponse
{
    public string generated_question;
    public string option_a;
    public string option_b;
    public string option_c;
    public string option_d;
    public int correct_answer;
    public string explanation;
}

public class IBMQuestionGenerator : MonoBehaviour
{
    [Header("IBM Cloud Configuration")]
    [Tooltip("IBM Watson API Key - Store securely in production")]
    public string watsonApiKey = "YOUR_API_KEY_HERE";

    [Tooltip("IBM Watson Service URL (e.g., https://api.us-south.watson.cloud.ibm.com)")]
    public string watsonUrl = "https://api.us-south.watson.cloud.ibm.com";

    [Tooltip("Watsonx.ai or Natural Language Understanding endpoint")]
    public string watsonEndpoint = "/instances/YOUR_INSTANCE_ID/deployments/YOUR_DEPLOYMENT_ID/chat";

    [Header("Local Fallback")]
    public QuestionData[] localQuestions; // Assigned in Inspector

    private Queue<Action<QuestionData>> pendingCallbacks = new Queue<Action<QuestionData>>();
    private bool isRequesting = false;

    /// <summary>
    /// Public method to request a generated question for a specific tower level.
    /// Returns QuestionData via callback (async).
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

        // Build the prompt for Watson
        string prompt = BuildPromptForLevel(level);
        string jsonBody = $"{{\"input\": \"{prompt}\", \"level\": \"{level}\"}}";

        using (UnityWebRequest request = new UnityWebRequest(watsonUrl + watsonEndpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {watsonApiKey}");

            yield return request.SendWebRequest();

            QuestionData result = null;

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Parse Watson response
                result = ParseWatsonResponse(request.downloadHandler.text, level);
                Debug.Log($"IBM Watson generated question for level {level}: {result.questionText}");
            }
            else
            {
                // Fallback to local questions if cloud fails
                Debug.LogWarning($"Watson API failed: {request.error}. Using local fallback.");
                result = GetLocalQuestionForLevel(level);
            }

            // Invoke all queued callbacks with the same question
            while (pendingCallbacks.Count > 0)
            {
                var callback = pendingCallbacks.Dequeue();
                callback?.Invoke(result);
            }

            isRequesting = false;
        }
    }

    private string BuildPromptForLevel(int level)
    {
        // Difficulty scales with tower level
        string difficulty = level < 3 ? "easy" : (level < 7 ? "medium" : "hard");

        return $"Generate a multiple-choice question about computer science or general knowledge. " +
               $"Difficulty: {difficulty}. " +
               $"Return format: Question|A. option|B. option|C. option|D. option|Correct letter (A/B/C/D)|Brief explanation. " +
               $"Make it educational and clear.";
    }

    private QuestionData ParseWatsonResponse(string jsonResponse, int level)
    {
        // Simple manual parsing - in production, use JsonUtility or Newtonsoft
        // Expected format: "Question|A. text|B. text|C. text|D. text|A|Explanation"
        try
        {
            // Extract the generated text (simplified - your actual Watson response structure may differ)
            string generatedText = ExtractGeneratedTextFromWatson(jsonResponse);
            string[] parts = generatedText.Split('|');

            if (parts.Length >= 7)
            {
                QuestionData qd = new QuestionData();
                qd.questionText = parts[0].Trim();
                qd.options = new string[]
                {
                    parts[1].Trim().Substring(3), // Remove "A. "
                    parts[2].Trim().Substring(3),
                    parts[3].Trim().Substring(3),
                    parts[4].Trim().Substring(3)
                };

                string correctLetter = parts[5].Trim().ToUpper();
                qd.correctOptionIndex = correctLetter[0] - 'A';
                qd.explanation = parts[6].Trim();
                qd.towerLevel = level;

                return qd;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse Watson response: {e.Message}");
        }

        // Fallback to local on parse failure
        return GetLocalQuestionForLevel(level);
    }

    private string ExtractGeneratedTextFromWatson(string watsonResponse)
    {
        // TODO: Replace with actual JSON path extraction based on your Watson service response format
        // Example assumes simple text response
        return watsonResponse;
    }

    private QuestionData GetLocalQuestionForLevel(int level)
    {
        if (localQuestions == null || localQuestions.Length == 0)
        {
            // Ultra-fallback hardcoded question
            return CreateDefaultQuestion(level);
        }

        int index = (level - 1) % localQuestions.Length;
        QuestionData local = localQuestions[index];
        local.towerLevel = level;
        return local;
    }

    private QuestionData CreateDefaultQuestion(int level)
    {
        QuestionData defaultQ = new QuestionData();
        defaultQ.questionText = $"What is the primary benefit of cloud computing? (Tower Level {level})";
        defaultQ.options = new string[]
        {
            "Lower electricity bills",
            "On-demand resources and scalability",
            "Built-in video games",
            "Free hardware upgrades"
        };
        defaultQ.correctOptionIndex = 1;
        defaultQ.explanation = "Cloud computing provides on-demand access to computing resources without upfront infrastructure costs.";
        defaultQ.towerLevel = level;
        return defaultQ;
    }
}