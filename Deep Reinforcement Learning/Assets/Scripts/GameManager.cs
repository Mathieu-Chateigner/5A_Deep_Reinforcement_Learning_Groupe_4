using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public Vector2Int gridSize;
    public State start;
    public State end;
    public List<State> obstacles;

    private Policy policy;
    private Dictionary<State, float> stateValues; // Used by PolicyEvaluation

    void Start()
    {
        InitializeObstacles();
        List<State> states = GenerateAllStates();

        start = new State(0, 0);
        end = new State(3, 3);
        policy = new Policy();
        policy.InitializePolicy(states, this);

        InitializeStateValues(states);

        printCoordinates();

        // Policy iteration pipeline
        /*PolicyEvaluation(states, 0.9f);
        PolicyImprovement(states);*/

        // Value iteration pipeline
        printPolicy();
        ValueIteration(states, 0.9f);
        printStateValues();
        printPolicy();
    }

    void InitializeObstacles()
    {
        obstacles = new List<State>
        {
            // On ajoute nos obstacles i�i
            //new State(1, 1),
            //new State(2, 2),
            //new State(4, 0)
        };
    }

    List<State> GenerateAllStates()
    {
        List<State> states = new List<State>();
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                State newState = new State(x, y);
                if (!obstacles.Contains(newState))
                {
                    states.Add(newState);
                }
            }
        }
        return states;
    }

    void InitializeStateValues(List<State> states)
    {
        stateValues = new Dictionary<State, float>();
        foreach (var state in states)
        {
            // Tous les �tats ont une valeur par d�faut de 0, sauf l'�tat final qui a une valeur de 1
            stateValues[state] = state.Equals(end) ? 1f : 0f;
        }
    }

    /*void PolicyEvaluation(List<State> states, float discountFactor)
    {
        float theta = 0.01f; // Seuil pour d�terminer quand arr�ter l'it�ration
        float delta = 0f;
        do
        {
            delta = 0f;
            foreach (State state in states)
            {
                if (IsEnd(state)) continue;

                float oldValue = stateValues[state];

                Action action = policy.GetAction(state);
                State nextState = GetNextState(state, action);
                float reward = GetImmediateReward(state, action);
                float newValue = reward + (discountFactor * stateValues[nextState]);
                

                stateValues[state] = newValue;

                delta = Mathf.Max(delta, Mathf.Abs(oldValue - newValue));
            }
        } while (delta > theta);
    }*/

    // Should be ok
    void ValueIteration(List<State> states, float discountFactor)
    {
        float theta = 0.001f; // Seuil pour d�terminer quand arr�ter l'it�ration
        float delta;
        do
        {
            delta = 0f;
            foreach (State state in states)
            {
                if (IsEnd(state)) continue;

                float oldValue = stateValues[state];

                // On prend seulement le max des actions possibles
                float maxValue = Mathf.NegativeInfinity;
                Action bestAction = Action.Up; // Default
                foreach (Action action in GetValidActions(state))
                {
                    State nextState = GetNextState(state, action);

                    float reward = IsEnd(nextState) ? 0f : GetImmediateReward(state, action); // To fix values between 0 and 1
                    float value = reward + (discountFactor * stateValues[nextState]);
                    if (value > maxValue)
                    {
                        maxValue = value;
                        bestAction = action;
                    }
                }
                
                stateValues[state] = maxValue;

                // Update policy
                policy.UpdatePolicy(state, bestAction);

                delta = Mathf.Max(delta, Mathf.Abs(oldValue - maxValue));
            }
        } while (delta > theta);
    }

    public void PolicyImprovement(List<State> states)
    {
        foreach (State state in states)
        {
            if (IsEnd(state)) continue; // Aucune action requise pour les �tats terminaux

            Action bestAction = Action.Up; // Valeur par d�faut, sera remplac�e
            float bestValue = float.NegativeInfinity;

            foreach (Action action in GetValidActions(state))
            {
                State nextState = GetNextState(state, action);
                float value = stateValues[nextState];

                if (value > bestValue)
                {
                    bestValue = value;
                    bestAction = action;
                }
            }
            //Debug.Log("["+ state.X +","+ state.Y +"] =>"+ bestValue);
            //Debug.Log("[" + state.X + "," + state.Y + "] =>" + GetValidActions(state).Count) ;
            // Mettre � jour la politique pour cet �tat avec la meilleure action trouv�e
            policy.UpdatePolicy(state, bestAction);
        }
    }

    public float GetImmediateReward(State currentState, Action action)
    {
        State nextState = GetNextState(currentState, action);
        if (nextState.Equals(end))
        {
            return 1.0f;
        }
        else if (obstacles.Contains(nextState))
        {
            return -1.0f;
        }
        else
        {
            return 0.0f;
        }
    }
    
    public State GetNextState(State state, Action action)
    {
        State nextState = new State(state.X, state.Y);

        switch (action)
        {
            case Action.Up:
                nextState.Y = nextState.Y + 1;
                break;
            case Action.Right:
                nextState.X = Mathf.Min(nextState.X + 1, gridSize.x - 1);
                break;
            case Action.Down:
                nextState.Y = nextState.Y - 1;
                break;
            case Action.Left:
                nextState.X = Mathf.Max(nextState.X - 1, 0);
                break;
        }

        if (obstacles.Contains(nextState))
        {
            return state;
        }

        return nextState;
    }

    public List<Action> GetValidActions(State state)
    {
        List<Action> validActions = new List<Action>();

        if (state.Y < gridSize.y - 1) validActions.Add(Action.Up); // Peut aller vers le haut
        if (state.X < gridSize.x - 1) validActions.Add(Action.Right); // Peut aller vers la droite
        if (state.Y > 0) validActions.Add(Action.Down); // Peut aller vers le bas
        if (state.X > 0) validActions.Add(Action.Left); // Peut aller vers la gauche

        // Check aussi les obstacles

        return validActions;
    }

    public bool IsEnd(State state)
    {
        return state.Equals(end);
    }

    private void printPolicy()
    {
        string gridPolicy = "Grid Policy:\n";
        for (int y = gridSize.y - 1; y >= 0; y--)
        {
            string line = "";
            for (int x = 0; x < gridSize.x; x++)
            {
                State state = new State(x, y);
                string value = "X";
                Action stateAction = policy.GetAction(state);
                switch (stateAction)
                {
                    case Action.Up:
                        value = "^";
                        break;
                    case Action.Right:
                        value = ">";
                        break;
                    case Action.Down:
                        value = "v";
                        break;
                    case Action.Left:
                        value = "<";
                        break;
                }
                line += value + "\t";
            }
            gridPolicy += line + "\n";
        }
        Debug.Log(gridPolicy);
    }

    private void printStateValues()
    {
        string gridRepresentation = "Grid State Values:\n";
        for (int y = gridSize.y - 1; y >= 0; y--)
        {
            string line = "";
            for (int x = 0; x < gridSize.x; x++)
            {
                State state = new State(x, y);
                float value = stateValues.ContainsKey(state) ? stateValues[state] : 0f;
                line += value.ToString("F2") + "\t";
            }
            gridRepresentation += line + "\n";
        }
        Debug.Log(gridRepresentation);
    }

    private void printCoordinates()
    {
        string gridCoordinates = "Grid Coordinates:\n";
        for (int y = gridSize.y - 1; y >= 0; y--)
        {
            string line = "";
            for (int x = 0; x < gridSize.x; x++)
            {
                State state = new State(x, y);
                line += state.X +","+ state.Y +"\t";
            }
            gridCoordinates += line + "\n";
        }
        Debug.Log(gridCoordinates);
    }
}