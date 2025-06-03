using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace WordleSolver.Strategies;

/// <summary>
/// MySolver is an implementation of the IWordleSolverStrategy interface
/// designed to efficiently solve Wordle puzzles. This version incorporates
/// a highly robust filtering mechanism and an optimized guess selection strategy
///
/// Algorithm Description:
/// 1. Initialization (`Reset()`):
///    - A static list of all valid Wordle words (`WordList`) is loaded once from `wordle.txt`.
///    - For each new game, the solver resets its state by setting `_remainingWords`
///      to a fresh copy of the entire `WordList`. This list represents all currently
///      possible answers for the secret word.
///
/// 2. First Guess (`PickNextGuess()` for `GuessNumber == 0`):
///    - The solver always starts with a predefined "best" first guess, typically "crane"
///      due to its high frequency of common letters and diverse positions. This aims
///      to quickly eliminate a large portion of the possible word list initially.
///
/// 3. Subsequent Guesses (Robust Filtering):
///    - For every subsequent guess, the `ApplyGuessFeedback` method is called.
///    - This method takes the `previousResult` (the word guessed and its feedback)
///      and filters `_remainingWords` using a direct simulation approach:
///      - It iterates through every `candidateWord` in `_remainingWords`.
///      - For each `candidateWord`, it *simulates* what the `LetterStatus` feedback
///        *would have been* if `previousResult.Word` was guessed and `candidateWord` was the secret answer.
///        This simulation (`SimulateWordleFeedback` method) precisely mimics the Wordle game engine's
///        logic, correctly handling duplicate letters and their statuses (Correct, Misplaced, Unused).
///      - If the `simulated feedback` for a `candidateWord` does *not* exactly match the
///        `actual feedback` (`previousResult.LetterStatuses`), then that `candidateWord`
///        is impossible and is removed from `_remainingWords`.
///      - The `previousResult.Word` itself is also removed from `_remainingWords` if it was not correct,
///        as it cannot be the answer.
///    - This simulation-based filtering is the most reliable way to prune the list correctly
///      under all Wordle feedback scenarios, especially concerning duplicate letters.
///
/// 4. Choosing the Next Guess (Optimized Heuristic):
///    - If `_remainingWords` contains only one word after filtering, that word is the answer and returned.
///    - If multiple words remain, the solver needs to pick the most informative next guess.
///    - It calculates the frequency of each character across *all* words still present in
///      `_remainingWords` (these are the *possible answers*).
///    - It then iterates through *all words in the full `WordList`* (the complete *valid guess dictionary*).
///      This allows the solver to make "information-gathering" guesses that might not be the answer
///      themselves but are excellent at discriminating between the remaining possibilities.
///    - Each word (from the full `WordList`) is scored by summing the frequencies of its *unique* letters
///      from the calculated `letterFrequencies` (which are derived from `_remainingWords`).
///    - **Prioritization Rule**: The solver selects the word with the highest score. If multiple words
///      have the same highest score, it strongly prioritizes a word that is *also* present in
///      `_remainingWords` (i.e., a word that could potentially be the actual answer). This balances
///      making effective information-gathering guesses with the efficiency of guessing the answer directly
///      if a strong candidate is also a good discriminator.
/// </summary>

//The "sealed" means that it cannot be inherited. The usage of IWordleSolverStrategy is to make MySolver guarantees that it implements the methods defined in that interface.
public sealed class MySolver : IWordleSolverStrategy
{
    ///Absolute or relative path of the word-list file. 
    private static readonly string WordListPath = Path.Combine("data", "wordle.txt");

    ///In-memory dictionary of valid five-letter words, loaded once.
    private static readonly List<string> WordList = LoadWordList();

    /// List of words that are still possible answers based on previous guesses and feedback.
    /// This list is reset for each new game.
    private List<string> _remainingWords = new();
    
    /// The predefined first guess, chosen for its effectiveness in narrowing down possibilities.
    private readonly string _firstGuess = "crane"; // A strong starting word for information gain
     
    /// Loads the dictionary from disk, filtering to distinct five-letter lowercase words.
    /// This method is called only once when the solver is initialized.
    /// <exception cref="FileNotFoundException">Thrown if the word list file is not found.</exception>
    private static List<string> LoadWordList()
    {
        if (!File.Exists(WordListPath))
            throw new FileNotFoundException($"Word list not found at path: {WordListPath}");

        // Read all lines from the word list file, trim whitespace, convert to lowercase, ensures they are ONLY 5 letters long, and removes any possible duplicates. 
        return File.ReadAllLines(WordListPath)
                   .Select(w => w.Trim().ToLowerInvariant())
                   .Where(w => w.Length == 5)
                   .Distinct()
                   .ToList();
    }
   
    
    /// Resets the solver's state for a new game. The list of remaining possible words
    /// is re-initialized to the full dictionary.
    public void Reset()
    {
        _remainingWords = new List<string>(WordList);
    }

    /// <summary>
    /// Determines the next word to guess given feedback from the previous guess.
    /// This method orchestrates the filtering of possible words and selection
    /// of the next optimal guess.
    /// </summary>
    /// <param name="previousResult">
    /// The <see cref="GuessResult"/> returned by the game engine for the last guess
    /// (or <see cref="GuessResult.Default"/> if this is the first turn).
    /// </param>
    /// <returns>A five-letter lowercase word.</returns>
    public string PickNextGuess(GuessResult previousResult)
    {
        // If this is the very first guess of the game (GuessNumber 0 indicates no valid guesses yet).
        if (previousResult.GuessNumber == 0)
        {
            return _firstGuess; // Return the predefined first guess.
        }

        // If a previous guess was invalid, it indicates a problem with the solver's word choice.
        // This scenario should ideally not happen if the solver consistently picks from WordList.
        if (!previousResult.IsValid && previousResult.GuessNumber > 0)
        {
            throw new InvalidOperationException($"MySolver attempted an invalid guess '{previousResult.Word}'. " +
                                                "This suggests a mismatch between the solver's internal dictionary " +
                                                "and the WordleService's dictionary, or an error in filtering logic.");
        }

        // Apply feedback from the previous guess to filter the list of remaining possible words.
        ApplyGuessFeedback(previousResult);

        // If only one word remains after filtering, it must be the answer.
        if (_remainingWords.Count == 1)
        {
            return _remainingWords[0];
        }

        // If no words remain, it indicates a critical error in the filtering logic,
        // as the true answer should always be among the possible words.
        if (_remainingWords.Count == 0)
        {
            throw new InvalidOperationException("No remaining words to choose from. " +
                                                "The filtering logic may have incorrectly eliminated the answer.");
        }

        // Choose the best word from the remaining possibilities using an optimized heuristic.
        return ChooseBestRemainingWord();
    }

    /// <summary>
    /// Filters the list of remaining possible words based on the feedback from the previous guess.
    /// This is the core of the solver's ability to narrow down the search space.
    /// </summary>
    /// <param name="previousResult">The <see cref="GuessResult"/> from the last turn.</param>
    private void ApplyGuessFeedback(GuessResult previousResult)
    {
        string guessedWord = previousResult.Word;
        LetterStatus[] actualFeedback = previousResult.LetterStatuses;

        // Create a new list to store only the words that are still possible answers.
        List<string> newRemainingWords = new List<string>();

        foreach (string candidate in _remainingWords)
        {
            // If the `guessedWord` was not the correct answer, it cannot be a candidate for the solution.
            if (!previousResult.IsCorrect && candidate == guessedWord)
            {
                continue; // Skip this word, it's definitively not the answer.
            }

            // Simulate the feedback that would be received if 'candidate' were the answer
            // and 'guessedWord' was the guess. This `SimulateWordleFeedback` method
            // exactly replicates the game's logic for generating feedback.
            LetterStatus[] simulatedFeedback = SimulateWordleFeedback(guessedWord, candidate);

            // Compare the simulated feedback with the actual feedback received from the game.
            // A candidate word is still possible ONLY if its simulated feedback
            // matches the actual feedback exactly for every letter.
            bool feedbackMatches = true;
            for (int i = 0; i < 5; i++)
            {
                if (simulatedFeedback[i] != actualFeedback[i])
                {
                    feedbackMatches = false;
                    break;
                }
            }

            if (feedbackMatches)
            {
                newRemainingWords.Add(candidate); // This candidate word is still a possibility.
            }
        }

        // Update the main list of remaining words with the filtered list.
        _remainingWords = newRemainingWords;
    }

    /// <summary>
    /// Simulates the Wordle feedback process for a given guess against a potential answer.
    /// This method is a direct replication of the core feedback logic within <c>WordleService.Guess</c>
    /// to ensure accurate filtering. This is crucial for correctly handling duplicate letters.
    /// </summary>
    /// <param name="guess">The word that was guessed.</param>
    /// <param name="answerCandidate">The word being tested as a potential secret answer.</param>
    /// <returns>An array of <see cref="LetterStatus"/> representing the simulated feedback.</returns>
    private LetterStatus[] SimulateWordleFeedback(string guess, string answerCandidate)
    {
        LetterStatus[] simulatedStatuses = Enumerable.Repeat(LetterStatus.Unused, 5).ToArray();

        // Create a mutable count of characters in the `answerCandidate`.
        // This is crucial for correctly handling duplicate letters in the guess and answer.
        var answerCharCounts = answerCandidate
            .GroupBy(c => c)
            .ToDictionary(g => g.Key, g => g.Count());

        // Pass 1: Identify and mark 'Correct' letters (right letter, right position).
        // Decrement counts from `answerCharCounts` so they are not used again for 'Misplaced'.
        for (int i = 0; i < 5; i++)
        {
            if (guess[i] == answerCandidate[i])
            {
                simulatedStatuses[i] = LetterStatus.Correct;
                answerCharCounts[guess[i]]--;
            }
        }

        // Pass 2: Identify and mark 'Misplaced' and 'Unused' letters.
        for (int i = 0; i < 5; i++)
        {
            // Skip letters already marked as 'Correct' in Pass 1.
            if (simulatedStatuses[i] == LetterStatus.Correct)
                continue;

            // If the letter exists in the answer (and hasn't been used by a 'Correct' match), mark as 'Misplaced'.
            // Otherwise, it's 'Unused'.
            if (answerCharCounts.TryGetValue(guess[i], out int remainingCount) && remainingCount > 0)
            {
                simulatedStatuses[i] = LetterStatus.Misplaced;
                answerCharCounts[guess[i]]--;
            }
            else
            {
                simulatedStatuses[i] = LetterStatus.Unused;
            }
        }

        return simulatedStatuses;
    }

    /// <summary>
    /// Chooses the "best" word as the next guess. This optimized version aims for
    /// an average of <= 4.0 guesses by considering all valid Wordle words
    /// (from the full `WordList`) as potential guesses, not just the currently
    /// remaining possible answers (`_remainingWords`).
    ///
    /// It scores words based on the sum of frequencies of their unique letters
    /// within the `_remainingWords` list. This maximizes information gain.
    ///
    /// The selection prioritizes words that:
    /// 1. Score highest based on common letter frequencies among possible answers.
    /// 2. Among words with equal highest scores, it prefers those that are *also*
    ///    currently in `_remainingWords` (i.e., could be the actual answer). This
    ///    balances information gathering with directly solving the puzzle.
    /// </summary>
    /// <returns>The chosen word as the next guess.</returns>
    private string ChooseBestRemainingWord()
    {
        // If only one word is left, it's the answer.
        if (_remainingWords.Count == 1)
        {
            return _remainingWords[0];
        }

        // Calculate the frequency of each character across all currently possible answers (`_remainingWords`).
        // This helps identify which letters are most common among the potential answers.
        Dictionary<char, int> letterFrequencies = new Dictionary<char, int>();
        foreach (string word in _remainingWords)
        {
            foreach (char c in word)
            {
                letterFrequencies.TryAdd(c, 0);
                letterFrequencies[c]++;
            }
        }

        string bestGuessCandidate = string.Empty;
        int maxScore = -1;
        bool bestGuessIsPossibleAnswer = false; // Flag to track if the current best candidate is also a possible answer.

        // Iterate through *all* words in the full dictionary (`WordList`) as potential guesses.
        // This allows for selecting words that might not be the answer but are excellent for
        // information gathering (e.g., words with many common letters from `_remainingWords`).
        foreach (string word in WordList) // Key optimization: iterate over ALL valid words
        {
            int currentScore = 0;
            // Calculate score based on unique letters in the current `word` and their frequencies
            // within the `_remainingWords` set.
            foreach (char uniqueChar in word.Distinct())
            {
                if (letterFrequencies.TryGetValue(uniqueChar, out int freq))
                {
                    currentScore += freq;
                }
            }

            // Determine if the current `word` being considered as a guess is also a possible answer.
            bool currentWordIsPossibleAnswer = _remainingWords.Contains(word);

            // Logic for selecting the best word:
            // We want to maximize the score.
            // If scores are equal, we prioritize a word that is also a possible answer.
            if (currentScore > maxScore ||
                (currentScore == maxScore && currentWordIsPossibleAnswer && !bestGuessIsPossibleAnswer))
            {
                maxScore = currentScore;
                bestGuessCandidate = word;
                bestGuessIsPossibleAnswer = currentWordIsPossibleAnswer;
            }
        }

        // Fallback: If no candidate was found (e.g., in a very unusual scenario or if _remainingWords was empty
        // at some point, which should ideally be caught earlier), default to the first remaining word.
        // This ensures a valid word is always returned if _remainingWords is not empty.
        if (string.IsNullOrEmpty(bestGuessCandidate) && _remainingWords.Count > 0)
        {
            return _remainingWords.First();
        }

        // This should always return a valid guess given the above logic and that _remainingWords.Count > 0.
        return bestGuessCandidate;
    }
}