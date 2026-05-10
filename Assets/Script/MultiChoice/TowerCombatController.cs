using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TowerCombatController : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Text questionText;
    public Button[] answerButtons;      // 4 buttons
    public TMP_Text[] answerLabels;
    public TMP_Text resultText;
    public TMP_Text levelText;
    public TMP_Text scoreText;
    public GameObject victoryPanel;
    public TMP_Text victoryText;

    [Header("Game Settings")]
    public int startLevel = 1;
    public int maxLevel = 10;
    public float delayAfterAnswer = 2f;

    [Header("Components")]
    public QuestionGenerator questionGenerator;

    private QuestionData currentQuestion;
    private int currentLevel;
    private int score;
    private bool isWaitingForAnswer = false;
    private bool gameActive = true;

    void Start()
    {
        // Setup button listeners
        for (int i = 0; i < answerButtons.Length; i++)
        {
            int index = i;
            answerButtons[i].onClick.AddListener(() => OnAnswerClicked(index));
        }

        // Initialize game
        score = 0;
        currentLevel = startLevel;
        gameActive = true;

        StartCombat();
    }

    void StartCombat()
    {
        if (!gameActive) return;

        isWaitingForAnswer = false;
        resultText.text = "Generating question from AI...";

        // Disable buttons while loading
        SetButtonsInteractable(false);

        // Update UI
        if (levelText != null) levelText.text = $"Tower Level: {currentLevel}";
        if (scoreText != null) scoreText.text = $"Score: {score}";

        // Request question from Hugging Face
        questionGenerator.RequestQuestionForLevel(currentLevel, OnQuestionLoaded);
    }

    void OnQuestionLoaded(QuestionData question)
    {
        if (!gameActive) return;

        if (question.options == null || question.options.Length < 4)
        {
            Debug.LogError("Question options missing!");
            return;
        }

        currentQuestion = question;
        isWaitingForAnswer = true;

        // Put question + options together
        questionText.text =
            $"{question.questionText}\n\n" +
            $"A) {question.options[0]}\n\n" +
            $"B) {question.options[1]}\n\n" +
            $"C) {question.options[2]}\n\n" +
            $"D) {question.options[3]}";

        resultText.text = "Choose the correct answer!";

        SetButtonsInteractable(true);
    }

    void OnAnswerClicked(int selectedIndex)
    {
        if (!isWaitingForAnswer || !gameActive) return;

        isWaitingForAnswer = false;
        SetButtonsInteractable(false);

        bool isCorrect = (selectedIndex == currentQuestion.correctOptionIndex);

        if (isCorrect)
        {
            // Correct answer - defeat enemy and advance
            score += 10 * currentLevel;  // Bonus for higher levels
            resultText.text = $"CORRECT! {currentQuestion.explanation}\n\n+{10 * currentLevel} points!";
            StartCoroutine(AdvanceToNextLevel());
        }
        else
        {
            // Wrong answer - player takes damage
            string correctLetter = ((char)('A' + currentQuestion.correctOptionIndex)).ToString();
            resultText.text = $"WRONG! The correct answer was {correctLetter}.\n\n{currentQuestion.explanation}\n\nYou take damage but continue...";
            StartCoroutine(RetryCurrentLevel());
        }

        // Update score display
        if (scoreText != null) scoreText.text = $"Score: {score}";
    }

    IEnumerator AdvanceToNextLevel()
    {
        yield return new WaitForSeconds(delayAfterAnswer);

        currentLevel++;

        if (currentLevel > maxLevel)
        {
            // Victory!
            victoryPanel.SetActive(true);
            gameActive = false;
            victoryText.text = $"VICTORY! \nYou conquered all {maxLevel} levels!\nFinal Score: {score}";
            SetButtonsInteractable(false);
            yield break;
        }

        // Move to next level
        StartCombat();
    }

    IEnumerator RetryCurrentLevel()
    {
        yield return new WaitForSeconds(delayAfterAnswer);

        // Stay on same level, get a new question
        StartCombat();
    }

    void SetButtonsInteractable(bool interactable)
    {
        foreach (Button btn in answerButtons)
        {
            btn.interactable = interactable;
        }
    }

    // Public method to restart game
    public void RestartGame()
    {
        score = 0;
        currentLevel = startLevel;
        gameActive = true;
        victoryPanel.SetActive(false);
        StartCombat();
    }
}