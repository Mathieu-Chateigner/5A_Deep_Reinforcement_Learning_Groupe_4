using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public enum Game
{
    GridWorld,
    Sokoban
}

public enum Action
{
    Up,
    Right,
    Down,
    Left
}

public class GameManager : MonoBehaviour
{
    public Vector2Int gridSize;
    public Camera camera;
    public List<Button> listButtons = new ();

    private State _start; // deprecated
    private State _end; // deprecated
    private List<State> _obstacles; // deprecated
    private Policy _policy;
    private Dictionary<State, float> _stateValues; // Used by PolicyEvaluation and ValueIteration
    private List<State> _states;
    private Map currentMap;
    private State currentState;

    private MapManager mapManager;

    private void Start()
    {
        mapManager = new MapManager();

        Game game = Game.Sokoban;
        currentMap = mapManager.GetMap(game, 2);
        gridSize = currentMap.size; // deprecated but still used
        _end = currentMap.endState; // deprecated but still used by GridWorld
        _states = GenerateAllStates(game, currentMap);
        currentState = currentMap.startState;

        Debug.Log("Total states count : " + _states.Count);

        camera.transform.position = new Vector3(currentMap.size.x / 2, currentMap.size.y / 2, -10);

        _policy = new Policy();
        _policy.InitializePolicy(_states, this);

        InitializeStateValues();

        //PrintCoordinates();

        PrintPolicy();
        TilemapManager.Instance.Display(currentMap, currentMap.startState, _policy); // Affiche la map et le state
        SetInteractivnessButtons(true);
    }

    public void playPolicy()
    {
        Debug.Log("Play current policy");
        currentState = GetNextState(currentState, _policy.GetAction(currentState));
        TilemapManager.Instance.Display(currentMap, currentState, _policy); // Affiche la map et le state
    }

    public void ExecMonteCarlo(int numEpisode)
    {
        SetInteractivnessButtons(false);
        _stateValues = MonteCarloOnPolicy(numEpisode, 0.9f, 10000, 0.9f, true); // First visit
        PrintStateValues();
        
        TilemapManager.Instance.Display(currentMap, currentState, _policy); // Affiche la map et le state
        SetInteractivnessButtons(true);
    }

    public void ExecQLearning(int numEpisode)
    {
        SetInteractivnessButtons(false);
        QLearning(numEpisode, 0.2f, 0.9f, 0.9f, 10000);

        TilemapManager.Instance.Display(currentMap, currentState, _policy); // Affiche la map et le state
        SetInteractivnessButtons(true);
    }

    // QLearning : Off policy
    public void QLearning(int numEpisode, float alpha, float discountFactor, float epsilon, int maxSteps)
    {
        Dictionary<(State, Action), float> qValues = new Dictionary<(State, Action), float>();

        foreach (var state in _states)
        {
            foreach (Action action in Enum.GetValues(typeof(Action)))
            {
                qValues[(state, action)] = 0.0f;
            }
        }

        for (int episode = 0; episode < numEpisode; episode++)
        {
            State state = currentMap.startState;
            int step = 0;

            while (!IsEnd(state) && step < maxSteps)
            {
                Action action = ChooseAction(state, qValues, epsilon);
                State nextState = GetNextState(state, action);
                float reward = GetImmediateReward(state, action);

                float maxQNext = Enum.GetValues(typeof(Action)).Cast<Action>().Max(a => qValues[(nextState, a)]);
                qValues[(state, action)] += alpha * (reward + discountFactor * maxQNext - qValues[(state, action)]);

                state = nextState;
                step++;
            }
        }
        
        UpdatePolicyBasedOnQValues(qValues);
    }

    // Used by QLearning
    private void UpdatePolicyBasedOnQValues(Dictionary<(State, Action), float> qValues)
    {
        foreach (var state in _states)
        {
            var bestAction = qValues.Where(q => q.Key.Item1 == state).OrderByDescending(q => q.Value).First().Key.Item2;
            _policy.UpdatePolicy(state, bestAction);
        }
    }


    // Used by QLearning
    // Choisi une action avec epsilon greedy
    private Action ChooseAction(State state, Dictionary<(State, Action), float> qValues, float epsilon)
    {
        if (UnityEngine.Random.value < epsilon)
        {
            // Exploration
            var actions = Enum.GetValues(typeof(Action)).Cast<Action>().ToList();
            return actions[UnityEngine.Random.Range(0, actions.Count)];
        }
        else
        {
            // Exploitation
            float maxValue = float.MinValue;
            Action bestAction = Action.Up;
            foreach (Action action in Enum.GetValues(typeof(Action)))
            {
                float value = qValues[(state, action)];
                if (value > maxValue)
                {
                    maxValue = value;
                    bestAction = action;
                }
            }
            return bestAction;
        }
    }

    private List<State> GenerateAllStates(Game game, Map map)
    {
        var states = new List<State>();

        // GridWorld
        if (game == Game.GridWorld)
        {
            for (int x = 0; x < map.size.x; x++)
            {
                for (int y = 0; y < map.size.y; y++)
                {
                    Vector2Int position = new Vector2Int(x, y);
                    if (!map.obstacles.Contains(position))
                    {
                        states.Add(new State(x, y));
                    }
                }
            }
        }
        // Sokoban
        else if (game == Game.Sokoban)
        {
            var allCrateCombinations = GetAllCombinations(map);
            foreach (var crateCombination in allCrateCombinations)
            {
                for (var x = 0; x < map.size.x; x++)
                {
                    for (var y = 0; y < map.size.y; y++)
                    {
                        Vector2Int playerPosition = new Vector2Int(x, y);
                        if (!map.obstacles.Contains(playerPosition) && !crateCombination.Contains(playerPosition))
                        {
                            states.Add(new State(playerPosition, crateCombination.ToList()));
                        }
                    }
                }
            }
        }

        return states;
    }

    private HashSet<HashSet<Vector2Int>> GetAllCombinations(Map map)
    {
        List<Vector2Int> possiblePositions = new List<Vector2Int>();
        // Positions possibles pour les caisses sans les positions d'obstacles
        for (int x = 0; x < map.size.x; x++)
        {
            for (int y = 0; y < map.size.y; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                if (!map.obstacles.Contains(pos))
                {
                    possiblePositions.Add(pos);
                }
            }
        }

        var allCombinations = new HashSet<HashSet<Vector2Int>>();

        GetCombinationsRecursive(possiblePositions, new List<Vector2Int>(), map.startState.crates.Count, 0, allCombinations);

        return allCombinations;
    }

    // Méthode récursive pour générer toutes les combinaisons possibles de positions des caisses.
    private void GetCombinationsRecursive(List<Vector2Int> possiblePositions, List<Vector2Int> currentCombination, int cratesLeft, int startPosition, HashSet<HashSet<Vector2Int>> allCombinations)
    {
        if (cratesLeft == 0)
        {
            // Si aucune caisse n'est laissée, ajoutez la combinaison actuelle aux combinaisons
            allCombinations.Add(new HashSet<Vector2Int>(currentCombination));
            return;
        }

        for (int i = startPosition; i <= possiblePositions.Count - cratesLeft; i++)
        {
            currentCombination.Add(possiblePositions[i]);
            GetCombinationsRecursive(possiblePositions, currentCombination, cratesLeft - 1, i + 1, allCombinations);
            currentCombination.RemoveAt(currentCombination.Count - 1); // Retirer le dernier élément pour essayer la prochaine combinaison
        }
    }

    private void InitializeStateValues()
    {
        _stateValues = new Dictionary<State, float>();
        foreach (var state in _states)
        {
            if(state.game == Game.GridWorld)
            {
                _stateValues[state] = state.Equals(currentMap.endState) ? 1f : 0f;
            }
            else if(state.game == Game.Sokoban)
            {
                // Ratio de caisses sur les cibles
                float score = CalculateScore(state, currentMap.targets);
                _stateValues[state] = score;
            }
        }
    }

    // Ratio of crates on targets
    private float CalculateScore(State state, List<Vector2Int> targets)
    {
        if (state.crates == null || state.crates.Count == 0)
        {
            return 0f; // Pas de caisse
        }

        // Compte le nombre de caisse sur une cible
        int cratesOnTarget = 0;
        foreach (var crate in state.crates)
        {
            if (targets.Contains(crate))
            {
                cratesOnTarget++;
            }
        }

        // Ratio nombre de caisses
        return (float)cratesOnTarget / state.crates.Count;
    }

    public void PolicyIteration(float discountFactor)
    {
        SetInteractivnessButtons(false);
        bool policyStable = false;
        do
        {
            PolicyEvaluation(discountFactor);

            policyStable = PolicyImprovement();
        } while (!policyStable);

        PrintPolicy();
        TilemapManager.Instance.Display(currentMap, currentState, _policy); // Affiche la map et le state
        SetInteractivnessButtons(true);
    }

    private void PolicyEvaluation(float discountFactor)
    {
        float theta = 0.001f; // Seuil pour d�terminer quand arr�ter l'it�ration
        float delta;
        do
        {
            delta = 0f;
            foreach (State state in _states)
            {
                if (IsEnd(state)) continue;

                float oldValue = _stateValues[state];

                Action action = _policy.GetAction(state);
                State nextState = GetNextState(state, action);
                float reward = IsEnd(nextState) ? 0f : GetImmediateReward(state, action);
                _stateValues[state] = reward + (discountFactor * _stateValues[nextState]);

                delta = Mathf.Max(delta, Mathf.Abs(oldValue - _stateValues[state]));
            }
        } while (delta > theta);
    }

    public bool PolicyImprovement()
    {
        bool policyStable = true;

        foreach (State state in _states)
        {
            if (IsEnd(state)) continue; // Aucune action requise pour les �tats terminaux

            Action oldAction = _policy.GetAction(state);
            float maxValue = float.NegativeInfinity;
            Action bestAction = oldAction; // Default
            foreach (Action action in GetValidActions(state))
            {
                State nextState = GetNextState(state, action);
                float value = _stateValues[nextState];

                if (value > maxValue)
                {
                    maxValue = value;
                    bestAction = action;
                }
            }

            if (oldAction != bestAction)
            {
                policyStable = false;
                _policy.UpdatePolicy(state, bestAction);
            }
        }

        return policyStable;
    }

    // OK
    public void ValueIteration(float discountFactor)
    {
        SetInteractivnessButtons(false);
        const float theta = 0.001f; // Seuil pour d�terminer quand arr�ter l'it�ration
        float delta;
        do
        {
            delta = 0f;
            foreach (var state in _states)
            {
                if (IsEnd(state)) continue;

                var oldValue = _stateValues[state];

                // On prend seulement le max des actions possibles
                float maxValue = float.NegativeInfinity;
                Action bestAction = Action.Up; // Default
                foreach (Action action in GetValidActions(state))
                {
                    var nextState = GetNextState(state, action);

                    float reward = IsEnd(nextState) ? 0f : GetImmediateReward(state, action);
                    float value = reward + (discountFactor * _stateValues[nextState]);
                    if (value > maxValue)
                    {
                        maxValue = value;
                        bestAction = action;
                    }
                }

                _stateValues[state] = maxValue;

                // Update policy
                _policy.UpdatePolicy(state, bestAction);

                delta = Mathf.Max(delta, Mathf.Abs(oldValue - maxValue));
            }
        } while (delta > theta);


        PrintPolicy();
        TilemapManager.Instance.Display(currentMap, currentState, _policy); // Affiche la map et le state
        SetInteractivnessButtons(true);
    }
    
    public Dictionary<State, float> MonteCarloOnPolicy(int numEpisode, float discountFactor, int maxSteps, float explorationFactor, bool firstVisit = true)
    {
        Dictionary<State, float>  returnsSum = new Dictionary<State, float>();
        Dictionary<State, int> returnsCount = new Dictionary<State, int>();

        foreach (var state in _states)
        {
            returnsSum[state] = 0.0f;
            returnsCount[state] = 0;
        }

        for (int e = 0; e < numEpisode; e++)
        {
            List<(State, Action, float)> episode = GenerateEpisode(maxSteps, explorationFactor); // Simulation

            float G = 0;
            HashSet<State> visitedStates = new HashSet<State>();

            // Backpropagation
            for (int t = episode.Count - 1; t >= 1; t--)
            {
                G = discountFactor * G + episode[t-1].Item3; // Reward t-1
                //G = 1;
                State stateT = episode[t].Item1;

                if(firstVisit) // First visit : si l'état n'a pas été déjà visité dans cet épisode
                {
                    if (!visitedStates.Contains(stateT))
                    {
                        returnsSum[stateT] += G;
                        returnsCount[stateT] += 1;
                        visitedStates.Add(stateT);
                    }
                }
                else // Every visit
                {
                    returnsSum[stateT] += G;
                    returnsCount[stateT] += 1;
                }
            }

            // Update la policy entre chaque épisode (redondant mais flemme)
            Dictionary<State, float> values = new Dictionary<State, float>();
            foreach (State state in returnsSum.Keys)
            {
                if (returnsCount[state] > 0)
                {
                    values[state] = returnsSum[state] / returnsCount[state];
                }
            }
            UpdatePolicyBasedOnStateValues(_states, values); // On policy
        }

        // Compute average
        Dictionary<State, float> stateValues = new Dictionary<State, float>();
        foreach (State state in returnsSum.Keys)
        {
            if(returnsCount[state] > 0)
            {
                stateValues[state] = returnsSum[state] / returnsCount[state];
            }
        }
        return stateValues;
    }

    private List<(State, Action, float)> GenerateEpisode(int maxSteps, float explorationFactor)
    {
        List<(State, Action, float)> episode = new List<(State, Action, float)>();
        State currentState = currentMap.startState; // Start state (without exploring start)

        int step = 0;
        while (!IsEnd(currentState))
        {
            Action action;
            List<Action> validActions = GetValidActions(currentState);

            // Espilon greedy
            if (UnityEngine.Random.value < explorationFactor) // Exploration
            {
                action = validActions[UnityEngine.Random.Range(0, validActions.Count)];
            }
            else // Exploitation
            {
                action = _policy.GetAction(currentState);
            }

            State nextState = GetNextState(currentState, action);
            float reward = GetImmediateReward(currentState, action);

            // Max steps atteint
            if (++step > maxSteps)
            {
                episode.Add((currentState, default(Action), -1f)); // Echec
                break;
            }

            episode.Add((currentState, action, reward));

            currentState = nextState; // L'état courant devient l'état suivant
        }

        if (IsEnd(currentState))
        {
            episode.Add((currentState, default(Action), 1f));
        }

        return episode;
    }

    // Update policy based on state values (les flèches pointent vers la valeur max des states suivants possibles)
    void UpdatePolicyBasedOnStateValues(List<State> states, Dictionary<State, float> values)
    {
        foreach (State state in states)
        {
            if (!IsEnd(state))
            {
                List<Action> validActions = GetValidActions(state);
                float maxValue = float.NegativeInfinity;
                Action bestAction = Action.Up;

                foreach (Action action in validActions)
                {
                    State nextState = GetNextState(state, action);
                    float value = values.ContainsKey(nextState) ? values[nextState] : 0f;
                    if (value > maxValue)
                    {
                        maxValue = value;
                        bestAction = action;
                    }
                }
                _policy.UpdatePolicy(state, bestAction);
            }
        }
    }

    public float GetImmediateReward(State currentState, Action action)
    {
        var nextState = GetNextState(currentState, action);

        if(currentState.game == Game.GridWorld)
        {
            if (nextState.Equals(_end))
            {
                return 1.0f;
            }

            Vector2Int playerPosition = new Vector2Int(nextState.X, nextState.Y);
            if (currentMap.obstacles.Contains(playerPosition))
            {
                return 0.0f;
            }
            return 0.0f;
        }
        else if (currentState.game == Game.Sokoban)
        {
            int totalTargets = currentMap.targets.Count;
            int cratesOnTargetNext = nextState.crates.Count(crate => currentMap.targets.Contains(crate));

            // Calculer le ratio des caisses sur les cibles pour l'état suivant
            float reward = (float)cratesOnTargetNext / totalTargets;
            //return reward;

            // Différence de ratio entre l'état actuel et le suivant pour renforcer le progrès
            int cratesOnTargetCurrent = currentState.crates.Count(crate => currentMap.targets.Contains(crate));
            float currentReward = (float)cratesOnTargetCurrent / totalTargets;

            return reward - currentReward;
        }
        return 0.0f;
    }

    private State GetNextState(State state, Action action)
    {
        if(state.game == Game.GridWorld)
        {
            State nextState = new State(state.X, state.Y);

            switch (action)
            {
                case Action.Up:
                    nextState.Y = nextState.Y + 1;
                    break;
                case Action.Right:
                    nextState.X = nextState.X + 1;
                    break;
                case Action.Down:
                    nextState.Y = nextState.Y - 1;
                    break;
                case Action.Left:
                    nextState.X = nextState.X - 1;
                    break;
                default:
                    break;
            }

            Vector2Int playerPosition = new Vector2Int(nextState.X, nextState.Y);
            return currentMap.obstacles.Contains(playerPosition) ? state : nextState;
        }
       else if(state.game == Game.Sokoban)
       {
            Vector2Int newPosition = new Vector2Int(state.player.x, state.player.y);
            switch (action)
            {
                case Action.Up: newPosition.y += 1; break;
                case Action.Down: newPosition.y -= 1; break;
                case Action.Left: newPosition.x -= 1; break;
                case Action.Right: newPosition.x += 1; break;
            }

            // Vérifiez si la nouvelle position est un obstacle
            if (currentMap.obstacles.Contains(newPosition))
            {
                return state; // Aucun mouvement si obstacle
            }

            // Vérifiez si la nouvelle position est une caisse
            if (state.crates.Contains(newPosition))
            {
                Vector2Int nextCratePosition = newPosition + (newPosition - state.player);  // La position suivante pour la caisse
                if (!currentMap.obstacles.Contains(nextCratePosition) && !state.crates.Contains(nextCratePosition))
                {
                    List<Vector2Int> newCrates = new List<Vector2Int>(state.crates);
                    newCrates.Remove(newPosition);  // Enlevez la caisse de l'ancienne position
                    newCrates.Add(nextCratePosition);  // Ajoutez la caisse à la nouvelle position
                    return new State(newPosition, newCrates);
                }
                else
                {
                    return state;  // Aucun mouvement si la caisse ne peut pas bouger
                }
            }

            // Si pas de caisse à la nouvelle position, le joueur se déplace librement
            if(!_states.Contains(new State(newPosition, state.crates)))
            {
                Debug.LogError("STATE INCONNU");
            }
            return new State(newPosition, state.crates);
        }
        
        return state;
    }

    public List<Action> GetValidActions(State state)
    {
        var validActions = new List<Action>();

        if (state.game == Game.GridWorld)
        {
            if (state.Y < gridSize.y - 1) validActions.Add(Action.Up); // Peut aller vers le haut
            if (state.X < gridSize.x - 1) validActions.Add(Action.Right); // Peut aller vers la droite
            if (state.Y > 0) validActions.Add(Action.Down); // Peut aller vers le bas
            if (state.X > 0) validActions.Add(Action.Left); // Peut aller vers la gauche

            // Check aussi les obstacles
            Vector2Int playerPosition = new Vector2Int(state.X, state.Y);
            State upNextState = GetNextState(state, Action.Up);
            State rightNextState = GetNextState(state, Action.Right);
            State downNextState = GetNextState(state, Action.Down);
            State leftNextState = GetNextState(state, Action.Left);
            if (currentMap.obstacles.Contains(new Vector2Int(upNextState.X, upNextState.Y)))
            {
                validActions.Remove(Action.Up);
            }
            if (currentMap.obstacles.Contains(new Vector2Int(rightNextState.X, rightNextState.Y)))
            {
                validActions.Remove(Action.Right);
            }
            if (currentMap.obstacles.Contains(new Vector2Int(downNextState.X, downNextState.Y)))
            {
                validActions.Remove(Action.Down);
            }
            if (currentMap.obstacles.Contains(new Vector2Int(leftNextState.X, leftNextState.Y)))
            {
                validActions.Remove(Action.Left);
            }
        }
        else if (state.game == Game.Sokoban)
        {
            foreach (Action action in Enum.GetValues(typeof(Action)))
            {
                State nextState = GetNextState(state, action);
                if (nextState.player != state.player)  // Si le joueur a pu bouger
                    validActions.Add(action);
            }
            /*if (state.player.y < gridSize.y - 1) validActions.Add(Action.Up); // Peut aller vers le haut
            if (state.player.x < gridSize.x - 1) validActions.Add(Action.Right); // Peut aller vers la droite
            if (state.player.y > 0) validActions.Add(Action.Down); // Peut aller vers le bas
            if (state.player.x > 0) validActions.Add(Action.Left); // Peut aller vers la gauche*/
        }
        return validActions;
    }

    private bool IsEnd(State state)
    {
        if(state.game == Game.GridWorld)
        {
            return state.Equals(_end);
        }
        else if(state.game == Game.Sokoban) 
        {
            return state.crates.All(crate => currentMap.targets.Contains(crate)) && state.crates.Count == currentMap.targets.Count;
        }
        return false;
    }

    private void PrintPolicy()
    {
        var gridPolicy = "Grid Policy:\n";
        for (var y = currentMap.size.y - 1; y >= 0; y--)
        {
            var line = "";
            for (var x = 0; x < currentMap.size.x; x++)
            {
                var state = new State(x, y);
                var value = "X";
                var stateAction = _policy.GetAction(state);
                value = stateAction switch
                {
                    Action.Up => "^",
                    Action.Right => ">",
                    Action.Down => "v",
                    Action.Left => "<",
                    _ => value
                };
                line += value + "\t";
            }
            gridPolicy += line + "\n";
        }
        Debug.Log(gridPolicy);
    }

    private void PrintStateValues()
    {
        var gridRepresentation = "Grid State Values:\n";
        for (var y = gridSize.y - 1; y >= 0; y--)
        {
            var line = "";
            for (var x = 0; x < gridSize.x; x++)
            {
                var state = new State(x, y);
                var value = _stateValues.ContainsKey(state) ? _stateValues[state] : 0f;
                line += value.ToString("F2") + "\t";
            }
            gridRepresentation += line + "\n";
        }
        Debug.Log(gridRepresentation);
    }

    private void PrintCoordinates()
    {
        var gridCoordinates = "Grid Coordinates:\n";
        for (var y = gridSize.y - 1; y >= 0; y--)
        {
            var line = "";
            for (var x = 0; x < gridSize.x; x++)
            {
                var state = new State(x, y);
                line += state.X +","+ state.Y +"\t";
            }
            gridCoordinates += line + "\n";
        }
        Debug.Log(gridCoordinates);
    }

    private void printPossibleActions()
    {
        string grid = "Grid Possible Actions:\n";
        for (int y = gridSize.y - 1; y >= 0; y--)
        {
            string line = "";
            for (int x = 0; x < gridSize.x; x++)
            {
                State state = new State(x, y);
                int count = GetValidActions(state).Count;
                if (_obstacles.Contains(state))
                {
                    count = 0;
                }
                line += count + "\t";
            }
            grid += line + "\n";
        }
        Debug.Log(grid);
    }

    private void SetInteractivnessButtons(bool interactable)
    {
        foreach (var button in listButtons)
        {
            button.interactable = interactable;
        }
    }
}
