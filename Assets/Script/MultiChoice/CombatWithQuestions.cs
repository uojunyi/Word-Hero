using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CombatWithQuestions : MonoBehaviour
{
    [Header("UI References")]
    public Text questionText;
    public Button[] optionButtons; // 4 buttons (index 0-3)
    public Text[] optionLabels;
    public Text resultText;
    public Text towerLevelText;

    [Header("Game Settings")]
    public int currentTowerLevel = 1;
    public int maxLevel = 10;

    [Header("IBM Integration")]
    public IBMQuestionGenerator questionGenerator;

    private QuestionData currentQuestion;
    private bool waitingForAnswer = false;

    void Start()
    {
        // Attach button listeners
        for (int i = 0; i < optionButtons.Length; i++)
        {
            int optionIndex = i; // Capture for closure
            optionButtons[i].onClick.AddListener(() => OnAnswerSelected(optionIndex));
        }

        StartCombatForLevel(currentTowerLevel);
    }

    /// <summary>
    /// Starts a combat encounter by requesting a question from IBM Cloud
    /// </summary>
    private void StartCombatForLevel(int level)
    {
        waitingForAnswer = false;
        resultText.text = "Generating question from IBM Cloud...";

        // Disable buttons while loading
        SetButtonsInteractable(false);

        // Request question from IBM Watson (or fallback)
        questionGenerator.RequestQuestionForLevel(level, OnQuestionReceived);
    }

    /// <summary>
    /// Callback when question is ready (from cloud or local)
    /// </summary>
    private void OnQuestionReceived(QuestionData question)
    {
        currentQuestion = question;
        waitingForAnswer = true;

        // Display question and options
        questionText.text = $"[Level {question.towerLevel} Enemy] {question.questionText}";
        for (int i = 0; i < optionLabels.Length && i < question.options.Length; i++)
        {
            optionLabels[i].text = $"{GetLetter(i)}. {question.options[i]}";
        }

        resultText.text = "Defeat the enemy by answering correctly!";
        SetButtonsInteractable(true);
        towerLevelText.text = $"Tower Level: {currentTowerLevel}";
    }

    /// <summary>
    /// Handles player's answer selection
    /// </summary>
    private void OnAnswerSelected(int selectedIndex)
    {
        if (!waitingForAnswer) return;

        waitingForAnswer = false;
        SetButtonsInteractable(false);

        bool isCorrect = (selectedIndex == currentQuestion.correctOptionIndex);

        if (isCorrect)
        {
            resultText.text = $"CORRECT! {currentQuestion.explanation}\nYou defeated the enemy!";
            StartCoroutine(AdvanceToNextLevel());
        }
        else
        {
            string correctLetter = GetLetter(currentQuestion.correctOptionIndex);
            resultText.text = $"WRONG! The correct answer was {correctLetter}.\n{currentQuestion.explanation}\nTake damage and retry!";
            StartCoroutine(RetryCurrentLevel());
        }
    }

    private IEnumerator AdvanceToNextLevel()
    {
        yield return new WaitForSeconds(2.5f);

        currentTowerLevel++;

        if (currentTowerLevel > maxLevel)
        {
            resultText.text = "VICTORY! You have conquered the tower!";
            SetButtonsInteractable(false);
            yield break;
        }

        StartCombatForLevel(currentTowerLevel);
    }

    private IEnumerator RetryCurrentLevel()
    {
        yield return new WaitForSeconds(2.5f);
        // Same level, get a new question (could be different due to Watson generation)
        StartCombatForLevel(currentTowerLevel);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        foreach (Button btn in optionButtons)
        {
            btn.interactable = interactable;
        }
    }

    private string GetLetter(int index)
    {
        return ((char)('A' + index)).ToString();
    }
}