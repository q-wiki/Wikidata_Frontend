﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Minigame
{
    public class MinigameSort : MonoBehaviour, IMinigame
    {
        /**
         * public fields
         */
        
        public List<GameObject> choices;
        public GameObject description;
        public Sprite boxSprite;
        public GameObject sendButton;
        public Timer timerPrefab;

        /**
         * private fields
         */

        private string _id;
        private string _taskDescription;
        private IList<string> _answerOptions;
        private Timer _timer;

        private MenuController menuController => GameObject.Find("MenuController").GetComponent<MenuController>(); 
        private GameObject ClosePanel => transform.Find("ClosePanel").gameObject;

        public async void Initialize(string miniGameId, string taskDescription, IList<string> answerOptions, int difficulty)
        {

            Reset();

            _id = miniGameId;
            _taskDescription = taskDescription;
            _answerOptions = answerOptions;
            AssignDescription(_taskDescription);
            AssignChoices(_answerOptions);
            
            /**
             * match difficulty to timer value
             */

            int milliseconds;
            
            if (difficulty == 0)
                milliseconds = 30000;
            else if (difficulty == 1)
                milliseconds = 20000;
            else if (difficulty == 2)
                milliseconds = 10000;
            else throw new Exception("Difficulty could not be used to determine milliseconds");

            /**
             * instantiate timer and let it count down x milliseconds
             */
            
            _timer = Instantiate(timerPrefab, transform.Find("Layout"));
            _timer.Initialize(this,  milliseconds);
            await _timer.Countdown();
        }
        
        /**
       * function to reset text colors before showing the game to user
       */
        private void Reset()
        {
            foreach (var item in choices)
            {
                item.GetComponentInChildren<Text>().color = Color.black;
            }
        }

        private void AssignChoices(IList<string> answerOptions)
        {
            for (var i = 0; i < choices.Count; i++)
            {
                var text = choices[i].transform.Find("Text");
                text.GetComponent<Text>().text = answerOptions[i];
            }
        }

        private void AssignDescription(string desc)
        {
            description.GetComponent<Text>().text = desc;
        }
        
        public async void ForceQuit()
        {
            Debug.Log("Sorry, you were too slow");
            var result = await Communicator.AnswerMinigame(_id, new List<string> {});
            ClosePanel.SetActive(true);
        }

        public async void Send()
        {
            if (!Communicator.IsConnected())
            {
                Debug.Log("You are not connected to any game");
                return;
            }

            _timer.isInterrupted = true;
            Destroy(_timer.gameObject);

            Debug.Log("Handling minigame result");
            List<string> answers = new List<string>();
            foreach (var choice in choices)
            {
                Text text = choice.GetComponentInChildren<Text>();
                answers.Add(text.text);
            }

            LoadingIndicator.Instance.Show();
            var result = await Communicator.AnswerMinigame(_id, answers);
            LoadingIndicator.Instance.Hide();

            // Check result and display feedback to user
            var correctAnswer = result.CorrectAnswer;
            Debug.Log($"Chosen answer: {answers}, Correct answer: {correctAnswer}");

            var correctAnswerColor = Color.green;

            if (answers.SequenceEqual(correctAnswer))
            {
                // sequence is correct
                choices.ForEach(c => c.GetComponentInChildren<Text>().color = correctAnswerColor);
            }
            else
            {
                // sequence has wrong elements => highlight right and wrong elements
                for (var i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    Text text = c.GetComponentInChildren<Text>();
                    if (answers[i] == correctAnswer[i])
                        text.color = correctAnswerColor;
                    else
                        text.color = Color.red;
                }
            }

            ClosePanel.SetActive(true);

        }

        public void Close()
        {
            menuController.GetComponent<MenuController>().RefreshGameState(false);
            menuController.ToggleCameraBehaviour();
            gameObject.SetActive(false);
            ClosePanel.SetActive(false);
        }
    }
}
